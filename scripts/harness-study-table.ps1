# Harnais de non-regression des tableaux de signe / variation (VSTO).
# Cf. ADR 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
#
# Execute le VRAI code C# compile (MathCursor.dll) dans un Word pilote en COM,
# par reflexion sur les types internes, et verifie la structure obtenue apres
# chaque action (OK / ECHEC). Le code Word des tableaux n'a pas de tests xUnit :
# ce harnais est sa non-regression. Word charge aussi l'add-in : la popup et
# le controleur reagissent aux changements de selection (cf. curseur sur le 0).
#
# Prerequis : build Debug de l'add-in (MSBuild adapter-vsto/src/MathCursor).
# Usage : powershell -File scripts/harness-study-table.ps1 [-Bin <dir>] [-PdfPath <out.pdf>]
# Code de sortie : 0 si tout passe, 1 sinon. Fichier ASCII (PowerShell 5.1).

param(
    [string]$Bin = (Join-Path $PSScriptRoot '..\adapter-vsto\src\MathCursor\bin\Debug'),
    [string]$PdfPath = ''
)
$ErrorActionPreference = 'Stop'

$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $Bin 'MathCursor.dll')))
$flags = [System.Reflection.BindingFlags]'Static,Public,NonPublic'
$tInserter = $asm.GetType('MathCursor.Host.StudyTableInserter')
$tActions = $asm.GetType('MathCursor.Host.StudyTableActions')
$tFormatter = $asm.GetType('MathCursor.Host.StudyTableFormatter')
$tKind = $asm.GetType('MathCursor.Host.StudyTableKind')
$tAction = $asm.GetType('MathCursor.Host.StudyTableAction')

$UP = [string][char]0x2197
$DOWN = [string][char]0x2198
$script:failures = 0
$script:checks = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $script:checks++
    if ($ok) { Write-Output "  OK     $name" }
    else { $script:failures++; Write-Output "  ECHEC  $name  $detail" }
}

function Base($o) { return $o.psobject.BaseObject }

function Read-Structure($table) { return $tFormatter.GetMethod('Read', $flags).Invoke($null, @((Base $table))) }

function Act($table, [int]$row, [int]$col, [string]$name) {
    $a = [Enum]::Parse($tAction, $name)
    $tActions.GetMethod('Run', $flags).Invoke($null, @((Base $table), $row, $col, $a)) | Out-Null
}

function Reapply-Column($table, [int]$col) {
    $tFormatter.GetMethod('ReapplyColumn', $flags).Invoke($null, @((Base $table), $col, 'harnais')) | Out-Null
}

function Set-Text($table, [int]$r, [int]$c, [string]$text) {
    $rng = $table.Cell($r, $c).Range
    $rng.MoveEnd(1, -1) | Out-Null
    $rng.Text = $text
}

function Text($table, [int]$r, [int]$c) {
    return ($table.Cell($r, $c).Range.Text -replace "[`r`a]", '').Trim()
}

function StyleOf($table, [int]$r, [int]$c) {
    return $table.Cell($r, $c).Range.Paragraphs.Item(1).Style.NameLocal
}

function Join-Kinds($list) { return ($list | ForEach-Object { $_.ToString() }) -join ',' }

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $null
try {
    $doc = $word.Documents.Add()
    $word.Selection.TypeText('Harnais tableaux')
    $word.Selection.TypeParagraph()

    Write-Output '1. Insertion par defaut (ruban)'
    $tInserter.GetMethod('Insert', $flags).Invoke($null, @((Base $word), [Enum]::Parse($tKind, 'Sign'))) | Out-Null
    $table = $doc.Tables.Item(1)
    $s = Read-Structure $table
    Check 'lignes x + signe' ((Join-Kinds $s.Rows) -eq 'X,Sign') (Join-Kinds $s.Rows)
    Check 'colonnes L F I F I F' ((Join-Kinds $s.Columns) -eq 'Label,Frontier,Interval,Frontier,Interval,Frontier') (Join-Kinds $s.Columns)
    Check 'valeur du milieu = 0' ((Text $table 1 4) -eq '0') (Text $table 1 4)
    $lab = $table.Cell(2, 1).Range
    Check 'etiquette f(x) en equation centree' ($lab.OMaths.Count -eq 1 -and $lab.OMaths.Item(1).Justification -eq 2) "omaths=$($lab.OMaths.Count)"
    Check 'separation sous la valeur du milieu' ($s.SeparatorLines.Contains(3)) "[$($s.SeparatorLines -join ',')]"
    Check 'le 0 du milieu est selectionne' ($word.Selection.Text -eq '0') "'$($word.Selection.Text)'"
    $hdr = $table.Cell(1, 4); $lab1 = $table.Cell(2, 1); $body = $table.Cell(2, 3)
    Check 'en-tete : fond bleu-gris, gras, bleu nuit' ($hdr.Shading.BackgroundPatternColor -eq 0xF8F2EE -and $hdr.Range.Font.Bold -eq -1 -and $hdr.Range.Font.Color -eq 0x5F3A1F) "fond=$('{0:X}' -f $hdr.Shading.BackgroundPatternColor) gras=$($hdr.Range.Font.Bold) couleur=$('{0:X}' -f $hdr.Range.Font.Color)"
    Check 'etiquette : meme style d en-tete' ($lab1.Shading.BackgroundPatternColor -eq 0xF8F2EE -and $lab1.Range.Font.Bold -eq -1)
    Check 'corps du tableau sans fond' ($body.Shading.BackgroundPatternColor -eq -16777216) "$('{0:X}' -f $body.Shading.BackgroundPatternColor)"
    $arrowFont = $doc.Styles.Item('MathCursor Fl' + [char]0x00E8 + 'che').Font
    Check 'fleches : Cambria Math 26 gris fonce' ($arrowFont.Name -eq 'Cambria Math' -and $arrowFont.Size -eq 26 -and $arrowFont.Color -eq 0x404040) "$($arrowFont.Name) $($arrowFont.Size) $('{0:X}' -f $arrowFont.Color)"

    Write-Output '2. Ajout de valeur a droite'
    Act $table 1 4 'AddValueRight'
    $s = Read-Structure $table
    Check '8 colonnes alternees' ((Join-Kinds $s.Columns) -eq 'Label,Frontier,Interval,Frontier,Interval,Frontier,Interval,Frontier') (Join-Kinds $s.Columns)
    $sum = 0; for ($c = 1; $c -le $table.Columns.Count; $c++) { $sum += $table.Cell(1, $c).PreferredWidth }
    Check 'largeurs = 100 %' ([math]::Abs($sum - 100) -lt 0.5) "$sum"

    Write-Output '3. Valeur tapee dans un intervalle de la ligne x'
    Set-Text $table 1 3 '-3'
    $before = $table.Columns.Count
    $word.UndoRecord.StartCustomRecord('harnais promotion')
    $tActions.GetMethod('PromoteIntervalToValue', $flags).Invoke($null, @((Base $table), 3)) | Out-Null
    $word.UndoRecord.EndCustomRecord()
    $s = Read-Structure $table
    Check 'deux colonnes de plus' ($table.Columns.Count -eq $before + 2) "$($table.Columns.Count)"
    Check 'la valeur est devenue une frontiere' ((Text $table 1 4) -eq '-3' -and (StyleOf $table 1 4) -eq ('MathCursor En-t' + [char]0x00EA + 'te valeur')) "'$(Text $table 1 4)' $(StyleOf $table 1 4)"
    Check 'signe recopie des deux cotes' ((Text $table 2 3) -eq '+' -and (Text $table 2 5) -eq '+' -and (Text $table 2 4) -eq '') "'$(Text $table 2 3)' '$(Text $table 2 4)' '$(Text $table 2 5)'"
    Check 'separation sous la nouvelle valeur' ($s.SeparatorLines.Contains(3)) "[$($s.SeparatorLines -join ',')]"

    Write-Output '4. Ctrl+Z annule la promotion en un coup'
    $doc.Undo(1) | Out-Null
    $s = Read-Structure $table
    Check 'colonnes revenues' ($table.Columns.Count -eq $before) "$($table.Columns.Count)"
    Check 'la case redevient un intervalle avec sa valeur' ((Text $table 1 3) -eq '-3' -and (StyleOf $table 1 3) -eq ('MathCursor En-t' + [char]0x00EA + 'te intervalle')) "'$(Text $table 1 3)' $(StyleOf $table 1 3)"
    Check 'structure relisible' ($s -ne $null -and (Join-Kinds $s.Columns) -eq 'Label,Frontier,Interval,Frontier,Interval,Frontier,Interval,Frontier') (Join-Kinds $s.Columns)
    Set-Text $table 1 3 ''

    Write-Output '5. Ligne de variation, fleches et extremum'
    Act $table 2 1 'AddVariationRow'
    $s = Read-Structure $table
    Check 'lignes x, signe, variation' ((Join-Kinds $s.Rows) -eq 'X,Sign,Variation') (Join-Kinds $s.Rows)
    Check 'ligne de variation haute' ($table.Rows.Item(3).Height -ge 53) "$($table.Rows.Item(3).Height)"
    Act $table 3 3 'ArrowUp'
    Act $table 3 5 'ArrowDown'
    Check 'fleches posees' ((Text $table 3 3) -eq $UP -and (Text $table 3 5) -eq $DOWN)
    Set-Text $table 3 4 '5'
    Reapply-Column $table 4
    Check 'maximum aligne en haut (entre fleche montante et descendante)' ($table.Cell(3, 4).VerticalAlignment -eq 0) "$($table.Cell(3, 4).VerticalAlignment)"

    Write-Output '6. Valeur interdite (aller-retour)'
    Act $table 1 6 'ToggleForbidden'
    $s = Read-Structure $table
    Check 'colonne 6 interdite' ($s.Forbidden.Contains(5)) "[$($s.Forbidden -join ',')]"
    Act $table 1 6 'ToggleForbidden'
    $s = Read-Structure $table
    Check 'interdiction retiree' (-not $s.Forbidden.Contains(5)) "[$($s.Forbidden -join ',')]"

    Write-Output '7. Separation posee / retiree par une valeur de x'
    Set-Text $table 1 6 '7'
    Reapply-Column $table 6
    $s = Read-Structure $table
    Check 'valeur ecrite : separation' ($s.SeparatorLines.Contains(5)) "[$($s.SeparatorLines -join ',')]"
    Set-Text $table 1 6 ''
    Reapply-Column $table 6
    $s = Read-Structure $table
    Check 'valeur effacee : plus de separation' (-not $s.SeparatorLines.Contains(5)) "[$($s.SeparatorLines -join ',')]"
    Check 'cases redevenues normales' ($table.Cell(2, 6).Range.Paragraphs.Count -eq 1 -and $table.Cell(2, 6).Range.ParagraphFormat.LeftIndent -eq 0)

    Write-Output '8. Police reduite / colonne elargie (colonne seule)'
    Set-Text $table 1 6 '-123456789,25'
    $w0 = $table.Cell(1, 6).PreferredWidth
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Reapply-Column $table 6
    $ms = $sw.ElapsedMilliseconds
    $fs = $table.Cell(1, 6).Range.Font.Size
    Check 'valeur longue : police reduite ou colonne elargie' ($fs -lt 11 -or $table.Cell(1, 6).PreferredWidth -gt $w0) "fs=$fs w=$($table.Cell(1, 6).PreferredWidth)"
    Write-Output "  (ReapplyColumn : $ms ms, hors processus)"

    Write-Output '9. Equation recentree'
    Set-Text $table 1 4 'a+1'
    $doc.OMaths.Add($doc.Range($table.Cell(1, 4).Range.Start, $table.Cell(1, 4).Range.End - 1)) | Out-Null
    $om = $table.Cell(1, 4).Range.OMaths.Item(1)
    $om.BuildUp(); $om = $table.Cell(1, 4).Range.OMaths.Item(1)
    $om.Justification = 3
    Reapply-Column $table 4
    Check 'equation de la ligne x centree' ($table.Cell(1, 4).Range.OMaths.Item(1).Justification -eq 2) "$($table.Cell(1, 4).Range.OMaths.Item(1).Justification)"

    Write-Output '10. 0 sur la ligne + curseur ramene sur le 0'
    Act $table 2 6 'Zero'
    $s = Read-Structure $table
    Check 'case 0 en trois paragraphes' ($table.Cell(2, 6).Range.Paragraphs.Count -eq 3) "$($table.Cell(2, 6).Range.Paragraphs.Count)"
    Check 'separation posee par le 0' ($s.SeparatorLines.Contains(5)) "[$($s.SeparatorLines -join ',')]"
    $seg = $table.Cell(2, 6).Range.Paragraphs.Item(1).Range
    $seg.Collapse(1); $seg.Select()
    $zero = $table.Cell(2, 6).Range.Paragraphs.Item(2).Range
    Check 'curseur pose sur un bout de ligne : ramene dans le paragraphe du 0' ($word.Selection.Start -ge $zero.Start -and $word.Selection.Start -lt $zero.End) "sel=$($word.Selection.Start) zero=$($zero.Start)-$($zero.End) (necessite l'add-in charge dans ce Word)"

    Write-Output '11. Ctrl+Z apres une sortie de case sans changement de mise en forme'
    # Case sans equation (une equation en gras rend des chiffres en gras mathematique).
    Set-Text $table 1 2 '1'
    Reapply-Column $table 2
    Set-Text $table 1 2 '2'
    $word.UndoRecord.StartCustomRecord('MathCursor : ajustement du tableau')
    Reapply-Column $table 2
    $word.UndoRecord.EndCustomRecord()
    $doc.Undo(1) | Out-Null
    Check 'le 1er Ctrl+Z annule la saisie (pas d''entree vide)' ((Text $table 1 2) -eq '1') "'$(Text $table 1 2)'"

    Write-Output '12. Valeur de x videe = valeur supprimee (intervalles fusionnes)'
    $s = Read-Structure $table
    $cols = $table.Columns.Count
    Set-Text $table 1 4 ''
    $removed = $tActions.GetMethod('RemoveErasedValue', $flags).Invoke($null, @((Base $table), 4))
    Check 'valeur interieure videe : supprimee' ($removed -and $table.Columns.Count -eq $cols - 2) "removed=$removed cols=$($table.Columns.Count)"
    Check 'intervalle de gauche garde son signe' ((Text $table 2 3) -eq '+') "'$(Text $table 2 3)'"
    $first = $tActions.GetMethod('RemoveErasedValue', $flags).Invoke($null, @((Base $table), 2))
    Check 'borne videe : jamais supprimee' (-not $first) "removed=$first"

    Write-Output '13. Suppressions'
    Act $table 3 1 'DeleteRow'
    $s = Read-Structure $table
    Check 'ligne supprimee' ((Join-Kinds $s.Rows) -eq 'X,Sign') (Join-Kinds $s.Rows)
    $cols = $table.Columns.Count
    Act $table 1 4 'DeleteValue'
    Check 'valeur supprimee (2 colonnes de moins)' ($table.Columns.Count -eq $cols - 2) "$($table.Columns.Count)"

    Write-Output '14. Insertion depuis une derniere ligne NON vide (bug preexistant de ResolveInsertRange)'
    $word.Selection.EndKey(6) | Out-Null
    $word.Selection.TypeText('ligne finale')
    $n = $doc.Tables.Count
    $tInserter.GetMethod('Insert', $flags).Invoke($null, @((Base $word), [Enum]::Parse($tKind, 'Sign'))) | Out-Null
    Check 'tableau insere sous la derniere ligne' ($doc.Tables.Count -eq $n + 1) "$($doc.Tables.Count)"

    if ($PdfPath) { $doc.ExportAsFixedFormat($PdfPath, 17); Write-Output "PDF : $PdfPath" }
}
catch {
    $script:failures++
    Write-Output ('ERREUR : ' + $_.Exception.ToString())
}
finally {
    if ($doc -ne $null) { $doc.Close([ref]0) | Out-Null }
    $word.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
}

Write-Output ''
Write-Output ("{0} verification(s), {1} echec(s)" -f $script:checks, $script:failures)
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
