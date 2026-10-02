"""
Bakes the crowd alien for the sudden death arena (SpaceArena.cs) from the player model's source mesh
(Tools/Alien2/Alien2.obj + its texture) into Assets/Game/Resources/SpaceArena/CrowdAlien.txt.

Run from the repo root (plain Python 3 with Pillow, no Blender):
    python Tools/crowd_alien.py            (or give another output path as the argument)

The crowd is hundreds of these, drawn in one go and animated in the vertex shader (SpaceArena/Crowd.shader), so the
file has what the shader needs and nothing else:
  - flat-shaded triangles (3 corners each; the clawed hands simplified) in Unity's space: metres, 1.8 m tall, feet on y = 0, facing +z, the
    model's left arm at -x (the same as the rigged player model, Tools/rig_alien2.py), in the source's A-pose;
  - one flat colour per triangle, sampled off the texture (so no texture is needed: the game's flat-colour look);
  - for every corner: which chain moves it (0 body, 1 head, 2 left arm, 3 right arm, 4 left leg, 5 right leg) and two
    blend weights: w1 = how much it follows the chain's first joint (shoulder / neck / hip) rather than the body,
    w2 = how much it follows the second joint (elbow / knee) rather than the first. Blending across the joints keeps
    the low-poly limbs in one piece when they bend;
  - whether it's on the head (the head is tinted lighter than the body, like the players);
  - the joints the shader turns the limbs round.

Format (whitespace separated): lines starting "P name x y z" are joints; then "T count"; then one line per corner:
    x y z  nx ny nz  r g b  chain w1 w2 head
"""
import math, os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(HERE, "Alien2", "Alien2.obj")
SRC_TEX = os.path.join(HERE, "Alien2", "Alientexturedtry2.png")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "Assets", "Game", "Resources", "SpaceArena", "CrowdAlien.txt")
HEIGHT = 1.8


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def norm(a):
    l = length(a)
    return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-12 else (0.0, 1.0, 0.0)
def clamp01(x): return 0.0 if x < 0 else 1.0 if x > 1 else x


# ---- read the OBJ (Blender's default export: y up, -z forward; so Blender (x, y, z) = OBJ (x, -z, y)) ----
V, VT, F = [], [], []
for line in open(SRC):
    s = line.split()
    if not s: continue
    if s[0] == 'v': V.append(tuple(map(float, s[1:4])))
    elif s[0] == 'vt': VT.append(tuple(map(float, s[1:3])))
    elif s[0] == 'f':
        F.append([tuple(int(x) if x else 0 for x in c.split('/')) for c in s[1:]])
B = [(v[0], -v[2], v[1]) for v in V]  # Blender space, like Tools/rig_alien2.py

# ---- the same scaling and joints as Tools/rig_alien2.py (feet on z = 0, 1.8 m tall) ----
zmin = min(b[2] for b in B); zmax = max(b[2] for b in B)
S = HEIGHT / (zmax - zmin)
B = [(b[0] * S, b[1] * S, (b[2] - zmin) * S) for b in B]
def P(x, y, z): return (x * S, y * S, (z - zmin) * S)
J = {
    "hips": P(0, 0.0, 1.50), "spine": P(0, 0.0, 1.78), "chest": P(0, 0.0, 2.05),
    "neck": P(0, 0.0, 2.55), "head": P(0, 0.0, 2.76), "top": P(0, 0.0, 3.25),
    "shoulder": P(0.27, 0.0, 2.54), "elbow": P(0.775, 0.005, 2.19), "wrist": P(1.08, -0.03, 1.92), "finger": P(1.33, -0.03, 1.69),
    "hip": P(0.20, 0.0, 1.56), "knee": P(0.26, -0.01, 0.95), "ankle": P(0.31, 0.04, 0.13), "toe": P(0.31, -0.30, 0.02),
}
def mx(v): return (-v[0], v[1], v[2])
# Blender +x is the model's left (rig_alien2.py names the +x limbs "Left")
bones = {
    "Hips": (J["hips"], J["spine"]), "Spine": (J["spine"], J["chest"]), "Chest": (J["chest"], J["neck"]),
    "Neck": (J["neck"], J["head"]), "Head": (J["head"], J["top"]),
}
for side, f in (("Left", lambda v: v), ("Right", mx)):
    bones[side + "UpperArm"] = (f(J["shoulder"]), f(J["elbow"]))
    bones[side + "LowerArm"] = (f(J["elbow"]), f(J["wrist"]))
    bones[side + "Hand"] = (f(J["wrist"]), f(J["finger"]))
    bones[side + "UpperLeg"] = (f(J["hip"]), f(J["knee"]))
    bones[side + "LowerLeg"] = (f(J["knee"]), f(J["ankle"]))
    bones[side + "Foot"] = (f(J["ankle"]), f(J["toe"]))


def seg_dist(p, a, b):
    ab = sub(b, a)
    t = max(0.0, min(1.0, dot(sub(p, a), ab) / dot(ab, ab)))
    return length(sub(add(a, mul(ab, t)), p))


def allowed(name, p):
    # (as rig_alien2.py) limbs only on their own side; legs not above the waist; the head not below the neck
    if name.startswith("Left") and p[0] < -0.005: return False
    if name.startswith("Right") and p[0] > 0.005: return False
    if "Leg" in name or "Foot" in name:
        if p[2] > J["hips"][2] + 0.06: return False
    if name in ("Head", "Neck") and p[2] < J["neck"][2] - 0.08: return False
    return True


def skin(p):
    """(chain, w1, w2) for a point in Blender space."""
    best = min((seg_dist(p, *bones[n]), n) for n in bones if allowed(n, p))[1]
    side = "Left" if p[0] > 0 else "Right"
    sgn = 1 if side == "Left" else -1
    sh, el = (J["shoulder"], J["elbow"]) if sgn > 0 else (mx(J["shoulder"]), mx(J["elbow"]))
    wr = J["wrist"] if sgn > 0 else mx(J["wrist"])
    adir = norm(sub(el, sh))
    t = dot(sub(p, sh), adir)
    # the top of the shoulder blends between the body and the arm along the arm (as the rig does)
    if abs(p[0]) > 0.11 and p[2] > J["chest"][2] + 0.1 and t < 0.1:
        best = side + "UpperArm"
    if best in ("Hips", "Spine", "Chest"):
        return 0, 0.0, 0.0
    if best in ("Neck", "Head"):
        return 1, clamp01((p[2] - (J["neck"][2] - 0.03)) / 0.1), 0.0
    if "Arm" in best or "Hand" in best:
        chain = 2 if side == "Left" else 3
        w1 = clamp01((t + 0.08) / 0.15)
        w2 = clamp01((dot(sub(p, el), norm(sub(wr, el))) + 0.04) / 0.08)
        return chain, w1, w2
    chain = 4 if side == "Left" else 5
    hip = J["hip"]; knee = J["knee"]
    w1 = clamp01((hip[2] + 0.03 - p[2]) / 0.1)
    w2 = clamp01((knee[2] + 0.04 - p[2]) / 0.08)
    return chain, w1, w2


def to_unity(b):  # Blender (x, y, z) -> Unity (-x, z, -y): faces +z, the model's left at -x
    return (-b[0], b[2], -b[1])


# ---- face colours off the texture ----
img = Image.open(SRC_TEX).convert("RGB")
W, H = img.size
def sample(uv):
    x = min(W - 1, max(0, int(uv[0] % 1.0 * W)))
    y = min(H - 1, max(0, int((1.0 - uv[1] % 1.0) * H)))
    return img.getpixel((x, y))

# ---- triangulate, check the winding (outward = counter-clockwise in the OBJ's right-handed space) ----
tris = []
for f in F:
    for i in range(1, len(f) - 1):
        tris.append((f[0], f[i], f[i + 1]))
vol = sum(dot(B[a[0] - 1], cross(B[b[0] - 1], B[c[0] - 1])) for a, b, c in tris) / 6.0
flip = vol < 0
print("[crowd_alien] %d triangles, volume %.4f m3%s" % (len(tris), abs(vol), " (winding flipped)" if flip else ""))

# ---- simplify the hands: the long clawed fingers are over half the triangles but only a pixel or two from the
# platform. Their vertices are snapped to a coarse grid (one per cell, at the cell's average) and the triangles that
# collapse are dropped, so the claws stay as a few chunky spikes ----
HAND_GRID = 0.05
key = list(range(len(B)))
cells = {}
for i, p in enumerate(B):
    sgn = 1 if p[0] > 0 else -1
    el = J["elbow"] if sgn > 0 else mx(J["elbow"])
    wr = J["wrist"] if sgn > 0 else mx(J["wrist"])
    fi = J["finger"] if sgn > 0 else mx(J["finger"])
    if dot(sub(p, wr), norm(sub(wr, el))) > -0.01 and seg_dist(p, wr, fi) < 0.16:
        k = (sgn,) + tuple(int(math.floor(x / HAND_GRID)) for x in p)
        cells.setdefault(k, []).append(i)
        key[i] = k
for k, ids in cells.items():
    avg = mul(tuple(sum(B[i][j] for i in ids) for j in range(3)), 1.0 / len(ids))
    for i in ids: B[i] = avg
seen = set()
kept = []
for a, b, c in tris:
    ks = (key[a[0] - 1], key[b[0] - 1], key[c[0] - 1])
    if len(set(ks)) < 3: continue
    sk = frozenset(ks)
    if sk in seen and any(isinstance(x, tuple) for x in ks): continue
    seen.add(sk)
    kept.append((a, b, c))
print("[crowd_alien] hands simplified: %d -> %d triangles" % (len(tris), len(kept)))
tris = kept

neck_cut = (J["neck"][2] + J["head"][2]) * 0.5
lines = []
for name, key in (("hips", "hips"), ("neck", "neck")):
    lines.append("P %s %.4f %.4f %.4f" % ((name,) + to_unity(J[key])))
for side, f in (("L", lambda v: v), ("R", mx)):
    for name in ("shoulder", "elbow", "wrist", "hip", "knee", "ankle"):
        lines.append("P %s%s %.4f %.4f %.4f" % ((name, side) + to_unity(f(J[name]))))
lines.append("T %d" % len(tris))
chains = [0] * 6
for a, b, c in tris:
    corners = [a, b, c]
    if flip: corners = [a, c, b]
    # mirroring x (right- to left-handed) turns the winding round: swap two corners so the outside stays the front
    corners = [corners[0], corners[2], corners[1]]
    pb = [B[k[0] - 1] for k in corners]
    pu = [to_unity(p) for p in pb]
    n = norm(cross(sub(pu[1], pu[0]), sub(pu[2], pu[0])))
    uvs = [VT[k[1] - 1] for k in corners]
    cu = ((uvs[0][0] + uvs[1][0] + uvs[2][0]) / 3, (uvs[0][1] + uvs[1][1] + uvs[2][1]) / 3)
    pts = [cu] + [((cu[0] + u[0]) * 0.5, (cu[1] + u[1]) * 0.5) for u in uvs]
    cols = [sample(u) for u in pts]
    col = tuple(sum(c[i] for c in cols) / len(cols) / 255.0 for i in range(3))
    centre = mul(add(add(pb[0], pb[1]), pb[2]), 1 / 3)
    head = 1 if centre[2] > neck_cut else 0
    for p, q in zip(pb, pu):
        ch, w1, w2 = skin(p)
        chains[ch] += 1
        lines.append("%.4f %.4f %.4f %.3f %.3f %.3f %.3f %.3f %.3f %d %.3f %.3f %d" % (q + n + col + (ch, w1, w2, head)))
print("[crowd_alien] corners per chain (body, head, arm L, arm R, leg L, leg R):", chains)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", newline="\n") as fo:
    fo.write("\n".join(lines) + "\n")
print("[crowd_alien] wrote", OUT)
