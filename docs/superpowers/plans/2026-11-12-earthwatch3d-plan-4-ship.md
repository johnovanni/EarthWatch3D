# EarthWatch3D Plan 4: Ship (Welcome, About, MSIX, signing, auto-update) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make EarthWatch3D installable by other people: a first-run Welcome window, an About window with every
required credit and licence, an app icon, and a self-contained x64 MSIX with an `.appinstaller` file for automatic
updates, signed with a local test certificate now and with the user's Azure Artifact Signing account once it exists.

**Architecture:**
- One version number, in `Directory.Build.props`, feeds `AppInfo.Version`, the MSIX `Identity` and the `.appinstaller`.
- When the app runs from its MSIX, its data folder is the package's own `LocalCache` folder, named explicitly, so every
  path the app shows (crash message, About) is the real one and uninstalling removes it.
- About and Welcome are ordinary dialogs behind `IDialogService`, with view models tested without a window, and a
  `--dialog` diagnostic option that renders a dialog to a PNG (no screen capture).
- `tools/build-release.ps1` does publish → stage (manifest, logos, `resources.pri`) → `makeappx pack` → sign
  (`-Signing Test` or `-Signing Azure`) → write the `.appinstaller`, then checks that the versions agree. The Windows
  SDK tools come from the pinned `Microsoft.Windows.SDK.BuildTools` NuGet package (the machine's SDK is 10.0.19041,
  whose `signtool` is too old for Artifact Signing).

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.4.0, xUnit 2.9.2, Windows SDK build tools (NuGet:
`Microsoft.Windows.SDK.BuildTools`: `makeappx`, `makepri`, `signtool`), Azure Artifact Signing client tools (NuGet;
formerly Trusted Signing), Python 3 + Pillow for the one-off icon generation (output committed).

**Spec:** `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md`. Read §1 (success criteria), §7 (Dialog windows:
About, Welcome; MVVM), §8 (Packaging and signing), §10 (manual checklist), §11. Plan 3's ledger
(`.superpowers/sdd/2026-11-12-earthwatch3d-plan-3-lightning-clouds-settings/progress.md`) stays where it is: MTG
(Plan 3 Tasks 1 and 7b) is still to come and is not part of this plan.

## Plan series

| Plan | Status |
|---|---|
| 1. Foundation | Done |
| 2. Events and time | Done |
| 3. Lightning, clouds, data use, Settings, km/miles | Done except MTG (waits on the user's EUMETSAT account) |
| **4. Ship (this plan)** | |

## Global Constraints

- Everything in Plans 2 and 3's Global Constraints still holds (frameworks, 0 warnings, UTC, `AppPaths`, `Network`
  test trait, no PowerShell screen capture, launch the app as few times as possible and only via `--snapshot` or
  `--dialog` for visual checks; no user data in any request; User-Agent `AppInfo.UserAgent`).
- Branch `plan-4-ship` (created from main 6675464; its first commit is this plan). Commits end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. **Never push, never create a GitHub release, never
  upload anything.** Publishing is the user's decision, made after this plan.
- **No secrets and no certificates in the repo.** Git-ignored: `artifacts/` (already), `*.pfx` (already),
  `packaging/metadata.json`, `packaging/signing.local.json`. The test certificate lives only in the user's
  `Cert:\CurrentUser\My` store; signing uses its thumbprint (`signtool /sha1`), so no PFX file and no password exist.
- **The user's machine settings are the user's.** Trusting the test certificate (importing it into
  `Cert:\LocalMachine\TrustedPeople`, an administrator action) and installing, updating or uninstalling the MSIX are
  done by the user, from exact commands in `docs/release.md`. The implementer never runs them.
- Version: `1.0.0` (three parts) in `Directory.Build.props`; the MSIX and `.appinstaller` version is the same plus
  `.0` (`1.0.0.0`).
- Package identity: `Name` = `johnovanni.EarthWatch3D`, `ProcessorArchitecture` = `x64`, application `Id` =
  `EarthWatch3D`, `Executable` = `EarthWatch3D.App.exe` (the assembly name is unchanged: pack URIs use
  `/EarthWatch3D.App;component/`), `EntryPoint` = `Windows.FullTrustApplication`, display name `EarthWatch3D`.
  `TargetDeviceFamily Windows.Desktop`, `MinVersion` `10.0.17763.0` (spec: Windows 10 1809), `MaxVersionTested`
  `10.0.26100.0`. Capabilities: `internetClient`, `rescap:runFullTrust`.
- Publisher: test builds use `CN=EarthWatch3D Test Signing` and display name `EarthWatch3D test build`; Azure builds
  read both from `packaging/signing.local.json` (`{ "publisher": "CN=…", "publisherDisplayName": "…" }`), which must
  equal the Artifact Signing certificate's subject exactly. **A test build and an Azure build are different apps to
  Windows** (the Publisher is part of the identity): the test install must be uninstalled before the signed one.
- Release file names never carry the version: `EarthWatch3D.msix` and `EarthWatch3D.appinstaller`, so the GitHub
  "latest release" URLs stay stable: base URI `https://github.com/johnovanni/EarthWatch3D/releases/latest/download/`.
- `.appinstaller` update settings: schema `http://schemas.microsoft.com/appx/appinstaller/2018`,
  `<OnLaunch HoursBetweenUpdateChecks="24" ShowPrompt="true" UpdateBlocksActivation="false"/>` and
  `<AutomaticBackgroundTask/>`.
- `LightningLayer.MtgAvailable` (const, false until MTG ships) decides every MTG/EUMETSAT wording in the new windows,
  as it already does in the legend and Settings, so turning MTG on stays a one-line change.
- New settings key: `WelcomeShown` (bool, default false). New `--layers`-style diagnostic: `--dialog about|welcome`.
- Dialog look: the Settings window's (`Background="#101624"`, `CardStyles.xaml`, white text, `CardButton`), and every
  text element must pass `CardReadabilityTests`' light-text rule.

## Review Focus

1. **The Welcome window must never block a diagnostic or offline run** (`--snapshot`, `--bench`, `--offline-samples`,
   `--dialog`): offline runs use a fresh temporary settings file, so `WelcomeShown` is always false there. A blocked
   modal would hang every snapshot. Pinned in Task 4 (`NotShownInDiagnosticOrOfflineRuns`).
2. **Packaged vs. unpackaged data folder.** Packaged, the app must use and show
   `%LOCALAPPDATA%\Packages\<family name>\LocalCache\Local\EarthWatch3D`; unpackaged, `%LOCALAPPDATA%\EarthWatch3D`
   as today. Pinned in Task 1 (`PackagedRootIsTheLocalCacheFolder`, `UnpackagedRootIsUnchanged`).
3. **The three version numbers disagree** (exe, MSIX `Identity`, `.appinstaller` `Version`/`MainPackage`), which makes
   updates silently never apply. Pinned by Task 5's self-check in `build-release.ps1` (the script fails) and
   `PackagingTemplateTests`.
4. **A link that cannot open** (no default browser, a policy block): `Process.Start` throws `Win32Exception`. About
   and Welcome must show a message, not reach the crash handler. Pinned in Task 3 (`LinkThatFailsToOpenShowsAMessage`).
5. **An update keeps the user's data**: settings, the encrypted EUMETSAT key, the cache and the download meter
   survive an in-place update (the package folder persists across versions, DPAPI is per user). Not unit-testable;
   pinned as a step in Task 5's manual check list (`docs/manual-test.md`, "Update keeps settings").

---

### Task 1: One version number, and the packaged data folder

**Files:**
- Modify: `Directory.Build.props` (add `<Version>1.0.0</Version>` in an unconditioned `PropertyGroup`),
  `src/EarthWatch3D.Core/EarthWatch3D.Core.csproj` (remove its `<Version>0.1.0</Version>`)
- Create: `src/EarthWatch3D.Core/Storage/PackageIdentity.cs`
- Modify: `src/EarthWatch3D.Core/Storage/AppPaths.cs`, `src/EarthWatch3D.Core/AppInfo.cs`
- Test: `tests/EarthWatch3D.Tests/AppInfoTests.cs`, `tests/EarthWatch3D.Tests/StorageTests.cs`

**Interfaces:**
- Produces: `AppInfo.MsixVersion` (`string`, e.g. `"1.0.0.0"`); `PackageIdentity.FamilyName` (`string?`, null when
  not running from a package); `AppPaths.ForUser(string localAppData, string? packageFamilyName)`;
  `AppPaths.Default` now = `ForUser(<LocalApplicationData>, PackageIdentity.FamilyName)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void VersionIsThreePartsAndMsixVersionAddsZero()
{
    Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    Assert.Equal(AppInfo.Version + ".0", AppInfo.MsixVersion);
}
[Fact] public void AppAndCoreShareTheVersion() =>   // the App assembly's informational version, without "+commit"
    Assert.Equal(AppInfo.Version, VersionOf(typeof(EarthWatch3D.App.App).Assembly));
[Fact] public void UnpackagedRootIsUnchanged() =>
    Assert.Equal(@"C:\L\EarthWatch3D", AppPaths.ForUser(@"C:\L", null).Root);
[Fact] public void PackagedRootIsTheLocalCacheFolder() =>
    Assert.Equal(@"C:\L\Packages\johnovanni.EarthWatch3D_abc123\LocalCache\Local\EarthWatch3D",
                 AppPaths.ForUser(@"C:\L", "johnovanni.EarthWatch3D_abc123").Root);
[Fact] public void TestsDoNotRunFromAPackage() => Assert.Null(PackageIdentity.FamilyName);
```

- [ ] **Step 2: Run them; they fail** — `dotnet test --filter "FullyQualifiedName~AppInfoTests|FullyQualifiedName~StorageTests"`.

- [ ] **Step 3: Implement.** `PackageIdentity.FamilyName`: P/Invoke `kernel32!GetCurrentPackageFamilyName(ref uint
  length, char[]? name)`; error 15700 (`APPMODEL_ERROR_NO_PACKAGE`) → null; call once with a null buffer to get the
  length (`ERROR_INSUFFICIENT_BUFFER`, 122), then again; cache the result in a `Lazy<string?>`; any other error or a
  non-Windows OS → null. `AppPaths.ForUser`: packaged →
  `<localAppData>\Packages\<family>\LocalCache\Local\EarthWatch3D` (the folder Windows would redirect
  `%LOCALAPPDATA%` writes to; naming it directly makes the paths the app shows real ones). Leave
  `AppInfo.Version` as it is (it reads the Core assembly, which now gets 1.0.0 from `Directory.Build.props`).

- [ ] **Step 4: Run all tests** — `dotnet test --filter Category!=Perf`. Expected: all pass, 0 warnings in
  `dotnet build -c Release`.

- [ ] **Step 5: Commit** — "One version (1.0.0) for every project; packaged data folder in LocalCache".

### Task 2: App icon and MSIX logos

**Files:**
- Create: `tools/make-icons.py` (Python 3 + Pillow; run once, output committed), `src/EarthWatch3D.App/app.ico`,
  `packaging/Images/*.png` (list below)
- Modify: `src/EarthWatch3D.App/EarthWatch3D.App.csproj` (`<ApplicationIcon>app.ico</ApplicationIcon>`, and
  `<Resource Include="app.ico" />`), `src/EarthWatch3D.App/MainWindow.xaml` (`Icon="/EarthWatch3D.App;component/app.ico"`)
- Test: `tests/EarthWatch3D.Tests/PackagingTemplateTests.cs` (new; the logo half here, the manifest half in Task 5)

**Interfaces:**
- Produces: the image files Task 5's manifest names: `StoreLogo`, `Square44x44Logo`, `Square150x150Logo`,
  `Wide310x150Logo`, each as `.scale-100.png` and `.scale-200.png`, plus
  `Square44x44Logo.targetsize-{16,24,32,48,256}.png` and `…targetsize-{16,24,32,48,256}_altform-unplated.png`.

- [ ] **Step 1: Write the failing test** `EveryLogoExistsAtItsSize`: for each file above, it exists under
  `packaging/Images` and its pixel size is the expected one (StoreLogo 50/100; Square44x44 44/88; Square150x150
  150/300; Wide310x150 310×150/620×300; targetsize-N N×N). Read sizes with `BitmapDecoder` (WPF). Also
  `AppIconHasTheStandardSizes`: `app.ico` holds 16, 24, 32, 48, 64 and 256 px frames (`IconBitmapDecoder`).

- [ ] **Step 2: Run it; it fails.**

- [ ] **Step 3: Write `tools/make-icons.py` and run it** (`python tools/make-icons.py`, not `-I`: Pillow must import).
  The design (a ruling; the user may ask for another): an orthographic globe rendered from
  `src/EarthWatch3D.App/Assets/bluemarble-8192.jpg`, centred on 15° N, 20° W, with the app's night shading (the same
  multipliers as spec §6: R 0.32, G 0.36, B 0.50, smoothstep over 0° to −12°) for a Sun over 10° S, 60° E, a thin gold
  terminator line (255, 200, 120), anti-aliased edge, transparent background. Render once at 1024 px and downscale
  with Lanczos for each size; the wide tile puts the 150-px globe at the left of a transparent 310×150. Look at the
  16 px and 256 px results yourself (Read the PNGs) and say in the report whether the 16 px one still reads as a globe.

- [ ] **Step 4: Run all tests; build Release (0 warnings).**

- [ ] **Step 5: Commit** — "App icon and MSIX logos (generated by tools/make-icons.py)".

### Task 3: About window (and the `--dialog` snapshot option)

**Files:**
- Create: `src/EarthWatch3D.App/ViewModels/AboutViewModel.cs`, `src/EarthWatch3D.App/Views/AboutWindow.xaml(.cs)`,
  `src/EarthWatch3D.App/Services/ILinkOpener.cs` (`ShellLinkOpener` implementation in the same file),
  `THIRD-PARTY-NOTICES.txt` (repo root)
- Modify: `IDialogService.cs`, `DialogService.cs`, `MainViewModel.cs` (`OpenAboutCommand`),
  `Views/LayerStrip.xaml` (an About button under Settings, same `StripCommandButton` style and click-unchecks
  pattern, `Tag="ⓘ"`, tooltip "About EarthWatch3D: version, credits and licences"),
  `EarthWatch3D.App.csproj` (copy `THIRD-PARTY-NOTICES.txt` to the output), `Diagnostics/LaunchOptions.cs`,
  `Diagnostics/SnapshotDiagnostic.cs`, `App.xaml.cs`
- Test: `tests/EarthWatch3D.Tests/AboutViewModelTests.cs`, `CardReadabilityTests.cs`, `LaunchOptionsTests.cs`

**Interfaces:**
- Consumes: `AppInfo.Version`, `AppPaths` (Task 1), `LightningLayer.MtgAvailable`.
- Produces: `IDialogService.ShowAbout()`; `ILinkOpener { void Open(string target); }` (throws on failure);
  `AboutViewModel(ILinkOpener opener, string dataFolder, bool mtgAvailable = LightningLayer.MtgAvailable)` with
  `Version` (`"Version 1.0.0"`), `Credits` (`IReadOnlyList<Credit>`), `Licences` (`IReadOnlyList<Credit>`),
  `DataFolder`, `Message` (string?, shown under the lists), `OpenLinkCommand(string)`, `OpenDataFolderCommand`,
  `OpenNoticesCommand`; `record Credit(string Name, string Text, string Url)`;
  `LaunchOptions.Dialog` (`"about"` or `"welcome"`, null otherwise).

- [ ] **Step 1: Write the failing tests** (`AboutViewModelTests`):
  - `CreditsNameEveryRequiredSource`: the joined `Name + Text` of `Credits` contains each of: "USGS",
    "National Hurricane Center", "National Weather Service", "GOES", "GDACS", "EONET", "GIBS", "Blue Marble",
    "Natural Earth", "Peter Bird (2003), PB2002; GIS conversion by Hugo Ahlenius, Nordpil", "ODC-By 1.0".
  - `EumetsatCreditOnlyWhenMtgIsAvailable`: with `mtgAvailable: false` no credit contains "EUMETSAT"; with `true`
    one contains exactly "Contains EUMETSAT data".
  - `LicencesListTheShippedLibraries`: names HelixToolkit.Wpf, CommunityToolkit.Mvvm, PureHDF and .NET, each with
    Text "MIT licence".
  - `ShowsTheVersion`: `Version == "Version " + AppInfo.Version`.
  - `OpenLinkPassesTheUrl`: a recording opener receives the URL given.
  - `LinkThatFailsToOpenShowsAMessage`: an opener that throws `Win32Exception` → no exception escapes, `Message`
    starts with "Could not open" and contains the URL (so the user can copy it).
  - `LaunchOptionsTests`: `--dialog about` → `Dialog == "about"`; `--dialog nope` → `Error` names the allowed values.
  - `CardReadabilityTests`: an `AboutWindow` built on a fake opener (STA, as the existing card tests) has only light
    text (reuse the existing helper).

- [ ] **Step 2: Run them; they fail.**

- [ ] **Step 3: Implement.**
  - The credits, in this order (Name — Text — Url): USGS — "Earthquakes: U.S. Geological Survey real-time feeds" —
    `https://earthquake.usgs.gov/earthquakes/feed/`; NOAA National Hurricane Center — "Tropical cyclones (Atlantic
    and East/Central Pacific)" — `https://www.nhc.noaa.gov/`; GDACS — "Tropical cyclones worldwide: Global Disaster
    Alert and Coordination System (European Commission and United Nations)" — `https://www.gdacs.org/`; NOAA National
    Weather Service — "US weather alerts and forecast zones" — `https://www.weather.gov/`; NASA EONET — "Severe
    storms: Earth Observatory Natural Event Tracker" — `https://eonet.gsfc.nasa.gov/`; NOAA GOES GLM — "Lightning:
    GOES-19 and GOES-18 Geostationary Lightning Mapper, NOAA" — `https://registry.opendata.aws/noaa-goes/`;
    (when `mtgAvailable`) EUMETSAT — "Lightning over Europe and Africa: Contains EUMETSAT data" —
    `https://www.eumetsat.int/`; NASA GIBS — "Clouds: VIIRS NOAA-21 true colour, NASA Global Imagery Browse
    Services" — `https://www.earthdata.nasa.gov/engage/open-data-services-software/earthdata-developer-portal/gibs-api`;
    NASA Blue Marble — "Globe imagery: NASA Earth Observatory, Blue Marble Next Generation (R. Stöckli, 2004)" —
    `https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/`; Natural Earth — "Coastlines and
    borders (public domain)" — `https://www.naturalearthdata.com/`; Plate boundaries — "Peter Bird (2003), PB2002;
    GIS conversion by Hugo Ahlenius, Nordpil. ODC-By 1.0" — `https://github.com/fraxen/tectonicplates`.
    Check each URL once with a HEAD/GET (a `Network`-trait test `CreditLinksAnswer` that accepts any 2xx/3xx) and
    replace a dead one with the source's current page.
  - Licences: HelixToolkit.Wpf `http://www.helix-toolkit.org/`, CommunityToolkit.Mvvm
    `https://github.com/CommunityToolkit/dotnet`, PureHDF `https://apollo3zehn.github.io/PureHDF/`, .NET
    `https://github.com/dotnet/runtime` — all "MIT licence".
  - `THIRD-PARTY-NOTICES.txt`: the full MIT text with each project's own copyright line, copied from the package's
    licence file in `%USERPROFILE%\.nuget\packages` (HelixToolkit's `LICENSE`) or the project's repository
    (CommunityToolkit, PureHDF, .NET runtime `LICENSE.TXT`), plus the ODC-By 1.0 notice for PB2002 and the NASA /
    Natural Earth statements from `src/EarthWatch3D.App/Assets/README.md`. The MIT licence requires the notice to ship
    with the copies, which is why this file is packaged and opened from About.
  - The window: title "About EarthWatch3D", width 480, `SizeToContent="Height"`, the version, one line "Live
    earthquakes, storms and lightning on a 3D globe.", the line "Shown for interest, not for safety decisions:
    follow your local weather service and authorities.", the credits (name bold, text, the name is a `Hyperlink`
    bound to `OpenLinkCommand`), the licences, "Data and logs: <folder>" with an Open button, "Licence texts" button,
    Close. `ShellLinkOpener` uses `Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })`.
  - `--dialog about` (with `--snapshot <png>`): once the main window is shown, open the About window non-modally,
    wait for it to render, save it with the existing `SnapshotDiagnostic.Save(window, path)`, exit. Without
    `--snapshot` it only opens the dialog. Add it to the README Diagnostics list.

- [ ] **Step 4: Run all tests; build Release; one snapshot**:
  `src\EarthWatch3D.App\bin\Release\net10.0-windows\EarthWatch3D.App.exe --offline-samples tests\Samples --dialog about --snapshot <scratch>\about.png`.
  Look at the PNG: readable, nothing clipped, the ⓘ glyph renders (if it shows as a box, use "i" in a circle drawn
  with the button's existing style and say so).

- [ ] **Step 5: Commit** — "About window: version, every credit and licence; --dialog snapshots".

### Task 4: Welcome window (first run only)

**Files:**
- Create: `src/EarthWatch3D.App/ViewModels/WelcomeViewModel.cs`, `src/EarthWatch3D.App/Views/WelcomeWindow.xaml(.cs)`
- Modify: `AppSettings.cs` (`WelcomeShown`), `IDialogService.cs`/`DialogService.cs` (`ShowWelcome()`),
  `App.xaml.cs` (show it once the main window has loaded), `SnapshotDiagnostic.cs` (`--dialog welcome`),
  `Diagnostics/LaunchOptions.cs` (`Bench`, `IsDiagnosticRun`)
- Test: `tests/EarthWatch3D.Tests/WelcomeViewModelTests.cs`, `CardReadabilityTests.cs`, `LaunchOptionsTests.cs`

**Interfaces:**
- Consumes: `IDialogService.ShowSettings()`, `ISettingsStore`, `LaunchOptions` (Task 3), `ILinkOpener` (Task 3).
- Produces: `WelcomeViewModel(ISettingsStore store, IDialogService dialogs, ILinkOpener opener, bool mtgAvailable =
  LightningLayer.MtgAvailable)` with `KeyText` (string), `OpenSettingsCommand`, `StartCommand`,
  `event EventHandler? RequestClose`, `Close()` (marks shown; the window calls it from `Closed`, so ✕ counts);
  `static bool WelcomeViewModel.ShouldShow(AppSettings s, LaunchOptions o)`;
  `LaunchOptions.IsDiagnosticRun` (true with `--snapshot`, `--face-sun`, `--look-at`, `--offline-samples`,
  `--dialog` or `--bench`; `Parse` now recognises `--bench` as a flag, `Bench`, which PerfRun still reads itself).

- [ ] **Step 1: Write the failing tests**
  - `ShownOnlyOnFirstRun`: `ShouldShow` is true for defaults and no options, false when `WelcomeShown`.
  - `NotShownInDiagnosticOrOfflineRuns`: false for each of `--snapshot x.png`, `--offline-samples d`, `--bench`,
    `--dialog about`, `--look-at 0,0`; `--dialog welcome` is handled by the diagnostic, not by `ShouldShow`.
  - `ClosingMarksItShownAndKeepsOtherSettings`: start with `Units = Miles`, `Close()` → store holds
    `WelcomeShown = true` and `Units = Miles`. Closing with the window's ✕ (no button) counts the same.
  - `OpenSettingsClosesWelcomeThenOpensSettings`: a recording dialog service sees `ShowSettings` once, after the
    view model raised `RequestClose`.
  - `KeyTextFollowsMtgAvailability`: false → contains "coming in a later version" and not "now"; true → says the key
    adds lightning over Europe and Africa.
  - Readability: the `WelcomeWindow` passes the light-text rule.

- [ ] **Step 2: Run them; they fail.**

- [ ] **Step 3: Implement.** Title "Welcome to EarthWatch3D", width 520. Content, in plain words for a
  non-technical user, one short paragraph or list each:
  1. What it shows: live earthquakes, tropical cyclones, US warnings, NASA storm reports and lightning on a 3D globe,
     with the Sun and Moon.
  2. How to move: drag to turn, wheel to zoom, double-click to centre, Home to reset; click a marker for details.
  3. The layer strip at the left turns layers on and off; hover a button for its key.
  4. The time bar replays the last 7 days (lightning is live only).
  5. Data use: about 80 MB an hour with lightning on, a few MB a day without; Settings can set a daily limit.
  6. `KeyText`: MtgAvailable false → "A free EUMETSAT key will add lightning over Europe and Africa in a coming
     version. You can store it in Settings now."; true → "Lightning covers the Americas. A free EUMETSAT key, entered in
     Settings, adds Europe and Africa."
  7. "Shown for interest, not for safety decisions."
  Buttons: "Open Settings" and "Start" (default). `App.xaml.cs` calls `dialogs.ShowWelcome()` from the main window's
  `ContentRendered` (once) when `ShouldShow` is true, so the globe is already up behind it and the feeds have started.

- [ ] **Step 4: Run all tests; build Release; one snapshot** with `--dialog welcome --snapshot <scratch>\welcome.png
  --offline-samples tests\Samples`; look at it.

- [ ] **Step 5: Commit** — "Welcome window on first run (skipped in diagnostic and offline runs)".

### Task 5: MSIX package, test signing and the .appinstaller

**Files:**
- Create: `packaging/AppxManifest.xml` (template), `packaging/EarthWatch3D.appinstaller` (template),
  `packaging/priconfig.xml`, `packaging/tools.proj` (restores the pinned tool packages only; builds nothing),
  `tools/build-release.ps1`, `docs/release.md`
- Modify: `.gitignore` (`packaging/metadata.json`, `packaging/signing.local.json`),
  `tests/EarthWatch3D.Tests/PackagingTemplateTests.cs`, `docs/manual-test.md`, `README.md` (Install section),
  `EarthWatch3D.sln` (do **not** add `tools.proj` to the solution)

**Interfaces:**
- Consumes: the logos (Task 2), `AppInfo.Version`/`MsixVersion` (Task 1).
- Produces: `tools/build-release.ps1 [-Signing Test|Azure|None] [-BaseUri <uri>] [-OutDir artifacts\release]`
  → `<OutDir>\EarthWatch3D.msix`, `<OutDir>\EarthWatch3D.appinstaller` and, for Test, `<OutDir>\EarthWatch3D-test.cer`.
  Template placeholders: `{Version}`, `{Publisher}`, `{PublisherDisplayName}`, `{AppInstallerUri}`, `{MsixUri}`.

- [ ] **Step 1: Write the failing tests** (`PackagingTemplateTests`, reading the files in `packaging/`):
  - `ManifestHasTheIdentityAndRequirements`: `Identity Name="johnovanni.EarthWatch3D"`, `Version="{Version}"`,
    `Publisher="{Publisher}"`, `ProcessorArchitecture="x64"`; `TargetDeviceFamily` `Windows.Desktop`, `10.0.17763.0`;
    `Application Id="EarthWatch3D" Executable="EarthWatch3D.App.exe" EntryPoint="Windows.FullTrustApplication"`;
    capabilities `internetClient` and `rescap:runFullTrust`.
  - `ManifestLogosExist`: every `*Logo` attribute names a file whose `.scale-100.png` exists in `packaging/Images`.
  - `ExecutableMatchesTheAssemblyName`: `Executable` = `<AssemblyName of EarthWatch3D.App>.exe`.
  - `AppInstallerUsesStableNamesAndUpdateSettings`: schema 2018 namespace; `MainPackage Name` equals the manifest's
    `Identity Name`; `Uri` attributes are `{AppInstallerUri}` / `{MsixUri}`; `OnLaunch HoursBetweenUpdateChecks="24"
    ShowPrompt="true" UpdateBlocksActivation="false"`; `AutomaticBackgroundTask` present.
  - `SigningFilesAreIgnored`: `git check-ignore` (or a parse of `.gitignore`) covers `packaging/metadata.json`,
    `packaging/signing.local.json`, `artifacts/`, `*.pfx`.

- [ ] **Step 2: Run them; they fail.**

- [ ] **Step 3: Implement the templates and `tools.proj`.** `tools.proj`: an SDK project
  (`Microsoft.Build.NoTargets` is not needed: `<Project Sdk="Microsoft.NET.Sdk">` with `net10.0` and
  `<IncludeBuildOutput>false</IncludeBuildOutput>`) holding `PackageReference`s with **exact** versions:
  `Microsoft.Windows.SDK.BuildTools` (the newest stable 10.0.26100.x on nuget.org) and the Artifact Signing client
  package. **First check its current name on Microsoft Learn** ("Set up signing integrations to use Artifact
  Signing"): it was `Microsoft.Trusted.Signing.Client`; the service was renamed Artifact Signing. Record the names and
  versions chosen in the report and in `docs/release.md`. `dotnet restore packaging/tools.proj` puts them in the NuGet
  cache; the script finds `makeappx.exe`, `makepri.exe`, `signtool.exe` (x64) and `Azure.CodeSigning.Dlib.dll`
  there (via `dotnet nuget locals global-packages --list`).

- [ ] **Step 4: Implement `tools/build-release.ps1`** (PowerShell 7; `Set-StrictMode -Version Latest`,
  `$ErrorActionPreference = 'Stop'`; **no `Add-Type`, no screen capture**):
  1. Read the version from `Directory.Build.props`; `$msixVersion = "$version.0"`.
  2. `dotnet restore packaging/tools.proj`; `dotnet publish src/EarthWatch3D.App -c Release -r win-x64
     --self-contained true -o artifacts/publish` (clean the folder first).
  3. Stage `artifacts/stage`: the publish output, `Images/`, and the filled manifest. Publisher from the signing
     mode (Test/None: the test values; Azure: `packaging/signing.local.json`, failing with a clear message if missing).
  4. `makepri createconfig /cf <stage>\priconfig.xml /dq en-US /o` (or the committed `priconfig.xml`), then
     `makepri new /pr <stage> /cf priconfig.xml /mn <stage>\AppxManifest.xml /of <stage>\resources.pri /o`.
  5. `makeappx pack /d <stage> /p <OutDir>\EarthWatch3D.msix /o`.
  6. Sign: **Test** — find or create (`New-SelfSignedCertificate -Type Custom -Subject "CN=EarthWatch3D Test
     Signing" -KeyUsage DigitalSignature -FriendlyName "EarthWatch3D test signing" -CertStoreLocation
     Cert:\CurrentUser\My -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")`), export the
     public `.cer` to `<OutDir>`, `signtool sign /fd SHA256 /sha1 <thumbprint> <msix>`; **Azure** — Task 6;
     **None** — leave unsigned (for template checks only; cannot be installed).
  7. Fill the `.appinstaller` (`{AppInstallerUri}` = BaseUri + `EarthWatch3D.appinstaller`, `{MsixUri}` = BaseUri +
     `EarthWatch3D.msix`; default BaseUri as in Global Constraints).
  8. **Self-check** (Review Focus 3): `makeappx unpack` the MSIX to a temp folder and compare its manifest `Version`
     and `Publisher` with the `.appinstaller`'s `Version`, `MainPackage Version` and `Publisher`, and the exe's
     `ProductVersion` (without `+commit`) with `$version`; any mismatch fails the script. For Test/Azure, `signtool
     verify /pa` must report the signature (with the untrusted test certificate, accept the "not trusted" chain
     error but require the signer subject to be the Publisher).
  9. Print the output paths and the package size.

- [ ] **Step 5: Run it** — `pwsh tools/build-release.ps1 -Signing Test` and, for local update testing,
  `pwsh tools/build-release.ps1 -Signing Test -BaseUri "file:///C:/repos/EarthWatch3D/artifacts/release/"`. Report
  the MSIX size, the build time and the self-check output. Do not install it.

- [ ] **Step 6: Write `docs/release.md` and the manual checks.** `docs/release.md`: prerequisites (the tool
  packages restore themselves; PowerShell 7); building a test package; **the user's one-time step to trust the test
  certificate** (elevated: `Import-Certificate -FilePath artifacts\release\EarthWatch3D-test.cer
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople`) and how to remove it again
  (`Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=EarthWatch3D Test Signing' |
  Remove-Item`); installing by opening the `.appinstaller` (double-click; the `ms-appinstaller:` web link is turned off
  by Windows by default, so the page links the file, not the protocol); bumping the version for an update; how a
  release is published (`gh release create v1.0.0 artifacts\release\EarthWatch3D.msix
  artifacts\release\EarthWatch3D.appinstaller`), marked **only when the user asks**; and an open item: whether App
  Installer follows GitHub's `releases/latest/download/` redirect is **not verified** (no source confirms it either
  way). It is checked at the first real release by installing from the published `.appinstaller`; if it fails, the
  fallback is to host the two files on GitHub Pages and build with that `-BaseUri`. `docs/manual-test.md`, section
  "MSIX": install from the `.appinstaller` (Start menu entry and icon; the app starts; the Welcome window shows once);
  data folder is under `%LOCALAPPDATA%\Packages\johnovanni.EarthWatch3D_…\LocalCache\Local\EarthWatch3D` (About shows
  it, Open opens it); **Update keeps settings**: set miles, a daily limit and a test EUMETSAT key, build 1.0.1 into
  the same file:/// folder, start the app, accept the update prompt, check the three settings and the event cache
  survived; uninstall from Settings → Apps leaves no `Packages\johnovanni.EarthWatch3D_*` folder; and "If the
  file:/// update source is refused, serve the folder with `python -m http.server 8080 --bind 127.0.0.1` and build with
  `-BaseUri http://127.0.0.1:8080/`". README: an "Install" section (download `EarthWatch3D.appinstaller` from the
  latest release and open it; updates arrive by themselves) and the Plan 4 row of "Coming in plans" removed.

- [ ] **Step 7: Run all tests; commit** — "MSIX packaging, test signing and the .appinstaller (tools/build-release.ps1)".

### Task 6: Azure Artifact Signing (built now, verified when the user's account exists)

**Files:**
- Create: `packaging/metadata.example.json`, `packaging/signing.local.example.json`
- Modify: `tools/build-release.ps1` (the Azure branch), `docs/release.md` (Azure section),
  spec `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md` (§1 table and §8: "Azure Artifact Signing
  (formerly Trusted Signing)"; the Plan 4 rulings table)

**Interfaces:**
- Consumes: `build-release.ps1` (Task 5).
- Produces: `-Signing Azure` works once `packaging/metadata.json` and `packaging/signing.local.json` exist and the
  user has signed in with `az login`.

- [ ] **Step 1: Check the current Microsoft Learn instructions** for SignTool with Artifact Signing (the dlib
  package, the `metadata.json` fields — `Endpoint`, `CodeSigningAccountName`, `CertificateProfileName`, optionally
  `CorrelationId` — the regional endpoint list, the timestamp URL `http://timestamp.acs.microsoft.com`, the minimum
  SignTool version, and how the certificate subject is shown in the portal). Record what you found, with the page URLs,
  in `docs/release.md`.

- [ ] **Step 2: Implement the Azure branch**: refuse with a clear message when `metadata.json` or
  `signing.local.json` is missing or `az account show` fails ("Run az login first"); then `signtool sign /v /debug
  /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib <Azure.CodeSigning.Dlib.dll> /dmdf
  packaging\metadata.json <msix>`; the self-check (Task 5 step 8) then requires `signtool verify /pa` to pass with a
  trusted chain. `metadata.example.json` holds placeholder values only (`"https://eus.codesigning.azure.net"`,
  `"<your account name>"`, `"<your certificate profile>"`).

- [ ] **Step 3: Test what can be tested without an account**: `-Signing Azure` with no `metadata.json` fails with the
  message and leaves no signed file; with an example-only `metadata.json` copied to a temp folder (pass a
  `-MetadataPath` parameter for this) and no `az login`, it fails at the sign-in check. Never put real values in the
  repo or the report.

- [ ] **Step 4: Spec updates**: §8 names Artifact Signing (formerly Trusted Signing) and the publisher source
  (`signing.local.json`); add "Rulings made while building Plan 4" with: the data folder in `LocalCache` (Task 1),
  the generated icon (Task 2), the pinned NuGet SDK tools instead of the machine SDK (Task 5), the test certificate
  in the user store signed by thumbprint (Task 5), release file names without a version (Task 5), version 1.0.0, and
  the EUMETSAT credit in About shown only while `MtgAvailable` (§7 lists it, but the app shows no EUMETSAT data until
  MTG ships; Task 3).

- [ ] **Step 5: Commit** — "Azure Artifact Signing in build-release.ps1 (verified once the account exists)".

- [ ] **Deferred step (after the user's Artifact Signing account and certificate profile exist; not part of this
  run):** the user fills `packaging/metadata.json` and `packaging/signing.local.json` locally and runs `az login`;
  run `pwsh tools/build-release.ps1 -Signing Azure`; check the self-check passes with a trusted chain; record the
  signer subject and timings in `docs/release.md`.

---

## Execution notes

- Tasks 1–6 need nothing from outside. Only Task 6's deferred step waits on the user's Azure account, and installing
  the test package waits on the user trusting the test certificate (their action).
- Order: 1 → 2 → 3 → 4 → 5 → 6. Task 2 before 5 (logos), 3 before 4 (`--dialog`, `ILinkOpener`).
- Launches of the app: one per dialog snapshot (Tasks 3 and 4), nothing else. The MSIX is built but never installed
  by the implementer.
