# GTA V: what Rockstar did, and what we copy (research 2026-11-15)

Sources:
- Adrian Courrèges, GTA V Graphics Study, parts 1–3: https://www.adriancourreges.com/blog/2015/11/02/gta-v-graphics-study/
- FiveM LOD guide: https://docs.fivem.net/docs/assets-manual/beginner-series/part-6/
- Sollumz docs
- GDC 2014 "The Sound of GTA V": https://gdcvault.com/play/1021165/The-Sound-of-Grand-Theft
- Development of GTA V: https://en.wikipedia.org/wiki/Development_of_Grand_Theft_Auto_V

## Frame (PC 2015, DX11): deferred, HDR, ~4 000 draw calls
- **Reflection cubemap every frame.** 6 faces of 128 px HDR, terrain, sky and big buildings only, stored as dual paraboloid.
- **G-buffer: 4 × RGBA8.**
  - diffuse
  - normal
  - spec (intensity, gloss, Fresnel)
  - irradiance (sun, emissive, skin/plant mask)
  - Depth is reversed log Z; the stencil holds object classes.
- **Shadows:** 4 cascades in a 1024×4096 atlas, cloud shadows in a second channel, depth-aware blur.
- **SSAO** at half resolution with bilateral blur.
- **Night lights:** light volumes. Far lights are instanced 32×32 quads ("distant lights"), tens of thousands of them.
- **Water:** planar reflection at 240×120, half-resolution refraction, caustics.
- **Atmosphere:** half-resolution light shafts, depth fog, a sky dome in one draw call, clouds on a ring mesh.
- **LOD fading:** screen-door dither.
- **Post chain:**
  1. Eye adaptation: luminance taken down to 1×1.
  2. Bloom: down to 1/16.
  3. Uncharted 2 filmic tone mapping.
  4. FXAA.
  5. Chromatic aberration.
  6. DOF (CoC map), motion blur (camera only), lens flares.
- **Wet roads:** a global wetness value darkens albedo and raises gloss; puddle masks carry ripple normals.

## World and assets
- **Reference:** about 250 000 GPS-tagged photos of LA, plus visits with architects and police. The city is condensed, not a 1:1 copy.
- **RAGE formats:** YDR (mesh with LODs), YFT (breakable), YTD (textures), YBN (collision), YTYP (archetypes and interiors), YMAP (placements, LOD tree, car generators, timecycle boxes).
- **LOD chain:** HD → LOD → SLOD1/2/3. SLOD is a merged mesh of a whole district (e.g. the Vinewood Hills in one 2 500-triangle draw call).
- **Facades:**
  - unique buildings with tiling textures plus a detail map, normal map and parallax
  - plenty of decal geometry: grime under windows, water streaks, dirt at wall bases, road cracks, oil
  - vertex colours: natural AO, artificial (interior) light, and a blend between two textures
- **Night windows:** emissive, with a random mix of lit and unlit, driven by the timecycle.
- **Props:** bins, hydrants, signs, poles with sagging wires, rubbish, parked cars from car generators.

## Pedestrians
- **Component variation:** 12 slots (head, hair, torso, legs, hands, feet, accessories…), each with several meshes and textures, plus props.
- **Density:** popcycle (zone × hour).
- **Animation:** mocap plus Euphoria.
- **Scenarios:** placed points (smoking, phone, bench, café, leaning).

## Timecycle
- One file per weather type, about 13 keyframes per day, hundreds of variables: sun, ambient, sky, fog, exposure, bloom, emissive, reflections, clouds.
- Values are interpolated in time and blended across weathers.
- Local override boxes handle tunnels and interiors.

## Audio
- Ambient zones (rules by time and weather), point emitters, a static bed plus random one-shots, traffic generated from the simulation.

## What we copy (Unity 6 URP, iGPU), in priority order
| # | Technique | Unity approach | Impact / cost |
|---|---|---|---|
| 1 | Timecycle table | Keyframes by hour × weather drive the sun, ambient, fog, exposure, bloom, emissive and wetness. | very high / ~0 |
| 2 | Vertex AO in the generator | Base of walls, under eaves, window reveals, corners; second channel for artificial light. | high / 0 |
| 3 | Tonemapping and grading | ACES; contrast +10, saturation −5; warm sunsets; fake eye adaptation (lerp Post Exposure). | very high / low |
| 4 | Grime decals as mesh strips | Drips under windows, dark band at the wall base, kerb dirt, cracks; one atlas, SRP Batcher. | high / low |
| 5 | Realtime reflection probe at the camera | 128 px, terrain, buildings and sky only, time-sliced. | high / med |
| 6 | Wet roads | `_Wetness`: albedo × (1 − 0.3w), smoothness → 0.85, noise mask for puddles. | very high in rain / low |
| 7 | Shadows | 2 cascades, 1024–2048, 80–120 m, soft Low. | high / med |
| 8 | Light cards | Instanced additive quads for street lamps and windows; real Lights only for the nearest ~8. | very high at night / low |
| 9 | LOD/SLOD | LOD Group with dither cross-fade; districts merged into one mesh for the distance. | essential for FPS |
| 10 | Bloom | threshold 1.0–1.1, intensity 0.3–0.5 by day and 0.8–1.2 at night, scatter 0.65. | high / low |
| 11 | SSAO | low quality, downsampled; offered as a toggle. | med / med–high |
| 12 | Fog, sky, clouds | Fog coloured by the timecycle, gradient sky, cloud ring. | high / low |
| 13 | Vltava water | Two normals, depth colour, Fresnel to the probe, light cards at night. | high / low–med |
| 14 | Pedestrians | Component variation, MaterialPropertyBlock tints, scenario points (benches, stops, cafés), popcycle. | very high / CPU-med |
| 15 | Props by rules | Lamps every 25–35 m, bins at crossings, bollards, signs, tram catenary, parked cars in parking lanes. | very high / med |

**Frame budget:** Forward+, HDR, Render Scale 0.75–0.85 with FSR1, FXAA, no DOF or motion blur, vignette 0.2.
