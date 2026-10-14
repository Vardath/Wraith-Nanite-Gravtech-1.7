from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

OUT = Path("Textures/Things/Building/Goauld/Shuttle")
OUT.mkdir(parents=True, exist_ok=True)

S = 2048
C = S // 2

def rgba(hexv, a=255):
    hexv = hexv.lstrip("#")
    return tuple(int(hexv[i:i+2], 16) for i in (0, 2, 4)) + (a,)

def poly(draw, pts, fill, outline=None, width=1):
    draw.polygon(pts, fill=fill)
    if outline:
        draw.line(pts + [pts[0]], fill=outline, width=width, joint="curve")

def glow_line(img, pts, color=(255, 130, 25, 255), width=20, glow=35):
    g = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(g)
    gd.line(pts, fill=color[:3] + (150,), width=width * 2, joint="curve")
    g = g.filter(ImageFilter.GaussianBlur(glow))
    img.alpha_composite(g)
    d = ImageDraw.Draw(img)
    d.line(pts, fill=color, width=width, joint="curve")
    d.line(pts, fill=(255, 210, 120, 210), width=max(3, width // 4), joint="curve")

def gradient_fill(mask, top, bottom):
    grad = Image.new("RGBA", (S, S))
    px = grad.load()
    for y in range(S):
        t = y / (S - 1)
        col = tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(4))
        for x in range(S):
            px[x, y] = col
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out

def center(im):
    box = im.getchannel("A").getbbox()
    if not box:
        return im
    cx = (box[0] + box[2]) / 2
    cy = (box[1] + box[3]) / 2
    dx = round(im.width / 2 - cx)
    dy = round(im.height / 2 - cy)
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    out.alpha_composite(im, (dx, dy))
    return out

def build_north():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    wing = [
        (C, 350), (860, 410), (690, 510), (530, 650), (350, 830), (145, 1110),
        (285, 1210), (500, 1115), (700, 1015), (870, 970), (C, 955),
        (1178, 970), (1378, 1015), (1578, 1115), (1763, 1210), (1903, 1110),
        (1698, 830), (1518, 650), (1358, 510), (1188, 410)
    ]

    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).polygon(wing, fill=255)
    img.alpha_composite(gradient_fill(mask, rgba("#151a20"), rgba("#333942")))

    d = ImageDraw.Draw(img)
    d.line(wing + [wing[0]], fill=rgba("#050709"), width=46, joint="curve")
    d.line(wing + [wing[0]], fill=rgba("#a66c2c"), width=20, joint="curve")
    d.line(wing + [wing[0]], fill=rgba("#d69a4b"), width=7, joint="curve")

    left_panels = [
        [(235, 1080), (420, 840), (610, 665), (800, 520), (900, 480), (820, 635), (660, 790), (500, 955)],
        [(330, 1120), (520, 1030), (720, 930), (875, 895), (840, 975), (680, 1040), (500, 1130)],
    ]
    for pts in left_panels:
        poly(d, pts, rgba("#252b32"), rgba("#050709"), 22)
        d.line(pts + [pts[0]], fill=rgba("#7f5529"), width=12, joint="curve")
        r = [(S - x, y) for x, y in pts]
        poly(d, r, rgba("#252b32"), rgba("#050709"), 22)
        d.line(r + [r[0]], fill=rgba("#7f5529"), width=12, joint="curve")

    for side in (-1, 1):
        machinery = [
            (C + side * 110, 535), (C + side * 240, 580), (C + side * 395, 710),
            (C + side * 520, 845), (C + side * 475, 900), (C + side * 340, 815),
            (C + side * 205, 690), (C + side * 90, 615)
        ]
        poly(d, machinery, rgba("#3a2413"), rgba("#050709"), 18)
        for k in range(7):
            y = 610 + k * 38
            x = C + side * (170 + k * 35)
            d.rounded_rectangle(
                (x - 34, y - 12, x + 34, y + 12),
                radius=8, fill=rgba("#8b5727"), outline=rgba("#c88b44"), width=5
            )

    fuselage = [
        (C, 250), (C - 175, 460), (C - 210, 760), (C - 150, 1050),
        (C, 1260), (C + 150, 1050), (C + 210, 760), (C + 175, 460)
    ]
    fmask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(fmask).polygon(fuselage, fill=255)
    img.alpha_composite(gradient_fill(fmask, rgba("#3d434b"), rgba("#151a20")))

    d = ImageDraw.Draw(img)
    d.line(fuselage + [fuselage[0]], fill=rgba("#050709"), width=44, joint="curve")
    d.line(fuselage + [fuselage[0]], fill=rgba("#b17735"), width=18, joint="curve")

    cockpit = [(C, 405), (C - 82, 520), (C - 75, 735), (C, 815), (C + 75, 735), (C + 82, 520)]
    poly(d, cockpit, rgba("#171717"), rgba("#d3a056"), 16)
    d.line([(C, 432), (C, 780)], fill=rgba("#805026"), width=10)
    d.polygon(
        [(C - 58, 535), (C - 52, 690), (C, 742), (C + 52, 690), (C + 58, 535), (C, 470)],
        fill=rgba("#2b2925")
    )
    d.line([(C - 48, 535), (C, 485), (C + 48, 535)], fill=rgba("#bb7d35"), width=8)

    d.line([(C, 820), (C, 1195)], fill=rgba("#090b0d"), width=32)
    d.line([(C, 830), (C, 1180)], fill=rgba("#c58b43"), width=10)
    for yy in (900, 985, 1070):
        d.line([(C - 115, yy), (C - 35, yy + 45)], fill=rgba("#07090b"), width=16)
        d.line([(C + 115, yy), (C + 35, yy + 45)], fill=rgba("#07090b"), width=16)

    for side in (-1, 1):
        p1 = [(C + side * 180, 610), (C + side * 330, 735), (C + side * 500, 890), (C + side * 690, 1055)]
        p2 = [(C + side * 430, 725), (C + side * 575, 860), (C + side * 745, 1040)]
        glow_line(img, p1, rgba("#ff8d18"), width=24, glow=30)
        glow_line(img, p2, rgba("#ff9d22"), width=14, glow=20)

    d = ImageDraw.Draw(img)
    for side in (-1, 1):
        x = C + side * 430
        d.rounded_rectangle(
            (x - 42, 930, x + 42, 1180),
            radius=28, fill=rgba("#0b0e11"), outline=rgba("#b97c35"), width=14
        )
        glow_line(img, [(x, 980), (x, 1115)], rgba("#ff8d18"), width=18, glow=18)
        d = ImageDraw.Draw(img)
        d.ellipse((x - 30, 1150, x + 30, 1210), fill=rgba("#13161a"), outline=rgba("#d08b35"), width=10)

    grooves = [
        [(390, 1015), (565, 860), (730, 730)],
        [(470, 1095), (660, 1005), (805, 950)],
        [(620, 760), (745, 635)],
    ]
    for line in grooves:
        d.line(line, fill=rgba("#090b0d"), width=14, joint="curve")
        d.line([(S - x, y) for x, y in line], fill=rgba("#090b0d"), width=14, joint="curve")

    return img

north = center(build_north().resize((512, 512), Image.Resampling.LANCZOS))
variants = {
    "WNG_GoauldDeathGlider.png": north,
    "WNG_GoauldDeathGlider_north.png": north,
    "WNG_GoauldDeathGlider_east.png": center(north.rotate(-90, resample=Image.Resampling.BICUBIC, expand=False)),
    "WNG_GoauldDeathGlider_south.png": center(north.rotate(180, resample=Image.Resampling.BICUBIC, expand=False)),
    "WNG_GoauldDeathGlider_west.png": center(north.rotate(90, resample=Image.Resampling.BICUBIC, expand=False)),
}

for name, im in variants.items():
    path = OUT / name
    im.save(path, optimize=True)
    print(name, im.getbbox(), path.stat().st_size)
