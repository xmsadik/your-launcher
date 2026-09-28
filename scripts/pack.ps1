<#
.SYNOPSIS
  Build a Release MSIX of Your Launcher, optionally signed with a self-signed certificate.

.DESCRIPTION
  WPF apps can't use single-project MSIX tooling (that only supports the WinUI/UWP project
  types), so the package is built by hand:

    1. dotnet publish the app to a clean layout folder (self-contained, ReadyToRun).
    2. Strip .pdb files, copy packaging/Assets in, and write AppxManifest.xml with the
       $VERSION$/$ARCH$ tokens substituted.
    3. makeappx pack the layout folder into dist\YourLauncher_<version>_<arch>.msix.

  Output goes to dist\. Without -Sign the package is unsigned and only useful for inspection.
  With -Sign, a self-signed code-signing certificate whose subject matches the manifest
  Publisher (the Partner Center publisher id) is created in CurrentUser\My on first use, its
  public part is exported to dist\YourLauncher.cer, and the MSIX is signed. Microsoft Store
  builds are unsigned instead (see pack-store.ps1): the Store signs them itself.

  To install a self-signed package, the target machine must trust the .cer once (elevated):
    Import-Certificate dist\YourLauncher.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

.PARAMETER Platform
  x64 or arm64.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')] [string]$Platform = 'x64',
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'

$root       = Resolve-Path (Join-Path $PSScriptRoot '..')
$project    = Join-Path $root 'src\Launcher.App'
$manifestTemplate = Join-Path $root 'packaging\AppxManifest.xml'
$assetsSrc  = Join-Path $root 'packaging\Assets'
$dist       = Join-Path $root 'dist'
$layout     = Join-Path $dist "layout-$Platform"

if (-not (Test-Path $manifestTemplate)) { throw "Manifest template not found: $manifestTemplate" }

# --- Version: single source is Directory.Build.props' <Version> ------------------------------
[xml]$buildProps = Get-Content (Join-Path $root 'Directory.Build.props')
$baseVersion = ($buildProps.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $baseVersion) { throw 'No <Version> found in Directory.Build.props' }
$version = "$baseVersion.0"

[xml]$manifestXml = Get-Content $manifestTemplate
$publisher = $manifestXml.Package.Identity.Publisher

Write-Host "Version: $version  Platform: $Platform"

# --- Publish -----------------------------------------------------------------------------------
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null

# Arguments are passed as separate argv elements to the call operator below, each its own array
# item - never concatenated into one quoted MSBuild-property string - which sidesteps the argv
# trailing-backslash-before-quote gotcha the DesktopTiler reference script has to work around
# (a literal `"$dist\"` breaks once the repo path contains a space, like "WinLauncher 2" here,
# because Windows argv parsing treats the trailing \" as an escaped quote, not end-of-string).
& dotnet publish $project -c Release -r "win-$Platform" --self-contained `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Get-ChildItem $layout -Filter '*.pdb' | Remove-Item -Force

# --- Assets + manifest ---------------------------------------------------------------------
$layoutAssets = Join-Path $layout 'Assets'
if (Test-Path $layoutAssets) { Remove-Item $layoutAssets -Recurse -Force }
Copy-Item $assetsSrc $layoutAssets -Recurse

$manifestOut = Join-Path $layout 'AppxManifest.xml'
(Get-Content $manifestTemplate -Raw) `
    -replace [regex]::Escape('$VERSION$'), $version `
    -replace [regex]::Escape('$ARCH$'), $Platform |
    Set-Content -Path $manifestOut -Encoding utf8

# --- Locate makeappx.exe -----------------------------------------------------------------------
function Find-SdkTool([string]$name) {
    $tool = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like '*\x64' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $tool) {
        $tool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64' -Filter $name -ErrorAction SilentlyContinue |
            Sort-Object { [version]($_.DirectoryName -replace '.*\\bin\\([\d.]+)\\x64$', '$1') } -Descending |
            Select-Object -First 1
    }
    return $tool
}

$makeappx = Find-SdkTool 'makeappx.exe'
if (-not $makeappx) { throw 'makeappx.exe not found; run a build first so the SDK.BuildTools package is restored, or install the Windows 10/11 SDK.' }

# --- Pack ----------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $dist | Out-Null
$msix = Join-Path $dist "YourLauncher_${version}_$Platform.msix"
& $makeappx.FullName pack /o /d $layout /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed ($LASTEXITCODE)" }

# Verify the resource language actually packed - lesson from a sibling project: an x-generate
# build produced a tr-TR package on a Turkish-locale build machine.
[xml]$packedManifest = Get-Content $manifestOut
$lang = $packedManifest.Package.Resources.Resource.Language
Write-Host "Packed manifest Resource Language: $lang"
if ($lang -ne 'en-US') { Write-Warning "Expected en-US, got '$lang'" }

# --- Optional signing ------------------------------------------------------------------------
if ($Sign) {
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $publisher -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3) `
            -TextExtension @('2.5.29.19={text}')
        Write-Host "Created signing certificate $($cert.Thumbprint)"
    }
    Export-Certificate -Cert $cert -FilePath (Join-Path $dist 'YourLauncher.cer') | Out-Null

    $signtool = Find-SdkTool 'signtool.exe'
    if (-not $signtool) { throw 'signtool.exe not found; run a build first so the SDK.BuildTools package is restored.' }

    & $signtool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint /s My /tr http://timestamp.digicert.com /td sha256 $msix
    if ($LASTEXITCODE -ne 0) { throw "Signing failed ($LASTEXITCODE)" }
}

Write-Host "Package: $msix"
