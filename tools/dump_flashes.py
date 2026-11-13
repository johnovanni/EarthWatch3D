#!/usr/bin/env python3
"""Dump lightning flashes from a NetCDF-4 file to CSV: time_utc,lat,lon,energy.

Usage:
    python dump_flashes.py <input.nc> <output.csv>               # GOES GLM L2 LCFA (verified)
    python dump_flashes.py <input.nc> <output.csv> --mtg [--mtg-map JSON]   # MTG LI-2-LFL (verified)

Requires: pip install netCDF4 numpy
netCDF4 applies scale_factor/add_offset (and _Unsigned) automatically.

GLM (verified on GOES-19, 2026-11-11):
    flash_lat, flash_lon   float32, degrees, NOT packed (no scale/offset)
    flash_energy           uint16 packed, scale_factor/add_offset -> joules
    flash_time_offset_of_first_event  uint16 packed -> seconds relative to product_time
    product_time           float64 seconds since 2000-01-01 12:00:00 (scalar)
    energy fill value (65535 unsigned) is masked -> written as empty.

MTG LI-2-LFL (verified on MTG-I1, 2026-11-13, with the CHK-BODY .nc file from inside the product zip):
    latitude, longitude    int16 packed, scale_factor 0.0027, add_offset 0, _FillValue -32767 -> degrees
    flash_time             float64, CF time "seconds since 2000-01-01 00:00:00.0" (UTC)
    radiance               uint16 (_Unsigned), _FillValue 65535, mW.m-2.sr-1 (NOT energy in joules:
                           the "energy" column holds radiance for MTG)
    flash_filter_confidence uint8, scale 0.004, _FillValue 255: 0 = high confidence of a true flash,
                           1 = low (EUM/MTG/SPE/10/0452 v5C). Flashes above 0.5 are left out, as the app does.
All variables are in the root group. Override names with --mtg-map, e.g.
    --mtg-map '{"lat":"latitude","lon":"longitude","energy":"radiance","time":"flash_time","group":""}'
"group" is optional (path of the NetCDF group holding the variables).
"""
import argparse
import csv
import datetime as dt
import json
import sys

import numpy as np
from netCDF4 import Dataset, num2date

GLM_EPOCH = dt.datetime(2000, 1, 1, 12, 0, 0, tzinfo=dt.timezone.utc)
MTG_DEFAULT_MAP = {"lat": "latitude", "lon": "longitude", "energy": "radiance",
                   "time": "flash_time", "group": "", "confidence": "flash_filter_confidence"}
MTG_MAX_CONFIDENCE = 0.5  # 0 = surely a flash, 1 = probably not (see above)


def _fmt_time(t):
    return t.strftime("%Y-%m-%dT%H:%M:%S.") + f"{t.microsecond // 1000:03d}Z"


def _val(x, fmt="{:.5f}"):
    return "" if np.ma.is_masked(x) or not np.isfinite(float(x)) else fmt.format(float(x))


def _e(x):
    return _val(x, "{:.6e}")


def read_glm(ds):
    lat = ds.variables["flash_lat"][:]
    lon = ds.variables["flash_lon"][:]
    energy = ds.variables["flash_energy"][:]
    offs = ds.variables["flash_time_offset_of_first_event"][:]
    base = GLM_EPOCH + dt.timedelta(seconds=float(ds.variables["product_time"][()]))
    rows = []
    for i in range(len(lat)):
        if np.ma.is_masked(offs[i]) or not np.isfinite(float(offs[i])):
            continue  # no valid time: skip the flash rather than invent one
        t = base + dt.timedelta(seconds=float(offs[i]))
        rows.append((_fmt_time(t), _val(lat[i]), _val(lon[i]), _e(energy[i])))
    return rows


def read_mtg(ds, vmap):
    g = ds
    if vmap.get("group"):
        for part in vmap["group"].split("/"):
            g = g.groups[part]
    lat, lon = g.variables[vmap["lat"]][:], g.variables[vmap["lon"]][:]
    energy = g.variables[vmap["energy"]][:]
    tv = g.variables[vmap["time"]]
    times = num2date(tv[:], tv.units, only_use_cftime_datetimes=False, only_use_python_datetimes=True)
    conf = g.variables[vmap["confidence"]][:] if vmap.get("confidence") else None
    rows = []
    for i in range(len(lat)):
        if np.ma.is_masked(lat[i]) or np.ma.is_masked(lon[i]):
            continue  # no position
        if conf is not None and not np.ma.is_masked(conf[i]) and float(conf[i]) > MTG_MAX_CONFIDENCE:
            continue  # low confidence of a true flash
        t = times[i].replace(tzinfo=dt.timezone.utc)
        rows.append((_fmt_time(t), _val(lat[i]), _val(lon[i]), _e(energy[i])))
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("nc")
    ap.add_argument("csv")
    ap.add_argument("--mtg", action="store_true", help="MTG LI-2-LFL layout (body .nc from the product zip)")
    ap.add_argument("--mtg-map", default=None, help="JSON overriding the MTG variable names")
    a = ap.parse_args()
    with Dataset(a.nc) as ds:
        if a.mtg:
            vmap = dict(MTG_DEFAULT_MAP)
            if a.mtg_map:
                vmap.update(json.loads(a.mtg_map))
            rows = read_mtg(ds, vmap)
        else:
            rows = read_glm(ds)
    with open(a.csv, "w", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["time_utc", "lat", "lon", "energy"])
        w.writerows(rows)
    print(f"{len(rows)} flashes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
