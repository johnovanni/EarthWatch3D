<#
.SYNOPSIS
  Downloads the NASA Blue Marble image and Natural Earth line files, and writes the app assets.
.DESCRIPTION
  Downloads to a scratch folder (outside the repo), resizes the Blue Marble to 8192x4096
  (HighQualityBicubic, JPEG quality 90) and copies the GeoJSON files into src/EarthWatch3D.App/Assets.
  Also builds plate_boundaries.geojson from Peter Bird's PB2002 steps (Hugo Ahlenius / Nordpil, ODC-By 1.0):
  the 5,824 step LineStrings are merged into polylines per boundary kind (convergent, divergent, transform).
  URLs are the ones recorded in docs/data-sources.md, section "Static assets".
#>
param([string]$WorkDir = (Join-Path $env:TEMP 'EarthWatch3D.assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\EarthWatch3D.App\Assets'
New-Item -ItemType Directory -Force $assets, $WorkDir | Out-Null
$assets = (Resolve-Path $assets).Path

$ua = 'EarthWatch3D/0.1.0 (+https://github.com/johnovanni/EarthWatch3D)'
$bmUrl = 'https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/july/world.topo.bathy.200407.3x21600x10800.jpg'
$ne = 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/'
$plateUrl = 'https://raw.githubusercontent.com/fraxen/tectonicplates/master/GeoJSON/PB2002_steps.json'
$geo = 'ne_50m_coastline.geojson', 'ne_50m_admin_0_boundary_lines_land.geojson'

function Get-IfMissing($url, $path) {
    if (Test-Path $path) { Write-Host "Using cached $path"; return }
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $path -UserAgent $ua
}

$bmSrc = Join-Path $WorkDir 'world.topo.bathy.200407.3x21600x10800.jpg'
Get-IfMissing $bmUrl $bmSrc
foreach ($g in $geo) { Get-IfMissing ($ne + $g) (Join-Path $WorkDir $g) }

# Resize 21600x10800 -> 8192x4096.
$src = [System.Drawing.Image]::FromFile($bmSrc)
try {
    $dst = New-Object System.Drawing.Bitmap 8192, 4096
    try {
        $gfx = [System.Drawing.Graphics]::FromImage($dst)
        $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $gfx.DrawImage($src, 0, 0, 8192, 4096)
        $gfx.Dispose()
        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
        $p = New-Object System.Drawing.Imaging.EncoderParameters 1
        $p.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 90L
        $dst.Save((Join-Path $assets 'bluemarble-8192.jpg'), $codec, $p)
    } finally { $dst.Dispose() }
} finally { $src.Dispose() }

# Plate boundaries: merge consecutive steps of one PLATEBOUND and kind into polylines.
$plateSrc = Join-Path $WorkDir 'PB2002_steps.json'
Get-IfMissing $plateUrl $plateSrc
$kindOf = @{ SUB = 'convergent'; OCB = 'convergent'; CCB = 'convergent'; OSR = 'divergent'; CRB = 'divergent'; OTF = 'transform'; CTF = 'transform' }
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$lines = New-Object System.Collections.Generic.List[object]   # @{ Kind; Plates; Pts = List[double[]] }
$cur = $null
function Add-Point($line, [double]$lon, [double]$lat) {
    $lon = [math]::Round($lon, 3); $lat = [math]::Round($lat, 3)
    $n = $line.Pts.Count
    if ($n -gt 0 -and $line.Pts[$n - 1][0] -eq $lon -and $line.Pts[$n - 1][1] -eq $lat) { return }
    $line.Pts.Add([double[]]@($lon, $lat))
}
$plates = Get-Content $plateSrc -Raw | ConvertFrom-Json
foreach ($f in $plates.features) {
    $kind = $kindOf[[string]$f.properties.STEPCLASS]
    if (-not $kind) { continue }
    $pb = [string]$f.properties.PLATEBOUND
    $c = $f.geometry.coordinates
    # A step that crosses the date line (a jump of more than 180 degrees) is cut into runs so no polyline jumps across it.
    $runs = New-Object System.Collections.Generic.List[object]
    $run = New-Object System.Collections.Generic.List[double[]]
    for ($i = 0; $i -lt $c.Count; $i++) {
        $pt = [double[]]@([double]$c[$i][0], [double]$c[$i][1])
        if ($run.Count -gt 0 -and [math]::Abs($pt[0] - $run[$run.Count - 1][0]) -gt 180) { $runs.Add($run); $run = New-Object System.Collections.Generic.List[double[]] }
        $run.Add($pt)
    }
    $runs.Add($run)
    foreach ($r in $runs) {
        $first = $r[0]; $join = $false
        if ($cur -and $cur.Kind -eq $kind -and $cur.Plates -eq $pb) {
            $last = $cur.Pts[$cur.Pts.Count - 1]
            $join = ([math]::Abs($first[0] - $last[0]) -le 0.001) -and ([math]::Abs($first[1] - $last[1]) -le 0.001)
        }
        if (-not $join) { $cur = @{ Kind = $kind; Plates = $pb; Pts = (New-Object System.Collections.Generic.List[double[]]) }; $lines.Add($cur) }
        foreach ($pt in $r) { Add-Point $cur $pt[0] $pt[1] }
    }
}
# The steps are densified (about 34 points each). Douglas-Peucker at 0.01 degrees (about 1 km) removes the redundant ones.
function Simplify-Line($pts, [double]$tol) {
    $n = $pts.Count
    $keep = New-Object bool[] $n
    $keep[0] = $true; $keep[$n - 1] = $true
    $stack = New-Object System.Collections.Generic.Stack[int[]]
    $stack.Push([int[]]@(0, ($n - 1)))
    while ($stack.Count -gt 0) {
        $seg = $stack.Pop(); $a = $seg[0]; $b = $seg[1]
        if ($b - $a -lt 2) { continue }
        $ax = $pts[$a][0]; $ay = $pts[$a][1]; $dx = $pts[$b][0] - $ax; $dy = $pts[$b][1] - $ay
        $len2 = $dx * $dx + $dy * $dy
        $best = -1.0; $bi = -1
        for ($i = $a + 1; $i -lt $b; $i++) {
            $px = $pts[$i][0] - $ax; $py = $pts[$i][1] - $ay
            if ($len2 -eq 0) { $d = [math]::Sqrt($px * $px + $py * $py) }
            else { $d = [math]::Abs($px * $dy - $py * $dx) / [math]::Sqrt($len2) }
            if ($d -gt $best) { $best = $d; $bi = $i }
        }
        if ($best -gt $tol) { $keep[$bi] = $true; $stack.Push([int[]]@($a, $bi)); $stack.Push([int[]]@($bi, $b)) }
    }
    for ($i = 0; $i -lt $n; $i++) { if ($keep[$i]) { ,$pts[$i] } }
}
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{"type":"FeatureCollection","features":[')
$first = $true
foreach ($l in $lines) {
    if ($l.Pts.Count -lt 2) { continue }
    if (-not $first) { [void]$sb.Append(',') }
    $first = $false
    $simple = @(Simplify-Line $l.Pts 0.01)
    $coords = ($simple | ForEach-Object { '[' + $_[0].ToString('0.###', $inv) + ',' + $_[1].ToString('0.###', $inv) + ']' }) -join ','
    [void]$sb.Append('{"type":"Feature","properties":{"kind":"' + $l.Kind + '","plates":' + (ConvertTo-Json ([string]$l.Plates) -Compress) + '},"geometry":{"type":"LineString","coordinates":[' + $coords + ']}}')
}
[void]$sb.Append(']}')
$plateOut = Join-Path $assets 'plate_boundaries.geojson'
[System.IO.File]::WriteAllText($plateOut, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Host ('Plate boundaries: {0} polylines from {1} steps, {2:N0} bytes' -f $lines.Count, $plates.features.Count, (Get-Item $plateOut).Length)

foreach ($g in $geo) { Copy-Item (Join-Path $WorkDir $g) (Join-Path $assets $g) -Force }

Get-ChildItem $assets -File | Where-Object Extension -ne '.md' |
    ForEach-Object { '{0,-52} {1,12:N0} bytes' -f $_.Name, $_.Length }
