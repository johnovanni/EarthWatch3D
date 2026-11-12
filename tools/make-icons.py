"""Generate the app icon (src/EarthWatch3D.App/app.ico) and the MSIX logos (packaging/Images/*.png).

Run from the repo root:  python tools/make-icons.py      (needs Pillow; do not use -I)

Design: an orthographic globe from Assets/bluemarble-8192.jpg centred on 15 N, 20 W, with the app's night shading
(R 0.32, G 0.36, B 0.50; smoothstep over Sun altitude 0 to -12 degrees; strength 0.85) for a Sun over 10 S, 60 E,
a thin gold terminator line, a pale-blue atmosphere rim, an anti-aliased edge and a transparent background. Rendered once at 1024 px and
downscaled with Lanczos for every size. Pure Python (no numpy), so the render takes a little while.
"""
import math
import os

from PIL import Image

Image.MAX_IMAGE_PIXELS = None

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "EarthWatch3D.App", "Assets", "bluemarble-8192.jpg")
OUT_IMAGES = os.path.join(ROOT, "packaging", "Images")
OUT_ICO = os.path.join(ROOT, "src", "EarthWatch3D.App", "app.ico")

N = 1024
RADIUS = 500.0           # leaves a small transparent margin around the disc
CENTRE_LAT, CENTRE_LON = 15.0, -20.0
SUN_LAT, SUN_LON = -10.0, 60.0
STRENGTH = 0.85
NIGHT = (0.32, 0.36, 0.50)
GOLD = (255, 200, 120)
RIM = (130, 180, 255)
LINE_WIDTH_DEG = 1.6     # Gaussian half-width of the terminator line, in degrees of Sun altitude


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def render():
    tex = Image.open(SRC).convert("RGB")
    tw, th = tex.size
    px = tex.load()

    def sample(lat, lon):
        fx = ((lon + 180.0) / 360.0 * tw - 0.5) % tw
        fy = min(max((90.0 - lat) / 180.0 * th - 0.5, 0.0), th - 1.001)
        x0, y0 = int(fx), int(fy)
        x1 = (x0 + 1) % tw
        y1 = y0 + 1
        ax, ay = fx - x0, fy - y0
        a, b, c, d = px[x0, y0], px[x1, y0], px[x0, y1], px[x1, y1]
        return [
            (a[i] * (1 - ax) + b[i] * ax) * (1 - ay) + (c[i] * (1 - ax) + d[i] * ax) * ay
            for i in range(3)
        ]

    p0 = math.radians(CENTRE_LAT)
    sin0, cos0 = math.sin(p0), math.cos(p0)
    sd, cd = math.sin(math.radians(SUN_LAT)), math.cos(math.radians(SUN_LAT))
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    out = img.load()
    c = (N - 1) / 2.0
    for j in range(N):
        y = (c - j) / RADIUS
        for i in range(N):
            x = (i - c) / RADIUS
            r2 = x * x + y * y
            r = math.sqrt(r2)
            cover = min(1.0, max(0.0, (1.0 - r) * RADIUS + 0.5))  # 1 px anti-aliased rim
            if cover <= 0.0:
                continue
            z = math.sqrt(max(0.0, 1.0 - r2))
            y2 = y * cos0 + z * sin0
            z2 = z * cos0 - y * sin0
            lat = math.degrees(math.asin(max(-1.0, min(1.0, y2))))
            lon = CENTRE_LON + math.degrees(math.atan2(x, z2))
            if lon > 180.0:
                lon -= 360.0
            elif lon < -180.0:
                lon += 360.0
            rgb = sample(lat, lon)
            phi, dl = math.radians(lat), math.radians(lon - SUN_LON)
            sh = math.sin(phi) * sd + math.cos(phi) * cd * math.cos(dl)
            h = math.degrees(math.asin(max(-1.0, min(1.0, sh))))
            f = STRENGTH * (1.0 - smoothstep(-12.0, 0.0, h))
            rgb = [rgb[k] * (1.0 - f * (1.0 - NIGHT[k])) for k in range(3)]
            g = math.exp(-(h / LINE_WIDTH_DEG) ** 2) * 0.9
            rgb = [rgb[k] * (1 - g) + GOLD[k] * g for k in range(3)]
            rim = smoothstep(0.90, 1.0, r) * 0.6   # pale-blue atmosphere rim keeps the night side readable when small
            rgb = [rgb[k] * (1 - rim) + RIM[k] * rim for k in range(3)]
            out[i, j] = (int(rgb[0] + 0.5), int(rgb[1] + 0.5), int(rgb[2] + 0.5), int(cover * 255 + 0.5))
    return img


def main():
    master = render()
    os.makedirs(OUT_IMAGES, exist_ok=True)

    def sq(size):
        return master.resize((size, size), Image.LANCZOS)

    def save(name, im):
        im.save(os.path.join(OUT_IMAGES, name))

    for base, size in (("StoreLogo", 50), ("Square44x44Logo", 44), ("Square150x150Logo", 150)):
        save(f"{base}.scale-100.png", sq(size))
        save(f"{base}.scale-200.png", sq(size * 2))
    for scale, (w, h) in ((100, (310, 150)), (200, (620, 300))):
        wide = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        wide.paste(sq(h), (0, 0))
        save(f"Wide310x150Logo.scale-{scale}.png", wide)
    for n in (16, 24, 32, 48, 256):
        save(f"Square44x44Logo.targetsize-{n}.png", sq(n))
        save(f"Square44x44Logo.targetsize-{n}_altform-unplated.png", sq(n))
    master.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 256)])
    print("done")


if __name__ == "__main__":
    main()
