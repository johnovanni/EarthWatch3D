# Storm Replay History (EW-003) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make tropical cyclones move during time-bar replay by giving each storm a timed history from GDACS and NHC,
and placing the storm on that history for the display time.

**Architecture:** `CycloneAdvisory` gains `History`, a list of timed past fixes, and `Replay`, which describes
where a drawn position came from.
- The GDACS reader fills `History` from track points it already downloads.
- The NHC feed fills it from a new best-track KMZ.
- A new pure function, `CycloneReplay.At`, turns one storm's stored advisories into what to draw at time t.
  `EventVisibility` calls it before `CycloneMerger`.
- The app shows a grey "intensity unknown" glyph and says in the tooltip and details panel when a position falls
  between fixes.

**Tech Stack:** C# / .NET 10, WPF, xunit; GlobeCore (`GeoLine`, `DateLine`), AstroCore (`GeoPosition`).

**Spec:** `C:\repos\EarthWatch3D\docs\superpowers\specs\2026-11-22-storm-replay-history-design.md`

## Global Constraints

- Live, and any display time at or after a storm's newest stored advisory, draws **exactly as today** (spec S1). The
  7 baseline snapshot scenes must stay pixel-identical: run PngDiff at tolerance 0 against
  `artifacts\snapshot-baseline`.
- Interpolation is great-circle and by time, between two observed fixes only. There is never any extrapolation, and
  the forecast track is never used for replay (S2, S4).
- When no wind is known, the glyph is the grey "intensity unknown" one (S3). An advisory's category is never shown
  at a time before that advisory.
- The 24 h storm window stays: a storm is hidden at t if its newest fix at or before t is more than 24 h old, or if t
  is before its earliest fix.
- The event cache `SchemaVersion` goes from 2 to 3.
- Work on branch `storm-history` in `C:\repos\EarthWatch3D`. Commit locally; never push. Commit messages end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Visual checks only through the app's own `--snapshot-scenes` (RenderTargetBitmap). No screen capture. Launch the app
  at most twice in the whole plan.
- One test is known to be flaky: `GlobeImageComposerPerfTests.Composes8192UnderBudget`, a Debug timing test.

## Review Focus

1. **A storm crossing the date line between two fixes** must glide across 180°, not jump the long way round. Pinned
   by `CycloneReplayTests.InterpolatesAcrossTheDateLine` (Task 4).
2. **A GDACS point label across New Year** (`31/12 18:00 UTC` with episode `todate` 2027-01-01) must get year 2026.
   Pinned by `GdacsHistoryTests.YearRollsBackAcrossNewYear` (Task 2).
3. **An NHC best-track KMZ that fails or is unreadable** must leave the storm drawn as today (empty `History`), with
   one log warning and a retry on the next run. Pinned by `NhcBestTrackTests.FailedBestTrackKeepsAdvisory` (Task 3).
4. **Two advisories of one storm whose `History` lists overlap** (the same fix times) must give one fix per time,
   preferring the newer advisory's value. Pinned by `CycloneReplayTests.DuplicateFixTimesCollapse` (Task 4).
5. **Selecting a storm and then replaying** must keep the selection: the derived advisory keeps the same `Id`.
   Pinned by `SelectionViewModelTests.ReplayedStormStaysSelected` (Task 4).

---

### Task 1: Model and cache

**Files:**
- Modify: `src/EarthWatch3D.Core/Events/EarthEvents.cs` (`CycloneAdvisory`)
- Modify: `src/EarthWatch3D.Core/Storage/EventCache.cs` (`CurrentSchemaVersion` 2 → 3)
- Test: `tests/EarthWatch3D.Tests/EventCacheWriterTests.cs` (or the existing cache test file)

**Interfaces:**
- Produces:
  - `public IReadOnlyList<StormFix> History { get; init; } = [];` on `CycloneAdvisory`. It holds past fixes only,
    in time order, and is included in `Equals` (via `ListEquality`) and in `GetHashCode`.
  - `public sealed record ReplayInfo(DateTime EarlierFixUtc, DateTime? LaterFixUtc, string HistorySource, bool IntensityKnown);`
    in `EarthEvents.cs`. `LaterFixUtc` is null when the position is the newest fix itself.
  - `[JsonIgnore] public ReplayInfo? Replay { get; init; }` on `CycloneAdvisory`. It is not in `Equals` and not
    cached; it is set only on derived advisories.

- [ ] **Step 1: Write the failing tests.**
  - `CacheRoundTripsCycloneHistory`: an advisory with 2 `History` fixes survives save and load, and the loaded copy
    `Equals` the original.
  - `SchemaVersion2CacheIsIgnored`: a cache file written with `SchemaVersion: 2` loads as empty, as other version
    changes do.
  - `ReplayIsNotCached`: an advisory with `Replay` set saves and loads with `Replay == null`.
- [ ] **Step 2:** Run them and expect FAIL.
- [ ] **Step 3:** Implement the properties and the version bump. Existing positional constructor calls stay
  unchanged.
- [ ] **Step 4:** Run the full EarthWatch3D suite and expect PASS.
- [ ] **Step 5:** Commit with the message `Storms: CycloneAdvisory.History and Replay; event cache schema 3`.

### Task 2: GDACS history from track points

**Files:**
- Modify: `src/EarthWatch3D.Core/Feeds/Gdacs/GdacsReader.cs` (the `Point_Polygon_Point_` branch, around line 108,
  which is empty today)
- Test: `tests/EarthWatch3D.Tests/GdacsHistoryTests.cs`

**Interfaces:**
- Consumes: `CycloneAdvisory.History` (Task 1).
- Produces: `GdacsReader.ReadGeometry` returns the advisory with `History` filled. Add
  `internal static DateTime? PointTime(string label, DateTime episodeToUtc)`, which parses `dd/MM HH:mm UTC`
  (invariant culture) and picks the year that puts the result within 183 days of `episodeToUtc`.

Rules:
- The fix position is the centre of the circle's bounding box (latitude: mid of min and max; longitude: mid, taken
  in a frame centred on the first vertex so a circle that straddles 180° works). It is normalised with
  `GeoPosition.NormalizeLongitude`.
- Keep only fixes with time ≤ the episode's `todate`, which is the advisory time; later points are forecast.
- `WindKt` is null.
- Sort by time and drop duplicate times.
- The `todate` comes from each Point feature's own `todate` property (UTC, no zone suffix in the sample).

- [ ] **Step 1: Write the failing tests.**
  - `SamplePenaHasPastFixes`: `ReadGeometry` on `tests/Samples/gdacs_geometry.json` gives a `History` whose first
    fix is 2026-11-06T21:00Z near (11.30, −150.10) within 0.01°, and whose last fix is ≤ 2026-11-11T15:00Z. There
    are no fixes after it, and the times strictly increase.
  - `YearRollsBackAcrossNewYear`: `PointTime("31/12 18:00 UTC", 2027-01-01T06:00Z)` is 2026-12-31T18:00Z.
  - `YearRollsForwardAcrossNewYear`: `PointTime("01/01 00:00 UTC", 2026-12-31T18:00Z)` is 2027-01-01T00:00Z.
  - `UnreadableLabelIsSkipped`: a label of "soon" gives null, and the reader logs one warning and keeps the other
    fixes.
- [ ] **Step 2:** Run them and expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the GDACS tests and the full suite, and expect PASS.
- [ ] **Step 5:** Commit with the message `GDACS: read the storm's timed past track points into History`.

### Task 3: NHC history from the best track

**Files:**
- Modify: `src/EarthWatch3D.Core/Feeds/Nhc/NhcReader.cs` (`NhcStorm` gains `string? BestTrackIssuance, Uri? BestTrackKmz`)
- Create: `src/EarthWatch3D.Core/Feeds/Nhc/BestTrackReader.cs`
- Modify: `src/EarthWatch3D.Core/Feeds/Nhc/NhcFeed.cs` (cache and download, like the track and cone KMZs)
- Modify: `src/EarthWatch3D.App/Diagnostics/OfflineSamples.cs` (add `nhc_best_track.kmz` to `Files` and map its URL)
- Sample (already added): `tests/Samples/nhc_best_track.kmz`, the live `cp042026` best track from 2026-11-22, with
  41 fixes from 2026-11-02 12:00 to 2026-11-12 12:00 UTC.
- Test: `tests/EarthWatch3D.Tests/NhcBestTrackTests.cs`

**Interfaces:**
- Consumes: `CycloneAdvisory.History` (Task 1).
- Produces: `public static IReadOnlyList<StormFix> BestTrackReader.Read(Stream kmz, IAppLog log)`. Each Placemark
  with `<atcfdtg>yyyyMMddHH</atcfdtg>`, `<lat>` and `<lon>` becomes a fix (UTC), and `<intensity>` becomes `WindKt`
  (null if missing). The list is time-ordered, with duplicate times dropped.

Rules:
- `BestTrackKmz` is `bestTrackGIS.kmzFile` if present, otherwise `bestTrackGIS.zipFile` with `.zip` replaced by
  `.kmz`. `BestTrackIssuance` is `bestTrackGIS.issuance`.
- The feed downloads it only when the issuance changes, on the same cache pattern and with the same error handling
  as `TrackAdv`.
- The advisory's `History` is the cached fixes with time ≤ the advisory's `TimeUtc` (`LastUpdateUtc`).
- Offline map: `https://www.nhc.noaa.gov/gis/best_track/cp042026_best_track.kmz` → `nhc_best_track.kmz`.

- [ ] **Step 1: Write the failing tests.**
  - `ReadsSampleBestTrack`: 41 fixes; the first is 2026-11-02T12:00Z at (7.7, −129.8) with 20 kt; the last is
    2026-11-12T12:00Z at (9.8, −173.2) with 30 kt.
  - `AdvisoryHistoryStopsAtAdvisoryTime`: an offline `NhcFeed` run gives `nhc:cp042026` a `History` whose last fix
    is ≤ 2026-11-11T15:00Z and includes 2026-11-11T12:00Z.
  - `FailedBestTrackKeepsAdvisory`: the best-track URL answers 500, the advisory is stored as today with an empty
    `History`, one warning is logged, and the next run asks again.
  - `BestTrackUrlFromZipFile`: a storm JSON with only `zipFile` gives the `.kmz` URL.
- [ ] **Step 2:** Run them and expect FAIL.
- [ ] **Step 3:** Implement. Check that the existing `OfflineSamplesTests` still pass, since `Files` grew.
- [ ] **Step 4:** Run the full suite and expect PASS.
- [ ] **Step 5:** Commit with the message `NHC: best-track history for each storm (timed past fixes)`.

### Task 4: Replay placement, display, docs and the issue record

**Files:**
- Create: `src/EarthWatch3D.Core/Events/CycloneReplay.cs`
- Modify: `src/EarthWatch3D.Core/Events/EventVisibility.cs` (the cyclone block, lines 38-47)
- Modify: `src/EarthWatch3D.App/Rendering/StormStyle.cs` (grey for unknown intensity), `Rendering/EventScene.cs`
  (glyph colour and label use it), `Views/LayerStrip.xaml` (one legend row: grey glyph, "Intensity unknown (replay)")
- Modify: `src/EarthWatch3D.App/ViewModels/SelectionViewModel.cs` (`HoverText` and the detail rows when `Replay` is set)
- Modify: `tests/SnapshotScenes/scenes.json` (add scene `storm-replay`)
- Modify: `README.md` (one sentence in the storms bullet), the spec status, and
  `private/EarthWatch3D-issues.xlsx` (EW-003 → Fixed)
- Test: `tests/EarthWatch3D.Tests/CycloneReplayTests.cs`, `SelectionViewModelTests.cs`, `EventVisibilityTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: `public static CycloneAdvisory? CycloneReplay.At(IReadOnlyList<CycloneAdvisory> storm, DateTime t)`,
  where all the advisories share one `Id`. It returns null when the storm is hidden at t.

`CycloneReplay.At` rules:
- **Newest advisory at or before t is also the newest advisory overall:** return that advisory unchanged
  (`Replay == null`).
- **Otherwise:**
  - **Known fixes:** each advisory's own `(TimeUtc, Location, WindKt)` plus its `History`. For a duplicate time,
    keep the value from the newest advisory.
  - **Neighbours:** `e` is the newest fix ≤ t and `l` is the earliest fix > t. If there is no `e`, or `t − e.TimeUtc`
    exceeds 24 h, return null.
  - **Position:** great-circle (vector slerp) between `e` and `l` at fraction `(t − e)/(l − e)`, or `e` itself when
    there is no `l`.
  - **Wind:** interpolate linearly when both `e` and `l` have one. Otherwise use the newest advisory ≤ t's `WindKt`.
    Otherwise it is unknown, which sets `IntensityKnown = false`.
  - **Past track:** `DateLine`-split lines through the known fixes ≤ t, plus the position.
  - **Forecast track, cone and wind areas:** from the newest advisory ≤ t if there is one; otherwise empty.
  - **The derived advisory:** it copies `Source`, `Id`, `Title`, `Link`, `Name` and `PressureHpa` from the newest
    advisory ≤ t, falling back to the earliest advisory. `TimeUtc` = `e.TimeUtc`. `Replay = new ReplayInfo(e.TimeUtc, l?.TimeUtc, "NHC best track" | "GDACS track", known)`.

`EventVisibility` then groups by `Id`, calls `CycloneReplay.At` for each storm, drops the nulls and passes the result
to `CycloneMerger.Merge` as before.

Tooltip wording when `Replay` is set:
- Between two fixes: `Pena · position at 12:00 UTC, between the 09:00 and 15:00 fixes (NHC best track)`.
- On a fix: `Pena · fix at 09:00 UTC (NHC best track)`.
- Wind is added as today when known.

Details panel: the "Advisory time" row becomes "Position" with the same text, and the category row says
"Intensity unknown" when it isn't known.

- [ ] **Step 1: Write the failing tests.**
  - `NewestAdvisoryIsUnchanged`: at t ≥ the newest advisory, the same instance is returned and `Replay` is null.
  - `InterpolatesBetweenFixes`: fixes (10°N, −150°) at 00:00 and (10°N, −152°) at 06:00, t = 03:00, give a position
    within 0.01° of the great-circle midpoint, with `LaterFixUtc` = 06:00.
  - `InterpolatesAcrossTheDateLine`: fixes at 179°E and 179°W give 180° at the midpoint.
  - `HiddenBeforeEarliestFix` and `HiddenAfter24hGap`.
  - `WindInterpolatedWhenBothKnown` (40 kt and 60 kt give 50 kt) and `UnknownWindIsGrey` (GDACS-only history before
    the first advisory gives `IntensityKnown` false and `WindKt` null).
  - `NoForecastBeforeFirstAdvisory`: `ForecastTrack`, `Cone` and `WindAreas` are empty.
  - `DuplicateFixTimesCollapse`.
  - `EventVisibilityTests.SampleStormMovesInReplay`: with the offline samples loaded, the merged Pena at
    2026-11-09T00:00Z and at 2026-11-10T00:00Z are more than 100 km apart, and both are between the fixes nearest
    those times.
  - `SelectionViewModelTests.ReplayedStormStaysSelected` and `HoverTextSaysBetweenFixes`, using the exact tooltip
    wording above.
- [ ] **Step 2:** Run them and expect FAIL.
- [ ] **Step 3:** Implement `CycloneReplay.At`, the `EventVisibility` change, the grey style (#9AA0A6) with its
  legend row, and the hover and detail text.
- [ ] **Step 4:** Run the full suite and expect PASS.
- [ ] **Step 5: Snapshots** (one launch):
  1. Add the scene `{"name": "storm-replay", "displayTimeUtc": "2026-11-09T03:00:00Z", "lookAt": [10, -160], "layers": ["cyclones", "coastlines"]}`.
  2. Run `--snapshot-scenes` into `artifacts\snapshot-ew003`.
  3. Run `PngDiff artifacts\snapshot-baseline artifacts\snapshot-ew003`. Expected: the 7 old scenes identical;
     `storm-replay` reported as only in one folder.
  4. Open `storm-replay.png` with the file-reading tool and check that Pena is drawn mid-track, with a past line up
     to it, no cone, and the right colour.
  5. Copy it into `artifacts\snapshot-baseline`.
- [ ] **Step 6: Docs and the record.**
  - README storms bullet: "In replay a storm moves along its observed track (NHC best track, GDACS track points),
    never along the forecast; a grey glyph means its strength at that time isn't known."
  - The spec's status becomes "Built".
  - EW-003 row: Status `Fixed`, Fixed in = the commit hash, Fixed (date) = the commit date, plus a one-line Notes
    addition.
- [ ] **Step 7:** Commit with the message `Storms move in replay along their observed track (EW-003)`.
