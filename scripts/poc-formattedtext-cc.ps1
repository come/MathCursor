# POC (règle dure Word API) — la copie FormattedText d'une sélection contenant
# un OMath + son anchor CC (pattern MCMeta, CC À CÔTÉ de l'OMath) préserve-t-elle
# le CC et son Tag une fois posée dans une cellule de tableau ?
#
# Reproduit l'ordre d'opérations validé d'OMathInserter : texte plain → OMath
# BuildUp → CC anchor EN DERNIER (Title=MathCursor, Tag=JSON), puis le move
# de ColumnLayoutInserter : table en dessous + cell.Range.FormattedText =
# sel.Range.FormattedText + delete source.
#
# Verdict attendu pour acter le port SANS logique bookmarks :
#   ccCount=1, tagOk=True, omathCount=1 dans la cellule.

$ErrorActionPreference = 'Stop'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
try {
    $doc = $word.Documents.Add()

    # ¶1 : prose + OMath + anchor CC à côté (ordre validé)
    $sel = $word.Selection
    $sel.TypeText("avant ")
    $eqStart = $sel.Start
    $sel.TypeText("f(x)=1/x")
    $eqRange = $doc.Range($eqStart, $sel.End)
    $om = $doc.OMaths.Add($eqRange)
    $doc.OMaths.Item(1).BuildUp()

    # repère l'OMath réel après BuildUp puis pose l'anchor CC juste APRÈS
    $om1 = $doc.OMaths.Item(1)
    $anchorPos = $om1.Range.End
    $anchor = $doc.Range($anchorPos, $anchorPos)
    $cc = $doc.ContentControls.Add(1, $anchor)  # 1 = wdContentControlRichText
    $cc.Title = "MathCursor"
    $cc.Tag = '{"v":1,"handle_id":"poc123","steno":"f(x)=1/x","latex":"f(x)=\\frac{1}{x}"}'

    $sel.EndKey(6) | Out-Null   # 6 = wdStory
    $sel.TypeText(" apres")

    # sélectionne tout le ¶1 (prose + OMath + CC)
    $para1 = $doc.Paragraphs.Item(1).Range
    $selRange = $doc.Range($para1.Start, $para1.End - 1)  # sans le ¶ final
    $selRange.Select()

    Write-Output ("source: omaths={0} ccs={1}" -f $doc.OMaths.Count, $doc.ContentControls.Count)

    # table 1x2 en dessous + FormattedText copy en col 1 + delete source.
    # ¶1 = dernier ¶ du doc → ajouter d'abord un ¶ d'accueil (même garde que
    # ColumnLayoutInserter), PUIS recalculer la position.
    $doc.Paragraphs.Item(1).Range.InsertParagraphAfter()
    $insertPos = $doc.Paragraphs.Item(1).Range.End
    $table = $doc.Tables.Add($doc.Range($insertPos, $insertPos), 1, 2)
    $cell = $table.Cell(1, 1).Range
    $cell.FormattedText = $word.Selection.Range.FormattedText
    $word.Selection.Range.Delete() | Out-Null

    # verdict : CC + Tag + OMath dans la cellule ?
    $cellNow = $table.Cell(1, 1).Range
    $ccCount = $cellNow.ContentControls.Count
    $omCount = $cellNow.OMaths.Count
    $tagOk = $false
    if ($ccCount -ge 1) {
        $tag = $cellNow.ContentControls.Item(1).Tag
        $tagOk = ($tag -like '*poc123*')
        Write-Output ("cell cc tag: {0}" -f $tag)
    }
    Write-Output ("VERDICT: ccCount={0} omathCount={1} tagOk={2}" -f $ccCount, $omCount, $tagOk)
    if ($ccCount -eq 1 -and $omCount -eq 1 -and $tagOk) { Write-Output "POC: PASS" } else { Write-Output "POC: FAIL" }
}
finally {
    $doc.Close([ref]0) | Out-Null   # 0 = wdDoNotSaveChanges
    $word.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
}
