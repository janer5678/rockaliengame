"""
Builds the first-person alien hands from the CC0 "WRAD ARMS" model by wriks (Tools/Hands/, see SOURCE.txt) and exports
Assets/Game/Resources/Hands/AlienHands.fbx.

Run headless from the repo root:
    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --factory-startup --python Tools/alien_hands.py
    (add `-- --render <dir>` to render the hands from the eye and the side there)

What it does:
- scales the model to metres (the hand, wrist to fingertip, about 24 cm - bigger than life, so it reads);
- bakes two hand shapes into two copies of each arm, both skinned to the shoulder, upper arm, forearm and wrist bones
  (the fingers fixed in place on the wrist):
    "Fist" - every finger curled round a handle, the thumb over them;
    "Cup"  - the fingers gently curled, the thumb out: a hand that carries or cradles;
    "Wrap" - the fingers well curled round something big and round (the rock), the thumb partly across;
- adds marker bones on each wrist that the game lines things up with (none of them move any skin):
    grip / gripA  - the middle of the fist's hole, and a point along the handle's line from it (towards the little-finger side);
    palm / palmN  - the middle of the cupped palm, and a point out from it along the palm's normal;
    knuck         - the middle finger's knuckle;
- exports it all (one armature, ArmR_Fist, ArmR_Cup, ArmL_Fist, ArmL_Cup) facing -Y (Unity +Z), the eye at the origin.
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(HERE, "Hands", "arms.glb")
OUT_DIR = os.path.join(REPO, "Assets", "Game", "Resources", "Hands")
OUT_FBX = os.path.join(OUT_DIR, "AlienHands.fbx")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[argv.index("--render") + 1] if "--render" in argv else None

SCALE = 0.125
FINGERS = ["index", "middle", "ring", "pinky"]
# the curls (degrees about each finger bone's own Z - the knuckle line): knuckle, middle joint, end joint
CURL = float(os.environ.get("CURL_SIGN", "1"))
FIST = {"index": (78, 96, 62), "middle": (84, 98, 64), "ring": (88, 100, 64), "pinky": (92, 100, 64)}
CUP = {"index": (16, 22, 14), "middle": (20, 26, 16), "ring": (24, 30, 18), "pinky": (28, 32, 20)}
# wrapped round something big and round (the rock): the fingers well curled, the thumb partly across
WRAP = {"index": (38, 46, 30), "middle": (42, 50, 32), "ring": (46, 52, 34), "pinky": (50, 54, 36)}
WRAP_THUMB = [(-18, 0, -4), (-12, 0, 0), (-10, 0, 0)]
FIST_THUMB = [tuple(float(v) for v in t.split(",")) for t in os.environ.get("THUMB", "-40,0,-8;-26,0,0;-18,0,0").split(";")]  # (x, y, z) per thumb bone: across and over the curled fingers
CUP_THUMB = [(0, 0, 0), (0, 0, 8), (0, 0, 6)]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.name != "arms_mesh"]: bpy.data.objects.remove(o, do_unlink=True)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = bpy.data.objects["arms_mesh"]
arm.name = "AlienHandsRig"
# the eye at the origin (the model's "head" bone), facing -Y like a Blender model for Unity (+Z there); in metres
head = arm.matrix_world @ arm.data.bones["head"].head_local
arm.location = -head * SCALE
arm.scale = (SCALE, SCALE, SCALE)
arm.rotation_euler = (0, 0, math.pi)
arm.location = Matrix.Rotation(math.pi, 4, 'Z') @ arm.location
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def mode(m):
    bpy.context.view_layer.objects.active = arm
    if bpy.context.object.mode != m: bpy.ops.object.mode_set(mode=m)

# ---- a flat material (the game colours it) ----
mat = bpy.data.materials.new("AlienSkin")
mesh.data.materials.clear(); mesh.data.materials.append(mat)
for p in mesh.data.polygons: p.use_smooth = True

def pose(curls, thumb):
    mode('POSE')
    for pb in arm.pose.bones: pb.rotation_mode = 'XYZ'; pb.rotation_euler = (0, 0, 0)
    for s in (".r", ".l"):
        for f in FINGERS:
            for j in range(3):
                arm.pose.bones[f"finger_{f}{j + 1}{s}"].rotation_euler = (0, 0, math.radians(curls[f][j]) * CURL * (1 if s == ".r" else -1))
        for j in range(3):
            x, y, z = thumb[j]
            if s == ".l": y, z = -y, -z
            arm.pose.bones[f"finger_thumb{j + 1}{s}"].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
    mode('OBJECT')
    bpy.context.view_layer.update()

def P(name):  # a bone's head where it is now (posed)
    return arm.matrix_world @ arm.pose.bones[name].head

def bake(name, curls, thumb):
    """A copy of the mesh with the fingers fixed in this pose, skinned to the arm bones only."""
    pose(curls, thumb)
    cp = mesh.copy(); cp.data = mesh.data.copy(); cp.name = name
    bpy.context.scene.collection.objects.link(cp)
    bpy.ops.object.select_all(action='DESELECT')
    cp.select_set(True); bpy.context.view_layer.objects.active = cp
    for m in list(cp.modifiers):
        if m.type == 'ARMATURE': bpy.ops.object.modifier_apply(modifier=m.name)
    # the fingers' weights go to the wrist (they're part of the hand now)
    for s in (".r", ".l"):
        wrist = cp.vertex_groups.get("wrist" + s)
        fg = [cp.vertex_groups.get(f"finger_{f}{j}{s}") for f in FINGERS + ["thumb"] for j in (1, 2, 3)]
        fg = [g for g in fg if g is not None]
        ids = {g.index for g in fg}
        for v in cp.data.vertices:
            w = sum(ge.weight for ge in v.groups if ge.group in ids)
            if w > 0: wrist.add([v.index], w, 'ADD')
        for g in fg: cp.vertex_groups.remove(g)
    mod = cp.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
    return cp

fist = bake("Fist", FIST, FIST_THUMB)
fistPts = {}
for s in (".r", ".l"):
    heads = [P(f"finger_{f}{j}{s}") for f in FINGERS for j in (1, 2, 3)]
    knuckles = [P(f"finger_{f}1{s}") for f in FINGERS]
    fistPts[s] = (sum(heads, Vector()) / len(heads), (knuckles[3] - knuckles[0]).normalized(), P(f"finger_middle1{s}"))
cup = bake("Cup", CUP, CUP_THUMB)
wrap = bake("Wrap", WRAP, WRAP_THUMB)
cupPts = {}
for s in (".r", ".l"):
    w = P("wrist" + s)
    ks = [P(f"finger_{f}1{s}") for f in FINGERS]
    centre = (w + sum(ks, Vector()) / 4) / 2
    fwd = (ks[1] - w).normalized()
    across = (ks[3] - ks[0]).normalized()
    n = fwd.cross(across) if s == ".r" else across.cross(fwd)
    # (which side is the palm: the side the fingertips curl towards)
    tip = P(f"finger_middle3{s}")
    if n.dot(tip - (w + fwd * (tip - w).dot(fwd))) < 0: n = -n
    cupPts[s] = (centre + n * 0.012, n)
pose({f: (0, 0, 0) for f in FINGERS}, [(0, 0, 0)] * 3)
mesh.select_set(False)
bpy.data.objects.remove(mesh, do_unlink=True)

# ---- the marker bones ----
mode('EDIT')
eb = arm.data.edit_bones
def marker(name, parent, at):
    b = eb.new(name); b.head = at; b.tail = at + Vector((0, 0, 0.01)); b.parent = eb[parent]; b.use_deform = False
for s in (".r", ".l"):
    g, a, k = fistPts[s]
    marker("grip" + s, "wrist" + s, g)
    marker("gripA" + s, "wrist" + s, g + a * 0.05)
    marker("knuck" + s, "wrist" + s, k)
    c, n = cupPts[s]
    marker("palm" + s, "wrist" + s, c)
    marker("palmN" + s, "wrist" + s, c + n * 0.05)
mode('OBJECT')

# ---- each arm its own meshes ----
def split(obj, base):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    right = {g.index for g in obj.vertex_groups if g.name.endswith(".r")}
    bpy.ops.object.mode_set(mode='EDIT')
    b = bmesh.from_edit_mesh(obj.data)
    dl = b.verts.layers.deform.verify()
    for f in b.faces: f.select = sum(sum(w for i, w in v[dl].items() if i in right) for v in f.verts) > len(f.verts) * 0.5
    bmesh.update_edit_mesh(obj.data)
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in bpy.context.selected_objects:
        isR = o != obj
        o.name = ("ArmR_" if isR else "ArmL_") + base; o.data.name = o.name
split(fist, "Fist")
split(cup, "Cup")
split(wrap, "Wrap")
parts = [o for o in bpy.data.objects if o.type == 'MESH']
for o in parts: print("[alien_hands]", o.name, "verts", len(o.data.vertices))

os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for o in parts: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP', mesh_smooth_type='OFF', use_armature_deform_only=False,
                         primary_bone_axis='Y', secondary_bone_axis='X')
print("[alien_hands] exported", OUT_FBX)

if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'SINGLE'; sc.display.shading.single_color = (0.18, 0.33, 0.8)
    sc.render.resolution_x = 640; sc.render.resolution_y = 480
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    g = fistPts[".r"][0]
    # a handle through the fist's grip (to check the hole lines up)
    a = fistPts[".r"][1]
    bpy.ops.mesh.primitive_cylinder_add(radius=0.016, depth=0.3, location=g)
    rod = bpy.context.active_object
    rod.rotation_euler = Vector((0, 0, 1)).rotation_difference(a).to_euler()
    for show in ("Fist", "Cup"):
        rod.hide_render = show != "Fist"
        for o in parts: o.hide_render = not o.name.endswith(show)
        for name, loc, look, lens in (("eye", Vector((0, 0, 0)), Vector((0, -1, -0.55)), 18), ("hand", g + Vector((0.25, -0.1, 0.12)), g, 40), ("under", g + Vector((-0.05, -0.12, -0.25)), g, 40)):
            cam.location = loc
            d = (look if name == "eye" else look - loc)
            cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
            cam.data.lens = lens
            sc.render.filepath = os.path.join(RENDER_DIR, f"{show}_{name}.png")
            bpy.ops.render.render(write_still=True)
