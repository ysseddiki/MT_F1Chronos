#!/usr/bin/env pwsh
# Résumé de la conf overlay F1 Chronos (LocalAppData).
# Usage: .\scripts\overlay-status.ps1

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $env:LOCALAPPDATA "MT_F1Chronos"

python3 "$repoRoot\scripts\overlay-status.py" --data-dir $dataDir
if ($LASTEXITCODE -ne 0) {
    # Fallback if python3 is not on PATH
    python "$repoRoot\scripts\overlay-status.py" --data-dir $dataDir
}
