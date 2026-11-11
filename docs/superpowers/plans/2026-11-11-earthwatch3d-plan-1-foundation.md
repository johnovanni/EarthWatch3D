# EarthWatch3D Plan 1: Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify every external data source, build the AstroCore library (with sub-solar and sub-lunar points),
and ship a running EarthWatch3D window. The window shows a rotatable and zoomable Blue Marble globe with live
day/night shading, coastlines and borders, Sun and Moon overhead markers, Sun and Moon arrows, and the Sun & Moon
card.

**Architecture:** AstroCore is a separate local git repo (net10.0, no UI). It is included in EarthWatch3D as a
submodule at `external/AstroCore`. EarthWatch3D.Core (net10.0) holds UI-free logic: geo helpers, the GeoJSON line
reader and the globe image composer. EarthWatch3D.App (net10.0-windows, WPF) renders with HelixToolkit.Wpf and
uses MVVM with CommunityToolkit.Mvvm. The 3D scene is updated directly by a `SceneController`, not through
bindings.

**Tech Stack:** .NET 10 SDK (10.0.401 installed), WPF, HelixToolkit.Wpf 3.1.2, CommunityToolkit.Mvvm 8.4.0,
xUnit 2.9.2, PowerShell 7 for tools, Python 3 (with `netCDF4`) only for the spike's cross-check.

**Spec:** `docs/superpowers/specs/2026-11-11-earthwatch3d-design.md` (read it before starting; section numbers
below refer to it).

## Plan series

This is plan 1 of 4. Each plan ends with working, tested software.

| Plan | Delivers |
|---|---|
| **1. Foundation (this plan)** | Data-source spike, AstroCore, the globe with night shading, Sun and Moon markers, arrows and card |
| 2. Events and time | Event model, `EventStore`, USGS/GDACS/NHC/NWS/EONET feeds, `FeedScheduler`, layer strip, ⋯ Display flyout, details panel, time bar with replay, status strip, area overlay on the globe, logging (spec §4, §5.1–5.5, §6 events, §7, §11) |
| 3. Lightning, clouds, data use | GLM and MTG readers, `LiveLightningSource`, overlap rules, view-based polling, `DownloadMeter` and limit, Settings dialog with DPAPI key storage and Test, GIBS clouds, km/miles (spec §5.6–5.8, §9) |
| 4. Ship | Welcome and About, MSIX packaging, Azure Trusted Signing, `.appinstaller`, `build-release.ps1`, README, manual checklist, perf doc (spec §8, §10 manual/perf) |

Plans 2–4 are written after the previous plan is finished, using `docs/data-sources.md` from Task 1.

## Global Constraints

- Target frameworks: `net10.0` (AstroCore, EarthWatch3D.Core) and `net10.0-windows` (App, Tests). Nullable and
  ImplicitUsings are enabled everywhere.
- No secrets, keys, account names or personal data are committed. Requests to outside services carry no user
  data. The HTTP User-Agent comes from one constant `AppInfo.UserAgent` = `EarthWatch3D/<version> (+<project page>)`.
- The Earth-fixed frame is a unit sphere = 6371 km, with +X at (0°N, 0°E), +Y at (0°N, 90°E) and +Z at the North
  Pole. The camera's up direction is +Z.
- Longitudes are always normalised to (−180°, 180°]. All `DateTime` values passed into astronomy are UTC.
  `DateTimeKind.Local` is converted to UTC, and `Unspecified` is treated as UTC.
- Globe image: equirectangular 8192×4096 BGRA. Pixel (x, y) has its centre at lon = −180 + (x+0.5)·360/W and
  lat = 90 − (y+0.5)·180/H.
- Night shade: grey RGB (40, 40, 48), opacity `maxOpacity × smoothstep(0°, −12°, h)`, maxOpacity 0.55 by default
  (range 0–0.9).
- Camera distance from the Earth's centre: 1.047 to 20 Earth radii. The mouse wheel zooms 15% per notch.
- Commits: one per task (or per step where marked), message ending with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. **Never push.** Pushing and GitHub repo creation
  wait for the user.
- Long-path guard: copy SolarSystem3D's `Directory.Build.props` pattern (redirect bin/obj to `%TEMP%\<Repo>.build`
  when the project path is longer than 110 characters).

## Review Focus

1. **Sub-points near the date line**: when the Sun or Moon is over ±180°, longitude must normalise to
   (−180, 180] and shading must be continuous across the image seam. Pinned in Task 5 (normalisation) and
   Task 8 (seam test).
2. **Time zones**: passing a local-time `DateTime` must give the same sub-solar point as the UTC equivalent, not
   one shifted by hours. Pinned in Task 5.
3. **Looking straight down on a pole**: Face Sun at a solstice, or double-clicking near a pole, must not produce
   a NaN or degenerate camera. Pinned in Task 9.
4. **A tiny or minimised window**: a 0×0 viewport must not throw or produce NaN arrow positions; the arrows just
   hide. Pinned in Task 11.
5. **Sleep and resume or a clock jump**: after the PC wakes, the Sun and Moon and the shading must show the
   current time, not catch up step by step. Pinned in Task 10 (view model) and Task 12 (scene).

---

### Task 1: Data-source spike

Check every external source in spec §12 against the live service and record the facts later plans depend on.
This task writes notes and sample files, not product code.

**Files:**
- Create: `C:\repos\EarthWatch3D\docs\data-sources.md`
- Create: `C:\repos\EarthWatch3D\tools\dump_flashes.py`
- Create: `C:\repos\EarthWatch3D\tools\capture-samples.ps1`
- Create: `C:\repos\EarthWatch3D\tests\Samples\` (captured responses, see below)

**Interfaces:**
- Produces: `docs/data-sources.md` with one section per source. Each section gives the exact URL(s), query
  parameters, response fields used, update cadence, typical size, latency, and any surprises. Plans 2 and 3 take
  their endpoints and field names from this file.

- [ ] **Step 1: USGS.** Fetch `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson` and
  `all_week.geojson`. Record the sizes, and confirm the fields `id`, `properties.mag`, `place`, `time`,
  `updated`, `url` and `geometry.coordinates[lon,lat,depth]`. Save `tests/Samples/usgs_all_week.geojson`.
- [ ] **Step 2: GDACS.** Call
  `https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH?eventlist=TC&alertlevel=green;orange;red&fromdate=<today-7d>&todate=<today>`.
  Find the endpoint that returns one event's track, cone and wind polygons; check the Swagger page
  `https://www.gdacs.org/gdacsapi/swagger/index.html`. Record how the geometry roles are labelled. Save one event
  list and one geometry file to `tests/Samples/gdacs_*`.
- [ ] **Step 3: NHC.** Fetch `https://www.nhc.noaa.gov/CurrentStorms.json`. Record its structure and the links
  to the forecast track and cone KMZ files. If no storm is active, record the structure from the NHC
  documentation and save an archived KMZ from `https://www.nhc.noaa.gov/gis/` for a 2026 storm. Save it to
  `tests/Samples/nhc_*`.
- [ ] **Step 4: NWS.** Fetch `https://api.weather.gov/alerts/active` (send the User-Agent constant). Check
  whether `https://api.weather.gov/alerts?start=<now-24h>` returns history, and how paging works
  (`pagination.next`). Count how many alerts have `geometry: null`. Fetch one `affectedZones` URL to confirm it
  has a polygon. Save `tests/Samples/nws_active.json` and one zone file.
- [ ] **Step 5: EONET.** Fetch
  `https://eonet.gsfc.nasa.gov/api/v3/events?category=severeStorms&status=all&days=7`. Record the geometry list
  format and the `closed` field. Save `tests/Samples/eonet_storms.json`.
- [ ] **Step 6: GOES GLM.** List
  `https://noaa-goes19.s3.amazonaws.com/?list-type=2&prefix=GLM-L2-LCFA/<yyyy>/<ddd>/<hh>/`, and the same for
  `noaa-goes18`. Download 3 consecutive files from each satellite and record their average size. Write
  `tools/dump_flashes.py` (args: path to `.nc`, output CSV; prints the flash count and writes
  `time_utc,lat,lon,energy` for all flashes using `netCDF4` with automatic scale/offset). Run it on one GOES-19
  file. Save that file as `tests/Samples/glm_g19_sample.nc` and its CSV as `tests/Samples/glm_g19_sample.csv`.
  Then compute **data use per hour per satellite** from the measured size × 180 files/hour, and record it.
- [ ] **Step 7: PureHDF check.** In a throwaway console project under the scratchpad (not the repo), reference
  `PureHDF` (latest 2.x). Open the saved GLM file, read `flash_lat`, `flash_lon` and `flash_energy`, apply their
  `scale_factor` and `add_offset` attributes, and compare the first 10 values with the CSV. Record the package
  version, any filter package needed for deflate (for example `PureHDF.Filters.Deflate.*`), and the result.
  If PureHDF cannot read the file, stop and report to the user (this changes spec §5.7).
- [ ] **Step 8: EUMETSAT MTG LI.** This needs a EUMETSAT consumer key and secret. **Ask the user** to register
  and enter the credentials into a git-ignored local file `tools/eumetsat.local.json` (never paste them in chat).
  Using that file:
  - Get a token from `https://api.eumetsat.int/token` (client credentials).
  - Search collection `EO:EUM:DAT:0691` for the latest products.
  - Download one product and record the file layout, variable names, size, cadence, and **latency** (product
    sensing end time versus when it appeared in the search).
  - Run `dump_flashes.py` with a `--mtg` variable map and save the sample and CSV as `tests/Samples/mtg_lfl_sample.*`.

  If the user chooses not to register now, record "deferred" and continue. Plan 3 then starts with this step.
- [ ] **Step 9: GIBS clouds.** Fetch
  `https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&REQUEST=GetCapabilities&VERSION=1.3.0`
  and list the layers whose names contain `Infrared`, `Cloud` or `Band13`, with their time dimension and
  cadence. Pick the best global cloud layer as described in spec §5.6 (preferred: a geostationary IR composite
  every few hours or more often; fallback: daily VIIRS true colour). Fetch one 4096×2048 GetMap image of it and
  record the URL template.
- [ ] **Step 10: Static assets.** Confirm the download URLs:
  - NASA Blue Marble Next Generation 21600×10800 JPEG (`world.topo.bathy.200407.3x21600x10800.jpg` on
    `eoimages.gsfc.nasa.gov`).
  - Natural Earth `ne_50m_coastline.geojson` and `ne_50m_admin_0_boundary_lines_land.geojson` (raw files in the
    `nvkelso/natural-earth-vector` GitHub repo, `geojson/` folder).

  Record the URLs and licences.
- [ ] **Step 11: Write `tools/capture-samples.ps1`.** It re-downloads steps 1–6 and 9 into `tests/Samples`.
  It takes no credentials; MTG stays a manual step. Run it once to check it works.
- [ ] **Step 12: Compare with the spec.** For each item that differs from spec §5 (an endpoint, a field, a data
  volume, the MTG latency, the GIBS layer), add a "Spec impact" line in `docs/data-sources.md` and tell the user
  before continuing. Update the spec only after the user agrees.
- [ ] **Step 13: Commit.** Add `tools/eumetsat.local.json` to `.gitignore` first, then:

```bash
git add .gitignore docs/data-sources.md tools/dump_flashes.py tools/capture-samples.ps1 tests/Samples
git commit -m "Record verified data sources and capture sample responses"
```

---

### Task 2: Create the AstroCore repo from SolarSystem3D's astronomy code

**Files:**
- Create repo `C:\repos\AstroCore` with: `AstroCore.sln`, `Directory.Build.props`, `.gitignore`, `README.md`,
  `src/AstroCore/AstroCore.csproj`, `tests/AstroCore.Tests/AstroCore.Tests.csproj`
- Copy from `C:\repos\SolarSystem3D\src\SolarSystem3D.Core\`:
  - `Astronomy/{Vector3d,Matrix3d,AstroTime,Frames,Conic}.cs` → `src/AstroCore/Astronomy/`
  - `Orbits/{IOrbitModel,PlanetOrbit,LunarOrbit,EarthOrbit}.cs` → `src/AstroCore/Orbits/`
- Create: `src/AstroCore/Orbits/EarthMoonSystem.cs`
- Copy and adapt: `tests/SolarSystem3D.Tests/MathTests.cs` → `tests/AstroCore.Tests/MathTests.cs`
- Create: `tests/AstroCore.Tests/EarthMoonAccuracyTests.cs`

**Interfaces:**
- Produces:
  - Namespaces `AstroCore.Astronomy` and `AstroCore.Orbits`. Types are unchanged from SolarSystem3D except the
    namespace.
  - `Matrix3d.RotationY(double angle)` (new, an active rotation like `RotationX` and `RotationZ`).
  - `public static class EarthMoonSystem` with:
    - `public static PlanetOrbit EarthMoonBarycentre { get; }`, built with exactly the `emb` elements from
      `MajorBodyCatalog.cs` line 40:
      `(1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531, -0.01294668, 100.46457166, 35999.37244981, 102.93768193, 0.32327364, 0.0, 0.0)`
    - `public static LunarOrbit Moon { get; }`
    - `public static EarthOrbit Earth { get; }` (= `new EarthOrbit(EarthMoonBarycentre, Moon)`)

- [ ] **Step 1: Create the repo skeleton.**

```bash
mkdir -p /c/repos/AstroCore && cd /c/repos/AstroCore && git init -b main
git config user.name johnovanni && git config user.email "$(git -C /c/repos/SolarSystem3D config user.email)"
dotnet new sln -n AstroCore --format sln
dotnet new classlib -o src/AstroCore -f net10.0
dotnet new xunit -o tests/AstroCore.Tests -f net10.0
dotnet sln add src/AstroCore tests/AstroCore.Tests
dotnet add tests/AstroCore.Tests reference src/AstroCore
```

  Delete the template `Class1.cs` and `UnitTest1.cs`. Copy `.gitignore` and `Directory.Build.props` from
  SolarSystem3D, changing `SolarSystem3D.build` to `AstroCore.build`. Pin the test packages to xunit 2.9.2,
  xunit.runner.visualstudio 2.8.2 and Microsoft.NET.Test.Sdk 17.11.1.
- [ ] **Step 2: Copy the source files** listed above. Change `namespace SolarSystem3D.Core.Astronomy` to
  `AstroCore.Astronomy` and `SolarSystem3D.Core.Orbits` to `AstroCore.Orbits`, and fix the `using` lines.
- [ ] **Step 3: Write the failing tests.** Copy `MathTests.cs` with namespaces fixed, keeping only the tests
  that use the copied types. Then add:

```csharp
// tests/AstroCore.Tests/EarthMoonAccuracyTests.cs
// Horizons state vectors (J2000 ecliptic, AU) for 2026-11-10 00:00 TDB, JD 2461354.5 — same values SolarSystem3D tests use.
public class EarthMoonAccuracyTests
{
    private const double Jd = 2461354.5;
    private static double AngleDeg(Vector3d a, Vector3d b) =>
        Math.Acos(Math.Clamp(Vector3d.Dot(a.Normalized(), b.Normalized()), -1, 1)) / Frames.Deg;

    [Fact]
    public void EarthMatchesHorizons()
    {
        var reference = new Vector3d(6.726227034059016E-01, 7.270693863771039E-01, -4.613446748933096E-05);
        var p = EarthMoonSystem.Earth.PositionAt(Jd);
        Assert.True(AngleDeg(p, reference) <= 0.1);
        Assert.True(Math.Abs(p.Length - reference.Length) / reference.Length <= 0.002);
    }

    [Fact]
    public void MoonMatchesHorizons()
    {
        var reference = new Vector3d(-1.518324045628562E-03, -2.186718077083572E-03, -2.324870968031709E-04);
        var p = EarthMoonSystem.Moon.PositionAt(Jd);
        Assert.True(AngleDeg(p, reference) <= 0.05);
        Assert.True(Math.Abs(p.Length - reference.Length) / reference.Length <= 0.002);
    }

    [Fact]
    public void RotationYIsActiveRightHanded()
    {
        var v = Matrix3d.RotationY(Math.PI / 2) * new Vector3d(0, 0, 1);
        Assert.Equal(1, v.X, 12); Assert.Equal(0, v.Y, 12); Assert.Equal(0, v.Z, 12);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail.** Run `dotnet test`. Expected: a compile error because
  `EarthMoonSystem` and `Matrix3d.RotationY` don't exist.
- [ ] **Step 5: Implement `Matrix3d.RotationY` and `EarthMoonSystem`.** `RotationY(a)` = `[[c,0,s],[0,1,0],[−s,0,c]]`.
- [ ] **Step 6: Run the tests to verify they pass.** Run `dotnet test`. Expected: all pass.
- [ ] **Step 7: README.** Say what AstroCore is, which types it contains, that it came from SolarSystem3D
  (2026-11), and its accuracy figures (Earth 0.002°, Moon 0.002°, copied from SolarSystem3D's README table).
- [ ] **Step 8: Commit.**

```bash
git add -A && git commit -m "AstroCore: astronomy core extracted from SolarSystem3D"
```

---

### Task 3: AstroCore precession (J2000 to the equator of date)

**Files:**
- Create: `C:\repos\AstroCore\src\AstroCore\Astronomy\Precession.cs`
- Test: `C:\repos\AstroCore\tests\AstroCore.Tests\PrecessionTests.cs`

**Interfaces:**
- Produces (in `AstroCore.Astronomy`):
  - `public static class Precession`
  - `public static Matrix3d FromJ2000(double jdTT)`: maps J2000 mean equatorial vectors to the mean equator and
    equinox of date.
  - `public static (double RaDeg, double DecDeg) ToEquatorOfDate(double raJ2000Deg, double decJ2000Deg, double jdTT)`:
    RA in [0, 360).

- [ ] **Step 1: Write the failing test** (Meeus, *Astronomical Algorithms*, example 21.b: θ Persei).

```csharp
public class PrecessionTests
{
    [Fact]
    public void MeeusExample21b()
    {
        var (ra, dec) = Precession.ToEquatorOfDate(41.054063, 49.227750, 2462088.69);
        Assert.Equal(41.547214, ra, 4);
        Assert.Equal(49.348483, dec, 4);
    }

    [Fact]
    public void IdentityAtJ2000()
    {
        var v = Precession.FromJ2000(AstroTime.J2000) * new Vector3d(0.3, -0.5, 0.8);
        Assert.Equal(0.3, v.X, 12); Assert.Equal(-0.5, v.Y, 12); Assert.Equal(0.8, v.Z, 12);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails.** Run `dotnet test --filter PrecessionTests`. Expected: a compile
  error because `Precession` doesn't exist.
- [ ] **Step 3: Implement `Precession`** using Meeus eq. 21.2 with the starting epoch J2000
  (T = centuries of TT since J2000, angles in arcseconds):

```
ζ = 2306.2181 T + 0.30188 T² + 0.017998 T³
z = 2306.2181 T + 1.09468 T² + 0.018203 T³
θ = 2004.3109 T − 0.42665 T² − 0.041833 T³
FromJ2000 = RotationZ(z) · RotationY(−θ) · RotationZ(ζ)     // active-rotation form of R3(−z)·R2(θ)·R3(−ζ)
```

  `ToEquatorOfDate` converts (RA, Dec) to a unit vector, applies `FromJ2000`, and converts back.
- [ ] **Step 4: Run the tests to verify they pass.** Run `dotnet test --filter PrecessionTests`. Expected: PASS.
- [ ] **Step 5: Commit.** `git commit -am "AstroCore: precession from J2000 to equator of date"` (after `git add`
  for the new files).

---

### Task 4: AstroCore Earth rotation (Greenwich mean sidereal time)

**Files:**
- Create: `C:\repos\AstroCore\src\AstroCore\Earth\EarthRotation.cs`
- Test: `C:\repos\AstroCore\tests\AstroCore.Tests\EarthRotationTests.cs`

**Interfaces:**
- Produces (in `AstroCore.Earth`):
  - `public static class EarthRotation`
  - `public static double GreenwichMeanSiderealTimeDeg(DateTime utc)`: in [0, 360). Treats UT1 as UTC, and
    `Local` and `Unspecified` kinds as described in Global Constraints.

- [ ] **Step 1: Write the failing tests** (Meeus examples 12.a and 12.b).

```csharp
public class EarthRotationTests
{
    [Fact]
    public void MeeusExample12a() =>
        Assert.Equal(197.693195, EarthRotation.GreenwichMeanSiderealTimeDeg(new DateTime(1987, 4, 10, 0, 0, 0, DateTimeKind.Utc)), 5);

    [Fact]
    public void MeeusExample12b() =>
        Assert.Equal(128.7378734, EarthRotation.GreenwichMeanSiderealTimeDeg(new DateTime(1987, 4, 10, 19, 21, 0, DateTimeKind.Utc)), 5);
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Run `dotnet test --filter EarthRotationTests`. Expected: a
  compile error.
- [ ] **Step 3: Implement** Meeus eq. 12.4, with JD from `AstroTime.ToJulianDayUtc` and T = (JD − 2451545.0)/36525:
  `θ0 = 280.46061837 + 360.98564736629·(JD − 2451545.0) + 0.000387933·T² − T³/38710000`, normalised to [0, 360).
- [ ] **Step 4: Run the tests to verify they pass.** Expected: PASS.
- [ ] **Step 5: Commit.** `git add -A && git commit -m "AstroCore: Greenwich mean sidereal time"`

---

### Task 5: AstroCore sub-solar and sub-lunar points, Moon phase, terminator

**Files:**
- Create: `C:\repos\AstroCore\src\AstroCore\Earth\GeoPosition.cs`
- Create: `C:\repos\AstroCore\src\AstroCore\Earth\SubPoints.cs`
- Create: `C:\repos\AstroCore\src\AstroCore\Earth\Terminator.cs`
- Test: `C:\repos\AstroCore\tests\AstroCore.Tests\SubPointsTests.cs`
- Test: `C:\repos\AstroCore\tests\AstroCore.Tests\TerminatorTests.cs`

**Interfaces:**
- Consumes: `EarthMoonSystem.Earth` and `.Moon` (Task 2), `Precession.FromJ2000` (Task 3),
  `EarthRotation.GreenwichMeanSiderealTimeDeg` (Task 4), `Frames.ObliquityJ2000`.
- Produces (in `AstroCore.Earth`):
  - `public readonly record struct GeoPosition(double LatitudeDeg, double LongitudeDeg)` with:
    - `public Vector3d ToUnitVector()`: Earth-fixed frame from Global Constraints.
    - `public static GeoPosition FromVector(Vector3d v)`.
    - `public static double NormalizeLongitude(double lonDeg)`: returns (−180, 180].
  - `public readonly record struct SkyPosition(double RightAscensionDeg, double DeclinationDeg, double DistanceKm)`:
    geocentric, mean equator of date.
  - `public static class SubPoints` with:
    - `SkyPosition Sun(DateTime utc)`
    - `SkyPosition Moon(DateTime utc)`
    - `GeoPosition SubSolarPoint(DateTime utc)`
    - `GeoPosition SubLunarPoint(DateTime utc)`
    - `double MoonIlluminatedFraction(DateTime utc)` (0–1)
    - `bool MoonIsWaxing(DateTime utc)`
  - `public static class Terminator` with:
    - `double SunAltitudeDeg(GeoPosition subSolar, double latDeg, double lonDeg)`

- [ ] **Step 1: Write the failing tests.** The reference values come from JPL Horizons (geocentric `500@399`,
  apparent RA/Dec of date, UT times), fetched 2026-11-11.

```csharp
public class SubPointsTests
{
    private const double AuKm = 149_597_870.7;
    private static DateTime U(int y, int mo, int d, int h) => new(y, mo, d, h, 0, 0, DateTimeKind.Utc);

    private static double SeparationDeg(double ra1, double dec1, double ra2, double dec2)
    {
        var a = new GeoPosition(dec1, ra1).ToUnitVector(); var b = new GeoPosition(dec2, ra2).ToUnitVector();
        return Math.Acos(Math.Clamp(Vector3d.Dot(a, b), -1, 1)) * 180 / Math.PI;
    }

    public static TheoryData<DateTime, double, double, double> SunRef => new()
    {
        { U(2026, 3, 20, 12), 359.894843697, -0.045488014, 0.99588568349617 },
        { U(2026, 6, 21, 12),  90.155654652, 23.437851309, 1.01620274452142 },
        { U(2026, 11, 10, 0), 225.136682593, -17.081149295, 0.99048012970480 },
        { U(2026, 12, 21, 12), 269.591437110, -23.436888513, 0.98375767705765 },
        { U(2030, 8, 15, 6), 144.834759588, 14.017147012, 1.01284581485577 },
    };

    public static TheoryData<DateTime, double, double, double, double, bool> MoonRef => new()
    {   // utc, RA, Dec, distance AU, illuminated %, waxing (Horizons "/T" = waxing, "/L" = waning)
        { U(2026, 3, 20, 12),  16.105368480, 10.503940182, 0.00246670500831,  2.81470, true },
        { U(2026, 6, 21, 12), 174.810347252,  0.049117817, 0.00258093733582, 45.83584, true },
        { U(2026, 11, 10, 0), 231.981249329, -24.008225458, 0.00267233043600,  0.68044, true },
        { U(2026, 12, 21, 12), 49.786880385, 23.602048833, 0.00243584025601, 90.29804, true },
        { U(2030, 8, 15, 6), 344.757169571, -1.037713239, 0.00254912192185, 95.84164, false },
    };

    [Theory, MemberData(nameof(SunRef))]
    public void SunMatchesHorizons(DateTime utc, double ra, double dec, double au)
    {
        var s = SubPoints.Sun(utc);
        Assert.True(SeparationDeg(s.RightAscensionDeg, s.DeclinationDeg, ra, dec) <= 0.05);
        Assert.True(Math.Abs(s.DistanceKm - au * AuKm) / (au * AuKm) <= 0.002);
    }

    [Theory, MemberData(nameof(MoonRef))]
    public void MoonMatchesHorizons(DateTime utc, double ra, double dec, double au, double illumPct, bool waxing)
    {
        var m = SubPoints.Moon(utc);
        Assert.True(SeparationDeg(m.RightAscensionDeg, m.DeclinationDeg, ra, dec) <= 0.05);
        Assert.True(Math.Abs(m.DistanceKm - au * AuKm) / (au * AuKm) <= 0.002);
        Assert.InRange(SubPoints.MoonIlluminatedFraction(utc) * 100 - illumPct, -0.5, 0.5);
        Assert.Equal(waxing, SubPoints.MoonIsWaxing(utc));
    }

    [Fact]
    public void SubSolarPointIsDeclinationAndRaMinusGmst()
    {
        var t = U(2026, 6, 21, 12);
        var s = SubPoints.Sun(t); var p = SubPoints.SubSolarPoint(t);
        Assert.Equal(s.DeclinationDeg, p.LatitudeDeg, 9);
        Assert.Equal(GeoPosition.NormalizeLongitude(s.RightAscensionDeg - EarthRotation.GreenwichMeanSiderealTimeDeg(t)), p.LongitudeDeg, 9);
    }

    [Fact] // Review Focus 2
    public void LocalTimeGivesSameAnswerAsUtc()
    {
        var utc = U(2026, 6, 21, 12);
        Assert.Equal(SubPoints.SubSolarPoint(utc), SubPoints.SubSolarPoint(utc.ToLocalTime()));
        Assert.Equal(SubPoints.SubSolarPoint(utc), SubPoints.SubSolarPoint(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
    }

    [Theory] // Review Focus 1
    [InlineData(180.0, 180.0)] [InlineData(-180.0, 180.0)] [InlineData(540.0, 180.0)]
    [InlineData(-179.999, -179.999)] [InlineData(359.0, -1.0)]
    public void LongitudeNormalisesToHalfOpenRange(double input, double expected) =>
        Assert.Equal(expected, GeoPosition.NormalizeLongitude(input), 9);

    [Fact]
    public void UnitVectorAxes()
    {
        var x = new GeoPosition(0, 0).ToUnitVector(); var y = new GeoPosition(0, 90).ToUnitVector(); var z = new GeoPosition(90, 0).ToUnitVector();
        Assert.Equal(1, x.X, 12); Assert.Equal(1, y.Y, 12); Assert.Equal(1, z.Z, 12);
        var back = GeoPosition.FromVector(new GeoPosition(-33.9, 151.2).ToUnitVector());
        Assert.Equal(-33.9, back.LatitudeDeg, 9); Assert.Equal(151.2, back.LongitudeDeg, 9);
    }
}

public class TerminatorTests
{
    [Fact]
    public void OverheadAtSubSolarPoint() => Assert.Equal(90, Terminator.SunAltitudeDeg(new(10, 20), 10, 20), 9);

    [Fact]
    public void ZeroOnGreatCircle90DegreesAway()
    {   // sub-solar at (10°N, 20°E); walk the great circle perpendicular to it in 15° steps
        var sub = new GeoPosition(10, 20);
        var s = sub.ToUnitVector();
        var a = Vector3d.Cross(s, new Vector3d(0, 0, 1)).Normalized();
        var b = Vector3d.Cross(s, a);
        for (int k = 0; k < 24; k++)
        {
            double t = k * 15 * Math.PI / 180;
            var p = GeoPosition.FromVector(a * Math.Cos(t) + b * Math.Sin(t));
            Assert.InRange(Terminator.SunAltitudeDeg(sub, p.LatitudeDeg, p.LongitudeDeg), -0.01, 0.01);
        }
    }

    [Fact]
    public void Minus6AtLongitude96OnEquatorWithSunAtOrigin() =>
        Assert.Equal(-6.0, Terminator.SunAltitudeDeg(new(0, 0), 0, 96), 2);
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Run `dotnet test`. Expected: compile errors.
- [ ] **Step 3: Implement.**
  - `Sun(utc)`:
    1. `jdTT = AstroTime.ToJulianDayTT(utc)` (after converting Local to UTC; Unspecified is treated as UTC).
    2. `g = −EarthMoonSystem.Earth.PositionAt(jdTT)` (J2000 ecliptic, AU).
    3. `eq = Matrix3d.RotationX(Frames.ObliquityJ2000) * g`.
    4. `d = Precession.FromJ2000(jdTT) * eq`.
    5. RA = atan2(y, x) in [0, 360). Dec = asin(z/|d|). Distance = |d| × 149 597 870.7 km.
  - `Moon(utc)`: the same steps, starting from `EarthMoonSystem.Moon.PositionAt(jdTT)`.
  - `SubSolarPoint` and `SubLunarPoint`: latitude = Dec, longitude = `NormalizeLongitude(RA − GMST(utc))`.
  - `MoonIlluminatedFraction`: phase angle *i* = the angle between (sun − moon) and (−moon) in J2000 ecliptic
    geocentric vectors. Return (1 + cos *i*)/2.
  - `MoonIsWaxing`: the Moon's ecliptic longitude minus the Sun's, mod 360, lies in (0, 180).
  - `Terminator.SunAltitudeDeg`: `asin(sin φ sin δ + cos φ cos δ cos(λ − λ☉))`.
- [ ] **Step 4: Run the tests to verify they pass.** Run `dotnet test`. Expected: all pass. Also print the actual
  separations with `ITestOutputHelper` and record the worst Sun and Moon errors in the AstroCore README accuracy
  table.
- [ ] **Step 5: Commit.** `git add -A && git commit -m "AstroCore: sub-solar/sub-lunar points, Moon phase, terminator"`

---

### Task 6: EarthWatch3D solution scaffold with the AstroCore submodule

**Files:**
- Create in `C:\repos\EarthWatch3D`: `EarthWatch3D.sln`, `.gitignore`, `Directory.Build.props`, `.gitmodules`
  (made by git)
- Create: `src/EarthWatch3D.Core/EarthWatch3D.Core.csproj`, `src/EarthWatch3D.Core/AppInfo.cs`
- Create: `src/EarthWatch3D.App/EarthWatch3D.App.csproj`, `App.xaml(.cs)`, `MainWindow.xaml(.cs)`
- Create: `tests/EarthWatch3D.Tests/EarthWatch3D.Tests.csproj`, `tests/EarthWatch3D.Tests/AppInfoTests.cs`

**Interfaces:**
- Produces:
  - `EarthWatch3D.Core.AppInfo` with `public const string ProjectPage = "https://github.com/johnovanni/EarthWatch3D";`
    and `public static string UserAgent { get; }` = `$"EarthWatch3D/{version} (+{ProjectPage})"`, where version
    is the Core assembly's informational version.
  - The project references described below.

- [ ] **Step 1: Add the submodule and projects.**

```bash
cd /c/repos/EarthWatch3D
git -c protocol.file.allow=always submodule add ../AstroCore external/AstroCore
dotnet new sln -n EarthWatch3D --format sln
dotnet new classlib -o src/EarthWatch3D.Core -f net10.0
dotnet new wpf -o src/EarthWatch3D.App -f net10.0
dotnet new xunit -o tests/EarthWatch3D.Tests -f net10.0
dotnet sln add src/EarthWatch3D.Core src/EarthWatch3D.App tests/EarthWatch3D.Tests external/AstroCore/src/AstroCore
dotnet add src/EarthWatch3D.Core reference external/AstroCore/src/AstroCore
dotnet add src/EarthWatch3D.App reference src/EarthWatch3D.Core
dotnet add tests/EarthWatch3D.Tests reference src/EarthWatch3D.App src/EarthWatch3D.Core
dotnet add src/EarthWatch3D.App package HelixToolkit.Wpf --version 3.1.2
dotnet add src/EarthWatch3D.App package CommunityToolkit.Mvvm --version 8.4.0
```

  Change the test project to `net10.0-windows` with `<UseWPF>true</UseWPF>`, and pin its packages as in Task 2.
  Copy `.gitignore` and `Directory.Build.props` from AstroCore, using `EarthWatch3D.build`. Delete the template
  `Class1.cs` and `UnitTest1.cs`. Set `<RootNamespace>` to match each project name.
- [ ] **Step 2: Write the failing test.**

```csharp
public class AppInfoTests
{
    [Fact]
    public void UserAgentNamesAppAndProjectOnly()
    {
        Assert.StartsWith("EarthWatch3D/", AppInfo.UserAgent);
        Assert.EndsWith("(+https://github.com/johnovanni/EarthWatch3D)", AppInfo.UserAgent);
        Assert.DoesNotContain("@", AppInfo.UserAgent);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails.** Run `dotnet test`. Expected: a compile error because `AppInfo`
  doesn't exist.
- [ ] **Step 4: Implement `AppInfo`.** Set `<Version>0.1.0</Version>` in the Core csproj.
- [ ] **Step 5: Verify.** Run `dotnet build -c Release` (expected: 0 errors, 0 warnings) and `dotnet test`
  (expected: PASS). Launch `dotnet run --project src/EarthWatch3D.App`; expected: an empty window titled
  "EarthWatch3D". Close it.
- [ ] **Step 6: Commit.** `git add -A && git commit -m "Scaffold EarthWatch3D solution with AstroCore submodule"`

---

### Task 7: Static assets and the GeoJSON line reader

**Files:**
- Create: `tools/prepare-assets.ps1`
- Create: `src/EarthWatch3D.App/Assets/bluemarble-8192.jpg`, `Assets/ne_50m_coastline.geojson`,
  `Assets/ne_50m_admin_0_boundary_lines_land.geojson` (produced by the script, committed)
- Create: `src/EarthWatch3D.Core/Geo/GeoJsonLines.cs`
- Test: `tests/EarthWatch3D.Tests/GeoJsonLinesTests.cs`

**Interfaces:**
- Consumes: `GeoPosition` (AstroCore.Earth).
- Produces:
  - `EarthWatch3D.Core.Geo.GeoJsonLines.Read(Stream geojson) → IReadOnlyList<IReadOnlyList<GeoPosition>>`. Each
    LineString, and each part of a MultiLineString, becomes one polyline. Other geometry types are skipped.
  - Embedded resources named `EarthWatch3D.App.Assets.<file>` (set `<EmbeddedResource Include="Assets\**" />`).

- [ ] **Step 1: Write `tools/prepare-assets.ps1`.**
  - Download the Blue Marble 21600×10800 JPEG and the two Natural Earth GeoJSON files to a scratch folder,
    using the URLs recorded in `docs/data-sources.md`.
  - Resize the Blue Marble to 8192×4096 with `System.Drawing` (HighQualityBicubic) and save it as a JPEG,
    quality 90, to `src/EarthWatch3D.App/Assets/bluemarble-8192.jpg`.
  - Copy the GeoJSON files into `Assets/`.
  - Print the final file sizes.
- [ ] **Step 2: Run the script.** Expected: three files in `Assets/`, with the JPEG under 15 MB.
- [ ] **Step 3: Write the failing test.**

```csharp
public class GeoJsonLinesTests
{
    private static Stream S(string json) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

    [Fact]
    public void ReadsLineStringAndMultiLineStringSkipsOthers()
    {
        var lines = GeoJsonLines.Read(S("""
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{},"geometry":{"type":"LineString","coordinates":[[10,20],[11,21]]}},
          {"type":"Feature","properties":{},"geometry":{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3],[4,4]]]}},
          {"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":[5,5]}},
          {"type":"Feature","properties":{},"geometry":null}
        ]}
        """));
        Assert.Equal(3, lines.Count);
        Assert.Equal(new GeoPosition(20, 10), lines[0][0]);   // GeoJSON order is [lon, lat]
        Assert.Equal(3, lines[2].Count);
    }

    [Fact]
    public void EmbeddedCoastlineLoads()
    {
        using var s = typeof(EarthWatch3D.App.App).Assembly.GetManifestResourceStream("EarthWatch3D.App.Assets.ne_50m_coastline.geojson")!;
        Assert.True(GeoJsonLines.Read(s).Count > 1000);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail.** Expected: compile errors.
- [ ] **Step 5: Implement `GeoJsonLines.Read`** with `System.Text.Json` (`JsonDocument`). Add the
  `EmbeddedResource` item to the App csproj.
- [ ] **Step 6: Run the tests to verify they pass.** Expected: PASS.
- [ ] **Step 7: Commit.** `git add -A && git commit -m "Add Blue Marble and Natural Earth assets with GeoJSON line reader"`

---

### Task 8: Globe image composer (Core, no WPF)

**Files:**
- Create: `src/EarthWatch3D.Core/Globe/Bgra32Image.cs`
- Create: `src/EarthWatch3D.Core/Globe/GlobeImageComposer.cs`
- Test: `tests/EarthWatch3D.Tests/GlobeImageComposerTests.cs`

**Interfaces:**
- Consumes: `GeoPosition`, `Terminator.SunAltitudeDeg`.
- Produces (namespace `EarthWatch3D.Core.Globe`):
  - `public sealed class Bgra32Image(int width, int height, byte[] pixels)` with `Width`, `Height`, `Pixels` and
    `static Bgra32Image Create(int w, int h)`. Pixels are straight (not premultiplied) BGRA, rows top to bottom,
    stride = 4·width.
  - `public sealed record GlobeLayers(GeoPosition SubSolarPoint, double NightMaxOpacity, Bgra32Image? Clouds = null, Bgra32Image? Areas = null, Bgra32Image? Lines = null)`.
  - `public sealed class GlobeImageComposer(Bgra32Image baseImage)` with:
    - `public void ComposeInto(GlobeLayers layers, Bgra32Image target)`: target has the same size as the base.
      Layers are applied in the order base → clouds → areas → night shade → lines, each alpha-blended.
    - `public static double NightOpacity(double sunAltitudeDeg, double maxOpacity)`.
  - Optional layers must be the same size as the base. Any other size throws `ArgumentException`.

- [ ] **Step 1: Write the failing tests.**

```csharp
public class GlobeImageComposerTests
{
    private static Bgra32Image Solid(int w, int h, byte v)
    { var img = Bgra32Image.Create(w, h); for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = img.Pixels[i+1] = img.Pixels[i+2] = v; img.Pixels[i+3] = 255; } return img; }
    private static byte R(Bgra32Image img, int x, int y) => img.Pixels[(y * img.Width + x) * 4 + 2];

    [Theory]
    [InlineData(10, 0)] [InlineData(0, 0)] [InlineData(-6, 0.275)] [InlineData(-12, 0.55)] [InlineData(-40, 0.55)]
    public void NightOpacityFollowsSmoothstep(double h, double expected) =>
        Assert.Equal(expected, GlobeImageComposer.NightOpacity(h, 0.55), 6);

    [Fact]
    public void DaySideUnchangedNightSideGreyed()
    {   // 360x180: pixel (180, 90) is centred at lat -0.5, lon 0.5; pixel (0, 89) at lat 0.5, lon -179.5 (antipode)
        var composer = new GlobeImageComposer(Solid(360, 180, 255));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(-0.5, 0.5), 0.55), target);
        Assert.Equal(255, R(target, 180, 90));
        Assert.Equal(137, R(target, 0, 89));            // 255·0.45 + 40·0.55 = 136.75 → 137 (red channel of grey is 40)
    }

    [Fact] // Review Focus 1: no seam at ±180°
    public void ShadingContinuousAcrossDateLine()
    {
        var composer = new GlobeImageComposer(Solid(360, 180, 255));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(0, 179.9), 0.55), target);
        Assert.InRange(Math.Abs(R(target, 0, 90) - R(target, 359, 90)), 0, 1);
    }

    [Fact]
    public void LinesDrawnOverNightShade()
    {
        var lines = Bgra32Image.Create(360, 180);
        int i = (89 * 360 + 0) * 4; lines.Pixels[i] = lines.Pixels[i+1] = lines.Pixels[i+2] = 255; lines.Pixels[i+3] = 255;
        var composer = new GlobeImageComposer(Solid(360, 180, 0));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(-0.5, 0.5), 0.55, Lines: lines), target);
        Assert.Equal(255, R(target, 0, 89));
    }

    [Fact]
    public void WrongSizedLayerThrows() =>
        Assert.Throws<ArgumentException>(() => new GlobeImageComposer(Solid(360, 180, 0))
            .ComposeInto(new GlobeLayers(new(0, 0), 0.55, Clouds: Bgra32Image.Create(10, 10)), Bgra32Image.Create(360, 180)));

    [Fact, Trait("Category", "Perf")]
    public void Composes8192UnderBudget()
    {
        var composer = new GlobeImageComposer(Solid(8192, 4096, 128));
        var target = Bgra32Image.Create(8192, 4096);
        composer.ComposeInto(new GlobeLayers(new(10, 20), 0.55), target);  // warm-up
        var sw = System.Diagnostics.Stopwatch.StartNew();
        composer.ComposeInto(new GlobeLayers(new(10, 20), 0.55, Lines: Bgra32Image.Create(8192, 4096)), target);
        Assert.True(sw.ElapsedMilliseconds < 300, $"{sw.ElapsedMilliseconds} ms");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Expected: compile errors.
- [ ] **Step 3: Implement.** Use `Parallel.For` over rows. For speed, per row precompute sin φ and cos φ, and per
  column precompute cos(λ − λ☉). Evaluate the altitude through its sine directly (compare with sin(0°) and
  sin(−12°) instead of calling `asin` per pixel, then apply smoothstep to the angle only for pixels inside the
  twilight band). `smoothstep(e0, e1, x)`: t = clamp((x − e0)/(e1 − e0), 0, 1), result t²(3 − 2t).
  Round blended channels with `Math.Round` (MidpointRounding.AwayFromZero).
- [ ] **Step 4: Run the tests to verify they pass.** Run `dotnet test --filter Category!=Perf`, then
  `dotnet test -c Release --filter Category=Perf`. Expected: PASS. If the Perf test fails, record the time and
  continue with 4096×2048 as the spec's fallback (set in Task 12).
- [ ] **Step 5: Commit.** `git add -A && git commit -m "Globe image composer with terminator night shade"`

---

### Task 9: Globe in the viewport: textured sphere, coastlines, camera rules

**Files:**
- Create: `src/EarthWatch3D.App/Rendering/GlobeMesh.cs`
- Create: `src/EarthWatch3D.App/Rendering/ImageConversions.cs`
- Create: `src/EarthWatch3D.App/Rendering/LineRasterizer.cs`
- Create: `src/EarthWatch3D.App/Rendering/CameraRules.cs`
- Create: `src/EarthWatch3D.App/Rendering/Projector.cs` (adapted from SolarSystem3D, see below)
- Modify: `src/EarthWatch3D.App/MainWindow.xaml(.cs)`
- Test: `tests/EarthWatch3D.Tests/CameraRulesTests.cs`
- Test: `tests/EarthWatch3D.Tests/GlobeMeshTests.cs`

**Interfaces:**
- Consumes: `Bgra32Image`, `GeoJsonLines`, `GeoPosition`.
- Produces (namespace `EarthWatch3D.App.Rendering`):
  - `GlobeMesh.Create(int lonSegments = 360, int latSegments = 180) → MeshGeometry3D`: unit sphere. Texture u =
    (lon + 180)/360 and v = (90 − lat)/180. The seam is duplicated so u runs from 0 to 1 without wrapping.
  - `ImageConversions`:
    - `Bgra32Image DecodeToBgra32(Stream jpegOrPng)`
    - `WriteableBitmap CreateBitmap(int w, int h)`
    - `void CopyInto(Bgra32Image src, WriteableBitmap dst)` (UI thread)
  - `LineRasterizer.Rasterize(IEnumerable<IReadOnlyList<GeoPosition>> lines, int w, int h, Color color, double thicknessPx) → Bgra32Image`:
    uses a `DrawingVisual` and `RenderTargetBitmap` in equirectangular projection, splitting any segment whose
    longitude jump is over 180°.
  - `CameraRules` with:
    - `const double MinDistance = 1.047, MaxDistance = 20.0, ZoomPerNotch = 1.15`
    - `static Point3D ClampDistance(Point3D position)`: keeps the direction, clamps |position|.
    - `static double RotationSensitivity(double distance)`: `clamp((distance − 1)/2, 0.02, 1.0)`.
    - `static (Point3D Position, Vector3D LookDirection, Vector3D UpDirection) LookDownOn(GeoPosition p, double distance)`:
      Position = p's unit vector × distance. LookDirection = −Position (not normalised, so it reaches the Earth's
      centre, as HelixToolkit expects).
  - `Projector` (copied from `C:\repos\SolarSystem3D\src\SolarSystem3D.App\Rendering\Projector.cs`, namespace
    changed), plus `public Vector3D ToCameraSpace(Vector3D worldDirection)`. It returns (right, up, forward)
    components.

- [ ] **Step 1: Write the failing tests.**

```csharp
public class CameraRulesTests
{
    [Theory] [InlineData(0.5, 1.047)] [InlineData(5, 5)] [InlineData(100, 20)]
    public void ClampsDistanceKeepingDirection(double d, double expected)
    {
        var p = CameraRules.ClampDistance(new Point3D(0, -d, 0));
        Assert.Equal(expected, ((Vector3D)p).Length, 9); Assert.Equal(0, p.X, 12);
    }

    [Theory] [InlineData(3, 1.0)] [InlineData(1.047, 0.0235)] [InlineData(1.0, 0.02)]
    public void SensitivityScalesWithAltitude(double d, double expected) =>
        Assert.Equal(expected, CameraRules.RotationSensitivity(d), 4);

    [Theory] // Review Focus 3: poles must not degenerate
    [InlineData(0, 0)] [InlineData(90, 0)] [InlineData(-90, 45)] [InlineData(23.44, 179.9)]
    public void LookDownOnPointsAtCentreWithValidUp(double lat, double lon)
    {
        var (pos, look, up) = CameraRules.LookDownOn(new GeoPosition(lat, lon), 3);
        var expected = new GeoPosition(lat, lon).ToUnitVector() * 3;
        Assert.Equal(expected.X, pos.X, 9); Assert.Equal(expected.Y, pos.Y, 9); Assert.Equal(expected.Z, pos.Z, 9);
        Assert.Equal(-3, Vector3D.DotProduct(look, (Vector3D)pos) / 3, 9);
        Assert.False(double.IsNaN(up.X + up.Y + up.Z));
        Assert.True(Vector3D.CrossProduct(look, up).Length > 0.1);
    }
}

public class GlobeMeshTests
{
    [Fact]
    public void VerticesOnUnitSphereAndTextureMatchesLatLon()
    {
        var m = GlobeMesh.Create(36, 18);
        Assert.All(m.Positions, p => Assert.Equal(1, ((Vector3D)p).Length, 9));
        for (int i = 0; i < m.Positions.Count; i++)
        {
            var g = GeoPosition.FromVector(new(m.Positions[i].X, m.Positions[i].Y, m.Positions[i].Z));
            if (Math.Abs(g.LatitudeDeg) > 89.9) continue;
            Assert.Equal((90 - g.LatitudeDeg) / 180, m.TextureCoordinates[i].Y, 6);
            double du = (m.TextureCoordinates[i].X - (g.LongitudeDeg + 180) / 360) % 1;   // seam: u=0 and u=1 are both lon ±180
            Assert.True(Math.Abs(du) < 1e-6 || Math.Abs(Math.Abs(du) - 1) < 1e-6);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Expected: compile errors.
- [ ] **Step 3: Implement** `GlobeMesh`, `CameraRules`, `ImageConversions`, `LineRasterizer` and `Projector`.
  `LookDownOn`: up = the projection of +Z perpendicular to the look direction. If that is shorter than 1e-6
  (at a pole), use +Y instead. `DecodeToBgra32` uses `BitmapDecoder` with `BitmapCacheOption.OnLoad` and a
  `FormatConvertedBitmap` to `PixelFormats.Bgra32`.
- [ ] **Step 4: Run the tests to verify they pass.** Expected: PASS.
- [ ] **Step 5: Wire up `MainWindow`.**
  - A `HelixViewport3D` fills the window: Background `#04060E`, `CameraRotationMode="Turntable"`,
    `ModelUpDirection="0,0,1"`, `ShowViewCube="False"`, `ZoomAroundMouseDownPoint="False"`, and a perspective
    camera at (0, −3, 1) looking at the origin with up (0, 0, 1) and FOV 45.
  - A `Canvas x:Name="Overlay"` sits on top for later tasks.
  - On load, decode the Blue Marble once. Rasterize the coastlines (white, 60% alpha, 1 px) and borders (light
    grey, 40% alpha, 1 px) into one `Lines` image at the same size. Compose with the current sub-solar point, copy
    into a `WriteableBitmap`, and use it as an `ImageBrush` `DiffuseMaterial` on the `GlobeMesh` inside a
    `ModelVisual3D`, with an `AmbientLight` (white) so the texture shows true colour.
  - On `CameraChanged`, clamp the distance with `ClampDistance` and set the viewport's `RotationSensitivity`
    from `RotationSensitivity`.
  - Mouse wheel: handle `PreviewMouseWheel` yourself (and set the event as handled). Each notch (Delta/120)
    multiplies the distance by `ZoomPerNotch` (wheel back) or divides by it (wheel forward), then clamps.
    Helix's own zoom speed varies with settings, so it isn't used.
  - Home key: `LookDownOn(new(20, −40), 3)` (the default view). Double-click: hit-test the globe, convert the
    hit point to a `GeoPosition`, and `CameraHelper.AnimateTo` the `LookDownOn` of that point at the current
    distance (700 ms).
- [ ] **Step 6: Run the app and check.** Run `dotnet run --project src/EarthWatch3D.App -c Release`. Expected:
  - The Blue Marble globe with coastlines and borders.
  - The night side shaded grey, with continents still visible under it.
  - Dragging rotates. Rotation slows close in.
  - The wheel zoom stops at about 300 km altitude and at 20 radii.
  - Home and double-click work, including double-clicking near a pole.

  Take a screenshot and save it to the scratchpad for the final report.
- [ ] **Step 7: Commit.** `git add -A && git commit -m "Render textured globe with coastlines and camera rules"`

---

### Task 10: Sun & Moon view model and card

**Files:**
- Create: `src/EarthWatch3D.Core/Time/IClock.cs` (`IClock`, `SystemClock`)
- Create: `src/EarthWatch3D.Core/Geo/GeoFormat.cs`
- Create: `src/EarthWatch3D.App/ViewModels/SunMoonViewModel.cs`
- Create: `src/EarthWatch3D.App/ViewModels/MainViewModel.cs`
- Create: `src/EarthWatch3D.App/Views/SunMoonCard.xaml(.cs)`
- Modify: `src/EarthWatch3D.App/MainWindow.xaml`, `App.xaml.cs` (composition root)
- Test: `tests/EarthWatch3D.Tests/GeoFormatTests.cs`
- Test: `tests/EarthWatch3D.Tests/SunMoonViewModelTests.cs`

**Interfaces:**
- Consumes: `SubPoints`, `GeoPosition`.
- Produces:
  - `EarthWatch3D.Core.Time.IClock { DateTime UtcNow { get; } }`, and `SystemClock : IClock`.
  - `EarthWatch3D.Core.Geo.GeoFormat.LatLon(GeoPosition p) → string`: one decimal, with hemisphere letters, for
    example `"12.3°S 45.0°W"`. Zero latitude is shown as `"0.0°N"` and longitude 180 as `"180.0°E"`.
  - `EarthWatch3D.App.ViewModels.SunMoonViewModel : ObservableObject`, with ctor `(IClock clock)` and:
    - Properties: `GeoPosition SubSolar`, `GeoPosition SubLunar`, `string SubSolarText`, `string SubLunarText`,
      `string MoonDistanceText` (for example `"384,400 km"`, thousands separators, invariant culture),
      `int MoonIlluminatedPercent`, `bool MoonWaxing`, `[ObservableProperty] bool IsCollapsed`.
    - `void Refresh()`: reads `clock.UtcNow` and recomputes everything.
    - Commands `FaceSunCommand` and `FaceMoonCommand`, which raise `event EventHandler<GeoPosition>? FaceRequested`
      with the current sub-point.
  - `MainViewModel : ObservableObject`, with ctor `(IClock clock)`, property `SunMoon`, and
    `void Tick()` → `SunMoon.Refresh()`.

- [ ] **Step 1: Write the failing tests.**

```csharp
public class GeoFormatTests
{
    [Theory]
    [InlineData(-12.34, -45.0, "12.3°S 45.0°W")] [InlineData(0, 180, "0.0°N 180.0°E")] [InlineData(18.05, 120.0, "18.1°N 120.0°E")]
    public void Formats(double lat, double lon, string expected) => Assert.Equal(expected, GeoFormat.LatLon(new(lat, lon)));
}

public class SunMoonViewModelTests
{
    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    [Fact]
    public void MatchesAstroCoreAtFixedTime()
    {
        var t = new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc);
        var vm = new SunMoonViewModel(new FakeClock(t)); vm.Refresh();
        Assert.Equal(SubPoints.SubSolarPoint(t), vm.SubSolar);
        Assert.Equal(SubPoints.SubLunarPoint(t), vm.SubLunar);
        Assert.Equal((int)Math.Round(SubPoints.MoonIlluminatedFraction(t) * 100), vm.MoonIlluminatedPercent);
        Assert.Equal(GeoFormat.LatLon(vm.SubSolar), vm.SubSolarText);
        Assert.EndsWith(" km", vm.MoonDistanceText);
    }

    [Fact] // Review Focus 5: a clock jump (sleep/resume) shows the new time immediately
    public void RefreshAfterClockJumpUsesCurrentTime()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc));
        var vm = new SunMoonViewModel(clock); vm.Refresh();
        clock.UtcNow = clock.UtcNow.AddHours(5); vm.Refresh();
        Assert.Equal(SubPoints.SubSolarPoint(clock.UtcNow), vm.SubSolar);
    }

    [Fact]
    public void FaceSunRaisesWithSubSolarPoint()
    {
        var vm = new SunMoonViewModel(new FakeClock(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc))); vm.Refresh();
        GeoPosition? got = null; vm.FaceRequested += (_, p) => got = p;
        vm.FaceSunCommand.Execute(null);
        Assert.Equal(vm.SubSolar, got);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Expected: compile errors.
- [ ] **Step 3: Implement** `IClock`, `GeoFormat`, `SunMoonViewModel` and `MainViewModel`.
- [ ] **Step 4: Run the tests to verify they pass.** Expected: PASS.
- [ ] **Step 5: Card and wiring.**
  - `SunMoonCard` is a rounded semi-transparent dark `Border`, top-right, about 220 px wide.
  - When expanded it shows: ☀ SubSolarText; ☾ SubLunarText; MoonDistanceText; "{MoonIlluminatedPercent}% lit,
    waxing/waning"; and **Face Sun** and **Face Moon** buttons. A chevron toggles `IsCollapsed`, which shows
    only ☀ and ☾ icons.
  - `App.xaml.cs` builds `MainViewModel(new SystemClock())` and sets it as the window's DataContext.
  - A `DispatcherTimer` (1 s) calls `Tick()`.
  - `FaceRequested` animates the camera with `CameraRules.LookDownOn(p, currentDistance)`.
- [ ] **Step 6: Run the app and check** that the card values change over time and that Face Sun and Face Moon
  swing the camera. Expected: Face Sun ends looking straight down on the middle of the unshaded area.
- [ ] **Step 7: Commit.** `git add -A && git commit -m "Sun & Moon card with live sub-points and Face Sun/Moon"`

---

### Task 11: Sun and Moon markers on the globe and edge arrows

**Files:**
- Create: `src/EarthWatch3D.App/Rendering/DirectionIndicator.cs`
- Create: `src/EarthWatch3D.App/Rendering/MoonPhaseGlyph.cs`
- Create: `src/EarthWatch3D.App/Rendering/SunMoonOverlay.cs`
- Test: `tests/EarthWatch3D.Tests/DirectionIndicatorTests.cs`
- Test: `tests/EarthWatch3D.Tests/MoonPhaseGlyphTests.cs`

**Interfaces:**
- Consumes: `Projector.ToCameraSpace`, `Projector.TryProject`, `SunMoonViewModel`.
- Produces (namespace `EarthWatch3D.App.Rendering`):
  - `public readonly record struct IndicatorPlacement(bool Visible, bool OnScreen, Point Position, double AngleDeg)`.
    AngleDeg is the screen-space direction (0 = right, 90 = down).
  - `DirectionIndicator.Place(Vector3D fromEye, Projector projector, double inset) → IndicatorPlacement`.
  - `MoonPhaseGlyph.CreateGeometry(double illuminatedFraction, bool waxing, double radius) → Geometry`: the lit
    part of a disc centred at (0, 0). Waxing lights the right-hand side, as seen from the northern hemisphere.
  - `SunMoonOverlay(Canvas overlay)` with `void Update(Projector projector, Vector3D sunDirection, Point3D moonPosition, double moonFraction, bool waxing)`.
    The Sun uses `fromEye` = its direction (at infinity). The Moon uses `fromEye` = moonPosition − eye, where
    moonPosition = `SubLunar.ToUnitVector() × DistanceKm / 6371`.

  Placement rule (the algorithm the signature doesn't determine):

```
if projector.Width < 2*inset+1 or projector.Height < 2*inset+1: return Visible=false   // Review Focus 4
c = projector.ToCameraSpace(fromEye)               // (x right, y up, z forward)
if c.z > 0 and TryProject(eye + fromEye) is inside [inset, W-inset]×[inset, H-inset]: OnScreen at that point
else:
  v = (c.x, -c.y)                                  // screen axes, y down; same direction as the projected offset when z>0
  if |v| < 1e-9: v = (0, 1)                        // straight behind: point down
  s = min((W/2-inset)/|v.x|, (H/2-inset)/|v.y|)    // treat a zero component as +∞
  Position = (W/2, H/2) + v*s ; AngleDeg = atan2(v.y, v.x) in degrees
```

- [ ] **Step 1: Write the failing tests.**

```csharp
public class DirectionIndicatorTests
{   // Camera at (0,-5,0) looking +Y, up +Z, FOV 60, viewport 800x600, inset 40
    private static Projector P(double w = 800, double h = 600) =>
        new(new PerspectiveCamera(new Point3D(0, -5, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), 60), w, h);

    [Fact] public void AheadIsOnScreenAtCentre()
    { var r = DirectionIndicator.Place(new(0, 1, 0), P(), 40); Assert.True(r.OnScreen); Assert.Equal(400, r.Position.X, 6); Assert.Equal(300, r.Position.Y, 6); }

    [Fact] public void RightIsArrowOnRightEdge()
    { var r = DirectionIndicator.Place(new(1, 0, 0), P(), 40); Assert.False(r.OnScreen); Assert.Equal(760, r.Position.X, 6); Assert.Equal(300, r.Position.Y, 6); Assert.Equal(0, r.AngleDeg, 6); }

    [Fact] public void UpIsArrowOnTopEdge()
    { var r = DirectionIndicator.Place(new(0, 0, 1), P(), 40); Assert.Equal(400, r.Position.X, 6); Assert.Equal(40, r.Position.Y, 6); Assert.Equal(-90, r.AngleDeg, 6); }

    [Fact] public void BehindLeftIsArrowOnLeftEdge()
    { var r = DirectionIndicator.Place(new(-1, -1, 0), P(), 40); Assert.Equal(40, r.Position.X, 6); Assert.Equal(300, r.Position.Y, 6); Assert.Equal(180, Math.Abs(r.AngleDeg), 6); }

    [Fact] public void StraightBehindPointsDown()
    { var r = DirectionIndicator.Place(new(0, -1, 0), P(), 40); Assert.Equal(400, r.Position.X, 6); Assert.Equal(560, r.Position.Y, 6); Assert.Equal(90, r.AngleDeg, 6); }

    [Fact] public void FarCornerClampsToInsetRectangle()
    { var r = DirectionIndicator.Place(new(1, 0.01, 1), P(), 40); Assert.InRange(r.Position.X, 40, 760); Assert.InRange(r.Position.Y, 40, 560); }

    [Theory] // Review Focus 4
    [InlineData(0, 0)] [InlineData(50, 50)]
    public void TinyViewportHidesWithoutNaN(double w, double h)
    { var r = DirectionIndicator.Place(new(1, 0, 0), P(w, h), 40); Assert.False(r.Visible); }
}

public class MoonPhaseGlyphTests
{
    [Theory] [InlineData(0.0)] [InlineData(0.25)] [InlineData(0.5)] [InlineData(0.9)] [InlineData(1.0)]
    public void LitAreaMatchesFraction(double k)
    {
        var g = MoonPhaseGlyph.CreateGeometry(k, waxing: true, radius: 100);
        double disc = Math.PI * 100 * 100;
        Assert.InRange(g.GetArea(0.01, ToleranceType.Absolute) - k * disc, -0.005 * disc, 0.005 * disc);
    }

    [Fact]
    public void WaxingLightsRightSide()
    {
        var g = MoonPhaseGlyph.CreateGeometry(0.3, waxing: true, radius: 100);
        Assert.True(g.FillContains(new Point(90, 0))); Assert.False(g.FillContains(new Point(-90, 0)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Expected: compile errors.
- [ ] **Step 3: Implement.**
  - `MoonPhaseGlyph`: the lit region is bounded by the lit half-circle and the terminator half-ellipse, with
    semi-minor axis r·|2k − 1|. The ellipse bulges toward the dark side when k > 0.5 and toward the lit side when
    k < 0.5. Waning mirrors it in x.
  - `SunMoonOverlay`:
    - **On screen:** draws a Sun icon (gold disc, 18 px) or a Moon icon (grey disc with `MoonPhaseGlyph`,
      18 px) at the position.
    - **Off screen:** draws an arrow (a 22 px chevron `Path` rotated by AngleDeg) with the text "Sun" or "Moon"
      beside it.
    - Elements are pooled; nothing is allocated per frame.
  - On the globe: two `BillboardVisual3D` markers at `SubSolar.ToUnitVector() × 1.01` and
    `SubLunar.ToUnitVector() × 1.01`, with the Sun and phase-drawn Moon images (`DrawingImage`), 20 px.
- [ ] **Step 4: Run the tests to verify they pass.** Expected: PASS.
- [ ] **Step 5: Run the app and check.**
  - With Face Sun, the Sun marker is in the middle of the lit side.
  - Rotating so the Sun direction leaves the screen moves the Sun arrow along the edge, pointing the right way.
  - Turning to look straight away from the Sun makes the arrow point down.
  - Minimising and restoring the window causes no exceptions.

  Take screenshots of on-screen and off-screen states.
- [ ] **Step 6: Commit.** `git add -A && git commit -m "Sun/Moon overhead markers, phase glyph and edge arrows"`

---

### Task 12: Live updates, scene controller, and verification

**Files:**
- Create: `src/EarthWatch3D.App/Controllers/SceneController.cs`
- Modify: `src/EarthWatch3D.App/MainWindow.xaml.cs` (move the wiring from Tasks 9–11 into `SceneController`)
- Create: `docs/perf.md`
- Create: `README.md`
- Test: `tests/EarthWatch3D.Tests/RecomposeScheduleTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `EarthWatch3D.App.Controllers.RecomposeSchedule` (pure logic), with ctor `(TimeSpan interval)` and
    `bool IsDue(DateTime nowUtc)` and `void MarkComposed(DateTime nowUtc)`. It is due when nothing has been
    composed yet, when `now − last ≥ interval`, **or when `now < last`** (the clock went backwards).
  - `SceneController(HelixViewport3D viewport, Canvas overlay, MainViewModel vm, IClock clock)`:
    - Owns the globe, markers and overlay.
    - Composes on a background `Task`, never more than one at a time; requests that arrive while one is running
      collapse into one follow-up compose.
    - Copies the result into the `WriteableBitmap` on the UI thread.
    - Updates the overlay on `CompositionTarget.Rendering` only when the camera or time changed.
    - Calls `Recompose()` on `SystemEvents.PowerModeChanged` (Resume).

- [ ] **Step 1: Write the failing tests.**

```csharp
public class RecomposeScheduleTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void DueInitially() => Assert.True(new RecomposeSchedule(TimeSpan.FromMinutes(1)).IsDue(T0));

    [Fact] public void NotDueWithinInterval()
    { var s = new RecomposeSchedule(TimeSpan.FromMinutes(1)); s.MarkComposed(T0); Assert.False(s.IsDue(T0.AddSeconds(59))); Assert.True(s.IsDue(T0.AddSeconds(60))); }

    [Fact] // Review Focus 5: sleep/resume or clock change
    public void DueAfterLargeJumpOrBackwardsClock()
    { var s = new RecomposeSchedule(TimeSpan.FromMinutes(1)); s.MarkComposed(T0); Assert.True(s.IsDue(T0.AddHours(6))); Assert.True(s.IsDue(T0.AddMinutes(-5))); }
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Expected: compile errors.
- [ ] **Step 3: Implement** `RecomposeSchedule` and `SceneController`. Move the window wiring into the
  controller, so `MainWindow.xaml.cs` only constructs it. If Task 8's Perf test failed, set the globe image size
  to 4096×2048 here (one constant, `SceneController.GlobeImageWidth`).
- [ ] **Step 4: Run all tests.** Run `dotnet test --filter Category!=Perf`. Expected: all pass.
- [ ] **Step 5: Measure and record in `docs/perf.md`** (Release build), with the date and machine description:
  - Compose time (8192 and 4096).
  - The working set after startup.
  - Frames per second while rotating.

  Use a temporary `Stopwatch` log behind `#if PERF_LOG`.
- [ ] **Step 6: Write the README skeleton.** Include:
  - What the app does today: globe, night shading, Sun and Moon markers, arrows and card.
  - Build and run: `git clone --recursive`, `dotnet run --project src/EarthWatch3D.App -c Release`.
  - Controls.
  - Credits: NASA Blue Marble, Natural Earth, SolarSystem3D/AstroCore origin and HelixToolkit.

  Mark the rest as coming in plans 2–4.
- [ ] **Step 7: Run the app and check.** Leave it running for at least 3 minutes. Expected:
  - The terminator moves (compare screenshots 3 minutes apart at the same camera angle).
  - The UI never freezes during a compose.
  - The card values update every second.
- [ ] **Step 8: Commit.** `git add -A && git commit -m "Scene controller with live recompose; perf notes and README"`
- [ ] **Step 9: Pin the submodule.** Confirm `external/AstroCore` points at AstroCore's latest commit
  (`git submodule status`). If not, update it and commit `"Update AstroCore submodule"`.
