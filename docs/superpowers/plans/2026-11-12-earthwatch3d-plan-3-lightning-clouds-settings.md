# EarthWatch3D Plan 3: Lightning, Clouds, Data Use and Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add live lightning (NOAA GOES GLM, plus EUMETSAT MTG with the user's own key), the optional live cloud
layer (NASA GIBS), a download meter with an optional daily limit, a Settings dialog (EUMETSAT key and secret
stored with DPAPI, daily limit, kilometres or miles), and units throughout the app.

**Architecture:**
- Lightning feeds (`GlmFeed` ×2, `MtgFeed`) are ordinary `IEventFeed`s on the existing `FeedScheduler`, so they
  get status dots and backoff for free. They write flashes into one thread-safe `LightningBuffer` (ring of the
  window, capped at 100,000). `LiveLightningSource : ILightningSource` (spec §9) exposes it with
  `SupportsHistory = false`.
- Feeds can now end a run with `Skipped` (not in view or not wanted), `NotSetUp` (no key) or `Paused` (daily limit)
  besides success and failure. Polling only what is in view uses a `ViewState` that `SceneController` updates.
- `DownloadMeter` counts bytes **on the wire** (FeedHttp decompresses itself), resets at local midnight and is
  saved across restarts.
- Clouds: one daily VIIRS image keyed against the Blue Marble base, fed into the composer's existing `Clouds` slot.
- Units: one `DistanceUnits` setting read by the formatting code; the view models re-format when it changes.

**Tech Stack:** .NET 10, WPF, HelixToolkit.Wpf 3.1.2 (`PointsVisual3D`), CommunityToolkit.Mvvm 8.4.0,
PureHDF 2.2.0 (NetCDF-4/HDF5), System.Security.Cryptography.ProtectedData (latest 10.0.x), System.IO.Compression,
xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md`. Read §5.6, §5.7, §5.8, §6 (Events: lightning),
§7 (layer strip, ⋯ Display, Settings, MVVM), §9, §10 and §11.
**Data facts:** `docs/data-sources.md` §6 (GLM), §7 (PureHDF), §8 (MTG, completed by Task 1), §9 (GIBS).

## Plan series

| Plan | Status |
|---|---|
| 1. Foundation | Done |
| 2. Events and time | Done (main, c3ebb9f) |
| **3. Lightning, clouds, data use, Settings dialog, km/miles (this plan)** | |
| 4. Ship (Welcome, About, MSIX, signing, auto-update) | After 3 |

## Global Constraints

- Everything in Plan 2's Global Constraints still holds (frameworks, 0 warnings, UTC, longitudes in (−180°, 180°],
  pixel mapping, `AppPaths`, retry delays 30 s → 1 → 2 → 5 → 10 min, `Network` test trait, no PowerShell screen
  capture, launch the app as few times as possible and only via `--snapshot` for visual checks).
- Branch `plan-3-lightning` (already created; its first commit is the I4 zone change). Commits end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. **Never push.**
- **The EUMETSAT key and secret are never built into the app, never logged, never put in a URL, never in chat,
  never committed.** In development they come from the git-ignored `tools/eumetsat.local.json`
  (`{ "consumerKey": "…", "consumerSecret": "…" }`); in the app from Settings, encrypted with DPAPI (CurrentUser).
  Tests use fake credentials and `SampleHttpHandler`.
- No user data in any request. User-Agent stays `AppInfo.UserAgent`.
- Lightning: GLM poll 20 s. GOES-19 (East) sub-satellite longitude −75.2°, GOES-18 (West) −137.2°, MTG 0.0°.
  Overlap boundaries −106.2° and −37.6° (spec §5.7). A satellite is polled only when the angle between its
  sub-satellite point and the camera's sub-point is below 100°. Window 5–60 min, default 15 (choices 5, 10, 15,
  30, 60). Buffer cap 100,000 flashes, oldest dropped first. Lightning is **live only**: hidden whenever the display
  is not live, because `SupportsHistory` is false.
- Clouds: `VIIRS_NOAA21_CorrectedReflectance_TrueColor`, previous UTC day, one 4096×2048 JPEG GetMap (template in
  data-sources §9), checked hourly, cached on disk, off by default.
- Download meter: bytes on the wire for every request, reset at local midnight, shown as "Today: 123 MB". The daily
  limit is off by default; when reached, only lightning pauses.
- New defaults: lightning layer on, clouds off, lightning window 15 min, units kilometres, daily limit off.
- New settings keys (in `AppSettings`, missing keys take the defaults): `ShowLightning`, `ShowClouds`,
  `LightningWindowMinutes`, `Units`, `DailyLimitMb` (null = off), `EumetsatKeyProtected`,
  `EumetsatSecretProtected` (base64 DPAPI blobs, null = not set).
- New `AppPaths`: `CloudsDir` = `cache\clouds`, `DownloadMeterFile` = `cache\download-meter.json`.
- New `--layers` names: `lightning`, `clouds`.

## Review Focus

1. **Coming back after a while** (laptop sleep, a satellite out of view for an hour, the app started fresh).
   Catch-up must fetch only files inside the lightning window, newest first, at most 45 per run, never hundreds at
   once. Pinned in Task 6 (`CatchUpIsLimitedToTheWindowNewestFirst`).
2. **Hour, day and year rollover** of the GLM key prefix: 23:59:50 → 00:00:10, day-of-year 365 → 001, and day 366
   in a leap year. Pinned in Task 4 (`PrefixRollsOverAtMidnightNewYearAndLeapDay`) and Task 6
   (`ListsBothHoursWhenTheWindowCrossesTheHour`).
3. **A wrong, revoked or expired EUMETSAT key, or EUMETSAT down.** MTG goes red with "key rejected" (or amber and
   red with the error), GOES keeps running, nothing crashes; an expired token is renewed once, not in a loop.
   Pinned in Task 7 (`RejectedKeyFailsWithKeyRejected`, `ExpiredTokenIsRenewedOnce`) and Task 7
   (`NoKeyIsNotSetUp`).
4. **The daily limit reached, then local midnight** (including a DST change day). Lightning pauses with a reason,
   other feeds continue, and lightning resumes after local midnight. Pinned in Task 2
   (`ResetsAtLocalMidnightIncludingDstDays`, `LimitReachedOnlyAtOrAboveTheLimit`) and Task 6
   (`PausedWhenTheLimitIsReached`).
5. **A truncated or corrupt NetCDF file** (download cut off, wrong content). That file is skipped and logged; the
   other files in the run still load. Pinned in Task 4 (`TruncatedFileThrowsFormatException`) and Task 6
   (`BadFileIsSkippedOthersLoad`).

---

### Task 1: Verify EUMETSAT MTG LI with the user's key (spike)

Needs `tools/eumetsat.local.json`. **If the file is not there when this task comes up, do Tasks 2–6 first and
return here before Task 7.** If any finding contradicts spec §5.7 (for example latency well over a few minutes, or
a product that is not NetCDF), update the spec and data-sources §8 and **stop for the user's review** before Task 7.

**Files:**
- Modify: `docs/data-sources.md` (§8: replace "deferred" with verified facts; summary table; "Spec impact" item 11)
- Modify: `tools/dump_flashes.py` (MTG defaults: the real variable names, remove "UNTESTED")
- Modify: `tools/capture-samples.ps1` (optional `-Eumetsat` switch that reads the local credentials file)
- Create: `tests/Samples/mtg_lfl_sample.nc` (or the product's real file), `tests/Samples/mtg_lfl_sample.csv`,
  `tests/Samples/mtg_search.json`, `tests/Samples/eumetsat_token.json` (with the token value replaced by `"REDACTED"`)
- Modify: `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md` §5.7 only if a finding requires it

- [ ] **Step 1:** Get a token: POST `https://api.eumetsat.int/token`, HTTP basic auth `key:secret`, form body
  `grant_type=client_credentials`. Record the response shape (`access_token`, `expires_in`, `token_type`) and
  lifetime. Never print the key, secret or token.
- [ ] **Step 2:** Find the Data Store search endpoint for collection `EO:EUM:DAT:0691` and record: the URL, the
  parameters for "products with sensing start after T" (`dtstart`, `dtend`, `sort`, page size), the response
  format, and how to get each product's download URL.
- [ ] **Step 3:** Download the newest product. Record: zip or single file, the file(s) inside, size, products per
  hour (cadence), and data per hour.
- [ ] **Step 4:** Record latency over at least 30 minutes: search-visible time minus sensing end time (median, min,
  max). If the median is over 5 minutes, note that the legend must show the newest MTG flash age (already planned
  in Task 8) and nothing may say "live" for MTG.
- [ ] **Step 5:** Record the layout: groups, the variables for flash latitude, longitude, time and energy/radiance,
  their types, packing (`scale_factor`, `add_offset`, `_Unsigned`, `_FillValue`) and time units. Run
  `python tools/dump_flashes.py <file> tests/Samples/mtg_lfl_sample.csv --mtg --mtg-map …` with the real names and
  record the flash count.
- [ ] **Step 6:** Confirm PureHDF 2.2.0 reads those variables (a throwaway console project outside the repo, as in
  data-sources §7). Record any filter or group quirks.
- [ ] **Step 7:** Record the coverage edge (westmost longitude with flashes) and confirm the −37.6° rule leaves no
  gap with GOES-East.
- [ ] **Step 8:** Commit the docs, tool changes and samples (check `git diff --cached` contains no key, secret or
  token).

```bash
git add docs/data-sources.md tools/dump_flashes.py tools/capture-samples.ps1 tests/Samples/mtg_* tests/Samples/eumetsat_token.json
git commit -m "Spike: EUMETSAT MTG LI-2-LFL verified (token, search, layout, size, latency)"
```

---

### Task 2: Feed outcomes, wire-byte counting, dispatcher injection, DownloadMeter

The Plan 2 carry-overs that lightning needs first, plus the meter that counts what they download.

**Files:**
- Modify: `src/EarthWatch3D.Core/Feeds/IEventFeed.cs`, `FeedStatus.cs`, `FeedScheduler.cs`, `FeedHttp.cs`
- Modify: the five feeds (`Usgs`, `Gdacs`, `Nhc`, `Nws`, `Eonet`): `RunOnceAsync` returns `FeedRunResult.Ok`
- Create: `src/EarthWatch3D.Core/Feeds/DownloadMeter.cs`, `src/EarthWatch3D.App/Services/IDialogService.cs`
  (`public interface IDialogService { bool ShowSettings(); }`, implemented in Task 10)
- Modify: `src/EarthWatch3D.Core/Storage/AppPaths.cs`, `AppSettings.cs`
- Modify: `src/EarthWatch3D.App/ViewModels/MainViewModel.cs`, `StatusViewModel.cs`, `Views/StatusStrip.xaml`,
  `Views/TimeBar.xaml`, `Views/StatusStrip.xaml.cs` (`FeedStateBrushConverter`)
- Test: `tests/EarthWatch3D.Tests/FeedSchedulerTests.cs`, `FeedHttpTests.cs`, `DownloadMeterTests.cs`,
  `StatusViewModelTests.cs`

**Interfaces:**
- Produces:
  - `public enum FeedRunResult { Ok, Skipped, NotSetUp, Paused }`
  - `IEventFeed.RunOnceAsync(CancellationToken ct)` now returns `Task<FeedRunResult>`.
  - `FeedState` gains `Paused`: `{ NotStarted, Ok, Retrying, Failing, NotSetUp, Paused }`.
  - `FeedStatus` gains `string? Note = null` (last parameter, e.g. "Not in view", "Daily download limit reached").
  - `FeedHttp.BytesReceived` reports **wire** bytes. New overloads:
    `Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct, AuthenticationHeaderValue? auth)` and
    `Task<string> PostFormAsync(Uri u, IReadOnlyDictionary<string, string> form, AuthenticationHeaderValue? auth, CancellationToken ct)`.
    A 401 or 403 throws `FeedAuthException : HttpRequestException` (so MTG can say "key rejected").
  - `public sealed class DownloadMeter(IClock clock, TimeZoneInfo zone, string? file, IAppLog log)` with
    `void Add(long bytes)` (thread-safe), `long TodayBytes { get; }`, `long? LimitBytes { get; set; }`,
    `bool LimitReached { get; }`, `void Save()`, `static string Format(long bytes)`.
  - `MainViewModel(IClock clock, ISettingsStore settings, Dispatcher? dispatcher = null, IDialogService? dialogs = null)`
    (dialogs is used from Task 10; until then the parameter is stored and unused). `StatusViewModel.DownloadText`
    (observable string).
  - `AppPaths.CloudsDir`, `AppPaths.DownloadMeterFile`; `AppSettings.DailyLimitMb` (`int?`, default null).

- [ ] **Step 1: Write the failing scheduler tests** in `FeedSchedulerTests.cs`:
  - `SkippedKeepsStateAndLastSuccess`: a feed returns Ok then Skipped; the status stays `Ok` with the first run's
    `LastSuccessUtc`, and `NextRunUtc == now + Interval`.
  - `NotSetUpAndPausedMapToTheirStates`: results `NotSetUp` → `FeedState.NotSetUp`, `Paused` → `FeedState.Paused`
    with `Note == "Daily download limit reached"`; neither counts as a failure (`ConsecutiveFailures == 0`).
- [ ] **Step 2: Write the failing FeedHttp tests** in `FeedHttpTests.cs`:
  - `CountsCompressedBytesAndDecompresses`: a handler returns a gzip body (`Content-Encoding: gzip`) of a
    100,000-byte text; the returned bytes equal the text and the `BytesReceived` total equals the gzip length.
  - `SendsAcceptEncoding`: the request carries `Accept-Encoding: gzip, deflate`.
  - `UnauthorizedThrowsFeedAuthException`: 401 → `FeedAuthException`.
  - `PostFormSendsBasicAuthAndForm`: the handler sees method POST, `Authorization: Basic …`, and body
    `grant_type=client_credentials`.
- [ ] **Step 3: Write the failing meter tests** in `DownloadMeterTests.cs` (zone `Europe/Berlin`, a `TestClock`):
  - `AddsAcrossThreads`: 8 threads × 10,000 × `Add(10)` → `TodayBytes == 800_000`.
  - `ResetsAtLocalMidnightIncludingDstDays`: bytes added at 23:59 local on 2026-10-24 are gone at 00:00:01 local on
    2026-10-25 (the DST change day), and bytes added during 2026-10-25 survive until 00:00 on 2026-10-26.
  - `LimitReachedOnlyAtOrAboveTheLimit`: limit 1,000,000; 999,999 bytes → false; 1,000,000 → true; limit null → false.
  - `SurvivesRestartOnTheSameDayOnly`: `Save()` then a new meter on the same file and same local day has the bytes;
    on the next local day it starts at 0; a corrupt file starts at 0 and logs a warning.
  - `FormatsToday`: `Format(400_000) == "Today: 0.4 MB"`, `Format(123_456_789) == "Today: 123 MB"`,
    `Format(1_234_000_000) == "Today: 1.23 GB"`.
- [ ] **Step 4: Run them** — `dotnet test -c Release --filter "FeedScheduler|FeedHttp|DownloadMeter"`. Expected:
  build errors for the new types and members.
- [ ] **Step 5: Implement.**
  - `FeedHttp`: the default handler gets `AutomaticDecompression = None`; send `Accept-Encoding: gzip, deflate`;
    count bytes as read from the response stream, then wrap it in `GZipStream`/`ZLibStream` per `Content-Encoding`.
    The 50 MB cap applies to the **decompressed** size (it protects memory).
  - Scheduler: map results as in the tests. `Skipped` keeps the previous `FeedStatus` and only moves `NextRunUtc`
    (raise `StatusChanged` only if the state changes).
  - Meter: Interlocked counter plus the local date it belongs to; `file` holds `{"localDate":"yyyy-MM-dd","bytes":n}`
    and is written with `AppPaths.TryWriteAllText`.
  - `MainViewModel`: use the injected dispatcher instead of `Application.Current?.Dispatcher`; `App.xaml.cs` passes
    `Dispatcher`.
  - `StatusViewModel`: `Paused` dots are blue `#0A84FF`; the tooltip adds `Note` on its own line. The time bar shows
    `DownloadText` after the dots (Consolas 12, `#C8CDD8`).
- [ ] **Step 6: Run** the filtered tests, then the full suite. Expected: all pass; build 0 warnings.
- [ ] **Step 7: Commit** — `git commit -m "Feeds: run results (skipped, not set up, paused), wire-byte counting, auth; DownloadMeter; injected dispatcher"`

---

### Task 3: Kilometres or miles throughout

**Files:**
- Create: `src/EarthWatch3D.Core/Geo/Units.cs`
- Modify: `src/EarthWatch3D.Core/Storage/AppSettings.cs`
- Modify: `src/EarthWatch3D.App/ViewModels/SelectionViewModel.cs`, `SunMoonViewModel.cs`, `MainViewModel.cs`
- Test: `tests/EarthWatch3D.Tests/UnitsTests.cs`, `SelectionViewModelTests.cs`, `SunMoonViewModelTests.cs`

**Interfaces:**
- Produces:
  - `public enum DistanceUnits { Kilometres, Miles }`; `AppSettings.Units` (default `Kilometres`).
  - `public static class Units` with `const double KmPerMile = 1.609344`,
    `string Distance(double km, DistanceUnits u, int digits = 0)` ("35 km" / "22 mi"),
    `string WindFromKnots(double kt, DistanceUnits u)` ("65 kt (120 km/h)" / "65 kt (75 mph)"),
    `string Large(double km, DistanceUnits u)` ("148.1 million km" / "92.0 million mi").
  - `MainViewModel.Units` (observable `DistanceUnits`, loaded from and saved to settings). Changing it re-runs
    `SunMoon.Refresh` and `Selection` (via its existing re-select).
  - `SelectionViewModel(Func<DateTime> nowUtc, Func<DistanceUnits>? units = null)`;
    `LinesFor(EarthEvent e, DateTime nowUtc, DistanceUnits units)` and `HoverText(EarthEvent e, DateTime nowUtc, DistanceUnits units)`.
  - `SunMoonViewModel.Units` property; distance texts follow it. The tooltips keep showing both units (Plan 2
    follow-up wording unchanged).

- [ ] **Step 1: Write the failing tests:**
  - `UnitsTests.FormatsBothSystems`: `Distance(35, Kilometres) == "35 km"`, `Distance(35, Miles) == "22 mi"`,
    `WindFromKnots(65, Kilometres) == "65 kt (120 km/h)"`, `WindFromKnots(65, Miles) == "65 kt (75 mph)"`,
    `Large(148_100_000, Miles) == "92.0 million mi"`.
  - `SelectionViewModelTests.QuakeLinesInMiles`: depth line is `("Depth", "22 mi")`; hover text
    `"M6.1 · 22 mi deep · 2 h ago"`. Same for a cyclone's wind line in mph. (Spec §10: each event type in km and
    in miles; extend the existing per-type tests with a Miles case each.)
  - `SunMoonViewModelTests.DistancesFollowUnits`: with `Units = Miles`, `MoonDistanceText` ends with `" mi"` and
    equals `Units.Distance(MoonDistanceKm, Miles)`; `SunDistanceText == Units.Large(SunDistanceKm, Miles)`.
  - `MainViewModel` test `UnitsAreSavedAndRestored`.
- [ ] **Step 2: Run** — expected build errors.
- [ ] **Step 3: Implement** — replace `SelectionViewModel.Km`/`Wind` and the Sun/Moon formats with `Units`.
- [ ] **Step 4: Run** the full suite. Expected: all pass.
- [ ] **Step 5: Commit** — `git commit -m "Units: kilometres or miles for depths, winds and Sun/Moon distances"`

---

### Task 4: GLM listing and file reader

**Files:**
- Modify: `src/EarthWatch3D.Core/EarthWatch3D.Core.csproj` (add `PureHDF` 2.2.0)
- Create: `src/EarthWatch3D.Core/Lightning/LightningFlash.cs`, `Lightning/Glm/GlmListing.cs`, `Lightning/Glm/GlmReader.cs`
- Modify: `tools/capture-samples.ps1` (also save one hour's listing to `tests/Samples/glm_g19_listing.xml`)
- Create: `tests/Samples/glm_g19_listing.xml`
- Test: `tests/EarthWatch3D.Tests/GlmReaderTests.cs`, `GlmListingTests.cs`

**Interfaces:**
- Produces:
  - `public enum LightningSensor { GoesEast, GoesWest, Mtg }`
  - `public readonly record struct LightningFlash(DateTime TimeUtc, double Lat, double Lon, double EnergyJ, LightningSensor Sensor)`
  - `public sealed record GlmObject(string Key, DateTime StartUtc, DateTime EndUtc, long Size)`
  - `public static class GlmListing` with `string Prefix(DateTime hourUtc)` → `"GLM-L2-LCFA/yyyy/ddd/HH/"`,
    `Uri ListUri(string bucket, string prefix, string? startAfter)`, `IReadOnlyList<GlmObject> Parse(Stream xml)`,
    `bool TryParseKey(string key, out DateTime startUtc, out DateTime endUtc)` (`s`/`e` fields
    `yyyydddHHMMSSt`, t = tenths).
  - `public static class GlmReader` with `IReadOnlyList<LightningFlash> Read(Stream nc, LightningSensor sensor)`;
    throws `FormatException` for a file it cannot read.

- [ ] **Step 1: Capture the listing sample** (one-time, network): extend `capture-samples.ps1`, run it for the
  listing only, and commit `glm_g19_listing.xml` with the other files of this task.
- [ ] **Step 2: Write the failing tests:**
  - `GlmReaderTests.MatchesNetCdf4Dump`: `Read(glm_g19_sample.nc, GoesEast)` has 757 flashes; the first 10 match
    `glm_g19_sample.csv` (time within 1 ms, lat/lon within 1e-5°, energy relative 1e-5).
  - `ReadsGoesWestSample`: `glm_g18_sample.nc` reads without error, every flash has `Sensor == GoesWest`, lat in
    [−90, 90], lon in (−180, 180].
  - `TruncatedFileThrowsFormatException`: the first 50,000 bytes of the sample → `FormatException`.
  - `GlmListingTests.ParsesListing`: the sample has 180 objects, keys in order, the first object's `StartUtc` and
    `EndUtc` 20 s apart and matching its key.
  - `PrefixRollsOverAtMidnightNewYearAndLeapDay`: `Prefix(2026-11-11 19:xx)` = `"GLM-L2-LCFA/2026/315/19/"`;
    `Prefix(2026-12-31 23:00)` = `".../2026/365/23/"`; `Prefix(2027-01-01 00:00)` = `".../2027/001/00/"`;
    `Prefix(2028-12-31 05:00)` = `".../2028/366/05/"`.
  - `ListUriUsesStartAfter`: `ListUri("noaa-goes19", p, "k")` equals
    `https://noaa-goes19.s3.amazonaws.com/?list-type=2&prefix=<p escaped>&start-after=<k escaped>`.
- [ ] **Step 3: Run** — expected build errors.
- [ ] **Step 4: Implement.** PureHDF as in data-sources §7: `flash_lat`/`flash_lon` float32; `flash_energy` and
  `flash_time_offset_of_first_event` read as `ushort[]` with float32 `scale_factor`/`add_offset` applied in double;
  skip energy `_FillValue` 65535 (energy 0); time = 2000-01-01T12:00:00Z + `product_time` + offset. Wrap PureHDF's
  exceptions in `FormatException`.
- [ ] **Step 5: Run** the tests. Expected: PASS.
- [ ] **Step 6: Commit** — `git commit -m "Lightning: GLM listing and NetCDF reader (PureHDF), checked against netCDF4"`

---

### Task 5: Lightning buffer, overlap rules, view state and ILightningSource

**Files:**
- Create: `src/EarthWatch3D.Core/Lightning/LightningBuffer.cs`, `LightningOverlap.cs`, `ILightningSource.cs`,
  `LiveLightningSource.cs`, `src/EarthWatch3D.Core/Geo/ViewState.cs`
- Test: `tests/EarthWatch3D.Tests/LightningBufferTests.cs`, `LightningOverlapTests.cs`, `ViewStateTests.cs`

**Interfaces:**
- Consumes: `LightningFlash`, `LightningSensor` (Task 4).
- Produces:
  - `public sealed class LightningBuffer(int capacity = 100_000)`: `int Add(IEnumerable<LightningFlash> flashes)`
    (returns how many were new; de-duplicates by sensor, time to the millisecond, and lat/lon to 1e-4°),
    `IReadOnlyList<LightningFlash> Between(DateTime fromUtc, DateTime toUtc)`, `void PruneBefore(DateTime utc)`,
    `DateTime? Newest(LightningSensor s)`, `int Count`, `event EventHandler? Changed` (raised after an `Add` that
    added any). Thread-safe.
  - `public static class LightningOverlap` with `const double WestEastBoundary = -106.2, EastMtgBoundary = -37.6`
    and `bool Keep(in LightningFlash f, bool mtgActive)`: GoesWest keeps lon < −106.2; GoesEast keeps
    −106.2 ≤ lon < −37.6, or every lon ≥ −106.2 when `!mtgActive`; Mtg keeps lon ≥ −37.6.
  - `public sealed record LightningCoverage(DateTime? NewestGoesEastUtc, DateTime? NewestGoesWestUtc, DateTime? NewestMtgUtc, bool MtgSetUp)`
  - `public interface ILightningSource` exactly as spec §9 (`Coverage`, `GetFlashes(from, to)`, `SupportsHistory`,
    `FlashesChanged`).
  - `public sealed class LiveLightningSource(LightningBuffer buffer, Func<bool> mtgSetUp) : ILightningSource`
    (`SupportsHistory = false`).
  - `public sealed class ViewState`: `GeoPosition CameraSubPoint { get; set; }` (thread-safe; default (0, 0)),
    `static bool Faces(GeoPosition cameraSubPoint, double subSatelliteLon, double maxAngleDeg = 100)`.

- [ ] **Step 1: Write the failing tests:**
  - `LightningOverlapTests.EachFlashKeptExactlyOnce`: synthetic flashes from all three sensors at lon −106.21,
    −106.2, −106.19, −37.61, −37.6, −37.59 (plus each sensor's view of the same flash); with MTG active each
    position is kept by exactly one sensor; with MTG inactive GoesEast keeps −37.6 and −37.59 (spec §10).
  - `LightningBufferTests.DeduplicatesAcrossFiles`: the same flash added twice (as from two adjacent GLM files)
    counts once; `Add` returns 0 the second time.
  - `CapDropsOldestFirst`: capacity 1,000, add 1,500 in time order → `Count == 1000` and the oldest kept is the 501st.
  - `BetweenAndPrune`: `Between` is inclusive of `from`, exclusive of `to`; `PruneBefore` removes older flashes.
  - `ChangedRaisedOnlyForNewFlashes`.
  - `ViewStateTests.FacesUsesTheGreatCircleAngle`: camera sub-point (0, −75.2) faces GOES-East (angle 0°),
    GOES-West (62°) and MTG (75.2°); camera (0, 100) does not face GOES-East (175.2°); camera (0, 24.8) is exactly
    100° from GOES-East → false.
  - `LiveLightningSourceDoesNotSupportHistory`.
- [ ] **Step 2: Run** — expected build errors.
- [ ] **Step 3: Implement.** Buffer: a list kept in time order plus a `HashSet` of de-dup keys, under one lock;
  inserts are mostly at the end.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Lightning: buffer with de-duplication and cap, overlap rules, view state, ILightningSource"`

---

### Task 6: GLM feed

**Files:**
- Create: `src/EarthWatch3D.Core/Lightning/Glm/GlmFeed.cs`
- Test: `tests/EarthWatch3D.Tests/GlmFeedTests.cs`

**Interfaces:**
- Consumes: `FeedHttp`, `FeedRunResult`, `DownloadMeter.LimitReached` (as a `Func<bool>`), `GlmListing`,
  `GlmReader`, `LightningBuffer`, `LightningOverlap`, `ViewState`.
- Produces: `public sealed class GlmFeed(FeedHttp http, LightningBuffer buffer, LightningSensor sensor, IClock clock,
  ViewState view, Func<TimeSpan> window, Func<bool> paused, Func<bool> mtgActive, Func<bool> wanted, IAppLog log) : IEventFeed`
  with `Name` "GLM East" / "GLM West", `Interval` 20 s, bucket `noaa-goes19` / `noaa-goes18`,
  sub-satellite longitude −75.2 / −137.2, and `const int MaxFilesPerRun = 45`.

Behaviour (decided here; the tests pin it):
- Order of checks: `!wanted()` (lightning layer off) → `Skipped`; `paused()` → `Paused`; not facing the camera →
  `Skipped`.
- For each hour from `hour(now − window)` to `hour(now)`: list with `start-after` = the last key already listed in
  that hour (a full listing the first time). New objects go into a pending set.
- Drop pending objects whose `EndUtc <= now − window`. Download the newest first, at most `MaxFilesPerRun`; read;
  `buffer.Add` the flashes that `LightningOverlap.Keep` accepts; `buffer.PruneBefore(now − 60 min)`.
- A file that fails to download or read is logged once and dropped (not retried); the run continues. A listing
  failure throws (normal feed backoff).
- Forget listing state for hours older than the window.

- [ ] **Step 1: Write the failing tests** (`SampleHttpHandler` mapping the listing sample and the sample `.nc` for
  every key; a `TestClock` at 2026-11-11 19:59:50Z unless stated):
  - `ReadsNewFilesIntoTheBuffer`: one run → flashes in the buffer, all `GoesEast`.
  - `SecondRunListsWithStartAfter`: the second listing request contains `start-after=` the newest key.
  - `CatchUpIsLimitedToTheWindowNewestFirst`: window 15 min and 180 listed files → the run downloads exactly 45, the
    newest 45; a second run downloads none of the files that ended more than 15 min ago.
  - `ListsBothHoursWhenTheWindowCrossesTheHour`: clock 20:05:00Z, window 15 min → listings for prefixes `…/19/` and
    `…/20/`.
  - `SkippedWhenNotInViewOrNotWanted`: `ViewState` sub-point (0, 100) → `Skipped` and no request; `wanted` false →
    `Skipped`.
  - `PausedWhenTheLimitIsReached`: `paused` true → `Paused`, no request.
  - `BadFileIsSkippedOthersLoad`: one key maps to a truncated file → the others load, one warning logged, result `Ok`.
- [ ] **Step 2: Run** — expected build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Lightning: GLM feed (in-view polling, start-after listing, window-limited catch-up)"`

---

### Task 7: EUMETSAT token client and MTG feed

Uses the facts Task 1 recorded in data-sources §8 (search endpoint and parameters, product packaging, variable names
and packing). Where this task says "per §8", take the exact value from there.

**Files:**
- Create: `src/EarthWatch3D.Core/Lightning/Mtg/EumetsatCredentials.cs`, `EumetsatClient.cs`, `MtgReader.cs`, `MtgFeed.cs`
- Test: `tests/EarthWatch3D.Tests/EumetsatClientTests.cs`, `MtgReaderTests.cs`, `MtgFeedTests.cs`

**Interfaces:**
- Consumes: `FeedHttp.PostFormAsync`, `FeedAuthException`, `LightningBuffer`, `LightningOverlap`, `ViewState`,
  `FeedRunResult`.
- Produces:
  - `public sealed record EumetsatCredentials(string Key, string Secret)` with `ToString()` returning
    `"EumetsatCredentials(***)"` (never the values).
  - `public enum EumetsatTestResult { Accepted, Rejected, Unreachable }`
  - `public sealed class EumetsatClient(FeedHttp http, IClock clock)`:
    `Task<string> GetTokenAsync(EumetsatCredentials c, CancellationToken ct)` (cached until 60 s before
    `expires_in`; a different credentials object invalidates the cache),
    `Task<(EumetsatTestResult Result, string? Message)> TestAsync(EumetsatCredentials c, CancellationToken ct)`,
    `Task<IReadOnlyList<MtgProduct>> SearchAsync(DateTime sinceUtc, CancellationToken ct)` and
    `Task<byte[]> DownloadAsync(MtgProduct p, CancellationToken ct)` (both per §8, retrying once with a fresh token on
    `FeedAuthException`).
  - `public sealed record MtgProduct(string Id, DateTime SensingStartUtc, DateTime SensingEndUtc, Uri Download)`
  - `public static class MtgReader` with `IReadOnlyList<LightningFlash> Read(byte[] product)` (handles a zip if §8
    says products are zipped; variable names and packing per §8); throws `FormatException`.
  - `public sealed class MtgFeed(EumetsatClient client, LightningBuffer buffer, IClock clock, ViewState view,
    Func<EumetsatCredentials?> credentials, Func<TimeSpan> window, Func<bool> paused, Func<bool> wanted, IAppLog log) : IEventFeed`
    with `Name` "MTG", `Interval` 20 s unless §8's cadence says otherwise (then that cadence).
  - A rejected key throws `EumetsatKeyRejectedException` with message `"EUMETSAT key rejected. Check it in Settings."`.

- [ ] **Step 1: Write the failing tests:**
  - `EumetsatClientTests.TokenIsCachedUntilNearExpiry`: `expires_in` 3600 → one token request for two calls; at
    +3541 s a second request.
  - `ExpiredTokenIsRenewedOnce`: the search answers 401 once, then 200 → exactly two token requests and the search
    succeeds; 401 twice → `EumetsatKeyRejectedException` (no loop).
  - `TestReportsAcceptedRejectedUnreachable`: token 200 → Accepted; 401 → Rejected; handler throws
    `HttpRequestException` → Unreachable with the message.
  - `CredentialsNeverAppearInLogsOrUris`: run a full feed cycle with key `"K-SECRET"` and secret `"S-SECRET"`; no
    request URI and no log line contains either string.
  - `MtgReaderTests.MatchesNetCdf4Dump`: the MTG sample's flash count and first 10 flashes match
    `mtg_lfl_sample.csv` (same tolerances as GLM), all `Sensor == Mtg`.
  - `MtgFeedTests.NoKeyIsNotSetUp`: credentials null → `NotSetUp`, no request.
  - `RejectedKeyFailsWithKeyRejected`: the scheduler shows `Failing` after 3 runs with `LastError` = the message
    above, and a GLM feed on the same scheduler still reports `Ok`.
  - `ReadsNewProductsOnce`: two runs with the same search response → each product downloaded once.
  - `SkippedWhenNotInView`, `PausedWhenTheLimitIsReached` (as for GLM).
- [ ] **Step 2: Run** — expected build errors.
- [ ] **Step 3: Implement.** Catch-up and window rules as in Task 6 (newest first, only inside the window).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Lightning: EUMETSAT token client and MTG LI feed"`

---

### Task 8: Lightning on the globe (layer, window, legend, replay hiding)

**First step is the performance check** the Plan 2 review asked for, so a slow approach is caught before it is
built on.

**Files:**
- Create: `src/EarthWatch3D.App/Rendering/LightningLayer.cs`
- Modify: `src/EarthWatch3D.App/Controllers/SceneController.cs` (constructor gains `ILightningSource lightning,
  ViewState view`; owns `LightningLayer`; sets `ViewState.CameraSubPoint` on camera change), `MainWindow.xaml.cs`
  (passes them through), `ViewModels/LayersViewModel.cs` (`ShowLightning`),
  `ViewModels/DisplayOptionsViewModel.cs` (`LightningWindowMinutes`, `LightningWindowChoices`),
  `Views/LayerStrip.xaml` (⚡ toggle with legend), `Views/DisplayFlyout.xaml` (LIGHTNING section, window chips),
  `Diagnostics/LaunchOptions.cs` (`lightning`), `Diagnostics/PerfRun.cs` (`--bench-lightning <n>`),
  `Core/Storage/AppSettings.cs`
- Test: `tests/EarthWatch3D.Tests/LightningLayerTests.cs`, `LayersAndDisplayViewModelTests.cs`
- Docs: `docs/perf.md`

**Interfaces:**
- Consumes: `ILightningSource`, `LightningCoverage`, `ViewState`.
- Produces:
  - `public sealed class LightningLayer` with `LightningLayer(HelixViewport3D viewport)`,
    `void Update(ILightningSource source, DateTime nowUtc, TimeSpan window, bool show)` (rebuilds at most once a
    second unless `show` changed), `int PointCount`, and
    `static int Bucket(TimeSpan age, TimeSpan window)` → 0..3.
  - `static bool LightningLayer.IsShown(bool layerOn, bool live, bool supportsHistory) => layerOn && (live || supportsHistory)`
  - Four `PointsVisual3D` (one per age bucket), points at radius 1.004:

    | Bucket | Age as a fraction of the window | Colour | Size (px) |
    |---|---|---|---|
    | 0 | < 0.10 | `#FFFFF0B0` | 4 |
    | 1 | < 0.35 | `#FFFFD84A` | 3.5 |
    | 2 | < 0.70 | `#FFFF9A2E` | 3 |
    | 3 | ≥ 0.70 | `#FFB4602A` | 2.5 |

  - `LayersViewModel.ShowLightning` (default true), `DisplayOptionsViewModel.LightningWindowMinutes` (default 15).
  - Legend tooltip text from `static string LightningLayer.LegendNote(LightningCoverage c, DateTime nowUtc)`:
    one line per sensor, e.g. `"GOES-East: newest flash 25 s ago"`, `"MTG: not set up (Settings)"`; when the
    MTG newest flash is older than 5 min the line says `"MTG: newest flash 12 min ago (delayed)"`.
  - Attribution line in the legend: `"GOES GLM: NOAA. MTG LI: Contains EUMETSAT data."`

- [ ] **Step 1: Benchmark first.** Add `--bench-lightning <n>` to `PerfRun` (PERF_LOG builds): fill a buffer with
  n random flashes over 15 min, show them through `LightningLayer`, rotate as the existing bench does, log fps.
  Run with n = 25,000. **Expected: ≥ 30 fps** (spec §10). Record it in `docs/perf.md`. If it is below 30, stop and
  report to the controller before going on (the fallback is drawing lightning into the globe image's sparse lines
  layer instead).
- [ ] **Step 2: Write the failing tests:**
  - `LightningLayerTests.BucketsByAgeFraction`: window 15 min; ages 0, 89 s, 90 s, 314 s, 315 s, 629 s, 630 s →
    buckets 0, 0, 1, 1, 2, 2, 3.
  - `HiddenInReplayWithoutHistory` (spec §10): `IsShown(true, false, false) == false`,
    `IsShown(true, true, false) == true`, `IsShown(true, false, true) == true`, `IsShown(false, true, true) == false`.
  - `LegendNoteDescribesEachSensor`: coverage with East 25 s old, West null, MTG not set up → the three lines above.
  - `LayersAndDisplayViewModelTests.LightningToggleAndWindowAreSaved`.
  - (STA) `UpdateDrawsOnlyFlashesInsideTheWindow`: 10 flashes inside and 5 outside a 15 min window →
    `PointCount == 10`; `show` false → `PointCount == 0`.
- [ ] **Step 3: Run** — expected build errors.
- [ ] **Step 4: Implement.** `SceneController` calls `Update` on its existing tick and on
  `ILightningSource.FlashesChanged` (coalesced with the same `BeginInvoke` pattern as the event store). The
  window comes from `DisplayOptions`; "live" from `TimelineViewModel.IsLive`.
- [ ] **Step 5: Run** the full suite. Expected: PASS.
- [ ] **Step 6: Commit** — `git commit -m "Lightning on the globe: age-bucketed points, window, legend, hidden in replay"`

---

### Task 9: Live clouds (GIBS)

**Files:**
- Create: `src/EarthWatch3D.Core/Globe/CloudLayer.cs`, `src/EarthWatch3D.Core/Feeds/Gibs/GibsCloudsFeed.cs`
- Modify: `SceneController.cs` (clouds into `GlobeLayers.Clouds`), `LayersViewModel.cs` (`ShowClouds`),
  `LayerStrip.xaml` (☁ toggle; legend shows the image date), `LaunchOptions.cs` (`clouds`), `AppSettings.cs`
- Modify: `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md` §5.6 (the keying rule below)
- Test: `tests/EarthWatch3D.Tests/CloudLayerTests.cs`, `GibsCloudsFeedTests.cs`

**Interfaces:**
- Produces:
  - `public static class CloudLayer` with `static byte Alpha(double viirsLuma, double baseLuma)` and
    `static Bgra32Image Build(Bgra32Image viirs, Bgra32Image baseImage)` (same size required). Rule: the true-colour
    image also shows land and desert, so alpha is keyed on **brightness above the Blue Marble**:
    `a = clamp((Lv − Lb − 0.06) / 0.30, 0, 1) × 0.9`, L = (0.299 R + 0.587 G + 0.114 B) / 255; the colour is the
    VIIRS pixel. Black gaps and polar night (Lv below Lb) are transparent.
  - `public static double CloudLayer.BlackFraction(Bgra32Image viirs)` — share of pixels with L < 0.02 between
    70°N and 70°S.
  - `public sealed class GibsCloudsFeed(FeedHttp http, AppPaths paths, IClock clock, Func<bool> wanted, IAppLog log) : IEventFeed`
    with `Name` "Clouds", `Interval` 1 h, `event EventHandler<CloudImage>? ImageReady`,
    `public sealed record CloudImage(DateTime DateUtc, byte[] Jpeg)`. Date = previous UTC day; if its
    `BlackFraction` is over 0.05, use the day before and try again next run. Files are
    `cache\clouds\viirs_yyyy-MM-dd.jpg`; older ones are deleted. A cached file for the date means no download.

- [ ] **Step 1: Write the failing tests:**
  - `CloudLayerTests.KeysOnBrightnessAboveTheBase` (alpha = `Math.Round(a × 255)`): `Alpha(0.9, 0.2) == 230`
    (clamped to 0.9), `Alpha(0.3, 0.3) == 0`
    (desert as bright as the base), `Alpha(0.0, 0.1) == 0` (gap), `Alpha(0.41, 0.2) == 115`.
  - `SampleHasPlausibleCover`: the sample JPEG against the app's Blue Marble asset at 4096×2048 → the share of pixels
    with alpha > 128 is between 0.10 and 0.80; `BlackFraction` < 0.05.
  - `GibsCloudsFeedTests.DownloadsYesterdayOnceAndCaches`: clock 2026-11-11T20:00Z → one GetMap with
    `TIME=2026-11-10`, `ImageReady` raised; a second run makes no request and raises nothing new; a new feed on the
    same paths loads from disk without a request and raises `ImageReady` with that file.
  - `FallsBackWhenYesterdayIsPartial`: the handler returns a mostly black JPEG for 2026-11-10 → the feed requests
    2026-11-09 in the same run.
  - `SkippedWhenNotWanted`.
- [ ] **Step 2: Run** — expected build errors.
- [ ] **Step 3: Implement.** Decode on `SceneController`'s `StaWorker` (as the base image is), build the layer at
  `GlobeImageWidth`, recompose. Clouds stay as they are in replay (one daily image), and the legend says which day.
- [ ] **Step 4: Run** the full suite. Expected: PASS.
- [ ] **Step 5: Snapshot check** (rebuild Release first):
  `EarthWatch3D.App.exe --snapshot <scratch>\clouds.png --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z --layers clouds`
  (the offline handler serves the sample JPEG for any GetMap). Look at it: clouds over ocean, deserts not white.
- [ ] **Step 6: Commit** — `git commit -m "Clouds: daily VIIRS layer keyed against the Blue Marble, cached, off by default"`

---

### Task 10: Settings dialog (EUMETSAT key, daily limit, units)

**Files:**
- Modify: `src/EarthWatch3D.App/EarthWatch3D.App.csproj` (add `System.Security.Cryptography.ProtectedData`)
- Create: `src/EarthWatch3D.Core/Storage/IProtector.cs`, `src/EarthWatch3D.App/Services/DpapiProtector.cs`,
  `src/EarthWatch3D.Core/Lightning/Mtg/EumetsatCredentialStore.cs`, `src/EarthWatch3D.App/Services/DialogService.cs`, `ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.xaml(.cs)`
- Modify: `LayerStrip.xaml` (⚙ Settings button at the bottom), `StatusStrip.xaml` (clicking the MTG dot when it is
  NotSetUp or key-rejected opens Settings), `MainViewModel.cs` (`OpenSettingsCommand`)
- Test: `tests/EarthWatch3D.Tests/SettingsViewModelTests.cs`, `EumetsatCredentialStoreTests.cs`

**Interfaces:**
- Consumes: `EumetsatClient.TestAsync`, `DownloadMeter.LimitBytes`, `MainViewModel.Units`.
- Produces:
  - `public interface IProtector { string Protect(string plain); string? Unprotect(string protectedBase64); }`
    (Unprotect returns null if the blob cannot be decrypted, e.g. another Windows user).
  - `DpapiProtector : IProtector` — `ProtectedData`, `DataProtectionScope.CurrentUser`, entropy = UTF-8
    `"EarthWatch3D.EUMETSAT"`.
  - `public sealed class EumetsatCredentialStore(ISettingsStore store, IProtector protector)`:
    `EumetsatCredentials? Current { get; }` (thread-safe), `void Set(EumetsatCredentials? c)` (saves protected
    values; null clears both).
  - `DialogService : IDialogService` (the interface is from Task 2) opens `SettingsWindow` modally over the main window.
  - `public sealed partial class SettingsViewModel(ISettingsStore store, EumetsatCredentialStore creds,
    Func<EumetsatCredentials, CancellationToken, Task<(EumetsatTestResult, string?)>> test, Action<AppSettings> applied)`
    with `ConsumerKey`,
    `ConsumerSecret`, `DailyLimitText`, `Units`, `TestStatus`, `string? Error`, `TestCommand` (async),
    `bool Save()` (false and `Error` set when invalid). `const string RegistrationUrl = "https://api.eumetsat.int/api-key/"`.
  - Validation: key and secret both empty (clears MTG) or both non-empty with no whitespace; daily limit empty
    (off) or a whole number of MB from 10 to 100,000.

- [ ] **Step 1: Write the failing tests:**
  - `SettingsViewModelTests.RejectsOneOfKeyOrSecretOrWhitespace`: key only → `Save()` false with
    `Error == "Enter both the consumer key and the consumer secret, or neither."`; key `"a b"` → false.
  - `RejectsBadLimit`: `"5"`, `"abc"`, `"200000"` → false with `Error == "Daily limit: a whole number of MB from 10 to 100,000, or empty for no limit."`;
    `""` → saved as null.
  - `KeyIsStoredEncrypted` (spec §10): with a fake protector that prefixes `"P:"`, saving key `"k1"`/secret `"s1"`
    stores `EumetsatKeyProtected == "P:k1"`, and `creds.Current == new("k1", "s1")`; the plain values appear in no
    `AppSettings` property.
  - `TestShowsTheResult`: Accepted → `TestStatus == "Key accepted."`; Rejected → `"Key rejected. Check the key and secret."`;
    Unreachable → `"Could not reach EUMETSAT: <message>"`.
  - `EumetsatCredentialStoreTests.UndecryptableIsTreatedAsNotSet`: a protector whose `Unprotect` returns null →
    `Current == null`.
  - `UnitsAndLimitApplyOnSave`: saving Miles and `"500"` calls `applied` once with `Units == Miles` and
    `DailyLimitMb == 500`, and the store holds the same. (The App's `applied` sets `MainViewModel.Units` and
    `meter.LimitBytes = DailyLimitMb × 1,000,000`.)
- [ ] **Step 2: Run** — expected build errors.
- [ ] **Step 3: Implement.** The window is a small dark dialog in the cards' style (CardStyles.xaml merged; text
  white, as the readability test requires — add `SettingsWindow` to `CardReadabilityTests`). PasswordBox for the
  secret, bound through code-behind (view-only plumbing). A "Get a key" link opens `RegistrationUrl` in the
  browser.
- [ ] **Step 4: Run** the full suite. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Settings dialog: EUMETSAT key (DPAPI), Test, daily limit, km or miles"`

---

### Task 11: Wiring, offline samples, docs and verification

**Files:**
- Modify: `src/EarthWatch3D.App/App.xaml.cs` (composition root), `Diagnostics/OfflineSamples.cs`
- Modify: `docs/manual-test.md`, `docs/perf.md`, `docs/data-sources.md` (measured data use), spec §5.7–§5.8 and §7
  for anything that changed (rulings section)
- Test: `tests/EarthWatch3D.Tests/OfflineSamplesTests.cs` (or the existing offline test)

**Interfaces:**
- Consumes: everything above.

Wiring (decided here):
- One `DownloadMeter` (local zone `TimeZoneInfo.Local`, file `AppPaths.DownloadMeterFile`), fed by
  `FeedHttp.BytesReceived`; limit from settings; saved every minute on the prune timer and at shutdown.
  `vm.Status.DownloadText` updated on the 1 s tick.
- One `ViewState` (set by `SceneController`), one `LightningBuffer`, `LiveLightningSource`.
- Feeds added to the scheduler: `GlmFeed` East and West, `MtgFeed`, `GibsCloudsFeed`. `wanted` for lightning =
  `Layers.ShowLightning && Timeline.IsLive` (read thread-safely, like `CurrentFilter`); for clouds =
  `Layers.ShowClouds`. `paused` = `meter.LimitReached`. `mtgActive` = credentials set and MTG's last status Ok.
- Offline: `OfflineSamples.CreateHandler` also serves the GLM listing and sample `.nc` (for both buckets, with keys
  rewritten to the sample clock's hour), the MTG sample with a fake token (only when a fake credential is set for the
  run), and the GIBS JPEG for any GetMap.

- [ ] **Step 1: Write the failing test** `OfflineRunHasLightningAndClouds`: the offline handler answers a GLM
  listing for 2026-11-11 19 h and the sample file; a `GlmFeed` run on it adds 757 flashes; a `GibsCloudsFeed` run
  raises `ImageReady`.
- [ ] **Step 2: Implement** the wiring and the offline handler.
- [ ] **Step 3: Run** the full suite and the Release build. Expected: all pass, 0 warnings.
- [ ] **Step 4: Snapshot checks** (rebuild Release first; one launch each):
  - `--offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z --look-at 0,-60 --layers lightning,clouds`
    → lightning over the Americas in age colours, clouds, "Today: …" in the status bar.
  - Same with `--display-time` replay one hour back after start (use `--snapshot-recompose`) → no lightning.
- [ ] **Step 5: Live run, 10 minutes** (PERF_LOG build, as in Plan 2): log fps, working set, flash count, MB
  downloaded per sensor. Record in `docs/perf.md` and `docs/data-sources.md` (replaces the 100 MB/h estimate in
  spec §1 with the measurement). If MTG is set up, include it.
- [ ] **Step 6: Docs.** `manual-test.md`: lightning on/off, window chips, lightning hiding in replay, Settings with
  a valid key, an invalid key and no key (spec §10), daily limit (set 10 MB, watch lightning pause and the blue
  dot), units switch, clouds on/off. Spec: record rulings made during this plan (a "Rulings made while building
  Plan 3" table like Plan 2's), including the cloud keying rule and the blue Paused dot.
- [ ] **Step 7: Commit** — `git commit -m "Plan 3 wiring: lightning, clouds and download meter in the app; offline samples; docs and measurements"`
