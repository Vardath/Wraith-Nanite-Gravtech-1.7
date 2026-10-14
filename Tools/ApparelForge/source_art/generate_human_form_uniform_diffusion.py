from pathlib import Path
import os, random
import torch
from PIL import Image
from diffusers import AutoPipelineForText2Image
from rembg import remove

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "ArtSource" / "Apparel" / "human_form_uniform"
OUT.mkdir(parents=True, exist_ok=True)

MODEL = os.environ.get("WNG_STEP5_MODEL", "stabilityai/sd-turbo")
device = "cpu"
dtype = torch.float32

pipe = AutoPipelineForText2Image.from_pretrained(
    MODEL,
    torch_dtype=dtype,
    use_safetensors=True,
)
pipe = pipe.to(device)
pipe.enable_attention_slicing()

BASE = """Professional game asset painting for a RimWorld mod, Stargate Atlantis Asuran / human-form Replicator uniform.
A single isolated garment only, no person, no mannequin, no head, no hands, no legs, no body visible.
High-end hand-painted sci-fi costume art, materially realistic at game-sprite scale, rich textile depth, soft folds, stitching, rubbed edges, subtle wear, nuanced broad-form lighting, readable silhouette.
Design language: Ancient/Asuran precision, tailored fitted uniform, pale silver-grey engineered woven outer cloth, dark graphite technical underlayer, restrained blue-grey accents, high collar, subtle aged gunmetal fasteners, fine seam construction.
It must look like real fabricated clothing, not armor plates, not geometric panels, not a vector icon, not a cartoon, not a superhero suit, not generic marine armor.
No glowing outlines, no neon piping, no hard white border, no ship parts, no background scenery.
Centered, orthographic garment study, neutral plain studio background, full garment visible, production-quality concept painting."""

PROMPTS = {
    "south": BASE + "\nFront / south orthographic view. Strong tailored chest and shoulder construction, subtle asymmetry and realistic sewn fabric.",
    "north": BASE + "\nBack / north orthographic view. Clear rear yoke, tailored back seams, cloth tension and restrained fastening details.",
    "east": BASE + "\nRight side / east orthographic view. True side profile, realistic garment thickness, collar and side tailoring visible.",
}

NEG = """human, person, mannequin, face, head, arms, hands, legs, trousers, boots, character, body,
flat vector, cel shading, cartoon, icon, UI icon, panel armor, segmented armor, white plastic armor,
power armor, marine armor, superhero, glowing cyan lines, neon, outline, hard border, geometric plates,
ship hull, vehicle parts, weapon, scenery, floor, dramatic background, text, logo, watermark"""

SEEDS = {"south": 73141, "north": 73157, "east": 73171}

def clean_and_place(im: Image.Image) -> Image.Image:
    # Real model artwork first; background removal is only post-processing.
    cut = remove(im.convert("RGBA"))
    a = cut.getchannel("A")
    bb = a.getbbox()
    if not bb:
        raise RuntimeError("background removal produced empty master")
    crop = cut.crop(bb)
    # Keep breathing room so ApparelForge has clean source edges.
    target = 900
    ratio = min(target / crop.width, target / crop.height)
    crop = crop.resize((max(1,int(crop.width*ratio)), max(1,int(crop.height*ratio))), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (1024,1024), (0,0,0,0))
    canvas.alpha_composite(crop, ((1024-crop.width)//2, (1024-crop.height)//2))
    return canvas

for view in ("south","north","east"):
    gen = torch.Generator(device="cpu").manual_seed(SEEDS[view])
    result = pipe(
        prompt=PROMPTS[view],
        negative_prompt=NEG,
        num_inference_steps=6,
        guidance_scale=1.4,
        height=512,
        width=512,
        generator=gen,
    ).images[0]
    master = clean_and_place(result)
    path = OUT / f"master_{view}.png"
    master.save(path, optimize=True)
    print("generated", path, master.size)
