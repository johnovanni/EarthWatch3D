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

## Lightning, clouds and Settings (Plan 3)

- [ ] **Lightning on/off:** lightning is on by default. Within about a minute of starting, flashes appear over the
      Americas (GOES East) in age colours, newest brightest. The bolt button in the layer strip hides them (turn it off
      and on again to check). The status bar
      shows **GLM East** (and **GLM West** when you turn the globe towards the Pacific) as dots, and **Today: ... MB** at
      the right of the time bar grows.
- [ ] **Window chips:** in the Display flyout, choosing one of the window lengths (5, 15, 30 or 60 minutes) changes how
      long a flash stays on the globe; fewer flashes at 5 than at 60. The choice is remembered after a restart.
- [ ] **Lightning hides in replay:** with lightning on, drag the time slider back (or press the step-back button): all
      flashes disappear, and come back when you press **NOW**. (Lightning is live only.)
- [ ] **Clouds on/off:** the cloud button shows and hides the live cloud layer (it is off by default); there is no
      black gap in the clouds between 60° N and 60° S, and the globe looks as before with it off.
- [ ] **Settings, no key:** open Settings (gear). With the EUMETSAT key and secret empty, **Test** says a key is needed
      and nothing is saved. The **MTG** dot is grey ("not set up") and the lightning legend's MTG line says
      "MTG: not set up (Settings)".
- [ ] **Settings, invalid key:** type a made-up key and secret, press **Test**: it says EUMETSAT rejected the key and
      the key is not stored.
- [ ] **Settings, valid key:** enter your own EUMETSAT key and secret, press **Test**: it says it worked, and after
      **Save** the key is stored encrypted (not readable in `%LOCALAPPDATA%\EarthWatch3D\settings.json`). The **MTG**
      dot turns green and Europe/Africa flashes appear.
- [ ] **MTG lightning:** with a valid key saved, Europe and Africa flashes appear within about a minute (the first
      search fetches up to 8 products of 10 minutes each). Hover the lightning legend: the MTG line shows the age of the
      newest flash and "updates every 10 min" (for example "MTG: newest flash 7 min ago (updates every 10 min)"); if the
      newest is over 15 minutes old it says "(delayed)" instead. The flashes only load while the camera faces Europe or
      Africa (the **MTG** dot is grey and idle otherwise). Saving a wrong key turns the **MTG** dot red within a few minutes ("EUMETSAT key
      rejected") and clicking it opens Settings; GOES East then shows flashes east of 37.6° W again.
- [ ] **Daily limit:** in Settings set the daily limit to **10** MB and Save. As soon as **Today:** reaches 10 MB the
      GLM dots turn **blue** (paused), the status bar reads **Today: … MB (limit reached)**, no new flashes arrive, and
      the other layers keep updating. Turning lightning off then turns the GLM dots grey (idle, "Not polled right
      now"). Setting the limit to
      empty (no limit) resumes lightning at the next update (within 20 s). The count is kept when you restart the app
      on the same day.
- [ ] **Units:** in Settings switch between **km** and **miles**: the details panel, the Sun and Moon card and the
      distances all change at once, and the choice is remembered.

## Errors

- [ ] Start the app with a bad option, for example `--display-time yesterday`. A message explains what is wrong and
      the app closes without opening the globe.

## MSIX

The installed package, built by `tools/build-release.ps1` (see [release.md](release.md)). Before the first check,
trust the test certificate once (elevated; the command is in release.md) and build a local test package whose
`.appinstaller` points at the files on disk:

```
pwsh tools/build-release.ps1 -Signing Test -BaseUri "file:///C:/repos/EarthWatch3D/artifacts/release/"
```

If an unpackaged EarthWatch3D is running (`dotnet run`), close it first.

- [ ] **Install:** double-click `artifacts\release\EarthWatch3D.appinstaller`. App Installer shows EarthWatch3D,
      publisher **EarthWatch3D test build**, version 1.0.0.0; **Install** completes without an error.
- [ ] **Start menu:** EarthWatch3D is in the Start menu with the globe icon (not a blank tile), also in the taskbar
      once it runs, and in Settings → Apps → Installed apps.
- [ ] **Starts:** the app starts from the Start menu, the globe and the events appear, and the status dots turn green.
- [ ] **Welcome once:** the Welcome window shows on the first start of the installed app; after closing it and
      restarting, it does not show again. (The installed app has its own settings, separate from `dotnet run`'s, so
      Welcome shows once here even if you have seen it before.)
- [ ] **Data folder:** About (the **i** button) shows the data folder as
      `%LOCALAPPDATA%\Packages\johnovanni.EarthWatch3D_…\LocalCache\Local\EarthWatch3D` (with the real path, not
      `%LOCALAPPDATA%\EarthWatch3D`), and **Open** opens that folder in Explorer, holding `settings.json`, the cache
      and `logs`.
- [ ] **About links and notices:** in About, clicking a credit link opens the page in the browser; **Licence texts**
      opens `THIRD-PARTY-NOTICES.txt` from the package folder (it lists WPF and points to
      `THIRD-PARTY-NOTICES-DOTNET-RUNTIME.txt` next to it).
- [ ] **Data and logs - Open:** the **Open** button next to the data folder opens the `LocalCache` folder in Explorer.
- [ ] **Welcome buttons:** **Open Settings** closes the Welcome window and opens Settings; **Get a free key** opens
      the EUMETSAT registration page in the browser; **Esc** closes the Welcome window (as **Start** does) and it does
      not show again at the next start.
- [ ] **Update keeps settings:**
      1. In the installed app, open Settings: choose **miles**, set the daily limit to **50** MB, enter a test
         EUMETSAT key and secret (any text; there is no need to press **Test**), and **Save**. Let the app run for a
         minute so events are cached, then close it.
      2. Raise the version in `Directory.Build.props` to `1.0.1` and build into the same folder with the same
         command as above (the `file:///` base URI). Do not commit the version change unless it is a real release.
      3. Start the app from the Start menu and accept the update prompt. Windows checks on start only when 24 hours
         have passed since its last check (`HoursBetweenUpdateChecks="24"`), so right after installing there may be
         no prompt yet: then double-click `artifacts\release\EarthWatch3D.appinstaller` instead, and App Installer
         offers **Update** from 1.0.0.0 to 1.0.1.0. (Check the prompt itself on a later day.)
      4. About shows version **1.0.1**. Settings still shows miles, the 50 MB limit, the test key in the key box and
         **(saved)** next to the secret box. The events appear straight away from the cache, before the first
         download finishes, and **Today:** keeps the day's count.
      5. Put the version back to `1.0.0`.
- [ ] **Uninstall:** Settings → Apps → Installed apps → EarthWatch3D → Uninstall. Afterwards there is no
      `%LOCALAPPDATA%\Packages\johnovanni.EarthWatch3D_*` folder, and EarthWatch3D is gone from the Start menu.
- [ ] **If the file:/// update source is refused** (App Installer reports it cannot use the URI): serve the folder
      with `python -m http.server 8080 --bind 127.0.0.1` (run in `artifacts\release`), build with
      `-BaseUri http://127.0.0.1:8080/` and install from `http://127.0.0.1:8080/EarthWatch3D.appinstaller`
      (download it and open the file).

## Offline samples

To check the drawing without depending on today's weather, start the app on the saved samples (captured around
19:15–20:30 UTC on 2026-11-11):

```
dotnet run --project src/EarthWatch3D.App -c Release -- --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z
dotnet run --project src/EarthWatch3D.App -c Release -- --offline-samples tests\Samples --display-time 2026-11-11T20:00:00Z --look-at 10,-165
dotnet run --project src/EarthWatch3D.App -c Release -- --offline-samples tests\Samples --display-time 2026-11-11T19:10:00Z --look-at 0,-60 --layers lightning,clouds
```

The third command shows the lightning samples (the GLM files are from 19:00 UTC, so a 15-minute window ending at
19:10 holds them; at 20:00 they would already have faded out): about 700 flashes over South America in orange age
colours, plus clouds. The **Today:** figure counts the sample files' bytes.

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
