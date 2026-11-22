# Storm replay history (EW-003) — Design

**Date:** 2026-11-22
**Status:** Approved by the owner 2026-11-22
**Issue:** EW-003 in `private/EarthWatch3D-issues.xlsx`: "Tropical storms don't move during time-bar playback"
(Severity Major, Priority High). Found by the owner on 2026-11-22.

## The bug

In replay, each tropical cyclone stays at its newest known position, and it disappears for times before that
position's advisory. The cause is that replay can only use advisories the app downloaded while it was running.
`EventVisibility.At` shows the newest stored advisory at or before the display time. NHC and GDACS only give the
current advisory, so a short run stores one advisory per storm. The past-track lines have no times on their points.
This has been true since Plan 2; the GlobeCore switch-over did not cause it.

## The fix (approach approved by the owner on 2026-11-22)

Give every storm a timed history and place the storm on it for the display time.

### Timed history from both sources

- **GDACS.** The geometry file the app already downloads has a `Point_Polygon_Point_N` feature for every track
  point. Each is a small circle with a `polygonlabel` such as `06/11 21:00 UTC` and a `key` in `MMddHHmm` form. The
  reader currently skips them. It will:
  - read each point's time from the label;
  - take the year from the episode's `todate`, choosing the year that puts the time within 183 days of it, so a
    storm crossing New Year works;
  - take the centre from the middle of the circle's bounding box.

  Points later than the advisory time are forecast points, so they are dropped. GDACS gives no wind per point.
  Checked against `tests/Samples/gdacs_geometry.json`: PENA-26 has 25 points, 06/11 21:00 to 14/11 00:00, with the
  advisory at 11/11 15:00.
- **NHC.** `CurrentStorms.json` gives each storm's `bestTrackGIS`. Its `.kmz` twin
  (`https://www.nhc.noaa.gov/gis/best_track/<id>_best_track.kmz`; the `.zip` URL with `.kmz`) has one Placemark
  per fix, with `<atcfdtg>yyyyMMddHH</atcfdtg>`, `<lat>`, `<lon>` and `<intensity>` (knots). It is downloaded
  alongside the track and cone KMZs, only when its `issuance` changes. Checked live on 2026-11-22 for `cp042026`:
  41 fixes, every 6 h.
- **The model.** `CycloneAdvisory` gains `IReadOnlyList<StormFix> History`, using the existing
  `StormFix(TimeUtc, Position, WindKt)` type, holding past fixes only. The event cache `SchemaVersion` goes from 2
  to 3.

### Where a storm is drawn at display time t

For each storm, the known fixes are the union, by time, of every stored advisory's own position and its `History`
fixes. All of these are observed positions; forecast points are never used for replay.

| Display time t | What is drawn |
|---|---|
| At or after the newest stored advisory (including live) | **Exactly as today**: that advisory, with its own past track, forecast, cone and wind areas |
| Between two known fixes, before the newest advisory | The glyph at the great-circle point between the two fixes, by time. The past-track line is the known fixes up to t, plus that point. The forecast, cone and wind areas come from the newest advisory at or before t, if there is one; otherwise none are drawn |
| Before the earliest known fix, or more than 24 h after the newest fix at or before t | Not drawn (the 24 h storm window stays) |

The glyph's category and colour come from the wind at t:
- When both neighbouring fixes have a wind (NHC), the wind is interpolated between them.
- Otherwise the newest advisory's wind at or before t is used.
- When there is neither, which happens for GDACS storms before their first stored advisory, the glyph is drawn in
  a neutral "intensity unknown" grey. The legend gets an entry for it.

**The tooltip and details panel** say what the position is: "Position at 12:00 UTC, between the 09:00 and 15:00 fixes
(NHC best track)", or "(GDACS track)". This way a time between fixes is never shown as an advisory position.

**NHC/GDACS merging** works as now, on the derived positions, so NHC still wins for the same storm.

### Calls made in this design (for the owner to check)

| # | Call | Reason |
|---|---|---|
| S1 | Live and "at or after the newest advisory" stay exactly as today | Keeps EarthWatch3D's 7 snapshot scenes pixel-identical, and the newest advisory is what was published |
| S2 | Great-circle interpolation by time between fixes, never extrapolation | Fixes are 3–6 h apart, and storms move fairly steadily between them. Extrapolating would be a forecast |
| S3 | Grey "intensity unknown" glyph when no wind is known | Showing a later advisory's category at an earlier time would be invented |
| S4 | GDACS points after the advisory time are treated as forecast and dropped | GDACS mixes past and forecast points in one list; the advisory time is the boundary |

## Testing

- **GDACS reader:** from the sample, PENA-26 gets the past fixes up to 11/11 15:00 with correct UTC times, plus a
  New Year case.
- **NHC best-track reader:** a captured sample KMZ goes in `tests/Samples` (`nhc_best_track.kmz`, from the live
  `cp042026` file).
- **`EventVisibility`:** the three table rows; the interpolated position at a midpoint and across the date line;
  the 24 h window; wind interpolation and the grey case; NHC/GDACS merging on derived positions.
- **The event cache:** a version-2 cache is ignored cleanly, as other version changes are.
- **Snapshots:** the 7 scenes are still pixel-identical to the baseline (S1). One new scene, `storm-replay`, at a
  display time between two PENA-26 fixes, is reviewed by eye and then becomes part of the baseline.

## Out of scope

- Replaying storms from before the app's 24 h storm window.
- Wind areas or cones for past times that had no advisory.
