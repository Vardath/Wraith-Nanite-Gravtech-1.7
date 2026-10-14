from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures" / "UI" / "WNG" / "Conduits"
OUT.mkdir(parents=True, exist_ok=True)

def make_icon(family, kind):
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle((48,111,208,151), radius=14, fill=(0,0,0,100))
    shadow = shadow.filter(ImageFilter.GaussianBlur(9))
    img.alpha_composite(shadow)
    d = ImageDraw.Draw(img)

    if family == "Asuran":
        edge=(38,55,62,255); body=(200,214,216,255); hi=(236,245,244,255)
        line=(68,198,231,255) if kind == "Power" else (82,154,236,255)
        d.rounded_rectangle((52,104,204,148),radius=9,fill=edge)
        d.rounded_rectangle((57,109,199,143),radius=7,fill=body)
        d.line((68,114,188,114),fill=hi,width=4)
        d.line((70,130,186,130),fill=(105,122,126,255),width=3)
        d.rounded_rectangle((72,121,184,132),radius=4,fill=(54,77,83,255))
        d.line((82,126,174,126),fill=line,width=5)
        d.polygon([(128,111),(141,126),(128,141),(115,126)],fill=edge)
        d.polygon([(128,116),(136,126),(128,136),(120,126)],fill=line)

    elif family == "Goauld":
        edge=(46,31,19,255); body=(100,67,34,255); gold=(184,132,51,255)
        line=(255,174,49,255) if kind == "Power" else (230,103,38,255)
        d.rounded_rectangle((52,106,204,148),radius=7,fill=edge)
        d.rounded_rectangle((58,111,198,143),radius=5,fill=body)
        for x in (70,92,164,186):
            d.rectangle((x,112,x+6,142),fill=gold)
        d.rectangle((66,122,190,132),fill=(52,35,22,255))
        d.line((72,127,184,127),fill=line,width=5)
        d.polygon([(128,108),(145,127),(128,146),(111,127)],fill=edge)
        d.polygon([(128,114),(139,127),(128,140),(117,127)],fill=gold)
        d.ellipse((122,121,134,133),fill=line)

    else:
        deep=(45,20,42,255); flesh=(94,46,82,255); rib=(143,84,122,255)
        line=(74,219,122,255) if kind == "Fuel" else (107,190,230,255)
        d.rounded_rectangle((50,110,206,146),radius=17,fill=deep)
        d.rounded_rectangle((56,115,200,141),radius=13,fill=flesh)
        for x in range(66,197,20):
            d.arc((x-8,110,x+9,147),70,290,fill=rib,width=4)
        d.line((68,128,188,128),fill=(31,18,29,255),width=8)
        d.line((72,128,184,128),fill=line,width=4)
        d.ellipse((112,108,144,146),fill=deep,outline=rib,width=4)
        d.ellipse((120,116,136,138),fill=(43,30,39,255),outline=line,width=3)

    px = img.load()
    for y in range(S):
        for x in range(S):
            if px[x,y][3] == 0:
                px[x,y] = (0,0,0,0)
    return img.resize((128,128), Image.Resampling.LANCZOS)

icons = [
    ("Wraith","Power","WNG_WraithNeuralConduit_MenuIcon.png"),
    ("Wraith","Fuel","WNG_WraithFuelConduit_MenuIcon.png"),
    ("Asuran","Power","WNG_AsuranPowerConduit_MenuIcon.png"),
    ("Asuran","Fuel","WNG_AsuranFuelConduit_MenuIcon.png"),
    ("Goauld","Power","WNG_GoauldPowerConduit_MenuIcon.png"),
    ("Goauld","Fuel","WNG_GoauldFuelConduit_MenuIcon.png"),
]

for family, kind, filename in icons:
    path = OUT / filename
    make_icon(family, kind).save(path, "PNG", optimize=True)
    with Image.open(path) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (128,128) or check.getchannel("A").getbbox() is None:
            raise RuntimeError(f"{path}: invalid conduit menu icon")

print(f"Generated {len(icons)} single-piece WNG conduit menu icons.")
