#requires -Version 5.1

<#
.SYNOPSIS
    Archive legacy MathCursor engine files to complete switch to RewriteEngine (Phase D-6).
.DESCRIPTION
    Copies all .cs files from Collision, Emit, Parsing, Resolution directories
    to archive/legacy-engine-2026-05-28/ and replaces source files with STUBs.
#>

$ErrorActionPreference = "Stop"

# =============================================================================
# CONFIGURATION
# =============================================================================
$ProjectRoot = "D:\Software\DocMath"
$ArchiveRoot = "$ProjectRoot\archive\legacy-engine-2026-05-28"
$SourceRoot = "$ProjectRoot\core-csharp\src\MathCursor.Engine"

# Directories to archive (relative to $SourceRoot)
$LegacyDirs = @("Collision", "Emit", "Parsing", "Resolution")

# =============================================================================
# STUB TEMPLATE
# =============================================================================
function Get-StubContent {
    param($RelativePath, $Namespace)
    return @"
// =============================================================================
// ARCHIVED 2026-05-28 - Phase D-6: Switch to RewriteEngine
// =============================================================================
// This file was archived to: archive\legacy-engine-2026-05-28\$RelativePath
// Do NOT use - replaced by RewriteEngine
//
// To restore: git checkout 4c70adc -- core-csharp\src\MathCursor.Engine\$RelativePath
// =============================================================================

#pragma warning disable CS0162
#pragma warning disable CS8019

namespace MathCursor.Engine.$Namespace
{
    [System.Obsolete("ARCHIVED 2026-05-28: Use RewriteEngine instead")]
    internal static class ARCHIVED_2026_05_28
    {
        static ARCHIVED_2026_05_28() =>
            throw new System.InvalidOperationException(
                "Legacy code archived 2026-05-28. Use RewriteEngine instead. See archive/legacy-engine-2026-05-28/");
    }
}
"@
}

# =============================================================================
# MAIN EXECUTION
# =============================================================================

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Legacy Engine Archive Script - Phase D-6" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Create archive root if not exists
if (-not (Test-Path -Path $ArchiveRoot)) {
    New-Item -ItemType Directory -Path $ArchiveRoot | Out-Null
    Write-Host "Created archive directory: $ArchiveRoot" -ForegroundColor Green
}

$TotalArchived = 0

foreach ($Dir in $LegacyDirs) {
    $SourceDir = Join-Path -Path $SourceRoot -ChildPath $Dir
    $ArchiveDir = Join-Path -Path $ArchiveRoot -ChildPath $Dir
    
    if (-not (Test-Path -Path $SourceDir)) {
        Write-Warning "Directory not found: $SourceDir"
        continue
    }
    
    Write-Host "`nProcessing directory: $Dir" -ForegroundColor Yellow
    
    # Create archive directory structure
    if (-not (Test-Path -Path $ArchiveDir)) {
        New-Item -ItemType Directory -Path $ArchiveDir | Out-Null
    }
    
    # Get all .cs files recursively
    $Files = Get-ChildItem -Path $SourceDir -Filter "*.cs" -Recurse -File
    
    if ($Files.Count -eq 0) {
        Write-Host "  No .cs files found in $Dir" -ForegroundColor Gray
        continue
    }
    
    Write-Host "  Found $($Files.Count) files to archive..." -ForegroundColor Gray
    
    foreach ($File in $Files) {
        # Calculate relative path from source directory
        $RelativePath = $File.FullName.Substring($SourceDir.Length).TrimStart('\')
        
        # Destination in archive
        $ArchivePath = Join-Path -Path $ArchiveDir -ChildPath $RelativePath
        
        # Ensure parent directory exists in archive
        $ArchiveParent = Split-Path -Path $ArchivePath -Parent
        if (-not (Test-Path -Path $ArchiveParent)) {
            New-Item -ItemType Directory -Path $ArchiveParent | Out-Null
        }
        
        # Copy file to archive
        Copy-Item -Path $File.FullName -Destination $ArchivePath -Force
        
        # Create STUB content - use the top-level directory as namespace
        $Namespace = $Dir
        $StubContent = Get-StubContent -RelativePath $RelativePath -Namespace $Namespace
        
        # Replace source file with STUB
        Set-Content -Path $File.FullName -Value $StubContent -Force
        
        Write-Host "  [ARCHIVED] $RelativePath" -ForegroundColor Green
        $TotalArchived++
    }
    
    Write-Host "  -> Archived $($Files.Count) files from $Dir" -ForegroundColor Green
}

# =============================================================================
# SUMMARY
# =============================================================================
Write-Host "`n" + "="*70 -ForegroundColor Cyan
Write-Host "SUCCESS: Archive completed!" -ForegroundColor Green
Write-Host "="*70 -ForegroundColor Cyan
Write-Host ""
Write-Host "Total files archived: $TotalArchived" -ForegroundColor Green
Write-Host "Archive location: $ArchiveRoot" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run: dotnet build core-csharp/src/MathCursor.Engine"
Write-Host "  2. Fix any compilation errors if needed"
Write-Host "  3. Run tests: dotnet test"
Write-Host ""
Write-Host "To restore a file:" -ForegroundColor Yellow
Write-Host "  git checkout 4c70adc -- [file_path]"
Write-Host ""
