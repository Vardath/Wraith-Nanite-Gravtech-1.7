#!/usr/bin/env python3
"""Wraith Living Wall: vanilla-like wall construction with Stargate Wraith skin.

Authoritative art source for the Wraith settlement wall only. The native linked states use a shared chitin/membrane material, except the
four independently approved masks 00, 01, 02 and 03. A raised lip and dark face appear ONLY on exposed wall edges,
never at wall-to-wall joins. No centre emblems or free-standing sprite shapes.

384px sampled tiles / 64px UV gutters / 512px atlas cells / 2048px atlas.
In-game ThingDef is an unchanged 1x1 Wall with CornerFiller, Wall/Rock links,
roof/room/door collision, regeneration, and other vanilla mechanics.

New original material: ArtSource/Walls/Wraith/OriginalWraithMaterial.svg.
Never use earlier portrait-like image generations, ornaments or procedural
striped experiments. All other wall families remain unchanged.
"""
from __future__ import annotations
import argparse
import math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ATLAS = ROOT / "Textures/Things/Building/Wraith/Structure/WNG_LivingWall_Atlas.png"
ICON = ROOT / "Textures/UI/WNG/Build/WNG_WraithLivingWall.png"
LEGACY = ROOT / "Textures/Things/Building/Wraith/Structure/WNG_LivingWall.png"
S, STRIDE, INSET, OPEN_MARGIN = 384, 512, 64, 12
APPROVED_MASK00 = ROOT / "ArtSource/Walls/Wraith/Approved/WraithLivingWall_link00_approved.jpg"
APPROVED_MASK01 = ROOT / "ArtSource/Walls/Wraith/Approved/WraithLivingWall_link01_approved.png"
APPROVED_MASK02 = ROOT / "ArtSource/Walls/Wraith/Approved/WraithLivingWall_link02_approved.png"
APPROVED_MASK03 = ROOT / "ArtSource/Walls/Wraith/Approved/WraithLivingWall_link03_approved.png"


def save(image: Image.Image, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    image.save(dest, format="PNG", optimize=True)


def membrane_material() -> Image.Image:
    """Render newly painted original chitin/membrane, NEVER old PNG/SVG art."""
    import cairosvg
    import io
    material = ROOT / "ArtSource/Walls/Wraith/OriginalWraithMaterial.svg"
    if not material.is_file():
        raise FileNotFoundError("New original Wraith wall master missing: " + str(material))
    raw = cairosvg.svg2png(bytestring=material.read_bytes(),
                          output_width=768, output_height=768)
    image = Image.open(io.BytesIO(raw)).convert("RGBA").resize(
        (S, S), Image.Resampling.LANCZOS)
    # All linked faces must have the *same* actual material at their borders.
    # Feather artwork only in the extreme 7px band. Never fabricate a framed
    # endpiece or use cropped old decorative medallions.
    rgba = np.asarray(image).copy()
    for i in range(7):
        alpha = i / 7.
        rgba[:, i, :3] = (rgba[:, i, :3] * alpha +
                          rgba[:, -8+i, :3] * (1-alpha)).astype(np.uint8)
    rgba[:, -7:, :3] = rgba[:, :7, :3]
    for i in range(7):
        alpha = i / 7.
        rgba[i, :, :3] = (rgba[i, :, :3] * alpha +
                          rgba[-8+i, :, :3] * (1-alpha)).astype(np.uint8)
    rgba[-7:, :, :3] = rgba[:7, :, :3]
    rgba[:, :, 3] = 255
    return Image.fromarray(rgba, "RGBA")


def linked_wall_tile(mask: int, material: Image.Image) -> Image.Image:
    """Native 1x1 wall slab; bone bevels only on *unlinked* external faces."""
    n, e, s, w = [not bool(mask & bit) for bit in (1, 2, 4, 8)]
    inset = 6
    left = inset if w else 0
    right = S-inset if e else S
    top = inset if n else 0
    bottom = S-inset if s else S
    out = Image.new("RGBA", (S, S))
    brush = ImageDraw.Draw(out)
    if n:
        brush.polygon([(left,top),(right,top),(right,top+7),(left,top+7)],fill=(171,145,153,142))
        brush.line([(left+1,top+2),(right-2,top+2)],fill=(221,189,177,192),width=2)
        brush.line([(left+1,top+9),(right-1,top+9)],fill=(32,24,39,144),width=3)
    if s:
        brush.polygon([(left,bottom-15),(right,bottom-15),(right,bottom),(left,bottom)],fill=(20,18,31,150))
        brush.line([(left+2,bottom-15),(right-2,bottom-15)],fill=(171,134,145,164),width=2)
        brush.line([(left+2,bottom-4),(right-2,bottom-4)],fill=(13,10,21,144),width=3)
    if w:
        brush.polygon([(left,top),(left+9,top),(left+9,bottom),(left,bottom)],fill=(173,151,149,100))
        brush.line([(left+2,top+2),(left+2,bottom-2)],fill=(215,189,169,161),width=2)
        brush.line([(left+11,top+1),(left+11,bottom-1)],fill=(32,26,41,109),width=3)
    if e:
        brush.polygon([(right-12,top),(right,top),(right,bottom),(right-12,bottom)],fill=(30,23,41,114))
        brush.line([(right-12,top+1),(right-12,bottom-1)],fill=(168,136,150,144),width=2)
        brush.line([(right-3,top+2),(right-3,bottom-2)],fill=(14,11,22,133),width=3)
    tile = np.asarray(Image.alpha_composite(material, out)).copy()
    if n: tile[:inset,:,3] = 0
    if s: tile[-inset:,:,3] = 0
    if w: tile[:,:inset,3] = 0
    if e: tile[:,-inset:,3] = 0
    return Image.fromarray(tile, "RGBA")



def approved_isolated_wall() -> Image.Image:
    """Render approved, high-resolution isolated Wraith wall artwork, mask 00 only.

    The uploaded reference is an opaque JPEG with black outer background;
    reconstruct transparency before fitting it into the linked-wall sample.
    """
    source = Image.open(APPROVED_MASK00).convert("RGB")
    rgb = np.asarray(source)
    # Detect the artwork rather than treating its black outside as an opaque tile.
    visible = np.max(rgb, axis=2) > 17
    if not visible.any():
        raise ValueError("Approved Wraith wall has no visible artwork")
    # The 1x1 wall is a continuous solid object. Close black internal gaps
    # horizontally while keeping all outside rows transparent.
    filled = (np.maximum.accumulate(visible, axis=1) &
              np.maximum.accumulate(visible[:, ::-1], axis=1)[:, ::-1])
    ys, xs = np.where(filled)
    bounds = (int(xs.min()), int(ys.min()),
              int(xs.max()) + 1, int(ys.max()) + 1)
    alpha = Image.fromarray((filled.astype(np.uint8) * 255), "L").filter(
        ImageFilter.GaussianBlur(0.65))
    painted = source.convert("RGBA")
    painted.putalpha(alpha)
    crop = painted.crop(bounds)
    # The new approved figure is intrinsically wider than the prior piece,
    # but still stays well inside RimWorld's 192x192 sampled 1x1 wall region.
    footprint = 144
    ratio = min(footprint / crop.width, footprint / crop.height)
    size = (round(crop.width * ratio), round(crop.height * ratio))
    scaled = crop.resize(size, Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (S, S))
    out.paste(scaled, ((S - size[0]) // 2, (S - size[1]) // 2))
    return out


def approved_north_connected_wall() -> Image.Image:
    """Piece 01, north-connected/south-exposed, using the user's approved painting.

    Match the measured vanilla GrayWall mask01 occupancy: 55px wide by 67px
    tall inside an 80px reference cell. Unlike isolated mask00, the top of
    the wall reaches the sample's northern edge, with no extra wall cap.
    """
    source = Image.open(APPROVED_MASK01).convert("RGB")
    pixels = np.asarray(source)
    # Original artwork is high-resolution RGB with black outside, not alpha.
    visible = np.max(pixels, axis=2) > 17
    if not visible.any():
        raise ValueError("Approved piece01 contains no visible wall painting")
    # Fill internal dark recesses between left/right painted edges, preserving
    # organic outside contours and avoiding a black opaque canvas square.
    filled = (np.maximum.accumulate(visible, axis=1) &
              np.maximum.accumulate(visible[:, ::-1], axis=1)[:, ::-1])
    ys, xs = np.where(filled)
    bounds = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    alpha = Image.fromarray((filled.astype(np.uint8) * 255), "L").filter(
        ImageFilter.GaussianBlur(0.45))
    painted = source.convert("RGBA")
    painted.putalpha(alpha)
    target_width, target_height = round(S * 55 / 80), round(S * 67 / 80)
    native = painted.crop(bounds).resize(
        (target_width, target_height), Image.Resampling.LANCZOS)
    tile = Image.new("RGBA", (S, S))
    tile.paste(native, ((S - target_width) // 2, 0))
    return tile


def approved_east_connected_wall() -> Image.Image:
    """Approved mask 02: exposed west terminus with an east-side wall join.

    Preserve the approved 192x192 RGBA raster pixel-for-pixel.  The artwork
    already contains its measured wall footprint, edge shading and connection
    profile.  No rotations, cropping or generic linked-tile repainting.
    """
    if not APPROVED_MASK02.is_file():
        raise FileNotFoundError("Approved Wraith wall mask02 missing: " + str(APPROVED_MASK02))
    with Image.open(APPROVED_MASK02) as source:
        tile = source.convert("RGBA")
    if tile.size != (S, S):
        raise ValueError(f"Approved mask02 must be {S}x{S} RGBA, got {tile.size}")
    if tile.getbbox() is None or tile.getbbox()[2] != S:
        raise ValueError("Approved mask02 east connection does not reach atlas sample edge")
    return tile


def approved_northeast_corner() -> Image.Image:
    """Approved mask 03 (N+E): leave west and south exposed.

    The source is a 192x192 RGBA tile prepared from the separately approved
    square master with native north/east connection profiles. No recoloring,
    rotation or recomputation from any unapproved art is permitted.
    """
    if not APPROVED_MASK03.is_file():
        raise FileNotFoundError("Approved Wraith wall mask03 missing: " + str(APPROVED_MASK03))
    with Image.open(APPROVED_MASK03) as source:
        tile = source.convert("RGBA")
    if tile.size != (S, S):
        raise ValueError(f"Approved mask03 must be {S}x{S} RGBA, got {tile.size}")
    alpha = np.asarray(tile.getchannel("A"))
    q = S // 8
    if np.mean(alpha[:2, q:-q] >= 250) < .55:
        raise ValueError("Approved mask03 north connector does not meet the tile edge")
    if np.mean(alpha[q:-q, -2:] >= 250) < .55:
        raise ValueError("Approved mask03 east connector does not meet the tile edge")
    if np.mean(alpha[-2:, q:-q] >= 250) > .35:
        raise ValueError("Approved mask03 south face must remain exposed")
    if np.mean(alpha[q:-q, :2] >= 250) > .35:
        raise ValueError("Approved mask03 west face must remain exposed")
    return tile


def generate() -> list[Image.Image]:
    """Rebuild all sixteen pieces against the measured native wall geometry.

    Authentic approved paintings are preserved on disk as immutable art sources.
    The runtime atlas is new authored chitin/membrane, not isolated ornaments.
    """
    from wng_true_wall import approved_source, generate as build, pack
    pieces = build(approved_source(ROOT))
    save(pack(pieces), ATLAS)
    save(pieces[0], ICON)
    save(pieces[0], LEGACY)
    return pieces


def preview(states: list[Image.Image],folder: Path) -> None:
    folder.mkdir(parents=True,exist_ok=True)
    scene=[
      "..#########.......",
      "..#.......#.......",
      "..#.####..#.......",
      "..#.#..#..#.......",
      "..#.####..#.......",
      "..#.......#.......",
      "..#########.......",
      "......#...........",
      "....#####.........",
      "......#...........",
    ]
    ts=48
    out=Image.new("RGBA",(len(scene[0])*ts,len(scene)*ts),(84,91,84,255))
    d=ImageDraw.Draw(out)
    for y,line in enumerate(scene):
        for x in range(len(line)):
            d.rectangle((x*ts,y*ts,(x+1)*ts-1,(y+1)*ts-1),
                       fill=(88,94,88,255) if (x+y)%2==0 else (82,88,82,255))
    for y,line in enumerate(scene):
        for x,c in enumerate(line):
            if c!="#": continue
            links=sum(bit for bit,dx,dy in ((1,0,-1),(2,1,0),(4,0,1),(8,-1,0))
                      if 0<=y+dy<len(scene) and 0<=x+dx<len(scene[y+dy])
                      and scene[y+dy][x+dx]=="#")
            out.alpha_composite(states[links].resize((ts,ts),Image.Resampling.LANCZOS),
                                (x*ts,y*ts))
    save(out,folder/"Wraith-Living-Wall-Room-Test.png")
    sheet=Image.new("RGBA",(4*S,4*S))
    for mask,t in enumerate(states):
        sheet.alpha_composite(t,((mask%4)*S,(3-mask//4)*S))
    save(sheet,folder/"Wraith-Living-Wall-16-States.png")
    # Inspectable game-zoom contact sheet, rendered FROM the real mod tiles.
    save(sheet.resize((768,768),Image.Resampling.LANCZOS),
         folder/"Wraith-Living-Wall-16-States-Review.png")


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument("--preview",type=Path)
    args=parser.parse_args()
    states=generate()
    if args.preview: preview(states,args.preview)
    coverage=[float((np.asarray(t)[:,:,3]>=250).mean()) for t in states]
    assert len(states)==16
    assert states[0].size == (384,384), 'HD wall generation must use a double-sized UV sample'
    # Native 192px wall sample, 9px inset for every exposed side.
    assert abs(coverage[0] - (174*174)/(192*192)) <= .005, coverage[0]
    assert abs(coverage[1] - (174*183)/(192*192)) <= .005, coverage[1]
    assert abs(coverage[2] - (183*174)/(192*192)) <= .005, coverage[2]
    assert abs(coverage[3] - (183*183)/(192*192)) <= .005, coverage[3]
    assert coverage[15] >= .995, coverage[15]
    print("Wraith settlement mod atlas committed path:",ATLAS)
    print("Coverage isolated:",round(coverage[0],4),"all linked:",round(coverage[15],4))


if __name__=="__main__":
    main()
