"""
Builds the game's first-person ALIEN ARMS and exports Assets/Game/Resources/FpsArms/AlienArms.fbx.

Simple, round and themed on the alien players: a slender smooth arm, an egg-shaped palm, three round fingers and a
thumb, each finger ending in a little ball of a fingertip like a classic grey alien's. The shapes are metaballs (so
everything flows together into one smooth, rounded skin with no seams), turned into a mesh and skinned to the arm rig
of the CC0 "fps arms (rigged only)" model (Tools/FpsArms - only its skeleton is used: shoulders, elbows, wrists and
every finger joint, the little finger's bones taken off), so ViewModel.Rig.cs poses it and bends the elbows exactly as
before.

Run headless from the repo root:
    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --factory-startup --python Tools/alien_arms.py
    (add `-- --render <dir>` to render it from a few sides there, a few fingers curled to check the skinning)
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(HERE, "FpsArms", "FpsArmsRig1.fbx")
OUT_DIR = os.path.join(REPO, "Assets", "Game", "Resources", "FpsArms")
OUT_FBX = os.path.join(OUT_DIR, "AlienArms.fbx")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[argv.index("--render") + 1] if "--render" in argv else None

# ---- the shapes (metres: the radius of each part's surface) ----
UPPER_R, ELBOW_R, FORE_R, WRIST_R = 0.034, 0.030, 0.025, 0.022
PALM = (0.034, 0.016, 0.042)      # (half width, half thickness, half length) of the egg of a palm
FINGER_R, TIP_R = 0.0102, 0.0138  # each finger, and its round fingertip
THUMB_R0, THUMB_R, THUMB_TIP = 0.0145, 0.0108, 0.0132
RES = 0.0034                      # metaball detail (smaller: smoother, more polygons)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
arm.name = "AlienArmsRig"
for o in [o for o in bpy.data.objects if o.type == 'MESH']: bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
# (no little fingers: the aliens have three fingers and a thumb)
bpy.ops.object.mode_set(mode='EDIT')
for b in [b for b in arm.data.edit_bones if b.name.startswith("f_pinky.") or b.name.startswith("palm_pinky.")]: arm.data.edit_bones.remove(b)
bpy.ops.object.mode_set(mode='OBJECT')
B = arm.data.bones
def head(n): return arm.matrix_world @ B[n].head_local
def tail(n): return arm.matrix_world @ B[n].tail_local

FINGERS = ["f_index", "f_middle", "f_ring"]

def capsule(mb, a, b, r, stiff=2.0):
    """A metaball capsule from a to b, its surface about r round."""
    el = mb.elements.new(type='CAPSULE')
    d = b - a
    el.co = (a + b) / 2
    el.size_x = d.length / 2
    el.radius = r * K
    el.stiffness = stiff
    el.rotation = Vector((1, 0, 0)).rotation_difference(d.normalized())
    return el

def ball(mb, c, r, stiff=2.0):
    el = mb.elements.new(type='BALL')
    el.co = c
    el.radius = r * K
    el.stiffness = stiff
    return el

def ellipsoid(mb, c, axes, half, stiff=2.0):
    """An egg: half-sizes along the three axes (unit vectors x, y, z)."""
    el = mb.elements.new(type='ELLIPSOID')
    el.co = c
    x, y, z = axes
    m = (x.to_3d(), y.to_3d(), z.to_3d())
    from mathutils import Matrix
    rot = Matrix((m[0], m[1], m[2])).transposed().to_quaternion()
    el.rotation = rot
    el.radius = K * max(half)
    el.size_x = half[0] / max(half); el.size_y = half[1] / max(half); el.size_z = half[2] / max(half)
    el.stiffness = stiff
    return el

# a metaball's surface sits inside its radius: K turns the wanted surface radius into a metaball radius
K = 1.55

def build_side(s):
    mb = bpy.data.metaballs.new("Arm" + s)
    mb.resolution = RES
    mb.render_resolution = RES
    mb.threshold = 0.6
    obj = bpy.data.objects.new("Arm" + s, mb)
    bpy.context.scene.collection.objects.link(obj)
    sh = head("upper_arm" + s); el = head("forearm" + s); wr = head("hand" + s)
    # the arm: slender, round, a soft elbow, tapering to a narrow wrist
    capsule(mb, sh, el, UPPER_R)
    ball(mb, sh, UPPER_R * 1.05)
    ball(mb, el, ELBOW_R)
    # (one smooth piece each - a chain of short ones comes out lumpy, like beads)
    capsule(mb, el, wr, FORE_R, 1.6)
    ball(mb, el.lerp(wr, 0.25), FORE_R * 1.08, 1.0)  # (fuller towards the elbow)
    ball(mb, wr, WRIST_R, 1.6)
    # the palm: an egg from the wrist to the knuckles
    ki = head("f_index.01" + s); km = head("f_middle.01" + s); kr = head("f_ring.01" + s)
    z = (km - wr).normalized()
    x = (kr - ki); x = (x - z * x.dot(z)).normalized()
    y = z.cross(x)
    centre = wr.lerp(km, 0.55)
    # (pushed a little to the back of the hand: the palm's underside stays flat-ish)
    ellipsoid(mb, centre, (x, y, z), PALM)
    # the fingers: one smooth round piece each (the skinning bends it at the joints), a ball of a fingertip
    for f in FINGERS:
        capsule(mb, head(f"{f}.01{s}"), head(f"{f}.03{s}").lerp(tail(f"{f}.03{s}"), 0.55), FINGER_R, 2.4)
        ball(mb, tail(f"{f}.03{s}"), TIP_R, 2.4)
    # the thumb: a fleshy base, then one round piece, a ball tip
    capsule(mb, head("thumb.01" + s), tail("thumb.01" + s), THUMB_R0, 2.0)
    capsule(mb, head("thumb.02" + s), head("thumb.03" + s).lerp(tail("thumb.03" + s), 0.55), THUMB_R, 2.4)
    ball(mb, tail("thumb.03" + s), THUMB_TIP, 2.4)
    # to a mesh
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    me = bpy.context.view_layer.objects.active
    me.name = "Arm" + s; me.data.name = "Arm" + s
    # smooth out the metaball's ripples, then fewer polygons (it's only ever seen at arm's length)
    sm = me.modifiers.new("smooth", 'LAPLACIANSMOOTH')
    sm.lambda_factor = 0.6; sm.iterations = 12; sm.use_volume_preserve = True
    dec = me.modifiers.new("decimate", 'DECIMATE')
    dec.ratio = 0.45
    bpy.context.view_layer.objects.active = me
    for mod in list(me.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in me.data.polygons: p.use_smooth = True
    return me

meshes = [build_side(".R"), build_side(".L")]
for m in meshes:
    m.name = "ArmR" if m.name.endswith(".R") or m.name == "Arm.R" else ("ArmL" if "L" in m.name else m.name)
    m.data.name = m.name
    print("[alien_arms]", m.name, "verts", len(m.data.vertices), "faces", len(m.data.polygons))

# ---- skinning: each vertex to the nearest bone of its arm, blended with the next bone along at the joints ----
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-12, ab.dot(ab))))
    return (p - (a + ab * t)).length, t

def skin(me, s):
    bones = ["upper_arm", "forearm", "hand"] + [f"{f}.0{j}" for f in FINGERS for j in (1, 2, 3)] + [f"thumb.0{j}" for j in (1, 2, 3)]
    bones = [b + s for b in bones]
    segs = {}
    for b in bones:
        a, c = head(b), tail(b)
        if b.startswith("hand"): c = head("f_middle.01" + s)            # (the palm, wrist to knuckles)
        if b.startswith("forearm"): c = head("hand" + s)
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
        blend = min(0.012, L * 0.35)
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

skin(meshes[0], ".R")
skin(meshes[1], ".L")

# ---- export ----
os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for o in meshes: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', mesh_smooth_type='OFF',
                         use_armature_deform_only=False, primary_bone_axis='Y', secondary_bone_axis='X')
print("[alien_arms] exported", OUT_FBX)

# ---- optional renders (a few fingers curled, to check the skinning) ----
if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for f, ang in (("f_index", 0.0), ("f_middle", 1.0), ("f_ring", 1.4)):
        for j in (1, 2, 3):
            pb = arm.pose.bones[f"{f}.0{j}.R"]
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler = (ang * 0.8, 0, 0)
    bpy.ops.object.mode_set(mode='OBJECT')
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0.2, 0.35, 0.75)
    scene.render.resolution_x = 640; scene.render.resolution_y = 480
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
    hand = head("hand.R")
    for name, off in (("top", Vector((0.0, 0.0, 0.45))), ("side", Vector((-0.4, 0.05, 0.05))), ("palm", Vector((0.05, -0.15, -0.4))), ("arm", Vector((0.25, 0.55, 0.35)))):
        cam.location = hand + off + Vector((0, -0.08, 0))
        cam.rotation_euler = (hand + Vector((0, -0.08, 0)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = 50 if name != "arm" else 30
        scene.render.filepath = os.path.join(RENDER_DIR, "alien_" + name + ".png")
        bpy.ops.render.render(write_still=True)
