param(
    [switch]$ImportFirefoxBookmarks
)

$ErrorActionPreference = 'Stop'
$distDir = Join-Path $PSScriptRoot 'dist'

Get-Process -Name StuderBrowser -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish (Join-Path $PSScriptRoot 'src\StuderBrowser\StuderBrowser.csproj') -c Release -o $distDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)" }

if ($ImportFirefoxBookmarks) {
    python -I (Join-Path $PSScriptRoot 'tools\import_firefox_bookmarks.py') (Join-Path $distDir 'profile\bookmarks.json')
    if ($LASTEXITCODE -ne 0) { throw "Lesezeichen-Import fehlgeschlagen ($LASTEXITCODE)" }
}

Write-Host "Fertig: $distDir\StuderBrowser.exe"
