# PDF Binder - Build Script (.NET 10 / win-x64)
# Usage:
#   .\build.ps1                              # Build both (all)
#   .\build.ps1 -Target self-contained       # Build Self-Contained only (Single-File EXE)
#   .\build.ps1 -Target framework-dependent  # Build Framework-Dependent only (DLL-separated)

param(
    [ValidateSet("all", "self-contained", "framework-dependent")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PDF Binder - Build Process (.NET 10 / win-x64)" -ForegroundColor Cyan
Write-Host " Target: $Target" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$distDir = Join-Path $PSScriptRoot "dist"
$selfContainedDir = Join-Path $distDir "self-contained"
$projectPath = Join-Path $PSScriptRoot "src/PDFBinder.App/PDFBinder.App.csproj"

# Clean output directories
if ($Target -eq "all") {
    if (Test-Path $distDir) {
        Write-Host "Cleaning dist folder..." -ForegroundColor Yellow
        Remove-Item $distDir -Recurse -Force
    }
} elseif ($Target -eq "self-contained") {
    if (Test-Path $selfContainedDir) {
        Write-Host "Cleaning dist/self-contained folder..." -ForegroundColor Yellow
        Remove-Item $selfContainedDir -Recurse -Force
    }
} elseif ($Target -eq "framework-dependent") {
    if (Test-Path $distDir) {
        Write-Host "Cleaning framework-dependent files in dist folder..." -ForegroundColor Yellow
        Get-ChildItem -Path $distDir -Exclude "self-contained" | Remove-Item -Recurse -Force
    }
}

# 1. Publish Self-Contained (Single-File EXE with ReadyToRun and embedded PDB)
if ($Target -eq "all" -or $Target -eq "self-contained") {
    Write-Host ""
    Write-Host "[Self-Contained] Publishing..." -ForegroundColor Green
    dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:DebugType=embedded `
        -o $selfContainedDir

    $scExe = Join-Path $selfContainedDir "PDFBinder.exe"
    if (-not (Test-Path $scExe)) {
        Write-Error "Failed to build Self-Contained PDFBinder.exe"
    }
}

# 2. Publish Framework-Dependent (DLL-separated with ReadyToRun and embedded PDB)
if ($Target -eq "all" -or $Target -eq "framework-dependent") {
    Write-Host ""
    Write-Host "[Framework-Dependent] Publishing..." -ForegroundColor Green
    dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --no-self-contained `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:DebugType=embedded `
        -o $distDir

    $fdExe = Join-Path $distDir "PDFBinder.exe"
    if (-not (Test-Path $fdExe)) {
        Write-Error "Failed to build Framework-Dependent PDFBinder.exe"
    }
}

# Summary
Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Publish Summary" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

$fdSummaryExe = Join-Path $distDir "PDFBinder.exe"
if (Test-Path $fdSummaryExe) {
    $fdItem = Get-Item $fdSummaryExe
    $fdExeSizeMb = [Math]::Round($fdItem.Length / 1MB, 2)
    $fdAllFiles = Get-ChildItem -Path $distDir -File
    $fdTotalBytes = ($fdAllFiles | Measure-Object -Property Length -Sum).Sum
    $fdTotalSizeMb = [Math]::Round($fdTotalBytes / 1MB, 2)
    Write-Host " [Framework-Dependent / フレームワーク依存版 (DLL分離形式)]" -ForegroundColor Cyan
    Write-Host "  - Entry EXE : $fdSummaryExe ($fdExeSizeMb MB)" -ForegroundColor White
    Write-Host "  - Total Dir : $distDir ($($fdAllFiles.Count) files, total $fdTotalSizeMb MB)" -ForegroundColor White
    Write-Host "  - Info      : Requires .NET 10 Desktop Runtime. DLL-separated, ReadyToRun, fastest cold start." -ForegroundColor Gray
    Write-Host ""
}

$scSummaryExe = Join-Path $selfContainedDir "PDFBinder.exe"
if (Test-Path $scSummaryExe) {
    $scItem = Get-Item $scSummaryExe
    $scSizeMb = [Math]::Round($scItem.Length / 1MB, 2)
    Write-Host " [Self-Contained / 自己完結版 (単一EXE形式)]" -ForegroundColor Cyan
    Write-Host "  - Path : $scSummaryExe" -ForegroundColor White
    Write-Host "  - Size : $scSizeMb MB" -ForegroundColor Yellow
    Write-Host "  - Info : Bundles .NET 10 runtime. ReadyToRun, fully offline, no pre-installed runtime required." -ForegroundColor Gray
    Write-Host ""
}

Write-Host "==========================================================" -ForegroundColor Green
