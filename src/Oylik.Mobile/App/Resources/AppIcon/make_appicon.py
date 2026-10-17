"""Android adaptive icon: desktop ikonkasi (docs/make_icon.py) uslubida.

python -I make_appicon.py <papka>  →  appicon.png (gradient fon, to'liq kvadrat), appicon_foreground.png (varaq + tanga, shaffof fon)
Belgi kanvasni to'ldiradi; kichraytirish — csproj'dagi ForegroundScale (safe zone: 108dp dan markaziy 66dp).
"""
import sys
from PIL import Image, ImageDraw, ImageFilter

out_dir = sys.argv[1]
SIZE = 432          # 108dp × 4 (xxxhdpi)
SS = 4              # supersampling

TOP = (99, 102, 241)      # #6366F1 indigo
BOTTOM = (14, 165, 233)   # #0EA5E9 sky
WHITE = (255, 255, 255, 255)
HEADER = (199, 210, 254, 255)
LINE = (203, 213, 225, 255)
GOLD_A = (252, 211, 77)
GOLD_B = (245, 158, 11)


def gradient(S, a, b):
    g = Image.new("RGBA", (S, S))
    px = g.load()
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * (S - 1))
            px[x, y] = tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)
    return g


def background():
    S = SIZE * 2
    img = gradient(S, TOP, BOTTOM)
    gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(gloss).ellipse([-S * 0.3, -S * 0.75, S * 1.3, S * 0.42], fill=(255, 255, 255, 34))
    img.alpha_composite(gloss)
    return img.resize((SIZE, SIZE), Image.LANCZOS)


def foreground():
    """Desktop ikonkasidagi belgi (varaq + tanga), birlik koordinatalar 0..1; keyin markazga keltiriladi."""
    S = SIZE * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Desktop belgisi chegaralari: x 0.22..0.915, y 0.19..0.915 — markazlab, kanvasning ~92% ini egallaydi.
    bx0, by0, bx1, by1 = 0.22, 0.19, 0.915, 0.915
    k = 0.92 / max(bx1 - bx0, by1 - by0)
    ox = 0.5 - (bx0 + bx1) / 2 * k
    oy = 0.5 - (by0 + by1) / 2 * k
    X = lambda v: S * (ox + v * k)
    Y = lambda v: S * (oy + v * k)
    L = lambda v: S * v * k

    x0, y0, x1, y1 = X(0.22), Y(0.19), X(0.70), Y(0.79)
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([x0, y0 + L(0.025), x1, y1 + L(0.025)], radius=L(0.07), fill=(15, 23, 42, 70))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(L(0.02))))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([x0, y0, x1, y1], radius=L(0.07), fill=WHITE)

    hx0, hx1 = x0 + L(0.06), x1 - L(0.06)
    hy0, hy1 = y0 + L(0.07), y0 + L(0.17)
    d.rounded_rectangle([hx0, hy0, hx1, hy1], radius=L(0.025), fill=HEADER)
    lw = max(1, int(L(0.022)))
    for i in range(3):
        y = hy1 + L(0.10 + i * 0.105)
        d.line([(hx0, y), (hx1 - L(0.05) * (i == 2), y)], fill=LINE, width=lw)
    vx = hx0 + (hx1 - hx0) * 0.42
    d.line([(vx, hy1 + L(0.05)), (vx, y1 - L(0.08))], fill=LINE, width=lw)

    cx, cy, r = X(0.69), Y(0.69), L(0.19)
    ring = L(0.035)
    d.ellipse([cx - r - ring, cy - r - ring, cx + r + ring, cy + r + ring], fill=WHITE)
    coin = gradient(int(2 * r) + 1, GOLD_A, GOLD_B)
    cm = Image.new("L", coin.size, 0)
    ImageDraw.Draw(cm).ellipse([0, 0, coin.size[0] - 1, coin.size[1] - 1], fill=255)
    img.paste(coin, (int(cx - r), int(cy - r)), cm)
    d = ImageDraw.Draw(img)
    ir = r * 0.68
    d.ellipse([cx - ir, cy - ir, cx + ir, cy + ir], outline=(255, 255, 255, 210), width=max(1, int(L(0.022))))
    d.ellipse([cx - ir * 0.92, cy - ir * 0.92, cx + ir * 0.92, cy + ir * 0.92], outline=(180, 83, 9, 90), width=max(1, int(L(0.012))))
    # Aniq markazlash: belgining haqiqiy chegarasi (soya bilan) kanvas markazida, 92% maydonda.
    glyph = img.crop(img.getbbox())
    k2 = S * 0.92 / max(glyph.size)
    glyph = glyph.resize((round(glyph.width * k2), round(glyph.height * k2)), Image.LANCZOS)
    centered = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    centered.alpha_composite(glyph, ((S - glyph.width) // 2, (S - glyph.height) // 2))
    return centered.resize((SIZE, SIZE), Image.LANCZOS)


background().save(f"{out_dir}/appicon.png")
foreground().save(f"{out_dir}/appicon_foreground.png")
print("ok")
