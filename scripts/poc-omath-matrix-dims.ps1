# POC (règles dures word-api) : que fait réellement Functions.Add(Mat, rows, cols)
# pour une matrice > 2×2, et comment agrandir Rows/Cols ?
# Mesure 2026-06-12 — bug « Le nombre doit être compris entre 1 et 2 » au
# commit d'une 2×3 via le walker (OmmlToOMathBuilder case "m").
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName Microsoft.Office.Interop.Word

$w = New-Object -ComObject Word.Application
$w.Visible = $false
$doc = $w.Documents.Add()

function Probe([int]$rows, [int]$cols) {
    Write-Host "=== Mat ${rows}x${cols} ==="
    $r = $doc.Range(0, 0)
    $r.Text = "xx"
    $omr = $doc.OMaths.Add($doc.Range(0, 2))
    $om = $omr.OMaths(1)
    $at = $doc.Range($om.Range.Start + 1, $om.Range.Start + 1)
    try {
        $fn = $om.Functions.Add($at, [Microsoft.Office.Interop.Word.WdOMathFunctionType]::wdOMathFunctionMat, $rows, $cols)
        $mat = $fn.Mat
        Write-Host "  apres Add(rows=$rows, cols=$cols) : Rows.Count=$($mat.Rows.Count) Cols.Count=$($mat.Cols.Count)"
        while ($mat.Rows.Count -lt $rows) {
            try { $mat.Rows.Add() | Out-Null; Write-Host "  Rows.Add() ok -> $($mat.Rows.Count)" }
            catch { Write-Host "  Rows.Add() THROW: $($_.Exception.Message)"; break }
        }
        while ($mat.Cols.Count -lt $cols) {
            try { $mat.Cols.Add() | Out-Null; Write-Host "  Cols.Add() ok -> $($mat.Cols.Count)" }
            catch { Write-Host "  Cols.Add() THROW: $($_.Exception.Message)"; break }
        }
        try {
            $cell = $mat.Cell($rows, $cols)
            Write-Host "  Cell($rows,$cols) OK"
        } catch { Write-Host "  Cell($rows,$cols) THROW: $($_.Exception.Message)" }
        # Variante : Add SANS dims puis grow
    } catch { Write-Host "  Functions.Add THROW: $($_.Exception.Message)" }
    $doc.Range(0, $doc.Content.End - 1).Delete() | Out-Null
}

Probe 2 2
Probe 2 3
Probe 3 3

# Variante B : Add avec dims par défaut (sans args) puis growth
Write-Host "=== Variante B : Add() sans dims puis Rows.Add/Cols.Add ==="
$r = $doc.Range(0, 0); $r.Text = "xx"
$omr = $doc.OMaths.Add($doc.Range(0, 2)); $om = $omr.OMaths(1)
$at = $doc.Range($om.Range.Start + 1, $om.Range.Start + 1)
$fn = $om.Functions.Add($at, [Microsoft.Office.Interop.Word.WdOMathFunctionType]::wdOMathFunctionMat)
$mat = $fn.Mat
Write-Host "  defaut : Rows=$($mat.Rows.Count) Cols=$($mat.Cols.Count)"
try { $mat.Cols.Add($mat.Cols.Item(1)) | Out-Null; Write-Host "  Cols.Add(BeforeCol=col1) ok -> $($mat.Cols.Count)" }
catch { Write-Host "  Cols.Add(BeforeCol=col1) THROW: $($_.Exception.Message)" }
try { $mat.Rows.Add($mat.Rows.Item(1)) | Out-Null; Write-Host "  Rows.Add(BeforeRow=row1) ok -> $($mat.Rows.Count)" }
catch { Write-Host "  Rows.Add(BeforeRow=row1) THROW: $($_.Exception.Message)" }

$doc.Close([Microsoft.Office.Interop.Word.WdSaveOptions]::wdDoNotSaveChanges)
$w.Quit()
Write-Host "POC terminé."
