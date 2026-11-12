# EarthWatch3D performance notes

Plan 1, Task 12. Measured **2026-11-11** (about 21:00–21:31 UTC).

**Machine:** laptop, Intel Core i7-1165G7 (4 cores, 8 threads, 2.8 GHz), 16 GB RAM, Intel Iris Xe integrated
graphics (WPF render tier 2) driving a 1920×1080 60 Hz panel plus two 1920×1080 75 Hz DisplayLink monitors.
Windows 11 (10.0.26300), .NET 10.0.12, Release build, workstation GC. Window at its default 1100×720.

Because the GPU is integrated, its textures live in system memory and count toward the app's working set.
On a PC with a separate graphics card the working set would be lower.

## How the numbers were taken

- **Working set:** `Get-Process` `WorkingSet64` from outside the app, and, in the `PERF_LOG` build, the app's own
  `Process.WorkingSet64` 10 s after the first globe image is shown.
- **Compose time:** the `PERF_LOG` build times every compose (`GlobeImageComposer.ComposeInto` on a background
  task) and logs it with the UTC time and sub-solar point.
- **Frames per second:** the `PERF_LOG` build's `--bench` option spins the camera 360° about +Z over 5 s and counts
  distinct `CompositionTarget.Rendering` frames.

```
dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"
src\EarthWatch3D.App\bin\Release\net10.0-windows\EarthWatch3D.App.exe --bench
type %TEMP%\EarthWatch3D-perf.log
```

## Results

| Step | Globe image | Lines layer | Working set 10 s after load | Compose time | FPS while rotating |
|---|---|---|---|---|---|
| Before Task 12 (HEAD 72505fe), external sample | 8192×4096 | dense BGRA | **1,199 MB** (1,144 MB private) | ≈200 ms (Task 8 Perf test) | not measured |
| Sparse lines | 8192×4096 | sparse | **1,197 MB** (1,141 MB private) | 86 ms first, then 292, 62, 52 ms | 46.4 fps |
| Sparse lines + 4096 fallback (**shipped**) | 4096×2048 | sparse | **477 MB** (418 MB private) | 76 ms first, then 31, 31, 61 ms | 48.6 fps |

Other measurements from the same runs:

- 4096×2048 compose from the real 8192 layers point-sampled to half size (5 runs after a warm-up, in the
  8192 run): 55, 55, 55, 56, 49 ms.
- Load (8192): decode + GeoJSON 400 ms; rasterizing the lines and making them sparse 780 ms; 468,439 line pixels (1.40 %).
- Load (4096): decode + GeoJSON 318 ms; rasterize + sparse 449 ms; 222,552 line pixels (2.65 %).
- Working set over 3.5 minutes with a compose every minute: 8192 stayed at 1,215–1,241 MB; 4096 stayed at
  477–533 MB (the peak is during and just after the rotation bench).
- `Get-Process` from outside: 1,235 MB 15 s after launch and 1,226 MB at 215 s (8192); 519 MB and 488 MB (4096).

## Plan 2, Task 14: events on the globe

Measured **2026-11-12** (06:10 and 06:20 UTC), same machine and window size, .NET 10.0.15, `--bench` as above. Both
runs loaded the same event cache (1,850 quakes, 3 cyclone advisories, 3 EONET storms); with the default filter
(M2.5+, last 24 h) 45 quake discs are drawn, 7 of them M5+ (3 of those labelled on the side facing the home view), and
4 quakes under an hour old have pulsing rings.

| Build | FPS while rotating | Working set 10 s after load |
|---|---|---|
| Before Task 14 (HEAD 2caa38d, no events drawn) | **52.4 fps** (262 frames in 5.00 s) | 538 MB |
| Task 14 (quake discs, rings, storm glyphs and tracks, labels) | **44.8 fps** (225 frames in 5.02 s) | 542 MB |
| Task 14 review fix: rings redrawn at most at 20 Hz into two reused buffers (06:29 UTC) | **46.3 fps** (232 frames in 5.01 s) | 545 MB |

Both are above the 30 fps target. While the camera moves, each frame re-places the labels and Helix recomputes its
screen-space lines (past, forecast and EONET tracks, quake rings) and billboards; the merged disc meshes are rebuilt only
when the visible quakes or their colour buckets change, or the minimum disc size changes by more than 20% and that changes
some disc's size. One run each, so the differences include run-to-run variation (Task 12's runs of one build gave 46.4
and 48.6 fps).

In all three Task 14 runs 4 quakes were under an hour old, so rings were animating. At first they were redrawn every
frame into a new point collection. Limiting them to 20 Hz, with two reused buffers, gave back about 1.5 fps. Most of
the remaining gap to the baseline is therefore not the rings. The likely causes are Helix's per-frame screen-space
updates of the other line visuals and billboards while the camera moves, and the label placement. Neither has been
measured separately.

## Plan 2, Task 17: with the offline samples

Measured **2026-11-12** (07:11 UTC), same machine and window size, .NET 10.0.15, `PERF_LOG` Release build:

```
dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"
src\EarthWatch3D.App\bin\Release\net10.0-windows\EarthWatch3D.App.exe --bench --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z
```

All five feeds read the samples, so every kind of event is loaded: 1,856 quakes, 5 cyclone advisories (Pena from both
NHC and GDACS, shown as one storm; Cempaka; two older GDACS storms not shown at 20:00), the 428 alerts of the NWS
sample and 4 EONET storms. At 20:00 UTC with the default filter (M2.5+, last 24 h) 33 quake discs are drawn (7 of them M5+), with the
Pena and Cempaka glyphs, Pena's tracks, cone and wind areas, the US warning areas and one EONET storm. The bench waits
until that is drawn (0.45 s after the first globe image) and then spins the default view, which shows the Atlantic,
the Americas, Europe and Africa; Pena and the Pacific come into view during the turn.

| Measurement | Value |
|---|---|
| FPS while rotating | **59.0 fps** (295 frames in 5.00 s) |
| Working set when the events were first drawn | 570 MB (505 MB private, 116 MB managed) |
| Working set 10 s after load | **523 MB** (452 MB private, 117 MB managed) |
| Working set after the bench | 556 MB (482 MB private) |
| Working set after a forced full GC (experiment) | 542 MB (466 MB private, 109 MB managed) |
| `Get-Process` from outside, 30 s after launch | 526 MB |
| Compose (4096×2048), first and second | 52 ms and 51 ms |

The frame rate is higher than Task 14's 46.3 fps because this run is a replay (the display time is fixed at 20:00
UTC): quake rings pulse only while the view is live, so none were animating, and fewer discs were drawn (33 against
45). It is one run, so it includes run-to-run variation too. The second compose is the one that adds the Areas layer;
the areas image (another 4096×2048 BGRA image, 32 MiB) and WPF's copy of the new texture account for most of the
working set's rise from 424 MB at the first globe image to 562 MB just after. Working set stays under the 800 MB
target.

## Plan 2 final fix wave: 10 minutes live

One launch of the PERF_LOG Release build (`dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"`)
against the live services, 2026-11-12 09:14:29–09:24:45 UTC, ended with `Stop-Process` (so there was no exit save).
The build logs, once a minute, the working set, the thread count, `events.json`'s size and the store and zone counts.

| Minute | Working set | Private | Managed | Threads | Handles | events.json | Alerts | Alert zones in memory |
|---|---|---|---|---|---|---|---|---|
| 1 | 576 MB | 489 MB | 128 MB | 36 | 927 | 2,409 KB | 1,350 | 46 of 647 |
| 2 | 576 MB | 489 MB | 124 MB | 35 | 918 | 2,409 KB | 1,350 | 46 of 647 |
| 3 | 586 MB | 500 MB | 139 MB | 28 | 914 | 2,409 KB | 1,351 | 46 of 647 |
| 4 | 584 MB | 497 MB | 139 MB | 20 | 878 | 2,409 KB | 1,351 | 46 of 647 |
| 5 | 596 MB | 512 MB | 149 MB | 27 | 911 | 2,409 KB | 1,351 | 46 of 647 |
| 6 | 606 MB | 523 MB | 165 MB | 21 | 875 | 2,404 KB | 1,351 | 46 of 647 |
| 7 | 616 MB | 535 MB | 175 MB | 25 | 897 | 2,404 KB | 1,351 | 46 of 647 |
| 8 | 616 MB | 534 MB | 178 MB | 28 | 905 | 2,404 KB | 1,351 | 46 of 647 |
| 9 | 656 MB | 575 MB | 217 MB | 26 | 900 | 2,404 KB | 1,358 | 46 of 647 |
| 10 | 657 MB | 576 MB | 214 MB | 28 | 921 | 2,404 KB | 1,358 | 46 of 647 |

- **Threads:** flat, 20–36 (the scheduler starts feeds without waiting and all rasterizing shares one STA thread, so
  no thread is made per pass). Handles flat at 875–927.
- **Memory:** the working set rose 576 → 657 MB and the managed heap 128 → 214 MB. That is garbage not yet collected,
  not memory held: no forced collection ran after the first 15 s, and the biggest step (minute 8 → 9) is the one
  Areas rebuild of the steady state (a new 32 MB image when 7 alerts arrived; the old one becomes garbage). To check
  without a second launch, a throwaway test drove the NWS feed (the 1.8 MB sample), the zone cache, the store, the
  cache save, the scheduler, the areas plan and the STA rasterizer through 60 two-minute cycles (two hours' worth)
  and forced a full collection every 10 cycles: the live managed heap stayed at 6.7–6.8 MB. The minute line now
  also logs the gen-2 collection count, so a longer run can tell garbage from growth directly.
- **Cache:** `events.json` 2.4 MB (1,843 quakes, 3 advisories, about 1,350 alerts, 3 EONET storms). Two saves in
  10 minutes (09:14:47 and 09:19:46), five minutes apart as the new minimum interval allows. The old cache had no
  schema version and was deleted at start, as designed ("Event cache has schema version 0, not 2").
- **NWS:** 1,350–1,358 alerts in the store (24 h of history plus active). The zones of the active Warnings (the
  default filter) all loaded: 46 zone outlines, 17 ZonesLoaded events, no zone download failures. The other 601 zone
  ids belong to alerts that are not Warnings or are no longer active, so none were downloaded (see I4).
- **Composes:** one a minute, 17–30 ms in the steady state, plus one for the Areas rebuild.
- **App log:** no errors. Two warnings: the old cache deleted (above) and "GDACS: geometry feature of event 1001347
  malformed; skipped" (a coordinate with fewer than 2 numbers in GDACS's own data).

## What the memory is

At 8192×4096 one BGRA copy of the globe is 128 MiB. In the 8192 run the managed heap held only the base image
and the composed image (272 MB in all; the sparse lines are about 4 MB). The working set grew from 853 MB to
1,197 MB in the 10 s after the image was first shown, with no managed allocation: that is WPF's own copies of the
texture (the `WriteableBitmap` back and front buffers and the Direct3D texture, which on integrated graphics sits in
system memory). A forced full, compacting garbage collection afterwards freed only 16 MB, so this was not garbage.

That is why making the lines sparse (saving 128 MiB of managed memory) and storing the base as BGR24 (at most
32 MiB) could not bring the working set under the 800 MB target, and why BGR24 was not done: the 4096×2048 fallback
quarters every copy, including WPF's. The 8192 asset is still embedded and is scaled down while decoding
(`BitmapImage.DecodePixelWidth`), so going back to 8192 is a one-constant change (`SceneController.GlobeImageWidth`).

## Notes

- The first compose after start-up and the occasional later one are slower (CPU clocks ramping up on a laptop, cold
  thread-pool threads). Composes run on a background task, so the UI never waits for them.
- The frame rate is about the same at both sizes, so the texture size is not what limits it here.
- 4096 px around the equator is about 10 km per pixel. At the closest zoom (about 300 km up) the image is visibly
  softer than at 8192. That is the cost of the fallback.

## Plan 3, Task 8: 25,000 lightning flashes on the globe

Measured **2026-11-12** (15:36 UTC), same machine and window size, .NET 10.0.15, `PERF_LOG` Release build, live (the
real event cache: 1,842 quakes, 1,516 alerts). `--bench-lightning 25000` fills a buffer with 25,000 random flashes spread
over 15 minutes (uniform over the sphere), draws them through `LightningLayer` (four `PointsVisual3D`, one per age
bucket) and then runs the usual 360° rotation. The layer is built in 33 ms.

```
dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"
src\EarthWatch3D.App\bin\Release\net10.0-windows\EarthWatch3D.App.exe --bench --bench-lightning 25000
```

| Measurement | Value |
|---|---|
| FPS while rotating, 25,000 flashes | **38.6 fps** (194 frames in 5.02 s) |
| Working set 10 s after load (before the flashes) | 771 MB (712 MB private, 269 MB managed) |
| Working set after the bench | 857 MB (787 MB private, 340 MB managed) |

Above the 30 fps target (spec §10), so drawing the flashes as points stays. One run, and no 0-flash run on this day's
live data to compare with (the working set before the flashes was already higher than in earlier notes because the live
cache is larger), so the cost of the flashes alone is not isolated.

## Plan 3, Task 11: 10 minutes live with lightning on

Measured **2026-11-12** (16:16-16:26 UTC), same machine, .NET 10.0.15, `PERF_LOG` Release build, real network, default
view (the Americas), `--layers lightning --bench` (lightning on for the run only). The app was closed with a normal
window close at the end of 10 minutes; the download meter file then read 33,128,412 bytes. Once a minute the app logs
(`%TEMP%\EarthWatch3D-perf.log`, "lightning minute" lines) the frames per second over that minute, memory, flashes held
and the bytes downloaded that minute.

```
dotnet build src/EarthWatch3D.App -c Release "-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"
src\EarthWatch3D.Appin\Release
et10.0-windows\EarthWatch3D.App.exe --layers lightning --bench
```

| Measurement | Value |
|---|---|
| FPS, per-minute average over minutes 1-10 | 46.1 to 49.1 (mostly 48.1 to 48.8) |
| FPS in the 5 s rotation at 10 s after load (no flashes yet) | 37.4 (187 frames) |
| Working set, minutes 1-10 | 624 to 946 MB (private 570 to 871 MB; it moves with garbage collections) |
| Flashes held after minute 1 / minute 10 | 12,982 / 21,801 (East 12,629 / 21,208; West 353 / 593); the buffer keeps 60 minutes |
| Downloaded in minute 1 | 20.5 MB (the 15-minute catch-up of up to 45 files per feed, plus the events and zones) |
| Downloaded in minutes 2-10 | 11.9 MB in 9 minutes = about 1.3 MB per minute = **about 80 MB per hour** |
| Total in 10 minutes | 33.1 MB |

The frame rate with lightning on stayed well above 30 fps. The 25,000-flash rotation figure (38.6 fps) is in Task 8
above. The first-minute figure of 20.5 MB shows what the daily limit sees when lightning is switched on.
