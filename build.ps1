# PDF Binder - Single-File EXE Build Script (.NET 10 / win-x64)
# Usage:
#   .\build.ps1                              # Build both (all)
#   .\build.ps1 -Target self-contained       # Build Self-Contained only
#   .\build.ps1 -Target framework-dependent  # Build Framework-Dependent only

param(
    [ValidateSet("all", "self-contained", "framework-dependent")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PDF Binder - Build Process (.NET 10 / win-x64)" -ForegroundColor Cyan
Write-Host " Target: $Target" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$distDir = Join-Path $PSScriptRoot "dist"
$selfContainedDir = Join-Path $distDir "self-contained"
$frameworkDependentDir = Join-Path $distDir "framework-dependent"
$projectPath = Join-Path $PSScriptRoot "src/PDFBinder.App/PDFBinder.App.csproj"

# Clean output directories
if ($Target -eq "all") {
    if (Test-Path $distDir) {
        Write-Host "Cleaning dist folder..." -ForegroundColor Yellow
        Remove-Item $distDir -Recurse -Force
    }
} elseif ($Target -eq "self-contained") {
    if (Test-Path $selfContainedDir) {
        Remove-Item $selfContainedDir -Recurse -Force
    }
} elseif ($Target -eq "framework-dependent") {
    if (Test-Path $frameworkDependentDir) {
        Remove-Item $frameworkDependentDir -Recurse -Force
    }
}

# 1. Publish Self-Contained
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
        -o $selfContainedDir

    $scExe = Join-Path $selfContainedDir "PDFBinder.exe"
    if (Test-Path $scExe) {
        # Keep backward compatibility and root placement
        Copy-Item -Path $scExe -Destination (Join-Path $distDir "PDFBinder.exe") -Force
    } else {
        Write-Error "Failed to build Self-Contained PDFBinder.exe"
    }
}

# 2. Publish Framework-Dependent
if ($Target -eq "all" -or $Target -eq "framework-dependent") {
    Write-Host ""
    Write-Host "[Framework-Dependent] Publishing..." -ForegroundColor Green
    dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --no-self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $frameworkDependentDir

    $fdExe = Join-Path $frameworkDependentDir "PDFBinder.exe"
    if (Test-Path $fdExe) {
        # Place alias in root dist directory
        Copy-Item -Path $fdExe -Destination (Join-Path $distDir "PDFBinder-FrameworkDependent.exe") -Force
    } else {
        Write-Error "Failed to build Framework-Dependent PDFBinder.exe"
    }
}

# Summary
Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Publish Summary" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

$scSummaryExe = Join-Path $selfContainedDir "PDFBinder.exe"
if (Test-Path $scSummaryExe) {
    $scItem = Get-Item $scSummaryExe
    $scSizeMb = [Math]::Round($scItem.Length / 1MB, 2)
    Write-Host " [Self-Contained / 自己完結版]" -ForegroundColor Cyan
    Write-Host "  - Path: $scSummaryExe (and dist/PDFBinder.exe)" -ForegroundColor White
    Write-Host "  - Size: $scSizeMb MB" -ForegroundColor Yellow
    Write-Host "  - Info: Bundles .NET 10 runtime. Fully offline, no pre-installed runtime required." -ForegroundColor Gray
    Write-Host ""
}

$fdSummaryExe = Join-Path $frameworkDependentDir "PDFBinder.exe"
if (Test-Path $fdSummaryExe) {
    $fdItem = Get-Item $fdSummaryExe
    $fdSizeMb = [Math]::Round($fdItem.Length / 1MB, 2)
    Write-Host " [Framework-Dependent / フレームワーク依存版]" -ForegroundColor Cyan
    Write-Host "  - Path: $fdSummaryExe (and dist/PDFBinder-FrameworkDependent.exe)" -ForegroundColor White
    Write-Host "  - Size: $fdSizeMb MB" -ForegroundColor Yellow
    Write-Host "  - Info: Requires .NET 10 Desktop Runtime installed on OS. Lightweight, faster cold start." -ForegroundColor Gray
    Write-Host ""
}

Write-Host "==========================================================" -ForegroundColor Green
