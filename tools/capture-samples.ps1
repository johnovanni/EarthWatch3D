<#
.SYNOPSIS
  Re-downloads the sample responses in tests/Samples (data-source spike, steps 1-6 and 9).
.DESCRIPTION
  Needs no credentials. The EUMETSAT MTG sample stays a manual step (see docs/data-sources.md).
  Written to be compatible with Windows PowerShell 5.1 and PowerShell 7 (tested on PowerShell 7 only). The GLM CSV needs Python with netCDF4;
  pass -Python <path to python.exe> (e.g. a venv) or the CSV step is skipped.
  Every request sends the project User-Agent and no personal data.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\tests\Samples'),
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$UA = 'EarthWatch3D/0.1.0 (+https://github.com/johnovanni/EarthWatch3D)'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$now = [DateTime]::UtcNow
$failed = @()

function Get-File($url, $name) {
    $path = Join-Path $OutDir $name
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-WebRequest -Uri $url -OutFile $path -UserAgent $UA -UseBasicParsing -TimeoutSec 60
    "{0,-34} {1,10:N0} bytes {2,6:N1} s  {3}" -f $name, (Get-Item $path).Length, $sw.Elapsed.TotalSeconds, $url
}
function Step($label, [scriptblock]$body) {
    try { & $body } catch { Write-Warning "$label FAILED: $($_.Exception.Message)"; $script:failed += $label }
}
function Get-Json($url) { (Invoke-WebRequest -Uri $url -UserAgent $UA -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json }

# 1. USGS
Step 'usgs' {
    Get-File 'https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson' 'usgs_all_week.geojson'
    Get-File 'https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson' 'usgs_all_hour.geojson'
}

# 2. GDACS: event list (7 days) + geometry of the first current event
Step 'gdacs' {
    $from = $now.AddDays(-7).ToString('yyyy-MM-dd'); $to = $now.ToString('yyyy-MM-dd')
    $u = "https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH?eventlist=TC&alertlevel=green;orange;red&fromdate=$from&todate=$to"
    Get-File $u 'gdacs_events_tc.json'
    $list = Get-Content (Join-Path $OutDir 'gdacs_events_tc.json') -Raw | ConvertFrom-Json
    $ev = $list.features | Where-Object { $_.properties.iscurrent -eq 'true' } | Select-Object -First 1
    if (-not $ev) { $ev = $list.features | Select-Object -First 1 }
    if ($ev) { Get-File $ev.properties.url.geometry 'gdacs_geometry.json' } else { Write-Warning 'GDACS: no events in the last 7 days' }
}

# 3. NHC: CurrentStorms.json + track and cone KMZ of the first active storm
Step 'nhc' {
    Get-File 'https://www.nhc.noaa.gov/CurrentStorms.json' 'nhc_CurrentStorms.json'
    $cs = Get-Content (Join-Path $OutDir 'nhc_CurrentStorms.json') -Raw | ConvertFrom-Json
    $s = $cs.activeStorms | Select-Object -First 1
    if ($s) {
        Get-File $s.forecastTrack.kmzFile 'nhc_track.kmz'
        Get-File $s.trackCone.kmzFile 'nhc_cone.kmz'
    } else {
        Write-Warning 'NHC: no active storm; track/cone KMZ not captured (keep the existing ones).'
    }
}

# 4. NWS: active alerts + one zone outline (first affected zone of an alert without geometry)
Step 'nws' {
    Get-File 'https://api.weather.gov/alerts/active' 'nws_active.json'
    $a = Get-Content (Join-Path $OutDir 'nws_active.json') -Raw | ConvertFrom-Json
    $nogeo = $a.features | Where-Object { -not $_.geometry -and $_.properties.affectedZones.Count -gt 0 } | Select-Object -First 1
    if ($nogeo) { Get-File $nogeo.properties.affectedZones[0] 'nws_zone.json' }
}

# 5. EONET
Step 'eonet' { Get-File 'https://eonet.gsfc.nasa.gov/api/v3/events?category=severeStorms&status=all&days=7' 'eonet_storms.json' }

# 6. GOES GLM: first file of the previous UTC hour, GOES-19 and GOES-18
Step 'glm' {
    $h = $now.AddHours(-1)
    $prefix = 'GLM-L2-LCFA/{0:yyyy}/{1:000}/{0:HH}/' -f $h, $h.DayOfYear
    foreach ($sat in 19, 18) {
        $listXml = (Invoke-WebRequest -Uri "https://noaa-goes$sat.s3.amazonaws.com/?list-type=2&prefix=$prefix" -UserAgent $UA -UseBasicParsing).Content
        # Same hour as glm_g19_sample.nc, so the sample's key is in the saved listing (test input for GlmListing).
        if ($sat -eq 19) { [IO.File]::WriteAllText((Join-Path $OutDir 'glm_g19_listing.xml'), $listXml, (New-Object Text.UTF8Encoding $false)) }
        $list = [xml]$listXml
        $first = $list.ListBucketResult.Contents | Select-Object -First 1
        if (-not $first) { Write-Warning "GLM G${sat}: no objects under $prefix"; continue }
        Get-File "https://noaa-goes$sat.s3.amazonaws.com/$($first.Key)" "glm_g${sat}_sample.nc"
    }
    $nc = Join-Path $OutDir 'glm_g19_sample.nc'; $csv = Join-Path $OutDir 'glm_g19_sample.csv'
    $dump = Join-Path $PSScriptRoot 'dump_flashes.py'
    if (Get-Command $Python -ErrorAction SilentlyContinue) {
        if (Test-Path $csv) { Remove-Item $csv }  # never leave a CSV that belongs to an older .nc
        & $Python -I $dump $nc $csv
        if ($LASTEXITCODE -ne 0) { throw "dump_flashes.py failed (is netCDF4 installed for ${Python}?)" }
    } else {
        if (Test-Path $csv) { Remove-Item $csv }
        Write-Warning 'Python not found: the new GLM .nc has NO matching CSV (the old one was removed so the pair cannot disagree). Re-run with -Python <path>.'
    }
}

# 9. GIBS: one 4096x2048 GetMap (GetCapabilities is 2.6 MB and is not saved) of yesterday's (complete) VIIRS NOAA-21 true colour
Step 'gibs' {
    $wms = 'https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi'
    $day = $now.AddDays(-1).ToString('yyyy-MM-dd')
    Get-File "${wms}?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&CRS=EPSG:4326&BBOX=-90,-180,90,180&WIDTH=4096&HEIGHT=2048&STYLES=&LAYERS=VIIRS_NOAA21_CorrectedReflectance_TrueColor&FORMAT=image/jpeg&TIME=$day" 'gibs_viirs_noaa21_truecolor_4096.jpg'
}

if ($failed.Count) { Write-Warning "Failed steps: $($failed -join ', ')"; exit 1 }
Write-Host 'Done.'
