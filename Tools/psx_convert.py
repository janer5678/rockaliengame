"""
Converts the PSX asset pack into game-ready models for the PSX graphics mode.

    blender -b --python Tools/psx_convert.py -- <assets dir> [key ...]

<assets dir> holds "ALIEN ROCK GAME PSX ASSETS", "crimsongcat", "AaronMYoung" and "GreySkyInteractive".
Every model is joined into one mesh, turned to the game's conventions (up = +Z here, the front / blade / muzzle
towards -Y, which is Unity's +Z), centred, and exported to Assets/Game/Resources/PsxModels/<key>.fbx with its textures
shrunk to PSX size (at most 256 px) as <key>_<slot>.png. psx_manifest.txt lists each model's textures by material slot
(the game makes the materials; "#rrggbb" = a plain colour). The texture packs are 3x3 atlases: they're cut into tiles
(tex_<pack>_<row><col>.png). Renders of every model go to <assets dir>/check for a look.
"""
import bpy, bmesh, sys, os, math, glob
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Game", "Resources", "PsxModels"))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = argv[0]
ONLY = set(argv[1:])
A = os.path.join(SRC, "ALIEN ROCK GAME PSX ASSETS")
W = os.path.join(SRC, "crimsongcat/01- Fantasy.Weapon.Pack.PS1.PSX/32dc17044f60444285cf0340d81fc178_Textured.gltf")
RP2 = os.path.join(A, "NATURE/Boarder-Backround Rocks/PSX_Large_Terrain_Rock_Pack_2/RP2")
CHECK = os.path.join(SRC, "check")
MAXTEX = 256

# key: source, which objects, texture override, orientation
SPECS = {
    "machine": dict(src=A + "/ALIEN MACHINE/ALIEN MACHINE/scene.gltf"),
    "door": dict(src=A + "/DOOR/source/Doors.fbx", tex=A + "/DOOR/textures/Wooden_Door.png"),
    "c4": dict(src=A + "/ITEMS/C4/source/x/LowPoly Psx C4/fbx/PsxC4Collection.fbx"),
    "chest": dict(src=A + "/ITEMS/CHEST/source/x/model/cardboardboxmodel.fbx", tex=A + "/ITEMS/CHEST/textures/256x256boxtexture.png"),
    "barrier": dict(src=A + "/ITEMS/HIGH EXTERNAL WALL/source/x/chastokol.fbx", rot=(0, 0, 90)),
    "meat": dict(src=A + "/ITEMS/HORSE MEAT/source/x/Meat/Meat.fbx", objs=["Meat"]),
    "revolver": dict(src=A + "/ITEMS/REVOLVER/source/x/RevolverPSX/revolver.fbx", rot=(0, 0, 90),
                     texmap={"Wood": A + "/ITEMS/REVOLVER/textures/WoodTexture2.png", "": A + "/ITEMS/REVOLVER/textures/MetalTexture2.png"}),
    "rocket": dict(src=A + "/ITEMS/ROCKET LAUNCHER/source/RocketLauncher.fbx", tex=A + "/ITEMS/ROCKET LAUNCHER/textures/RocketLauncherTextureMap.png"),
    "shotgun": dict(src=A + "/ITEMS/SHOTGUN/source/m1897.glb", skip=["shell"], rot=(0, 0, 180)),
    "spear": dict(src=A + "/ITEMS/SPEAR/source/x/model/model.dae", tex=A + "/ITEMS/SPEAR/source/x/model/textures/lambert1_albedo.jpg", orient="tool"),
    "bush": dict(src=A + "/NATURE/BERRY BUSH/source/Sketchfab_2023_02_24_14_06_22.blend", objs=["BIG PLANT"]),
    "skeleton": dict(src=A + "/PLAYER/DEAD MODEL/source/skeleton.fbx"),
    # (the pack's FIRST PERSON ARMS are human: the first-person arms are the alien's own, below)
    # (the ripped horse texture is blank - it's a plain chestnut coat instead)
    "horse": dict(src=SRC + "/AaronMYoung/01- PS1.Style.Animal/96198883407f4301975bdcc5eb914df9_Textured.gltf", objs=["Horse_Horse_0"], colour="#8f5634"),
    # the fantasy weapon pack: picked out by the parts each weapon is made of
    "sword": dict(src=W, objs=["_gltfNode_94"], orient="tool", flat=True, post=(180, 0, 0)),  # the hilt spreads wider than the tip
    "hatchet": dict(src=W, objs=["_gltfNode_28", "_gltfNode_29"], orient="tool"),
    "treecracker": dict(src=W, objs=["_gltfNode_23", "_gltfNode_24"], orient="tool", post=(0, 0, 90)),  # double-bladed: blades front and back
    "bow": dict(src=W, objs=["_gltfNode_59", "_gltfNode_60"], orient="bow"),
    "crossbow": dict(src=W, objs=["_gltfNode_71", "_gltfNode_72"], orient="gun", post=(0, 0, 180)),
    "arrow": dict(src=W, objs=["_gltfNode_68", "_gltfNode_69"], orient="tool", post=(0, 180, 0)),  # fletching spreads wider than the tip
    "deathwand": dict(src=W, objs=["_gltfNode_7", "_gltfNode_8"], orient="tool"),
    "giantstaff": dict(src=W, objs=["_gltfNode_5"], orient="tool"),
}
# first-person arms: the forearm and hand cut out of the alien player model itself (by bone weights), hand forward
ALIEN = os.path.normpath(os.path.join(HERE, "..", "Assets", "Game", "Resources", "Alien"))
for side, name in (("r", "Right"), ("l", "Left")):
    SPECS[f"alienarm_{side}"] = dict(src=ALIEN + "/AlienRigged.fbx", vgroups=[name + "LowerArm", name + "Hand"], tex=ALIEN + "/Alien2.png", orient="tool", post=(-90, 0, 0))
for i in range(1, 7):
    SPECS[f"rock{i}"] = dict(src=A + "/NATURE/ROCKS.blend", objs=[f"Rock{i}"])
for i in range(6):
    SPECS[f"bigrock{i}"] = dict(src=f"{RP2}/FBX_Exports/SM_RP2_Rock_8m_{i}.fbx", rp2=True)
for i in (0, 2, 5):
    SPECS[f"midrock{i}"] = dict(src=f"{RP2}/FBX_Exports/SM_RP2_Rock_4m_{i}.fbx", rp2=True)

ATLASES = {
    "asphalt": "GreySkyInteractive/01- PSX.Asphalt.Textures",
    "grass": "GreySkyInteractive/03- PSX.Grass.Textures",
    "wood": "GreySkyInteractive/03- PSX.Wood.Textures",
    "metal": "GreySkyInteractive/04- PSX.Metal.Textures",
    "concrete": "GreySkyInteractive/05- PSX.Concrete.Textures",
    "cobble": "GreySkyInteractive/06- PSX.Cobblestone.Textures",
}


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".fbx": bpy.ops.import_scene.fbx(filepath=path)
    elif ext in (".gltf", ".glb"): bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".dae": bpy.ops.wm.collada_import(filepath=path)
    elif ext == ".blend":
        with bpy.data.libraries.load(path) as (src, dst):
            dst.objects = list(src.objects)
        for o in dst.objects:
            if o is not None: bpy.context.scene.collection.objects.link(o)


def save_image(img, name):
    """A copy of the image at most MAXTEX on a side, saved as <name>.png in OUT."""
    if img is None: return None
    try:
        if img.packed_file is None and not os.path.exists(bpy.path.abspath(img.filepath)):
            # a missing external file: look for it next to the source
            base = os.path.basename(img.filepath)
            hits = glob.glob(os.path.join(SRC, "**", base), recursive=True)
            if hits: img.filepath = hits[0]
        img.reload() if img.packed_file is None else None
        w, h = img.size
        if w == 0: return None
        _ = img.pixels[0]  # make sure the pixels are loaded before copying
        cp = img.copy()
        s = min(1.0, MAXTEX / max(w, h))
        cp.scale(max(1, int(w * s)), max(1, int(h * s)))  # (also gives the copy its own pixels to save)
        cp.filepath_raw = os.path.join(OUT, name + ".png")
        cp.file_format = 'PNG'
        cp.save()
        return name
    except Exception as e:
        print("  texture failed", name, e)
        return None


def slot_texture(mat):
    """The image feeding a material's colour (or any image in it), and its plain colour."""
    col = (0.7, 0.7, 0.7)
    if mat is None: return None, col
    if mat.use_nodes:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf:
            inp = bsdf.inputs["Base Color"]
            col = tuple(inp.default_value[:3])
            if inp.is_linked:
                n = inp.links[0].from_node
                for _ in range(4):
                    if n.type == 'TEX_IMAGE' and n.image: return n.image, col
                    ins = [i for i in n.inputs if i.is_linked]
                    if not ins: break
                    n = ins[0].links[0].from_node
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image: return n.image, col
    else:
        col = tuple(mat.diffuse_color[:3])
    return None, col


def rp2_image(mat):
    """The terrain rock pack's FBXs name their material but not the file: Rock_0/1/2 -> T_RP2_Rock_N_2K.png."""
    n = mat.name if mat else ""
    d = next((c for c in n if c.isdigit()), "0")
    p = os.path.join(RP2, "Textures", f"T_RP2_Rock_{d}_2K.png")
    return bpy.data.images.load(p) if os.path.exists(p) else None


def pick(spec):
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if "objs" in spec: meshes = [o for o in meshes if o.name in spec["objs"]]
    if "skip" in spec: meshes = [o for o in meshes if not any(o.name.startswith(s) for s in spec["skip"])]
    return meshes


def join(meshes):
    # bake any armature pose, drop parents, apply transforms, one object
    for o in meshes:
        for m in list(o.modifiers):
            bpy.context.view_layer.objects.active = o
            try: bpy.ops.object.modifier_apply(modifier=m.name)
            except Exception: o.modifiers.remove(m)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    for o in list(bpy.context.scene.objects):
        if o not in meshes: bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = meshes[0]
    for o in meshes: o.select_set(True)
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def verts(ob):
    return [v.co.copy() for v in ob.data.vertices]


def transform(ob, m):
    ob.data.transform(m)
    ob.data.update()


def principal(points):
    """Unit vector along the longest spread of the points (power iteration on the covariance)."""
    c = sum(points, Vector()) / len(points)
    cov = [[0.0] * 3 for _ in range(3)]
    for p in points:
        d = p - c
        for i in range(3):
            for j in range(3): cov[i][j] += d[i] * d[j]
    v = Vector((1, 0.3, 0.2))
    for _ in range(60):
        v = Vector([sum(cov[i][j] * v[j] for j in range(3)) for i in range(3)])
        if v.length < 1e-12: return Vector((0, 0, 1))
        v.normalize()
    return v


def orient(ob, spec):
    mode = spec.get("orient")
    if "rot" in spec:
        r = [math.radians(a) for a in spec["rot"]]
        transform(ob, Matrix.Rotation(r[2], 4, 'Z') @ Matrix.Rotation(r[1], 4, 'Y') @ Matrix.Rotation(r[0], 4, 'X'))
    if mode in ("tool", "bow"):
        # long axis up (+Z)
        ax = principal(verts(ob))
        transform(ob, ax.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4())
        pts = verts(ob)
        c = sum(pts, Vector()) / len(pts)
        zs = [p.z for p in pts]
        lo, hi = min(zs), max(zs)
        # the head (the end that spreads out most) goes up
        def spread(sel): return sum((Vector((p.x - c.x, p.y - c.y, 0))).length for p in sel) / max(1, len(sel))
        top = [p for p in pts if p.z > hi - (hi - lo) * 0.3]
        bot = [p for p in pts if p.z < lo + (hi - lo) * 0.3]
        if spread(bot) > spread(top) * 1.05: transform(ob, Matrix.Rotation(math.pi, 4, 'X'))
        pts = verts(ob)
        c = sum(pts, Vector()) / len(pts)
        zs = [p.z for p in pts]; lo, hi = min(zs), max(zs)
        sel = pts if mode == "bow" else [p for p in pts if p.z > hi - (hi - lo) * 0.35]
        if mode == "bow":
            # the bow's curve lies front to back, bulging forward (-Y), the string behind
            xy = [Vector((p.x, p.y, 0)) for p in pts]
            a = principal(xy)
            transform(ob, Matrix.Rotation(-math.atan2(a.y, a.x) - math.pi / 2, 4, 'Z'))
            pts = verts(ob)
            ys = [p.y for p in pts]
            if sum(ys) / len(ys) > (min(ys) + max(ys)) / 2: transform(ob, Matrix.Rotation(math.pi, 4, 'Z'))
        elif spec.get("flat"):
            # a blade: its thin side faces +-X (the edge points forward)
            xy = [Vector((p.x, p.y, 0)) for p in pts]
            a = principal(xy)  # widest across the blade
            ang = math.atan2(a.y, a.x)
            transform(ob, Matrix.Rotation(-ang - math.pi / 2, 4, 'Z'))
        else:
            # the blade / limbs stick out forward (-Y)
            off = sum((Vector((p.x - c.x, p.y - c.y, 0)) for p in sel), Vector()) / max(1, len(sel))
            if off.length > 1e-5:
                ang = math.atan2(off.y, off.x)
                transform(ob, Matrix.Rotation(-math.pi / 2 - ang, 4, 'Z'))
    elif mode == "gun":
        # long axis forward (-Y), flattest axis across (X)
        ax = principal(verts(ob))
        transform(ob, ax.rotation_difference(Vector((0, -1, 0))).to_matrix().to_4x4())
    if "post" in spec:
        r = [math.radians(a) for a in spec["post"]]
        transform(ob, Matrix.Rotation(r[2], 4, 'Z') @ Matrix.Rotation(r[1], 4, 'Y') @ Matrix.Rotation(r[0], 4, 'X'))
    # centre it
    pts = verts(ob)
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    transform(ob, Matrix.Translation(-(lo + hi) / 2))


def export(key, ob, spec, manifest):
    texs = []
    for i, slot in enumerate(ob.material_slots):
        img, col = slot_texture(slot.material)
        if spec.get("tex"): img = bpy.data.images.load(spec["tex"], check_existing=False)
        if spec.get("texmap"):
            mn = slot.material.name if slot.material else ""
            img = bpy.data.images.load(next(v for k, v in spec["texmap"].items() if k in mn), check_existing=False)
        if spec.get("rp2"): img = rp2_image(slot.material) or img
        name = save_image(img, f"{key}_{i}") if img else None
        texs.append(name if name else "#%02x%02x%02x" % tuple(int(max(0, min(1, c)) ** (1 / 2.2) * 255) for c in col))
    if not ob.material_slots: texs.append("#b3b3b3")
    if spec.get("colour"): texs = [spec["colour"]] * max(1, len(texs))
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    ob.name = key
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, key + ".fbx"), use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
    pts = verts(ob)
    size = [max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)]
    manifest[key] = ";".join(texs)
    print(f"  {key}: {len(pts)} verts, size {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f}, textures {texs}")


def render(key, ob):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'FLAT'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 300; sc.render.resolution_y = 300
    if sc.world is None: sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.25, 0.3, 0.35)
    pts = verts(ob)
    size = max(max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3))
    for name, d, up in (("front", Vector((0, -1, 0)), 'Y'), ("side", Vector((1, 0, 0)), 'Y'), ("top", Vector((0, 0, 1)), 'Y')):
        cam = bpy.data.cameras.new(name); cam.type = 'ORTHO'; cam.ortho_scale = size * 1.15; cam.clip_end = size * 20
        co = bpy.data.objects.new(name, cam); sc.collection.objects.link(co)
        co.location = d * size * 4
        co.rotation_euler = (-d).to_track_quat('-Z', up).to_euler()
        sc.camera = co
        sc.render.filepath = os.path.join(CHECK, f"{key}_{name}.png")
        bpy.ops.render.render(write_still=True)


def split_arms(ob):
    """The arms rig is both arms in one mesh: one model per arm (left = -X)."""
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.mesh.separate(type='LOOSE')
    parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    left = [o for o in parts if sum((o.matrix_world @ v.co).x for v in o.data.vertices) / max(1, len(o.data.vertices)) < 0.25]
    right = [o for o in parts if o not in left]
    return left, right


def cut_atlases(manifest):
    for name, folder in ATLASES.items():
        f = glob.glob(os.path.join(SRC, folder, "*_RGB_texture.png"))
        if not f: continue
        img = bpy.data.images.load(f[0])
        w, h = img.size
        px = list(img.pixels)
        tw, th = w // 3, h // 3
        for r in range(3):
            for c in range(3):
                t = bpy.data.images.new(f"tex_{name}_{r}{c}", tw, th, alpha=True)
                tp = [0.0] * (tw * th * 4)
                # Blender images start at the bottom row: atlas row 0 is the top one
                y0 = h - (r + 1) * th
                for y in range(th):
                    s = ((y0 + y) * w + c * tw) * 4
                    tp[y * tw * 4:(y + 1) * tw * 4] = px[s:s + tw * 4]
                t.pixels = tp
                t.filepath_raw = os.path.join(OUT, f"tex_{name}_{r}{c}.png")
                t.file_format = 'PNG'
                t.save()
        print("  atlas", name)


os.makedirs(OUT, exist_ok=True)
os.makedirs(CHECK, exist_ok=True)
manifest = {}
mpath = os.path.join(OUT, "psx_manifest.txt")
if os.path.exists(mpath):
    for line in open(mpath):
        if "|" in line:
            k, v = line.strip().split("|", 1)
            manifest[k] = v
for key, spec in SPECS.items():
    if ONLY and key not in ONLY: continue
    print("==", key)
    clear()
    load(spec["src"])
    meshes = pick(spec)
    if not meshes:
        print("  nothing to export"); continue
    ob = join(meshes)
    if spec.get("vgroups"):
        # keep only what those bones move most
        idx = {g.index for g in ob.vertex_groups if g.name in spec["vgroups"]}
        bm = bmesh.new(); bm.from_mesh(ob.data)
        dl = bm.verts.layers.deform.active
        drop = []
        for v in bm.verts:
            w = v[dl] if dl else {}
            best = max(w.items(), key=lambda kv: kv[1])[0] if len(w) else -1
            if best not in idx: drop.append(v)
        bmesh.ops.delete(bm, geom=drop, context='VERTS')
        bm.to_mesh(ob.data); bm.free()
        ob.data.update()
    if spec.get("split") == "arms":
        left, right = split_arms(ob)
        for side, parts in (("arms_l", left), ("arms_r", right)):
            if not parts: continue
            for o in bpy.context.scene.objects: o.select_set(False)
            for o in parts: o.select_set(True)
            bpy.context.view_layer.objects.active = parts[0]
            if len(parts) > 1: bpy.ops.object.join()
            o = bpy.context.view_layer.objects.active
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            # keep this arm only for the export / render
            hide = [x for x in bpy.context.scene.objects if x.type == 'MESH' and x != o]
            for x in hide: x.hide_render = True
            orient(o, spec)
            export(side, o, spec, manifest)
            render(side, o)
            for x in hide: x.hide_render = False
            o.hide_render = True
        continue
    orient(ob, spec)
    export(key, ob, spec, manifest)
    render(key, ob)
if not ONLY or "atlas" in ONLY:
    clear()
    cut_atlases(manifest)
with open(mpath, "w") as f:
    for k in sorted(manifest): f.write(f"{k}|{manifest[k]}\n")
print("DONE")
