"""
Makes the tinted textures the model export asks for (Tools/export_assets.ps1): the game draws a texture times a colour
(URP's BaseMap x BaseColor - the alien's grey skin in the team colour, the arms' baked shading under the hand colour), so
each OBJ's materials point at a PNG with that tint baked in. The export writes <dir>/tints.txt: one line per texture to
make - out file (relative to <dir>), source under Assets/Game/Resources, and the r g b multiplier (can be above 1).

    python -I Tools/tint_textures.py <export dir>
"""
import os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
RES = os.path.join(REPO, "Assets", "Game", "Resources")


def main(out_dir):
    manifest = os.path.join(out_dir, "tints.txt")
    if not os.path.exists(manifest):
        print("no tints.txt in", out_dir)
        return 1
    cache = {}
    made = 0
    for line in open(manifest, encoding="utf-8-sig"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 5:
            print("bad line:", line)
            continue
        out, src, r, g, b = parts
        mul = (float(r), float(g), float(b))
        src_path = os.path.join(RES, src.replace("/", os.sep))
        if src_path not in cache:
            cache[src_path] = Image.open(src_path).convert("RGBA")
        img = cache[src_path]
        px = img.load()
        w, h = img.size
        dst = Image.new("RGBA", (w, h))
        dp = dst.load()
        for y in range(h):
            for x in range(w):
                pr, pg, pb, pa = px[x, y]
                dp[x, y] = (min(255, int(round(pr * mul[0]))), min(255, int(round(pg * mul[1]))), min(255, int(round(pb * mul[2]))), pa)
        out_path = os.path.join(out_dir, out.replace("/", os.sep))
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        dst.save(out_path)
        made += 1
    print(f"tinted {made} textures")
    return 0


if __name__ == "__main__":
    sys.exit(main(os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(REPO, "Builds", "Models")))
