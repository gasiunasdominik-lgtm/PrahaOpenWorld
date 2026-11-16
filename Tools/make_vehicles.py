"""Vehicle models for STOVĚŽATÁ, built procedurally in Blender (no third-party meshes) after photos of what drives in Prague:
a small hatchback (the shape of a Fabia-class car), a family estate (Octavia-class combi), a compact SUV, a delivery van
(Transporter-class) and a Prague taxi (the estate with its yellow roof sign). Each car: a bevelled body with real wheel arches,
a glass greenhouse you can see into (pillars, roof), an interior (dashboard, steering wheel, seats with headrests), lights,
grille, plates, mirrors and separate wheels (tyre, five-spoke rim, hub). Exported per model to an FBX with:
  Body (detail mesh), Wheel_FL/FR/RL/RR (origins at the axles), Seat_Driver and Front (empties), Low (one ~1k-tri mesh for
  the thousands of parked cars). Blender: +X forward, +Y left, +Z up; the game turns the model by its Front marker.
Materials by name: Paint, Glass, Black, Chrome, Tyre, Rim, Head, Tail, Indicator, Plate, Interior, Seat, Taxi.
Usage: blender -b --factory-startup --python Tools/make_vehicles.py -- <out dir>
"""
import math, os, sys
import bpy, bmesh
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Vehicles")
MATS = {}


def mat(name):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); MATS[name] = m
    return m


def clear():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for me in list(bpy.data.meshes): bpy.data.meshes.remove(me)


def new_obj(name, bm, m):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mat(m))
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    return ob


def prism(name, prof, width, m, top_scale=1.0, z_split=None):
    """A side profile [(x, z)] extruded across y (-w/2..w/2); top_scale narrows the verts above z_split (tumblehome)."""
    bm = bmesh.new(); L, R = [], []
    for x, z in prof:
        s = top_scale if (z_split is not None and z > z_split) else 1.0
        L.append(bm.verts.new((x, width / 2 * s, z))); R.append(bm.verts.new((x, -width / 2 * s, z)))
    bm.faces.new(L[::-1]); bm.faces.new(R)
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((L[i], L[j], R[j], R[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj(name, bm, m)


def box(name, c, size, m, rot=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    if rot: bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=c, verts=bm.verts)
    return new_obj(name, bm, m)


def between(name, a, b, t, m, t2=None):
    a, b = Vector(a), Vector(b); d = b - a; L = d.length
    rot = d.to_track_quat("Z", "X").to_matrix().to_4x4()
    return box(name, (a + b) / 2, (t, t2 or t, L), m, rot)


def cyl(name, c, r, depth, m, axis="Y", seg=24, cap=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, segments=seg, radius1=r, radius2=r, depth=depth)
    if axis == "Y": bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 4, "X"), verts=bm.verts)
    if axis == "X": bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 4, "Y"), verts=bm.verts)
    bmesh.ops.translate(bm, vec=c, verts=bm.verts)
    return new_obj(name, bm, m)


def torus(name, c, R, r, m, seg=24, axis_x=True):
    bm = bmesh.new()
    for i in range(seg):
        a0 = 2 * math.pi * i / seg
        for k in range(8):
            b0 = 2 * math.pi * k / 8
            p = Vector(((R + r * math.cos(b0)) * math.cos(a0), (R + r * math.cos(b0)) * math.sin(a0), r * math.sin(b0)))
            bm.verts.new(p)
    bm.verts.ensure_lookup_table()
    for i in range(seg):
        for k in range(8):
            a = bm.verts[i * 8 + k]; b = bm.verts[((i + 1) % seg) * 8 + k]; c2 = bm.verts[((i + 1) % seg) * 8 + (k + 1) % 8]; d = bm.verts[i * 8 + (k + 1) % 8]
            bm.faces.new((a, b, c2, d))
    if axis_x: bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 4, "Y"), verts=bm.verts)
    bmesh.ops.translate(bm, vec=c, verts=bm.verts)
    return new_obj(name, bm, m)


def modifier(ob, kind, **kw):
    md = ob.modifiers.new(kind, kind)
    for k, v in kw.items(): setattr(md, k, v)
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=md.name)


def bevel(ob, w, segs=3, angle=40):
    modifier(ob, "BEVEL", width=w, segments=segs, limit_method="ANGLE", angle_limit=math.radians(angle))


def cut(ob, cutter):
    modifier(ob, "BOOLEAN", operation="DIFFERENCE", object=cutter, solver="EXACT")
    bpy.data.objects.remove(cutter, do_unlink=True)


def join(name, obs):
    obs = [o for o in obs if o]
    with bpy.context.temp_override(object=obs[0], active_object=obs[0], selected_objects=obs, selected_editable_objects=obs):
        bpy.ops.object.join()
    obs[0].name = name; obs[0].data.name = name
    return obs[0]


def empty(name, p):
    e = bpy.data.objects.new(name, None); e.location = p; bpy.context.collection.objects.link(e); return e


def wheel(name, c, r, w):
    tyre = cyl(name, (0, 0, 0), r, w, "Tyre", "Y", 32); bevel(tyre, 0.05, 3, 30)
    rim = cyl(name + "r", (0, 0, 0), r * 0.64, w + 0.012, "Rim", "Y", 32)
    inner = cyl(name + "i", (0, 0, 0), r * 0.56, w + 0.016, "Black", "Y", 24)
    hub = cyl(name + "h", (0, 0, 0), r * 0.16, w + 0.04, "Rim", "Y", 16)
    spokes = []
    for k in range(5):
        a = 2 * math.pi * k / 5
        for sy in (1, -1):
            p0 = Vector((0, sy * (w / 2 + 0.012), 0)); p1 = Vector((math.cos(a) * r * 0.58, sy * (w / 2 + 0.01), math.sin(a) * r * 0.58))
            spokes.append(between(name + "s", p0, p1, 0.055, "Rim", 0.03))
    wo = join(name, [tyre, rim, inner, hub] + spokes)
    wo.location = c
    return wo


CARS = {
    # L, W, wheelbase, front overhang, tyre r, ground/sill z, belt z, roof z, windscreen base x(from front), screen top x, roof end x(from rear), rear glass base x
    "Hatch": dict(L=3.97, W=1.73, wb=2.56, fo=0.76, r=0.31, sill=0.24, belt=0.96, roof=1.47, ws=1.08, wt=1.86, re=0.32, rg=0.1, rear="hatch"),
    "Estate": dict(L=4.69, W=1.81, wb=2.69, fo=0.92, r=0.32, sill=0.24, belt=0.97, roof=1.46, ws=1.3, wt=2.08, re=0.22, rg=0.07, rear="estate"),
    "Suv": dict(L=4.70, W=1.88, wb=2.79, fo=0.92, r=0.36, sill=0.32, belt=1.1, roof=1.68, ws=1.28, wt=2.02, re=0.24, rg=0.08, rear="estate"),
    "Van": dict(L=4.90, W=1.90, wb=3.0, fo=0.95, r=0.34, sill=0.3, belt=1.12, roof=1.97, ws=0.62, wt=1.28, re=0.05, rg=0.02, rear="van"),
}


def car(name, c, taxi=False):
    clear()
    L, W, wb, r, belt, roof = c["L"], c["W"], c["wb"], c["r"], c["belt"], c["roof"]
    xf, xr = L / 2, -L / 2
    fx, rx = xf - c["fo"], xf - c["fo"] - wb  # axle x
    xs, xt, xe, xg = xf - c["ws"], xf - c["wt"], xr + c["re"], xr + c["rg"]
    van = c["rear"] == "van"
    nose_z = belt - (0.14 if van else 0.2)
    # lower body profile: bumper, nose, bonnet rising to the screen, belt line, tail
    prof = [(xr + 0.06, c["sill"]), (xf - 0.06, c["sill"]), (xf, c["sill"] + 0.22), (xf - 0.01, nose_z - 0.05),
            (xf - 0.12, nose_z + 0.02), (xs, belt)]
    if van: prof += [(xt + 0.3, belt), (xt + 0.3, belt + 0.02)]  # the cab; the cargo box rises to the roof behind it
    prof += [(xe + 0.05, belt + 0.02), (xr + 0.02, belt - 0.03), (xr, c["sill"] + 0.25)]
    body = prism("Body", prof, W, "Paint")
    if van:
        cargo = prism("Cargo", [(xr, belt - 0.05), (xt - 0.05, belt - 0.05), (xt - 0.05, roof - 0.04), (xr + 0.02, roof - 0.04)], W - 0.02, "Paint")
        body = join("Body", [body, cargo])
    bevel(body, 0.07, 3, 35)
    for ax in (fx, rx):
        cut(body, cyl("arch", (ax, 0, r + 0.02), r + 0.07, W + 0.4, "Paint", "Y", 32))
    parts = [body]
    # greenhouse: glass prism with tumblehome, roof skin and pillars on top
    gw = W - 0.14
    gprof = [(xs, belt - 0.02), (xt, roof - 0.03), (xe if not van else xt - 0.05, roof - 0.03), (xg if not van else xt - 0.05, belt - 0.02)]
    glass = prism("Glass", gprof, gw, "Glass", 0.86, belt + 0.05)
    parts.append(glass)
    tw = gw * 0.86
    rend = xe if not van else xt - 0.05
    parts.append(box("Roof", ((xt + rend) / 2 - 0.02, 0, roof + 0.008), (xt - rend + 0.06, tw + 0.04, 0.05), "Paint"))
    for s in (1, -1):
        parts.append(between("A", (xs, s * gw / 2, belt), (xt, s * tw / 2, roof - 0.02), 0.07, "Paint"))
        if not van:
            bx = xt - (xt - xe) * 0.45
            parts.append(between("B", (bx, s * (gw / 2 + 0.005), belt), (bx - 0.05, s * (tw / 2 + 0.005), roof - 0.03), 0.09, "Black"))
            parts.append(between("C", (xe + 0.05, s * tw / 2, roof - 0.03), (xg + 0.05, s * gw / 2, belt + 0.02), 0.16 if c["rear"] == "hatch" else 0.08, "Paint"))
        # mirrors, door handles, side skirt
        parts.append(box("Mirror", (xs - 0.08, s * (W / 2 + 0.09), belt + 0.09), (0.12, 0.16, 0.11), "Paint"))
        parts.append(box("MirrorG", (xs - 0.145, s * (W / 2 + 0.1), belt + 0.09), (0.01, 0.13, 0.08), "Chrome"))
        for hx in ((xs - 0.75, xs - 1.65) if not van else (xs - 0.5,)):
            parts.append(box("Handle", (hx, s * (W / 2 + 0.005), belt - 0.08), (0.16, 0.02, 0.03), "Chrome"))
        parts.append(box("Skirt", ((fx + rx) / 2, s * (W / 2 - 0.02), c["sill"] + 0.07), (wb - 2 * r - 0.25, 0.05, 0.12), "Black"))
        # lights
        parts.append(box("Head", (xf - 0.07, s * (W / 2 - 0.27), nose_z - 0.1), (0.14, 0.42, 0.13), "Head"))
        parts.append(box("Ind", (xf - 0.1, s * (W / 2 - 0.05), nose_z - 0.12), (0.12, 0.08, 0.07), "Indicator"))
        parts.append(box("Tail", (xr + 0.03, s * (W / 2 - 0.2), belt - 0.12 if not van else c["sill"] + 0.6), (0.08, 0.32, 0.17 if not van else 0.4), "Tail"))
    parts.append(box("Grille", (xf + 0.004, 0, c["sill"] + 0.38), (0.04, W * 0.42, 0.17), "Black"))
    parts.append(box("GrilleC", (xf + 0.02, 0, c["sill"] + 0.47), (0.02, W * 0.42, 0.02), "Chrome"))
    parts.append(box("BumperF", (xf - 0.02, 0, c["sill"] + 0.1), (0.1, W - 0.1, 0.12), "Black"))
    parts.append(box("BumperR", (xr + 0.02, 0, c["sill"] + 0.1), (0.1, W - 0.1, 0.12), "Black"))
    parts.append(box("PlateF", (xf + 0.03, 0, c["sill"] + 0.22), (0.02, 0.52, 0.11), "Plate"))
    parts.append(box("PlateR", (xr - 0.01, 0, c["sill"] + 0.42), (0.02, 0.52, 0.11), "Plate"))
    # interior: a dark tub at the belt (doors, floor), dashboard, steering wheel on the left, seats with headrests
    parts.append(box("Tub", ((xs + xg) / 2, 0, belt - 0.03), (xs - xg - 0.1, gw - 0.08, 0.03), "Interior"))
    parts.append(prism("Dash", [(xs - 0.22, belt + 0.02), (xs - 0.3, belt + 0.13), (xs - 0.55, belt + 0.11), (xs - 0.6, belt - 0.08)], gw - 0.12, "Interior"))
    sw = torus("Wheel", (xs - 0.58, 0.37, belt + 0.12), 0.19, 0.022, "Black")
    sw.rotation_euler = (0, math.radians(-62), 0); parts.append(sw)
    seat_x = xs - 1.05
    for row, sx in ((0, seat_x), (1, seat_x - 0.9)):
        if van and row == 1: break
        ys = (0.37, -0.37) if row == 0 else (0.0,)
        for y in ys:
            wdt = 0.5 if row == 0 else gw - 0.3
            parts.append(box("Cushion", (sx, y, belt - 0.4), (0.5, wdt, 0.12), "Seat"))
            back = box("Back", (sx - 0.27, y, belt - 0.02), (0.11, wdt, 0.68), "Seat")
            back.rotation_euler = (0, math.radians(-12), 0); parts.append(back)
            for hy in ((y,) if row == 0 else (0.4, -0.4)):
                parts.append(box("Headrest", (sx - 0.35, hy, belt + 0.42), (0.09, 0.26, 0.18), "Seat"))
    if van:
        parts.append(box("Bulkhead", (xt - 0.08, 0, (belt + roof) / 2), (0.04, gw - 0.05, roof - belt - 0.1), "Interior"))
    if taxi:
        parts.append(box("TaxiSign", ((xt + rend) / 2, 0, roof + 0.1), (0.24, 0.62, 0.16), "Taxi"))
    for p in parts:
        if p.rotation_euler != (0, 0, 0):
            with bpy.context.temp_override(object=p, active_object=p, selected_objects=[p], selected_editable_objects=[p]):
                bpy.ops.object.transform_apply(rotation=True)
    bodyo = join("Body", parts)
    wy = W / 2 - 0.12
    wheels = [wheel("Wheel_FL", (fx, wy, r), r, 0.21), wheel("Wheel_FR", (fx, -wy, r), r, 0.21),
              wheel("Wheel_RL", (rx, wy, r), r, 0.21), wheel("Wheel_RR", (rx, -wy, r), r, 0.21)]
    empty("Seat_Driver", (seat_x, 0.37, belt - 0.34)); empty("Front", (xf + 2, 0, 0))
    # the low mesh for parked cars: body + wheels, decimated
    copies = []
    for o in [bodyo] + wheels:
        cp = o.copy(); cp.data = o.data.copy(); bpy.context.collection.objects.link(cp); copies.append(cp)
        cp.location = o.location
    for cp in copies:
        with bpy.context.temp_override(object=cp, active_object=cp, selected_objects=[cp], selected_editable_objects=[cp]):
            bpy.ops.object.transform_apply(location=True)
    low = join("Low", copies)
    tris_hi = sum(len(p.vertices) - 2 for p in bodyo.data.polygons)
    modifier(low, "DECIMATE", ratio=max(0.05, 1100 / max(1, sum(len(p.vertices) - 2 for p in low.data.polygons))))
    for o in [bodyo, low] + wheels:
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o]):
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    tris_lo = sum(len(p.vertices) - 2 for p in low.data.polygons)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", bake_anim=False)
    print("VEHICLE", name, "body tris", tris_hi, "low tris", tris_lo)
    if os.environ.get("PREVIEW"): preview(name, L)


def preview(name, L):
    cam = bpy.data.cameras.new("c"); co = bpy.data.objects.new("c", cam); bpy.context.collection.objects.link(co)
    co.location = (L * 0.9, L * 0.75, L * 0.38); d = Vector((0, 0, 0.7)) - co.location
    co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler(); bpy.context.scene.camera = co
    sun = bpy.data.lights.new("s", "SUN"); so = bpy.data.objects.new("s", sun); bpy.context.collection.objects.link(so); so.rotation_euler = (0.7, 0.2, 0.6)
    cols = {"Paint": (0.1, 0.25, 0.6, 1), "Glass": (0.1, 0.15, 0.2, 0.35), "Black": (0.03, 0.03, 0.03, 1), "Chrome": (0.8, 0.8, 0.8, 1), "Tyre": (0.02, 0.02, 0.02, 1),
            "Rim": (0.7, 0.7, 0.72, 1), "Head": (1, 1, 0.9, 1), "Tail": (0.8, 0.05, 0.05, 1), "Indicator": (1, 0.6, 0.1, 1), "Plate": (1, 1, 1, 1),
            "Interior": (0.12, 0.12, 0.13, 1), "Seat": (0.2, 0.2, 0.22, 1), "Taxi": (1, 0.85, 0.1, 1)}
    for n, m in MATS.items():
        m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Base Color"].default_value = cols.get(n, (0.5, 0.5, 0.5, 1))
        if n == "Glass": b.inputs["Alpha"].default_value = 0.3; m.blend_method = "BLEND"
    sc = bpy.context.scene; sc.render.engine = "BLENDER_EEVEE_NEXT"; sc.render.resolution_x = 900; sc.render.resolution_y = 560
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.6, 0.65, 0.7)
    sc.render.filepath = os.path.join(OUT, "..", "..", "..", "Screenshots", "veh_" + name + ".png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    only = os.environ.get("ONLY")
    for n, c in CARS.items():
        if not only or only == n: car(n, c)
    if not only or only == "Taxi": car("Taxi", CARS["Estate"], taxi=True)
