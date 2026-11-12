# EarthWatch3D Plan 2: Events and Time Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put live earthquakes, tropical cyclones (GDACS + NHC), US weather warnings and NASA EONET storms on the
globe, with a time bar that replays the last 7 days (24 h for storms and alerts). Also add the layer strip, the
⋯ Display flyout, a details panel, the status strip, logging and crash handling, and an optional tectonic plate
boundaries layer (Task 18).

**Architecture:**
- A single **display time** (owned by `TimelineViewModel`) drives everything time-dependent.
- Feeds run on a background `FeedScheduler` and write to a thread-safe `EventStore`, which hands out immutable
  snapshots and is saved to a JSON cache.
- A pure `EventVisibility` function decides what is visible at the display time (including the GDACS/NHC merge
  and EONET de-duplication).
- The app draws point and line events as 3D geometry, and area events (alert zones, cones, wind bands) into the
  globe image's Areas layer.
- All feed readers are tested against the real responses captured in `tests/Samples`.

**Tech Stack:** .NET 10, WPF, HelixToolkit.Wpf 3.1.2, CommunityToolkit.Mvvm 8.4.0, System.Text.Json,
System.IO.Compression (KMZ), xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md`. Read §4, §5.1–5.5, §6 Events, §7 and §11.
**Data facts:** `docs/data-sources.md` §1–5 (endpoints, field names, sizes, surprises).

## Plan series

| Plan | Status |
|---|---|
| 1. Foundation | Done (main, 9f3bde1) |
| **2. Events and time (this plan)** | |
| 3. Lightning, clouds, data use, Settings dialog, km/miles | Next |
| 4. Ship (Welcome, About, MSIX, signing, auto-update) | After 3 |

## Global Constraints

- Target frameworks: `net10.0` (Core) and `net10.0-windows` (App, Tests). Nullable and ImplicitUsings are enabled.
  `dotnet build -c Release` must show 0 warnings.
- **Display time** is `MainViewModel.DisplayTimeUtc`. Nothing time-dependent reads `IClock` except the timeline (for
  live time), feeds and the scheduler (wall clock), store retention, and staleness.
- All `DateTime`s are UTC (`DateTimeKind.Utc`). Feed times without a zone (GDACS) are parsed as UTC. USGS times
  are epoch milliseconds.
- Longitudes are always in (−180°, 180°]. Shapes that cross ±180° are split when they are read, so nothing
  downstream ever sees a crossing.
- Earth-fixed frame and globe image mapping are unchanged from Plan 1. Pixel (x, y) has its centre at
  lon = −180 + (x+0.5)·360/W and lat = 90 − (y+0.5)·180/H.
- HTTP: one shared `HttpClient` (inside `FeedHttp`) with a 30 s timeout, gzip/deflate decompression, and the
  User-Agent `AppInfo.UserAgent`. No user data is ever sent. Retry delays after consecutive failures go
  30 s → 1 min → 2 min → 5 min → 10 min (cap).
- Poll intervals: USGS 60 s (with a week refresh every 30 min), GDACS 15 min, NHC 15 min, NWS 2 min (with 24 h of
  history at startup), EONET 30 min.
- Retention: earthquakes 7 days, cyclone advisories 24 h, weather alerts 24 h after they end, EONET 7 days.
- Defaults:
  - Minimum magnitude: M2.5.
  - Earthquake window: 24 h.
  - US alerts: Warnings only.
  - Night strength: 0.85.
  - Day/night line: on.
  - All event layers: on.
  - Plate boundaries: off.
- Files go under `%LOCALAPPDATA%\EarthWatch3D` via `AppPaths`: `cache\events.json`, `cache\zones\`, `logs\`,
  `settings.json`. Tests always use a temporary `AppPaths`.
- **No PowerShell screen capture** (the user's antivirus). For visual checks, use only the app's own
  `--snapshot` diagnostics, and launch the app as few times as possible.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Work on branch `plan-2-events`.
  **Never push.**
- Tests that touch the network are tagged `[Trait("Category","Network")]`. All other tests run offline, using
  `tests/Samples` through `SampleHttpHandler`.

## Review Focus

1. **Replaying into a time with no data**, for example before the app first ran or past the 24 h storm window.
   The globe shows nothing for those layers and doesn't throw; Live brings everything back. Pinned in Task 1
   (`GoLiveAfterReplayRestores`) and Task 5 (`NothingVisibleBeforeAllEvents`).
2. **Shapes across the date line.** The NHC cone spans 177°E to 192.6°E and its track runs to −181.6°. Shapes
   must be split, never drawn as a band across the whole map or as a line across the globe. Pinned in Task 3
   (`SplitsNhcConeAtDateLine`, `SplitsUnwrappedTrack`), Task 10 (on the real KMZ) and Task 15
   (`AreaCrossingDateLineFillsBothEdgesOnly`).
3. **Offline or failing feeds.** Cached events still show, the source's dot turns amber then red, and nothing
   crashes. Pinned in Task 6 (`CorruptCacheIsDeletedAndIgnored`) and Task 7 (`FailingFeedBacksOffAndTurnsRed`).
4. **An alert storm.** Hundreds of alerts listing the same zones must not trigger hundreds of duplicate downloads
   or more than 4 at once. Pinned in Task 11 (`SameZoneRequestedOnceAndConcurrencyCapped`).
5. **Malformed or partial records:** null magnitude, empty intensity string, null geometry, unknown `Class`. The
   bad record is skipped and logged; the rest of the feed still loads. Pinned in each reader task
   (`SkipsMalformedRecords`).

---

### Task 1: Display time (TimelineViewModel) and the refactor that makes replay possible

**Files:**
- Create: `src/EarthWatch3D.App/ViewModels/TimelineViewModel.cs`
- Create: `src/EarthWatch3D.App/Controllers/Throttle.cs`
- Modify: `src/EarthWatch3D.App/ViewModels/MainViewModel.cs`, `SunMoonViewModel.cs`
- Modify: `src/EarthWatch3D.App/Controllers/SceneController.cs`, `src/EarthWatch3D.App/App.xaml.cs`,
  `src/EarthWatch3D.App/Diagnostics/SnapshotDiagnostic.cs`
- Test: `tests/EarthWatch3D.Tests/TimelineViewModelTests.cs`, `ThrottleTests.cs`; update `SunMoonViewModelTests.cs`

**Interfaces:**
- Produces:
  - `TimelineViewModel : ObservableObject`, with ctor `(IClock clock)`.
    - `static readonly TimeSpan Range = 7 days`; `static IReadOnlyList<double> Speeds = [1, 60, 600, 3600]`.
    - Observable properties: `DateTime DisplayTimeUtc`, `bool IsLive` (initially true), `bool IsPlaying`,
      `double Speed` (initially 1).
    - `DateTime MinTimeUtc` (= clock − Range) and `DateTime MaxTimeUtc` (= clock).
    - `double SliderSeconds` (seconds since MinTimeUtc). Setting it leaves live and clamps to the range.
    - `string ReadoutText`: `"LIVE 14:32:05 UTC"` or `"REPLAY 10 Nov 14:32 UTC"` (invariant culture).
    - `string LocalTimeText`.
    - `void Advance(TimeSpan wallElapsed)`:
      - live: DisplayTimeUtc = clock.
      - playing: += elapsed × Speed; on reaching MaxTimeUtc, become live.
      - otherwise: unchanged.
      - In every case, re-clamp to the range.
    - Commands: `GoLive`, `TogglePlay`, `StepMinus10Min`, `StepPlus10Min`, `StepMinus1Hour`, `StepPlus1Hour`,
      `SetSpeed(double)`. Steps leave live.
    - `event EventHandler? DisplayTimeChanged`, raised whenever DisplayTimeUtc changes.
  - `MainViewModel`, with ctor `(IClock clock)`: properties `Timeline` and `SunMoon`;
    `DateTime DisplayTimeUtc => Timeline.DisplayTimeUtc`; `event EventHandler? DisplayTimeChanged`;
    `void Tick()`, which calls `Timeline.Advance(elapsed since last Tick, measured with the clock)` and then raises
    `Ticked`. `SunMoon.Refresh(DisplayTimeUtc)` runs on every DisplayTimeChanged.
  - `SunMoonViewModel()` takes no parameters: `void Refresh(DateTime utc)` replaces `Refresh()`.
  - `Throttle(TimeSpan minGap)`: `bool TryAcquire(DateTime nowUtc)` and `DateTime NextAllowedUtc`.
  - SceneController:
    - Composes for `_vm.DisplayTimeUtc`; never `_clock.UtcNow`.
    - On `DisplayTimeChanged`: if `RecomposeSchedule.IsDue(DisplayTimeUtc)`, request a recompose through a
      250 ms `Throttle`, with a trailing one-shot DispatcherTimer for the last request.
    - Keeps the resume and recompose behaviour.
    - `_clock` is kept only for staleness. If it's unused, remove it from the constructor and update the call
      sites.

- [ ] **Step 1: Write the failing tests.**

```csharp
public class TimelineViewModelTests
{
    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void StartsLiveAtClockTime()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c); vm.Advance(TimeSpan.Zero); Assert.True(vm.IsLive); Assert.Equal(T0, vm.DisplayTimeUtc); Assert.StartsWith("LIVE", vm.ReadoutText); }

    [Fact] public void LiveFollowsClock()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c); c.UtcNow = T0.AddMinutes(3); vm.Advance(TimeSpan.FromMinutes(3)); Assert.Equal(T0.AddMinutes(3), vm.DisplayTimeUtc); }

    [Fact] public void MovingSliderLeavesLiveAndShowsReplay()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.SliderSeconds = TimeSpan.FromDays(6).TotalSeconds;
      Assert.False(vm.IsLive); Assert.Equal(T0 - TimeSpan.FromDays(1), vm.DisplayTimeUtc); Assert.StartsWith("REPLAY", vm.ReadoutText); }

    [Fact] public void SliderClampsToSevenDays()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.SliderSeconds = -5; Assert.Equal(T0 - TimeSpan.FromDays(7), vm.DisplayTimeUtc);
      vm.SliderSeconds = 1e12; Assert.Equal(T0, vm.DisplayTimeUtc); }

    [Fact] public void PlayAdvancesAtSpeedAndBecomesLiveAtNow()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c); vm.StepMinus1HourCommand.Execute(null);
      vm.SetSpeedCommand.Execute(600.0); vm.TogglePlayCommand.Execute(null);
      vm.Advance(TimeSpan.FromSeconds(3)); Assert.Equal(T0.AddHours(-1).AddMinutes(30), vm.DisplayTimeUtc);
      vm.Advance(TimeSpan.FromSeconds(10)); Assert.True(vm.IsLive); Assert.Equal(T0, vm.DisplayTimeUtc); }

    [Fact] // Review Focus 1
    public void GoLiveAfterReplayRestores()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.SliderSeconds = 0; vm.GoLiveCommand.Execute(null); Assert.True(vm.IsLive); Assert.Equal(T0, vm.DisplayTimeUtc); }

    [Fact] public void DisplayTimeChangedRaisedOnStep()
    { var vm = new TimelineViewModel(new FakeClock(T0)); int n = 0; vm.DisplayTimeChanged += (_, _) => n++; vm.StepMinus10MinCommand.Execute(null); Assert.Equal(1, n); }
}

public class ThrottleTests
{
    private static readonly DateTime T = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
    [Fact] public void AllowsThenBlocksWithinGap()
    { var t = new Throttle(TimeSpan.FromMilliseconds(250)); Assert.True(t.TryAcquire(T)); Assert.False(t.TryAcquire(T.AddMilliseconds(100))); Assert.True(t.TryAcquire(T.AddMilliseconds(250))); }
}
```

  Update `SunMoonViewModelTests`: construct with `new SunMoonViewModel()` and call `Refresh(t)`. The clock-jump
  test becomes `RefreshUsesTheGivenTime`.
- [ ] **Step 2: Run the tests to verify they fail.** Run
  `dotnet test --filter "FullyQualifiedName~TimelineViewModelTests|FullyQualifiedName~ThrottleTests"`. Expected:
  compile errors.
- [ ] **Step 3: Implement** the interfaces above.
  - App.xaml.cs keeps the 1 s DispatcherTimer calling `vm.Tick()`.
  - `--snapshot-recompose` must still work: OffsetClock +6 h → Tick → live display time jumps → recompose.
- [ ] **Step 4: Run all tests.** Run `dotnet test --filter Category!=Perf`. Expected: all pass, including the
  existing SceneController-related tests.
- [ ] **Step 5: Snapshot check.** Run once:
  `EarthWatch3D.App.exe --snapshot <scratchpad>\t1.png --snapshot-recompose`. Expected: the -a and -b images
  show the night side moved about 90° (as in Plan 1).
- [ ] **Step 6: Commit.** `"Display time owned by the timeline; scene and Sun/Moon follow it"`

---

### Task 2: Time bar view

**Files:**
- Create: `src/EarthWatch3D.App/Views/TimeBar.xaml(.cs)`
- Modify: `MainWindow.xaml` (add a bottom row: the viewport row is `*`, the bar row is `Auto`)
- Test: `tests/EarthWatch3D.Tests/TimeBarBindingTests.cs`

**Interfaces:**
- Consumes: `TimelineViewModel` (Task 1).
- Produces: `TimeBar` UserControl, bound to `MainViewModel.Timeline`. It has a named `StatusHost` ContentControl
  at its right end, which Task 13 fills.

- [ ] **Step 1: Write the failing test.** `TimeBarBindsToTimeline`: on an STA thread (copy Plan 1's
  `RunOnStaThread` helper into a test helper `Sta.Run`), create `TimeBar` with a `TimelineViewModel` DataContext,
  call `Measure`/`Arrange`, find the Slider by name `TimeSlider`, set its `Value` to 0, and assert
  `vm.IsLive == false`.
- [ ] **Step 2: Run the test to verify it fails.**
- [ ] **Step 3: Implement the layout.** One row, 40 px high, background `#CC0A0E18`. Left to right:
  - Step-back menu button (−10 min, −1 h).
  - Play/pause toggle (▶/⏸).
  - Speed ComboBox (1×, 60×, 600×, 3600×).
  - Step-forward menu button.
  - **LIVE** button: accent when IsLive, outline otherwise.
  - Slider `TimeSlider`: Minimum 0, Maximum = Range seconds, `Value` two-way bound to `SliderSeconds`. Its track
    shows a lighter band over the last 24 h (a Rectangle behind it, sized to 1/7 of the width at the right).
  - Readout TextBlock (`ReadoutText`, monospace 13 px) with a tooltip of `LocalTimeText`.
  - `StatusHost`.
- [ ] **Step 4: Run the test to verify it passes.** Then build Release with 0 warnings.
- [ ] **Step 5: Commit.** `"Time bar with live, replay, play speeds and steps"`

---

### Task 3: Geo shapes, date-line splitting, distance, point-in-polygon

**Files:**
- Create: `src/EarthWatch3D.Core/Geo/GeoShapes.cs` (GeoLine, GeoPolygon)
- Create: `src/EarthWatch3D.Core/Geo/DateLine.cs`
- Create: `src/EarthWatch3D.Core/Geo/GeoMath.cs`
- Test: `tests/EarthWatch3D.Tests/GeoShapesTests.cs`

**Interfaces:**
- Produces (namespace `EarthWatch3D.Core.Geo`):
  - `sealed record GeoLine(IReadOnlyList<GeoPosition> Points)`
  - `sealed record GeoPolygon(IReadOnlyList<IReadOnlyList<GeoPosition>> Rings)`: the first ring is the outer
    boundary, the rest are holes. No ring crosses ±180°. It has:
    - `bool Contains(GeoPosition p)`: even-odd ray cast in lon/lat, holes respected.
    - `(double MinLat, double MinLon, double MaxLat, double MaxLon) Bounds`.
  - `static class DateLine`:
    - `IReadOnlyList<GeoLine> SplitLine(IReadOnlyList<(double Lon, double Lat)> pts)`: input longitudes may be
      wrapped or unwrapped (for example 192.6 or −181.6). Output pieces are normalised and split where they cross
      ±180, with an interpolated point at ±180 on both sides.
    - `IReadOnlyList<GeoPolygon> SplitPolygon(IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> rings)`:
      unwrap the outer ring so consecutive longitudes differ by < 180. If it then lies outside (−180, 180],
      clip it against [−180, 180] and against [180, 540] (and [−540, −180]) with Sutherland–Hodgman on vertical
      edges, and shift the clipped pieces back by ∓360. Holes are kept only when nothing is split.
  - `static class GeoMath`: `double DistanceKm(GeoPosition a, GeoPosition b)` (haversine, R = 6371 km).

- [ ] **Step 1: Write the failing tests.**

```csharp
public class GeoShapesTests
{
    [Fact] // Review Focus 2, real NHC cone extent
    public void SplitsNhcConeAtDateLine()
    {
        var ring = new List<(double, double)> { (177.28, 8.0), (192.56, 9.0), (190.0, 14.0), (178.0, 12.0), (177.28, 8.0) };
        var polys = DateLine.SplitPolygon([ring]);
        Assert.Equal(2, polys.Count);
        Assert.All(polys.SelectMany(p => p.Rings[0]), q => Assert.InRange(q.LongitudeDeg, -180.0, 180.0));
        Assert.Contains(polys, p => p.Bounds.MaxLon == 180.0 && p.Bounds.MinLon == 177.28);
        Assert.Contains(polys, p => p.Bounds.MinLon == -180.0 && Math.Abs(p.Bounds.MaxLon - (192.56 - 360)) < 1e-9);
    }

    [Fact] public void PolygonNotCrossingIsUnchanged()
    { var polys = DateLine.SplitPolygon([[(10, 0), (20, 0), (20, 10), (10, 0)]]); Assert.Single(polys); Assert.Equal(4, polys[0].Rings[0].Count); }

    [Fact] // Review Focus 2, real NHC track runs to -181.6
    public void SplitsUnwrappedTrack()
    {
        var lines = DateLine.SplitLine([(-167.7, 9.9), (-175.0, 10.2), (-181.6, 10.9)]);
        Assert.Equal(2, lines.Count);
        Assert.Equal(-180.0, lines[0].Points[^1].LongitudeDeg, 9);
        Assert.Equal(180.0, lines[1].Points[0].LongitudeDeg, 9);
        Assert.Equal(178.4, lines[1].Points[^1].LongitudeDeg, 9);
    }

    [Fact] public void ContainsRespectsHoles()
    {
        var p = new GeoPolygon([
            [new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)],
            [new(4, 4), new(4, 6), new(6, 6), new(6, 4), new(4, 4)]]);
        Assert.True(p.Contains(new(2, 2))); Assert.False(p.Contains(new(5, 5))); Assert.False(p.Contains(new(20, 20)));
    }

    [Fact] public void DistanceOneDegreeAtEquator() => Assert.Equal(111.19, GeoMath.DistanceKm(new(0, 0), new(0, 1)), 1);
}
```

- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"Geo shapes with date-line splitting, distance and containment"`

---

### Task 4: Event model and EventStore

**Files:**
- Create: `src/EarthWatch3D.Core/Events/EarthEvents.cs` (records and enums)
- Create: `src/EarthWatch3D.Core/Events/AlertTypes.cs`
- Create: `src/EarthWatch3D.Core/Events/SaffirSimpson.cs`
- Create: `src/EarthWatch3D.Core/Events/EventStore.cs`
- Test: `tests/EarthWatch3D.Tests/EventStoreTests.cs`, `AlertTypesTests.cs`

**Interfaces:**
- Produces (namespace `EarthWatch3D.Core.Events`). This uses `AstroCore.Earth.GeoPosition` in place of the spec's
  "GeoPoint".

```csharp
public enum EventSource { Usgs, Gdacs, Nhc, Nws, Eonet }
public abstract record EarthEvent(EventSource Source, string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link);
public sealed record Earthquake(string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    double Magnitude, double DepthKm, string Place, DateTime UpdatedUtc) : EarthEvent(EventSource.Usgs, Id, TimeUtc, Location, Title, Link);
public sealed record WindArea(double ThresholdKmh, GeoPolygon Area);
// Id = storm key: "gdacs:<eventid>" or "nhc:<id>". TimeUtc = advisory issue time.
public sealed record CycloneAdvisory(EventSource Source, string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    string Name, double? WindKt, double? PressureHpa, IReadOnlyList<GeoLine> PastTrack, IReadOnlyList<GeoLine> ForecastTrack,
    IReadOnlyList<GeoPolygon> Cone, IReadOnlyList<WindArea> WindAreas) : EarthEvent(Source, Id, TimeUtc, Location, Title, Link)
{ public int Category => SaffirSimpson.Category(WindKt); }
// TimeUtc = sent. Location = centroid of Areas, or (0,0) until zones load. Areas empty when only ZoneIds are given.
public sealed record WeatherAlert(string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    string EventType, string Severity, DateTime OnsetUtc, DateTime EndsUtc, DateTime? CancelledUtc,
    IReadOnlyList<GeoPolygon> Areas, IReadOnlyList<string> ZoneIds, string Description) : EarthEvent(EventSource.Nws, Id, TimeUtc, Location, Title, Link);
public sealed record StormFix(DateTime TimeUtc, GeoPosition Position, double? WindKt);
// TimeUtc = first fix; Location = last fix.
public sealed record EonetStorm(string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    IReadOnlyList<StormFix> Fixes, DateTime? ClosedUtc) : EarthEvent(EventSource.Eonet, Id, TimeUtc, Location, Title, Link);

[Flags] public enum AlertTypes { None = 0, Warnings = 1, Watches = 2, Advisories = 4, Statements = 8 }
public static class AlertTypeClassifier { public static AlertTypes Classify(string eventType); }  // suffix Warning/Watch/Advisory; anything else → Statements
public static class SaffirSimpson { public static int Category(double? windKt); }  // null or <34 → -1 (TD); 34–63 → 0 (TS); 64–82 → 1; 83–95 → 2; 96–112 → 3; 113–136 → 4; ≥137 → 5

public sealed record EventSnapshot(IReadOnlyList<Earthquake> Earthquakes, IReadOnlyList<CycloneAdvisory> Cyclones,
    IReadOnlyList<WeatherAlert> Alerts, IReadOnlyList<EonetStorm> Eonet, long Version)
{ public static readonly EventSnapshot Empty; }

public sealed class EventStore(IClock clock)
{
    public EventSnapshot Snapshot { get; }                 // immutable; replaced atomically on every change
    public event EventHandler? Changed;                     // raised on the writing thread after Snapshot is replaced
    public void Load(EventSnapshot cached);                 // replaces everything (cache load at startup)
    public void UpsertEarthquakes(IEnumerable<Earthquake> quakes);                           // by Id; newer UpdatedUtc wins
    public void ReplaceEarthquakeWeek(IReadOnlyCollection<Earthquake> week, DateTime weekStartUtc); // upsert, then remove quakes at or after weekStartUtc that are absent (deleted)
    public void AddCycloneAdvisories(IEnumerable<CycloneAdvisory> advisories);              // key (Id, TimeUtc); same key replaces
    public void UpsertAlerts(IEnumerable<WeatherAlert> alerts);                               // by Id
    public void UpsertEonet(IEnumerable<EonetStorm> storms);                                  // by Id
    public void Prune();   // retention per Global Constraints, relative to clock.UtcNow
}
```

- [ ] **Step 1: Write the failing tests.**
  - `UpsertReplacesRevisedQuake`: same Id with a newer UpdatedUtc changes the magnitude; with an older one it
    doesn't.
  - `WeekRefreshRemovesDeletedQuakes`: a quake inside the week window that is absent from the new week list is
    removed; one older than weekStartUtc is kept.
  - `AdvisoriesKeyedByStormAndTime`: two advisories for one storm at different times are both kept; the same
    time replaces.
  - `PruneAppliesRetention`: with the clock at T, a quake at T−8d, an advisory at T−25h, an alert ended at T−25h
    and an EONET storm whose last fix is at T−8d are all removed; things just inside the limits are kept.
  - `ChangedRaisedAndVersionIncrements`.
  - `ConcurrentWritersDoNotLoseUpdates`: 8 threads each upsert 1,000 distinct quakes; the count is 8,000.
  - `AlertTypesTests`:
    - `"Tornado Warning"` → Warnings, `"Flood Watch"` → Watches, `"Small Craft Advisory"` → Advisories,
      `"Special Weather Statement"` → Statements, `"Air Quality Alert"` → Statements.
    - SaffirSimpson boundaries: 33 → −1, 34 → 0, 64 → 1, 137 → 5, null → −1.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.** One private lock for writers. Readers just read the `Snapshot` field, which is
  volatile. Lists inside a snapshot are never mutated after publishing.
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"Event model and thread-safe EventStore with retention"`

---

### Task 5: Visibility at the display time, cyclone merging, EONET de-duplication

**Files:**
- Create: `src/EarthWatch3D.Core/Events/EventVisibility.cs`
- Create: `src/EarthWatch3D.Core/Events/CycloneMerger.cs`
- Test: `tests/EarthWatch3D.Tests/EventVisibilityTests.cs`, `CycloneMergerTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record DisplayFilter(bool ShowQuakes, bool ShowCyclones, bool ShowAlerts, bool ShowEonet,
    double MinMagnitude /* double.NegativeInfinity = All */, TimeSpan QuakeWindow, AlertTypes AlertTypes)
{ public static readonly DisplayFilter Default = new(true, true, true, true, 2.5, TimeSpan.FromHours(24), AlertTypes.Warnings); }

public sealed record VisibleQuake(Earthquake Quake, double AgeFraction);   // 0 = now, 1 = end of window
public sealed record VisibleEonet(EonetStorm Storm, StormFix Fix);
public sealed record VisibleEvents(IReadOnlyList<VisibleQuake> Quakes, IReadOnlyList<CycloneAdvisory> Cyclones,
    IReadOnlyList<WeatherAlert> Alerts, IReadOnlyList<VisibleEonet> Eonet)
{ public static readonly VisibleEvents None; }

public static class EventVisibility { public static VisibleEvents At(EventSnapshot s, DateTime t, DisplayFilter f); }
public static class CycloneMerger
{
    public const double SameStormKm = 300;
    public static string NormalizeName(string name);   // drop a trailing "-YY" (GDACS), upper-case, trim
    public static IReadOnlyList<CycloneAdvisory> Merge(IReadOnlyList<CycloneAdvisory> latestPerStorm);
}
```

  Rules (spec §4 "What is visible at time t"):
  - Quakes: `TimeUtc ≤ t`, `t − TimeUtc ≤ QuakeWindow`, and Magnitude ≥ MinMagnitude.
  - Cyclones: the latest advisory per Id with TimeUtc ≤ t and t − TimeUtc ≤ 24 h, then `Merge`. Merge rule:
    - An NHC advisory and a GDACS advisory are the same storm when their NormalizeName values are equal and they
      are within 300 km.
    - Keep the NHC advisory, adopting the GDACS WindAreas when NHC has none, and drop the GDACS one.
  - Alerts: `OnsetUtc ≤ t < min(EndsUtc, CancelledUtc ?? max)`, and `Classify(EventType) & f.AlertTypes != 0`.
  - EONET: the latest fix ≤ t, not closed before t, and no visible merged cyclone within 300 km of that fix.

- [ ] **Step 1: Write the failing tests.**
  - `QuakeWindowAndMagnitudeFilter`, including that `AgeFraction` is 0.5 at half the window.
  - `LatestAdvisoryAtTimeIsUsed`: advisories at t−12 h and t−6 h → the t−6 h one; at a t before both → none.
  - `StormHiddenAfter24hWithoutAdvisory`.
  - `AlertActiveWindowAndTypeFilter`: a Watch is hidden by default and shown with `Watches`; a cancelled alert
    is hidden after CancelledUtc.
  - `EonetHiddenNearMergedCyclone` (100 km away) and `EonetShownWhenAlone` (1,000 km away).
  - `NothingVisibleBeforeAllEvents` (Review Focus 1): t = earliest event − 1 h → `VisibleEvents` with all lists
    empty.
  - `CycloneMergerTests`:
    - `NormalizeName("PENA-26") == NormalizeName("Pena")`.
    - Merge of NHC Pena and GDACS PENA-26 at the same position → 1 advisory, the NHC one, carrying GDACS's
      2 wind areas.
    - Same names 500 km apart → 2 advisories.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"Event visibility at display time with cyclone merge and EONET de-duplication"`

---

### Task 6: Paths, log file, settings and event cache

**Files:**
- Create: `src/EarthWatch3D.Core/Storage/AppPaths.cs`, `EventCache.cs`, `SettingsStore.cs`, `AppSettings.cs`
- Create: `src/EarthWatch3D.Core/Logging/IAppLog.cs` (`IAppLog`, `NullLog`), `FileLog.cs`
- Test: `tests/EarthWatch3D.Tests/StorageTests.cs`, `FileLogTests.cs`

**Interfaces:**
- Produces:
  - `sealed record AppPaths(string Root)`: properties `CacheDir`, `ZonesDir`, `LogsDir`, `SettingsFile`, and
    `EventsFile` (= CacheDir\events.json); `static AppPaths Default` (%LOCALAPPDATA%\EarthWatch3D);
    `void EnsureCreated()`.
  - `interface IAppLog { void Info(string m); void Warn(string m, Exception? e = null); void Error(string m, Exception? e = null); }`
    and `NullLog : IAppLog`.
  - `FileLog(string logsDir, IClock clock) : IAppLog, IDisposable`:
    - One file per local day, `earthwatch3d-yyyyMMdd.log`.
    - Lines look like `2026-11-12T14:03:05.123Z WARN message | exception`.
    - Thread-safe.
    - Files older than 7 days are deleted when it's created.
  - `sealed record AppSettings`:
    - `ShowQuakes = true`, `ShowCyclones = true`, `ShowAlerts = true`, `ShowEonet = true`,
      `ShowCoastlines = true`.
    - `double MinMagnitude = 2.5` (NaN = All), `int QuakeWindowDays = 1` (1 or 7),
      `AlertTypes AlertTypes = Warnings`.
    - `double NightStrength = 0.85`, `bool ShowTerminator = true`, `bool LayerStripExpanded = false`.
    - `DisplayFilter ToFilter()`. A MinMagnitude of NaN maps to `double.NegativeInfinity`.

    Plan 3 adds more keys. Unknown JSON keys are ignored, and missing keys take their defaults.
  - `SettingsStore(AppPaths paths, IAppLog log)`:
    - `AppSettings Load()` returns defaults when the file is missing or corrupt, logging a warning if corrupt.
    - `void Save(AppSettings s)` writes atomically (temp file, then `File.Move` with overwrite).
  - `EventCache(AppPaths paths, IAppLog log)`:
    - `EventSnapshot? Load()` returns null when the file is missing. When it is corrupt, it deletes the file,
      logs a warning and returns null.
    - `void Save(EventSnapshot s)` writes atomically. It uses a DTO with four typed lists.
- [ ] **Step 1: Write the failing tests.**
  - `SettingsRoundTripAndDefaults`.
  - `CorruptSettingsGiveDefaults`.
  - `EventCacheRoundTrip`, covering one of each event type, including polygons and tracks.
  - `CorruptCacheIsDeletedAndIgnored` (Review Focus 3).
  - `FileLogWritesAndPrunesOldFiles`: create an 8-day-old file, construct the log, and assert the old file is
    gone and the new line was written.

  Use a temp directory per test.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"App paths, log file, settings store and event cache"`

---

### Task 7: HTTP, sample handler, retry delays and the feed scheduler

**Files:**
- Create: `src/EarthWatch3D.Core/Feeds/FeedHttp.cs`, `IEventFeed.cs`, `FeedStatus.cs`, `Backoff.cs`,
  `FeedScheduler.cs`
- Create: `src/EarthWatch3D.Core/Feeds/SampleHttpHandler.cs`. This lives in Core so the app's offline diagnostic
  (Task 17) can use it too.
- Test: `tests/EarthWatch3D.Tests/FeedSchedulerTests.cs`, `FeedHttpTests.cs`

**Interfaces:**
- Produces (namespace `EarthWatch3D.Core.Feeds`):
  - `FeedHttp(HttpMessageHandler? handler = null) : IDisposable`:
    - `Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct)` and `Task<string> GetStringAsync(Uri u, CancellationToken ct)`.
    - `event Action<long>? BytesReceived`, used by Plan 3's DownloadMeter.
    - Uses the UA, 30 s timeout and decompression from Global Constraints.
    - Non-success HTTP status codes throw `HttpRequestException`.
  - `SampleHttpHandler : HttpMessageHandler`:
    - `Map(string urlPrefix, string filePath)` and `MapText(string urlPrefix, string body)`.
    - It answers the longest matching prefix and returns 404 otherwise.
    - It records `Requests` (a list of the URLs it was asked for) for assertions.
  - `interface IEventFeed { string Name { get; } TimeSpan Interval { get; } Task RunOnceAsync(CancellationToken ct); }`
  - `enum FeedState { NotStarted, Ok, Retrying, Failing, NotSetUp }`
  - `sealed record FeedStatus(string Name, FeedState State, DateTime? LastSuccessUtc, string? LastError, int ConsecutiveFailures, DateTime NextRunUtc)`
  - `static class Backoff { public static TimeSpan Delay(int consecutiveFailures); }`: 1 → 30 s, 2 → 1 min,
    3 → 2 min, 4 → 5 min, ≥ 5 → 10 min.
  - `FeedScheduler(IEnumerable<IEventFeed> feeds, IClock clock, IAppLog log) : IDisposable`:
    - `IReadOnlyList<FeedStatus> Statuses`.
    - `event EventHandler<FeedStatus>? StatusChanged`.
    - `Task TickAsync(CancellationToken ct)`: runs every due feed in parallel. On success: Ok, and next =
      now + Interval. On failure: failures + 1, State = Retrying for 1–2 failures and Failing for ≥ 3, next =
      now + Backoff, and the error is logged.
    - `void Start()`: a background loop with `PeriodicTimer`(5 s) calling TickAsync. `Stop()` and `Dispose()`
      cancel it.
    - All feeds are due at start.
- [ ] **Step 1: Write the failing tests** (fake clock, fake feeds, no real timers):
  - `DueFeedsRunAndReschedule`: two feeds (60 s and 15 min). Tick at T0 → both run. Tick at T0+61 s → only the
    first runs.
  - `FailingFeedBacksOffAndTurnsRed` (Review Focus 3): a feed that throws. Ticks at T0, then when due each time:
    the states are Retrying, Retrying, Failing and the delays 30 s, 1 min, 2 min. A success then gives Ok and
    ConsecutiveFailures 0. StatusChanged was raised each time.
  - `BackoffSequence`: 1..6 → 30 s, 60 s, 120 s, 300 s, 600 s, 600 s.
  - `FeedHttpSendsUserAgentAndCountsBytes`: through a recording handler. The UA equals `AppInfo.UserAgent`, and
    `BytesReceived` sums to the body length.
  - `SampleHandlerLongestPrefixAnd404`.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"Feed HTTP, scheduler with retry delays and status, sample handler"`

---

### Task 8: USGS earthquakes

**Files:**
- Create: `src/EarthWatch3D.Core/Feeds/Usgs/UsgsReader.cs`, `UsgsFeed.cs`
- Test: `tests/EarthWatch3D.Tests/UsgsTests.cs`

**Interfaces:**
- Produces:
  - `static class UsgsReader { public static IReadOnlyList<Earthquake> Read(Stream geojson, IAppLog log); }`:
    - Keeps only `properties.type == "earthquake"`.
    - Skips records with a null `mag` or missing coordinates (logged; Review Focus 5).
    - Title = `properties.title`, Link = `properties.url`.
  - `UsgsFeed(FeedHttp http, EventStore store, IClock clock, IAppLog log) : IEventFeed`: Name "USGS", Interval
    60 s.
    - The first run, and any run 30 min or more after the last week fetch, gets the week feed and calls
      `ReplaceEarthquakeWeek(list, now − 7 days)`.
    - Other runs get the hour feed and call `UpsertEarthquakes`.
    - URLs as in data-sources.md §1.
- [ ] **Step 1: Write the failing tests** (using `tests/Samples/usgs_all_week.geojson`, copied to the test output
  with `<None Include="..\Samples\**" CopyToOutputDirectory="PreserveNewest" LinkBase="Samples" />` in the test
  csproj, added in this task):
  - `ReadsWeekSample`: 1,806 quakes (the earthquake type only). Exactly 319 have Magnitude ≥ 2.5.
  - `FirstFeatureFields`: id `aka2026wjnmsm`, magnitude 0.7, TimeUtc = epoch ms 1794428350078 (Kind Utc),
    Location (60.018, −153.06), DepthKm 5.
  - `SkipsMalformedRecords` (Review Focus 5): an inline FeatureCollection with one good record, one with
    `"mag": null`, and one with `"geometry": null` → 1 quake and 2 warnings logged (use a recording IAppLog).
  - `FeedFetchesWeekThenHour`: through SampleHttpHandler mapping both URLs. Run 1 requests the week URL; run 2
    (clock +60 s) requests the hour URL; run 3 (clock +31 min) requests the week URL again.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"USGS earthquake reader and feed"`

---

### Task 9: GDACS tropical cyclones

**Files:**
- Create: `src/EarthWatch3D.Core/Feeds/Gdacs/GdacsReader.cs`, `GdacsFeed.cs`
- Test: `tests/EarthWatch3D.Tests/GdacsTests.cs`

**Interfaces:**
- Produces:
  - `sealed record GdacsEvent(int EventId, int EpisodeId, string EventName, bool IsCurrent, string Source, GeoPosition Position, double? WindKmh, DateTime ModifiedUtc, Uri GeometryUrl, Uri? ReportUrl)`
  - `static class GdacsReader`:
    - `IReadOnlyList<GdacsEvent> ReadEventList(Stream s, IAppLog log)`: `iscurrent` is a string; dates have no
      zone and are UTC; WindKmh comes from `severitydata.severity`.
    - `CycloneAdvisory ReadGeometry(Stream s, GdacsEvent e, IAppLog log)`:
      - Groups features by `Class` prefix.
      - `Point_Centroid` → Location.
      - `Line_Line_*` → PastTrack when `forecast` is false, ForecastTrack when true. Each LineString goes through
        `DateLine.SplitLine`.
      - `Poly_Cones` → Cone, via `SplitPolygon` for each polygon of the MultiPolygon.
      - `Poly_Green`/`Poly_Orange`/`Poly_Red` whose `polygonlabel` ends in `km/h` → WindAreas, with
        ThresholdKmh parsed from the label. Other wind bands (forecast steps) and `Point_Polygon_Point_*` are
        ignored.
      - Unknown classes are logged once and ignored.
      - Id = `"gdacs:" + EventId`, TimeUtc = ModifiedUtc, Name = EventName, WindKt = WindKmh / 1.852,
        Title = `"Tropical cyclone " + EventName`, Link = ReportUrl.
  - `GdacsFeed(FeedHttp http, EventStore store, IClock clock, IAppLog log) : IEventFeed`: Name "GDACS", Interval
    15 min.
    - Lists the last 2 days with all three alert levels.
    - Fetches geometry only for an (EventId, EpisodeId, ModifiedUtc) not seen before; that set is remembered in
      memory.
    - `AddCycloneAdvisories`.
- [ ] **Step 1: Write the failing tests** (samples `gdacs_events_tc.json`, `gdacs_geometry.json`):
  - `ReadsEventList`: 4 events.
    - 1001348/20 `PENA-26`, current, NOAA, at (9.9, −167.7).
    - 1001347/22 `CEMPAKA-26`, JTWC.
    - 1001349/6 `02-20262027-27`, RSMC.
    - 1001343/52 `IN-FA-26`, IsCurrent false.
    - All ModifiedUtc values have Kind Utc.
  - `ReadsGeometryRoles`:
    - Location (9.9, −167.7).
    - Past track built from 19 segments, forecast track from 6, counting segments before splitting.
    - Exactly 2 current wind areas, at 60 and 90 km/h.
    - The cone is non-empty, and every longitude is in [−180, 180].
  - `SkipsMalformedRecords`: an event-list feature with no `eventid` is skipped and logged.
  - `FeedFetchesGeometryOncePerEpisode`: two runs with the same list → the geometry URL is requested once.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"GDACS cyclone reader and feed"`

---

### Task 10: NHC tropical cyclones and the KMZ/KML reader

**Files:**
- Create: `src/EarthWatch3D.Core/Feeds/Nhc/NhcReader.cs`, `KmlReader.cs`, `NhcFeed.cs`
- Test: `tests/EarthWatch3D.Tests/NhcTests.cs`

**Interfaces:**
- Produces:
  - `sealed record NhcStorm(string Id, string Name, string Classification, double? WindKt, double? PressureMb, GeoPosition Position, DateTime LastUpdateUtc, string? TrackAdv, Uri? TrackKmz, string? ConeAdv, Uri? ConeKmz)`
  - `static class NhcReader { public static IReadOnlyList<NhcStorm> ReadCurrentStorms(Stream s, IAppLog log); }`:
    `intensity` and `pressure` are strings; an empty or non-numeric value gives null. An empty `activeStorms`
    gives an empty list.
  - `static class KmlReader`:
    - `(IReadOnlyList<GeoLine> Lines, IReadOnlyList<GeoPosition> Points, IReadOnlyList<GeoPolygon> Polygons) ReadKmz(Stream kmz)`.
    - Opens the zip, reads the first `.kml`, and collects every LineString, Point and Polygon (outer boundary) in
      KML's namespace-agnostic way: match on local names.
    - Coordinates are `lon,lat[,alt]` separated by whitespace, and go through `DateLine.SplitLine` /
      `SplitPolygon`.
  - `NhcFeed(FeedHttp http, EventStore store, IAppLog log) : IEventFeed`: Name "NHC", Interval 15 min.
    - For each storm, downloads the track and cone KMZ only when TrackAdv/ConeAdv changed.
    - Builds a `CycloneAdvisory`:
      - Id = `"nhc:" + Id`, TimeUtc = LastUpdateUtc, Name = Name.
      - Forecast track = the KML lines; PastTrack empty (NHC's forecast KMZ has no history).
      - Cone = the KML polygons; WindAreas empty.
      - Title = `$"{Classification} {Name}"`.
      - Link = `https://www.nhc.noaa.gov/`.
- [ ] **Step 1: Write the failing tests** (samples `nhc_CurrentStorms.json`, `nhc_track.kmz`, `nhc_cone.kmz`):
  - `ReadsCurrentStorms`: 1 storm.
    - Id `cp042026`, Name `Pena`, Classification `TS`, WindKt 40, PressureMb 1002, Position (9.9, −167.7).
    - LastUpdateUtc 2026-11-11T15:00Z, TrackAdv `020`.
  - `ConeKmzSplitsAtDateLine` (Review Focus 2): 2 polygons with every longitude in [−180, 180]; one reaches 180
    and the other −180.
  - `TrackKmzSplitsAtDateLine`: the lines include a piece ending at −180 and a piece starting at 180 that reaches
    178.4.
  - `EmptyActiveStormsGivesNoStorms`: `{"activeStorms":[]}` → empty.
  - `SkipsMalformedRecords`: a storm with `"intensity": ""` is still read, with WindKt null; a storm with no `id`
    is skipped and logged.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"NHC reader, KMZ/KML reader with date-line handling, and feed"`

---

### Task 11: NWS alerts and the zone cache

**Files:**
- Create: `src/EarthWatch3D.Core/Feeds/Nws/NwsReader.cs`, `ZoneCache.cs`, `NwsFeed.cs`
- Test: `tests/EarthWatch3D.Tests/NwsTests.cs`, `ZoneCacheTests.cs`

**Interfaces:**
- Produces:
  - `static class NwsReader`:
    - `(IReadOnlyList<WeatherAlert> Alerts, Uri? Next) ReadPage(Stream s, IAppLog log)`.
    - Times: OnsetUtc = `onset ?? effective`; EndsUtc = `ends ?? expires`.
    - CancelledUtc is set when `messageType == "Cancel"`.
    - Geometry is read with SplitPolygon when present. Otherwise Areas is empty and ZoneIds = `affectedZones`
      (full URLs).
    - Location = the centroid of the first area's outer ring, or (0, 0).
    - Title = `headline ?? event`.
    - Link = the alert `id` when it is an https URL, otherwise null.
    - `Next` = `pagination.next`, or null.
  - `ZoneCache(FeedHttp http, AppPaths paths, IClock clock, IAppLog log, int maxConcurrent = 4)`:
    - `bool TryGet(string zoneUrl, out IReadOnlyList<GeoPolygon> areas)`: memory, then disk
      (`ZonesDir/<last URL segment>.json`, valid for 30 days).
    - `void Request(IEnumerable<string> zoneUrls)`: queues unique URLs that aren't cached or already queued, and
      downloads with at most `maxConcurrent` in flight.
    - `event EventHandler? ZonesLoaded`, raised after each batch.
    - `int InFlightPeak`, for tests.
  - `NwsFeed(FeedHttp http, EventStore store, ZoneCache zones, Func<AlertTypes> filter, IClock clock, IAppLog log) : IEventFeed`:
    Name "NWS", Interval 2 min.
    - The first run reads history from now − 24 h, following `Next` until null.
    - Every run reads `/alerts/active` and calls `UpsertAlerts`.
    - It then calls `zones.Request` for the ZoneIds of alerts whose type passes `filter()` and are active now.
- [ ] **Step 1: Write the failing tests** (sample `nws_active.json`, `nws_zone.json`):
  - `ReadsActiveSample`: 428 alerts, 419 with empty Areas and non-empty ZoneIds, 138 classified as Warnings.
    `Next` is null.
  - `FollowsPaginationOnFirstRun`: SampleHttpHandler serves a history page with `pagination.next` pointing to a
    second page (inline JSON, 1 alert each) and the active sample. After run 1, the store holds 428 + 2 alerts
    (distinct ids), and both history URLs were requested.
  - `ZoneParsedAndCachedToDisk`: `nws_zone.json` (PZZ150) → 1 polygon. A second ZoneCache on the same directory
    finds it without HTTP. With the clock +31 days, it re-downloads.
  - `SameZoneRequestedOnceAndConcurrencyCapped` (Review Focus 4): request 50 URLs where 10 are distinct, through
    a handler that delays 50 ms per request. Exactly 10 downloads happen and `InFlightPeak ≤ 4`.
  - `FeedRequestsZonesOnlyForFilteredAlerts`: with the filter set to Warnings, only the ZoneIds of warning alerts
    are requested.
  - `SkipsMalformedRecords`: a feature with no `properties.event` is skipped and logged.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"NWS alert reader with history paging, zone cache and feed"`

---

### Task 12: NASA EONET

**Files:**
- Create: `src/EarthWatch3D.Core/Feeds/Eonet/EonetReader.cs`, `EonetFeed.cs`
- Test: `tests/EarthWatch3D.Tests/EonetTests.cs`

**Interfaces:**
- Produces:
  - `static class EonetReader { public static IReadOnlyList<EonetStorm> Read(Stream s, IAppLog log); }`:
    - Fixes are sorted by date.
    - WindKt = `magnitudeValue` when `magnitudeUnit == "kts"`.
    - ClosedUtc comes from `closed`.
    - Link = `link`.
  - `EonetFeed(FeedHttp http, EventStore store, IAppLog log) : IEventFeed`: Name "EONET", Interval 30 min,
    URL as in data-sources.md §5.
- [ ] **Step 1: Write the failing tests** (sample `eonet_storms.json`):
  - `ReadsSample`: 4 storms.
    - `EONET_26159` "Tropical Cyclone 03S" with 5 fixes, `EONET_26157` with 19, `EONET_26158` with 20.
    - `EONET_25728` with 51 fixes and ClosedUtc 2026-11-10T00:00Z.
    - Fixes ascend in time.
  - `SkipsMalformedRecords`: a fix with no coordinates is skipped and the storm is kept; a storm with no fixes is
    skipped and logged.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"EONET severe storm reader and feed"`

---

### Task 13: Layer strip, ⋯ Display flyout and status strip

**Files:**
- Create: `src/EarthWatch3D.App/ViewModels/LayersViewModel.cs`, `DisplayOptionsViewModel.cs`,
  `StatusViewModel.cs`
- Create: `src/EarthWatch3D.App/Views/LayerStrip.xaml(.cs)`, `DisplayFlyout.xaml(.cs)`, `StatusStrip.xaml(.cs)`
- Modify: `MainViewModel.cs` (it owns them), `MainWindow.xaml`, `TimeBar.xaml` (`StatusHost` content)
- Test: `tests/EarthWatch3D.Tests/LayersAndDisplayViewModelTests.cs`, `StatusViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `SettingsStore` (Task 6), `DisplayFilter` (Task 5), `FeedStatus`/`FeedState` (Task 7).
- Produces:
  - `interface ISettingsStore { AppSettings Load(); void Save(AppSettings s); }`, implemented by `SettingsStore`.
    Add it in this task.
  - `LayersViewModel(ISettingsStore store)`: observable `ShowQuakes`, `ShowCyclones`, `ShowAlerts`,
    `ShowEonet`, `ShowCoastlines` and `IsExpanded`. Any change saves.
  - `DisplayOptionsViewModel(ISettingsStore store)`:
    - `IReadOnlyList<(string Label, double Value)> MagnitudeChoices` = All (NaN), 1, 2.5, 4, 5, 6.
    - Observable `MinMagnitude`, `QuakeWindowDays` (1 or 7), and `ShowWarnings`, `ShowWatches`,
      `ShowAdvisories`, `ShowStatements`.
    - Observable `NightStrength` (0–1) and `ShowTerminator`. Any change saves.
  - `MainViewModel`:
    - `DisplayFilter CurrentFilter`, built from Layers and Display.
    - `event EventHandler? FilterChanged`.
    - `MainViewModel(IClock clock, ISettingsStore settings)`. Update App.xaml.cs and the tests.
  - `StatusViewModel(Dispatcher? dispatcher)`:
    - `ObservableCollection<StatusItem> Items`, where `StatusItem(string Name, FeedState State, string Tooltip)`.
    - `void Apply(FeedStatus s)` marshals to the dispatcher when one is given.
    - Tooltip = `"USGS: updated 14:03 UTC"` or `"NWS: retrying (timeout) – next try 14:05 UTC"`.
  - Views: the layer strip, flyout and status dots follow the spec §7 sketch. Dot colours:
    - Ok: #34C759
    - Retrying: #FFCC00
    - Failing: #FF3B30
    - NotSetUp / NotStarted: #8E8E93

    The layer buttons' tooltips are legends: quake age colours, NWS colours, storm categories.
  - **SceneController uses the settings:**
    - `NightStrength` and `ShowTerminator` (replacing the Plan 1 constant), and `ShowCoastlines` (lines omitted
      when off).
    - It recomposes when they change.
- [ ] **Step 1: Write the failing tests.**
  - `TogglingLayerSavesSettings` (fake store).
  - `FilterReflectsDisplayOptions`: "All" gives `NegativeInfinity`; Watches on gives the Warnings|Watches flags;
    7 days gives a 7-day window.
  - `FilterChangedRaised`.
  - `StatusItemsAddedAndUpdated`: two Apply calls for the same name update one item.
  - `TooltipTextForEachState`.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement** the view models and views.
- [ ] **Step 4: Run the tests to verify they pass.** Build Release with 0 warnings.
- [ ] **Step 5: Commit.** `"Layer strip, Display flyout and status strip, backed by saved settings"`

---

### Task 14: Drawing events in 3D (quakes, cyclones, EONET) and their labels

**Files:**
- Create: `src/EarthWatch3D.App/Rendering/EventScene.cs`, `QuakeStyle.cs`, `StormStyle.cs`, `SphereLines.cs`
- Create: `src/EarthWatch3D.App/Rendering/LabelOverlay.cs`, copied from
  `C:\repos\SolarSystem3D\src\SolarSystem3D.App\Rendering\LabelOverlay.cs` with the namespace changed and its
  palette reference replaced with plain colours.
- Modify: `SceneController.cs`
- Test: `tests/EarthWatch3D.Tests/EventStyleTests.cs`, `SphereLinesTests.cs`

**Interfaces:**
- Consumes: `VisibleEvents` (Task 5), `Projector`, `GeoPosition`.
- Produces:
  - `static class QuakeStyle`:
    - `double RadiusEarthRadii(double mag)` = clamp(0.004 × 1.5^(mag − 2.5), 0.0015, 0.05).
    - `int ColorBucket(double ageFraction)`: < 0.1 → 0, < 0.35 → 1, < 0.7 → 2, else 3.
    - `Color BucketColor(int b)`: #FF3B30, #FF9500, #FFD60A, #9C8F5A.
    - `double MinRadiusForView(double cameraDistance, double fovHorizontalDeg, double viewportWidthPx)`: the world
      size of 3 px at the nearest surface point = 3 × 2 × (d − 1) × tan(fov/2) / width.
  - `static class StormStyle`: `Color CategoryColor(int category)`:
    - −1: #5EBAFF
    - 0: #00FAF4
    - 1: #FFFFCC
    - 2: #FFE775
    - 3: #FFC140
    - 4: #FF8F20
    - 5: #FF6060
  - `static class SphereLines`:
    - `IReadOnlyList<Point3D> Densify(GeoLine line, double radius, double maxStepDeg = 1.0)`: great-circle
      subdivision.
    - `IReadOnlyList<IReadOnlyList<Point3D>> Dashes(IReadOnlyList<Point3D> path, double dashDeg = 1.5, double gapDeg = 1.0)`.
  - `EventScene(HelixViewport3D viewport)`: `void Update(VisibleEvents v, bool live, Projector projector, double cameraDistance)`.
    - **Quakes:**
      - Flat discs tangent to the sphere at r = 1.002, radius = max(RadiusEarthRadii, MinRadiusForView).
      - One merged `MeshGeometry3D` per colour bucket, built with HelixToolkit `MeshBuilder`.
      - Meshes are rebuilt when the visible set changes or the camera distance changes by > 20%.
    - **Pulsing rings** for quakes < 1 h old, only when live: a `LinesVisual3D` circle at 1.0–1.8× the disc
      radius, animated over 1.5 s on CompositionTarget.Rendering.
    - **Cyclones:**
      - A `BillboardVisual3D` storm glyph at the centre: a 22 px spiral DrawingImage in CategoryColor.
      - The past track as a `LinesVisual3D` (white, thickness 1.5) along Densify(r = 1.003).
      - The forecast track as Dashes (white, 1.5).
    - **EONET:** a 14 px billboard dot in CategoryColor(Category of WindKt), and the last 8 fixes as a thin grey
      line.
  - Labels (LabelOverlay on the existing overlay Canvas, recomputed whenever the overlay is dirty):
    - `"M6.1"` for visible quakes with M ≥ 5.
    - The storm name plus category (`"PENA TS"`, `"CEMPAKA C2"`).
    - Only for points on the visible side (`MarkerHover.IsOnVisibleSide`-style test).
  - SceneController calls `EventScene.Update` when any of these happen: DisplayTimeChanged (throttled with the
    recompose throttle), the store's `Changed` (marshalled to the UI thread), FilterChanged, or a camera change
    (for the min size and labels).
- [ ] **Step 1: Write the failing tests.**
  - `QuakeRadiusScalesAndClamps`: M2.5 → 0.004; M7 → 0.004 × 1.5^4.5; M0 → 0.0015; M9.5 → 0.05.
  - `ColorBucketBoundaries`: 0.09 → 0, 0.1 → 1, 0.69 → 2, 0.7 → 3.
  - `MinRadiusForView`: d = 3, fov 45, width 1000 → 3 × 2 × 2 × tan(22.5°) / 1000.
  - `CategoryColors`: −1 → #5EBAFF, 5 → #FF6060.
  - `DensifyStaysOnSphereAndStepsAreSmall`: every point has |p| = r, and adjacent angles are ≤ 1°.
  - `DashesAlternate`: a 10° path gives 4 dashes.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement** EventScene and the SceneController wiring.
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"3D quakes, storm glyphs and tracks, EONET storms, labels"`

---

### Task 15: Area overlay (alerts, cones, wind bands) on the globe image

**Files:**
- Create: `src/EarthWatch3D.App/Rendering/AreaRasterizer.cs`, `AlertColors.cs`
- Modify: `SceneController.cs` (builds the Areas layer and passes it to `GlobeLayers.Areas`)
- Test: `tests/EarthWatch3D.Tests/AreaRasterizerTests.cs`, `AlertColorsTests.cs`

**Interfaces:**
- Consumes: `VisibleEvents`, `ZoneCache.TryGet` (Task 11), `GlobeLayers.Areas`.
- Produces:
  - `static class AlertColors`: `Color For(string eventType)`. NWS hazard colours:

    | Event type | Colour |
    |---|---|
    | Tornado Warning | #FF0000 |
    | Severe Thunderstorm Warning | #FFA500 |
    | Flash Flood Warning | #8B0000 |
    | Flood Warning | #00FF00 |
    | Winter Storm Warning | #FF69B4 |
    | Blizzard Warning | #FF4500 |
    | Ice Storm Warning | #8B008B |
    | Hurricane Warning | #DC143C |
    | Tropical Storm Warning | #B22222 |
    | Storm Warning | #9400D3 |
    | Gale Warning | #DDA0DD |
    | High Wind Warning | #DAA520 |
    | Excessive Heat Warning | #C71585 |
    | Red Flag Warning | #FF1493 |
    | Special Marine Warning | #FFA500 |
    | Tornado Watch | #FFFF00 |
    | Severe Thunderstorm Watch | #DB7093 |
    | Flood Watch | #2E8B57 |
    | Winter Storm Watch | #4682B4 |
    | Hurricane Watch | #FF00FF |
    | Small Craft Advisory | #D8BFD8 |
    | Wind Advisory | #D2B48C |
    | Anything else | #A0A0A0 |

  - `static class AreaRasterizer`:
    - `Bgra32Image Rasterize(IEnumerable<(GeoPolygon Area, Color Color)> areas, int width, int height)`.
    - Runs on an STA thread (reuse `RunOnStaThread`).
    - A `DrawingVisual` in equirectangular projection draws each polygon's fill at 35% alpha and a 1-px edge at
      100% alpha.
    - The result is a new image each call and is never mutated afterwards. The composer may be reading the
      previous one.
  - SceneController builds the area list from the visible events:
    - Alerts: their Areas, or `ZoneCache.TryGet` for each ZoneId, with the AlertColors colour.
    - Cyclone cones: white.
    - Wind areas: 60 km/h → #34C759, 90 km/h → #FF9500, 120 km/h and up → #FF3B30.

    It re-rasterizes only when the area set changes: compare a hash of (alert ids + zone-loaded count + cyclone
    ids/times). It also re-rasterizes on `ZoneCache.ZonesLoaded`. Then it recomposes.
- [ ] **Step 1: Write the failing tests.**
  - `AlertColorsKnownAndFallback`.
  - `FillsInteriorAtThirtyFivePercent`: a square (lat 0–10, lon 0–10) on a 360×180 image. The centre pixel has
    alpha 89 (35% of 255, ±1); a far pixel has alpha 0.
  - `AreaCrossingDateLineFillsBothEdgesOnly` (Review Focus 2): the NHC-like cone split by `DateLine.SplitPolygon`.
    Pixels at x = 0 and x = 359 on its latitude are filled; x = 180 (lon 0) is not.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"Alert zones, cyclone cones and wind bands drawn into the globe image"`

---

### Task 16: Picking, hover tooltips and the details panel

**Files:**
- Create: `src/EarthWatch3D.App/Rendering/EventPicker.cs`
- Create: `src/EarthWatch3D.App/ViewModels/SelectionViewModel.cs`
- Create: `src/EarthWatch3D.App/Views/DetailsPanel.xaml(.cs)`
- Modify: `SceneController.cs` (click selects; hover shows the event tooltip, after the Sun/Moon marker check),
  `MainWindow.xaml`
- Test: `tests/EarthWatch3D.Tests/EventPickerTests.cs`, `SelectionViewModelTests.cs`

**Interfaces:**
- Produces:
  - `static class EventPicker { public static EarthEvent? Pick(Point mouse, Projector p, VisibleEvents v, GeoPosition? surfaceUnderMouse, double hitPx = 10); }`
    - First choice: the nearest point event (quake, cyclone centre, EONET fix) within hitPx on the visible side.
    - Otherwise, an alert whose area (or loaded zone) `Contains(surfaceUnderMouse)`. Pass a
      `Func<string, IReadOnlyList<GeoPolygon>?> zoneLookup` overload for zones.
  - `SelectionViewModel`:
    - `EarthEvent? Selected`, `string Title`, `IReadOnlyList<(string Label, string Value)> Lines`,
      `Uri? Link`, `bool IsOpen`, and a `Close` command.
    - `void Select(EarthEvent? e)`.
    - `void ClearIfNotVisible(VisibleEvents v)`.
    - Lines per type, in km for now (Plan 3 adds miles):
      - Quake: Magnitude ("M6.1"), Depth ("35 km"), Time (UTC), Place.
      - Cyclone: Category ("Tropical storm" or "Category 2 hurricane"), Wind ("40 kt (74 km/h)"),
        Pressure ("1002 hPa"), Advisory time, Source (GDACS/NHC).
      - Alert: Event, Severity, From, Until, Area.
      - EONET: Wind, Last fix time, Source "NASA EONET".
  - The details panel slides in at the right below the Sun & Moon card. Esc and × close it.
- [ ] **Step 1: Write the failing tests.**
  - `PicksNearestPointWithinRadius`: camera setup as in MarkerHoverTests; two quakes at 5 px and 8 px → the 5 px
    one.
  - `IgnoresFarSidePoints`.
  - `PicksAlertContainingSurfacePoint`.
  - `QuakeLines`, `CycloneCategoryText`, `AlertLines`.
  - `SelectionClearsWhenEventLeavesView`.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit.** `"Event picking, hover tooltips and details panel"`

---

### Task 17: Composition root, error handling, offline diagnostic, docs and verification

**Files:**
- Modify: `App.xaml.cs`, `SceneController.cs`, `LaunchOptions.cs`, `SnapshotDiagnostic.cs`
- Create: `src/EarthWatch3D.App/Diagnostics/CrashHandler.cs`
- Create: `docs/manual-test.md`
- Modify: `README.md`, `docs/perf.md`
- Test: `tests/EarthWatch3D.Tests/CrashHandlerTests.cs`, `LaunchOptionsTests.cs`

**Interfaces:**
- Produces:
  - **App.OnStartup builds:**
    - `AppPaths.Default` (EnsureCreated), `FileLog`, `SettingsStore`, `EventStore`, filled from
      `EventCache.Load()` when present.
    - `FeedHttp`, `ZoneCache`, and the five feeds, then `FeedScheduler.Start()`.
    - A cache writer that saves 10 s after the last store change (debounced).
    - Scheduler status goes to StatusViewModel.
    - On exit: stop the scheduler, save the cache once, dispose.
  - `CrashHandler(IAppLog log, Action<string> showMessage)`: `Install(Application app)`.
    - Hooks `DispatcherUnhandledException` (marks it handled) and `TaskScheduler.UnobservedTaskException`
      (`SetObserved`).
    - Logs every exception.
    - Shows a message only for the first one in a session: "Something went wrong; details are in the log
      file…".
    - Has a testable `Handle(Exception e)`.
  - **Compose failure fallback (spec §11):** when a compose throws, log it, display the plain base image (no
    shading), and retry on the next schedule. `_firstCompose` faults are observed, so no extra message appears.
  - **LaunchOptions:**
    - `--offline-samples <dir>`: the FeedHttp uses a SampleHttpHandler mapping every feed URL prefix to the files
      in `<dir>`, and the cache goes to a temp directory.
    - `--display-time <ISO UTC>`: start in replay at that time.
  - **Snapshot:**
    `--snapshot <png> --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z`. Expected:
    - Quake discs, with a label on any M5+.
    - Pena near (9.9°N, 167.7°W) with its track and cone.
    - US warning zones on the US coasts.
    - The status dots green.
- [ ] **Step 1: Write the failing tests.**
  - `CrashHandlerLogsAllShowsOnce`.
  - `ParsesOfflineSamplesAndDisplayTime`: an invalid date gives a parse error with a clear message.
- [ ] **Step 2: Run the tests to verify they fail.**
- [ ] **Step 3: Implement** the wiring, CrashHandler and fallback.
- [ ] **Step 4: Run all tests.** Run `dotnet test --filter Category!=Perf` (all pass), the Perf test in Release,
  and a Release build with 0 warnings.
- [ ] **Step 5: Offline snapshots.** Take two:
  - Default view: `p2-atlantic.png`.
  - Pacific: add `--face-moon`, or a new `--look-at <lat,lon>` option (add it if needed, documented), facing
    (10, −165) for Pena: `p2-pena.png`.

  Check each point in the expected list above. Measure memory and fps with events using the PERF_LOG build, and
  record them in docs/perf.md.
- [ ] **Step 6: Write docs/manual-test.md**, a checklist for the user. It covers:
  - Layer toggles, the Display flyout, and the time bar: replay, play speeds, Live.
  - Hover and click on a quake, a storm and an alert, and the details panel.
  - Status dots with the network unplugged, which should go amber then red.
  - Restarting offline, which shows cached events.

  Update the README's "what works" list, controls and diagnostics.
- [ ] **Step 7: Commit.** `"Wire feeds into the app, crash handling, offline diagnostic, docs"`

---

### Task 18: Tectonic plate boundaries layer

Added at the user's request on 2026-11-11 (spec §6, item 5a). It is optional and off by default.

**Files:**
- Modify: `tools/prepare-assets.ps1`, which adds the plate step. Record the source URL and licence in
  `docs/data-sources.md` under a new "Static assets: plate boundaries" item.
- Create: `src/EarthWatch3D.App/Assets/plate_boundaries.geojson`, produced by the script and committed. It is an
  EmbeddedResource like the coastlines.
- Modify: `src/EarthWatch3D.App/Assets/README.md` (source and licence), and `README.md` credits.
- Create: `src/EarthWatch3D.Core/Geo/PlateBoundaries.cs`
- Modify: `AppSettings` (`bool ShowPlates = false`), `LayersViewModel` (`ShowPlates`), `LayerStrip.xaml` (a
  button with a legend tooltip), `SceneController.cs` (lines layer)
- Test: `tests/EarthWatch3D.Tests/PlateBoundariesTests.cs`

**Interfaces:**
- Consumes: `DateLine.SplitLine` (Task 3), `LineRasterizer`/`SparseOverlay` (Plan 1), `LayersViewModel` and
  `AppSettings` (Tasks 6 and 13).
- Produces:
  - `enum PlateBoundaryKind { Convergent, Divergent, Transform }`
  - `sealed record PlateBoundary(PlateBoundaryKind Kind, GeoLine Line)`
  - `static class PlateBoundaries`:
    - `IReadOnlyList<PlateBoundary> Read(Stream geojson)`: reads each LineString's `properties.kind`
      (`convergent` / `divergent` / `transform`); unknown kinds are skipped.
    - `Color`-free style constants as ARGB ints: Convergent 0xD9FF4D4D, Divergent 0xD9FFD23F,
      Transform 0xD95AC8FA (85% alpha).
  - The lines layer (SceneController) is rasterized on the STA thread from coastlines and borders (when
    `ShowCoastlines`) **plus** plate boundaries (when `ShowPlates`, drawn after the coastlines, 1.5 px). It is
    re-rasterized and recomposed when either toggle changes. Both off gives no lines layer.

- [ ] **Step 1: Extend `tools/prepare-assets.ps1`.**
  - Download `https://raw.githubusercontent.com/fraxen/tectonicplates/master/GeoJSON/PB2002_steps.json`
    (10.4 MB) to the scratch folder.
  - Merge its 5,824 step LineStrings into polylines:
    - Walk the features in file order. Within each `PLATEBOUND`, append each step's coordinates to the current
      polyline while the step's kind equals the current kind and its first point is within 0.001° of the
      polyline's last point. Otherwise start a new polyline.
    - Map the kinds: `SUB`, `OCB`, `CCB` → convergent; `OSR`, `CRB` → divergent; `OTF`, `CTF` → transform.
    - Drop consecutive duplicate points.
    - Round coordinates to 3 decimals (about 100 m).
  - Write a FeatureCollection of LineStrings with `properties.kind` and `properties.plates` (`PLATEBOUND`).
    Longitudes stay as they are; the reader splits at the date line.
  - Print the feature count and the file size. The file should be under 1.5 MB.
- [ ] **Step 2: Run the script.** Commit the asset and add it to the EmbeddedResource glob (it is already
  covered by `Assets\**`).
- [ ] **Step 3: Write the failing tests.**

```csharp
public class PlateBoundariesTests
{
    private static IReadOnlyList<PlateBoundary> Load()
    {
        using var s = typeof(EarthWatch3D.App.App).Assembly.GetManifestResourceStream("EarthWatch3D.App.Assets.plate_boundaries.geojson")!;
        return PlateBoundaries.Read(s);
    }

    [Fact] public void HasAllThreeKinds()
    { var b = Load(); Assert.Contains(b, x => x.Kind == PlateBoundaryKind.Convergent); Assert.Contains(b, x => x.Kind == PlateBoundaryKind.Divergent); Assert.Contains(b, x => x.Kind == PlateBoundaryKind.Transform); }

    [Fact] public void LongitudesInRangeAfterSplit() =>
        Assert.All(Load().SelectMany(x => x.Line.Points), p => Assert.InRange(p.LongitudeDeg, -180.0, 180.0));

    [Theory] // PB2002 step lengths summed by kind (km): convergent 91,921; divergent 94,806; transform 74,068
    [InlineData(PlateBoundaryKind.Convergent, 91921)] [InlineData(PlateBoundaryKind.Divergent, 94806)] [InlineData(PlateBoundaryKind.Transform, 74068)]
    public void TotalLengthMatchesPb2002WithinTwoPercent(PlateBoundaryKind kind, double expectedKm)
    {
        double km = Load().Where(x => x.Kind == kind)
            .Sum(x => x.Line.Points.Zip(x.Line.Points.Skip(1), GeoMath.DistanceKm).Sum());
        Assert.InRange(km, expectedKm * 0.98, expectedKm * 1.02);
    }

    [Fact] public void UnknownKindSkipped()
    {
        var json = """{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"mystery"},"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}}]}""";
        Assert.Empty(PlateBoundaries.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))));
    }

    [Fact] public void ShowPlatesDefaultsOffAndIsSaved()
    { var store = new FakeSettingsStore(); var vm = new LayersViewModel(store); Assert.False(vm.ShowPlates); vm.ShowPlates = true; Assert.True(store.Saved!.ShowPlates); }
}
```

  `FakeSettingsStore` is the test double from Task 13's tests; move it to a shared `TestDoubles.cs` if it isn't
  there already.
- [ ] **Step 4: Run the tests to verify they fail.**
- [ ] **Step 5: Implement.**
  - The reader splits each line with `DateLine.SplitLine`.
  - The settings and view-model property.
  - The layer button: icon ⛰ (or a simple jagged-line glyph). Its tooltip legend reads "Plate boundaries (Bird
    2003)", with three coloured swatches: Converging (subduction), Spreading (ridges, rifts), Sliding past
    (transform faults).
  - The lines-layer rasterization change in SceneController.
- [ ] **Step 6: Run the tests to verify they pass.** Run all tests and a Release build with 0 warnings.
- [ ] **Step 7: Snapshot check.** Run
  `--snapshot <scratchpad>\plates.png --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z`,
  with plates switched on. Use a `--layers plates` option if one exists; otherwise temporarily set
  `ShowPlates: true` in a test settings file. Expected:
  - The Mid-Atlantic Ridge in yellow down the middle of the Atlantic.
  - Subduction in red along the west coast of South America.
  - The transform faults in blue offsetting the ridge.
- [ ] **Step 8: Update docs.** Add plate boundaries to the README "what works" list and credits. Credit line:
  "Plate boundaries: Peter Bird (2003), An updated digital model of plate boundaries, G³ 4(3):1027; GIS data by
  Hugo Ahlenius, Nordpil (ODC-By 1.0)."
- [ ] **Step 9: Commit.** `"Tectonic plate boundaries layer (PB2002), coloured by boundary type"`
