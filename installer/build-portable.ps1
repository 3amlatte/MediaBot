param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectDir = Split-Path $root -Parent
$dist = Join-Path $projectDir 'dist'
$staging = Join-Path $dist 'portable'

Push-Location $projectDir
dotnet publish "MediaRenamer.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $staging -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Pop-Location

# Debug symbols aren't useful in a portable download.
Get-ChildItem -Path $staging -Filter '*.pdb' -File | Remove-Item -Force

Copy-Item -Path (Join-Path $projectDir 'README.md') -Destination $staging -Force
Copy-Item -Path (Join-Path $projectDir 'LICENSE') -Destination $staging -Force

# The marker switches the app to portable mode: settings.json and undo.json
# are kept in this folder instead of %AppData%\MediaRenamer.
New-Item -ItemType File -Path (Join-Path $staging 'MediaBot.portable') -Force | Out-Null

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem -Path $dist -Filter 'MediaBot-*-portable-x64.zip' | Remove-Item -Force
$zip = Join-Path $dist "MediaBot-$Version-portable-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "Built $zip"
