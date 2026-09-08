# PDF Binder 単一実行可能ファイル（Self-Contained Single-File EXE）発行スクリプト
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PDF Binder - 単一EXE発行プロセスを開始します (.NET 10 / win-x64)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$distDir = Join-Path $PSScriptRoot "dist"
$projectPath = Join-Path $PSScriptRoot "src/PDFBinder.App/PDFBinder.App.csproj"

if (Test-Path $distDir) {
    Write-Host "既存の dist フォルダをクリーンアップしています..." -ForegroundColor Yellow
    Remove-Item $distDir -Recurse -Force
}

Write-Host "発行コマンドを実行しています..." -ForegroundColor Green
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $distDir

$exePath = Join-Path $distDir "PDFBinder.exe"
if (Test-Path $exePath) {
    $item = Get-Item $exePath
    $sizeMb = [Math]::Round($item.Length / 1MB, 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " 発行成功: $exePath" -ForegroundColor Green
    Write-Host " ファイルサイズ: $sizeMb MB" -ForegroundColor Green
    Write-Host " この単一exeファイルのみで完全オフライン・ランタイム不要で起動可能です。" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Error "PDFBinder.exe の生成に失敗しました。"
}
