"""
Makes the first-person arms' skin detail map (Assets/Game/Resources/FpsArms/AlienArmsSkin.png) from the arms model's
own skin texture (Tools/FpsArms/new_diff.png - see SOURCE.txt): its light and shade only (knuckle creases, nails, the
palm's lines), grey, about 0.9 on average, so the game tints it any team colour (ViewModel.Rig.cs / HandColorHook).
The empty space between the UV islands is filled with the average grey, so no dark edges creep in at the seams.

    python -I Tools/fps_arms_skin.py
"""
import os
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "FpsArms", "new_diff.png")
OUT = os.path.join(os.path.dirname(HERE), "Assets", "Game", "Resources", "FpsArms", "AlienArmsSkin.png")

im = Image.open(SRC).convert("RGB")
w, h = im.size
px = im.load()
lum = [[0.0] * w for _ in range(h)]
total = 0.0
n = 0
for y in range(h):
    for x in range(w):
        r, g, b = px[x, y]
        l = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
        lum[y][x] = l
        if l > 0.06:
            total += l
            n += 1
mean = total / max(1, n)
out = Image.new("L", (w, h))
o = out.load()
for y in range(h):
    for x in range(w):
        l = lum[y][x]
        # (a little more contrast than the photo-real original, so the creases and nails read once it's tinted)
        v = 0.88 if l <= 0.06 else 0.88 + (l / mean - 1.0) * 2.2
        o[x, y] = int(max(0.3, min(1.0, v)) * 255)
out = out.filter(ImageFilter.SMOOTH)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
out.save(OUT)
print("skin detail written:", OUT, "mean", round(mean, 3))
