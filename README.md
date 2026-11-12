# EarthWatch3D

A Windows desktop app (C#, WPF, HelixToolkit) that shows a 3D Earth with what is happening on it now.

> **Work in progress.** This is the end of plan 2 of 4 (events and time). Lightning, clouds, the Settings dialog
> and the installer come in plans 3–4 and are **not** in the app yet.

## What works today

- **Globe:** NASA Blue Marble imagery with Natural Earth coastlines and country borders. Rotate it and zoom in and out. An optional layer (off by default) draws the tectonic plate boundaries, coloured by type: red converging, yellow spreading, blue sliding past.
- **Night shading:** the night side is darkened with a slight blue tint, with a soft twilight edge, and a thin gold
  line marks the day/night boundary (sunset and sunrise), so it shows even over dark ocean. The image is recomposed every minute (and after the PC wakes from sleep), so the line moves.
- **Sun and Moon markers:** the points directly under the Sun and the Moon are marked on the globe. The Moon marker
  shows the current phase. Hover over a marker for a tooltip: it is where that body is directly overhead, which is
  also the closest point on Earth to it.
- **Arrows:** when the Sun or Moon is off screen, an arrow at the window edge points toward it
  ("Sun (behind)" / "Moon (behind)" when it is behind you, the camera: for example the Sun while you look at the day
  side). Arrows keep clear of the Sun & Moon card. When the Sun or Moon is in view but the Earth is in the way, its icon
  is dimmed and labelled "Sun (behind Earth)" / "Moon (behind Earth)".
- **Sun & Moon card** (top right): the sub-solar and sub-lunar points, the Moon's distance and how much of it is lit,
  updated every second, with **Face Sun** and **Face Moon** buttons. The chevron collapses the card.
- **Earthquakes** (USGS, every minute): a disc on the globe for each quake, sized by magnitude and coloured by age,
  with a label on M5 and above. Quakes under an hour old pulse while the view is live.
- **Tropical cyclones** (NHC every 15 min, GDACS every 15 min): a glyph coloured by category, the past track as a
  solid white line, the forecast track dashed, the forecast cone in white and the GDACS wind areas, all drawn
  correctly across the date line. When NHC and GDACS report the same storm it is shown once, from NHC.
- **US weather alerts** (NWS, every 2 min): the alert areas drawn on the globe in the standard NWS colours. Alerts that
  name forecast zones instead of giving an outline are drawn once the zone outlines have downloaded (kept on disk for
  30 days). Warnings are shown by default; watches, advisories and statements can be added.
- **Severe storms** (NASA EONET, every 30 min): a dot with its recent track, for storms not already shown from NHC or
  GDACS.
- **Hover and click:** hovering over an event shows a one-line tooltip; clicking it opens a details panel with a link
  to the source's own page.
- **Time bar** (bottom): the display time is live, or any time in the last 7 days. Step back or forward by 10 minutes
  or an hour, drag the slider, or play the replay at 1×, 60×, 600× or 3600×. The Sun, Moon, night side and events all
  follow the display time. **LIVE** jumps back to now.
- **Layer strip and Display options** (top left): show or hide each layer, choose the minimum quake magnitude, 24 h or
  7 days of quakes, which kinds of US alerts to show, the night-shade strength and the day/night line. Choices are
  saved.
- **Status dots** (bottom right): one per data source, green when its last update worked, amber while it is retrying
  and red after three failures in a row; hover for details. Events are cached on disk, so a restart without a network
  still shows them.
- **Errors:** a failed update affects only that source, and an unexpected error is logged and shown once, while the
  app keeps running. An error that repeats is logged in full once and then counted (one summary line a minute); if
  errors keep coming (more than 50 in 10 seconds) the app closes rather than hang. Each day's log file is capped at
  20 MB. If the globe image ever fails to compose, the plain Blue Marble is shown until the next try.
  The log is in `%LOCALAPPDATA%\EarthWatch3D\logs`.

### Coming in plans 3–4

| Plan | Adds |
|---|---|
| 3. Lightning, clouds, data use | Live lightning (GOES GLM, and Meteosat MTG with your own EUMETSAT key), live clouds (NASA GIBS), a data-use meter and the Settings dialog |
| 4. Ship | Welcome and About screens, a signed MSIX installer with automatic updates |

## Build and run

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone --recursive https://github.com/johnovanni/EarthWatch3D.git
cd EarthWatch3D
dotnet run --project src/EarthWatch3D.App -c Release
```

`--recursive` also fetches the `external/AstroCore` submodule (the astronomy library). If you cloned without it,
run `git submodule update --init`.

Run the tests with `dotnet test --filter "Category!=Perf&Category!=Network"`. The `Network` tests (for example, that every link in the About window still answers) use the internet; run them with `dotnet test --filter Category=Network`. The compose-time test is tagged `Perf`; run it in Release
with `dotnet test -c Release --filter Category=Perf`.

## Controls

| Action | What it does |
|---|---|
| Left-drag | Rotate the globe |
| Mouse wheel | Zoom in and out (closest about 300 km altitude) |
| Double-click the globe | Turn that point to the centre |
| Home | Back to the starting view (20° N, 40° W) |
| **Face Sun** / **Face Moon** (card) | Turn the globe so the point under the Sun or the Moon faces you |
| Hover over an event | A one-line tooltip (quake, storm, alert area or EONET storm; also the Sun and Moon markers) |
| Click an event | Open its details panel; click empty space, **×** or press Esc to close it |
| Layer strip buttons | Show or hide earthquakes, cyclones, US alerts, EONET storms, coastlines and borders, plate boundaries; ☰ shows the labels |
| **⋯** / **Display ▸** | Display options: minimum magnitude, 24 h or 7 days, alert types, night shade, day/night line |
| ◀◀ ▾ / ▾ ▶▶ | Step the display time back or forward (10 min or 1 hour) |
| Time slider | Pick any time in the last 7 days |
| ▶ and the speed box | Play the replay at 1×, 60×, 600× or 3600×; it becomes live when it reaches now |
| **LIVE** | Back to the present |

## Diagnostics

These command-line options are for checking the app without a screen capture tool. A bad value for
`--offline-samples`, `--display-time` or `--look-at` shows a message saying what is wrong, and the app closes.

| Option | What it does |
|---|---|
| `--snapshot <png path>` | Once the globe image is shown, saves a picture of the window (rendered by the app itself, at the window's DPI) as a PNG and exits. Exit code 1 if saving failed. |
| `--face-sun` | Once the globe image is shown, turns the camera to the Sun (as the Face Sun button does). With `--snapshot`, the picture is taken after the turn. |
| `--layers <list>` | Switches the named layers on for this run only (nothing is saved): any of `quakes`, `cyclones`, `alerts`, `eonet`, `coastlines`, `plates`, separated by commas, for example `--layers plates`. |
| `--look-at <lat,lon>` | Once the globe image is shown, turns the camera at once to look straight down on that point, in degrees (north and east positive), for example `--look-at 10,-165`. |
| `--delay-ms <N>` | With `--snapshot`: wait N milliseconds before the picture (default 1500). |
| `--dialog <about\|welcome>` | Once the globe is shown, opens that dialog over the main window. With `--snapshot`, the picture is of the dialog alone (the main window is not saved) and the app then exits. |
| `--snapshot-recompose` | With `--snapshot <png path>`: saves two pictures instead of one, `<name>-a.png` and `<name>-b.png`. The second is taken after the app's clock is moved 6 hours ahead and the globe image recomposed and shown, so the night side should have moved about 90° west. This checks that a later recompose reaches the screen, not only the first. |
| `--display-time <UTC time>` | Starts in replay at that time, written in ISO form with a `Z` or an offset, for example `2026-11-11T20:00:00Z`. A time more than 7 days ago is moved to the start of the 7 days. With `--offline-samples`, the app's clock starts at that time instead (see below). |
| `--offline-samples <folder>` | Answers every data request from the sample files in the folder (`tests\Samples`) instead of the network. The event cache, zone outlines and settings go to a temporary folder (`%TEMP%\EarthWatch3D-offline-…`, deleted on exit), so your own cache and saved choices are not touched; the log still goes to the usual folder. With `--display-time`, the app's clock starts at that time, so the samples aren't treated as days old, and runs on from there. Sample data never looks live: the window title ends with "— SAMPLE DATA (offline)", and the time readout says **SAMPLE** (for example "SAMPLE 11 Nov 20:00 UTC") where it would say LIVE; the LIVE button reads NOW and is not lit. Folders left by offline runs that were killed are deleted at the next start once they are a day old. With `--snapshot`, the picture waits (at most 20 s) until every source has reported once, the zone outlines have loaded and any alert and cyclone areas are on the globe. |

The samples were captured around 19:15–20:30 UTC on 2026-11-11. They hold the outline of only one NWS zone (PZZ150,
off the Washington coast), so offline every alert that names zones is drawn in that one zone: enough to see that
zones load and draw, not where those alerts really were. The GDACS track and wind areas are Pena's only; the other
GDACS storms appear as glyphs without tracks.

For example:

```
dotnet run --project src/EarthWatch3D.App -c Release -- --face-sun --snapshot %TEMP%\earth.png
dotnet run --project src/EarthWatch3D.App -c Release -- --face-sun --snapshot %TEMP%\earth.png --snapshot-recompose
dotnet run --project src/EarthWatch3D.App -c Release -- --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z --look-at 10,-165 --snapshot %TEMP%\pena.png
```

The second command writes `earth-a.png` (the day side, facing the Sun) and `earth-b.png` (6 hours later: the
terminator runs down the middle of the view, with the night side on the right). The third shows Tropical Storm Pena in
the central Pacific with its tracks, cone and wind areas, from the samples.

For hands-on checks (the mouse, the time bar, the network), see [docs/manual-test.md](docs/manual-test.md).

Performance logging is compiled in only when you ask for it:

```
dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"
```

`%3B` is an escaped `;`. Setting `DefineConstants` replaces the SDK's own list, so the command repeats `TRACE` and
`RELEASE` (the .NET SDK has no `ExtraDefineConstants` property to add to the list instead).

That build appends load and compose timings and memory use to `%TEMP%\EarthWatch3D-perf.log`, and accepts
`--bench`, which spins the globe once (360° over 5 s) and logs the frame rate. With `--offline-samples`, the bench
waits until the sample events are drawn, so it measures the globe with events on it. Rebuild normally afterwards. The
measured numbers are in [docs/perf.md](docs/perf.md).

## Credits

- **Imagery:** NASA Earth Observatory, *Blue Marble: Next Generation* with topography and bathymetry (R. Stöckli, July 2004).
- **Coastlines and borders:** [Natural Earth](https://www.naturalearthdata.com/) (public domain).
- **Plate boundaries:** Peter Bird (2003), An updated digital model of plate boundaries, G³ 4(3):1027; GIS data by Hugo Ahlenius, Nordpil (ODC-By 1.0, https://opendatacommons.org/licenses/by/1-0/).
- **Astronomy:** AstroCore, the shared astronomy library that grew out of the author's SolarSystem3D project
  (included as a git submodule).
- **3D:** [HelixToolkit.Wpf](https://github.com/helix-toolkit/helix-toolkit).
- **MVVM:** [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).
- **Earthquakes:** [U.S. Geological Survey](https://earthquake.usgs.gov/earthquakes/feed/) real-time GeoJSON feeds.
- **Tropical cyclones:** [NOAA National Hurricane Center](https://www.nhc.noaa.gov/) (current storms, forecast tracks
  and cones) and [GDACS](https://www.gdacs.org/), the Global Disaster Alert and Coordination System (European
  Commission and United Nations).
- **US weather alerts:** [NOAA National Weather Service](https://www.weather.gov/) API, alerts and forecast zones.
- **Severe storms:** [NASA EONET](https://eonet.gsfc.nasa.gov/), the Earth Observatory Natural Event Tracker.

The data sources, their URLs and how often they are read are listed in [docs/data-sources.md](docs/data-sources.md).
The events are shown for interest, not for safety decisions: follow your local weather service and authorities.
