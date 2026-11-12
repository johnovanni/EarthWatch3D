# EarthWatch3D — Design

**Date:** 2026-11-11
**Status:** Draft for review

## 1. Purpose

EarthWatch3D is a Windows desktop app (C#, WPF) that shows a 3D Earth with what is happening on it now:
earthquakes, storms and lightning, as soon as each source reports them. It also shows where the Sun and Moon are.
You can rotate the globe and zoom in and out. Arrows on the screen point toward the Sun and the Moon.

It is for people who are curious about the live state of the planet. Other people will install it as an MSIX
package, so it has to work without developer tools and explain itself to non-technical users.

### Success criteria

- When it starts, the app shows current earthquakes, tropical cyclones, US weather alerts, NASA EONET storms
  and lightning (GOES coverage, plus Meteosat coverage if the user has entered a EUMETSAT key) within about
  30 seconds on a normal broadband connection.
- The point directly under the Sun and the point directly under the Moon are correct to 0.05°. The day/night
  shading follows the real terminator.
- The time slider replays the last 7 days of earthquakes and the last 24 hours of storms and alerts. The Sun,
  Moon and night shading move with it.
- Data use stays small: roughly 100 MB per hour with lightning on (estimate, to be measured), and only a few MB
  per day with lightning off.
- It installs, updates itself and uninstalls cleanly as a signed MSIX.

### Decisions made during brainstorming

| Topic | Decision |
|---|---|
| Storms | Tropical cyclones (GDACS + NHC), US severe-weather alerts (NWS) and NASA EONET severe storms: all three |
| Lightning | NOAA GOES GLM (no key) plus EUMETSAT MTG Lightning Imager (each user enters their own free key; never built into the app) |
| Lightning history | **Live only.** A rolling window of 5–60 minutes, 15 by default. No replay. The code is designed so a Windows Service collector can be added later (§9) |
| Time | Live by default, plus replay: 7 days for earthquakes, 24 hours for storms and alerts |
| Audience | Other people, installing a packaged app |
| Globe look | Blue Marble on both hemispheres. The night side is darkened with a slight blue tint, and a thin gold line marks the day/night boundary (changed 2026-11-11 from the original translucent grey layer, which lightened the dark Blue Marble oceans instead of darkening them). Live clouds can be switched on (NASA GIBS) |
| Plate boundaries | Tectonic plate boundaries (Peter Bird's PB2002 model) can be switched on, coloured by type: converging, spreading, sliding past (added 2026-11-11) |
| Rendering | WPF 3D with HelixToolkit.Wpf (the same approach as SolarSystem3D) |
| Shared code | A new **AstroCore** repo, used as a git submodule at `external/AstroCore` |
| Packaging | Self-contained x64 MSIX signed with Azure Trusted Signing, with an `.appinstaller` file for automatic updates |

### Rulings made while building Plan 2 that changed behaviour

| Ruling | Decision |
|---|---|
| R5 | The USGS week refresh deletes absent quakes only between the week's start and the feed's own `generated` time, so quakes newer than the CDN-cached week feed don't flicker (§4, §5.1) |
| R7 | NWS Update and Cancel messages end the alerts they reference: the referenced alert's `CancelledUtc` becomes the newer message's sent time, including when the reference arrives first (§5.4) |
| R8 | A merged NHC storm takes GDACS's past track when NHC has none, as it already took the wind bands (§5.3) |
| R10 | Plate boundaries are drawn 3 px wide, fully opaque, in saturated colours (convergent #FF3030, divergent #FFE600, transform #00E5FF) instead of 1.5 px at 85% (§6, 5a) |

### Out of scope

- Lightning history and replay, and the collector service (only its seam is designed, in §9).
- Blitzortung or any other unofficial lightning feed.
- High-resolution tiled imagery for close zoom. The closest zoom is limited to about 300 km altitude.
- Monthly (seasonal) Blue Marble images, city lights and a starfield.
- Microsoft Store submission (MSIX keeps it possible later).
- Switching SolarSystem3D over to AstroCore. That is a separate follow-up task.

## 2. Repositories and projects

```
C:\repos\AstroCore                     new repo: shared astronomy library (net10.0, no UI)
  src/AstroCore                        Vector3d, Matrix3d, AstroTime, Frames, Conic,
                                       IOrbitModel, PlanetOrbit, LunarOrbit, EarthOrbit,
                                       + new: Precession, EarthRotation, SubPoints, Terminator
  tests/AstroCore.Tests                moved Horizons accuracy tests + new tests (§10)

C:\repos\EarthWatch3D                  new repo
  external/AstroCore                   git submodule (pinned commit)
  src/EarthWatch3D.Core                net10.0, no UI: events, feeds, store, lightning, geo helpers
  src/EarthWatch3D.App                 net10.0-windows WPF app (HelixToolkit.Wpf, CommunityToolkit.Mvvm)
  packaging/                           AppxManifest.xml template, MSIX logos, .appinstaller template
  tests/EarthWatch3D.Tests             feed parser, store, replay, lightning tests; tests/Samples
  tools/                               scripts: image preparation, sample capture, signing
```

- **.NET 10** (LTS) for everything. .NET 8 reached end of support on 2026-11-10, so a newly shipped app should not
  target it. SolarSystem3D moves to .NET 10 as part of its switch to AstroCore.
- Both repos are created locally. Nothing is pushed to GitHub until the user asks.
- The AstroCore code is copied from SolarSystem3D's `Astronomy/` and `Orbits/` folders: `Vector3d`, `Matrix3d`,
  `AstroTime`, `Frames`, `Conic`, `IOrbitModel`, `OrbitSummary`, `PlanetOrbit`, `LunarOrbit` and `EarthOrbit`.
  Earth's orbital elements, which now sit in SolarSystem3D's `MajorBodyCatalog`, move into AstroCore as a small
  static class.
- The namespace changes from `SolarSystem3D.Core.*` to `AstroCore.*`. The behaviour stays the same, and the
  moved tests prove it.
- EarthWatch3D adapts parts of SolarSystem3D's app code (a copy, not a library): the label overlay with overlap
  avoidance, the screen projector and parts of the scene controller. The README credits the source.

## 3. Astronomy (AstroCore additions)

All of AstroCore works in the J2000 mean ecliptic frame, with times as TT Julian dates (existing convention).
The new code adds:

- **`Precession`**: precession of the equinoxes from J2000 equatorial coordinates to the mean equator and
  equinox of date (Meeus ch. 21). This is needed because about 0.36° of precession has built up since J2000,
  far more than the 0.05° target.
- **`EarthRotation`**: Greenwich mean sidereal time from UT (Meeus ch. 12). The app treats UT1 as UTC; the
  difference is under 0.9 s, about 0.004°.
- **`SubPoints`**:
  - Sun: geocentric Sun = −`EarthOrbit` position → equatorial J2000 → precessed to date → right ascension and
    declination. Sub-solar latitude = δ, longitude = α − GMST. Nutation (≤0.005°) and aberration (0.006°) are
    ignored, which keeps the result well inside the 0.05° tolerance.
  - Moon: the same steps, starting from `LunarOrbit`. It also returns the Moon's distance in km.
  - The phase (illuminated fraction) comes from the Sun–Earth–Moon angle.
- **`Terminator`**: Sun altitude at any latitude and longitude:
  `sin h = sin φ sin δ + cos φ cos δ cos(λ − λ☉)`.

The app's Earth-fixed frame: unit sphere = one Earth radius (6371 km), +X toward (0°, 0°), +Y toward (0°, 90°E),
+Z toward the North Pole. The camera's up direction is +Z.

## 4. Event model, store and replay (EarthWatch3D.Core)

### Model

```csharp
abstract record EarthEvent(string Source, string Id, DateTime TimeUtc, GeoPoint Location, string Title, Uri? Link);
record Earthquake(..., double Magnitude, double DepthKm, string Place, DateTime UpdatedUtc);
record CycloneAdvisory(..., string Name, string Basin, int? Category, double? WindKt, double? PressureHpa,
                       IReadOnlyList<TrackPoint> PastTrack, IReadOnlyList<TrackPoint> ForecastTrack,
                       GeoPolygon? Cone, IReadOnlyList<WindArea> WindAreas, DateTime IssuedUtc);
record WeatherAlert(..., string EventType, string Severity, DateTime OnsetUtc, DateTime EndsUtc,
                    DateTime? CancelledUtc, IReadOnlyList<GeoPolygon> Areas, IReadOnlyList<string> ZoneIds,
                    string Headline, string Description);   // Areas empty when the alert gives only ZoneIds
record EonetStorm(..., IReadOnlyList<(DateTime t, GeoPoint p)> Track, DateTime? ClosedUtc);
readonly record struct LightningFlash(DateTime TimeUtc, float Lat, float Lon, float Energy, LightningSensor Sensor);
```

`GeoPolygon` stores rings in degrees. Polygons that cross the date line are split there when they are read.

### Store

- `EventStore` keeps earthquakes (7 days), cyclone advisories (24 h of snapshots), weather alerts (24 h) and
  EONET storms (7 days). It is thread-safe: feeds write from background threads, and the UI reads immutable
  snapshots.
- Earthquakes are upserted by ID, so a revised magnitude replaces the old version. An earthquake missing from a
  full weekly refresh counts as deleted and is removed, but only between the week's start and the feed's own
  `generated` time (ruling R5, §5.1).
- The store is saved to `%LOCALAPPDATA%\EarthWatch3D\cache\events.json` 10 s after the last change, at most once
  every 5 minutes, and once more on exit, and is loaded at startup. The file carries a `SchemaVersion` (currently 2):
  a file from another version, a corrupt file, or one whose records hold nulls the model forbids is deleted and its
  data downloaded again. MSIX redirects this path
  into the package's own folder transparently.
- Lightning is not kept in the store. It lives in memory in the lightning source (§5.7).

### What is visible at time *t*

*t* is the display time: `MainViewModel.DisplayTimeUtc`, owned by the time bar's `TimelineViewModel`. In live mode
it follows the clock; in replay it is the slider's time. Everything time-dependent (events, the Sun and Moon, the
night shading) reads this one value, and a `DisplayTimeChanged` event tells the scene to update. `IClock` is used
only for wall-clock needs (feed polling, staleness, the live time itself).

- **Earthquakes:** `TimeUtc ≤ t` and within the display window (24 h by default, 7 days optional). The colour
  goes from red to orange to yellow to faded as the quake ages through the window. Quakes less than an hour old
  also get a pulsing ring (live mode only).
- **Cyclones:** for each storm, the latest advisory with `IssuedUtc ≤ t`. A storm with no advisory in the
  previous 24 h is hidden.
- **Weather alerts:** `OnsetUtc ≤ t < min(EndsUtc, CancelledUtc)`, and the alert type passes the US alert
  filter (§5.4: Warnings only by default).
- **EONET storms:** the latest track point at or before *t*, provided the storm was not closed before *t*, and
  no visible GDACS or NHC cyclone is within 300 km of that point (§5.5).
- **Lightning:** only in live mode, as flashes within the lightning window before now. When *t* is not live, the
  layer hides and the legend says "Lightning: live only".

## 5. Data sources

Every feed implements `IEventFeed` (`string Name`, `TimeSpan Interval`, `Task RunOnceAsync(CancellationToken)`).
A run fetches what it needs and writes it straight into the `EventStore`; a thrown exception is the run's failure.
The `FeedScheduler` checks every 5 s and starts each feed that is due without waiting for the others, so one slow
feed never holds back the rest; a feed is not started again while its own run is in flight.

- **HTTP:** one shared `HttpClient` with a 30 s timeout. The User-Agent is `EarthWatch3D/<version> (+<project
  page>)`, set in one constant. It never contains user data.
- **Retries:** after a failure, retry delays go 30 s → 1 → 2 → 5 → 10 min (cap).
- **Status:** each feed reports its status (last success, last error) to the status strip.

The endpoints and fields below were verified against the live services on 2026-11-11
(`docs/data-sources.md` has the details and `tests/Samples` the captured responses).

### 5.1 USGS earthquakes

- At startup: `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson` (about 1.3 MB). Every 60 s:
  `.../all_hour.geojson` (a few KB). The full weekly refresh repeats every 30 min to pick up deletions and revisions.
  USGS regenerates both feeds every minute.
- Fields: top-level `id`; `properties.mag` (can be null), `place`, `time` and `updated` (both **epoch
  milliseconds**), `url`, `type`; `geometry.coordinates` = [lon, lat, depth km].
- `properties.type` is not always `earthquake` (quarry blasts and explosions appear). Only `earthquake` is shown.
- The minimum magnitude shown is a display filter (M2.5 by default). All quakes are stored.
- **Week refresh window (ruling R5):** the CDN copy of the week feed can be a minute or so old, so the refresh
  removes only absent quakes between the week's start and the feed's `metadata.generated` time; newer quakes stay.
  A week response with no quakes at all is treated as a bad response: it is logged and nothing is removed.

### 5.2 GDACS tropical cyclones

- Every 15 min: `https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH?eventlist=TC&alertlevel=green;orange;red&fromdate=<yyyy-MM-dd>&todate=<yyyy-MM-dd>`
  (dates cover the last 2 days). The alert level is always given explicitly, because the default hides green
  storms. The response is a GeoJSON FeatureCollection with one Point per event (its current episode).
- Event fields: `eventid`, `episodeid`, `eventname` (e.g. `PENA-26`), `name`, `alertlevel`, `iscurrent` (a
  **string** `"true"`/`"false"`), `fromdate`, `todate`, `datemodified` (ISO, UTC, no zone), `source` (NOAA, JTWC,
  RSMC), `severitydata`, and `url.geometry`.
- Geometry: for each new or changed episode (new `episodeid` or `datemodified`), fetch `properties.url.geometry`
  (`/gdacsapi/api/polygons/getgeometry?eventtype=TC&eventid=…&episodeid=…`, about 200–300 KB). Features are told
  apart by `properties.Class`, never by `polygonlabel` text:

  | `Class` | Use |
  |---|---|
  | `Point_Centroid` | Current position |
  | `Line_Line_<n>` | Track segment n (past and forecast) |
  | `Poly_Cones` | Forecast cone (MultiPolygon, already cut at ±180°) |
  | `Poly_Green` / `Poly_Orange` / `Poly_Red` | Wind bands. A `polygonlabel` ending in `km/h` is a current band; a time label is a forecast step (not drawn) |
  | `Point_Polygon_Point_<n>` | Small radii circles: not drawn |

- Episodes follow agency advisories, about every 6 h.

### 5.3 NHC tropical cyclones

- Every 15 min: `https://www.nhc.noaa.gov/CurrentStorms.json` (Atlantic and East/Central Pacific). It is
  `{ "activeStorms": [...] }`, an empty array when nothing is active, served with a 5-minute CDN cache.
- Storm fields: `id` (e.g. `cp042026`), `name` (e.g. `Pena`), `classification` (`TD`, `TS`, `HU`…), `intensity`
  (knots, **string**), `pressure` (mb, string), `latitudeNumeric`/`longitudeNumeric` (west negative),
  `movementDir`, `movementSpeed`, `lastUpdate`.
- Geometry comes from KMZ files (a zip with one KML): `forecastTrack.kmzFile` (LineStrings and Points) and
  `trackCone.kmzFile` (one Polygon). Re-download only when their `advNum`/`fileUpdateTime` changes.
- **NHC KMZ longitudes are not wrapped** (a cone near the date line ran from 177° to 192.6°). The KML reader
  normalises longitudes and splits shapes that cross ±180°.
- **Merging with GDACS:** a GDACS storm and an NHC storm are the same storm when their names match after removing
  GDACS's `-YY` year suffix (case-insensitive; `PENA-26` = `Pena`) and their current positions are within
  300 km. In NHC basins the NHC advisory wins, and the GDACS wind bands and past track are kept if NHC has none
  (NHC's feed gives no past track, so this is where an NHC storm's past track comes from; ruling R8).

### 5.4 NWS US alerts

- Every 2 min: `https://api.weather.gov/alerts/active` (about 1.8 MB, no paging). At startup, the last 24 h:
  `https://api.weather.gov/alerts?start=<ISO UTC>`, following `pagination.next` until it is absent (pages of
  500; about 1,100 alerts and roughly 5 MB per day). A User-Agent is required.
- Fields: `id` (URN), `properties.event` (e.g. `Tornado Warning`), `severity`, `status`, `messageType`, `sent`,
  `effective`, `onset`, `expires`, `ends`, `headline`, `description`, `areaDesc`, `affectedZones`, `references`.
- **Geometry is null for 94–98% of alerts.** Those use `affectedZones`: each zone URL
  (`https://api.weather.gov/zones/forecast/<id>`, 20–120 KB) returns a Polygon or MultiPolygon. Zones are fetched
  on demand (at most 4 requests at a time) and cached in `cache/zones/` for 30 days. A failed zone download (an
  HTTP error, no geometry, a parse error) is not tried again for 10 min, doubling after each further failure up to
  6 h.
- **References (ruling R7):** an Update or Cancel message ends the alerts it lists in `references`. The store sets
  each referenced alert's `CancelledUtc` to the newer message's `sent` time (the earliest one wins), remembering
  references that arrive before their original, and records whether an Update (superseded) or a Cancel
  (cancelled) ended it. The details panel's "Until" shows the earlier of the end and that time, with
  "(superseded)" or "(cancelled)". A Cancel message itself never shows.
- **Default filter: Warnings only** (an `event` ending in "Warning"). Watches, Advisories and Statements can each
  be switched on in the ⋯ Display flyout. Zones are downloaded only for alerts that pass the filter.
- Colours follow the standard NWS hazard colours for the common types (tornado, severe thunderstorm, flash flood,
  winter storm, hurricane, gale and so on). Other types get a neutral colour.

### 5.5 NASA EONET

- Every 30 min: `https://eonet.gsfc.nasa.gov/api/v3/events?category=severeStorms&status=all&days=7` (slow: 3–4 s).
- Events: `id`, `title`, `closed` (null while open, ISO when closed), `sources[].id` and `sources[].url` (the
  details panel links to the first source's page, falling back to `link`), and `geometry`: a list of
  Points, one per fix (`date`, `coordinates` [lon, lat], `magnitudeValue` in kts), usually 6-hourly.
- **EONET repeats storms GDACS and NHC already have** (in the sample all four were JTWC storms, one also an NHC
  storm). An EONET storm is hidden at time *t* when a GDACS or NHC cyclone is visible within 300 km of its
  position at *t*. It is shown only when it adds a storm the others don't have.

### 5.6 Clouds (NASA GIBS)

- No global geostationary infrared layer exists in GIBS (only separate GOES-East, GOES-West and Himawari
  layers, which leave Europe, Africa and the western Indian Ocean uncovered). The cloud layer is therefore
  `VIIRS_NOAA21_CorrectedReflectance_TrueColor`, requested for the **previous UTC day** (the current day is
  partial), as one WMS `GetMap` image (EPSG:4326) at 4096×2048, used with brightness-keyed alpha.
- It refreshes once a day. It has thin gaps between satellite passes. The cloud image is optional and hidden
  while missing.

### 5.7 Lightning

`ILightningSource` (§9) is implemented now by `LiveLightningSource`. It combines:

- **GOES GLM:**
  - Satellites: GOES-19 (East, 75.2°W) and GOES-18 (West, 137.2°W).
  - Files: public S3 buckets `noaa-goes19` and `noaa-goes18`, listed anonymously with
    `https://noaa-goes1X.s3.amazonaws.com/?list-type=2&prefix=GLM-L2-LCFA/<yyyy>/<ddd>/<HH>/&start-after=<last key>`
    (ddd = day of year; one 20-second file each, 180 per hour). Roll the prefix over each hour.
  - Latency: a file appears about 10 s (5–55 s) after its period ends. Poll every 20 s.
  - Size: GOES-19 about 390 KB per file (39–72 MB/h, storm-dependent), GOES-18 about 165 KB (about 30 MB/h).
  - Reading: **PureHDF 2.2.0**, no filter package needed. `flash_lat` and `flash_lon` are plain float32.
    `flash_energy` and `flash_time_offset_of_first_event` are packed uint16 (`_Unsigned`) with `scale_factor` and
    `add_offset`. Flash times can start up to about 1.15 s before the file's start time, so flashes are
    de-duplicated by time and position.
  - The downloaded file is thrown away after reading. Only flashes are kept.
- **EUMETSAT MTG LI** (only when the user has entered a key):
  - Satellite: Meteosat at 0°.
  - Sign-in: an OAuth token from `https://api.eumetsat.int/token` using the user's consumer key and secret.
  - Finding new files: search the Data Store for collection `EO:EUM:DAT:0691` (LI-2-LFL, lightning flashes) for
    products newer than the last one read, then download each product's NetCDF file.
  - Reading: the same PureHDF reader, with this product's variable names. **Not yet verified: Plan 3 starts by
    checking the product layout, file size and latency with the user's key.**
  - If latency turns out to be well over a few minutes, the legend shows the actual age of the newest MTG flash
    and nothing is described as "live".
- **Overlap rules** (each flash is kept by exactly one satellite, decided by longitude):
  - GOES-West: west of 106.2°W.
  - GOES-East: from 106.2°W to 37.6°W.
  - MTG: east of 37.6°W, out to its edge of coverage.
  - With no MTG key, GOES-East keeps everything it sees east of 106.2°W.
- **Only download what is in view:** a satellite is polled only when its coverage disk faces the camera
  (the angle between its sub-satellite point and the view direction is below 100°). When polling resumes, it
  catches up on the files it missed, up to the length of the window.
- **Memory:** a ring buffer of flashes covering the window, capped at 100,000 flashes. The oldest are dropped
  first.

### 5.8 Data use

- `DownloadMeter` counts the bytes received by all feeds, resets at local midnight and shows "Today: 123 MB" in
  the status bar.
- Optional daily limit, off by default. When the limit is reached, lightning polling pauses and the status bar
  says why. Other feeds continue, since they are tiny.

## 6. Rendering (EarthWatch3D.App)

### Globe

- A UV sphere (360×180 segments) with the composed globe image as its material.
- `GlobeImageComposer` builds an 8192×4096 BGRA image on a background thread, using `Parallel.For` over rows:
  1. **Blue Marble** (NASA Blue Marble Next Generation, public domain). It ships with the app as an 8192×4096
     JPEG, made by `tools/` from the 21600×10800 source.
  2. **Clouds**, alpha-blended, if switched on and available.
  3. **Area features**: NWS alert polygons, cyclone cones and wind areas, filled at 35% opacity with 100% edges.
     They are drawn with a WPF `DrawingVisual` in equirectangular projection into a `RenderTargetBitmap` overlay,
     redrawn only when those features change.
  4. **Night shade**: for each pixel, the Sun altitude *h* comes from `Terminator`. Each channel is darkened:
     `v × (1 − f·(1 − m))`, with f = `strength × smoothstep(0°, −12°, h)` and full-night multipliers
     m = R 0.32, G 0.36, B 0.50 (a dark blue night that never brightens a pixel). `strength` defaults to 0.85 and
     can be set from 0 to 1.
  4a. **Day/night line**: a soft gold line (RGB 255, 200, 120, peak 70% opacity) along h = 0, fading out
     2.5 image pixels each side, so the boundary shows even over dark ocean. It can be switched off.
  5. **Coastlines and borders** (Natural Earth 1:50m, public domain), 1-pixel lines, optional.
  5a. **Tectonic plate boundaries** (optional, off by default): Peter Bird's PB2002 model (Bird 2003, *G³*
     4(3):1027), as converted by Hugo Ahlenius / Nordpil (ODC Attribution licence; credit required). The 5,824
     classified boundary steps are merged by `tools/prepare-assets.ps1` into polylines with a `kind`:
     - **convergent** (subduction `SUB`, oceanic and continental convergent `OCB`/`CCB`): #FF3030;
     - **divergent** (spreading ridges `OSR`, continental rifts `CRB`): #FFE600;
     - **transform** (`OTF`, `CTF`): #00E5FF. All fully opaque, drawn 3 px wide so they read at full-globe zoom.

     Drawn 3 px wide, fully opaque, into the same lines layer as the coastlines, on top of them (ruling R10: the
     first style, 1.5 px at 85% in paler colours, looked nearly grey at full-globe zoom).
- **When it is rebuilt:** once a minute in live mode, whenever the overlay or clouds change, and while the time
  slider moves (at most every 250 ms, with a final rebuild once the slider stops).
- The finished image is swapped onto the material on the UI thread. Target time: under 300 ms. If that target is
  missed, it falls back to 4096×2048.

### Events (3D geometry, kept just above the surface at r = 1.002)

- **Earthquakes:** flat discs tangent to the surface, radius `∝ 1.5^magnitude` (with a minimum size on screen),
  coloured by age. All quakes go into one merged mesh per colour bucket to keep WPF fast.
- **Cyclones:**
  - A storm glyph (billboard) at the current position.
  - The past track as a solid tube or line, and the forecast track as a dashed line, using HelixToolkit
    `LinesVisual3D`.
  - A name and category label.
- **EONET storms:** a point glyph and a short track line.
- **Lightning:** HelixToolkit `PointsVisual3D` (screen-space points), rebuilt at most once a second. Colour and
  size fade with age: from white-yellow to dim orange.

### Sun and Moon

- **Overhead markers:** small Sun and Moon glyphs sit on the globe at the sub-solar and sub-lunar points. The
  Moon glyph is drawn at its current phase.
- **Direction indicators:** a 2D overlay above the viewport.
  - Each frame, the direction (a point far along it) is projected to the screen.
  - If it is in front of the camera and inside the viewport, a Sun or Moon icon is drawn there.
  - Otherwise, an arrow is drawn at the screen edge. The arrow is clamped to an inset rectangle and points
    toward the direction's projected position (mirrored when the direction is behind the camera).
  - Each arrow is labelled "Sun" or "Moon".
- **Sun & Moon panel:** sub-solar and sub-lunar latitude and longitude, the Moon's distance and its illuminated
  percentage.

### Camera and input

- An orbit camera around the Earth's centre.
  - Distance: from 1.047 (about 300 km above the surface) to 20 Earth radii.
  - Left-drag rotates. The rotation rate scales with altitude.
  - The mouse wheel zooms 15% per notch.
  - Double-click centres on the clicked surface point.
  - Home resets the view.
  - **Face Sun** and **Face Moon** animate the camera to look straight down on the sub-solar or sub-lunar point.
- **Picking:** point events are found by projecting them to the screen; the surface point under the mouse (for
  alert areas and double-click) comes from an analytic ray/unit-sphere intersection, cheap enough for hover. A click selects it and fills the right panel. Hovering shows a
  tooltip.
- **Labels:** M5+ earthquakes and named storms get labels from the adapted label overlay, which avoids overlaps.

## 7. User interface

The globe takes up nearly the whole window. Controls you use while watching stay on the main screen as compact
overlays. Only rarely changed settings go in dialog windows.

```
┌──────────────────────────────────────────────────────────────┐
│[≡]                                          ┌──────────────┐ │
│[◉] quakes                                   │ ☀ 12.3°S 45°W│ │  Sun & Moon card
│[🌀] cyclones                                 │ ☾ 18°N 120°E │ │  (folds to two icons)
│[⚠] US alerts           (globe)              │   73% lit    │ │
│[⚡] lightning                                └──────────────┘ │
│[☁] clouds                                   ┌──────────────┐ │
│[⋯] display ▸                     ◀ Sun      │ M6.1 Chile   │ │  details: only while
│                                             │ depth 35 km… │ │  an event is selected
│                                             └──────────[×]─┘ │
├──────────────────────────────────────────────────────────────┤
│ ◀◀ ▶ [LIVE] ═════════════●══════  14:32 UTC  ● ● ● ● ● 41 MB │  time bar + status
└──────────────────────────────────────────────────────────────┘
```

- **Layer strip** (top left, about 48 px wide):
  - Icon toggle buttons for earthquakes, cyclones, US alerts, EONET storms, lightning, clouds, coastlines and
    borders, and tectonic plate boundaries.
  - Each has a tooltip, and hovering shows that layer's legend (for example the age colours, the NWS hazard
    colours or the lightning fade).
  - `≡` expands the strip to show text labels next to the icons.
- **⋯ Display flyout** (opens beside the strip and closes when you click elsewhere):
  - Minimum earthquake magnitude (M2.5 by default; "All" is allowed).
  - Earthquake display window: 24 h or 7 days.
  - Lightning window: 5–60 min (15 by default).
  - US alert types: Warnings (on by default), Watches, Advisories, Statements.
  - Night-shade strength, and the day/night line on or off.
- **Sun & Moon card** (top right): sub-solar and sub-lunar points, the Moon's distance and its illuminated
  percentage, plus **Face Sun** and **Face Moon** buttons. It folds down to two icons.
- **Details panel** (right): slides in when an event is selected and closes with × or Esc. It shows the event's
  fields and a link to the source's page.
- **Bottom bar**, in one row:
  - Time controls: step back, play and pause, speed (1×, 60×, 600× and 3600×), step forward (±10 min and ±1 h
    from a small menu) and **Live**.
  - Time slider: covers the last 7 days, with the last 24 h marked. Cyclone advisories and US alerts exist only
    in those 24 h; earthquakes and EONET storms cover the full 7 days. Moving the slider leaves live mode, and the
    readout then shows "REPLAY".
  - Time readout: UTC, with local time in the tooltip.
  - Status dots, one per source (green: updated recently; amber: retrying; red: failing; grey: not set up). Each
    has a tooltip giving the last update and the last error.
  - The download meter.
- **Dialog windows:**
  - **Settings:**
    - EUMETSAT consumer key and secret, a **Test** button and a link to the registration page.
    - Daily download limit.
    - Kilometres or miles.
    - Saved in `settings.json`. The key and secret are encrypted with DPAPI (`ProtectedData`, CurrentUser
      scope). The ⋯ Display choices and layer toggles are saved there too, so they survive a restart.
  - **About:** the version, every data source with its required credit (USGS, NOAA/NHC/NWS, NASA
    GIBS/EONET/Blue Marble, GDACS, EUMETSAT "Contains EUMETSAT data", Natural Earth, and for plate boundaries
    "Peter Bird (2003), PB2002; GIS conversion by Hugo Ahlenius, Nordpil", ODC-By 1.0) and the open-source licences
    (HelixToolkit, CommunityToolkit, PureHDF).
  - **Welcome** (first run only, can be skipped): explains the layers and the optional EUMETSAT key.
- **MVVM structure:**
  - **Models** live in `EarthWatch3D.Core` and AstroCore, with no UI code: the event records, `EventStore`, the
    feeds, `ILightningSource`, `DownloadMeter` and the astronomy.
  - **View models** live in the App, one per view: `MainViewModel` (which owns the others), `LayersViewModel`,
    `DisplayOptionsViewModel`, `SunMoonViewModel`, `SelectionViewModel`, `TimelineViewModel`, `StatusViewModel`,
    `SettingsViewModel`, `AboutViewModel` and `WelcomeViewModel`. They use CommunityToolkit.Mvvm
    (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
  - **Views** bind to their view models, and code-behind is limited to view-only plumbing.
  - **The 3D scene is the deliberate exception.** WPF 3D handles thousands of bound objects badly, so
    `SceneController` (view side) watches the view models' state (time, layers, display options, selection) and
    the store's snapshots, and updates the Helix scene directly. The same applies to the Sun and Moon arrow
    overlay. The view models never reference WPF 3D types.
  - **Services** reach the view models through constructor injection, so view models can be tested without a
    window. The interfaces today are `IClock` (wall clock and live time), `ISettingsStore` (settings.json),
    `IAppLog` (the log) and `IEventFeed` (each data source). The event store is the concrete, thread-safe
    `EventStore` class and the NWS zone outlines the concrete `ZoneCache`; both go to `SceneController` through its
    constructor. `ILightningSource`, `IDialogService` and `DownloadMeter` arrive with the lightning, settings and
    download-meter work.

## 8. Packaging and signing

- `dotnet publish` produces a self-contained `win-x64` build. `MakeAppx.exe pack` (Windows SDK) packages it with
  `packaging/AppxManifest.xml` into an MSIX. This avoids a Visual Studio-only `.wapproj` and builds from the
  command line. Minimum OS: Windows 10 version 1809.
- **Signing:** `signtool sign` with the Azure Trusted Signing dlib (`Azure.CodeSigning.Dlib.dll`) and a
  `metadata.json` that names the account and certificate profile.
  - `metadata.json` is local only and git-ignored. The repo has `metadata.example.json`.
  - Sign-in is through the Azure CLI or the developer's account. No secrets go in the repo.
  - The manifest's Publisher must exactly match the certificate subject. It is set from the certificate profile
    during the first packaging task.
- **Updates:** each release publishes the `.msix` and an `.appinstaller` file. The `.appinstaller` points to a
  stable URL (the GitHub "latest release" download URL) and checks for updates on launch. Nothing is published
  until the user asks.
- `tools/build-release.ps1` runs publish, package, sign and appinstaller generation in one go.

## 9. Future lightning collector (seam only)

```csharp
interface ILightningSource
{
    LightningCoverage Coverage { get; }                    // which sensors are active, newest flash age
    IReadOnlyList<LightningFlash> GetFlashes(DateTime fromUtc, DateTime toUtc);
    bool SupportsHistory { get; }                          // false for LiveLightningSource
    event EventHandler? FlashesChanged;
}
```

- Today: `LiveLightningSource`, with `SupportsHistory = false`. The UI hides lightning in replay **because** of
  this flag, not because of a hard-coded rule.
- **Later:**
  - A Windows Service (`EarthWatch3D.Collector`) reuses the GLM and MTG readers from EarthWatch3D.Core and
    writes flashes to a SQLite file in `%ProgramData%\EarthWatch3D\lightning.db`.
  - A `CollectorLightningSource` reads that file, with `SupportsHistory = true`.
  - The app uses it when the database exists and is fresh. Lightning replay then works with no other UI change.
- **Known cost of MSIX:** installing a service from an MSIX needs the restricted `packagedServices` capability,
  an administrator install and Windows 10 version 2004 or later. The other option is a separate service
  installer. This gets decided when the collector is built.

## 10. Testing

- **AstroCore.Tests:**
  - All existing SolarSystem3D accuracy tests, moved across.
  - GMST against Meeus example 12.a.
  - Sub-solar point against JPL Horizons (apparent RA/Dec of the Sun from the geocentre, combined with GMST) at
    the two solstices, an equinox and two other dates. Tolerance 0.05°.
  - Sub-lunar point checked the same way. Tolerance 0.05°.
  - Terminator: points 90° from the sub-solar point have h = 0° ± 0.01°.
  - Precession against Meeus example 21.b.
- **EarthWatch3D.Tests** (run against real responses captured once into `tests/Samples` by
  `tools/capture-samples.ps1`):
  - **Feed readers:**
    - Polygons that cross the date line.
    - NWS alerts that have only zone references.
    - Earthquake revision and deletion.
    - GDACS–NHC merging of the same storm.
    - Empty and malformed responses (skipped and logged, never thrown).
  - **Lightning readers:** one real GLM file and one real MTG file. The flash count and the first 10 flashes
    must match values read independently with Python `netCDF4` (`tools/dump_flashes.py`).
  - **Overlap rules:** synthetic flashes on both sides of each boundary are kept exactly once.
  - **Replay:** visibility and fade at chosen times for each event type. Lightning is hidden when
    `SupportsHistory` is false and the time is not live.
  - **Store:** cache round-trip. A corrupt cache file is discarded.
  - **DownloadMeter:** resets at midnight. The daily limit pauses lightning only.
  - **View models** (fake `IClock`, `IEventStore`, `ILightningSource`, `ISettingsStore` and `IDialogService`;
    no window):
    - `TimelineViewModel`:
      - Live follows the clock.
      - Moving the slider leaves live mode and shows "REPLAY".
      - Play advances time at the chosen speed.
      - The slider is clamped to the 7-day range.
    - `LayersViewModel` and `DisplayOptionsViewModel`: toggles and filters change what the view model reports as
      visible, and they are saved to and restored from settings.
    - `SelectionViewModel`: each event type is formatted correctly in km and in miles. Selection clears when the
      event leaves the visible set.
    - `SunMoonViewModel`: the shown coordinates and illuminated percentage match AstroCore for a fixed time.
    - `StatusViewModel`: each feed's state maps to the right dot colour and tooltip.
    - `SettingsViewModel`: empty or invalid keys are rejected. A key that passes **Test** is stored encrypted
      (the DPAPI call is behind an interface).
  - **Network tests** that call the live services are tagged `[Trait("Category","Network")]`, so they can be
    skipped with `--filter Category!=Network`.
- **Performance** (measured on the developer machine and recorded in `docs/perf.md`):
  - Globe image rebuild under 300 ms.
  - At least 30 fps while rotating with 25,000 lightning points.
- **Manual checklist** (`docs/manual-test.md`), run against the real app with screenshots:
  - Each layer on and off.
  - Replay, including lightning hiding itself.
  - Sun and Moon arrows off screen in each direction.
  - Face Sun and Face Moon.
  - EUMETSAT key Test with a valid key, an invalid key and no key.
  - Daily limit.
  - MSIX install, update and uninstall.

## 11. Error handling

- A feed failure affects only that feed: its status dot turns amber, then red, and retries back off (§5).
  Cached data stays on screen.
- Malformed records are skipped and logged. Logs go to `%LOCALAPPDATA%\EarthWatch3D\logs`, a rolling daily file
  kept for 7 days.
- If the EUMETSAT key is missing or invalid, the MTG dot is grey ("not set up") or red ("key rejected"), with a
  link to Settings. GLM continues on its own.
- If the globe image cannot be composed (low memory, for example), the app falls back to 4096×2048, and then to
  the plain Blue Marble with no shading.
- Unhandled exceptions are caught at `DispatcherUnhandledException` and `TaskScheduler.UnobservedTaskException`,
  logged and shown once as a message, and the app keeps running where possible.

## 12. Things to verify first (plan task 1, a spike)

Each of these is checked against the live service and the result recorded in `docs/data-sources.md` before the
code that depends on it is written:

1. GLM: object listing format, typical file size (to replace the 2–3 GB/day estimate), and that PureHDF reads
   `flash_*` variables including deflate-compressed data.
2. MTG LI-2-LFL: token flow, search query, product file layout, size and real latency.
3. GIBS: which cloud layer to use and how often it updates.
4. GDACS geometry endpoint, NHC `CurrentStorms.json` structure and KMZ links, NWS alert history and paging,
   and the EONET query.

If a check fails, the affected part of this spec is updated and shown to the user before work continues.
