param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectDir = Split-Path $root -Parent
$payload = Join-Path $root 'payload'
$dist = Join-Path $projectDir 'dist'

Push-Location $projectDir
dotnet publish "MediaRenamer.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $payload -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Pop-Location

function Get-RelativePath([string]$full) {
    if ($full -eq $payload) { return '' }
    return $full.Substring($payload.Length).TrimStart('\')
}

function New-StableGuid([string]$text) {
    $md5 = [Security.Cryptography.MD5]::Create()
    $hash = $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))
    return ([guid]::new($hash)).ToString().ToUpper()
}

Push-Location $root

$files = @(Get-ChildItem -Recurse -File $payload)
$relDirs = @($files |
    ForEach-Object { Get-RelativePath $_.DirectoryName } |
    Where-Object { $_ -ne '' } |
    Sort-Object -Unique)

$dirIds = @{}
$dirXml = New-Object System.Text.StringBuilder
$i = 0
foreach ($d in $relDirs) {
    if ($d -match '[\\/]') { throw "Nested payload folders are not supported: $d" }
    $id = "DIR_$i"
    $dirIds[$d] = $id
    [void]$dirXml.AppendLine("      <Directory Id=""$id"" Name=""$d"" />")
    $i++
}

$compXml = New-Object System.Text.StringBuilder
$i = 0
foreach ($f in $files) {
    $rel = Get-RelativePath $f.FullName
    $relDir = Get-RelativePath $f.DirectoryName
    $dirRef = if ($relDir -eq '') { 'INSTALLFOLDER' } else { $dirIds[$relDir] }
    $guid = New-StableGuid $rel
    [void]$compXml.AppendLine("      <Component Id=""C_$i"" Directory=""$dirRef"" Guid=""$guid"">")
    [void]$compXml.AppendLine("        <File Id=""F_$i"" Source=""payload\$rel"" KeyPath=""yes"" />")
    [void]$compXml.AppendLine("      </Component>")
    $i++
}

$componentsWxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <DirectoryRef Id="INSTALLFOLDER">
$($dirXml.ToString())    </DirectoryRef>
  </Fragment>
  <Fragment>
    <ComponentGroup Id="AppFiles">
$($compXml.ToString())    </ComponentGroup>
  </Fragment>
</Wix>
"@
Set-Content -Path (Join-Path $root 'Components.wxs') -Value $componentsWxs -Encoding UTF8

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem -Path $dist -Filter 'MediaBot-*-x64.msi' | Remove-Item -Force
Get-ChildItem -Path $dist -Filter 'MediaBot-*-x64.wixpdb' | Remove-Item -Force
$msi = Join-Path $dist "MediaBot-$Version-x64.msi"
wix build "Product.wxs" "Components.wxs" -arch x64 -ext WixToolset.UI.wixext -d "Version=$Version" -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

Pop-Location
Write-Host "Built $msi"
