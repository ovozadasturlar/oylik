"""Oylik dastur ikonkasi (v2): gradient squircle, oq jadval varag'i va oltin tanga.

python -I docs/make_icon.py <chiqish_papkasi>  →  oylik.ico, oylik-256.png, preview.png
"""
import sys
from PIL import Image, ImageDraw, ImageFilter

out_dir = sys.argv[1]

TOP = (99, 102, 241)      # #6366F1 indigo
BOTTOM = (14, 165, 233)   # #0EA5E9 sky
WHITE = (255, 255, 255, 255)
HEADER = (199, 210, 254, 255)   # #C7D2FE
LINE = (203, 213, 225, 255)     # #CBD5E1
GOLD_A = (252, 211, 77)         # #FCD34D
GOLD_B = (245, 158, 11)         # #F59E0B
GOLD_EDGE = (180, 83, 9, 255)   # #B45309


def gradient(S, a, b):
    """Chap-yuqoridan o'ng-pastga diagonal gradient."""
    g = Image.new("RGBA", (S, S))
    px = g.load()
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * (S - 1))
            px[x, y] = tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)
    return g


def draw(size: int) -> Image.Image:
    s = 4 if size >= 128 else 8
    S = size * s
    radius = int(S * 0.235)

    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(gradient(S, TOP, BOTTOM), (0, 0), mask)

    # yuqori yaltiroq
    gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(gloss).ellipse([-S * 0.3, -S * 0.75, S * 1.3, S * 0.42], fill=(255, 255, 255, 34))
    img.alpha_composite(Image.composite(gloss, Image.new("RGBA", (S, S)), mask))

    small = size <= 24

    # varaq (jadval)
    x0, y0, x1, y1 = S * 0.22, S * 0.19, S * 0.70, S * 0.79
    if small:
        x0, y0, x1, y1 = S * 0.20, S * 0.18, S * 0.74, S * 0.80
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([x0, y0 + S * 0.025, x1, y1 + S * 0.025], radius=S * 0.07, fill=(15, 23, 42, 70))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(S * 0.02)))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([x0, y0, x1, y1], radius=S * 0.07, fill=WHITE)

    # sarlavha qatori
    hx0, hx1 = x0 + S * 0.06, x1 - S * 0.06
    hy0, hy1 = y0 + S * 0.07, y0 + S * (0.17 if not small else 0.19)
    d.rounded_rectangle([hx0, hy0, hx1, hy1], radius=S * 0.025, fill=HEADER if not small else (99, 102, 241, 255))

    if not small:
        # jadval qatorlari va ustun chizig'i
        lw = max(1, int(S * 0.022))
        for k in range(3):
            y = hy1 + S * (0.10 + k * 0.105)
            d.line([(hx0, y), (hx1 - S * 0.05 * (k == 2), y)], fill=LINE, width=lw)
        vx = hx0 + (hx1 - hx0) * 0.42
        d.line([(vx, hy1 + S * 0.05), (vx, y1 - S * 0.08)], fill=LINE, width=lw)
    else:
        lw = max(1, int(S * 0.06))
        for k in range(2):
            y = hy1 + S * (0.15 + k * 0.17)
            d.line([(hx0, y), (hx1, y)], fill=(148, 163, 184, 255), width=lw)

    # tanga
    cx, cy, r = S * 0.69, S * 0.69, S * (0.19 if not small else 0.22)
    ring = S * 0.035
    d.ellipse([cx - r - ring, cy - r - ring, cx + r + ring, cy + r + ring], fill=WHITE)
    coin = gradient(int(2 * r) + 1, GOLD_A, GOLD_B)
    cm = Image.new("L", coin.size, 0)
    ImageDraw.Draw(cm).ellipse([0, 0, coin.size[0] - 1, coin.size[1] - 1], fill=255)
    img.paste(coin, (int(cx - r), int(cy - r)), cm)
    if not small:
        d = ImageDraw.Draw(img)
        ir = r * 0.68
        d.ellipse([cx - ir, cy - ir, cx + ir, cy + ir], outline=(255, 255, 255, 210), width=max(1, int(S * 0.022)))
        d.ellipse([cx - ir * 0.92, cy - ir * 0.92, cx + ir * 0.92, cy + ir * 0.92], outline=(180, 83, 9, 90), width=max(1, int(S * 0.012)))

    return img.resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """ICO: 16–64 — siqilmagan 32-bit BMP (Explorer/taskbar/Start eng ishonchli o'qiydi), 128/256 — PNG.
    Pillow 9.x hamma o'lchamni PNG yozadi — shuning uchun qo'lda."""
    import io
    import struct
    entries = []
    for sz in sorted(images):
        im = images[sz].convert("RGBA")
        if sz >= 128:
            buf = io.BytesIO()
            im.save(buf, "PNG")
            data = buf.getvalue()
        else:
            header = struct.pack("<IiiHHIIiiII", 40, sz, sz * 2, 1, 32, 0, 0, 0, 0, 0, 0)
            r, g, b, a = im.split()
            bgra = Image.merge("RGBA", (b, g, r, a)).transpose(Image.FLIP_TOP_BOTTOM).tobytes()
            row = ((sz + 31) // 32) * 4
            mask = bytes(row * sz)  # alfa kanal ishlatiladi, AND-niqob bo'sh
            data = header + bgra + mask
        entries.append((sz, data))
    out = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    body = b""
    for sz, data in entries:
        dim = 0 if sz >= 256 else sz
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        body += data
    open(path, "wb").write(out + body)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = {sz: draw(sz) for sz in sizes}
write_ico(f"{out_dir}/oylik.ico", images)
images[256].save(f"{out_dir}/oylik-256.png")

shown = (16, 24, 32, 48, 64, 128)
preview = Image.new("RGBA", (sum(shown) + 16 * (len(shown) + 1), 160), (238, 240, 243, 255))
x = 16
for sz in shown:
    preview.alpha_composite(images[sz], (x, 80 - sz // 2))
    x += sz + 16
preview.save(f"{out_dir}/preview.png")
print("ok")
