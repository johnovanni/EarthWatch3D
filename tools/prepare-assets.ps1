<#
.SYNOPSIS
  Downloads the NASA Blue Marble image and Natural Earth line files, and writes the app assets.
.DESCRIPTION
  Downloads to a scratch folder (outside the repo), resizes the Blue Marble to 8192x4096
  (HighQualityBicubic, JPEG quality 90) and copies the GeoJSON files into src/EarthWatch3D.App/Assets.
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

foreach ($g in $geo) { Copy-Item (Join-Path $WorkDir $g) (Join-Path $assets $g) -Force }

Get-ChildItem $assets -File | Where-Object Extension -ne '.md' |
    ForEach-Object { '{0,-52} {1,12:N0} bytes' -f $_.Name, $_.Length }
