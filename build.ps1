#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "==> Building Release" -ForegroundColor Cyan
dotnet build MT_F1Chronos.sln -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$exePath = Join-Path $root "dist\MT_F1Chronos.exe"
if (-not (Test-Path $exePath)) {
    throw "Executable not found: $exePath"
}

$iconPath = Join-Path $root "assets\app.ico"
$workingDir = Split-Path -Parent $exePath

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [string]$IconPath
    )

    $folder = Split-Path -Parent $ShortcutPath
    if (-not (Test-Path $folder)) {
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
    }

    # Always replace an existing shortcut with the latest build path/icon.
    if (Test-Path $ShortcutPath) {
        Remove-Item -LiteralPath $ShortcutPath -Force
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = "F1 Chronos"
    if ($IconPath -and (Test-Path $IconPath)) {
        $shortcut.IconLocation = "$IconPath,0"
    }
    $shortcut.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

Write-Host "==> Creating shortcuts" -ForegroundColor Cyan

$desktop = [Environment]::GetFolderPath("Desktop")
$startup = [Environment]::GetFolderPath("Startup")

$desktopShortcut = Join-Path $desktop "F1 Chronos.lnk"
$startupShortcut = Join-Path $startup "F1 Chronos.lnk"

New-AppShortcut -ShortcutPath $desktopShortcut -TargetPath $exePath -WorkingDirectory $workingDir -IconPath $iconPath
New-AppShortcut -ShortcutPath $startupShortcut -TargetPath $exePath -WorkingDirectory $workingDir -IconPath $iconPath

Write-Host ""
Write-Host "Executable:" -ForegroundColor Green
Write-Host "  $exePath"
Write-Host "Desktop shortcut:" -ForegroundColor Green
Write-Host "  $desktopShortcut"
Write-Host "Startup shortcut:" -ForegroundColor Green
Write-Host "  $startupShortcut"
