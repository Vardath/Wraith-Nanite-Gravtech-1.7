#!/usr/bin/env python3
"""Validate WNG's linked wall textures using RimWorld's actual sampled UVs.

Run from any directory: python Tools/WallArt/validate_wall_art.py
Requires Pillow and NumPy. --json PATH saves the inventory and measured evidence.

This checks deliverable assets and XML inheritance contracts, not the art generator.
Native reference: Verse.MaterialAtlasPool, Graphic_LinkedCornerFiller and
LinkDirections in RimWorld 1.6. UV rows begin at the bottom in Unity; PNG rows
begin at the top. An atlas cell includes 1/32-atlas padding on each side, so
only 3/16 of the atlas width is actually printed onto one full map tile.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image


WALL_NATIVE = ET.fromstring("""<ThingDef Name="Wall">
<thingClass>Building</thingClass><category>Building</category>
<altitudeLayer>Building</altitudeLayer><passability>Impassable</passability>
<fillPercent>1</fillPercent><holdsRoof>true</holdsRoof>
<blockLight>true</blockLight><blockWind>true</blockWind>
<castEdgeShadows>true</castEdgeShadows><coversFloor>true</coversFloor>
<rotatable>false</rotatable><size>(1,1)</size>
<graphicData><graphicClass>Graphic_Appearances</graphicClass>
<linkType>CornerFiller</linkType><linkFlags><li>Wall</li><li>Rock</li></linkFlags>
<drawSize>(1,1)</drawSize><drawOffset>(0,0,0)</drawOffset>
<damageData><cornerTL>Damage/Corner</cornerTL><cornerTR>Damage/Corner</cornerTR>
<cornerBL>Damage/Corner</cornerBL><cornerBR>Damage/Corner</cornerBR>
<edgeTop>Damage/Edge</edgeTop><edgeBot>Damage/Edge</edgeBot>
<edgeLeft>Damage/Edge</edgeLeft><edgeRight>Damage/Edge</edgeRight></damageData></graphicData>
<replaceTags><li>Wall</li></replaceTags>
<building><isWall>true</isWall><isInert>true</isInert>
<paintable>true</paintable>
<isPlaceOverableWall>true</isPlaceOverableWall>
<supportsWallAttachments>true</supportsWallAttachments>
<ai_chillDestination>false</ai_chillDestination>
<blueprintGraphicData><texPath>Things/Building/Linked/Wall_Blueprint_Atlas</texPath></blueprintGraphicData>
</building></ThingDef>""")

DOOR_NATIVE = ET.fromstring("""<ThingDef Name="DoorBase">
<thingClass>Building_Door</thingClass><holdsRoof>true</holdsRoof>
<building><canPlaceOverWall>true</canPlaceOverWall>
<blueprintClass>Blueprint_Door</blueprintClass></building>
<placeWorkers><li>PlaceWorker_DoorLearnOpeningSpeed</li></placeWorkers>
</ThingDef>""")

FAMILIES = {
    "Wraith": {
        "walls": ["WNG_WraithLivingWall", "WNG_OrganicGravshipHull"],
        "atlas": "Things/Building/Wraith/Gravship/WNG_OrganicHull_Atlas",
    },
    "Precursor": {
        "walls": ["WNG_PrecursorWall", "WNG_PrecursorGravshipHull"],
        "atlas": "Things/Building/Precursor/Gravship/WNG_PrecursorHull_Atlas",
    },
    "Goauld": {
        "walls": ["WNG_GoauldGravshipHull"],
        "atlas": "Things/Building/Goauld/Gravship/WNG_GoauldHull_Atlas",
    },
}

# These are native Wall hooks that fixed-material WNG descendants must retain.
REQUIRED_HOOKS = {
    "thingClass": "Building", "category": "Building",
    "altitudeLayer": "Building", "passability": "Impassable",
    "fillPercent": "1", "holdsRoof": "true", "blockLight": "true",
    "blockWind": "true", "castEdgeShadows": "true", "coversFloor": "true",
    "rotatable": "false", "size": "(1,1)",
    "building/isWall": "true", "building/isInert": "true",
    "building/isPlaceOverableWall": "true",
    "building/supportsWallAttachments": "true",
    "building/ai_chillDestination": "false",
    "building/paintable": "true",
}


@dataclass
class Definition:
    node: ET.Element
    source: str

    @property
    def name(self):
        return self.node.findtext("defName") or self.node.get("Name", "<anonymous>")


class Validator:
    def __init__(self, repo: Path):
        self.repo = repo
        self.errors = []
        self.warnings = []
        self.report = {"walls": [], "atlases": [], "corners": [], "doors": [], "icons": []}
        self.named = {"Wall": WALL_NATIVE, "DoorBase": DOOR_NATIVE, "Door": DOOR_NATIVE}
        self.definitions = []
        self.textures = {}

    def check(self, condition, message):
        if not condition:
            self.errors.append(message)

    def load_definitions(self):
        for path in sorted((self.repo / "Defs").rglob("*.xml")):
            for node in ET.parse(path).getroot().findall("ThingDef"):
                self.definitions.append(Definition(node, str(path.relative_to(self.repo))))
                if node.get("Name"):
                    self.named[node.get("Name")] = node

    def chain(self, node):
        result = [node]
        seen = set()
        while node.get("ParentName"):
            parent = node.get("ParentName")
            if parent in seen:
                self.errors.append(f"Circular ThingDef inheritance: {parent}")
                break
            seen.add(parent)
            node = self.named.get(parent)
            if node is None:
                break
            result.append(node)
        return result

    def effective(self, node, path, default=None):
        """Resolve a declared field; Inherit=False on an ancestor group cuts it off."""
        groups = path.split("/")
        for ancestor in self.chain(node):
            value = ancestor.find(path)
            if value is not None:
                return value
            for i in range(1, len(groups)):
                group = ancestor.find("/".join(groups[:i]))
                if group is not None and group.get("Inherit", "true").lower() == "false":
                    return default
        return default

    def value(self, node, path, default=None):
        field = self.effective(node, path)
        return field.text.strip() if field is not None and field.text else default

    def items(self, node, path):
        field = self.effective(node, path)
        return [item.text.strip() for item in field.findall("li") if item.text] if field is not None else []

    def texture(self, tex_path):
        if tex_path in self.textures:
            return self.textures[tex_path]
        path = self.repo / "Textures" / f"{tex_path}.png"
        if not path.exists():
            self.errors.append(f"Missing texture: {path.relative_to(self.repo)}")
            return None
        with Image.open(path) as image:
            self.check(image.mode == "RGBA", f"{tex_path}: texture must preserve RGBA transparency")
            self.textures[tex_path] = np.asarray(image.convert("RGBA"))
            return self.textures[tex_path]

    def walls(self):
        expected = {name: info["atlas"] for info in FAMILIES.values() for name in info["walls"]}
        expected["WNG_WraithLivingWall"] = "Things/Building/Wraith/Structure/WNG_LivingWall_Atlas"
        discovered = set()
        for definition in self.definitions:
            node = definition.node
            if node.get("Abstract", "false").lower() == "true":
                continue
            native_wall = any(item.get("Name") == "Wall" for item in self.chain(node)[1:])
            if not native_wall and self.value(node, "building/isWall") != "true":
                continue
            name = definition.name
            self.check(name not in discovered, f"Duplicate functional wall defName: {name}")
            discovered.add(name)
            self.check(native_wall, f"{name}: must inherit native Wall hooks")
            for field, wanted in REQUIRED_HOOKS.items():
                actual = self.value(node, field)
                normalized = re.sub(r"\s", "", actual or "")
                self.check(normalized == wanted, f"{name}: {field}={actual!r}; native wall requires {wanted}")
            self.check("Wall" in self.items(node, "replaceTags"), f"{name}: missing native Wall replacement tag")
            self.check(bool(self.value(node, "building/blueprintGraphicData/texPath")), f"{name}: missing inherited wall blueprint hook")
            self.check(self.value(node, "graphicData/graphicClass") == "Graphic_Single", f"{name}: authored atlas needs Graphic_Single")
            self.check(self.value(node, "graphicData/linkType") == "CornerFiller", f"{name}: missing native diagonal CornerFiller hook")
            self.check(set(self.items(node, "graphicData/linkFlags")) == {"Wall", "Rock"}, f"{name}: wall/rock edge link flags changed")
            self.check(self.value(node, "building/isAirtight") == "true", f"{name}: fixed-material wall needs isAirtight=true; stuffable airtightness needs stuff")
            self.check(self.value(node, "graphicData/damageData/enabled", "true") == "true", f"{name}: native wall damage drawing disabled")
            for edge in ("cornerTL", "cornerTR", "cornerBL", "cornerBR", "edgeTop", "edgeBot", "edgeLeft", "edgeRight"):
                wanted = "Damage/Corner" if edge.startswith("corner") else "Damage/Edge"
                self.check(self.value(node, f"graphicData/damageData/{edge}") == wanted, f"{name}: missing native {edge} damage hook")
            # Graphic_Linked prints a native 1x1 plane regardless of drawSize.
            self.check(re.sub(r"\s", "", self.value(node, "graphicData/drawOffset", "(0,0,0)")) == "(0,0,0)", f"{name}: graphic offset breaks native grid alignment")
            path = self.value(node, "graphicData/texPath")
            if name in expected:
                self.check(path == expected[name], f"{name}: settlement/hull family texture diverged")
            else:
                self.errors.append(f"Uninventoried functional wall: {name}; include its art family in this validator")
            icon = self.value(node, "uiIconPath", "")
            self.check(icon.startswith("UI/WNG/Build/"), f"{name}: needs a dedicated authored wall icon")
            self.report["walls"].append({"defName": name, "source": definition.source, "atlas": path, "icon": icon})
        self.check(set(expected) <= discovered, f"Missing wall definitions: {sorted(set(expected) - discovered)}")

    def atlas(self, family, path):
        rgba = self.texture(path)
        if rgba is None:
            return
        height, width = rgba.shape[:2]
        self.check(width == height and width % 32 == 0 and width >= 512, f"{path}: atlas must be square, >=512px and divisible by32")
        if width != height or width % 32:
            return
        stride, inset, sample = width // 4, width // 32, 3 * width // 16
        tile_reports = []
        cores = []
        for mask in range(16):
            x, y = mask % 4 * stride, (3 - mask // 4) * stride
            cell = rgba[y:y + stride, x:x + stride]
            core = cell[inset:inset + sample, inset:inset + sample]
            cores.append(core)
            alpha = core[..., 3]
            quarter = sample // 8
            opaque = alpha >= 250
            coverage = float(opaque.mean())
            center = float(opaque[quarter:-quarter, quarter:-quarter].mean())
            if family == "Wraith settlement":
                # Actual outer silhouettes measured from the ReBuild copies of
                # vanilla's Smooth, Bricks and Planks 1024px wall atlases.
                # Nine of every 192 sample pixels of transparent setback on each
                # exposed side; connected sides extend all the way across.
                setback = round(sample * 9 / 192)
                lx = 0 if mask & 8 else setback
                rx = sample if mask & 2 else sample-setback
                ty = 0 if mask & 1 else setback
                by = sample if mask & 4 else sample-setback
                expected_alpha = np.zeros((sample,sample),dtype=bool)
                expected_alpha[ty:by,lx:rx] = True
                self.check(np.array_equal(opaque,expected_alpha),
                           f"{family} mask{mask}: external dimensions disagree with measured vanilla {rx-lx}x{by-ty} wall")
                self.check(center >= .995,
                           f"{family} mask{mask}: insufficient real solid wall mass ({center:.1%})")
                material_std = float(core[32:-32,32:-32,:3].astype(float).std(axis=(0,1)).mean())
                self.check(material_std >= 6.5,
                           f"{family} mask{mask}: painted bone/membrane lacks material relief ({material_std:.1f})")
                if not (mask & 4):
                    factor = sample/192
                    span = lambda lo,hi: slice(round(lo*factor),round(hi*factor))
                    roof = core[span(55,90),span(65,127),:3].astype(float)
                    face = core[span(133,168),span(65,127),:3].astype(float)
                    self.check(abs(float(roof.mean()-face.mean())) >= 3,
                               f"{family} mask{mask}: missing the measured deep south-facing facade")
                if mask == 15:
                    self.check(coverage >= .995, f"{family}: all-linked slab must occupy complete sample")
            else:
                self.check(center >= .995, f"{family} mask{mask}: tile center opacity {center:.1%}; must fill tile body")
                self.check(coverage >= .80, f"{family} mask{mask}: sampled coverage {coverage:.1%}; wall reads too narrow")
                if mask == 15:
                    self.check(coverage >= .995, f"{family}: all-linked tile must cover its whole native tile, got {coverage:.1%}")
            # Adjacent linked directions must form a solid quarter-tile region.
            # Native CornerFiller covers then blend over existing wall mass;
            # the underlying atlas must not leave holes at these intersections.
            half = sample // 2
            corner_quadrants = {
                "NW": (9, alpha[:half, :half]),
                "NE": (3, alpha[:half, half:]),
                "SW": (12, alpha[half:, :half]),
                "SE": (6, alpha[half:, half:]),
            }
            solid_corners = {}
            for direction, (bits, quadrant) in corner_quadrants.items():
                if mask & bits == bits:
                    fraction = float((quadrant == 255).mean())
                    if family != "Wraith settlement":
                        self.check(fraction == 1, f"{family} mask{mask} {direction}: linked corner quadrant is not fully opaque")
                    solid_corners[direction] = fraction
            edges = [opaque[:2, quarter:-quarter], opaque[quarter:-quarter, -2:], opaque[-2:, quarter:-quarter], opaque[quarter:-quarter, :2]]
            edge_coverage = [float(edge.mean()) for edge in edges]
            for bit, direction, fraction in zip((1, 2, 4, 8), "NESW", edge_coverage):
                linked = bool(mask & bit)
                if family == "Wraith settlement":
                    # Visually real walls have half-tile-thick painted connector
                    # profiles, not 100% opaque straight rectangular tile edges.
                    valid = fraction >= .55 if linked else fraction <= .35
                else:
                    valid = fraction >= .99 if linked else fraction <= .01
                self.check(valid, f"{family} mask{mask} {direction}: edge {fraction:.1%} opaque; unexpected {'linked' if linked else 'exposed'} silhouette")
            # Bilinear filtering must see clamped edge texels throughout the atlas gutter.
            padded = np.pad(core, ((inset, inset), (inset, inset), (0, 0)), mode="edge")
            self.check(np.array_equal(cell, padded), f"{family} mask{mask}: padding does not clamp sampled edge; filtering can expose seams")
            # Native CornerFiller samples local UV (.5,.6), or PNG (.5,.4).
            cx, cy = sample // 2, round(sample * .4)
            patch = core[cy - 2:cy + 3, cx - 2:cx + 3]
            self.check(np.mean(patch[..., 3] >= 250) >= (.80 if family == "Wraith settlement" else 1), f"{family} mask{mask}: native diagonal filler samples outside painted wall")
            luminance = patch[..., :3].astype(float).mean(axis=2)
            if luminance.std() > 24:
                self.warnings.append(f"{family} mask{mask}: bright detail at CornerFiller UV may produce a visible flat patch")
            tile_reports.append({"mask": mask, "png_cell": [mask % 4, 3 - mask // 4], "coverage": round(coverage, 5), "center_opacity": round(center, 5), "edge_opacity_NESW": edge_coverage, "solid_corner_quadrants": solid_corners})
        if family == "Wraith settlement":
            # Structural silhouette and complete cardinal seam assertions above
            # replace the old false assumption that separately painted pieces
            # must have byte-identical central roof art.
            self.check(len(cores) == 16, "Wraith wall must contain sixteen structural states")
        # Exercise every compatible linked neighbour pair, including T and four-way
        # junctions. Opaque edges can still show a visible seam if the art is not periodic.
        seams = []
        margin = sample // 8
        for direction, outgoing, incoming in (("E/W", 2, 8), ("S/N", 4, 1)):
            for first in range(16):
                for second in range(16):
                    if not first & outgoing or not second & incoming:
                        continue
                    if direction == "E/W":
                        a = cores[first][margin:-margin, -1]
                        b = cores[second][margin:-margin, 0]
                    else:
                        a = cores[first][-1, margin:-margin]
                        b = cores[second][0, margin:-margin]
                    delta = np.abs(a.astype(float) - b.astype(float))
                    mean, largest = float(delta[:, :3].mean()), float(delta[:, :3].max())
                    seam_ok = np.array_equal(a[:, 3], b[:, 3]) and mean <= 3 and largest <= 12
                    self.check(seam_ok, f"{family} masks{first}/{second} {direction}: linked seam differs (mean RGB {mean:.1f}, max {largest:.0f})")
                    seams.append({"direction": direction, "masks": [first, second], "mean_rgb_delta": round(mean, 4), "max_rgb_delta": largest})
        self.report["icons"].extend(self.icons(path, cores[0]))
        self.report["atlases"].append({"family": family, "path": path, "size": width, "cell": stride, "padding": inset, "sample": sample, "tiles": tile_reports, "seams": seams})

    def icons(self, atlas_path, isolated):
        result = []
        paths = {wall["icon"] for wall in self.report["walls"] if wall["atlas"] == atlas_path and wall["icon"]}
        for path in sorted(paths):
            icon = self.texture(path)
            if icon is None:
                continue
            self.check(icon.shape == isolated.shape and np.array_equal(icon, isolated), f"{path}: icon must be the isolated mask0 sampled tile, without atlas padding or resizing")
            result.append({"path": path, "size": list(icon.shape[:2]), "source_mask": 0})
        return result

    def doors(self):
        for definition in self.definitions:
            node = definition.node
            if node.get("Abstract", "false").lower() == "true":
                continue
            if definition.name not in {"WNG_WraithMembraneDoor", "WNG_PrecursorDoor", "WNG_WraithGravshipDoor", "WNG_AsuranGravshipDoor", "WNG_GoauldGravshipDoor"}:
                continue
            name = definition.name
            self.check({"Wall", "Rock"} <= set(self.items(node, "graphicData/linkFlags")), f"{name}: door cannot bridge linked wall edges")
            self.check(self.value(node, "building/canPlaceOverWall") == "true", f"{name}: missing door-over-wall replacement hook")
            self.check(self.value(node, "building/blueprintClass") == "Blueprint_Door", f"{name}: missing native door blueprint hook")
            self.check("PlaceWorker_DoorLearnOpeningSpeed" in self.items(node, "placeWorkers"), f"{name}: missing native door placement hook")
            self.report["doors"].append({"defName": name, "source": definition.source})

    def corners(self):
        source = (self.repo / "Source/WraithNaniteGravtech/Gravships/SectionLayer_WNGFamilyGravshipHull.cs").read_text()
        for family, info in FAMILIES.items():
            hull = info["walls"][-1]
            root = f"Things/Building/{family}/Gravship/HullCorners"
            self.check(f'new FamilyVisuals("{hull}", "{root}")' in source, f"{family}: missing family corner-layer registration")
            canonical = {}
            for suffix in ("northwest", "northeast", "southwest", "southeast"):
                for variant in ("", "Partial_"):
                    path = f"{root}/AngledGravshipHull_{variant}{suffix}"
                    rgba = self.texture(path)
                    if rgba is None:
                        continue
                    alpha = rgba[..., 3]
                    self.check(alpha.shape[0] == alpha.shape[1], f"{path}: corner overlay must use square native UV space")
                    self.check(np.any(alpha >= 250) and np.any(alpha == 0), f"{path}: corner overlay needs opaque structure and transparent cutout")
                    # Normalize into NW PNG coordinates. Native NW offsets put the
                    # diagonal neighbour at TL, cardinal neighbours at TR/BL and
                    # the empty exterior cell at BR. The other corners mirror it.
                    normalized = alpha
                    if suffix in ("northeast", "southeast"):
                        normalized = np.fliplr(normalized)
                    if suffix in ("southwest", "southeast"):
                        normalized = np.flipud(normalized)
                    size = normalized.shape[0]
                    def patch(x, y):
                        radius = max(1, size // 25)
                        px, py = round(x * (size - 1)), round(y * (size - 1))
                        return float((normalized[py-radius:py+radius, px-radius:px+radius] >= 250).mean())
                    self.check(patch(.25, .75) >= .95 and patch(.75, .25) >= .95, f"{path}: overlay does not reach both native cardinal neighbour tiles")
                    self.check(patch(.9, .9) <= .01, f"{path}: corner crop/orientation fills the empty exterior corner")
                    diagonal = patch(.15, .15)
                    self.check(diagonal <= .01 if variant else diagonal >= .99, f"{path}: {'partial must leave the absent diagonal tile open' if variant else 'full must fill its diagonal neighbour tile'}")
                    canonical[(variant, suffix)] = normalized
                    self.report["corners"].append({"path": path, "coverage": round(float((alpha >= 250).mean()), 5), "normalized_diagonal_opacity": diagonal})
            for variant in ("", "Partial_"):
                reference = canonical.get((variant, "northwest"))
                for direction in ("northeast", "southwest", "southeast"):
                    other = canonical.get((variant, direction))
                    if reference is not None and other is not None:
                        self.check(reference.shape == other.shape and np.mean(np.abs(reference.astype(float) - other.astype(float)) > 8) <= .015, f"{family} {variant}{direction}: mirrored corner silhouette/crop disagrees with native orientation")
            for direction in ("northwest", "northeast", "southwest", "southeast"):
                full, partial = canonical.get(("", direction)), canonical.get(("Partial_", direction))
                if full is not None and partial is not None and full.shape == partial.shape:
                    self.check(np.mean((partial.astype(float) - full.astype(float)) > 8) <= .005, f"{family} {direction}: partial silhouette extends beyond its full corner")
                    self.check(float((full >= 250).mean()) - float((partial >= 250).mean()) > .1, f"{family} {direction}: full and partial corner silhouettes collapsed into one shape")
            for name in ("SubstructureCorner_Full_west", "SubstructureCorner_Full_east", "SubstructureCorner_Tip_west", "SubstructureCorner_Tip_east"):
                self.texture(f"{root}/{name}")
        for native_hook in ("HullCornerScale = 2f", "ShouldDrawCornerPiece", "Diagonal_NW", "Diagonal_NE", "Diagonal_SW", "Diagonal_SE", "ShaderDatabase.GravshipMaskMasked", "ShaderDatabase.IndoorMaskMasked", "BakeGravshipIndoorMesh", "AltitudeLayer.MetaOverlays", "3185"):
            self.check(native_hook in source, f"Hull corner layer: missing native geometry/mask hook {native_hook}")
        bridge = (self.repo / "Source/WraithNaniteGravtech/Gravships/GravshipFamilySystems.cs").read_text()
        self.check("nameof(SectionLayer_GravshipHull.BakeGravshipIndoorMesh)" in bridge and "__result.AddRange(SectionLayer_WNGFamilyGravshipHull.BakeGravshipIndoorMesh(map, bounds, center))" in bridge, "Family hull masks must participate in the native flight/interior mesh bake")

    def run(self):
        self.load_definitions()
        self.walls()
        for family, info in FAMILIES.items():
            self.atlas(family, info["atlas"])
        self.atlas("Wraith settlement", "Things/Building/Wraith/Structure/WNG_LivingWall_Atlas")
        self.doors()
        self.corners()
        self.report["errors"] = self.errors
        self.report["warnings"] = self.warnings
        return self.report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--json", type=Path, help="Write an inspectable inventory and measured evidence.")
    arguments = parser.parse_args()
    validator = Validator(arguments.repo.resolve())
    report = validator.run()
    if arguments.json:
        arguments.json.parent.mkdir(parents=True, exist_ok=True)
        arguments.json.write_text(json.dumps(report, indent=2) + "\n")
    print(f"Inspected {len(report['walls'])} functional walls, {len(report['atlases'])} atlases, {len(report['corners'])} angled/partial overlays, {len(report['doors'])} door declarations.")
    for item in report["atlases"]:
        coverage = [tile["coverage"] for tile in item["tiles"]]
        print(f"{item['family']}: {item['size']}px atlas / {item['sample']}px sampled tile; isolated {coverage[0]:.1%}, all-linked {coverage[15]:.1%} opaque.")
    for message in validator.errors[:40]:
        print(f"FAIL: {message}")
    if len(validator.errors) > 40:
        print(f"... {len(validator.errors) - 40} further failures; use --json for complete evidence.")
    for message in validator.warnings:
        print(f"NOTE: {message}")
    print(f"{'PASS' if not validator.errors else 'FAIL'}: {len(validator.errors)} errors; {len(validator.warnings)} visual notes.")
    return bool(validator.errors)


if __name__ == "__main__":
    sys.exit(main())
