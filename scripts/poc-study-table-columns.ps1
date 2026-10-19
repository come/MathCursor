# POC (regle dure Word API) - tableau de signe / variation flexible.
# Cf. ADR 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
#
# Sans ruban ni pipeline : on construit dans Word le gabarit actuel
# (StudyTableInserter : etiquettes + colonnes alternees frontiere/intervalle),
# puis on repond a 3 questions :
#   Q1. Inserer une PAIRE de colonnes au milieu (Columns.Add) : styles,
#       bordures, largeurs, hauteur de la ligne de variation ? Idem lignes.
#   Q2. Rendu de la valeur interdite : bordures de cellule vs caractere U+2016.
#   Q3. Detection du debordement d'une EQUATION dans une cellule etroite +
#       reduction de sa taille (plancher 9 pt).
# Le rendu est exporte en PDF (argument 1) pour verification visuelle.
#
# Usage : powershell -File scripts/poc-study-table-columns.ps1 <out.pdf>

param([string]$PdfPath = "$env:TEMP\poc-study-table.pdf")

$ErrorActionPreference = 'Stop'

$ARROW_UP = [string][char]0x2197
$ARROW_DOWN = [string][char]0x2198
$MINUS = [string][char]0x2212
$INF = [string][char]0x221E
$PRIME = [string][char]0x2032
$DBAR = [string][char]0x2016
$SQRT = [string][char]0x221A

# Constantes Word
$wdBorderTop = -1; $wdBorderLeft = -2; $wdBorderBottom = -3; $wdBorderRight = -4
$wdBorderHorizontal = -5; $wdBorderVertical = -6
$wdLineNone = 0; $wdLineSingle = 1; $wdLineDouble = 7
$wdPct = 2
$vTop = 0; $vCenter = 1; $vBottom = 3
$wdFirstCharacterLineNumber = 10
$wdVerticalPositionRelativeToPage = 6

function Get-OrAddStyle($doc, [string]$name, [float]$size) {
    try { return $doc.Styles.Item($name) } catch { }
    $s = $doc.Styles.Add($name, 1)   # 1 = wdStyleTypeParagraph
    $s.Font.Size = $size
    $s.ParagraphFormat.Alignment = 1  # centre
    $s.ParagraphFormat.SpaceBefore = 2
    $s.ParagraphFormat.SpaceAfter = 2
    return $s
}

function Set-Line($border, $style) {
    $border.LineStyle = $style
    if ($style -ne $wdLineNone) { $border.LineWidth = 6 }  # 0.75 pt
}

function Style-Name($range) {
    try { return $range.Paragraphs.Item(1).Style.NameLocal } catch { return "?" }
}

function Dump-Table($table, [string]$title) {
    Write-Output "--- $title : rows=$($table.Rows.Count) cols=$($table.Columns.Count)"
    for ($r = 1; $r -le $table.Rows.Count; $r++) {
        $row = $table.Rows.Item($r)
        $line = "  r$r h=$([math]::Round($row.Height,1)) rule=$($row.HeightRule) |"
        for ($c = 1; $c -le $table.Columns.Count; $c++) {
            $cell = $table.Cell($r, $c)
            $txt = $cell.Range.Text.TrimEnd([char]13, [char]7)
            $l = $cell.Borders.Item($wdBorderLeft).LineStyle
            $rb = $cell.Borders.Item($wdBorderRight).LineStyle
            $w = [math]::Round($cell.PreferredWidth, 1)
            $line += " [$c '$txt' $(Style-Name $cell.Range) w=$w/$($cell.PreferredWidthType) b=$l,$rb v=$($cell.VerticalAlignment)]"
        }
        Write-Output $line
    }
}

# Roles : label | frontiere (valeur x / 0 / extremum) | intervalle | fleche
$roleStyle = @{}

function Apply-Role($doc, $cell, [string]$role, [string]$rowKind) {
    $cell.Range.Style = $roleStyle[$role]
    if ($role -eq 'label') { $cell.Range.Font.Italic = 1 }
    if ($rowKind -eq 'variation' -and $role -eq 'frontier') { $cell.VerticalAlignment = $vBottom }
    else { $cell.VerticalAlignment = $vCenter }
}

function Role-Of([int]$col) {
    if ($col -eq 1) { return 'label' }
    if (($col % 2) -eq 0) { return 'frontier' }  # 1-based : 2,4,6 = frontieres
    return 'interval'
}

function Apply-Widths($table) {
    $n = $table.Columns.Count
    $frontiers = 0; $intervals = 0
    for ($c = 2; $c -le $n; $c++) { if ((Role-Of $c) -eq 'frontier') { $frontiers++ } else { $intervals++ } }
    $fw = 10.0
    $iw = (84.0 - $fw * $frontiers) / [math]::Max(1, $intervals)
    $table.PreferredWidthType = $wdPct
    $table.PreferredWidth = 100
    foreach ($cell in $table.Range.Cells) {
        $cell.PreferredWidthType = $wdPct
        $role = Role-Of $cell.ColumnIndex
        if ($role -eq 'label') { $cell.PreferredWidth = 16 }
        elseif ($role -eq 'frontier') { $cell.PreferredWidth = $fw }
        else { $cell.PreferredWidth = $iw }
    }
}

function Apply-Borders($table) {
    foreach ($b in @($wdBorderTop, $wdBorderBottom, $wdBorderLeft, $wdBorderRight, $wdBorderHorizontal)) {
        Set-Line $table.Borders.Item($b) $wdLineSingle
    }
    $table.Borders.Item($wdBorderVertical).LineStyle = $wdLineNone
    for ($r = 1; $r -le $table.Rows.Count; $r++) {
        Set-Line $table.Cell($r, 1).Borders.Item($wdBorderRight) $wdLineSingle
    }
}

function Line-Span($doc, $cell) {
    # Lignes occupees par le contenu de la cellule (hors marque de fin).
    $cr = $cell.Range
    $start = $cr.Start
    $end = [math]::Max($start, $cr.End - 1)
    $first = $doc.Range($start, $start).Information($wdFirstCharacterLineNumber)
    $last = $doc.Range($end, $end).Information($wdFirstCharacterLineNumber)
    $y0 = $doc.Range($start, $start).Information($wdVerticalPositionRelativeToPage)
    $y1 = $doc.Range($end, $end).Information($wdVerticalPositionRelativeToPage)
    return @{ first = $first; last = $last; y0 = $y0; y1 = $y1; wraps = (($last -ne $first) -or ([math]::Abs($y1 - $y0) -gt 2)) }
}

function Put-Math($doc, $cell, [string]$linear) {
    $cr = $cell.Range
    $r = $doc.Range($cr.Start, $cr.End - 1)
    $r.Text = $linear
    $r = $doc.Range($cell.Range.Start, $cell.Range.End - 1)
    $null = $doc.OMaths.Add($r)
    $om = $cell.Range.OMaths.Item(1)
    $om.BuildUp()
    return $cell.Range.OMaths.Item(1)
}

function Fit-Cell($doc, $cell, [float]$startSize, [float]$floor) {
    [single]$size = $startSize
    $steps = @()
    while ($true) {
        try { $cell.Range.Font.Size = [single]$size }
        catch {
            # Repli : taille posee sur chaque OMath de la cellule.
            $steps += "$size pt: Range.Font.Size KO ($($_.Exception.Message)) -> via OMath.Range"
            foreach ($om in $cell.Range.OMaths) { $om.Range.Font.Size = [single]$size }
        }
        $span = Line-Span $doc $cell
        $steps += "$size pt: lines $($span.first)-$($span.last) y $([math]::Round($span.y0,1))-$([math]::Round($span.y1,1)) wraps=$($span.wraps)"
        if (-not $span.wraps -or $size -le $floor) { break }
        $size = [single]($size - 1)
    }
    return @{ size = $size; steps = $steps; fits = (-not $span.wraps) }
}

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $null
try {
    $doc = $word.Documents.Add()
    $roleStyle['label'] = (Get-OrAddStyle $doc 'MC Etiquette' 11).NameLocal
    $roleStyle['frontier'] = (Get-OrAddStyle $doc 'MC Frontiere' 11).NameLocal
    $roleStyle['interval'] = (Get-OrAddStyle $doc 'MC Intervalle' 11).NameLocal
    $roleStyle['arrow'] = (Get-OrAddStyle $doc 'MC Fleche' 20).NameLocal

    $word.Selection.TypeText("POC tableau de variation")
    $word.Selection.TypeParagraph()
    $insert = $doc.Range($word.Selection.Start, $word.Selection.Start)
    $table = $doc.Tables.Add($insert, 3, 6)
    $table.AllowAutoFit = $false

    $kinds = @('x', 'sign', 'variation')
    $labels = @('x', ('f' + $PRIME + '(x)'), 'f(x)')
    for ($r = 1; $r -le 3; $r++) {
        for ($c = 1; $c -le 6; $c++) {
            $role = Role-Of $c
            if ($kinds[$r - 1] -eq 'variation' -and $role -eq 'interval') { $role = 'arrow' }
            Apply-Role $doc ($table.Cell($r, $c)) $role $kinds[$r - 1]
        }
        $table.Cell($r, 1).Range.Text = $labels[$r - 1]
    }
    $table.Cell(1, 2).Range.Text = $MINUS + $INF
    $table.Cell(1, 6).Range.Text = '+' + $INF
    $table.Cell(2, 3).Range.Text = '+'; $table.Cell(2, 4).Range.Text = '0'; $table.Cell(2, 5).Range.Text = $MINUS
    $table.Cell(3, 3).Range.Text = $ARROW_UP; $table.Cell(3, 5).Range.Text = $ARROW_DOWN
    $table.Cell(3, 4).VerticalAlignment = $vTop
    $table.Rows.Item(3).HeightRule = 1  # wdRowHeightAtLeast
    $table.Rows.Item(3).Height = 54
    Apply-Borders $table
    Apply-Widths $table
    Dump-Table $table 'gabarit initial'

    # ---------------------------------------------------------------- Q1
    # Ajouter une valeur de x A DROITE de la frontiere col 4 = paire
    # (intervalle, frontiere) inseree avant l'ancienne col 5.
    $before = $table.Columns.Item(5)
    $null = $table.Columns.Add($before)
    $null = $table.Columns.Add($table.Columns.Item(6))
    Dump-Table $table 'Q1a apres Columns.Add x2 (brut, avant reapplication des roles)'

    # Reapplication des roles sur les 2 nouvelles colonnes (5 = intervalle, 6 = frontiere).
    for ($r = 1; $r -le $table.Rows.Count; $r++) {
        foreach ($c in @(5, 6)) {
            $role = Role-Of $c
            if ($kinds[$r - 1] -eq 'variation' -and $role -eq 'interval') { $role = 'arrow' }
            Apply-Role $doc ($table.Cell($r, $c)) $role $kinds[$r - 1]
        }
    }
    $table.Cell(2, 5).Range.Text = $MINUS
    $table.Cell(2, 6).Range.Text = '0'
    $table.Cell(3, 5).Range.Text = $ARROW_DOWN
    $table.Cell(3, 7).Range.Text = $ARROW_UP
    $table.Cell(2, 7).Range.Text = '+'
    Apply-Borders $table
    Apply-Widths $table
    Dump-Table $table 'Q1b apres reapplication roles + bordures + largeurs'

    # Ajout de lignes : une ligne de signe sous la ligne 2 (Rows.Add avant la 3),
    # une ligne de variation en fin (Rows.Add sans argument).
    $null = $table.Rows.Add($table.Rows.Item(3))
    $null = $table.Rows.Add()
    Dump-Table $table 'Q1c apres Rows.Add (signe en 3, variation en fin) - brut'

    # ---------------------------------------------------------------- Q2
    # Valeur interdite sur la frontiere col 6 (lignes 2..n) : trois rendus,
    # chacun sur une ligne differente pour comparer dans le PDF.
    $table.Cell(1, 6).Range.Text = '2 (A bords)'
    $table.Cell(2, 6).Range.Text = ''
    Set-Line $table.Cell(2, 6).Borders.Item($wdBorderLeft) $wdLineSingle
    Set-Line $table.Cell(2, 6).Borders.Item($wdBorderRight) $wdLineSingle
    $table.Cell(3, 6).Range.Text = $DBAR   # B : caractere U+2016
    Set-Line $table.Cell(4, 6).Borders.Item($wdBorderLeft) $wdLineDouble   # C : bord double gauche
    Set-Line $table.Cell(5, 6).Borders.Item($wdBorderLeft) $wdLineSingle
    Set-Line $table.Cell(5, 6).Borders.Item($wdBorderRight) $wdLineSingle
    Dump-Table $table 'Q2 valeurs interdites (r2 bords G+D, r3 U+2016, r4 bord double G, r5 bords G+D ligne haute)'

    # ---------------------------------------------------------------- Q3
    # Equation dans une frontiere etroite (col 4, ligne x) puis valeur longue.
    $short = Put-Math $doc ($table.Cell(1, 4)) ($SQRT + '3/2')
    Write-Output ("Q3 omath court: type={0} size={1}" -f $short.Type, $short.Range.Font.Size)
    $fit1 = Fit-Cell $doc ($table.Cell(1, 4)) 11 9
    Write-Output "Q3 sqrt3/2 :"; $fit1.steps | ForEach-Object { Write-Output "   $_" }

    $long = Put-Math $doc ($table.Cell(3, 4)) ('f(' + $MINUS + '1/2)=3/4+' + $SQRT + '5')
    $fit2 = Fit-Cell $doc ($table.Cell(3, 4)) 11 9
    Write-Output "Q3 f(-1/2)=3/4+sqrt5 :"; $fit2.steps | ForEach-Object { Write-Output "   $_" }
    Write-Output ("Q3 lecture taille apres fit : {0}" -f $table.Cell(3, 4).Range.OMaths.Item(1).Range.Font.Size)

    # Texte simple long (non-equation) pour comparer la detection.
    $table.Cell(1, 2).Range.Text = ($MINUS + '123456789')
    $fit3 = Fit-Cell $doc ($table.Cell(1, 2)) 11 9
    Write-Output "Q3 texte -123456789 :"; $fit3.steps | ForEach-Object { Write-Output "   $_" }

    Dump-Table $table 'final'

    # ---------------------------------------------------------------- Q2b
    # Valeur interdite = bordures de PARAGRAPHE G+D dans la case frontiere,
    # retraits pour resserrer la double barre au centre, interligne exact
    # pour couvrir la hauteur de la ligne. Tableau separe : label |
    # intervalle | frontiere interdite | intervalle.
    $word.Selection.EndKey(6) | Out-Null
    $word.Selection.TypeParagraph()
    $word.Selection.TypeText("Q2b valeur interdite par bordure de paragraphe")
    $word.Selection.TypeParagraph()
    $t2 = $doc.Tables.Add($doc.Range($word.Selection.Start, $word.Selection.Start), 3, 4)
    $t2.AllowAutoFit = $false
    $t2.PreferredWidthType = $wdPct; $t2.PreferredWidth = 100
    foreach ($cell in $t2.Range.Cells) {
        $cell.PreferredWidthType = $wdPct
        $cell.PreferredWidth = [single](@(16, 37, 10, 37)[$cell.ColumnIndex - 1])
        $cell.VerticalAlignment = $vCenter
        $cell.Range.ParagraphFormat.Alignment = 1
    }
    Apply-Borders $t2
    $t2.Rows.Item(3).HeightRule = 1; $t2.Rows.Item(3).Height = 54
    $t2.Cell(1, 1).Range.Text = 'x'; $t2.Cell(1, 3).Range.Text = '2'
    $t2.Cell(2, 1).Range.Text = 'g(x)'; $t2.Cell(2, 2).Range.Text = '+'; $t2.Cell(2, 4).Range.Text = $MINUS
    $t2.Cell(3, 1).Range.Text = 'g(x)'; $t2.Cell(3, 2).Range.Text = $ARROW_UP; $t2.Cell(3, 4).Range.Text = $ARROW_DOWN
    $cw = $t2.Cell(2, 3).Width
    Write-Output ("Q2b largeur case frontiere = {0} pt (padding G/D {1}/{2})" -f $cw, $t2.LeftPadding, $t2.RightPadding)
    foreach ($r in @(2, 3)) {
        $cell = $t2.Cell($r, 3)
        $cell.TopPadding = 0; $cell.BottomPadding = 0
        $cell.VerticalAlignment = $vTop
        $pf = $cell.Range.ParagraphFormat
        $inner = $cw - $t2.LeftPadding - $t2.RightPadding
        $indent = [single](($inner - 2.5) / 2)
        $pf.LeftIndent = $indent; $pf.RightIndent = $indent
        $pf.SpaceBefore = 0; $pf.SpaceAfter = 0
        $pf.LineSpacingRule = 4   # wdLineSpaceExactly
        $pf.LineSpacing = $(if ($r -eq 3) { [single]54 } else { [single]$t2.Rows.Item(2).Height })
        foreach ($b in @($wdBorderLeft, $wdBorderRight)) {
            $pb = $pf.Borders.Item($b)
            $pb.LineStyle = $wdLineSingle; $pb.LineWidth = 4   # 0.5 pt
        }
        $pf.Borders.DistanceFromLeft = 0; $pf.Borders.DistanceFromRight = 0
        Write-Output ("Q2b r{0}: indent={1} lineSpacing={2} rowH={3}" -f $r, $indent, $pf.LineSpacing, $t2.Rows.Item($r).Height)
    }
    $doc.ExportAsFixedFormat($PdfPath, 17)   # 17 = wdExportFormatPDF
    Write-Output "PDF: $PdfPath"
}
finally {
    if ($doc -ne $null) { $doc.Close([ref]0) | Out-Null }
    $word.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
}
