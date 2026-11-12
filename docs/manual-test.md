# EarthWatch3D manual test checklist

Hands-on checks for things the automated tests and the `--snapshot` pictures can't cover: the mouse, the keyboard,
timing, and the network. Run a Release build:

```
dotnet run --project src/EarthWatch3D.App -c Release
```

Tick each box as you go. If something fails, the log is in `%LOCALAPPDATA%\EarthWatch3D\logs` (one file a day).

Some checks need events on screen. Live data changes all the time, so if there is no tropical cyclone or US warning
today, use the offline samples instead (see [Offline samples](#offline-samples) at the end).

## Globe and camera (from Plan 1)

- [ ] **Drag:** left-drag rotates the globe. Close in, the same drag turns it less.
- [ ] **Wheel limits:** wheel forward zooms in and stops at about 300 km altitude; wheel back zooms out and stops at
      about 20 Earth radii. Neither limit jumps or shakes.
- [ ] **Home:** after rotating and zooming, the Home key swings back to the starting view (20° N, 40° W, whole globe).
- [ ] **Double-click:** double-clicking a point turns it to the centre of the view, at the same distance.
- [ ] **Double-click near a pole:** double-click close to the North Pole (and then the South Pole). The globe turns
      smoothly, stays north-up, and doesn't spin or flip.
- [ ] **Face Sun / Face Moon:** each button on the Sun & Moon card swings the globe so the marker for the point under
      the Sun (or Moon) is in the middle. Face Sun ends looking straight down on the middle of the day side.
- [ ] **Night side:** the night side is darker and bluish, with a thin gold day/night line, and continents still show
      through it.

## Layers and display options

- [ ] **Layer strip** (top left): each button (Earthquakes, Cyclones, US alerts, EONET storms, Coastlines) hides and
      shows its layer at once. Coastlines also hides the country borders.
- [ ] The **☰** button expands the strip to show the labels, and the choice is remembered after a restart.
- [ ] **Display flyout** (**⋯** or **Display ▸**):
  - [ ] Minimum magnitude: choosing 5 leaves only the larger quake discs; "All" shows the most.
  - [ ] Show the last 24 h / 7 days changes how many quakes are shown and their age colours.
  - [ ] US alerts: Warnings is on by default. Turning on Watches or Advisories adds areas; turning off Warnings removes
        the warning areas.
  - [ ] Night shade slider: the night side gets lighter and darker as you drag it, without stutter.
  - [ ] Day/night line: unticking it removes the gold line.
- [ ] Close the app and start it again: the layer and display choices are as you left them.

## Time bar

- [ ] At start the readout says **LIVE** with the UTC time, updating every second.
- [ ] **Step back** (◀◀ ▾): the menu offers −10 min and −1 hour. Choosing one changes the readout to **REPLAY** with
      the new time; the Sun & Moon card, the night side and the events follow it.
- [ ] **Slider:** dragging it back over the last 7 days moves everything with it. Quakes that hadn't happened yet at
      that time disappear; storms show where they were.
- [ ] **Play** (▶) runs the replay forward. Each speed in the speed box (1×, 60×, 600×, 3600×) visibly changes how
      fast time moves. At 3600× the night side sweeps across the globe.
- [ ] Playing forward reaches the present and switches to **LIVE** by itself.
- [ ] **LIVE** jumps straight back to the present from any replay time.
- [ ] **Speed box click-outside:** open the speed box's list, then click somewhere else (on the globe or the empty
      space). The list closes, and the speed is unchanged.

## Hover, click and the details panel

- [ ] **Quake:** hovering over a quake disc shows a tooltip with its magnitude, depth and age ("M5.1 · 35 km deep ·
      2 h ago"). Clicking it opens the details panel (below the Sun & Moon card) with its magnitude, depth, time and
      place, and a link to the USGS page.
- [ ] **Storm:** hovering over a cyclone glyph shows its name, category and wind; clicking opens its details
      (category, wind, pressure, source) with a link to the NHC or GDACS page. The glyph's past track is a solid white line, the
      forecast track dashed, and the forecast cone a white area.
- [ ] **Alert:** hovering inside a US warning area shows the alert type and when it ends; clicking it opens the
      alert's details (event, severity, from, until and headline).
- [ ] **EONET storm:** hovering over a small EONET dot shows the storm's name (and wind, if known); clicking opens
      its details (wind, last fix, source).
- [ ] Clicking empty ocean, the **×** button or pressing **Esc** closes the details panel.
- [ ] Moving the mouse off the globe (onto the card or out of the window) closes the tooltip, and it doesn't come back
      by itself a moment later.
- [ ] Hovering over the Sun or Moon marker still shows its own tooltip, and clicking it doesn't select an event.

## Status dots and the network

The five dots at the bottom right are the data sources (USGS, GDACS, NHC, NWS, EONET). Hover over one for its
last update time or its error.

- [ ] With the network on, all five turn green within a minute of starting.
- [ ] **Unplug the network** (or turn Wi-Fi off) and wait. Each dot turns **amber** at its next failed update and
      **red** after three failures in a row. USGS and NWS do this within about 2–4 minutes; GDACS and NHC update every
      15 minutes and EONET every 30, so they can take up to about half an hour to turn amber. Events already on the
      globe stay there.
- [ ] Plug the network back in: each dot turns green again at its next attempt (at most 10 minutes later).
- [ ] **Restart offline:** with the network still unplugged, close the app (wait about 10 s after the last update first,
      so the cache is saved) and start it again. The cached events appear straight away, and the dots go amber and
      then red as above.

## Errors

- [ ] Start the app with a bad option, for example `--display-time yesterday`. A message explains what is wrong and
      the app closes without opening the globe.

## Offline samples

To check the drawing without depending on today's weather, start the app on the saved samples (captured around
19:15–20:30 UTC on 2026-11-11):

```
dotnet run --project src/EarthWatch3D.App -c Release -- --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z
dotnet run --project src/EarthWatch3D.App -c Release -- --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z --look-at 10,-165
```

The window title ends with **— SAMPLE DATA (offline)** and the readout says **SAMPLE 11 Nov 20:00 UTC** (never
LIVE), with all five dots green. The LIVE button reads **NOW** and is not lit red. Stepping back shows REPLAY as usual; **NOW** returns to SAMPLE. The app uses a
temporary cache and settings, so your own are not changed. Expect:

- Quake discs around the Pacific and in the Americas, with M5+ labels.
- Tropical Storm Pena near 9.9° N, 167.7° W (the second command looks straight at it): the glyph, a solid past track
  to the east, a dashed forecast track and white cone to the west running across the date line, and green GDACS wind
  areas. Typhoon Cempaka near 30° N, 154° E (a glyph only: the samples hold no track for it).
- US Flood Warning areas (green) where they really were, and the marine warnings (Gale and others) all drawn in one
  small zone off the Washington coast: the samples have the outline of only one zone (PZZ150), so every zone-only
  warning uses it. That shows zones load and draw; it is not where those warnings really were.
- One EONET storm, Tropical Cyclone 03S, near Madagascar. The EONET copies of Pena and Cempaka are hidden because
  the NHC/GDACS storms are shown.
