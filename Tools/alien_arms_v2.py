"""
Builds the game's first-person ALIEN ARMS, modelled on the alien player model (Tools/Alien2): slender smooth grey arms,
a narrow palm, three long tapering fingers and a thumb, each ending in a small dark claw - and exports
Assets/Game/Resources/FpsArms/AlienArms.fbx (what ViewModel.Rig.cs loads).

How it's made (the way creature hands are box-modelled): a skeleton of points along the arm rig's bones - the shoulder,
elbow and wrist, three branches out across the palm to the knuckles, then each finger and the thumb joint by joint -
with a radius at every point (the Skin modifier wraps a clean quad cage round it), subdivided twice so it's properly
smooth, then skinned to the rig bone by bone (each point to its nearest bone, blended only at the joints). The rig is the CC0 "fps arms (rigged only)" model's skeleton
(Tools/FpsArms/FpsArmsRig1.fbx; its little finger taken off): the grips, the finger posing and the elbow IK in the game
all work on it unchanged. The claws are their own little meshes ("ArmR_claws" / "ArmL_claws") on each finger's last bone.

Run headless from the repo root:
    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --factory-startup --python Tools/alien_arms_v2.py
    (add `-- --render <dir>` to render previews there: from the eye, the hand close up, and a fist)
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(HERE, "FpsArms", "FpsArmsRig1.fbx")
OUT_DIR = os.path.join(REPO, "Assets", "Game", "Resources", "FpsArms")
OUT_FBX = os.path.join(OUT_DIR, "AlienArms.fbx")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[argv.index("--render") + 1] if "--render" in argv else None

FINGERS = ["f_index", "f_middle", "f_ring"]
# radii (metres) - slender like the alien's
R_SHOULDER, R_UPPER, R_ELBOW, R_FORE, R_WRIST = 0.038, 0.034, 0.03, 0.03, 0.022
R_PALM = 0.025                                   # each of the three branches that together make the palm (thick enough to fuse into one)
R_FINGER = [0.0124, 0.0113, 0.0101, 0.0078]       # knuckle, two joints, tip (human thickness, a rounded tip)
R_THUMB = [0.0172, 0.0138, 0.0115, 0.0088]
FINGER_LONGER = 1.0                               # (human length: they hold things like a person's)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in [o for o in bpy.data.objects if o.type == 'MESH']: bpy.data.objects.remove(o, do_unlink=True)
arm.name = "AlienArmsRig"
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
# no little fingers; the fingers a bit longer (the alien's are)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
for b in [b for b in eb if b.name.startswith("f_pinky.") or b.name.startswith("palm_pinky.")]: eb.remove(b)
for s in (".R", ".L"):
    for f in FINGERS:
        for j in (1, 2, 3):
            b = eb[f"{f}.0{j}{s}"]
            d = b.tail - b.head
            shift = d * (FINGER_LONGER - 1.0)
            b.tail = b.head + d * FINGER_LONGER
            # (the bones after it move along with it)
            for c in [eb[f"{f}.0{k}{s}"] for k in range(j + 1, 4)] + ([eb.get(f"{f}.03{s}_end")] if eb.get(f"{f}.03{s}_end") else []):
                if c is None: continue
                c.head += shift; c.tail += shift
bpy.ops.object.mode_set(mode='OBJECT')
B = arm.data.bones
def H(n): return arm.matrix_world @ B[n].head_local
def T(n): return arm.matrix_world @ B[n].tail_local

def build_side(s):
    pts, rad, edges = [], [], []
    def add(p, r, prev=None):
        pts.append(p.copy()); rad.append(r)
        i = len(pts) - 1
        if prev is not None: edges.append((prev, i))
        return i
    sh = H("upper_arm" + s); el = H("forearm" + s); wr = H("hand" + s)
    i_sh = add(sh, R_SHOULDER)
    i = add(sh.lerp(el, 0.5), R_UPPER, i_sh)
    i_el = add(el, R_ELBOW, i)
    i = add(el.lerp(wr, 0.35), R_FORE * 1.05, i_el)
    i = add(el.lerp(wr, 0.75), R_FORE * 0.85, i)
    i_wr = add(wr, R_WRIST, i)
    # the palm: three branches from just past the wrist to the knuckles (together a narrow flat palm)
    base = wr + (H("f_middle.01" + s) - wr) * 0.22
    i_base = add(base, R_PALM * 1.25, i_wr)
    for f in FINGERS:
        k = H(f"{f}.01{s}")
        i_mid = add(base.lerp(k, 0.55), R_PALM, i_base)
        i_k = add(k, R_FINGER[0] * 1.05, i_mid)
        prev = i_k
        for j, r in zip((1, 2), R_FINGER[1:3]):
            prev = add(T(f"{f}.0{j}{s}"), r, prev)
        add(T(f"{f}.03{s}"), R_FINGER[3], prev)
    # the thumb, from the heel of the palm
    t0 = H("thumb.01" + s)
    i_t = add(wr.lerp(t0, 0.6), R_THUMB[0] * 1.15, i_wr)
    prev = add(t0, R_THUMB[0], i_t)
    for j, r in zip((1, 2), R_THUMB[1:3]):
        prev = add(T(f"thumb.0{j}{s}"), r, prev)
    add(T("thumb.03" + s), R_THUMB[3], prev)

    me = bpy.data.meshes.new("Arm" + s)
    me.from_pydata([tuple(p) for p in pts], edges, [])
    ob = bpy.data.objects.new("Arm" + s, me)
    bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    sk = ob.modifiers.new("skin", 'SKIN')
    sk.use_smooth_shade = True
    sk.branch_smoothing = 0.6
    for idx, r in enumerate(rad):
        sv = me.skin_vertices[0].data[idx]
        sv.radius = (r, r)
        sv.use_root = idx == i_sh
    ss = ob.modifiers.new("subsurf", 'SUBSURF'); ss.levels = 2; ss.render_levels = 2
    bpy.ops.object.modifier_apply(modifier="skin")
    bpy.ops.object.modifier_apply(modifier="subsurf")
    # the palm: one flat piece - squash the fused branches towards the palm's middle plane (thinner than it's wide),
    # fading out at the wrist and at the knuckles
    ki, km, kr = H("f_index.01" + s), H("f_middle.01" + s), H("f_ring.01" + s)
    z = (km - wr); L = z.length; z.normalize()
    x = (kr - ki); x = (x - z * x.dot(z)).normalized()
    n = z.cross(x).normalized()
    c0 = wr + z * (L * 0.5)
    for v in ob.data.vertices:
        p = v.co
        t = (p - wr).dot(z) / L
        if t < -0.05 or t > 1.12: continue
        w = min(1.0, max(0.0, (t + 0.05) / 0.25)) * min(1.0, max(0.0, (1.12 - t) / 0.2))
        if abs((p - wr).dot(x)) > 0.045: continue
        h = (p - c0).dot(n)
        v.co = p - n * h * (0.42 * w)
    sm = ob.modifiers.new("smooth", 'SMOOTH'); sm.factor = 0.35; sm.iterations = 3
    bpy.ops.object.modifier_apply(modifier="smooth")
    for p in ob.data.polygons: p.use_smooth = True
    ob.name = "ArmR" if s == ".R" else "ArmL"; ob.data.name = ob.name
    return ob

def claws(s):
    """Small dark claws on each fingertip (and the thumb's), each a cone along its last bone."""
    bm = bmesh.new()
    groups = []
    for f in FINGERS + ["thumb"]:
        last = f"{f}.03{s}"
        tip = T(last); d = (T(last) - H(last)).normalized()
        r = (R_FINGER if f != "thumb" else R_THUMB)[3]
        before = len(bm.verts)
        res = bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=r * 0.95, radius2=0.0012, depth=0.009)
        rot = Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=rot, verts=res["verts"])
        bmesh.ops.translate(bm, vec=tip + d * 0.0015 + Vector((0, 0, 0)), verts=res["verts"])
        groups.append((last, list(range(before, len(bm.verts)))))
    me = bpy.data.meshes.new("claws" + s); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(("ArmR" if s == ".R" else "ArmL") + "_claws", me)
    bpy.context.scene.collection.objects.link(ob)
    for p in ob.data.polygons: p.use_smooth = True
    for bone, ids in groups:
        g = ob.vertex_groups.new(name=bone); g.add(ids, 1.0, 'REPLACE')
    ob.parent = arm
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
    return ob

def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-12, ab.dot(ab))))
    return (p - (a + ab * t)).length, t

def skin(me, s):
    bones = ["upper_arm", "forearm", "hand"] + [f"{f}.0{j}" for f in FINGERS for j in (1, 2, 3)] + [f"thumb.0{j}" for j in (1, 2, 3)]
    bones = [b + s for b in bones]
    segs = {}
    for b in bones:
        a, c = H(b), T(b)
        if b.startswith("hand"): c = H("f_middle.01" + s)            # (the palm, wrist to knuckles)
        if b.startswith("forearm"): c = H("hand" + s)
        segs[b] = (a, c)
    nxt = {"upper_arm" + s: "forearm" + s, "forearm" + s: "hand" + s}
    for f in FINGERS:
        nxt[f"{f}.01{s}"] = f"{f}.02{s}"; nxt[f"{f}.02{s}"] = f"{f}.03{s}"
    nxt["thumb.01" + s] = "thumb.02" + s; nxt["thumb.02" + s] = "thumb.03" + s
    prev = {v: k for k, v in nxt.items()}
    for f in FINGERS: prev[f"{f}.01{s}"] = "hand" + s
    prev["thumb.01" + s] = "hand" + s
    groups = {b: me.vertex_groups.new(name=b) for b in bones}
    for v in me.data.vertices:
        p = me.matrix_world @ v.co
        best, bd, bt = None, 1e9, 0.0
        for b, (a, c) in segs.items():
            d, t = seg_dist(p, a, c)
            if b.startswith("hand"): d -= 0.012  # (the palm is wide: its edges belong to the hand, not the fingers)
            if d < bd: best, bd, bt = b, d, t
        w = {best: 1.0}
        # blend into the next / previous bone near a joint
        a, c = segs[best]
        L = (c - a).length
        blend = min(0.016, L * 0.45)  # (a soft bend at each joint)
        if best in nxt and bt * L > L - blend:
            k = 0.5 * (1.0 - (L - bt * L) / blend)
            w = {best: 1.0 - k, nxt[best]: k}
        elif best in prev and bt * L < blend:
            k = 0.5 * (1.0 - (bt * L) / blend)
            w = {best: 1.0 - k, prev[best]: k}
        for b, wt in w.items():
            if wt > 0: groups[b].add([v.index], wt, 'REPLACE')
    mod = me.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    me.parent = arm


parts = []
for s in (".R", ".L"):
    ob = build_side(s)
    # skin it: automatic weights
    # skinned bone by bone: each point to its nearest bone, blended half and half only right at the joints - so the
    # fingers bend at their knuckles like fingers (automatic weights blurred each bend along the whole finger: noodles)
    skin(ob, s)
    parts.append(ob)
    parts.append(claws(s))
for o in parts: print("[alien_arms_v2]", o.name, "verts", len(o.data.vertices), "faces", len(o.data.polygons))

os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for o in parts: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP', mesh_smooth_type='OFF', use_armature_deform_only=False,
                         primary_bone_axis='Y', secondary_bone_axis='X')
print("[alien_arms_v2] exported", OUT_FBX)

if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'OBJECT'
    for o in parts: o.color = (0.08, 0.08, 0.12, 1) if "claws" in o.name else (0.2, 0.32, 0.62, 1)
    sc.display.shading.show_specular_highlight = True
    sc.render.resolution_x = 800; sc.render.resolution_y = 600
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    hand = H("hand.R")
    def shot(name, loc, look, lens):
        cam.location = loc
        cam.rotation_euler = (look - loc).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        sc.render.filepath = os.path.join(RENDER_DIR, name + ".png")
        bpy.ops.render.render(write_still=True)
    shot("top", hand + Vector((0.05, -0.12, 0.3)), hand + Vector((0, -0.1, 0)), 45)
    shot("side", hand + Vector((-0.3, -0.12, 0.05)), hand + Vector((0, -0.1, 0)), 45)
    shot("under", hand + Vector((0.05, -0.15, -0.3)), hand + Vector((0, -0.1, 0)), 45)
    # a fist
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for f in FINGERS:
        for j, a in zip((1, 2, 3), (80, 95, 70)):
            pb = arm.pose.bones[f"{f}.0{j}.R"]; pb.rotation_mode = 'XYZ'; pb.rotation_euler = (math.radians(a), 0, 0)
    bpy.ops.object.mode_set(mode='OBJECT')
    shot("fist", hand + Vector((0.05, -0.12, 0.3)), hand + Vector((0, -0.1, 0)), 45)
    shot("fist_side", hand + Vector((-0.3, -0.12, 0.05)), hand + Vector((0, -0.1, 0)), 45)
