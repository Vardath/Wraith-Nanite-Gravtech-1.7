# WNG ART UPGRADES — ACTIVE HANDOFF

**Project:** Wraith-Nanite-Gravtech (WNG), RimWorld 1.6  
**Production repo:** `Vardath/Wraith-Nanite-Gravtech-1.6`  
**Branch:** `main`  
**Updated:** 2026-09-22

This is the active art-production brief. Read this file before touching WNG art.

## 2026-09-25 USER OVERRIDE — FULL COMPOUND-FORM REBUILD

The live playtest shows the current Replicators still read as one repeated machine at different scales, and the basic Drone does not visually belong to the same compound-form family. Rebuild the full block-form family together when necessary to preserve a coherent art direction.

- The **basic Replicator Drone is the canonical six-legged Stargate-style machine**.
- Larger/specialist compound forms are **not required to keep six legs**. They may use six, eight, ten or another role-appropriate support layout when that makes the compound form clearly different.
- All forms must still look like one technology: modular metallic Replicator blocks, insectoid/arthropod mechanical construction, restrained accents, and natural RimWorld-scale readability.
- Larger forms must visibly look like reorganised concentrations of smaller Replicator mass rather than the same Drone sprite enlarged.
- Hunter, Bulwark, Titan, Siege Mass, Controller, Repairer, Burrower and Artillery each need a distinct silhouette and role language.
- The current full-family professional pass supersedes the older stop-after-each-unit rule for this rebuild.

The current priority remains deliberately narrow: finish the block-form Replicator pawn family before wandering into unrelated art families.

---

# ACTIVE PRIORITY — REBUILD EVERY BLOCK-FORM REPLICATOR PAWN, ONE AT A TIME

The current Replicator family is visually wrong even though it is structurally valid. The newer pass made the units look too much like the same design repeated at different scales. The older procedural/cartoon pass was visually crude, but it did something important correctly: **Drone, Hunter, Bulwark, Titan, Siege Mass, Controller, Repairer, Burrower and Artillery had visibly different silhouettes and role cues.**

The task is **not** to restore the old cartoon rendering. The task is to preserve that old role differentiation and rebuild every unit at professional quality.

## Historical reference that must be examined

Use Git history, not memory.

- Current six-legged rebuild commit: `8defde9e717e918b8287f8349df4b39f724e449b` — `Rebuild Replicator pawn art with six-legged Stargate forms`.
- Its parent: `fb1dff9baa182659a45fb7eaee28bb84da6a6ff9`.
- The parent-side version of `.github/artgen/generate_replicator_pawns.py` contains the earlier differentiated procedural/cartoon design logic. It explicitly gave the nine roles different body dimensions, leg layouts and specialized front/role structures.

That older state is a **design/silhouette reference**, not a final art source. Do not copy its crude geometry pixel-for-pixel. Recover what it got right about role, scale and silhouette, then render the result professionally.

## Stargate baseline — non-negotiable

Every ordinary block-form Replicator in this family must still read immediately as a Stargate SG-1 style block Replicator:

- **the base Drone has six legs; compound/specialist forms may vary leg count to communicate their reorganised role**;
- modular metallic block construction;
- insectoid/arthropod mechanical posture;
- hard segmented geometry assembled from Replicator blocks;
- same technological family across all variants;
- no generic four/eight-legged sci-fi spider drone;
- no humanoid Asuran visual language;
- no unrelated alien chitin creature;
- no excessive neon that overwhelms the metallic block construction.

The family resemblance must come from shared Replicator construction. **The silhouettes must not come from reusing the same complete body and merely stretching, recolouring or adding one attachment.**

---

# EXACT PRODUCTION ORDER

Work in this order. **One unit only per user-approved pass. After completing and committing one unit family, stop and wait for the user to say `continue`.**

1. **Drone**
2. **Hunter**
3. **Bulwark**
4. **Titan**
5. **Siege Mass**
6. **Controller**
7. **Repairer**
8. **Burrower**
9. **Artillery**

Do not skip ahead because another unit looks easier. Do not batch-generate the nine units.

---

# UNIT DESIGN REQUIREMENTS

Source of live gameplay truth: `Defs/ThingDefs/Races_Replicator.xml` plus role-specific C# where present.

All nine PawnKinds use `Graphic_Multi` and therefore require coherent directional art under:

`Textures/Things/Pawn/Replicator/`

For each unit, preserve the full family expected by the current mod:

- `WNG_Replicator<Unit>.png`
- `WNG_Replicator<Unit>_north.png`
- `WNG_Replicator<Unit>_east.png`
- `WNG_Replicator<Unit>_south.png`
- `WNG_Replicator<Unit>_west.png`

The base compatibility image and the four cardinal facings must represent **one consistent machine**, not five unrelated generations.

## 1. Replicator Drone

**DefName:** `WNG_ReplicatorDrone`  
**Body size:** `0.55`  
**Draw size:** `0.85`  
**Class:** Light  
**Role:** irreducible basic mobile block form; two compatible Drones reorganize into one Hunter.

Visual requirement:
- smallest, simplest, lightest six-legged Replicator;
- compact central block body;
- thin/light segmented legs;
- basic feeding/manipulation mouthparts rather than a specialist weapon profile;
- visibly much smaller and less elaborate than Hunter;
- should look like the foundational unit from which the larger forms are assembled.

Why it matters:
- it establishes the visual grammar for the whole block-form family;
- if the Drone is too bulky or ornate, every later size tier becomes visually compressed.

## 2. Replicator Hunter

**DefName:** `WNG_ReplicatorHunter`  
**Body size:** `0.85`  
**Draw size:** `1.25`  
**Class:** Light  
**Move speed:** `5.4`  
**Role:** fast pursuit form assembled from two Drones; scything limbs; two Hunters reorganize into a Bulwark.

Visual requirement:
- longer, lower and faster-looking than Drone;
- extended pursuit stance;
- clearly sharper/scything attack limbs;
- still light rather than armoured;
- stronger forward attack silhouette without becoming a bulky tank.

## 3. Replicator Bulwark

**DefName:** `WNG_ReplicatorBulwark`  
**Body size:** `1.45`  
**Draw size:** `1.8`  
**Class:** Medium  
**Role:** dense armoured block form assembled from two Hunters; armoured crusher; two Bulwarks reorganize into a Titan.

Visual requirement:
- wider, lower, denser armoured mass;
- heavy block layering and thicker leg roots;
- front must read as crushing/ramming armour rather than Hunter blades;
- obvious defensive mass at a glance;
- must look substantially larger than Hunter.

## 4. Replicator Titan

**DefName:** `WNG_ReplicatorTitan`  
**Body size:** `2.4`  
**Draw size:** `2.6`  
**Class:** Heavy  
**Role:** siege-scale construct assembled from two Bulwarks; siege mandibles; two Titans reorganize into a Siege Mass.

Visual requirement:
- large, intimidating siege form;
- tall/thick layered central chassis with visibly enormous six-leg supports;
- large front siege mandibles;
- enough structural detail to communicate that multiple smaller Replicators have reorganized into a much larger machine;
- not simply a scaled-up Bulwark.

## 5. Replicator Siege Mass

**DefName:** `WNG_ReplicatorSiegeMass`  
**Body size:** `3.8`  
**Draw size:** `3.5`  
**Class:** Heavy  
**Role:** largest ordinary physical concentration; devouring ram; destruction releases two Titans.

Visual requirement:
- largest ordinary block-form silhouette in the hierarchy;
- broad, brutal mass with an unmistakable forward devouring/ram structure;
- six major load-bearing legs, visibly heavier than Titan;
- should look like a mobile concentration of swarm material rather than one ordinary insect enlarged;
- scale must read immediately in comparison with Titan.

## 6. Replicator Controller

**DefName:** `WNG_ReplicatorController`  
**Body size:** `1.20`  
**Draw size:** `1.45`  
**Class:** Medium  
**Role:** mature-swarm coordination body; improves same-faction assimilation and recombination while functional.

Visual requirement:
- medium mass but unmistakably command/coordination oriented;
- visible command-lattice/sensor/coordination structure integrated into the same six-legged block body;
- more cerebral/centralized silhouette, not simply a Hunter with a glowing dot;
- specialized structure must remain clearly mechanical Replicator technology.

## 7. Replicator Repairer

**DefName:** `WNG_ReplicatorRepairer`  
**Body size:** `0.85`  
**Draw size:** `1.20`  
**Class:** Light  
**Role:** Hunter-mass support body that prioritizes repairing seriously damaged allied block Replicators.

Visual requirement:
- similar overall mass tier to Hunter but a clearly different support silhouette;
- integrated repair/manipulator limbs or tool clusters;
- less aggressive front profile;
- visibly capable of reaching, gripping and rebuilding damaged block structures;
- no human-style handheld repair tool.

## 8. Replicator Burrower

**DefName:** `WNG_ReplicatorBurrower`  
**Body size:** `0.90`  
**Draw size:** `1.25`  
**Class:** Light  
**Role:** Hunter-mass structural specialist; prioritizes physical access blockers and powered containment; breaching mandibles.

Visual requirement:
- six-legged Stargate Replicator first, breaching specialist second;
- strong forward structural-breach mandibles/cutting/crushing head geometry;
- compact but reinforced forebody;
- must not become a generic drill tank, worm or unrelated burrowing creature;
- current restored Burrower file is only a validated fallback and **is not exempt from this new role-differentiation pass**.

## 9. Replicator Artillery

**DefName:** `WNG_ReplicatorArtillery`  
**Body size:** `1.45`  
**Draw size:** `1.85`  
**Class:** Medium  
**Role:** Bulwark-mass long-range support form with integrated `WNG_ReplicatorArtilleryCaster`.

Visual requirement:
- medium/heavy support chassis with six stable legs;
- integrated long-range emitter/caster grown into the body, not a human gun bolted on top;
- stable firing posture and obvious firing direction;
- visually distinct from Bulwark despite similar mass;
- artillery organ must look like Replicator blocks reorganized into a weapon.

---

# ONE-UNIT WORKFLOW — DO THIS EXACTLY

For every unit in the production order:

1. **Read the live unit first.**
   - Read its ThingDef/PawnKindDef and role-specific C#.
   - Confirm body size, draw size, speed, armour, function, weapons/tools and split/recombine relationship.

2. **Recover the older differentiated concept.**
   - Inspect the historical pre-`8defde9...` art/generator state, especially parent `fb1dff9...`.
   - Identify what made that role visibly different: body dimensions, stance, leg arrangement, front equipment, armour density, specialist structures.
   - Use those ideas as design information only.

3. **Check Stargate Replicator morphology.**
   - Six legs.
   - Block-built mechanical insect form.
   - Segmented metallic geometry.
   - No generic spider-drone substitution.

4. **Design only that unit.**
   - Do not generate a nine-unit sheet.
   - Do not start the next unit.
   - Do not use one universal master body for every role.

5. **Create one coherent four-facing machine.**
   - North, south, east and west must be the same design viewed consistently.
   - Side views must actually read as side orientations, not arbitrary new creatures.
   - Preserve size and specialist equipment across all facings.

6. **Prepare actual game assets, not a poster.**
   - Transparent RGBA canvas.
   - No title text.
   - No labels such as NORTH/EAST/SOUTH/WEST inside the PNG.
   - No black square/background.
   - No concept-art border.
   - No logo.
   - No descriptive copy.
   - No floor shadow baked as an opaque rectangle.

7. **Replace the real files in the public mod repo.**
   - The deliverable is the actual `Textures/Things/Pawn/Replicator/...` family on `main`.
   - A generated image shown in ChatGPT is only an intermediate source and **does not count as completion**.
   - If a generation is approved, convert/package it into the correct directional PNGs and commit them.

8. **Validate the exact family.**
   - Full Pillow decode/load of every PNG.
   - Confirm RGBA/alpha transparency.
   - Confirm no alpha touches the canvas edge accidentally.
   - Confirm no white/gray matte or dirty transparent RGB fringe.
   - Confirm logical centring and safe margins.
   - Confirm current `Graphic_Multi` paths resolve.
   - Run the targeted Replicator art/static validation relevant to the changed family.

9. **Visually inspect the committed result.**
   - Check at full resolution and at approximate RimWorld gameplay scale.
   - Compare with the older role silhouette and with the better professional-quality art already present in WNG.
   - Green automation is necessary but not sufficient.

10. **Stop.**
    - Report the exact unit and paths committed.
    - Wait for the user to say `continue` before touching the next Replicator.

---

# WHY THIS WORKFLOW MATTERS

The Replicator hierarchy is gameplay information, not decoration.

A player should be able to identify threat/function from silhouette before reading a label:

- tiny/basic = Drone;
- fast/scything = Hunter;
- armoured/crushing = Bulwark;
- siege-scale = Titan;
- enormous mobile concentration = Siege Mass;
- coordination structure = Controller;
- support/manipulation = Repairer;
- structural breaching = Burrower;
- long-range weapon body = Artillery.

If all nine are the same body with tiny cosmetic changes, the art is failing to communicate the actual mod design even if every XML reference and PNG validator is green.

Professional quality also matters because WNG is being treated as a finished public mod, not as a placeholder prototype. The goal is **Stargate recognition + RimWorld readability + role differentiation + consistent WNG production quality**.

---

# QUALITY BAR

Use the better current WNG art as a **quality benchmark**, not as a source file.

Required:

- professional shading and material definition;
- crisp segmented construction;
- readable silhouette at RimWorld zoom;
- controlled highlights rather than glowing everywhere;
- coherent perspective across all four facings;
- strong role-specific shape language;
- clean alpha;
- complete object inside canvas;
- no floating fragments;
- no accidental crop;
- no generated text or poster elements;
- no crude stick-figure/cartoon result;
- no same-body-for-every-role shortcut.

`wng-art.html` may be looked at to understand the current **quality level** of good WNG art, but it is not authoritative for files or validation. The mod repo and live Defs remain the source of truth.

Do not touch the cosmology art page.

---

# WHAT HAS BEEN GOING WRONG OVER THE LAST DAY

The immediate failure was not lack of access or lack of available tools. It was repeatedly executing the wrong workflow.

1. The Replicator variants were upgraded toward a common six-legged master and **lost the older role differentiation**.
2. I treated “professional” as “more detailed rendering” while allowing the functional silhouettes to collapse toward one design.
3. I repeatedly generated **standalone chat concept images** instead of finishing actual mod texture families.
4. Some generated concepts were generic spider drones rather than recognizable Stargate block Replicators.
5. I ignored the explicit six-leg requirement in several attempts.
6. I failed to use the older differentiated art/generator as the design reference even after the user explicitly identified it as what worked better.
7. I let stale context contaminate image generation, producing the wrong unit (especially Burrower) while the requested target was Drone.
8. I generated posters/sprite sheets with text, logos and black backgrounds when the mod requires clean transparent game sprites.
9. I kept generating again after a wrong result instead of first correcting the source/design workflow.
10. I confused “make the asset” with “show a concept.” The requested deliverable was the actual repo asset.
11. I spent excessive time hammering CI/log routes during a one-asset fix instead of changing method quickly.
12. I created temporary branches/status churn that were irrelevant to the requested one-asset job.
13. I made completion claims before verifying exactly what was on `main`.
14. I relied too heavily on green structural validation even when the visual result was poor.
15. I failed to stop after the single requested unit/pass and wait for user approval.

## How to avoid repeating this

Before every Replicator-art action, answer these five checks internally:

1. **Which exact unit is active?**
2. **What does its live Def say it does and how large is it?**
3. **What did the older differentiated version do to make that role visibly distinct?**
4. **Does the design still look like a six-legged Stargate block Replicator?**
5. **Am I replacing the real repo directional files, or am I accidentally just making a chat picture?**

If any answer is unclear, resolve that before generating or committing anything.

---

# PAUSED UNTIL REPLICATOR PAWN PASS IS ACCEPTED

The following art work remains valid work but is **not the current task**:

- Asuran Queen Recovery Carrier incomplete associated pieces;
- remaining Replicator resources/weapons/projectiles/buildings/ruins/adaptation/UI;
- remaining Goa'uld/Asuran/Wraith art-family cleanup described in the historical mistakes below;
- whole-mod art audit and website regeneration.

Do not use those items as an excuse to detour from the one-at-a-time Replicator pawn sequence.

---

# ERRORS / MISTAKES — PRESERVE AND EXTEND

These are historical failures from the art rebuild. Keep them so future work does not repeat them. Add new numbered mistakes when a new failure mode is discovered; do not erase the old ones.

## 1. Followed an obsolete local-only workflow
Mistake:
- allowed D118-D141 work to remain local/File-Library while public GitHub stayed stale.

Correct rule:
- public WNG repo is the production source;
- current checkpoints belong in GitHub.

## 2. Replaced a working display page with a ZIP-loader
Mistake:
- made the user select a WNG ZIP in the website.

Correct rule:
- the website is a generated display page only;
- no ZIP upload/file picker.

## 3. Hid art behind filters/comparison state
Mistake:
- gallery logic obscured art and confused what actually existed.

Correct rule:
- final display is a straightforward whole-mod page;
- never use gallery filters as proof of mod content.

## 4. Pointed a page at stale mod content
Mistake:
- website could technically work while GitHub still contained an old mod.

Correct rule:
- mod repository first;
- website last.

## 5. Overcomplicated asset transfer
Mistake:
- gallery archives, delta manifests, base64 transfer detours and cache systems.

Correct rule:
- keep the mod repo current;
- use bounded deterministic transfers only when needed.

## 6. Compared changed files when the user asked for all art
Mistake:
- spent time on deltas rather than the requested whole-mod view.

Correct rule:
- change history is irrelevant to the display page unless explicitly requested.

## 7. Created misleading branch names
Mistake:
- `d141-current-art` existed while still pointing at D117.

Correct rule:
- verify commit/tree before claiming branch content.

## 8. Asuran Gravship Door family
Paths:
`Textures/Things/Building/Precursor/Gravship/WNG_AsuranGravshipDoor*.png`

Mistake/known problem:
- gray flat rectangle, cyan construction-line scribbles, obvious placeholder look;
- not a seamless airtight aperture in an Asuran nanite-composite hull.

Correct rule:
- clean seamless Ancient/Asuran aperture;
- integrate with surrounding hull language;
- subtle blue luminous seam where appropriate;
- transparent edges;
- no guide-line scribbles;
- verify intended vanilla-equivalent door rotation behavior before changing `Graphic_Single`/directional handling.

## 9. Goa'uld Gravship Door family
Paths:
`Textures/Things/Building/Goauld/Gravship/WNG_GoauldGravshipDoor*.png`

Mistake/known problem:
- flat picture-frame brown/gold rectangles rather than heavy Ha'tak pressure doors.

Correct rule:
- heavy dark naquadah door/hatch;
- bronze/gold structural ribs;
- central amber lock/energy seam;
- physical thickness;
- verify graphic/rotation behavior before deleting or changing directional files.

## 10. Wraith Gravship Door family
Paths:
`Textures/Things/Building/Wraith/Gravship/WNG_WraithGravshipDoor*.png`

Correct rule:
- preserve good art when it is already good;
- alpha-edge, centring and directional-consistency checks still required;
- confirm living/organic Wraith architecture remains readable at one-tile scale.

## 11. Hull / wall families
The angled gravship hull artwork is comparatively strong.

Correct rule:
- use it as a quality benchmark for coherent material, edge treatment and silhouette;
- linked wall/hull atlases must preserve RimWorld linked-atlas requirements;
- do not convert linked wall textures into ordinary independent squares.

## 12. Risked touching the wrong website area
Permanent rule:
- WNG work touches only WNG files/pages;
- Cosmology site/gallery is unrelated.

## 13. Used the art page as if it were part of the art-production process
Mistake:
- repeatedly treated the website viewer/gallery as a working source or QA mechanism.

Correct rule:
- never use `wng-art.html` as the authoritative art-production source;
- source is the mod repo + Defs;
- page is display/quality-reference only and is regenerated after mod work.

## 14. Equated “PNG exists / reference resolves / validation green” with “art is finished”
Mistake:
- zero missing refs was treated as visual completion.

Correct rule:
- structural validation and visual QA are separate gates;
- both must pass.

## 15. Marked explicitly provisional art as completed
Mistake:
- Precursor Field Armour was listed as reworked/completed while its own XML still said the visual was provisional.

Correct rule:
- read the actual Def comments/descriptions before declaring completion;
- provisional means unfinished.

## 16. Allowed borders/halos around ship sprites
Mistake:
- Puddle Jumper, Queen Recovery Carrier and other craft retained visible pale fringe/border contamination.

Correct rule:
- inspect alpha edges directly;
- preserve dark RGB under transparency;
- remove white/gray matte;
- test all facings.

## 17. Produced icons with embedded square panels and bad centring
Mistake:
- some gene/xenotype/build/ability icons contain a visible rectangular source image or off-centre content.

Correct rule:
- transparent icon canvas;
- centred alpha mass;
- consistent padding;
- dedicated UI composition.

## 18. Failed to centre the Goa'uld Grav-field Projector
Mistake:
- art had an unconnected line/floating fragment and most of the 2×2 canvas was wasted.

Correct rule:
- read footprint/function first;
- centre the complete device;
- no disconnected artifact;
- silhouette must communicate function.

## 19. Accepted a poor Death Glider despite a recognizable canonical silhouette
Mistake:
- the Death Glider was muddy and did not strongly resemble the Stargate fighter it represents.

Correct rule:
- reference recognizable canonical craft before drawing;
- preserve broad canonical silhouette;
- adapt to RimWorld top-down readability.

## 20. Trusted prior “apparel completed” claims without reviewing every body/direction family
Mistake:
- 217 apparel PNGs were treated as effectively solved based on targeted path checks.

Correct rule:
- inspect representative male/female/thin/hulk/fat + N/E/S views for every apparel family;
- sample remaining variants;
- no family gets completion status from filenames alone.

## 21. Created directional files without first confirming the active graphic class
Mistake:
- Asuran/Goa'uld gravship doors used `Graphic_Single` while multiple directional PNGs also existed.

Correct rule:
- inspect Def graphic behavior first;
- do not create redundant art families or change XML blindly;
- test rotation in-game before deciding Single vs Multi.

## 22. Mixed world-sprite and UI-icon requirements
Mistake:
- some UI art was reused world/object rendering rather than purpose-built icon art.

Correct rule:
- world sprite: footprint, direction, interaction origin;
- UI icon: square readability, centring, padding;
- share design language, not necessarily the exact same export.

## 23. Let direct raw-image links become a mobile nuisance
Mistake:
- raw PNG click-through could lead to Android downloads that appeared in the user's Gallery.

Correct rule:
- do not use raw-link downloading as internal QA;
- do not download the whole art set to the user's phone;
- website click behavior should avoid unintended download semantics.

## 24. Generated/redrew art before establishing exact function and lore
Mistake:
- assets were treated as generic sci-fi art rather than specific functional objects.

Correct rule:
- Def first;
- lore second;
- vanilla presentation benchmark third;
- then draw.

## 25. Deleted directional door art because the current Def said `Graphic_Single`
Mistake:
- inferred that Goa'uld/Wraith east/north/south/west door PNGs were dead art and removed them.

Correct rule:
- establish intended vanilla-equivalent behavior first;
- preserve potentially required directional/state art while auditing;
- compare WNG door behavior against vanilla doors;
- then change XML/code and art together;
- never delete art solely because a broken/incomplete Def does not reference it.

## 26. Automatic art-generator push could overwrite approved professional art
Mistake:
- legacy broad generator automation could rerun first-pass/procedural scripts and overwrite later approved art.

Correct rule:
- approved professional art is authoritative;
- broad art generator is manual-only until every generator is an authoritative source;
- narrow automatic finalizers are allowed only for explicitly deterministic derivations from an approved master.

## 27. Tried to auto-derive a four-view craft from one facing
Mistake:
- a temporary Asuran carrier finalizer rotated one north-facing master into all directions, threatening separately authored coherent facings.

Correct rule:
- preserve genuinely authored directional craft views;
- do not replace them with geometric rotations merely for convenience;
- validate each source PNG before propagation.

## 28. Lost the active art target and generated unrelated door art
Mistake:
- after a freeze, task state was lost and art for the previous family was generated instead of the active projector/extender task.

Correct rule:
- before every image-generation call, re-read the exact active item/path/Def;
- one active family at a time;
- after a freeze, verify last committed state before generating anything.

## 29. PNG signature/non-empty checks were not enough
Mistake:
- some PNGs passed shallow signature checks but failed full Pillow decoding.

Correct rule:
- every PNG validation pass must fully decode the file;
- use Pillow `Image.open(...).load()` and/or `verify()` followed by reopen+load;
- also inspect alpha edges, contamination and logical centring.

## 30. Copied truncated base64 into live PNG art
Mistake:
- long base64 payloads were visibly truncated/ellipsized, yet the resulting blobs were committed and initially treated as valid.

Correct rule:
- never copy binary/base64 payloads from a clipped tool response;
- prefer verified binary transfer or deterministic in-repo generation;
- verify byte count/hash and full decode at destination before sign-off.

---

## 31. Flattened nine Replicator roles into one repeated master design
Mistake:
- the newer Replicator art pass gained six-legged Stargate-like detail but reused too much of one master body, so Drone, Hunter, Bulwark, Titan, Siege Mass, Controller, Repairer, Burrower and Artillery became visually too similar.

Why this is wrong:
- the actual mod gives those units different sizes, speeds, armour, hierarchy levels and functions;
- the older crude art communicated those distinctions better.

Correct rule:
- shared block technology, **different complete silhouettes**;
- recover role differentiation from the old procedural/cartoon state and upgrade its rendering quality rather than erasing its design logic.

## 32. Generated chat images instead of completing mod assets
Mistake:
- responded to an actual mod-art replacement task by generating standalone images in ChatGPT.

Correct rule:
- chat generation is only an intermediate source;
- completion means the correct directional PNG family is prepared, committed to public `main`, decoded, validated and visually checked.

## 33. Generated non-Stargate generic spider drones
Mistake:
- several attempts produced generic sci-fi spiders/robots and did not consistently preserve the canonical six-legged block-Replicator morphology.

Correct rule:
- count the legs;
- six means six;
- body must read as block-built Stargate Replicator before specialist ornamentation is added.

## 34. Ignored the user's requested historical design reference
Mistake:
- after the user said the previous cartoony versions were better differentiated, I still invented fresh generic designs rather than retrieving that older state.

Correct rule:
- inspect Git history first;
- use parent `fb1dff9...` / pre-`8defde9...` differentiated design logic as the explicit concept reference for this pass.

## 35. Let stale context choose the wrong Replicator
Mistake:
- generated Burrower concepts while the active requested first unit was Drone.

Correct rule:
- before every generation, state internally the exact active DefName and do not include unrelated prior-unit context in the generation target.

## 36. Generated poster/sheet graphics instead of transparent game sprites
Mistake:
- outputs included black backgrounds, titles, logos, role descriptions and direction labels.

Correct rule:
- production files are transparent sprite assets only;
- no poster design, typography, labels or branding inside game textures.

## 37. Repeated generation after failure without fixing the workflow
Mistake:
- after a wrong image, another image was generated immediately with essentially the same broken approach.

Correct rule:
- correct the source reference, unit identity and output format first;
- then generate again once the workflow itself is fixed.

## 38. Spent hours on one asset by retrying the same failing technical route
Mistake:
- repeatedly queried the same failed CI/log path during the Burrower replacement instead of quickly switching to repository history/known-good blob and targeted workflow metadata.

Correct rule:
- after one or two failed attempts on the same route, change method;
- isolate one file, one commit, one targeted validator;
- do not turn one art replacement into a repo-wide debugging session.

## 39. Created irrelevant temporary branches during a one-file fix
Mistake:
- temporary Burrower branches were created even though the required work was a direct bounded production asset fix.

Correct rule:
- do not create branches unless the task actually requires them;
- keep one-asset changes small and directly verifiable.

## 40. Confused structural success with visual success for Replicators
Mistake:
- a generated family could pass decode/static validation and still be a poor professional design because all variants looked alike.

Correct rule:
- Replicator acceptance requires both:
  1. technical validity; and
  2. obvious role/size/function differentiation at gameplay scale.

## 41. Failed to stop after the requested one-unit pass
Mistake:
- the user explicitly requested one Replicator, then `continue`, but repeated attempts kept spilling into extra generation and explanation.

Correct rule:
- one unit family only;
- commit and verify it;
- stop;
- wait for `continue`.

---

# NEXT ACTION

**Next unit: `WNG_ReplicatorDrone`.**

Do not start by making a concept poster. Start by retrieving the old differentiated Drone design from the historical pre-`8defde9...` state, reading the current Drone Def, and building the professional six-legged Drone directional family for the actual mod.
