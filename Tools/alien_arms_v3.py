"""
Builds the game's first-person ALIEN ARMS (v3) and exports Assets/Game/Resources/FpsArms/AlienArms.fbx, the model
ViewModel.Rig.cs loads: a pair of arms with the alien player model's look - slender grey arms, a narrow palm, three
fingers and a thumb with dark tips - built the way a sculptor blocks a hand out, so it has the mass and shape of a hand
(what the earlier tube-and-blob versions never had):

  1. the arm is blocked out from solid volumes placed by real hand anatomy: a palm slab (narrower at the wrist, wider
     at the knuckles), the thumb's muscle (thenar) and the heel of the hand (hypothenar), a knuckle at every joint, tapered
     finger bones with rounded pads at the tips, a wrist narrower than the forearm, a forearm that swells to the elbow;
  2. the volumes are fused into ONE clean, watertight surface (a voxel remesh of their union - no blobbing, no seams),
     smoothed, then re-topologised to even quads (quadriflow) so it shades smoothly;
  3. the skin is unwrapped and its ambient occlusion baked (AlienArmsAO.png, with a faint grain): the creases between the
     fingers, the knuckles and the hollow of the palm shade darker in the game, so the hand reads as a hand and not a balloon;
  4. the dark fingertips are split off as their own mesh ("ArmR_claws" / "ArmL_claws": the game colours them darker);
  5. a matching skeleton is built to fit it (the bone names ViewModel.Rig.cs expects: upper_arm / forearm / hand, three
     fingers and a thumb of three bones each, with _end tips), and the skin is weighted to it with Blender's automatic
     (bone heat) weights, so fingers bend at the knuckles (skin() is the plain nearest-bone fallback);
  6. the left arm is the right one mirrored (the same skin, weights and texture).

Run headless from the repo root:
    "C:/Program Files/Blender Foundation/Blender 4.2/blender.exe" -b --factory-startup --python Tools/alien_arms_v3.py
    (add `-- --render <dir>` to render previews there: the hand from several sides, relaxed and as a fist, and the eye's view)
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
OUT_DIR = os.path.join(REPO, "Assets", "Game", "Resources", "FpsArms")
OUT_FBX = os.path.join(OUT_DIR, "AlienArms.fbx")
OUT_AO = os.path.join(OUT_DIR, "AlienArmsAO.png")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[argv.index("--render") + 1] if "--render" in argv else None

# ---------------------------------------------------------------- the measurements (metres)
HAND = 1.2           # the hand bigger than life, like an FPS's (it has to read at arm's length)
SHOULDER_X = 0.18    # how far out each shoulder is from the middle
UPPER_LEN, FORE_LEN = 0.30, 0.28
R_SHOULDER, R_UPPER, R_ELBOW, R_FORE_ELBOW, R_FORE_WRIST = 0.046, 0.041, 0.037, 0.035, 0.0245
WRIST = (0.028, 0.03, 0.019)              # the wrist's half sizes: across, along, thick
PALM_LEN, PALM_W_KNUCKLE, PALM_W_WRIST = 0.094, 0.040, 0.031
PALM_T_BACK, PALM_T_HEEL, PALM_T_KNUCKLE = 0.012, 0.018, 0.013   # half thickness: the back; the palm side at the heel / the knuckles
# the fingers (index, middle, ring): where each knuckle is (across, along), its three bones' lengths, its radii at the
# knuckle, the two joints, and the tip pad; and how far it splays (degrees, + towards the thumb)
FINGERS = [
    ("f_index",  (+0.0268, 0.090), (0.036, 0.023, 0.020), (0.0122, 0.0110, 0.0098, 0.0088), +2.5),
    ("f_middle", (0.0,     0.095), (0.039, 0.025, 0.021), (0.0126, 0.0114, 0.0102, 0.0092), 0.0),
    ("f_ring",   (-0.0268, 0.088), (0.036, 0.023, 0.020), (0.0116, 0.0105, 0.0094, 0.0085), -3.0),
]
THUMB_BASE = (0.030, 0.026, -0.007)       # the thumb's root in the heel of the palm (across, along, up)
THUMB_LEN = (0.044, 0.030, 0.025)
THUMB_R = (0.0146, 0.0126, 0.0108, 0.0096)
VOXEL = 0.002
QUAD_FACES = 7000

def mat_frame(origin, z_dir, scale=(1, 1, 1)):
    """A matrix placing a unit primitive at origin, its +Z along z_dir, scaled."""
    q = Vector((0, 0, 1)).rotation_difference(Vector(z_dir).normalized())
    return Matrix.Translation(origin) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1))

class Side:
    """One arm's geometry, in metres. Local hand coordinates (a, b, c): a across towards the thumb, b along the fingers,
    c up out of the back of the hand - placed in the world by W(): the right arm at -X (Unity's +X), fingers along -Y."""
    def __init__(self, right):
        self.right = right
        self.sx = 1.0 if right else -1.0
        self.ox = -SHOULDER_X * self.sx
        self.suffix = ".R" if right else ".L"
        self.name = "ArmR" if right else "ArmL"
        # the bones' joints
        self.S = Vector((self.ox, 0.0, -0.04))
        self.E = self.S + Vector((0, -UPPER_LEN, 0))
        self.Wr = self.E + Vector((0, -FORE_LEN, 0))
        self.digits = {}  # name -> list of joint points [root, j1, j2, tip] (world) and radii
        for name, (ka, kb), lens, rads, splay in FINGERS:
            d = self.F(math.sin(math.radians(splay)), math.cos(math.radians(splay)), -0.02)  # (a touch downward)
            p0 = self.W(ka * HAND, kb * HAND, 0.001 * HAND)
            pts = [p0]
            for L in lens: pts.append(pts[-1] + d * (L * HAND))
            self.digits[name] = (pts, [r * HAND for r in rads], d)
        b = self.W(*[v * HAND for v in THUMB_BASE])
        dirs = [self.F(0.78, 0.55, -0.15), self.F(0.62, 0.76, -0.1), self.F(0.52, 0.84, -0.08)]
        pts = [b]
        for L, d in zip(THUMB_LEN, dirs): pts.append(pts[-1] + d * (L * HAND))
        self.digits["thumb"] = (pts, [r * HAND for r in THUMB_R], dirs)

    def W(self, a, b, c):
        return self.Wr + Vector((self.sx * a, -b, c))
    def F(self, a, b, c):
        return Vector((self.sx * a, -b, c)).normalized()

    # ---- the block-out ----
    def block(self):
        bm = bmesh.new()
        def sphere(c, r, scale=(1, 1, 1), z=(0, 0, 1)):
            bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=1.0, matrix=mat_frame(c, z, (r * scale[0], r * scale[1], r * scale[2])))
        def capsule(a, b, r0, r1, flat=1.0):
            d = b - a
            bmesh.ops.create_cone(bm, cap_ends=True, segments=28, radius1=r0, radius2=r1, depth=d.length,
                                  matrix=mat_frame((a + b) * 0.5, d, (1, flat, 1)))
            sphere(a, r0, (1, flat, 1), d); sphere(b, r1, (1, flat, 1), d)
        S, E, Wr = self.S, self.E, self.Wr
        up, fwd = Vector((0, 0, 1)), Vector((0, -1, 0))
        # the arm: shoulder, upper arm, elbow, forearm swelling from the wrist to just under the elbow (a little flattened)
        sphere(S, R_SHOULDER)
        capsule(S, E, R_UPPER, R_ELBOW)
        sphere(E, R_ELBOW * 1.05)
        capsule(E, Wr, R_FORE_ELBOW, R_FORE_WRIST, 0.84)
        # the wrist
        sphere(Wr, 1.0, (WRIST[0] * HAND, WRIST[2] * HAND, WRIST[1] * HAND), (0, 1, 0))  # (the ellipsoid's own Z goes along the arm)
        # the palm: a rounded slab, narrower at the wrist, wider at the knuckles, thick at the heel and thinner at the
        # knuckles on the palm side, its back nearly flat
        cb = bmesh.new()
        bmesh.ops.create_cube(cb, size=2.0)
        for v in cb.verts:
            a, b, c = v.co.x, v.co.y, v.co.z
            w = (PALM_W_WRIST if b < 0 else PALM_W_KNUCKLE) * HAND
            t = PALM_T_BACK if c > 0 else (PALM_T_HEEL if b < 0 else PALM_T_KNUCKLE)
            v.co = Vector((a * w, b * PALM_LEN * 0.5 * HAND, c * t * HAND))
        bmesh.ops.bevel(cb, geom=cb.edges[:], offset=0.011 * HAND, segments=6, affect='EDGES', profile=0.6)
        for v in cb.verts:
            # (world: the slab from the wrist to the knuckles)
            v.co = self.W(v.co.x, PALM_LEN * 0.5 * HAND + 0.004 * HAND + v.co.y, v.co.z - 0.001 * HAND)
        tmp = bpy.data.meshes.new("palm"); cb.to_mesh(tmp); cb.free(); bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
        # the thumb's muscle (from the wrist out along the thumb's root) and the heel of the hand (down the far side)
        tdir = self.digits["thumb"][2][0]
        sphere(self.W(0.024 * HAND, 0.040 * HAND, -0.007 * HAND), 1.0, (0.020 * HAND, 0.016 * HAND, 0.036 * HAND), tdir)
        sphere(self.W(-0.030 * HAND, 0.038 * HAND, -0.006 * HAND), 1.0, (0.014 * HAND, 0.014 * HAND, 0.038 * HAND), fwd)
        # the webbing between the fingers' bases
        fb = [self.digits[n][0][0] for n in ("f_index", "f_middle", "f_ring")]
        for i in range(2):
            sphere((fb[i] + fb[i + 1]) * 0.5 + fwd * (0.006 * HAND) - up * (0.002 * HAND), 1.0, (0.0105 * HAND, 0.0085 * HAND, 0.0125 * HAND), fwd)
        # the fingers and the thumb: a knuckle at each joint, tapered bones between, a rounded pad at the tip
        for name, (pts, rads, d) in self.digits.items():
            dirs = d if isinstance(d, list) else [d, d, d]
            for j in range(3):
                capsule(pts[j], pts[j + 1], rads[j], rads[j + 1] if j < 2 else rads[3] * 0.8)
            if name != "thumb": sphere(pts[0] + up * (rads[0] * 0.08), rads[0] * 1.06)  # (the knuckle)
            for j in (1, 2): sphere(pts[j], rads[j] * 1.05)
            tipd = dirs[2]
            # (the tip: a pad that narrows to a rounded point - the alien's claw)
            sphere(pts[3] - tipd * (rads[3] * 0.45), 1.0, (rads[3] * 0.92, rads[3] * 0.82, rads[3] * 1.25), tipd)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob

    # ---- the skeleton's segments for the skin weights: name -> (a, b, radius), and the chain links ----
    def segments(self):
        s = self.suffix
        seg = {"upper_arm" + s: (self.S, self.E, R_UPPER), "forearm" + s: (self.E, self.Wr, R_FORE_WRIST * 1.1),
               "hand" + s: (self.Wr + Vector((0, -0.01, 0)), self.digits["f_middle"][0][0] - Vector((0, -0.012, 0)), 0.030 * HAND)}
        nxt, prv = {"upper_arm" + s: "forearm" + s, "forearm" + s: "hand" + s}, {"forearm" + s: "upper_arm" + s, "hand" + s: "forearm" + s}
        for name, (pts, rads, _) in self.digits.items():
            for j in range(3):
                b = f"{name}.0{j + 1}{s}"
                seg[b] = (pts[j], pts[j + 1], rads[j] * 0.95)
                prv[b] = f"{name}.0{j}{s}" if j > 0 else "hand" + s
                if j < 2: nxt[b] = f"{name}.0{j + 2}{s}"
        return seg, nxt, prv

def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-12, ab.dot(ab))))
    return (p - (a + ab * t)).length, t

def skin(ob, side):
    seg, nxt, prv = side.segments()
    groups = {b: ob.vertex_groups.new(name=b) for b in seg}
    for v in ob.data.vertices:
        p = ob.matrix_world @ v.co
        best, bd, bt = None, 1e9, 0.0
        for b, (a, c, r) in seg.items():
            d, t = seg_dist(p, a, c)
            d -= r  # (how far outside that bone's own thickness: a finger's skin is nearest its own bone, not the palm's)
            if d < bd: best, bd, bt = b, d, t
        a, c, r = seg[best]
        L = (c - a).length
        blend = min(0.011 * HAND, L * 0.42)
        w = {best: 1.0}
        if best in nxt and bt * L > L - blend:
            k = 0.5 * (1.0 - (L - bt * L) / blend); w = {best: 1.0 - k, nxt[best]: k}
        elif best in prv and bt * L < blend:
            k = 0.5 * (1.0 - (bt * L) / blend); w = {best: 1.0 - k, prv[best]: k}
        for b, wt in w.items():
            if wt > 0.0: groups[b].add([v.index], wt, 'REPLACE')

def skin_auto(ob, arm, side):
    """Blender's automatic (bone heat) weights: each point weighted to the bones nearest it through the mesh, blending
    smoothly over the joints. False if Blender couldn't solve them (then skin() does it the plain way)."""
    for b in arm.data.bones: b.use_deform = not b.name.endswith("_end")
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    try:
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    except Exception as e:
        print("[alien_arms_v3] automatic weights failed:", e); return False
    other = ".L" if side.right else ".R"
    for vg in list(ob.vertex_groups):
        if vg.name.endswith(other) or vg.name.endswith(other + "_end"): ob.vertex_groups.remove(vg)
    need = set(side.segments()[0].keys())
    have = {vg.name for vg in ob.vertex_groups}
    if not need <= have: print("[alien_arms_v3] automatic weights missing groups", need - have); return False
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # every point must have weight somewhere (the heat solve leaves none out, but check)
    loose = sum(1 for v in ob.data.vertices if sum(g.weight for g in v.groups) < 0.5)
    if loose: print("[alien_arms_v3] automatic weights left", loose, "points unweighted"); return False
    return True

def build_armature(sides):
    arm_data = bpy.data.armatures.new("AlienArmsRig")
    arm = bpy.data.objects.new("AlienArmsRig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones
    def bone(name, head, tail, parent=None, connect=False):
        b = eb.new(name); b.head = head; b.tail = tail
        if parent: b.parent = eb[parent]; b.use_connect = connect
        b.align_roll(Vector((0, 0, 1)))
        return b
    for sd in sides:
        s = sd.suffix
        bone("upper_arm" + s, sd.S, sd.E)
        bone("forearm" + s, sd.E, sd.Wr, "upper_arm" + s, True)
        bone("forearm" + s + "_end", sd.Wr, sd.Wr + Vector((0, -0.01, 0)), "forearm" + s)
        bone("hand" + s, sd.Wr, sd.Wr + Vector((0, -0.04, 0)), "forearm" + s, True)
        for name, (pts, rads, d) in sd.digits.items():
            dirs = d if isinstance(d, list) else [d, d, d]
            par = "hand" + s
            for j in range(3):
                n = f"{name}.0{j + 1}{s}"
                bone(n, pts[j], pts[j + 1], par, j > 0)
                par = n
            bone(f"{name}.03{s}_end", pts[3], pts[3] + dirs[2] * 0.008, par)
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm

def unwrap(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.006)  # (the pieces fill the whole texture)
    bpy.ops.object.mode_set(mode='OBJECT')

def bake_ao(ob, path, size=1024, samples=64):
    """Bakes the arm's shading into one image - its ambient occlusion (the gaps between the fingers, the hollow of the
    palm) and its cavity (every crease and joint, from the surface's curvature) - lifts and stretches them, lays a faint
    grain over the lot, and saves it: the game puts it under the team colour."""
    import numpy as np
    sc = bpy.context.scene
    if len(ob.data.materials) == 0:
        ob.data.materials.append(bpy.data.materials.new("AlienSkin")); ob.data.materials.append(bpy.data.materials.new("AlienTip"))
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.use_denoising = False
    sc.world = bpy.data.worlds.new("bake world")
    sc.world.light_settings.distance = 0.14  # (how far the occlusion looks: a hand's creases and hollows, not the room)
    sc.render.bake.margin = 0  # (the bake's own margin leaves dark half-covered specks round the pieces: the margin is grown below instead)
    sc.render.bake.use_clear = True
    sc.render.bake.use_selected_to_active = False

    def bake(kind, name, n, wire):
        """One bake into a new image: every material gets an image node (the bake's target) and `wire` sets its nodes up."""
        img = bpy.data.images.new(name, size, size)
        for mat in ob.data.materials:
            mat.use_nodes = True
            nodes = mat.node_tree.nodes
            node = nodes.new('ShaderNodeTexImage'); node.image = img
            wire(mat)
            for nd in nodes: nd.select = nd == node
            nodes.active = node
        sc.cycles.samples = n
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.bake(type=kind)
        return np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4)[:, :, 0]

    # the cavity: how much the surface folds in at every point (neighbours above its tangent plane: a crease), as a
    # colour on the points, baked out through an emission shader
    me = ob.data
    nv = len(me.vertices)
    co = np.array([v.co[:] for v in me.vertices]); nm = np.array([v.normal[:] for v in me.vertices])
    adj = [[] for _ in range(nv)]
    for e in me.edges: a, b = e.vertices; adj[a].append(b); adj[b].append(a)
    curv = np.zeros(nv)
    for i in range(nv):
        if not adj[i]: continue
        d = co[adj[i]] - co[i]
        curv[i] = np.mean((d @ nm[i]) / ((d * d).sum(1) + 1e-12))   # (~ -1/2r on a bump of radius r, + in a crease)
    sm = np.array([(curv[i] + curv[adj[i]].mean()) * 0.5 if adj[i] else curv[i] for i in range(nv)])
    cav = np.clip(1.0 - np.clip(sm, 0, None) * 0.006 + np.clip(-sm, 0, None) * 0.0006, 0.45, 1.06)
    col = me.color_attributes.new("cavity", 'FLOAT_COLOR', 'POINT')
    for i in range(nv): col.data[i].color = (cav[i], cav[i], cav[i], 1.0)
    print("[alien_arms_v3] cavity: creases to %.2f, bumps to %.2f" % (cav.min(), cav.max()))

    ao = bake('AO', "AlienArmsAO", samples, lambda mat: None)
    def wire_cavity(mat):
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        attr = nodes.new('ShaderNodeAttribute'); attr.attribute_name = "cavity"
        emit = nodes.new('ShaderNodeEmission')
        out = [nd for nd in nodes if nd.type == 'OUTPUT_MATERIAL'][0]
        links.new(attr.outputs["Color"], emit.inputs["Color"])
        links.new(emit.outputs["Emission"], out.inputs["Surface"])
    cv = bake('EMIT', "AlienArmsCavity", 4, wire_cavity)

    # (the bake leaves what's outside the skin's pieces black and the pieces' edge pixels half-covered: neither is skin)
    inside = ao > 0.6
    def box(a):
        return sum(np.roll(np.roll(a, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9.0
    def mblur(a, passes, m, fill):
        """A little blur that stays inside the skin's pieces (the bake's grain would be stretched into speckles below);
        `fill` where there's no skin."""
        mf = m.astype(np.float32); a = a * mf; w = mf.copy()
        for _ in range(passes): a = box(a); w = box(w)
        return np.where(w > 1e-3, a / np.maximum(w, 1e-3), fill)
    ao_b = mblur(ao, 2, inside, 1.0)
    cv_b = mblur(cv, 1, inside, 1.0)
    # true occlusion on an open hand is subtle - most of it sits between 0.75 and 1 - so it's stretched: the darkest 2 %
    # of the skin to 35 %, the open 15 % to full, so the gaps between the fingers and the hollow of the palm really read
    lo, hi = (np.percentile(ao_b[inside], 2), np.percentile(ao_b[inside], 85)) if inside.any() else (0.0, 1.0)
    print("[alien_arms_v3] ao inside the skin: 2 %% %.3f, 50 %% %.3f, 85 %% %.3f" % (lo, np.percentile(ao_b[inside], 50) if inside.any() else 0, hi))
    t = np.clip((ao_b - lo) / max(1e-4, hi - lo), 0, 1)
    shade = 0.35 + 0.65 * np.power(t, 0.9)
    shade = shade * np.where(inside, np.clip(cv_b, 0.45, 1.06), 1.0)
    rng = np.random.default_rng(7)
    def smooth_noise(cells):
        g = rng.random((cells + 1, cells + 1))
        xs = np.linspace(0, cells, size)
        i = np.minimum(np.floor(xs).astype(int), cells - 1); f = xs - i
        row = g[:, i] * (1 - f) + g[:, i + 1] * f
        return row[i, :] * (1 - f)[:, None] + row[i + 1, :] * f[:, None]
    # the skin's shading grown out past every piece's edge (a margin, so the seams stay clean when the texture is
    # filtered); light beyond that, so nothing dark can ever bleed in
    v, w = shade * inside, inside.astype(np.float32)
    for _ in range(10):
        nv, nw = box(v), box(w)
        v = np.where(w > 0, v, np.where(nw > 1e-3, nv / np.maximum(nw, 1e-3), 0.0))
        w = np.where(w > 0, w, (nw > 1e-3).astype(np.float32))
    v = np.where(w > 0, v, 1.0)
    grain = 1.0 + (rng.random((size, size)) - 0.5) * 0.06 + (smooth_noise(48) - 0.5) * 0.08 + (smooth_noise(12) - 0.5) * 0.06
    v = np.clip(v * grain, 0, 1)
    img = bpy.data.images["AlienArmsAO"]
    px = np.empty((size, size, 4), dtype=np.float32)
    px[:, :, 0] = v; px[:, :, 1] = v; px[:, :, 2] = v; px[:, :, 3] = 1.0
    img.pixels = px.ravel()
    img.filepath_raw = path; img.file_format = 'PNG'
    img.save()
    # (the materials back to plain, with the finished image: the previews show it)
    for mat in ob.data.materials:
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        for nd in list(nodes):
            if nd.type in ('ATTRIBUTE', 'EMISSION') or (nd.type == 'TEX_IMAGE' and nd.image != img): nodes.remove(nd)
        bsdf = nodes.get("Principled BSDF"); out = [nd for nd in nodes if nd.type == 'OUTPUT_MATERIAL'][0]
        if bsdf: links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    print("[alien_arms_v3] baked", path, "ao mean %.3f" % float(ao.mean()))

def mirror(ob, name):
    """A copy of the (right) arm's object mirrored across X: the left arm, its weights renamed to the .L bones."""
    o = ob.copy(); o.data = ob.data.copy(); o.name = name; o.data.name = name
    bpy.context.scene.collection.objects.link(o)
    for v in o.data.vertices: v.co.x = -v.co.x
    o.data.flip_normals()
    for vg in o.vertex_groups: vg.name = vg.name.replace(".R", ".L")
    return o

def finish_mesh(ob):
    """The block-out into one clean surface: a voxel remesh of the union, smoothed, re-topologised to even quads."""
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    ob.data.remesh_voxel_size = VOXEL
    ob.data.remesh_voxel_adaptivity = 0.0
    ob.data.use_remesh_fix_poles = True
    bpy.ops.object.voxel_remesh()
    sm = ob.modifiers.new("smooth", 'SMOOTH'); sm.factor = 0.5; sm.iterations = 3
    bpy.ops.object.modifier_apply(modifier="smooth")
    before = len(ob.data.polygons)
    try:
        bpy.ops.object.quadriflow_remesh(target_faces=QUAD_FACES, use_preserve_sharp=False, use_mesh_symmetry=False, mode='FACES', seed=1)
    except Exception as e:
        print("[alien_arms_v3] quadriflow failed, decimating instead:", e)
        dec = ob.modifiers.new("dec", 'DECIMATE'); dec.ratio = min(1.0, QUAD_FACES * 2.0 / max(1, before))
        bpy.ops.object.modifier_apply(modifier="dec")
    bpy.ops.object.shade_smooth()
    print(f"[alien_arms_v3] {ob.name}: remeshed {before} faces -> {len(ob.data.polygons)} faces, {len(ob.data.vertices)} verts")

def split_tips(ob, side):
    """The dark tips: the last part of every finger and the thumb, as their own object."""
    if len(ob.data.materials) < 2:
        ob.data.materials.clear()
        ob.data.materials.append(bpy.data.materials.new("AlienSkin")); ob.data.materials.append(bpy.data.materials.new("AlienTip"))
    zones = []
    for name, (pts, rads, d) in side.digits.items():
        dirs = d if isinstance(d, list) else [d, d, d]
        L = (pts[3] - pts[2]).length
        zones.append((pts[2], dirs[2], L * 0.48, rads[2] * 1.9))
    for poly in ob.data.polygons:
        c = ob.matrix_world @ poly.center
        for (j2, d, along, rad) in zones:
            v = c - j2
            t = v.dot(d)
            if t > along and (v - d * t).length < rad:
                poly.material_index = 1
                break
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for poly in ob.data.polygons: poly.select = poly.material_index == 1
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    claws = [o for o in bpy.context.selected_objects if o != ob][0]
    claws.name = side.name + "_claws"; claws.data.name = claws.name
    return claws

# ---------------------------------------------------------------- build
bpy.ops.wm.read_factory_settings(use_empty=True)
sides = [Side(True), Side(False)]
arm = build_armature(sides)
sd = sides[0]
ob = sd.block()
finish_mesh(ob)
unwrap(ob)
if not skin_auto(ob, arm, sd):
    for vg in list(ob.vertex_groups): ob.vertex_groups.remove(vg)
    skin(ob, sd)
    ob.parent = arm
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
os.makedirs(OUT_DIR, exist_ok=True)
bake_ao(ob, OUT_AO)
claws = split_tips(ob, sd)
parts = [ob, claws, mirror(ob, "ArmL"), mirror(claws, "ArmL_claws")]
for o in parts: print("[alien_arms_v3]", o.name, "verts", len(o.data.vertices), "faces", len(o.data.polygons))
for sd in sides:
    for name, (pts, rads, d) in sd.digits.items():
        print(f"[alien_arms_v3] {name}{sd.suffix} root {tuple(round(v, 4) for v in pts[0])} tip {tuple(round(v, 4) for v in pts[3])}")

bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP', mesh_smooth_type='OFF', use_armature_deform_only=False,
                         primary_bone_axis='Y', secondary_bone_axis='X')
print("[alien_arms_v3] exported", OUT_FBX)

# ---------------------------------------------------------------- previews
if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_specular_highlight = True
    sc.display.shading.show_cavity = True
    sc.display.shading.cavity_type = 'BOTH'
    sc.display.shading.cavity_ridge_factor = 1.0; sc.display.shading.cavity_valley_factor = 1.0
    sc.render.resolution_x = 900; sc.render.resolution_y = 675
    skin_col, tip_col = (0.19, 0.31, 0.62, 1), (0.05, 0.05, 0.09, 1)
    for o in parts:
        o.color = tip_col if "claws" in o.name else skin_col
        for mat in o.data.materials:
            bsdf = mat.node_tree.nodes.get("Principled BSDF"); tex = [n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE']
            if bsdf and tex: mat.node_tree.links.new(tex[0].outputs["Color"], bsdf.inputs["Base Color"])
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    def shot(name, loc, look, lens=50):
        cam.location = Vector(loc)
        cam.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        sc.render.filepath = os.path.join(RENDER_DIR, name + ".png")
        bpy.ops.render.render(write_still=True)
    r = sides[0]
    hand_c = r.W(0, 0.09 * HAND, 0)
    def pose(curls, thumb_curl):
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='POSE')
        for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = Quaternion()
        axis = Vector((1, 0, 0))  # (the fingers curl about the across-the-hand axis, towards the palm)
        for sd in sides:
            for name, (pts, rads, d) in sd.digits.items():
                dirs = d if isinstance(d, list) else [d, d, d]
                for j in range(3):
                    pb = arm.pose.bones[f"{name}.0{j + 1}{sd.suffix}"]
                    if name == "thumb":
                        ax = dirs[j].cross(Vector((-sd.sx * 0.5, 0, -1))).normalized(); ang = thumb_curl[j]
                    else:
                        ax = axis; ang = curls[j]
                    local = (pb.bone.matrix_local.to_3x3().inverted() @ ax).normalized()
                    pb.rotation_quaternion = Quaternion(local, math.radians(ang))
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.context.view_layer.update()
    pose((0, 0, 0), (0, 0, 0))
    shot("top", hand_c + Vector((-0.12, 0.05, 0.3)), hand_c, 60)
    shot("palm", hand_c + Vector((0.05, 0.02, -0.3)), hand_c, 60)
    shot("side", hand_c + Vector((0.33, 0.03, 0.06)), hand_c, 60)
    pose((18, 24, 14), (8, 10, 8))
    shot("relaxed", hand_c + Vector((-0.1, 0.1, 0.28)), hand_c, 60)
    pose((82, 98, 66), (30, 36, 26))
    shot("fist", hand_c + Vector((-0.1, 0.1, 0.28)), hand_c, 60)
    shot("fist_side", hand_c + Vector((0.33, 0.03, 0.06)), hand_c, 60)
    pose((18, 24, 14), (8, 10, 8))
    # the eye's view: the camera where the game has it (0.22 above the model's origin, 0.05 behind it), 70 degrees tall
    cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(70)
    shot("eye", (0, 0.05, 0.22), (0, -0.6, -0.1), 50)
