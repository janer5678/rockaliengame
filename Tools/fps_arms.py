"""
Turns the CC0 "fps arms (rigged only)" model (Tools/FpsArms/FpsArmsRig1.fbx, see SOURCE.txt) into the game's first-person
alien arms and exports Assets/Game/Resources/FpsArms/AlienArms.fbx.

Run headless from the repo root:
    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --factory-startup --python Tools/fps_arms.py
    (add `-- --render <dir>` to also render it from a few sides there)

What it does:
- the little finger comes off each hand (the aliens have three fingers and a thumb): its vertices go, the hole left at
  the knuckle is closed and smoothed over;
- the armature's 0.1 scale is applied (metres), the whole thing shaded smooth;
- the mesh is split into the right arm ("ArmR") and the left ("ArmL"), both on the one armature, so each first-person
  arm is its own renderer (ViewModel.Hands.cs);
- exported for Unity facing +Z (Blender's -Y), the camera at the origin between the shoulders.
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

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
arm.name = "AlienArmsRig"
mesh.name = "Arms"

# ---- metres: apply the armature's 0.1 scale (and the mesh's own transform) ----
for o in bpy.data.objects: o.select_set(False)
arm.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# ---- off with the little fingers ----
pinky = [g.index for g in mesh.vertex_groups if g.name.startswith("f_pinky.")]
bm = bmesh.new()
bm.from_mesh(mesh.data)
deform = bm.verts.layers.deform.verify()
gone = [v for v in bm.verts if sum(v[deform].get(i, 0.0) for i in pinky) >= 0.5]
print("[fps_arms] little-finger vertices:", len(gone))
bmesh.ops.delete(bm, geom=gone, context='VERTS')
# close each hole left at the knuckle, then smooth the patch and the ring round it so it reads as the side of the palm
before = set(f.index for f in bm.faces)
edges = [e for e in bm.edges if e.is_boundary]
# (only the holes at the hands: the arm's open end at the shoulder stays as it is)
loops = []
seen = set()
for e in edges:
    if e in seen: continue
    # walk the boundary loop
    loop = [e]; seen.add(e)
    stack = [e]
    while stack:
        cur = stack.pop()
        for v in cur.verts:
            for ne in v.link_edges:
                if ne.is_boundary and ne not in seen:
                    seen.add(ne); loop.append(ne); stack.append(ne)
    loops.append(loop)
for loop in loops:
    vs = set(v for e in loop for v in e.verts)
    centre = sum((v.co for v in vs), Vector()) / len(vs)
    span = max((v.co - centre).length for v in vs)
    if span > 0.05: continue  # (the shoulder openings)
    res = bmesh.ops.holes_fill(bm, edges=loop, sides=0)
    patch = set(vs)
    for f in res["faces"]:
        for v in f.verts: patch.add(v)
    ring = set(patch)
    for v in list(patch):
        for e in v.link_edges: ring.add(e.other_vert(v))
    bmesh.ops.triangulate(bm, faces=res["faces"])
    for _ in range(6):
        bmesh.ops.smooth_vert(bm, verts=list(ring), factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
bm.to_mesh(mesh.data)
bm.free()
mesh.data.update()

# the little finger's vertex groups and bones are no longer needed
for g in [g for g in mesh.vertex_groups if g.name.startswith("f_pinky.")]: mesh.vertex_groups.remove(g)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for b in [b for b in arm.data.edit_bones if b.name.startswith("f_pinky.")]: arm.data.edit_bones.remove(b)
bpy.ops.object.mode_set(mode='OBJECT')

# ---- smooth shading ----
for p in mesh.data.polygons: p.use_smooth = True

# ---- split into the right and left arm (by which side of the middle they're on) ----
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.mode_set(mode='EDIT')
bm = bmesh.from_edit_mesh(mesh.data)
for v in bm.verts: v.select = False
for f in bm.faces: f.select = all((mesh.matrix_world @ v.co).x < 0 for v in f.verts)  # Blender -X = the right arm (camera looks down -Y)
bmesh.update_edit_mesh(mesh.data)
bpy.ops.mesh.separate(type='SELECTED')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.data.objects if o.type == 'MESH']
for o in parts:
    xs = [(o.matrix_world @ v.co).x for v in o.data.vertices]
    o.name = "ArmR" if sum(xs) / len(xs) < 0 else "ArmL"
    o.data.name = o.name
    print("[fps_arms]", o.name, "verts", len(o.data.vertices))

# ---- export ----
os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for o in parts: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', mesh_smooth_type='OFF',
                         use_armature_deform_only=False, primary_bone_axis='Y', secondary_bone_axis='X')
print("[fps_arms] exported", OUT_FBX)
for b in arm.data.bones:
    if b.name.endswith(".R") or b.name.endswith(".R_end"):
        print("[fps_arms] bone %-18s head %s tail %s" % (b.name, tuple(round(x, 4) for x in b.head_local), tuple(round(x, 4) for x in b.tail_local)))

# ---- optional renders ----
if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x = 640; scene.render.resolution_y = 480
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
    for name, at, look in [("eye", Vector((0, 0.02, 0.0)), Vector((0, -1, -0.45))), ("hand", Vector((-0.25, -0.75, -0.2)), Vector((-0.5, -0.33, -0.36))),
                           ("palm", Vector((-0.5, -0.5, -0.65)), Vector((-0.5, -0.33, -0.36)))]:
        cam.location = at
        d = (look if name == "eye" else look - at)
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = 18 if name == "eye" else 50
        scene.render.filepath = os.path.join(RENDER_DIR, "arms_" + name + ".png")
        bpy.ops.render.render(write_still=True)
