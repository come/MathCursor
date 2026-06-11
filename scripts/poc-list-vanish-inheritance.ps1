# POC (règle dure Word API) — bug « caret coincé sur la 2e puce » (listbug.docx,
# 2026-06-10) : dans une liste, l'anchor ZWSP vanish est le seul run plain du ¶ ;
# après Entrée, la frappe du ¶ suivant hérite de <w:vanish/> → texte invisible.
#
# Mime EXACTEMENT le chemin liste d'OMathInserter (v2 du POC : la v1 passait
# par OMaths.Add+BuildUp et ne reproduisait pas — l'historique de frappe
# visible masquait l'héritage ; la prod passe par InsertXML, le SEUL run plain
# du ¶ est le ZWSP) :
#   ZWSP TypeText (pas hidden : liste) → placeholder □ → WordOpenXML →
#   remplace le run □ par m:oMath → InsertXML → CC.Add sur ZWSP →
#   cc.Range.Font.Hidden=-1 → escape SetRange(om.End)+MoveRight →
#   Entrée + "ABC" → le run ABC porte-t-il vanish ?
#
# Phase A : sans fix (bug attendu : vanish=True)
# Phase B : + sel.Font.Hidden = 0 après l'escape (attendu : vanish=False)

$ErrorActionPreference = 'Stop'
$word = New-Object -ComObject Word.Application
$word.Visible = $false

$OMML = '<m:oMath><m:r><m:t>F(x)=1/x</m:t></m:r></m:oMath>'

function Test-Sequence($word, [bool]$applyFix) {
    $doc = $word.Documents.Add()
    try {
        $sel = $word.Selection
        $sel.Range.ListFormat.ApplyBulletDefault()

        # 4. ZWSP plain, PAS hidden (chemin liste)
        $zwspStart = $sel.Start
        $sel.TypeText([char]0x200B)
        $zwspEnd = $sel.Start

        # 5. placeholder □ + InsertXML (réplique BuildOMathViaOmml)
        $phStart = $sel.Start
        $sel.TypeText([char]0x25A1)
        $phEnd = $sel.Start
        $phRange = $doc.Range($phStart, $phEnd)
        $xml = $phRange.WordOpenXML
        # remplace le run contenant □ par l'OMath (regex jetable, POC only).
        # Pattern construit par code-point (pas de littéral non-ASCII : le
        # .ps1 sans BOM est lu en ANSI par PS 5.1 et mangle le caractère).
        $ph = [System.Text.RegularExpressions.Regex]::Escape([string][char]0x25A1)
        $pattern = '<w:r(?:\s[^>]*)?>(?:(?!</w:r>).)*?' + $ph + '(?:(?!</w:r>).)*?</w:r>'
        $newXml = [System.Text.RegularExpressions.Regex]::Replace($xml, $pattern, $OMML, 'Singleline')
        if ($newXml -eq $xml) { throw "run placeholder introuvable dans WordOpenXML" }
        $phRange.InsertXML($newXml)

        # re-probe OMath + Inline forcé (chemin liste)
        $om = $doc.Range($phStart, [Math]::Min($doc.Content.End, $phStart + 200)).OMaths.Item(1)
        try { $om.Type = 1 } catch {}   # 1 = wdOMathInline

        # 7. CC anchor sur le ZWSP + Font.Hidden APRÈS cc.Add
        $anchor = $doc.Range($zwspStart, $zwspEnd)
        $cc = $doc.ContentControls.Add(1, $anchor)
        $cc.Title = "MathCursor"
        $cc.Range.Font.Hidden = -1

        # escape : caret après l'OMath + MoveRight
        $om = $doc.Range($cc.Range.End, [Math]::Min($doc.Content.End, $cc.Range.End + 200)).OMaths.Item(1)
        $sel.SetRange($om.Range.End, $om.Range.End)
        $sel.MoveRight(1, 1, 0) | Out-Null

        if ($applyFix) { $sel.Font.Hidden = 0 }

        # repro user : Entrée → puce 2 → frappe
        $sel.TypeText("`r")
        $typedStart = $sel.Start
        $sel.TypeText("ABC")
        $typedEnd = $sel.Start

        $typedRange = $doc.Range($typedStart, $typedEnd)
        $hidden = $typedRange.Font.Hidden
        $hasVanish = $typedRange.WordOpenXML -match '<w:vanish\s*/>'
        return ("hidden={0} vanishInXml={1}" -f $hidden, $hasVanish)
    }
    finally {
        $doc.Close([ref]0) | Out-Null
    }
}

try {
    $repro = Test-Sequence $word $false
    Write-Output ("PHASE A (sans fix, bug attendu)  : {0}" -f $repro)
    $fixed = Test-Sequence $word $true
    Write-Output ("PHASE B (avec sel.Font.Hidden=0) : {0}" -f $fixed)
}
finally {
    $word.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
}
