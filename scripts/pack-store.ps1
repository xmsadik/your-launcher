<#
.SYNOPSIS
  Build the Microsoft Store upload: one unsigned .msixbundle with the x64 and arm64 packages.

.DESCRIPTION
  Runs pack.ps1 (unsigned - the Store signs the package itself) for x64 and arm64, then bundles
  both with makeappx into dist\store\YourLauncher_<version>_Bundle.msixbundle. Upload that file
  in Partner Center -> Packages. The package identity (Name/Publisher) must already be the one
  Partner Center assigned, or the upload is rejected.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root  = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist  = Join-Path $root 'dist'
$store = Join-Path $dist 'store'

[xml]$buildProps = Get-Content (Join-Path $root 'Directory.Build.props')
$baseVersion = ($buildProps.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $baseVersion) { throw 'No <Version> found in Directory.Build.props' }
$version = "$baseVersion.0"

[xml]$manifest = Get-Content (Join-Path $root 'packaging\AppxManifest.xml')
if ($manifest.Package.Identity.Name -ne 'ABAPer.YourLauncher') {
    Write-Warning 'Package identity is not the one Partner Center reserved; the upload will be rejected.'
}

$packages = foreach ($platform in 'x64', 'arm64') {
    & (Join-Path $PSScriptRoot 'pack.ps1') -Platform $platform | Out-Host
    $msix = Get-ChildItem $dist -Filter "YourLauncher_${version}_$platform.msix" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msix) { throw "No $platform MSIX for version $version under $dist" }
    $msix
}

$makeappx = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like '*\x64' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $makeappx) {
    $makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64' -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.DirectoryName -replace '.*\\bin\\([\d.]+)\\x64$', '$1') } -Descending |
        Select-Object -First 1
}
if (-not $makeappx) { throw 'makeappx.exe not found; run a build first so the SDK.BuildTools package is restored, or install the Windows 10/11 SDK.' }

New-Item -ItemType Directory -Force $store | Out-Null
$mapping = Join-Path $store 'bundle_mapping.txt'
$lines = @('[Files]') + ($packages | ForEach-Object { "`"$($_.FullName)`" `"$($_.Name)`"" })
Set-Content -Path $mapping -Value $lines -Encoding utf8

$bundle = Join-Path $store "YourLauncher_${version}_Bundle.msixbundle"
& $makeappx.FullName bundle /o /f $mapping /p $bundle /bv $version
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed ($LASTEXITCODE)" }

Write-Host "Store bundle: $bundle"
