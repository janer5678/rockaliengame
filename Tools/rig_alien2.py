"""
Rigs the "PS1 Low Poly Grey Alien" (Tools/Alien2/Alien2.obj) for the game and exports
Assets/Game/Resources/Alien/AlienRigged.fbx (+ copies the texture to Assets/Game/Resources/Alien/Alien2.png).

Run headless from the repo root:
    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --python Tools/rig_alien2.py
    (add `-- --render <dir>` to also render test poses there)

Matches the contract BodyAnimator.cs expects: a Generic-rig skinned mesh 1.8 m tall, feet on y=0, facing +Z in Unity
(-Y in Blender), with humanoid bone names (Hips, Spine, Chest, Neck, Head, Left/Right UpperArm/LowerArm/Hand,
Left/Right UpperLeg/LowerLeg/Foot). The model is in an A-pose; BodyAnimator.Straighten() re-bases the limbs.
"""
import bpy, bmesh, math, os, shutil, sys
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(HERE, "Alien2", "Alien2.obj")
SRC_TEX = os.path.join(HERE, "Alien2", "Alientexturedtry2.png")
OUT_DIR = os.path.join(REPO, "Assets", "Game", "Resources", "Alien")
OUT_FBX = os.path.join(OUT_DIR, "AlienRigged.fbx")
OUT_TEX = os.path.join(OUT_DIR, "Alien2.png")
HEIGHT = 1.8

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[argv.index("--render") + 1] if "--render" in argv else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=SRC)
mesh = bpy.context.selected_objects[0]
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mesh.name = "Alien"
mesh.data.name = "Alien"

# ---- scale to 1.8 m, feet on the ground, centred on x (the model already faces -Y, i.e. +Z in Unity) ----
co = [v.co.copy() for v in mesh.data.vertices]
zmin = min(c.z for c in co); zmax = max(c.z for c in co)
S = HEIGHT / (zmax - zmin)
for v in mesh.data.vertices:
    v.co = Vector((v.co.x * S, v.co.y * S, (v.co.z - zmin) * S))
mesh.data.update()

# ---- skeleton (joint positions measured on the source mesh, in source units, then scaled) ----
def P(x, y, z): return Vector((x * S, y * S, (z - zmin) * S))
J = {
    "hips": P(0, 0.0, 1.50), "spine": P(0, 0.0, 1.78), "chest": P(0, 0.0, 2.05),
    "neck": P(0, 0.0, 2.55), "head": P(0, 0.0, 2.76), "top": P(0, 0.0, 3.25),
    "shoulder": P(0.27, 0.0, 2.54), "elbow": P(0.775, 0.005, 2.19), "wrist": P(1.08, -0.03, 1.92), "finger": P(1.33, -0.03, 1.69),
    "hip": P(0.20, 0.0, 1.56), "knee": P(0.26, -0.01, 0.95), "ankle": P(0.31, 0.04, 0.13), "toe": P(0.31, -0.30, 0.02),
}

arm_data = bpy.data.armatures.new("AlienArmature")
arm = bpy.data.objects.new("AlienArmature", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones

def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head = head; b.tail = tail
    if parent: b.parent = eb[parent]; b.use_connect = connect
    return b

def mx(v): return Vector((-v.x, v.y, v.z))

bone("Hips", J["hips"], J["spine"])
bone("Spine", J["spine"], J["chest"], "Hips", True)
bone("Chest", J["chest"], J["neck"], "Spine", True)
bone("Neck", J["neck"], J["head"], "Chest", True)
bone("Head", J["head"], J["top"], "Neck", True)
for side, f in (("Left", lambda v: v), ("Right", mx)):
    bone(side + "UpperArm", f(J["shoulder"]), f(J["elbow"]), "Chest")
    bone(side + "LowerArm", f(J["elbow"]), f(J["wrist"]), side + "UpperArm", True)
    bone(side + "Hand", f(J["wrist"]), f(J["finger"]), side + "LowerArm", True)
    bone(side + "UpperLeg", f(J["hip"]), f(J["knee"]), "Hips")
    bone(side + "LowerLeg", f(J["knee"]), f(J["ankle"]), side + "UpperLeg", True)
    bone(side + "Foot", f(J["ankle"]), f(J["toe"]), side + "LowerLeg", True)
# rolls: z axis of every bone points backwards/up consistently (doesn't matter to BodyAnimator, but keeps it tidy)
for b in eb:
    b.align_roll(Vector((0, 1, 0)) if abs(b.vector.normalized().y) < 0.9 else Vector((0, 0, 1)))
bpy.ops.object.mode_set(mode='OBJECT')

# ---- skin: automatic weights, then clean-up so no limb pulls on the other side of the body ----
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')

bones = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones}

def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (a + ab * t - p).length

def allowed(name, p):
    # limbs only on their own side; legs not above the waist, arms not below their reach etc.
    if name.startswith("Left") and p.x < -0.005: return False
    if name.startswith("Right") and p.x > 0.005: return False
    if "Leg" in name or "Foot" in name:
        if p.z > J["hips"].z + 0.06: return False
    if name in ("Head", "Neck") and p.z < J["neck"].z - 0.08: return False
    return True

vg = {g.name: g for g in mesh.vertex_groups}
for v in mesh.data.vertices:
    p = v.co
    w = {}
    for g in v.groups:
        n = mesh.vertex_groups[g.group].name
        if g.weight > 0.0 and allowed(n, p): w[n] = g.weight
    if not w:
        # fall back to the nearest allowed bone
        best = min((seg_dist(p, *bones[n]), n) for n in bones if allowed(n, p))
        w = {best[1]: 1.0}
    # shoulders: blend the top of the shoulder smoothly between chest and upper arm along the arm, so that when the
    # arms hang down (the game's rest pose) the shoulder rounds off instead of leaving a pointy pad sticking out
    side = "Left" if p.x > 0 else "Right"
    sh, el = bones[side + "UpperArm"]
    adir = (el - sh).normalized()
    t = (p - sh).dot(adir)
    if abs(p.x) > 0.11 and p.z > J["chest"].z + 0.1 and t < 0.1:
        wa = max(0.0, min(1.0, (t + 0.08) / 0.15))
        w = {side + "UpperArm": wa, "Chest": 1.0 - wa}
    # keep the 4 strongest, normalise
    top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
    tot = sum(x for _, x in top)
    for g in mesh.vertex_groups: g.remove([v.index])
    for n, x in top:
        if x / tot > 0.01: vg[n].add([v.index], x / tot, 'REPLACE')

# ---- lower the arms: the source is in a wide A-pose (arms ~35 degrees under horizontal). BodyAnimator's idle is the
# rest pose with small offsets, so bake a relaxed pose (upper arms ~62 degrees under horizontal, like the old model)
# into the mesh and make it the new rest pose ----
ARM_DOWN_DEG = 62.0
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
for side, sgn in (("Left", 1), ("Right", -1)):
    pbn = arm.pose.bones[side + "UpperArm"]
    d = (pbn.tail - pbn.head).normalized()
    a = math.radians(ARM_DOWN_DEG)
    want = Vector((sgn * math.cos(a), d.y, -math.sin(a))).normalized()
    q = d.rotation_difference(want)
    h = pbn.head.copy()
    pbn.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pbn.matrix
    bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.objects.active = mesh
mod = next(m for m in mesh.modifiers if m.type == 'ARMATURE')
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT')
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
mod = mesh.modifiers.new("Armature", 'ARMATURE')
mod.object = arm
bones = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones}

# ---- material: one textured material; PlayerNet tints it in the team colour ----
mat = bpy.data.materials.new("Alien2")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")
tex = nt.nodes.new("ShaderNodeTexImage")
os.makedirs(OUT_DIR, exist_ok=True)
shutil.copyfile(SRC_TEX, OUT_TEX)
tex.image = bpy.data.images.load(OUT_TEX)
tex.interpolation = 'Closest'
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 1.0
head_mat = mat.copy()
head_mat.name = "Alien2_Head"  # same texture; the head is tinted lighter than the body in the game
mesh.data.materials.clear()
mesh.data.materials.append(mat)
mesh.data.materials.append(head_mat)
neck_cut = (J["neck"].z + J["head"].z) * 0.5
for poly in mesh.data.polygons:
    poly.material_index = 1 if poly.center.z > neck_cut else 0

# ---- export ----
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True); arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', mesh_smooth_type='OFF',
                         use_armature_deform_only=True, primary_bone_axis='Y', secondary_bone_axis='X')
print("[rig_alien2] exported", OUT_FBX, "scale", S)
for b in arm_data.bones:
    print("[rig_alien2] bone %-14s head %s tail %s" % (b.name, tuple(round(x, 3) for x in b.head_local), tuple(round(x, 3) for x in b.tail_local)))

# ---- optional: render test poses to check the skinning ----
if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x = 900; scene.render.resolution_y = 600
    cam_data = bpy.data.cameras.new("cam"); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 3.0
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
    pb = arm.pose.bones

    def turn(name, axis, deg):
        # rotate a bone (and everything below it) about its head, in world space; like BodyAnimator's character space
        b = pb[name]
        h = b.head.copy()
        b.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-h) @ b.matrix
        bpy.context.view_layer.update()

    def arms_down():
        # what BodyAnimator.Straighten() does: arms hanging at the sides
        for side, sgn in (("Left", 1), ("Right", -1)):
            d = (pb[side + "LowerArm"].head - pb[side + "UpperArm"].head).normalized()
            want = Vector((0.1 * sgn, 0, -1)).normalized()
            q = d.rotation_difference(want)
            ax, ang = q.to_axis_angle()
            turn(side + "UpperArm", ax, math.degrees(ang))

    X = Vector((1, 0, 0))
    poses = {
        "rest": [],
        "idle": [arms_down],
        "walk": [arms_down, lambda: turn("LeftUpperLeg", X, 35), lambda: turn("LeftLowerLeg", X, -50),
                 lambda: turn("RightUpperLeg", X, -30), lambda: turn("LeftUpperArm", X, -35), lambda: turn("RightUpperArm", X, 35),
                 lambda: turn("LeftLowerArm", X, 30), lambda: turn("RightLowerArm", X, 30)],
        "swingup": [arms_down, lambda: turn("RightUpperArm", X, 165), lambda: turn("RightLowerArm", X, 70),
                    lambda: turn("LeftUpperArm", X, 90)],
        "crouch": [arms_down, lambda: turn("Spine", X, -25), lambda: turn("LeftUpperLeg", X, 75), lambda: turn("RightUpperLeg", X, 75),
                   lambda: turn("LeftLowerLeg", X, -115), lambda: turn("RightLowerLeg", X, -115)],
    }
    for name, steps in poses.items():
        for b in pb:
            b.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        for st in steps: st()
        for view, pos in (("front", (0, -6, 0.95)), ("side", (6, 0, 0.95))):
            cam.location = pos
            cam.rotation_euler = (Vector((0, 0, 0.95)) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(RENDER_DIR, "%s_%s.png" % (name, view))
            bpy.ops.render.render(write_still=True)
