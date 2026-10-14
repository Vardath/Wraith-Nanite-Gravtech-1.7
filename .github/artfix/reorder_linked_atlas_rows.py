from pathlib import Path
from PIL import Image

# RimWorld MaterialAtlasPool maps link indices 0..15 from the BOTTOM texture row upward:
# Up=1, Right=2, Down=4, Left=8.  Historical WNG art was produced by more than one
# generator, so some atlases are already authored in that bottom-origin order while others
# are ordinary top-down row-major.  Detect the directional order first and ONLY reverse the
# four atlas row bands when doing so converts a wrong atlas into the canonical RimWorld order.
#
# This never mirrors, rotates, redraws, rescales or interpolates approved art.

ATLAS_PATHS = [
    Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldHull_Atlas.png"),
    Path("Textures/Things/Building/Precursor/Gravship/WNG_PrecursorHull_Atlas.png"),
    Path("Textures/Things/Building/Wraith/Gravship/WNG_OrganicHull_Atlas.png"),
    Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldPowerConduit_Atlas.png"),
    Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldFuelConduit_Atlas.png"),
    Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranPowerConduit_Atlas.png"),
    Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranFuelConduit_Atlas.png"),
    Path("Textures/Things/Building/Wraith/Gravship/WNG_WraithNeuralConduit_Atlas.png"),
    Path("Textures/Things/Building/Wraith/Gravship/WNG_WraithFuelConduit_Atlas.png"),
]

DIRECTION_BITS = (
    (1, "up"),
    (2, "right"),
    (4, "down"),
    (8, "left"),
)

EXPECTED = {
    index: {name for bit, name in DIRECTION_BITS if index & bit}
    for index in range(16)
}


def edge_links(image: Image.Image, index: int) -> set[str]:
    width, height = image.size
    if width % 4 or height % 4:
        raise ValueError(f"atlas dimensions are not divisible by four: {image.size}")

    cell_w, cell_h = width // 4, height // 4
    col = index % 4
    row_from_top = 3 - (index // 4)

    # MaterialAtlasPool samples the central 3/4 of each nominal atlas cell:
    # 1/32 full-texture padding on either side of each 1/4 cell.
    pad_x, pad_y = cell_w // 8, cell_h // 8
    x0 = col * cell_w + pad_x
    x1 = (col + 1) * cell_w - pad_x
    y0 = row_from_top * cell_h + pad_y
    y1 = (row_from_top + 1) * cell_h - pad_y

    alpha = image.getchannel("A")
    band = max(2, min(cell_w, cell_h) // 48)
    boxes = {
        "up": (x0, y0, x1, min(y1, y0 + band)),
        "right": (max(x0, x1 - band), y0, x1, y1),
        "down": (x0, max(y0, y1 - band), x1, y1),
        "left": (x0, y0, min(x1, x0 + band), y1),
    }

    links = set()
    for direction, box in boxes.items():
        crop = alpha.crop(box)
        active = sum(1 for value in crop.getdata() if value > 24)
        if active >= max(2, band):
            links.add(direction)
    return links


def canonical(image: Image.Image) -> bool:
    return all(edge_links(image, index) == expected for index, expected in EXPECTED.items())


def reverse_row_bands(image: Image.Image) -> Image.Image:
    width, height = image.size
    if height % 4:
        raise ValueError(f"atlas height is not divisible by four: {image.size}")
    band_h = height // 4
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    for source_row in range(4):
        band = image.crop((0, source_row * band_h, width, (source_row + 1) * band_h))
        out.paste(band, (0, (3 - source_row) * band_h))
    return out


def repair(path: Path) -> None:
    if not path.exists():
        raise FileNotFoundError(path)

    with Image.open(path) as src:
        image = src.convert("RGBA")

    if canonical(image):
        print(f"already canonical: {path}")
        return

    candidate = reverse_row_bands(image)
    if not canonical(candidate):
        before = {index: sorted(edge_links(image, index)) for index in EXPECTED}
        after = {index: sorted(edge_links(candidate, index)) for index in EXPECTED}
        raise ValueError(
            f"{path}: neither current nor row-reversed atlas matches RimWorld link directions; "
            f"current={before}, reversed={after}"
        )

    candidate.save(path, format="PNG", optimize=True)
    print(f"repaired linked-atlas row order: {path} ({image.size[0]}x{image.size[1]})")


for atlas_path in ATLAS_PATHS:
    repair(atlas_path)
