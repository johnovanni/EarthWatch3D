# Building and releasing EarthWatch3D

EarthWatch3D ships as one self-contained x64 MSIX package (`EarthWatch3D.msix`) plus an App Installer file
(`EarthWatch3D.appinstaller`) that tells Windows where to look for updates. Both are built by
`tools/build-release.ps1`.

## Prerequisites

- Windows 10 or 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and **PowerShell 7** (`pwsh`).
- Nothing else: the Windows SDK tools (`makeappx`, `makepri`, `signtool`) and the Artifact Signing client come from
  NuGet packages pinned in `packaging/tools.proj`. The script runs `dotnet restore packaging/tools.proj`, which puts
  them in the NuGet cache (`%USERPROFILE%\.nuget\packages`), and uses them from there. The Windows SDK installed on the
  machine (if any) is not used; 10.0.19041, for example, has a `signtool` too old for Artifact Signing.

| Package | Version | What it provides |
|---|---|---|
| `Microsoft.Windows.SDK.BuildTools` | 10.0.26100.9457 | `makeappx.exe`, `makepri.exe`, `signtool.exe` (x64) |
| `Microsoft.ArtifactSigning.Client` | 1.0.151 | `Azure.CodeSigning.Dlib.dll` for `signtool /dlib` (Azure signing) |

Chosen 2026-11-12. `Microsoft.ArtifactSigning.Client` is the package that Microsoft Learn's
[Set up signing integrations to use Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations)
links for the SignTool dlib (the service was called Trusted Signing, and its older package
`Microsoft.Trusted.Signing.Client` stops at 1.0.95). 1.0.151 was its newest version on nuget.org. The build tools
are the newest stable 10.0.26100.x (a 10.0.28000 line also exists; it is not needed). The same page asks for SignTool
10.0.2261.755 or later. To move to newer versions, change both numbers in `packaging/tools.proj` and this table, then
build and check the self-check passes.

The package and `.appinstaller` templates are `packaging/AppxManifest.xml` and `packaging/EarthWatch3D.appinstaller`;
the script fills their `{Version}`, `{Publisher}`, `{PublisherDisplayName}`, `{AppInstallerUri}` and `{MsixUri}`.
`packaging/priconfig.xml` is the resource-index configuration for the logos (committed; see the comment in it).

## Building a test package

```
pwsh tools/build-release.ps1 -Signing Test
```

It takes about half a minute when the app is already built. The steps:

1. Reads the version from `Directory.Build.props` (`1.0.0`; the package version adds `.0`: `1.0.0.0`).
2. `dotnet publish` (Release, win-x64, self-contained) to `artifacts\publish`.
3. Stages the publish output, the logos (`packaging\Images`) and the filled manifest in `artifacts\stage`, and builds
   `resources.pri` with `makepri`.
4. `makeappx pack` to `artifacts\release\EarthWatch3D.msix`.
5. Signs it with the test certificate **CN=EarthWatch3D Test Signing**. The first run creates that certificate in
   your personal store, `Cert:\CurrentUser\My`, valid for one year (a later run makes a new one when it has under 7
   days left). Signing uses its thumbprint (`signtool sign /sha1`), so there is no `.pfx` file and no password. The
   public half is exported to `artifacts\release\EarthWatch3D-test.cer`.
6. Writes `artifacts\release\EarthWatch3D.appinstaller`.
7. **Self-check:** unpacks the MSIX and checks that the package's `Version` and `Publisher`, the `.appinstaller`'s
   `Version`, `MainPackage Version`, `Publisher` and `Name`, and the exe's product version all agree with
   `Directory.Build.props`. It also runs `signtool verify /pa` and checks that the signer is the publisher. For a test
   build the only error allowed is "terminated in a root certificate which is not trusted", which is expected until
   you trust the test certificate (next section). Any mismatch fails the script because a version mismatch makes
   updates silently never apply.

Whenever the script fails after packing (a failed signature, a mismatch, an unreadable file), it deletes
`EarthWatch3D.msix`, `EarthWatch3D.appinstaller` and `EarthWatch3D-test.cer` from the output folder before it stops,
so an unsigned or unchecked package is never left where it could be published by mistake.

Options:

| Option | Default | Meaning |
|---|---|---|
| `-Signing Test` | Test | local test certificate (as above) |
| `-Signing Azure` | | Azure Artifact Signing (see the last section; publisher from `packaging/signing.local.json`) |
| `-Signing None` | | unsigned, for checking the templates only; Windows will not install it |
| `-BaseUri <uri>` | `https://github.com/johnovanni/EarthWatch3D/releases/latest/download/` | where the `.appinstaller` says the two files are |
| `-OutDir <folder>` | `artifacts\release` | output folder |
| `-MetadataPath <file>` | `packaging\metadata.json` | Azure only: the Artifact Signing account and profile file |
| `-SigningLocalPath <file>` | `packaging\signing.local.json` | Azure only: the publisher and display name |

The file names never carry the version, so the GitHub "latest release" URLs stay the same from one release to the
next and every installed copy finds the next version at the same address.

A test build and an Azure-signed build are **different apps to Windows** (the publisher is part of the package
identity). Uninstall the test build before installing a signed one.

## Trusting the test certificate (your one-time step)

Windows installs an MSIX only if its signature chains to a trusted root. The test certificate is self-signed, so you
have to trust it once on each PC you test on. In an **elevated** PowerShell (Run as administrator), from the
repository folder:

```
Import-Certificate -FilePath artifacts\release\EarthWatch3D-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

This changes a machine setting, so the build script never does it. To remove the trust again (also elevated):

```
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=EarthWatch3D Test Signing' | Remove-Item
```

The certificate with its private key stays in `Cert:\CurrentUser\My` for signing; it grants no trust there. To delete
it too: `Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=EarthWatch3D Test Signing' | Remove-Item`
(the next test build then makes a new one, which you would have to trust again).

## Installing

Open `EarthWatch3D.appinstaller` (double-click it in Explorer). App Installer shows the app, the publisher and the
version; choose **Install**. For a local test, build with a `file:///` base URI so the `.appinstaller` points at the
files on disk:

```
pwsh tools/build-release.ps1 -Signing Test -BaseUri "file:///C:/repos/EarthWatch3D/artifacts/release/"
```

On a web page, link the `.appinstaller` **file** itself (a normal download). Do not use an `ms-appinstaller:?source=`
link: Windows turns that protocol off by default (since late 2023), so it does nothing for most people.

The manual checks for an installed package (Start menu entry, data folder, update keeps settings, uninstall) are in
[manual-test.md](manual-test.md#msix).

## Releasing an update

1. Raise the version in `Directory.Build.props` (for example `1.0.0` → `1.0.1`). It is the only place it is set; the
   app, the package and the `.appinstaller` all follow it.
2. Build with the same `-Signing` and `-BaseUri` as the installed version.
3. Publish both files at the base URI, replacing the old ones.

Installed copies find the new `.appinstaller` at the same address. Windows looks in the background, and when the app
starts (at most once every 24 hours) it shows a prompt; the update is not forced and does not block starting the app
(`HoursBetweenUpdateChecks="24" ShowPrompt="true" UpdateBlocksActivation="false"`, plus `AutomaticBackgroundTask`).

## Publishing a release on GitHub

**Only when the owner (johnovanni) asks for it.** A release is public, and every installed copy updates from it.

```
gh release create v1.0.0 artifacts\release\EarthWatch3D.msix artifacts\release\EarthWatch3D.appinstaller
```

Build with the default `-BaseUri` (`https://github.com/johnovanni/EarthWatch3D/releases/latest/download/`) and the
`Azure` signing (a test-signed package cannot be installed by anyone who has not trusted the test certificate). Never
upload `EarthWatch3D-test.cer`, `packaging/metadata.json` or `packaging/signing.local.json`.

### Open item: does App Installer follow GitHub's redirect?

`https://github.com/<owner>/<repo>/releases/latest/download/<file>` answers with a redirect to the newest release's
file. Whether App Installer follows that redirect, both when installing from the `.appinstaller` and when checking
for updates, is **not verified**: no source found confirms it either way. Check it at the first real release by
installing from the published `.appinstaller` (and later, by updating from it). If it fails, host the two files on
GitHub Pages instead and build with that `-BaseUri` (for example `https://johnovanni.github.io/EarthWatch3D/`).

## Signing with Azure Artifact Signing

Azure Artifact Signing (formerly Trusted Signing) gives the package a publicly trusted signature, so Windows installs
it without anyone trusting a test certificate. Its certificates last only three days; the signature stays valid
because `signtool` also adds a timestamp from Microsoft's time-stamping authority.

### What Microsoft Learn says (checked 2026-11-12)

From [Set up signing integrations to use Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations)
(page dated 2026-08-03) and [Quickstart: Set up Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/quickstart)
(2026-10-08):

- **SignTool:** the Windows SDK `signtool.exe` 10.0.2261.755 or later; the SDK 20348 build is not supported with the
  dlib. The script uses the 10.0.26100.9457 build from the pinned NuGet package.
- **dlib:** NuGet package `Microsoft.ArtifactSigning.Client`; the file is `bin\x64\Azure.CodeSigning.Dlib.dll` (x64
  `signtool` needs the x64 dlib; the restored 1.0.151 package has exactly that file, next to its dependencies and
  `metadata.sample.json`). It needs the .NET 8 runtime and the Visual C++ redistributable; the package carries the C++
  runtime DLLs in `bin\x64`. **Verify on the first real run** that signing works with only .NET 10 installed (the
  Learn page names the .NET 8 runtime); if `signtool` reports the dlib cannot start, install the .NET 8 runtime.
- **Command line:** `signtool sign /v /debug /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib
  <...>\x64\Azure.CodeSigning.Dlib.dll /dmdf <metadata.json> <file>`. The script runs exactly this.
- **`metadata.json` fields:** `Endpoint`, `CodeSigningAccountName`, `CertificateProfileName`, and an optional
  `CorrelationId` (an opaque string to tie signing requests to your own builds). `ExcludeCredentials` can switch
  authentication methods off.
- **Endpoint** must be the one for the region where the account and profile were created; a mismatch gives a 403 and
  `SignerSign()` failed. Regions: Brazil South `brs`, Central US `cus`, East US `eus`, Japan East `jpe`, Korea Central
  `krc`, North Central US `ncus`, North Europe `neu`, Poland Central `plc`, South Central US `scus`, Switzerland North
  `swn`, West Central US `wcus`, West Europe `weu`, West US `wus`, West US 2 `wus2`, West US 3 `wus3`, each as
  `https://<code>.codesigning.azure.net`.
- **Authentication:** `DefaultAzureCredential`, which includes the Azure CLI's sign-in. Your account needs the
  **Artifact Signing Certificate Profile Signer** role on the profile.
- **Subject:** when you create the certificate profile in the Azure portal, **Certificate Subject Preview** shows what
  will be in the certificate (CN and O from the verified identity, plus city, state and country; street address and
  postal code only if you tick them). The package's `Publisher` must equal the certificate's subject, exactly. Learn
  does not give the subject as one ready-made `CN=...` string, so build it from the preview and **confirm it
  against the signed file** (below).

### One-time setup (yours)

1. In the Azure portal: register the `Microsoft.CodeSigning` resource provider, create an Artifact Signing account,
   complete identity validation (public trust; for an individual developer this needs a US or Canadian address, and
   review takes 1 to 20 business days), then create a certificate profile (type Public Trust).
2. Give your user the Certificate Profile Signer role.
3. Copy `packaging\metadata.example.json` to `packaging\metadata.json` and fill in the endpoint for your region, the
   account name and the profile name.
4. Copy `packaging\signing.local.example.json` to `packaging\signing.local.json` and fill in `publisher` (the
   subject, like `CN=Your Name, O=Your Name, L=City, S=State, C=US`, in the order and spelling the certificate has)
   and `publisherDisplayName`.
5. `az login`.

Both JSON files are git-ignored and must stay local; never commit or upload them.

### Building

```
pwsh tools/build-release.ps1 -Signing Azure
```

Before the long build the script checks that both files exist and are filled in (and that `publisher` starts with
`CN=`), that the `az` command exists and that `az account show` succeeds ("Not signed in to Azure. Run az login
first"). Then it publishes, packs and signs the same way as a test build, with `signtool` and the dlib. The
self-check differs from the test build's in one way: `signtool verify /pa` must **pass with a trusted chain** (no
untrusted-root exception), and the signer's subject must equal `publisher`. If it does not, the script deletes the
release files and says why.

If the signer-subject check fails, the message shows the real subject; copy it into `signing.local.json` (the
package's `Publisher` is built from that file, so it changes with it).

### Not verified yet

This branch of the script was written without an Artifact Signing account. Checked so far: the missing-file and
not-signed-in refusals, and the failure cleanup. **Not checked:** an actual signature, the trusted-chain
self-check, the .NET 8 note above, and the timings. After the first real run, record here the signer subject, the
build time and anything that differed.
