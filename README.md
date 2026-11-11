# EarthWatch3D

A Windows desktop app (C#, WPF, HelixToolkit) that shows a 3D Earth with what is happening on it now.

> **Work in progress.** This is the end of plan 1 of 4 (the foundation). Earthquakes, storms, weather alerts,
> lightning, clouds, the time slider and the installer come in plans 2–4 and are **not** in the app yet.

## What works today

- **Globe:** NASA Blue Marble imagery with Natural Earth coastlines and country borders. Rotate it and zoom in and out.
- **Night shading:** a translucent grey layer covers the night side and follows the real day/night line, with a
  soft twilight edge. The image is recomposed every minute (and after the PC wakes from sleep), so the line moves.
- **Sun and Moon markers:** the points directly under the Sun and the Moon are marked on the globe. The Moon marker
  shows the current phase.
- **Arrows:** when the Sun or Moon is off screen, an arrow at the window edge points toward it
  ("Sun (behind)" / "Moon (behind)" when it is behind you, the camera: for example the Sun while you look at the day
  side). Arrows keep clear of the Sun & Moon card. When the Sun or Moon is in view but the Earth is in the way, its icon
  is dimmed and labelled "Sun (behind Earth)" / "Moon (behind Earth)".
- **Sun & Moon card** (top right): the sub-solar and sub-lunar points, the Moon's distance and how much of it is lit,
  updated every second, with **Face Sun** and **Face Moon** buttons. The chevron collapses the card.

### Coming in plans 2–4

| Plan | Adds |
|---|---|
| 2. Events and time | Earthquakes (USGS), tropical cyclones (GDACS, NHC), US weather alerts (NWS), NASA EONET storms, a details panel, and a time bar that replays the last 7 days |
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

Run the tests with `dotnet test --filter Category!=Perf`. The compose-time test is tagged `Perf`; run it in Release
with `dotnet test -c Release --filter Category=Perf`.

## Controls

| Action | What it does |
|---|---|
| Left-drag | Rotate the globe |
| Mouse wheel | Zoom in and out (closest about 300 km altitude) |
| Double-click the globe | Turn that point to the centre |
| Home | Back to the starting view (20° N, 40° W) |
| **Face Sun** / **Face Moon** (card) | Turn the globe so the point under the Sun or the Moon faces you |

## Diagnostics

These command-line options are for checking the app without a screen capture tool:

| Option | What it does |
|---|---|
| `--snapshot <png path>` | Once the globe image is shown, saves a picture of the window (rendered by the app itself, at the window's DPI) as a PNG and exits. Exit code 1 if saving failed. |
| `--face-sun` | Once the globe image is shown, turns the camera to the Sun (as the Face Sun button does). With `--snapshot`, the picture is taken after the turn. |
| `--delay-ms <N>` | With `--snapshot`: wait N milliseconds before the picture (default 1500). |
| `--snapshot-recompose` | With `--snapshot <png path>`: saves two pictures instead of one, `<name>-a.png` and `<name>-b.png`. The second is taken after the app's clock is moved 6 hours ahead and the globe image recomposed and shown, so the night side should have moved about 90° west. This checks that a later recompose reaches the screen, not only the first. |

For example:

```
dotnet run --project src/EarthWatch3D.App -c Release -- --face-sun --snapshot %TEMP%\earth.png
dotnet run --project src/EarthWatch3D.App -c Release -- --face-sun --snapshot %TEMP%\earth.png --snapshot-recompose
```

The second command writes `earth-a.png` (the day side, facing the Sun) and `earth-b.png` (6 hours later: the
terminator runs down the middle of the view, with the night side on the right).

Performance logging is compiled in only when you ask for it:

```
dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"
```

`%3B` is an escaped `;`. Setting `DefineConstants` replaces the SDK's own list, so the command repeats `TRACE` and
`RELEASE` (the .NET SDK has no `ExtraDefineConstants` property to add to the list instead).

That build appends load and compose timings and memory use to `%TEMP%\EarthWatch3D-perf.log`, and accepts
`--bench`, which spins the globe once (360° over 5 s) and logs the frame rate. Rebuild normally afterwards. The
measured numbers are in [docs/perf.md](docs/perf.md).

## Credits

- **Imagery:** NASA Earth Observatory, *Blue Marble: Next Generation* with topography and bathymetry (R. Stöckli, July 2004).
- **Coastlines and borders:** [Natural Earth](https://www.naturalearthdata.com/) (public domain).
- **Astronomy:** AstroCore, the shared astronomy library that grew out of the author's SolarSystem3D project
  (included as a git submodule).
- **3D:** [HelixToolkit.Wpf](https://github.com/helix-toolkit/helix-toolkit).
- **MVVM:** [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).

Data sources for the coming plans are listed in [docs/data-sources.md](docs/data-sources.md).
