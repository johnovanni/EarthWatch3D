# EarthWatch3D data sources (verified)

Spike for plan 1, task 1. Every source in spec section 12 was fetched live on **2026-11-11 (about 19:15 to 20:30 UTC)**.
Plans 2 and 3 take their endpoints and field names from this file. Sample responses are in `tests/Samples/`
and can be re-captured with `tools/capture-samples.ps1` (no credentials needed).

All requests used the User-Agent `EarthWatch3D/0.1.0 (+https://github.com/johnovanni/EarthWatch3D)`.
Latency figures are one measurement from one home connection, not a benchmark.

Summary:

| Source | Status | One-line result |
| --- | --- | --- |
| USGS | verified | As in the spec. Week feed 1.3 MB, hour feed about 4 KB. |
| GDACS | verified | Geometry endpoint found: the `url.geometry` link inside each event. |
| NHC | verified | `CurrentStorms.json` has direct KMZ links. KMZ longitudes are **not** wrapped to +-180. |
| NWS | verified | History and paging work. 94 to 98 percent of alerts have no geometry. 24 h history is about 1,100 alerts over 4 pages (roughly 5 MB, estimated). |
| EONET | verified | Point tracks only (JTWC sourced), `closed` is null while open. |
| GOES GLM | verified | 20 s files. G19 about 72 MB/h, G18 about 30 MB/h. `flash_lat` and `flash_lon` are plain floats. |
| PureHDF | verified | 2.2.0 reads the real file with no extra filter package. |
| EUMETSAT MTG LI | **deferred to plan 3** | No credentials during the spike. |
| GIBS clouds | verified | No global geostationary IR layer exists. Chosen: daily VIIRS NOAA-21 true colour. |
| Static assets | verified | NASA Blue Marble URL in the brief is dead. Replacement URL below. |

---

## 1. USGS earthquakes (spec 5.1)

- URLs (GET, no parameters, no auth):
  - `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson`
  - `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson`
- Size and latency: week = 1,314,164 bytes (1,850 features, 0.27 s). Hour = 5,214 bytes (7 features, 0.15 s;
  a later capture was 3,826 bytes). - Cadence: USGS regenerates the feeds every minute (`metadata.generated` is in epoch ms; both feeds had `generated` equal to within 1 s).
- Structure: `FeatureCollection` with `metadata` (`generated`, `url`, `title`, `status`, `api`, `count`), `features`, `bbox`.
- Fields confirmed on each feature:
  - `id` (string, e.g. `us7000txmx`), top level, next to `type`, `properties`, `geometry`.
  - `properties.mag` (number, can be null for some events), `properties.place` (string), `properties.time` and
    `properties.updated` (both **epoch milliseconds**), `properties.url` (event page), `properties.detail`
    (per-event GeoJSON), plus `status` (`automatic` / `reviewed`), `tsunami`, `sig`, `magType`, `type`, `title`, `alert`, `felt`, `net`.
  - `geometry.coordinates` = `[lon, lat, depth_km]`.
- Surprises: none. `properties.type` is not always `earthquake` (quarry blasts and explosions appear); filter on it if wanted.
- Sample: `tests/Samples/usgs_all_week.geojson` (full, untrimmed, 1.3 MB) and `usgs_all_hour.geojson`.

## 2. GDACS tropical cyclones (spec 5.2)

- Event list (exact URL used, dates are `yyyy-MM-dd`):
  `https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH?eventlist=TC&alertlevel=green;orange;red&fromdate=2026-11-04&todate=2026-11-11`
  (the `;` is sent literally and works). 5,526 bytes, 4 events, 0.9 s.
- Event list structure: GeoJSON `FeatureCollection`, one `Point` feature per **event** (the current episode). `properties`:
  `eventtype` (`TC`), `eventid` (int), `episodeid` (int), `eventname` (e.g. `PENA-26`), `name`, `alertlevel` /
  `episodealertlevel` (`Green`, `Orange`, `Red`), `alertscore`, `iscurrent` (**string** `"true"`/`"false"`),
  `istemporary`, `fromdate`, `todate`, `datemodified` (ISO, no zone, UTC), `source` (NOAA, JTWC, RSMC),
  `severitydata` {`severity`, `severitytext`, `severityunit` (km/h)}, `country`, `iso3`, and
  `url` = {`geometry`, `report`, `details`}.
- **Geometry endpoint (the answer to "verify")**: the link is in each event, `properties.url.geometry`:
  `https://www.gdacs.org/gdacsapi/api/polygons/getgeometry?eventtype=TC&eventid=1001348&episodeid=20`
  Returns GeoJSON `FeatureCollection`, 224 KB for a 5-day storm (288 KB for another), 1.7 to 1.8 s.
  Swagger (`https://www.gdacs.org/gdacsapi/swagger/v1/swagger.json`, 152 KB; the UI is
  `https://www.gdacs.org/gdacsapi/swagger/index.html`) lists it as `GET /api/Polygons/getgeometry`.
  Other useful paths from Swagger: `/api/Events/geteventdata`, `/api/Events/getepisodedata`, `/api/Events/geteventlist/latest`.
- **How geometry roles are labelled.** Every feature repeats the event properties and adds `Class` and `polygonlabel`
  (and `polygondate`, `featuretype` on some). The role is in `properties.Class`:

  | `Class` | geometry | `polygonlabel` | Meaning |
  | --- | --- | --- | --- |
  | `Point_Centroid` | Point | `Centroid` | Current position |
  | `Line_Line_<n>` | LineString | storm type, e.g. `TD`, `TS` | One track segment per step; `n` is the step index (25 in the example) |
  | `Point_Polygon_Point_<n>` | Polygon | `dd/MM HH:mm UTC` | Small circle (radii polygon) around the track point `n`; has `featuretype` = `PointRadii`, `key` |
  | `Poly_Cones` | MultiPolygon | `Uncertainty Cones` | Forecast uncertainty cone |
  | `Poly_Green` / `Poly_Orange` / `Poly_Red` | Polygon | `60 km/h`, `90 km/h`, `120 km/h`... for the current wind areas, or `dd/MM HH:mm` for forecast times | Wind-speed bands (colour = threshold). `polygondate` = time of the band |

  Rule for the reader: group by `Class` prefix, not by `polygonlabel` text. Wind bands with `polygonlabel` ending in
  `km/h` are the current ones; those with a time label are forecast steps.
- Surprises:
  - The list's `iscurrent` is a string. Closed storms (`iscurrent: "false"`) are still returned for the date window.
  - Event `source` is the agency; NOAA events here are the same storms as NHC (Pena in the sample is NHC `cp042026`).
  - Longitudes are cut at +-180 (the cone is a MultiPolygon, min/max = -180/180). NHC KMZ files are not cut. See section 3.
- Samples: `tests/Samples/gdacs_events_tc.json`, `gdacs_geometry.json` (event 1001348, episode 20).

## 3. NHC tropical cyclones (spec 5.3)

- URL: `https://www.nhc.noaa.gov/CurrentStorms.json`. 4,626 bytes, 0.27 s. A storm (Pena, `cp042026`) was active, so the live
  structure was recorded (no archive needed).
- Structure: `{ "activeStorms": [ ... ] }` (empty array when nothing is active). Each storm:
  - Identity: `id` (`cp042026` = basin + number + year), `binNumber`, `name` (capitalised, `Pena`), `classification` (`TS`, `HU`, `TD`...),
    `intensity` (knots, **string**), `pressure` (mb, string), `latitude`/`longitude` (strings like `9.9N`, `167.7W`),
    `latitudeNumeric`/`longitudeNumeric` (numbers; west is negative), `movementDir`, `movementSpeed`, `lastUpdate` (ISO, `Z`).
  - Product blocks, each `{ advNum, issuance, fileUpdateTime, url | zipFile | kmzFile }`: `publicAdvisory`, `forecastAdvisory`,
    `windSpeedProbabilities`, `forecastDiscussion`, `forecastGraphics`, **`forecastTrack`**, **`trackCone`**,
    `initialWindExtent`, `forecastWindRadiiGIS`, `bestTrackGIS`, `earliestArrivalTimeTSWindsGIS`, `mostLikelyTimeTSWindsGIS`,
    `windSpeedProbabilitiesGIS`; and `windWatchesWarnings`, `stormSurgeWatchWarningGIS`, `potentialStormSurgeFloodingGIS`, `peakSurgeKML`
    (null when absent).
- Links to use:
  - Track: `activeStorms[i].forecastTrack.kmzFile` = `https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_TRACK.kmz` (7.9 KB).
  - Cone: `activeStorms[i].trackCone.kmzFile` = `.../CP042026_020adv_CONE.kmz` (11.7 KB).
  - Current wind radii (optional): `initialWindExtent.kmzFile` = `.../CP042026_initialradii_020adv.kmz` (3.1 KB).
  - The `zipFile` entries are shapefile archives; ignore them.
  - Change detection: compare `advNum` / `fileUpdateTime` of `forecastTrack` and `trackCone` with the last seen values.
- KMZ layout (zip containing one `.kml` plus PNG icons):
  - Track: 8 placemarks. `LineString` (`#72_line`, `#120_line` = 72 h and 120 h segments) and `Point` (`#initial_point`, `#s_point`...). Coordinates `lon,lat,0`.
    `ExtendedData/Data[@name]` on the line carries `stormType`, `basin`, `fcstpd`, `storm`, `atcfid`, `advisoryNum`, `maxWindKnots`, `maxWindMPH`, `maxGustKnots`, `stormMovement`, `minimumPressure`, `pubAdvTime`, `TCInitLocation`.
  - Cone: one `Polygon` placemark (`#120`), the outer ring only, with `stormType`, `advisoryDate`, `fcstpd`... in `ExtendedData`.
  - Radii: `Folder` named `wind_field`, one `Polygon` per threshold (`<name>34</name>`; 50 and 64 kt when present).
- **Surprise: longitudes are not wrapped.** For a storm near the date line the cone spans lon 177.28 to 192.56 and the track
  runs to -181.6 (the cone is shifted to above 180 while the point is -167.7). The KML reader must normalise longitudes (and
  split or unwrap polygons that cross +-180) before drawing.
- Merge with GDACS (spec): GDACS had the same storm as event 1001348 (name `PENA-26`, source NOAA, position 9.9 N 167.7 W =
  NHC lat/lon). Name matching must tolerate the GDACS `-26` suffix (NHC `Pena`, GDACS `PENA-26`).
- Samples: `nhc_CurrentStorms.json`, `nhc_track.kmz`, `nhc_cone.kmz`.

## 4. NWS alerts (spec 5.4)

- Active: `https://api.weather.gov/alerts/active`. 1,813,492 bytes (427 alerts, 0.87 s). **No `pagination` object** (the whole set is one response).
  Top level: `@context`, `type`, `features`, `title`, `updated`. A User-Agent is required (done).
- **History works**: `https://api.weather.gov/alerts?start=2026-11-10T20:14:26Z` (ISO UTC) returned 200. First page = 500 alerts, 2.27 MB, 1.7 s.
  The default page size is already 500; `&limit=500` gives the same page. Paging: `pagination.next` is a full URL with a `cursor`
  (adds `&cursor=...`); follow it until `pagination` is absent. 24 h of history = **1,114 alerts in 4 pages** (the first has 500; the rest were walked and counted but their sizes were not recorded). The first page is 2.27 MB, so the total is
  roughly 5 MB (estimate). Plan for several MB at startup. Filtering with
  `event=`/`status=actual` was **not** tested.
- Feature fields: `id` (a URN), `properties.event` (e.g. `Flood Warning`), `severity`, `certainty`, `urgency`, `status`, `messageType`,
  `sent`, `effective`, `onset`, `expires`, `ends`, `headline`, `description`, `areaDesc`, `geocode` {`SAME`, `UGC`},
  `affectedZones` (array of zone URLs), `senderName`, `references`.
- **Geometry**: `geometry` is `null` for most alerts. Active: **418 of 427 (97.9 percent)** are null; 24 h history: **1,053 of 1,114 (94.5 percent)**
  (the sample day had hundreds of marine advisories: Small Craft Advisory 220, Gale Warning 100). The zone fallback is therefore the normal case, not the exception.
- Zones: `https://api.weather.gov/zones/forecast/LMZ323` returns a `Feature` with `geometry` = `Polygon` (marine zone: 120 KB, geometry part 30 KB, 0.37 s;
  land zone `MIZ031`: 20 KB). Expect `MultiPolygon` for some zones. `properties` has `id`, `name`, `state`, `forecastOffice`, `effectiveDate`.
  An alert lists several zones (5 in the example), so one alert can mean several downloads. Cache them (spec `cache/zones/`).
- Sample: `nws_active.json` (1.8 MB, untrimmed), `nws_zone.json` (from `affectedZones[0]` of the first geometry-less alert).

## 5. NASA EONET (spec 5.5)

- URL: `https://eonet.gsfc.nasa.gov/api/v3/events?category=severeStorms&status=all&days=7`. 18,984 bytes, 4 events, 2.9 to 4.3 s (slow).
- Structure: `{ title, description, link, events: [...] }`. Event: `id` (`EONET_26159`), `title`, `description` (often null), `link`,
  **`closed`** (`null` while open, ISO `...Z` string when closed), `categories`, `sources` [{`id` (e.g. `JTWC`), `url`}],
  `geometry` (array).
- Geometry list: each item is `{ "magnitudeValue": 35.0, "magnitudeUnit": "kts", "date": "2026-11-07T18:00:00Z", "type": "Point", "coordinates": [lon, lat] }`.
  Points only, one per fix (5, 19, 20 and 51 fixes in the sample). Track = the points sorted by `date`. No cones or polygons.
- Surprise: all four events are sourced from JTWC (`sources[].id`), including Pena, which is also an NHC storm. EONET adds little over GDACS, and the same storm appears in GDACS, NHC and EONET under different names/ids.
- Sample: `eonet_storms.json`.

## 6. GOES GLM lightning (spec 5.7)

### Listing

- URL: `https://noaa-goes19.s3.amazonaws.com/?list-type=2&prefix=GLM-L2-LCFA/2026/315/19/` (year / **day of year, 3 digits** / hour, UTC). Same for `noaa-goes18`.
  Anonymous access works (S3 ListObjectsV2 XML). 0.35 to 0.45 s.
- Response: `ListBucketResult` with `KeyCount` (180), `MaxKeys` (1000), `IsTruncated` (false), and `Contents` with `Key`, `LastModified`
  (`2026-11-11T19:00:39.000Z`), `ETag`, `Size`. One hour is 180 objects, so a single listing call is enough (no continuation token needed).
- Key pattern: `GLM-L2-LCFA/2026/315/19/OR_GLM-L2-LCFA_G19_s20263151900000_e20263151900200_c20263151900219.nc`
  = `s` start, `e` end, `c` created; each is `yyyydddHHMMSSt` (t = tenth of a second). Each file covers **20 s** exactly.
- Polling without relisting the hour: add `&start-after=<last key>`. A test with a key 20 s from the end of the hour returned `KeyCount` 4. Roll the
  prefix over at each hour (and at midnight, day-of-year changes).
- Latency: `LastModified` minus the end time `e` was **median 10 s (min 6, max 55)** for G19 and median 9 s (5 to 27) for G18 over one hour.
  So a flash is available about 10 to 40 s after it occurred, and a 20 s poll is right.

### Sizes (measured)

| Satellite | Files in sampled hour (2026-11-11 19 UTC) | Mean file | Hour total |
| --- | --- | --- | --- |
| GOES-19 (East) | 180 | 397,588 bytes (3 downloaded: 392,919 / 388,890 / 375,453, mean 385,754) | 71.6 MB |
| GOES-18 (West) | 180 | 167,688 bytes (3 downloaded: 164,883 / 168,512 / 167,819, mean 167,071) | 30.2 MB |

- **Data use per hour per satellite = measured mean x 180 files/hour: GOES-19 = 397,588 x 180 = 71.6 MB/h (1.72 GB/day); GOES-18 = 167,688 x 180 = 30.2 MB/h (0.72 GB/day).**
  Both satellites together = about 102 MB/h (2.4 GB/day), consistent with the spec's 2 to 3 GB/day estimate.
- Variation (other hours of 2026-11-10): G19 = 62.4, 51.3, 38.8, 68.7 MB/h at 00, 06, 12, 21 UTC; G18 = 31.2, 31.0, 30.9, 30.4 MB/h.
  G19 is storm-dependent (active land convection), G18 is nearly constant.
- A G18 file with only 4 to 12 flashes is still about 165 KB, so there is a large fixed part per file (event/group/field-of-view variables). A G19 file with 690 to 757 flashes is about 390 KB.
  Size is not proportional to flash count. Typical flashes per 20 s file: G19 690 to 757 here, G18 4 to 12 here.

### File layout (NetCDF-4 / HDF5, read with netCDF4 and PureHDF)

- Dimensions: `number_of_flashes`, `number_of_groups`, `number_of_events`, `number_of_time_bounds`, plus field-of-view and wavelength bounds.
- All variables use **shuffle + deflate (zlib level 5)**.
- Variables the app needs (flash level):

  | Variable | Stored type | Packing | Use |
  | --- | --- | --- | --- |
  | `flash_lat` | float32 | **none** (plain degrees_north) | latitude |
  | `flash_lon` | float32 | **none** (plain degrees_east) | longitude |
  | `flash_energy` | int16, `_Unsigned = "true"` (read as uint16) | `scale_factor` 9.99996e-16, `add_offset` 2.8515e-16, `_FillValue` -1 (= 65535 unsigned), `valid_range` [0, -6] | radiant energy, joules |
  | `flash_time_offset_of_first_event` | int16, `_Unsigned = "true"` | `scale_factor` 0.0003814756, `add_offset` -5.0; units `seconds since <file start>` | time of flash, seconds |
  | `product_time` | float64 scalar | none; `seconds since 2000-01-01 12:00:00` | file start time |
  | `product_time_bounds` | float64 [2] | none | file start and end |

  Flash time = `product_time` + (`raw` x `scale_factor` + `add_offset`). Global attributes: `time_coverage_start/_end`, `platform_ID` (`G19`), `dataset_name`.
- **Surprise 1:** `flash_lat` and `flash_lon` are **not** packed integers (the spec says they might be). Only `flash_energy` and `flash_time_offset_of_first_event` need scale/offset and unsigned handling.
- **Surprise 2:** flash times range from about **1.15 s before** the file start to 19.4 s after it (sample file: -1.148 to +19.358 s). A flash can belong to the file of the previous 20 s, so de-duplicate by time + position (or by `flash_id`, which is per-file), not by file window.
- Sample: `tests/Samples/glm_g19_sample.nc` (G19, 19:00:00, 757 flashes, 392,919 bytes), `glm_g19_sample.csv` (`time_utc,lat,lon,energy`), and `glm_g18_sample.nc` (G18, 164,883 bytes).
- Tool: `tools/dump_flashes.py <in.nc> <out.csv>` prints the flash count (757 for the sample) and writes the CSV. Needs `pip install netCDF4`.

## 7. PureHDF check (spec 5.7, step 7)

- Throwaway console project (outside the repo), `net10.0`, SDK 10.0.401, package **PureHDF 2.2.0** (the latest 2.x; no other packages).
- **No filter package was needed.** Shuffle and deflate were read with the built-in filters. (The optional `PureHDF.Filters.Deflate.*` packages
  are only for speed.)
- API used: `H5File.OpenRead(path)`, `file.Dataset("flash_lat").Read<float[]>()`, `Read<ushort[]>()` for `flash_energy` and
  `flash_time_offset_of_first_event` (raw uint16, the `_Unsigned` attribute is not applied automatically, so the app reads `ushort` and applies scale/offset itself),
  `Read<double>()` for the scalar `product_time`, and `dataset.Attribute("scale_factor").Read<float[]>()[0]` for attributes.
- Result: n = 757 flashes read. The first 10 rows (time, lat, lon, energy) match `glm_g19_sample.csv` (made by netCDF4) exactly at the printed precision
  (5 decimals, 7 significant digits). The one difference seen: the energy `3.8285e-14` printed as `3.828500e-14` by PureHDF (double arithmetic) and `3.828499e-14` by Python
  (float32 arithmetic), which is rounding only. Scale and offset are float32 attributes; the app should compute in double.
- Conclusion: spec 5.7 holds. PureHDF can read the GLM files. No change to the design.

## 8. EUMETSAT MTG LI (spec 5.7): deferred to plan 3

**Deferred to Plan 3 (needs the user's EUMETSAT key).** Not checked in this spike because no consumer key and secret were available. Nothing below is verified.

Known from the spec:

- Collection `EO:EUM:DAT:0691` (MTG LI-2-LFL, lightning flashes).
- Token endpoint `https://api.eumetsat.int/token` (OAuth client credentials from the user's consumer key and secret).
- Search the Data Store for products newer than the last one read, then download each product's NetCDF file.
- Credentials go in the git-ignored file `tools/eumetsat.local.json` (never in chat or in the repo).

Open questions for plan 3 (do these first):

1. Token flow: exact request (basic auth with key:secret, `grant_type=client_credentials`), token lifetime.
2. Search query: the Data Store search/browse endpoint and its parameters (`dtstart`, `dtend`, `sort`, paging), how a "newer than" query is written.
3. Product layout: is one product one NetCDF file, a zip of several chunks, or many files per slot; group and variable names for flash latitude, longitude, radiance/energy and time; whether values are packed.
4. Size per product and cadence (spec assumption: a file every 10 s? unknown), data per hour.
5. **Latency** (product sensing end time versus when it appears in the search). If well over a few minutes, the legend must show the age of the newest MTG flash and nothing may be called "live".
6. Whether PureHDF reads it (groups, string variables, deflate).
7. Coverage edge and the overlap rule with GOES-East at 37.6 W.

`tools/dump_flashes.py` already has an `--mtg` option with an `--mtg-map` JSON variable map (defaults: `latitude`, `longitude`, `radiance`, `flash_time`,
optional `group`). **It is untested**; the default names are guesses to be corrected in plan 3.

## 9. GIBS clouds (spec 5.6)

- GetCapabilities: `https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&REQUEST=GetCapabilities&VERSION=1.3.0` = 2,577,571 bytes, 1.3 s.
- Geostationary IR layers (all with a 10 minute time step, `PT10M`, bounding box -180..180 / -90..90):

  | Layer | Newest time at 20:15 UTC | Coverage seen in the 4096x2048 image |
  | --- | --- | --- |
  | `GOES-East_ABI_Band13_Clean_Infrared` | 19:30 | lon -156.4 to +6.2 |
  | `GOES-West_ABI_Band13_Clean_Infrared` | 19:40 (blank at that time; 19:30 worked) | wraps across the date line |
  | `Himawari_AHI_Band13_Clean_Infrared` | 19:30 | wraps across the date line |

  Also present: `*_ABI_Air_Mass`, `*_ABI_GeoColor` (visible by day), `*_Dust`, `*_FireTemp`, `*_Band2_Red_Visible_1km`, `IMERG_Precipitation_Rate_30min`.
  There is **no Meteosat or other layer for Europe, Africa and the western Indian Ocean**, and no merged global IR layer.
- Measured: the union of the three IR images covers 71.8 percent of the map (81.2 percent area-weighted). The hole is lon about +6 to +60 at the equator, +3 to +67 at 45 degrees,
  and -4 to +76 at 64 degrees. The disks overlap and each has soft, banded edges. The IR layer is a **colour-enhanced palette** (rainbow over grey, alpha steps 0/64/128/191/255),
  not raw brightness, so "white where cold" would need a palette inversion. Each IR image is 3.6 to 4.7 MB (PNG with transparency), 3.3 to 4.0 s.
- Daily true-colour layers (`P1D`, global): `VIIRS_NOAA21_CorrectedReflectance_TrueColor` (to 2026-11-11), `VIIRS_NOAA20_...` (to 2026-11-11), `MODIS_Terra_...` and `MODIS_Aqua_...`.
  `VIIRS_SNPP_CorrectedReflectance_TrueColor` **stops at 2026-11-02** (stale). The `_Granule` variants are 6 minute pieces, not global.
- **Chosen layer: `VIIRS_NOAA21_CorrectedReflectance_TrueColor`, `TIME` = yesterday (UTC).** Why: it is global in one image, daily, current, and a complete day has
  only 1.0 percent black pixels between 70 N and 70 S (MODIS Terra: 4.8 percent). Compare today's date: at 20:15 UTC the image for `TIME=2026-11-11` had 43.7 percent black
  (swaths not yet done), so request the **previous UTC day** (or fall back to the previous day when the current one is partial). North of about 78 N is black in polar night in November.
  Faint vertical grey streaks between swaths remain (the thin gaps the spec expected).
- GetMap URL template (3.0 to 5.5 s, JPEG 2.0 MB, PNG would be 10.9 MB so use JPEG):
  `https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&CRS=EPSG:4326&BBOX=-90,-180,90,180&WIDTH=4096&HEIGHT=2048&STYLES=&LAYERS=VIIRS_NOAA21_CorrectedReflectance_TrueColor&FORMAT=image/jpeg&TIME=<yyyy-MM-dd>`
  Note WMS 1.3.0 with `CRS=EPSG:4326` uses **lat,lon axis order** in `BBOX` (`-90,-180,90,180`). Without `TIME` the server returns the latest available time.
- Refresh: once a day is enough. Cloud alpha comes from brightness (as in the spec fallback); keep the brightness-keyed alpha with a floor so dark ocean stays transparent.
- Sample: `tests/Samples/gibs_viirs_noaa21_truecolor_4096.jpg` (2.0 MB, `TIME=2026-11-10`).

## 10. Static assets

- **NASA Blue Marble Next Generation with Topography and Bathymetry, 21600x10800 JPEG.**
  - The brief's host does not work: `https://eoimages.gsfc.nasa.gov/images/imagerecords/73000/73909/world.topo.bathy.200407.3x21600x10800.jpg` and even `https://eoimages.gsfc.nasa.gov/` gave **no HTTP response (curl timeout, exit 28)**.
  - Working URL, taken from the NASA page `https://visibleearth.nasa.gov/images/73909/december-blue-marble-next-generation-w-topography-and-bathymetry`
    (it redirects to a NASA Science page titled "Blue Marble: Next Generation with Topography and Bathymetry"):
    `https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/july/world.topo.bathy.200407.3x21600x10800.jpg`
    -> HTTP 200, `image/jpeg`, **27,201,049 bytes (27.2 MB)**. A 5400x2700 version is at the same path with `3x5400x2700.jpg`.
  - The file is the **July** (200407) month, despite the page's URL saying "december". The folder name is `july`.
  - Licence: NASA imagery is generally not copyrighted (NASA Media Usage Guidelines); credit "NASA Earth Observatory (Blue Marble Next Generation, R. Stockli)". Not downloaded in full in this spike.
- **Natural Earth** (public domain, no attribution required, though it is polite to credit):
  - `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_coastline.geojson` -> 200, 1,640,858 bytes.
  - `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_boundary_lines_land.geojson` -> 200, 760,189 bytes.
  - Served as `text/plain`. Pin a commit hash or tag in the final download URL if reproducibility matters.

## Spec impact

Differences from spec section 5. The spec has **not** been edited; the controller decides.

1. **5.7 GLM variables.** `flash_lat` and `flash_lon` are plain float32 (no `scale_factor`/`add_offset`). Only `flash_energy` and `flash_time_offset_of_first_event` are packed (uint16 via `_Unsigned`, with scale and offset). The reader must read those two as `ushort`.
2. **5.7 GLM time.** Flash times can fall up to about 1.15 s before the file's start time; a file is not a clean 20 s window. Dedup by time and position.
3. **5.7 data volume (spec 12.1).** Measured GOES-19 = 71.6 MB/h and GOES-18 = 30.2 MB/h (G19 ranged 39 to 72 MB/h over five sampled hours): about 2.4 GB/day for both, consistent with the 2 to 3 GB/day estimate. With "only download what is in view" each satellite alone is 0.7 to 1.7 GB/day.
4. **5.7 PureHDF.** Works on the real file with built-in shuffle and deflate, no filter package. No change needed. The package version to pin is 2.2.0.
5. **5.6 clouds.** The preferred option (a geostationary IR composite) is not available as a global layer: GIBS only has separate GOES-East, GOES-West and Himawari layers, with a hole over Europe, Africa and the western Indian Ocean (about 19 percent of the globe by area), and they are colour palettes, not brightness. The fallback is used: `VIIRS_NOAA21_CorrectedReflectance_TrueColor` daily, requested for the **previous UTC day** (the current day is partial). Spec 5.6 should say this, and that the VIIRS SNPP layer is stale.
6. **5.2 GDACS geometry endpoint (the verify item).** It is the event's `properties.url.geometry` link (`/gdacsapi/api/polygons/getgeometry?eventtype=TC&eventid=..&episodeid=..`). Roles are in `properties.Class` (`Point_Centroid`, `Line_Line_<n>`, `Point_Polygon_Point_<n>`, `Poly_Cones`, `Poly_Green|Orange|Red`). `iscurrent` is a string.
7. **5.3 NHC.** `CurrentStorms.json` structure is as hoped, with direct `forecastTrack.kmzFile` and `trackCone.kmzFile` links. But NHC KMZ longitudes are **not wrapped to +-180** (cone reached 192.6, track -181.6), so the KML reader needs longitude normalisation. Name matching with GDACS must tolerate the `-26` year suffix (`Pena` vs `PENA-26`).
8. **5.4 NWS.** History and paging work as assumed (`pagination.next` cursor URLs, 500 per page). But 94 to 98 percent of alerts have no geometry, so the zone path is the main path, and the 24 h startup history is about 1,100 alerts in 4 pages (roughly 5 MB, estimated) and could be more on a stormy day. The `/alerts/active` response has no paging.
9. **5.1 USGS.** Matches the spec. (Note only: `time` and `updated` are epoch milliseconds.)
10. **5.5 EONET.** Matches the spec (point fixes in a `geometry` list, `closed` null while open). Worth noting it is slow (3 to 4 s) and all events here are JTWC-sourced.
11. **MTG (5.7, 12.2).** Not verified; deferred to plan 3. Latency, layout and size remain open.
12. **Blue Marble asset.** The `eoimages.gsfc.nasa.gov` URL in the plan does not respond; use `assets.science.nasa.gov/.../july/world.topo.bathy.200407.3x21600x10800.jpg` (27.2 MB). The image is the July month.
