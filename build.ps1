#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "==> Reading version from Directory.Build.props" -ForegroundColor Cyan
$props = Get-Content "Directory.Build.props" -Raw
if ($props -notmatch "<Version>([^<]+)</Version>") {
    throw "Version not found in Directory.Build.props"
}
$version = $Matches[1]
Write-Host "    Version: $version"

Write-Host "==> Publishing self-contained win-x64 to publish\" -ForegroundColor Cyan
dotnet publish "src\MT_F1Chronos.App\MT_F1Chronos.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o "publish"

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host ""
Write-Host "Published app:" -ForegroundColor Green
Write-Host "  publish\MT_F1Chronos.exe"

$iscc = $null
foreach ($candidate in @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "ISCC.exe"
)) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        $iscc = (Get-Command $candidate).Source
        break
    }
    if (Test-Path $candidate) {
        $iscc = $candidate
        break
    }
}

if ($iscc) {
    Write-Host "==> Building installer with Inno Setup" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
    & $iscc "installer\MT_F1Chronos.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
    Write-Host ""
    Write-Host "Installer:" -ForegroundColor Green
    Write-Host "  artifacts\F1Chronos-Setup-$version.exe"
} else {
    Write-Host ""
    Write-Host "Inno Setup not found — app published, installer skipped." -ForegroundColor Yellow
    Write-Host "Install Inno Setup 6, then run:" -ForegroundColor Yellow
    Write-Host "  iscc installer\MT_F1Chronos.iss"
}

Write-Host ""
Write-Host "REMINDER: bump Version in Directory.Build.props + installer\MT_F1Chronos.iss + README notes on each release." -ForegroundColor Magenta
