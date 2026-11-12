#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the EarthWatch3D MSIX package and its .appinstaller file.

.DESCRIPTION
    publish (self-contained win-x64) -> stage (publish output, logos, filled manifest, resources.pri)
    -> makeappx pack -> sign -> fill the .appinstaller -> self-check (the versions and the publisher agree).

    The Windows SDK tools (makeappx, makepri, signtool) and the Artifact Signing dlib come from the NuGet
    packages pinned in packaging/tools.proj; nothing has to be installed first. See docs/release.md.

    Outputs in -OutDir:
      EarthWatch3D.msix, EarthWatch3D.appinstaller and, for -Signing Test, EarthWatch3D-test.cer
      (the public half of the test certificate, for the user's one-time trust step).

    The script never installs, trusts or uploads anything. -Signing Test creates the test certificate in
    Cert:\CurrentUser\My the first time (an untrusted personal store; it grants no trust) and signs by
    thumbprint, so there is no .pfx file and no password.

.PARAMETER Signing
    Test  - sign with the local test certificate "CN=EarthWatch3D Test Signing" (created if missing).
    Azure - sign with Azure Artifact Signing (formerly Trusted Signing); publisher from packaging/signing.local.json.
    None  - leave the package unsigned (for template checks only; Windows will not install it).

.PARAMETER BaseUri
    Where the two release files will be downloadable from; the .appinstaller points at
    <BaseUri>EarthWatch3D.appinstaller and <BaseUri>EarthWatch3D.msix.

.PARAMETER OutDir
    Output folder, relative to the repository root unless rooted.

.EXAMPLE
    pwsh tools/build-release.ps1 -Signing Test

.EXAMPLE
    pwsh tools/build-release.ps1 -Signing Test -BaseUri "file:///C:/repos/EarthWatch3D/artifacts/release/"
#>
[CmdletBinding()]
param(
    [ValidateSet('Test', 'Azure', 'None')]
    [string] $Signing = 'Test',

    [string] $BaseUri = 'https://github.com/johnovanni/EarthWatch3D/releases/latest/download/',

    [string] $OutDir = 'artifacts\release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false   # exit codes are checked explicitly below

$clock = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackagingDir = Join-Path $RepoRoot 'packaging'
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
$PublishDir = Join-Path $ArtifactsDir 'publish'
$StageDir = Join-Path $ArtifactsDir 'stage'
$CheckDir = Join-Path $ArtifactsDir 'selfcheck'
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $RepoRoot $OutDir }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

$MsixName = 'EarthWatch3D.msix'
$AppInstallerName = 'EarthWatch3D.appinstaller'
$TestCerName = 'EarthWatch3D-test.cer'
$ExeName = 'EarthWatch3D.App.exe'

$TestPublisher = 'CN=EarthWatch3D Test Signing'
$TestPublisherDisplayName = 'EarthWatch3D test build'
# signtool verify /pa on a test signature whose root is not trusted on this machine (signtool wraps the line).
$UntrustedRootPattern = 'terminated in a root\s+certificate which is not trusted'

if (-not $BaseUri.EndsWith('/')) { $BaseUri += '/' }
$AppInstallerUri = $BaseUri + $AppInstallerName
$MsixUri = $BaseUri + $MsixName

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------
function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

# Runs a native tool, captures its output (makepri writes UTF-16, hence the NUL removal) and fails on a non-zero
# exit code unless -AllowFailure is given.
function Invoke-Tool {
    param([string] $Exe, [string[]] $Arguments, [switch] $AllowFailure)
    $lines = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" -replace "`0", '' }
    $code = $LASTEXITCODE
    $text = ($lines | Where-Object { $_ -ne '' }) -join [Environment]::NewLine
    if ($code -ne 0 -and -not $AllowFailure) {
        Write-Host $text
        throw "$(Split-Path -Leaf $Exe) failed with exit code $code."
    }
    [pscustomobject]@{ ExitCode = $code; Output = $text }
}

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Remove-Folder([string] $Path) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

# Drops the template's explanatory comments, replaces {Name} placeholders with XML-escaped values and fails if any
# placeholder is left.
function Expand-Template([string] $TemplatePath, [hashtable] $Values) {
    $text = (Get-Content -LiteralPath $TemplatePath -Raw) -replace '(?s)<!--.*?-->\r?\n', ''
    foreach ($key in $Values.Keys) {
        $text = $text.Replace('{' + $key + '}', [System.Security.SecurityElement]::Escape([string]$Values[$key]))
    }
    $left = [regex]::Matches($text, '\{(Version|Publisher|PublisherDisplayName|AppInstallerUri|MsixUri)\}')
    if ($left.Count -gt 0) { throw "$TemplatePath still has placeholders: $($left.Value -join ', ')" }
    $text
}

function Find-Tool([string] $PackageRoot, [string] $FileName) {
    $found = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse -File -Filter $FileName |
        Where-Object { $_.Directory.Name -eq 'x64' })
    if ($found.Count -eq 0) { throw "$FileName (x64) not found under $PackageRoot. Run: dotnet restore packaging/tools.proj" }
    $found[0].FullName
}

# ---------------------------------------------------------------------------------------------------------------
# 1. Version
# ---------------------------------------------------------------------------------------------------------------
$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
$propsXml = [xml](Get-Content -LiteralPath $propsPath -Raw)
$versionNode = $propsXml.SelectSingleNode('/Project/PropertyGroup[not(@Condition)]/Version')
if ($null -eq $versionNode) { throw "No <Version> in an unconditioned PropertyGroup of $propsPath." }
$version = $versionNode.InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$version' in $propsPath is not three numbers (e.g. 1.0.0)." }
$msixVersion = "$version.0"
Write-Step "EarthWatch3D $version (MSIX $msixVersion), signing: $Signing"

# ---------------------------------------------------------------------------------------------------------------
# Signing settings, checked before the long publish step
# ---------------------------------------------------------------------------------------------------------------
switch ($Signing) {
    'Azure' {
        $signingLocal = Join-Path $PackagingDir 'signing.local.json'
        if (-not (Test-Path -LiteralPath $signingLocal)) {
            throw "-Signing Azure needs $signingLocal with { ""publisher"": ""CN=..."", ""publisherDisplayName"": ""..."" } " +
                  "(publisher = the Artifact Signing certificate's subject, exactly). See docs/release.md."
        }
        $local = Get-Content -LiteralPath $signingLocal -Raw | ConvertFrom-Json
        $publisher = [string]$local.publisher
        $publisherDisplayName = [string]$local.publisherDisplayName
        if ([string]::IsNullOrWhiteSpace($publisher) -or [string]::IsNullOrWhiteSpace($publisherDisplayName)) {
            throw "$signingLocal must set both ""publisher"" and ""publisherDisplayName""."
        }
        throw '-Signing Azure is not implemented yet (Plan 4 Task 6). Use -Signing Test.'
    }
    default {
        $publisher = $TestPublisher
        $publisherDisplayName = $TestPublisherDisplayName
    }
}

# ---------------------------------------------------------------------------------------------------------------
# 2. Tools and publish
# ---------------------------------------------------------------------------------------------------------------
Write-Step 'Restoring the pinned packaging tools (packaging/tools.proj)'
$toolsProj = Join-Path $PackagingDir 'tools.proj'
Invoke-Dotnet @('restore', $toolsProj, '--nologo')

$toolsXml = [xml](Get-Content -LiteralPath $toolsProj -Raw)
function Get-PinnedVersion([string] $Package) {
    $node = $toolsXml.SelectSingleNode("//PackageReference[@Include='$Package']")
    if ($null -eq $node) { throw "$Package is not in $toolsProj." }
    $node.GetAttribute('Version')
}
$localsOutput = @(& dotnet nuget locals global-packages --list)
$globalPackages = [string]($localsOutput | Where-Object { $_ -match '^global-packages:' } | Select-Object -First 1)
$globalPackages = ($globalPackages -replace '^global-packages:\s*', '').Trim()
if ($LASTEXITCODE -ne 0 -or $globalPackages -eq '' -or -not (Test-Path -LiteralPath $globalPackages)) { throw "NuGet global-packages folder not found ($globalPackages)." }

$sdkToolsRoot = Join-Path $globalPackages 'microsoft.windows.sdk.buildtools' (Get-PinnedVersion 'Microsoft.Windows.SDK.BuildTools')
$makeAppx = Find-Tool $sdkToolsRoot 'makeappx.exe'
$makePri = Find-Tool $sdkToolsRoot 'makepri.exe'
$signTool = Find-Tool $sdkToolsRoot 'signtool.exe'
$dlibRoot = Join-Path $globalPackages 'microsoft.artifactsigning.client' (Get-PinnedVersion 'Microsoft.ArtifactSigning.Client')
$dlib = Find-Tool $dlibRoot 'Azure.CodeSigning.Dlib.dll'
Write-Host "    makeappx: $makeAppx"
Write-Host "    makepri:  $makePri"
Write-Host "    signtool: $signTool"
Write-Host "    dlib:     $dlib"

Write-Step 'Publishing (Release, win-x64, self-contained)'
Remove-Folder $PublishDir
Invoke-Dotnet @('publish', (Join-Path $RepoRoot 'src\EarthWatch3D.App'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-o', $PublishDir, '--nologo')
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $ExeName))) { throw "$ExeName is not in $PublishDir." }

# ---------------------------------------------------------------------------------------------------------------
# 3. Stage
# ---------------------------------------------------------------------------------------------------------------
Write-Step "Staging $StageDir"
Remove-Folder $StageDir
New-Item -ItemType Directory -Path $StageDir | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $StageDir -Recurse
Copy-Item -LiteralPath (Join-Path $PackagingDir 'Images') -Destination (Join-Path $StageDir 'Images') -Recurse
$manifestText = Expand-Template (Join-Path $PackagingDir 'AppxManifest.xml') @{
    Version              = $msixVersion
    Publisher            = $publisher
    PublisherDisplayName = $publisherDisplayName
}
$stageManifest = Join-Path $StageDir 'AppxManifest.xml'
Set-Content -LiteralPath $stageManifest -Value $manifestText -Encoding utf8NoBOM -NoNewline

# ---------------------------------------------------------------------------------------------------------------
# 4. resources.pri (the logos' scale and target-size variants), from the committed packaging/priconfig.xml
# ---------------------------------------------------------------------------------------------------------------
Write-Step 'Indexing resources (makepri)'
Invoke-Tool $makePri @('new', '/pr', $StageDir, '/cf', (Join-Path $PackagingDir 'priconfig.xml'),
    '/mn', $stageManifest, '/of', (Join-Path $StageDir 'resources.pri'), '/o') | Out-Null

# ---------------------------------------------------------------------------------------------------------------
# 5. Pack
# ---------------------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$msixPath = Join-Path $OutDir $MsixName
$appInstallerPath = Join-Path $OutDir $AppInstallerName
$cerPath = Join-Path $OutDir $TestCerName
foreach ($old in @($msixPath, $appInstallerPath, $cerPath)) {
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
}

Write-Step "Packing $msixPath"
Invoke-Tool $makeAppx @('pack', '/d', $StageDir, '/p', $msixPath, '/o') | Out-Null

# ---------------------------------------------------------------------------------------------------------------
# 6. Sign
# ---------------------------------------------------------------------------------------------------------------
$testCert = $null
switch ($Signing) {
    'Test' {
        Write-Step 'Signing with the test certificate (Cert:\CurrentUser\My)'
        $testCert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -eq $TestPublisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7) } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if ($null -eq $testCert) {
            Write-Host "    creating ""$TestPublisher"" in Cert:\CurrentUser\My (untrusted; no .pfx, no password)"
            $testCert = New-SelfSignedCertificate -Type Custom -Subject $TestPublisher -KeyUsage DigitalSignature `
                -FriendlyName 'EarthWatch3D test signing' -CertStoreLocation Cert:\CurrentUser\My `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        }
        Write-Host "    certificate: $($testCert.Subject), thumbprint $($testCert.Thumbprint), valid to $($testCert.NotAfter.ToString('yyyy-MM-dd'))"
        # Public part only, for the user's one-time trust step (docs/release.md).
        [System.IO.File]::WriteAllBytes($cerPath, $testCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Invoke-Tool $signTool @('sign', '/fd', 'SHA256', '/sha1', $testCert.Thumbprint, $msixPath) | Out-Null
    }
    'None' {
        Write-Step 'Leaving the package unsigned (-Signing None: it cannot be installed)'
    }
}

# ---------------------------------------------------------------------------------------------------------------
# 7. The .appinstaller
# ---------------------------------------------------------------------------------------------------------------
Write-Step "Writing $appInstallerPath"
$appInstallerText = Expand-Template (Join-Path $PackagingDir $AppInstallerName) @{
    Version         = $msixVersion
    Publisher       = $publisher
    AppInstallerUri = $AppInstallerUri
    MsixUri         = $MsixUri
}
Set-Content -LiteralPath $appInstallerPath -Value $appInstallerText -Encoding utf8NoBOM -NoNewline

# ---------------------------------------------------------------------------------------------------------------
# 8. Self-check: what was packed, what the .appinstaller says and what the exe says must agree.
#    A mismatch makes updates silently never apply (plan Review Focus 3).
# ---------------------------------------------------------------------------------------------------------------
Write-Step 'Self-check'
Remove-Folder $CheckDir
Invoke-Tool $makeAppx @('unpack', '/p', $msixPath, '/d', $CheckDir, '/o') | Out-Null

$failures = [System.Collections.Generic.List[string]]::new()
function Test-Equal([string] $What, [string] $Actual, [string] $Expected) {
    $ok = $Actual -ceq $Expected
    Write-Host ("    [{0}] {1}: {2}" -f ($(if ($ok) { 'ok' } else { 'MISMATCH' })), $What, $Actual)
    if (-not $ok) { $failures.Add("$What is '$Actual', expected '$Expected'") }
}

$packedManifest = [xml](Get-Content -LiteralPath (Join-Path $CheckDir 'AppxManifest.xml') -Raw)
$ns = [System.Xml.XmlNamespaceManager]::new($packedManifest.NameTable)
$ns.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identity = $packedManifest.SelectSingleNode('/m:Package/m:Identity', $ns)

$installer = [xml](Get-Content -LiteralPath $appInstallerPath -Raw)
$ns.AddNamespace('ai', 'http://schemas.microsoft.com/appx/appinstaller/2018')
$aiRoot = $installer.SelectSingleNode('/ai:AppInstaller', $ns)
$aiMain = $installer.SelectSingleNode('/ai:AppInstaller/ai:MainPackage', $ns)
if ($null -eq $identity -or $null -eq $aiRoot -or $null -eq $aiMain) { throw 'Self-check could not read the packed manifest or the .appinstaller.' }

$exe = Join-Path $CheckDir $ExeName
if (-not (Test-Path -LiteralPath $exe)) { throw "Self-check: $ExeName is not in the package." }
$exeVersion = ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]

Write-Host "    Directory.Build.props Version: $version (package $msixVersion)"
Test-Equal 'exe ProductVersion (without +commit)' $exeVersion $version
Test-Equal 'package Identity Version' $identity.GetAttribute('Version') $msixVersion
Test-Equal '.appinstaller Version' $aiRoot.GetAttribute('Version') $msixVersion
Test-Equal '.appinstaller MainPackage Version' $aiMain.GetAttribute('Version') $msixVersion
Test-Equal 'package Identity Publisher' $identity.GetAttribute('Publisher') $publisher
Test-Equal '.appinstaller MainPackage Publisher' $aiMain.GetAttribute('Publisher') $publisher
Test-Equal '.appinstaller MainPackage Name' $aiMain.GetAttribute('Name') $identity.GetAttribute('Name')
Test-Equal '.appinstaller Uri' $aiRoot.GetAttribute('Uri') $AppInstallerUri
Test-Equal '.appinstaller MainPackage Uri' $aiMain.GetAttribute('Uri') $MsixUri

$signatureFile = Join-Path $CheckDir 'AppxSignature.p7x'
if ($Signing -eq 'None') {
    Test-Equal 'signature' $(if (Test-Path -LiteralPath $signatureFile) { 'present' } else { 'none' }) 'none'
}
else {
    $verify = Invoke-Tool $signTool @('verify', '/pa', '/v', $msixPath) -AllowFailure
    # Each error message, with signtool's wrapped continuation lines joined.
    $verifyErrors = @([regex]::Matches($verify.Output, 'SignTool Error:[^\r\n]*(\r?\n\s+[^\r\n]+)*') |
        ForEach-Object { $_.Value -replace '\s+', ' ' })
    if ($verify.ExitCode -eq 0) {
        Write-Host '    [ok] signtool verify /pa: signature verified, chain trusted'
    }
    elseif ($Signing -eq 'Test' -and $verifyErrors.Count -eq 1 -and $verifyErrors[0] -match $UntrustedRootPattern -and
            $verify.Output -match 'Number of errors: 1\b') {
        # Expected until the user trusts the test certificate (docs/release.md); the signature itself is intact,
        # and the signer is checked just below.
        Write-Host '    [ok] signtool verify /pa: signed; the only error is the untrusted test root (expected):'
        Write-Host "         $($verifyErrors[0])"
    }
    else {
        Write-Host $verify.Output
        $failures.Add("signtool verify /pa failed (exit $($verify.ExitCode)): $($verifyErrors -join ' | ')")
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $msixPath
    $signer = if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { '(no signer)' }
    Test-Equal 'signer subject' $signer $publisher
    if ($Signing -eq 'Test') {
        $signerThumb = if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { '' }
        Test-Equal 'signer thumbprint' $signerThumb $testCert.Thumbprint
    }
}
Remove-Folder $CheckDir

if ($failures.Count -gt 0) {
    # Leave nothing behind that could be published by mistake.
    foreach ($bad in @($msixPath, $appInstallerPath)) { Remove-Item -LiteralPath $bad -Force -ErrorAction SilentlyContinue }
    throw "Self-check failed (the .msix and .appinstaller were deleted):`n  " + ($failures -join "`n  ")
}
Write-Host '    self-check passed'

# ---------------------------------------------------------------------------------------------------------------
# 9. Summary
# ---------------------------------------------------------------------------------------------------------------
$clock.Stop()
$sizeMb = (Get-Item -LiteralPath $msixPath).Length / 1MB
Write-Step 'Done'
Write-Host ("    {0}  ({1:N1} MB)" -f $msixPath, $sizeMb)
Write-Host "    $appInstallerPath"
if ($Signing -eq 'Test') { Write-Host "    $cerPath  (public test certificate; trusting it is the user's step, see docs/release.md)" }
Write-Host "    .appinstaller points at $AppInstallerUri"
Write-Host ("    build time {0:mm\:ss}" -f $clock.Elapsed)
