# Builds the game's pedestrians in Blender from the MakeHuman CC0 assets (base mesh hm08, macro targets, low-poly
# proxy bodies, clothes, hair, the "mixamo_unity" skeleton and its weights). Only data files are read; no add-on code.
# Run:  blender -b --factory-startup --python make_people.py -- <mpfb_data_dir> <assets_dir> <out_dir>
#   mpfb_data_dir = extracted MPFB package /data (3dobjs, targets, rigs);  assets_dir = makehuman_system_assets_cc0
# Output: <out_dir>/<name>.fbx (one skinned mesh, ~20 bones) + textures in <out_dir>/Tex/, manifest.json
import bpy, gzip, json, math, os, shutil, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
D, A, OUT = argv[0], argv[1], argv[2]
os.makedirs(os.path.join(OUT, "Tex"), exist_ok=True)

KEEP = ["Root", "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head"] + [s + b for s in ("Left", "Right") for b in
        ("Shoulder", "Arm", "ForeArm", "Hand", "UpLeg", "Leg", "Foot", "ToeBase")]
KEEP = ["mixamorig:" + k for k in KEEP]

PEOPLE = [  # name, gender, race (african, asian, caucasian), age 0.5 young..1 old, weight -1..1, height -1..1, skin, body, clothes, hair, eyes, brows
    ("Muz1", 1, (0, 0, 1), 0.50, 0.0, 0.3, "young_caucasian_male", "male1591", ["male_casualsuit02", "shoes02"], "short02", "brown", "eyebrow001"),
    ("Zena1", 0, (0, 0, 1), 0.50, -0.2, 0.0, "young_caucasian_female", "female1605", ["female_casualsuit01", "shoes03"], "bob02", "blue", "eyebrow006"),
    ("Muz2", 1, (0, 0, 1), 0.72, 0.5, 0.0, "middleage_caucasian_male", "male1591", ["male_worksuit01", "shoes01"], "short03", "grey", "eyebrow002"),
    ("Zena2", 0, (0, 0, 1), 0.62, 0.2, 0.2, "middleage_caucasian_female", "female1605", ["female_elegantsuit01", "shoes05"], "ponytail01", "green", "eyebrow007"),
    ("Muz3", 1, (1, 0, 0), 0.50, 0.0, 0.5, "young_african_male", "male1591", ["male_casualsuit04", "shoes06"], "short04", "brown", "eyebrow003"),
    ("Zena3", 0, (0, 1, 0), 0.55, -0.1, 0.1, "young_asian_female", "female1605", ["female_casualsuit02", "shoes03"], "long01", "brown", "eyebrow008"),
    ("Muz4", 1, (0, 0, 1), 0.60, 0.2, 0.2, "young_caucasian_male2", "male1591", ["male_elegantsuit01", "shoes04"], "short01", "bluegreen", "eyebrow004"),
    ("Zena4", 0, (0, 0, 1), 0.50, -0.3, 0.3, "young_caucasian_female2", "female1605", ["female_sportsuit01", "shoes02"], "braid01", "lightblue", "eyebrow009"),
    ("Muz5", 1, (0, 1, 0), 0.55, -0.2, -0.3, "young_asian_male", "male1591", ["male_casualsuit03", "shoes01"], "short02", "brown", "eyebrow005"),
    ("Muz6", 1, (0, 0, 1), 0.85, 0.3, -0.3, "old_caucasian_male", "male1591", ["male_casualsuit05", "shoes06"], "short03_grey", "grey", "eyebrow002"),
    # the age spectrum: seniors, teenagers, children, toddlers, a baby for the prams
    ("Stara1", 0, (0, 0, 1), 0.90, 0.3, -0.4, "old_caucasian_female", "female1605", ["female_elegantsuit01", "shoes05"], "bob01_grey", "blue", "eyebrow007"),
    ("Stara2", 0, (0, 0, 1), 0.86, 0.5, -0.2, "old_caucasian_female", "female1605", ["female_casualsuit01", "shoes03"], "short02_grey", "grey", "eyebrow006"),
    ("Stary2", 1, (0, 0, 1), 0.92, 0.0, -0.2, "old_caucasian_male", "male1591", ["male_worksuit01", "shoes01"], "short04_grey", "brown", "eyebrow001"),
    ("Teen1", 1, (0, 0, 1), 0.28, -0.3, 0.2, "young_caucasian_male", "male1591", ["male_casualsuit06", "shoes02"], "short02", "blue", "eyebrow003"),
    ("Teen2", 0, (0, 0, 1), 0.27, -0.3, 0.1, "young_caucasian_female2", "female1605", ["female_sportsuit01", "shoes02"], "ponytail01", "green", "eyebrow008"),
    ("Teen3", 0, (0, 1, 0), 0.30, -0.2, 0.0, "young_asian_female", "female1605", ["female_casualsuit02", "shoes03"], "long01", "brown", "eyebrow009"),
    ("Dite1", 1, (0, 0, 1), 0.12, -0.1, 0.0, "young_caucasian_male", "male1591", ["male_casualsuit03", "shoes06"], "short02", "blue", "eyebrow001"),
    ("Dite2", 0, (0, 0, 1), 0.13, -0.1, 0.0, "young_caucasian_female", "female1605", ["female_casualsuit02", "shoes03"], "braid01", "lightblue", "eyebrow006"),
    ("Dite3", 1, (1, 0, 0), 0.10, 0.0, 0.0, "young_african_male", "male1591", ["male_casualsuit04", "shoes02"], "short04", "brown", "eyebrow003"),
    ("Batole1", 0, (0, 0, 1), 0.035, 0.0, 0.0, "young_caucasian_female", "female1605", ["female_casualsuit01", "shoes03"], "bob02", "blue", "eyebrow006"),
    ("Batole2", 1, (0, 0, 1), 0.03, 0.0, 0.0, "young_caucasian_male", "male1591", ["male_casualsuit02", "shoes02"], "short03", "brown", "eyebrow001"),
    ("Mimino", 1, (0, 0, 1), 0.0, 0.0, 0.0, "young_caucasian_male", "male1591", ["male_casualsuit03"], "short04", "blue", "eyebrow001"),
]
BUDGET = {"clothes": 1100, "hair": 700, "shoes": 160, "body": 1300}

def tgt(path):
    out = {}
    for l in gzip.decompress(open(path, "rb").read()).decode().splitlines():
        if l and l[0] != "#":
            p = l.split(); out[int(p[0])] = (float(p[1]), float(p[2]), float(p[3]))
    return out

# ---- base mesh ------------------------------------------------------------------------------------------------------
base, groups, g = [], {}, None
for l in open(os.path.join(D, "3dobjs", "base.obj")):
    if l.startswith("v "): base.append([float(x) for x in l.split()[1:4]])
    elif l.startswith("g "): g = l.split()[1]
    elif l.startswith("f ") and g and g.startswith("joint-"):
        groups.setdefault(g, set()).update(int(t.split("/")[0]) - 1 for t in l.split()[1:])
rig = json.load(open(os.path.join(D, "rigs", "standard", "rig.mixamo_unity.json")))
wj = json.load(open(os.path.join(D, "rigs", "standard", "weights.mixamo_unity.json")))["weights"]

def keep_of(b):
    while b not in KEEP: b = rig[b]["parent"]
    return b
W = {}
for b, lst in wj.items():
    k = keep_of(b)
    for v, w in lst: W.setdefault(v, {}); W[v][k] = W[v].get(k, 0) + w

def morph(gender, race, age, weight, height):
    c = [list(v) for v in base]
    gw = {"male": gender, "female": 1 - gender}
    # MakeHuman age scale: 0 = 1 year, 0.1875 = 11 years, 0.5 = 25, 1 = 90 (real child proportions, not a scaled adult)
    if age < 0.1875: aw = {"baby": 1 - age / 0.1875, "child": age / 0.1875}
    elif age < 0.5: aw = {"child": 1 - (age - 0.1875) / 0.3125, "young": (age - 0.1875) / 0.3125}
    else: aw = {"young": 1 - (age - 0.5) * 2, "old": (age - 0.5) * 2}
    def add(path, w):
        if w <= 1e-4 or not os.path.exists(path): return
        for i, d in tgt(path).items():
            c[i][0] += d[0] * w; c[i][1] += d[1] * w; c[i][2] += d[2] * w
    M = os.path.join(D, "targets", "macrodetails")
    for ri, r in enumerate(("african", "asian", "caucasian")):
        for gn, gv in gw.items():
            for an, av in aw.items(): add(os.path.join(M, f"{r}-{gn}-{an}.target.gz"), race[ri] * gv * av)
    for gn, gv in gw.items():
        for an, av in aw.items():
            wt = "maxweight" if weight > 0 else "minweight"
            add(os.path.join(M, f"universal-{gn}-{an}-averagemuscle-{wt}.target.gz"), gv * av * abs(weight) * 0.5)
            ht = "maxheight" if height > 0 else "minheight"
            add(os.path.join(M, "height", f"{gn}-{an}-averagemuscle-averageweight-{ht}.target.gz"), gv * av * abs(height) * 0.2)
    return c

# ---- proxies / clothes ----------------------------------------------------------------------------------------------
def read_obj(path):
    v, vt, f = [], [], []
    for l in open(path):
        if l.startswith("v "): v.append([float(x) for x in l.split()[1:4]])
        elif l.startswith("vt "): vt.append([float(x) for x in l.split()[1:3]])
        elif l.startswith("f "):
            f.append([(int(t.split("/")[0]) - 1, int(t.split("/")[1]) - 1 if "/" in t and t.split("/")[1] else -1) for t in l.split()[1:]])
    return v, vt, f

def read_clo(path):
    refs, scale, dele, mode, meta = [], {}, set(), None, {}
    for l in open(path):
        l = l.strip()
        if not l or l.startswith("#"): continue
        p = l.split()
        if p[0] in ("x_scale", "y_scale", "z_scale"): scale[p[0][0]] = (int(p[1]), int(p[2]), float(p[3])); continue
        if p[0] == "verts": mode = "v"; continue
        if p[0] == "delete_verts": mode = "d"; continue
        if p[0] in ("obj_file", "material"): meta[p[0]] = p[1]; continue
        if mode == "v" and p[0].lstrip("-").isdigit():
            if len(p) >= 9: refs.append(([int(p[0]), int(p[1]), int(p[2])], [float(x) for x in p[3:6]], [float(x) for x in p[6:9]]))
            elif len(p) == 1: refs.append(([int(p[0])] * 3, [1, 0, 0], [0, 0, 0]))
            else: mode = None
        elif mode == "d":
            i = 0
            while i < len(p):
                if i + 2 < len(p) and p[i + 1] == "-": dele.update(range(int(p[i]), int(p[i + 2]) + 1)); i += 3
                elif p[i].isdigit(): dele.add(int(p[i])); i += 1
                else: mode = None; break
        elif not p[0].lstrip("-").isdigit() and mode == "v": continue  # metadata inside the verts block
        else: mode = None
    return refs, scale, dele, meta

def fit(refs, scale, c):
    s = []
    for ax, k in (("x", 0), ("y", 1), ("z", 2)):
        a, b, val = scale.get(ax, (0, 0, 1))
        s.append(abs(c[a][k] - c[b][k]) / val if val else 1)
    pts, wts = [], []
    for r, w, o in refs:
        p = [sum(w[j] * c[r[j]][k] for j in range(3)) + o[k] * s[k] for k in range(3)]
        pts.append(p)
        acc = {}
        for j in range(3):
            for b, x in W.get(r[j], {}).items(): acc[b] = acc.get(b, 0) + x * w[j]
        top = sorted(acc.items(), key=lambda t: -t[1])[:4]; tot = sum(x for _, x in top) or 1
        wts.append({b: x / tot for b, x in top})
    return pts, wts

def mat_info(mhmat):
    d = os.path.dirname(mhmat); tex = None; alpha = False
    for l in open(mhmat):
        p = l.split()
        if not p: continue
        if p[0] == "diffuseTexture": tex = os.path.join(d, os.path.basename(p[1]))
        if p[0] == "transparent" and p[1] == "True": alpha = True
    return tex, alpha

def blender_pos(p): return Vector((p[0] * 0.1, -p[2] * 0.1, p[1] * 0.1))

def make_obj(name, pts, wts, f, vt, mat, keep=None):
    idx = {}
    verts, faces, uvs = [], [], []
    for face in f:
        if keep is not None and not all(keep[vi] for vi, _ in face): continue
        nf = []
        for vi, ti in face:
            if vi not in idx: idx[vi] = len(verts); verts.append(blender_pos(pts[vi]))
            nf.append(idx[vi])
        faces.append(nf); uvs.append([vt[ti] if ti >= 0 else (0, 0) for _, ti in face])
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    uv = me.uv_layers.new(name="UVMap")
    li = 0
    for poly, fu in zip(me.polygons, uvs):
        for k, li2 in enumerate(poly.loop_indices): uv.data[li2].uv = fu[k]
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    vg = {}
    for vi, ni in idx.items():
        for b, x in wts[vi].items():
            if b not in vg: vg[b] = ob.vertex_groups.new(name=b)
            vg[b].add([ni], x, "REPLACE")
    for p in me.polygons: p.use_smooth = True
    return ob

def material(tag, tex, alpha, manifest):
    m = bpy.data.materials.get(tag) or bpy.data.materials.new(tag)
    if tex and os.path.exists(tex):
        dst = os.path.join(OUT, "Tex", tag + ".png")
        if not os.path.exists(dst): shutil.copyfile(tex, dst)
    manifest[tag] = {"alpha": alpha}
    return m

def decimate(ob, budget):
    n = len(ob.data.vertices)
    if n <= budget: return
    mod = ob.modifiers.new("dec", "DECIMATE"); mod.ratio = budget / n
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=mod.name)

manifest = {"materials": {}, "people": []}
for (name, gender, race, age, weight, height, skin, body, clothes, hair, eyes, brows) in PEOPLE:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    c = morph(gender, race, age, weight, height)
    parts = []
    dele = set()
    items = [("clothes" if not cl.startswith("shoes") else "shoes", os.path.join(A, "clothes", cl, cl + ".mhclo")) for cl in clothes]
    grey = hair.endswith("_grey"); hair = hair.replace("_grey", "")
    items.append(("hair", os.path.join(A, "hair", hair, hair + ".mhclo")))
    items.append(("brows", os.path.join(A, "eyebrows", brows, brows + ".mhclo")))
    items.append(("eyes", os.path.join(A, "eyes", "low-poly", "low-poly.mhclo")))
    for kind, path in items:
        refs, scale, dl, meta = read_clo(path); dele |= dl
        v, vt, f = read_obj(os.path.join(os.path.dirname(path), meta.get("obj_file", os.path.basename(path)[:-6] + ".obj")))
        pts, wts = fit(refs, scale, c)
        if kind == "eyes": tex, alpha = mat_info(os.path.join(A, "eyes", "materials", eyes + ".mhmat")); tag = "Eye_" + eyes
        else:
            mm = os.path.join(os.path.dirname(path), meta.get("material", ""))
            tex, alpha = mat_info(mm) if os.path.isfile(mm) else (None, False); tag = os.path.basename(os.path.dirname(path))
        if kind in ("hair", "brows"): alpha = True
        if kind == "hair" and grey: tag += "_grey"
        ob = make_obj(kind, pts, wts, f, vt, material(tag, tex, alpha, manifest["materials"]))
        decimate(ob, BUDGET.get(kind, 99999)); parts.append(ob)
    # body proxy minus everything hidden under the clothes
    refs, scale, _, meta = read_clo(os.path.join(A, "proxymeshes", body, body + ".proxy"))
    v, vt, f = read_obj(os.path.join(A, "proxymeshes", body, meta.get("obj_file", body + ".obj")))
    pts, wts = fit(refs, scale, c)
    keep = [not all(r in dele for r in rr[0]) for rr in refs]
    tex, _ = mat_info(os.path.join(A, "skins", skin, skin + ".mhmat"))
    bo = make_obj("body", pts, wts, f, vt, material("Skin_" + skin, tex, False, manifest["materials"]), keep)
    decimate(bo, BUDGET["body"]); parts.insert(0, bo)

    # skeleton
    def jpos(spec, default_cube=None):
        if spec["strategy"] == "CUBE": vs = groups[spec["cube_name"]]
        elif spec["strategy"] == "VERTEX": vs = [spec["vertex_index"]]
        else: vs = spec["vertex_indices"]
        p = [sum(c[i][k] for i in vs) / len(vs) for k in range(3)]
        return blender_pos(p)
    arm = bpy.data.armatures.new("Rig"); ao = bpy.data.objects.new("Rig", arm); bpy.context.collection.objects.link(ao)
    bpy.context.view_layer.objects.active = ao; bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for b in KEEP:
        e = arm.edit_bones.new(b); e.head = jpos(rig[b]["head"]); e.tail = jpos(rig[b]["tail"])
        if (e.tail - e.head).length < 1e-3: e.tail = e.head + Vector((0, 0, 0.05))
        e.roll = rig[b].get("roll", 0); eb[b] = e
    for b in KEEP:
        p = rig[b]["parent"]
        while p and p not in KEEP: p = rig[p]["parent"]
        if p: eb[b].parent = eb[p]
    bpy.ops.object.mode_set(mode="OBJECT")

    # one mesh, feet on the ground
    for o in bpy.context.view_layer.objects: o.select_set(o in parts)
    bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join()
    me = parts[0]; me.name = name
    z0 = min(v.co.z for v in me.data.vertices)
    for v in me.data.vertices: v.co.z -= z0
    ao.location.z = -z0; bpy.context.view_layer.objects.active = ao; ao.select_set(True); me.select_set(False)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    me.parent = ao; mod = me.modifiers.new("Armature", "ARMATURE"); mod.object = ao
    # far LOD: a quarter of the triangles, same skeleton
    lod = me.copy(); lod.data = me.data.copy(); bpy.context.collection.objects.link(lod)
    d = lod.modifiers.new("dec", "DECIMATE"); d.ratio = 0.28; bpy.context.view_layer.objects.active = lod
    for o in bpy.context.view_layer.objects: o.select_set(o == lod)
    bpy.ops.object.modifier_apply(modifier=d.name)
    me.name = name + "_LOD0"; lod.name = name + "_LOD1"
    for o in bpy.context.view_layer.objects: o.select_set(True)
    tris = sum(len(p.vertices) - 2 for p in me.data.polygons)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                             add_leaf_bones=False, bake_anim=False, apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE",
                             use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X")
    manifest["people"].append({"name": name, "female": gender == 0, "age": age, "tris": tris, "height": max(v.co.z for v in me.data.vertices)})
    print("PERSON", name, "tris", tris, "verts", len(me.data.vertices), "h", round(manifest["people"][-1]["height"], 2))
if os.environ.get("PREVIEW"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for i, pp in enumerate(manifest["people"]):
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, pp["name"] + ".fbx"))
        for o in bpy.context.selected_objects:
            if o.parent is None: o.location.x = i * 0.75
            if o.type == "MESH":
                for m in o.data.materials:
                    m.use_nodes = True; n = m.node_tree.nodes; bs = n.get("Principled BSDF")
                    tp = os.path.join(OUT, "Tex", m.name.split(".")[0] + ".png")
                    if bs and os.path.exists(tp):
                        t = n.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(tp)
                        m.node_tree.links.new(t.outputs[0], bs.inputs["Base Color"]); m.node_tree.links.new(t.outputs[1], bs.inputs["Alpha"])
                        m.blend_method = "CLIP"
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); bpy.context.collection.objects.link(cam)
    nn = len(manifest["people"]); cam.location = (nn * 0.375 - 0.4, -nn * 0.95, 1.0); cam.rotation_euler = (math.radians(88), 0, 0); bpy.context.scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); bpy.context.collection.objects.link(sun); sun.rotation_euler = (0.8, 0.2, 0.4); sun.data.energy = 4
    sc = bpy.context.scene; sc.render.engine = "BLENDER_EEVEE_NEXT"; sc.render.resolution_x = 2400; sc.render.resolution_y = 640
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.5, 0.55, 0.6)
    sc.render.filepath = os.path.join(OUT, "preview.png"); bpy.ops.render.render(write_still=True)
json.dump(manifest, open(os.path.join(OUT, "manifest.json"), "w"), indent=1)
print("PEOPLE_DONE")
