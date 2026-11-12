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
