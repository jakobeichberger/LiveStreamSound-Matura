"""
Regenerates src/LiveStreamSound.App/Assets/app.ico from a procedural logo.

Logo design:
  A rounded-corner tile with a radial cyan→indigo gradient. On top, a
  white speaker icon on the left with 3 concentric arcs on the right —
  visually says "sound being streamed". Matches the role-selection
  cards (blue "Senden" + green "Empfangen"), skewed to blue.

Usage:
  python3 tools/generate-icon.py

No runtime dependency for the app — this only regenerates the ICO at
dev time. The resulting ICO is committed to the repo.
"""

from PIL import Image, ImageDraw
import os, math, io

SIZES = [16, 24, 32, 48, 64, 128, 256]
OUT_ICO = os.path.join(
    os.path.dirname(__file__), "..",
    "src", "LiveStreamSound.App", "Assets", "app.ico"
)

PRIMARY = (37, 99, 235)        # accent blue (matches Send card)
PRIMARY_DARK = (17, 40, 135)
ACCENT = (22, 163, 74)         # accent green (matches Receive card)
WHITE = (255, 255, 255, 255)


def radial_gradient(size, inner, outer):
    img = Image.new("RGB", (size, size), outer)
    draw = ImageDraw.Draw(img)
    cx = cy = size / 2
    max_r = size * 0.72
    for i in range(int(max_r), 0, -1):
        t = i / max_r
        r = int(inner[0] * (1 - t) + outer[0] * t)
        g = int(inner[1] * (1 - t) + outer[1] * t)
        b = int(inner[2] * (1 - t) + outer[2] * t)
        draw.ellipse([cx - i, cy - i, cx + i, cy + i], fill=(r, g, b))
    return img


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle([0, 0, size - 1, size - 1], radius, fill=255)
    return mask


def draw_logo(size):
    # Background: rounded tile with radial gradient
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bg = radial_gradient(size, PRIMARY, PRIMARY_DARK)
    bg = bg.convert("RGBA")
    bg.putalpha(rounded_mask(size, max(4, size // 7)))
    base.alpha_composite(bg)

    d = ImageDraw.Draw(base)

    # Speaker body — trapezoid + rectangle on the left
    cx = size * 0.34
    cy = size * 0.5
    sp_w = size * 0.22
    sp_h = size * 0.38
    # Box (back of speaker)
    box = [
        (cx - sp_w * 0.4, cy - sp_h * 0.3),
        (cx - sp_w * 0.4, cy + sp_h * 0.3),
        (cx - sp_w * 0.1, cy + sp_h * 0.3),
        (cx + sp_w * 0.4, cy + sp_h * 0.6),
        (cx + sp_w * 0.4, cy - sp_h * 0.6),
        (cx - sp_w * 0.1, cy - sp_h * 0.3),
    ]
    d.polygon(box, fill=WHITE)

    # Sound arcs to the right — 3 concentric, opacity decreasing
    arc_cx = size * 0.55
    arc_cy = size * 0.5
    thickness = max(2, size // 36)
    for i, r_factor in enumerate([0.20, 0.30, 0.42]):
        r = size * r_factor
        # draw arc from -35° to +35° relative to horizontal (open to right)
        bbox = [arc_cx - r, arc_cy - r, arc_cx + r, arc_cy + r]
        alpha = 255 - i * 60
        arc = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ad = ImageDraw.Draw(arc)
        ad.arc(bbox, start=-40, end=40, fill=(255, 255, 255, alpha), width=thickness)
        base.alpha_composite(arc)

    # tiny green accent dot — signals the "live stream" vibe
    dot_r = max(2, size // 26)
    d.ellipse(
        [size * 0.82 - dot_r, size * 0.22 - dot_r,
         size * 0.82 + dot_r, size * 0.22 + dot_r],
        fill=ACCENT + (255,),
    )

    return base


def main():
    os.makedirs(os.path.dirname(OUT_ICO), exist_ok=True)
    images = [draw_logo(s) for s in SIZES]
    # PIL writes ICO with multiple embedded images
    images[-1].save(
        OUT_ICO,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        append_images=images[:-1],
    )
    # Sanity: reopen and print contents
    print(f"wrote {OUT_ICO}")
    with Image.open(OUT_ICO) as ico:
        print("sizes:", [im.size for im in ico.ico.entry if True] if hasattr(ico, "ico") else ico.size)


if __name__ == "__main__":
    main()
