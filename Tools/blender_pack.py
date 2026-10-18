"""
Turns the game's model export (Tools/export_assets.ps1: OBJ + MTL files, one per model, in a folder per category) into
Blender files:
  - every model as an FBX next to its OBJ (textures embedded), so <category>/<model>.obj / .mtl / .fbx sit side by side;
  - blender/<category>.blend: every model of that category in one scene, each under its own empty, laid out in a grid
    (the alien poses one row per team colour);
  - blender/RockAlienGame_All.blend: everything, one collection per category;
  - blender/AlienRigged_Teams.blend: the rigged player alien (the FBX the game loads, with its skeleton) four times, in
    each team's tinted skin, ready to pose;
  - blender/AlienArms_Teams.blend: the rigged first-person alien arms, the same way.

    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --factory-startup --python Tools/blender_pack.py -- <export dir>
"""
import bpy, os, sys, glob, math

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Builds", "Models")
BLEND_DIR = os.path.join(ROOT, "blender")
os.makedirs(BLEND_DIR, exist_ok=True)
CATEGORIES = ["aliens", "hands", "items", "structures", "deployables", "vehicles", "nature", "world"]
TEAMS = ["blue", "red", "green", "yellow"]


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0


def import_obj(path):
    """Imports one OBJ; returns the new objects."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path, forward_axis='NEGATIVE_Z', up_axis='Y')
    objs = [o for o in bpy.data.objects if o not in before]
    vertex_colour_materials(objs)
    return objs


def vertex_colour_materials(objs):
    """Materials named vc_... are drawn from the mesh's vertex colours (the game's painted foliage, bark, flags): wire
    the colour attribute into the base colour."""
    for o in objs:
        if o.type != 'MESH':
            continue
        for slot in o.material_slots:
            mat = slot.material
            if mat is None or not mat.name.split(".")[0].startswith("vc_") or not mat.use_nodes:
                continue
            nodes = mat.node_tree.nodes
            if any(n.type == 'VERTEX_COLOR' for n in nodes):
                continue
            bsdf = nodes.get("Principled BSDF")
            if bsdf is None:
                continue
            vc = nodes.new("ShaderNodeVertexColor")
            vc.layer_name = "Color"
            mat.node_tree.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])


def group(objs, name, collection=None):
    """Parents the objects to a new empty (the model's root), in the collection."""
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_size = 0.2
    (collection or bpy.context.scene.collection).objects.link(empty)
    for o in objs:
        o.parent = empty
        if collection is not None:
            for c in list(o.users_collection):
                c.objects.unlink(o)
            collection.objects.link(o)
    return empty


def bounds(objs):
    xs, ys, zs = [], [], []
    for o in objs:
        if o.type != 'MESH':
            continue
        for v in o.bound_box:
            p = o.matrix_world @ __import__("mathutils").Vector(v)
            xs.append(p.x); ys.append(p.y); zs.append(p.z)
    if not xs:
        return (0, 0, 0), (0, 0, 0)
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def save(path):
    """Saves the scene with its textures packed inside (the file stands on its own wherever the zip goes), no .blend1 backup."""
    # (images the FBX import brought along and nothing uses any more, or that aren't there: they'd stop the packing)
    for img in list(bpy.data.images):
        if img.packed_file is None and (img.users == 0 or (img.filepath and not os.path.exists(bpy.path.abspath(img.filepath)))):
            bpy.data.images.remove(img)
    try:
        bpy.ops.file.pack_all()
    except Exception as e:
        print("[pack] pack_all:", e)
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    backup = path + "1"
    if os.path.exists(backup):
        os.remove(backup)


def export_fbx(path):
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, mesh_smooth_type='OFF', use_mesh_modifiers=True,
                             add_leaf_bones=False, bake_anim=False, use_custom_props=False)


def fbx_everything():
    n = 0
    for cat in CATEGORIES:
        for obj in sorted(glob.glob(os.path.join(ROOT, cat, "*.obj"))):
            reset()
            objs = import_obj(obj)
            group(objs, os.path.splitext(os.path.basename(obj))[0])
            export_fbx(os.path.splitext(obj)[0] + ".fbx")
            n += 1
    print(f"[pack] {n} FBX files")


def grid_rows(cat, files):
    """How the models of a category are laid out: a list of rows, each a list of files."""
    if cat == "aliens":
        rows = []
        for team in TEAMS:
            row = [f for f in files if os.path.basename(f).startswith(f"alien_{team}_")]
            if row:
                rows.append(row)
        rest = [f for f in files if not any(os.path.basename(f).startswith(f"alien_{t}_") for t in TEAMS)]
        if rest:
            rows.append(rest)
        return rows
    if cat == "hands":
        rows = []
        for style in ["square", "round", "alien"]:
            for team in TEAMS:
                row = [f for f in files if os.path.basename(f).startswith(f"hands_{style}_{team}_")]
                if row:
                    rows.append(row)
        return rows
    per = max(6, int(math.sqrt(len(files)) * 1.5))
    return [files[i:i + per] for i in range(0, len(files), per)]


def lay_out(cat, collection, origin=(0.0, 0.0, 0.0)):
    """Imports every OBJ of the category into the collection, in a grid from origin; returns the grid's depth (y extent)."""
    files = sorted(glob.glob(os.path.join(ROOT, cat, "*.obj")))
    y = origin[1]
    for row in grid_rows(cat, files):
        x = origin[0]
        row_depth = 0.0
        for f in row:
            objs = import_obj(f)
            name = os.path.splitext(os.path.basename(f))[0]
            lo, hi = bounds(objs)
            w = max(hi[0] - lo[0], 0.3)
            d = max(hi[1] - lo[1], 0.3)
            empty = group(objs, name, collection)
            # (the model's root at the grid spot: its own origin kept, so what's on the ground stays on the ground)
            empty.location = (x - lo[0] + 0.0, y - lo[1], origin[2])
            x += w + 0.6
            row_depth = max(row_depth, d)
        y += row_depth + 1.2
    return y - origin[1]


def blend_per_category():
    for cat in CATEGORIES:
        if not glob.glob(os.path.join(ROOT, cat, "*.obj")):
            continue
        reset()
        col = bpy.data.collections.new(cat)
        bpy.context.scene.collection.children.link(col)
        lay_out(cat, col)
        save(os.path.join(BLEND_DIR, f"{cat}.blend"))
        print(f"[pack] {cat}.blend")


def blend_all():
    reset()
    y = 0.0
    for cat in CATEGORIES:
        if not glob.glob(os.path.join(ROOT, cat, "*.obj")):
            continue
        col = bpy.data.collections.new(cat)
        bpy.context.scene.collection.children.link(col)
        y += lay_out(cat, col, (0.0, y, 0.0)) + 6.0
    save(os.path.join(BLEND_DIR, "RockAlienGame_All.blend"))
    print("[pack] RockAlienGame_All.blend")


def image_material(name, image_path, color=(1, 1, 1, 1), roughness=0.85):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Base Color"].default_value = color
    if image_path and os.path.exists(image_path):
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(image_path)
        tex.image.colorspace_settings.name = 'sRGB'
        tex.interpolation = 'Closest'
        links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def rigged_teams(fbx, out_name, slot_texture, spacing):
    """The rigged FBX imported four times, each in a team's materials. slot_texture(material name, team) -> texture path or None."""
    if not os.path.exists(fbx):
        print("[pack] missing", fbx)
        return
    reset()
    for i, team in enumerate(TEAMS):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=fbx, use_custom_normals=True, ignore_leaf_bones=True)
        objs = [o for o in bpy.data.objects if o not in before]
        col = bpy.data.collections.new(team)
        bpy.context.scene.collection.children.link(col)
        for o in objs:
            for c in list(o.users_collection):
                c.objects.unlink(o)
            col.objects.link(o)
            if o.parent is None:
                o.location.x += i * spacing
            if o.type == 'MESH':
                for slot in o.material_slots:
                    if slot.material is None:
                        continue
                    base = slot.material.name.split(".")[0]
                    tex = slot_texture(base, team, o.name)
                    slot.material = image_material(f"{base}_{team}", tex)
    save(os.path.join(BLEND_DIR, out_name))
    print("[pack]", out_name)


def main():
    fbx_everything()
    blend_per_category()
    blend_all()
    src = os.path.join(ROOT, "sources")
    rigged_teams(os.path.join(src, "AlienRigged.fbx"), "AlienRigged_Teams.blend",
                 lambda m, team, obj: os.path.join(ROOT, "aliens", f"alien_{team}_{'head' if 'Head' in m else 'body'}.png"), 2.0)
    rigged_teams(os.path.join(src, "AlienArms.fbx"), "AlienArms_Teams.blend",
                 lambda m, team, obj: os.path.join(ROOT, "hands", f"arms_{team}_{'claws' if 'claws' in obj.lower() else 'skin'}.png"), 1.0)
    print("[pack] done")


main()
