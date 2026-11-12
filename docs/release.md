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
   you trust the test certificate (next section). Any mismatch fails the script and deletes the two release files,
   because a version mismatch makes updates silently never apply.

Options:

| Option | Default | Meaning |
|---|---|---|
| `-Signing Test` | Test | local test certificate (as above) |
| `-Signing Azure` | | Azure Artifact Signing (Plan 4 Task 6; publisher from `packaging/signing.local.json`) |
| `-Signing None` | | unsigned, for checking the templates only; Windows will not install it |
| `-BaseUri <uri>` | `https://github.com/johnovanni/EarthWatch3D/releases/latest/download/` | where the `.appinstaller` says the two files are |
| `-OutDir <folder>` | `artifacts\release` | output folder |

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

To be written in Plan 4 Task 6 (`-Signing Azure`).
