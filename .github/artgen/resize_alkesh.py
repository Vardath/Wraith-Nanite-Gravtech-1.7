from pathlib import Path
import re

path = Path('Defs/ThingDefs/Shuttle_Goauld.xml')
text = path.read_text(encoding='utf-8')
marker = '  <!-- Death Glider: exact two-seat fighter, native shuttle travel + WNG physical two-pass sortie. -->'
if marker not in text:
    raise RuntimeError('Death Glider marker not found; refusing broad shuttle edit')

alkesh, rest = text.split(marker, 1)
rest_before = rest

# Restore the pre-art-rebuild Al'kesh gameplay contract, changing geometry only where
# needed for the requested long 5x7 shuttle. Do not replace/re-author any linked Defs.
# The original transport stats and connected shuttle/bombing systems are preserved.

def replace_exact_count(pattern, replacement, expected, label):
    global alkesh
    alkesh, count = re.subn(pattern, replacement, alkesh)
    if count != expected:
        raise RuntimeError(f'{label}: expected {expected} replacements, found {count}; refusing partial edit')

replace_exact_count(r'<drawSize>\([^<]+\)</drawSize>', '<drawSize>(5.5,7.7)</drawSize>', 4, 'drawSize')
replace_exact_count(r'<size>\([^<]+\)</size>', '<size>(5,7)</size>', 4, 'size')
replace_exact_count(r'<shadowSize>\([^<]+\)</shadowSize>', '<shadowSize>(5.5,7.7)</shadowSize>', 2, 'shadowSize')
replace_exact_count(r'<shadowData><volume>\([^<]+\)</volume><offset>\(0,0,0\)</offset></shadowData>', '<shadowData><volume>(4.8,1.2,6.8)</volume><offset>(0,0,0)</offset></shadowData>', 1, 'landed shadow')
replace_exact_count(r'<interactionCellOffset>\([^<]+\)</interactionCellOffset>', '<interactionCellOffset>(0,0,-4)</interactionCellOffset>', 1, 'interaction cell')

# Restore the original pre-resize mass. Size/shape work must not silently rebalance the shuttle.
replace_exact_count(r'<Mass>[^<]+</Mass>', '<Mass>185</Mass>', 1, 'mass')

# Hard guards for every gameplay connection that existed before the art/size rebuild.
required = [
    '<defName>WNG_AlkeshIncoming</defName>',
    '<thingClass>PassengerShuttleIncoming</thingClass>',
    '<defName>WNG_AlkeshLeaving</defName>',
    '<thingClass>PassengerShuttleLeaving</thingClass>',
    '<defName>WNG_AlkeshBombingPass</defName>',
    '<thingClass>WraithNaniteGravtech.Skyfaller_GoauldAlkeshBombingPass</thingClass>',
    '<defName>WNG_AlkeshWorld</defName>',
    '<defName>WNG_Ship_Alkesh</defName>',
    '<shipThing>WNG_AlkeshTransport</shipThing>',
    '<arrivingSkyfaller>WNG_AlkeshIncoming</arrivingSkyfaller>',
    '<leavingSkyfaller>WNG_AlkeshLeaving</leavingSkyfaller>',
    '<worldObject>WNG_AlkeshWorld</worldObject>',
    '<playerShuttle>true</playerShuttle>',
    '<defName>WNG_AlkeshTransport</defName>',
    '<thingClass>Building_PassengerShuttle</thingClass>',
    '<graphicClass>Graphic_Multi</graphicClass>',
    '<texPath>Things/Building/Goauld/Shuttle/WNG_AlkeshTransport</texPath>',
    '<uiIconPath>Things/Building/Goauld/Shuttle/WNG_AlkeshTransport</uiIconPath>',
    '<researchPrerequisites><li>WNG_GoauldShuttles</li></researchPrerequisites>',
    '<li Class="CompProperties_Shuttle"><shipDef>WNG_Ship_Alkesh</shipDef></li>',
    '<skyfallerLeaving>WNG_AlkeshLeaving</skyfallerLeaving>',
    '<worldObjectDef>WNG_AlkeshWorld</worldObjectDef>',
    '<li Class="CompProperties_Transporter"><massCapacity>900</massCapacity>',
    '<li Class="CompProperties_Refuelable"><fuelCapacity>360</fuelCapacity>',
    '<li Class="CompProperties_AmbientSound"><sound>ShuttleIdle_Ambience</sound></li>',
    '<li Class="WraithNaniteGravtech.CompProperties_GoauldAlkeshBombingRun">',
]
for token in required:
    if token not in alkesh:
        raise RuntimeError(f'Al\'kesh connection missing: {token}')

# Geometry assertions.
assert alkesh.count('<size>(5,7)</size>') == 4
assert alkesh.count('<drawSize>(5.5,7.7)</drawSize>') == 4
assert alkesh.count('<shadowSize>(5.5,7.7)</shadowSize>') == 2
assert '<shadowData><volume>(4.8,1.2,6.8)</volume><offset>(0,0,0)</offset></shadowData>' in alkesh
assert '<interactionCellOffset>(0,0,-4)</interactionCellOffset>' in alkesh
assert '<Mass>185</Mass>' in alkesh

# Absolutely no Death Glider or later Def may be altered by this correction.
assert rest == rest_before

out = alkesh + marker + rest
path.write_text(out, encoding='utf-8')
print("Al'kesh locked to 5x7 long geometry while preserving its original shuttle, world-flight, research, fuel/cargo and bombing-run connections.")
