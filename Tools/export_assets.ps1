# Exports every 3D model in the game for Blender and zips them: Builds/RockAlienGame_3D_Assets.zip
#   1. (unless -NoBuild) builds the Windows player
#   2. runs the player in the export mode (-autotest exportassets, AutoTest.Export.cs): every model as OBJ + MTL in Builds/Models
#   3. tints the textures the models use (Tools/tint_textures.py)
#   4. copies the source models (the rigged alien and arms FBX, the original PS1 alien OBJ, textures, licence)
#   5. Blender: an FBX next to every OBJ, a .blend per category, one with everything, and the rigged alien / arms in the
#      four team skins (Tools/blender_pack.py)
#   6. zips the lot
# Usage: powershell -NoProfile -File Tools/export_assets.ps1 [-NoBuild] [-Out Builds/Models]
param([switch]$NoBuild, [string]$Out = "Builds/Models", [string]$Zip = "Builds/RockAlienGame_3D_Assets.zip")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$unity = @("C:/Program Files/Unity/Hub/Editor/6000.0.42f1/Editor/Unity.exe", "C:/Unity/6000.0.42f1/Editor/Unity.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
$blender = @("C:/Program Files/Blender Foundation/Blender 4.2/blender.exe", "C:/Program Files/Blender Foundation/Blender 4.3/blender.exe", "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $NoBuild) {
    "== building the Windows player"
    & $unity -batchmode -quit -projectPath . -executeMethod RockGame.EditorTools.ProjectSetup.BuildWindows -logFile Logs/batch_build.log | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unity build failed (exit $LASTEXITCODE): see Logs/batch_build.log" }
}
$export = Join-Path $root $Out
if (Test-Path $export) { Remove-Item -Recurse -Force $export }
New-Item -ItemType Directory -Force $export | Out-Null
"== exporting the models from the game"
& (Join-Path $PSScriptRoot "run_autotests.ps1") "exportassets:-solo -exportdir `"$export`"" -TimeoutSec 900
if (-not (Test-Path (Join-Path $export "index.txt"))) { throw "the export didn't finish (no index.txt): see Logs/autotest" }
"== tinting the textures"
$ErrorActionPreference = "Continue" # (native programs write progress to stderr; that's not an error)
python -I (Join-Path $PSScriptRoot "tint_textures.py") $export
if ($LASTEXITCODE -ne 0) { throw "tint_textures.py failed (exit $LASTEXITCODE)" }
"== copying the source models"
$src = Join-Path $export "sources"
New-Item -ItemType Directory -Force $src | Out-Null
foreach ($f in @("Assets/Game/Resources/Alien/AlienRigged.fbx", "Assets/Game/Resources/Alien/Alien.fbx", "Assets/Game/Resources/Alien/Alien2.png",
                 "Assets/Game/Resources/Alien/Alien_Body.png", "Assets/Game/Resources/Alien/Alien_Head.png", "Assets/Game/Resources/Alien/Alien_Body_Suited.png",
                 "Assets/Game/Resources/Alien/LICENSE.txt", "Assets/Game/Resources/FpsArms/AlienArms.fbx", "Assets/Game/Resources/FpsArms/AlienArmsAO.png",
                 "Tools/Alien2/Alien2.obj", "Tools/Alien2/Alien2.mtl", "Tools/Alien2/Alientexturedtry2.png", "Tools/Alien2/SOURCE.txt")) {
    if (Test-Path $f) { Copy-Item $f $src }
}
"== Blender: FBX and .blend files"
& $blender -b --factory-startup --python (Join-Path $PSScriptRoot "blender_pack.py") -- $export 2>$null | Where-Object { $_ -match '^\[pack\]|Error|Traceback' }
if ($LASTEXITCODE -ne 0) { throw "Blender failed (exit $LASTEXITCODE)" }
$ErrorActionPreference = "Stop"
$readme = @"
ALIEN ROCK GAME - 3D ASSETS
===========================
Every model in the game, exported by the game itself (so it's exactly what's drawn), in metres, Y up, right-handed.
Each model is there three ways, side by side: NAME.obj + NAME.mtl (Wavefront, loads anywhere), NAME.fbx (textures embedded).

  aliens/       the player alien in each team colour (blue, red, green, yellow) in every pose the game puts it in - idle,
                walking, sprinting, crouching, jumping, sliding, drawing the bow, winding up / throwing the spear, aiming,
                eating, swinging the rock / hatchet / sword, ramming, sawing, carrying the ball, riding, dead, dancing,
                knocked over, the lobby's lounging poses - with what it's holding; APose_rest is the untouched model.
                alien_<team>_body.png / _head.png: the skin texture in that team's tint.
  hands/        the first-person arms - square, round and alien - in each team colour, holding the rock, hatchet, spear,
                a drawn bow and the pistol. In view space: the eye at the origin, looking down +Z... (-Z in Blender's front).
  items/        every item (tools, weapons, guns, food, potions, the wacky airdrop items, Emerald mode's things), the ball,
                the rocket, the stuck spear and arrow.
  structures/   every building piece (foundation, wall, doorway, floor, stairs, barrier, window, tower, egg block, gate)
                in wood, stone, metal and armoured (the armoured trim in each team's colour).
  deployables/  chest, bag, airdrop crate, gamble machine, workbenches, sleeping bag, bear trap, ladder, turret, converter,
                barricade, fake airdrop, land mine, base guardian - a copy per team where the colour matters.
  vehicles/     car, horse, unicorn, slender, boat, training dummy.
  nature/       every pine variant with its stump, berry bushes, the airdrop bush.
  world/        the alien machines, upgrade stations, wood machines, emerald shops, flags, lucky blocks, the centre sign,
                the UFO, the hats, the wacky gadgets (bouncy castle, snail, clutter, stick of truth, word, smoke).
  blender/      <category>.blend - that category laid out in a grid; RockAlienGame_All.blend - everything;
                AlienRigged_Teams.blend - the rigged alien (skeleton, ready to pose) in the four team skins;
                AlienArms_Teams.blend - the rigged first-person arms the same way.
  sources/      the original files: AlienRigged.fbx (the rigged alien the game loads), Alien.fbx (the older alien),
                AlienArms.fbx (the arms), their textures, the PS1 low-poly grey alien it was rigged from (Alien2.obj, CC0 - see LICENSE.txt).
  index.txt     every exported model with its part and triangle count.

Blender: File > Import > Wavefront (.obj) or FBX (.fbx) with the defaults, or just open a .blend. The models are made of
many flat-coloured parts (one object each, named after the part); materials are named by their colour (c_RRGGBB), with
_aNN for see-through ones and _eRRGGBB for glowing ones.
"@
Set-Content -Encoding utf8 (Join-Path $export "README.txt") $readme
"== zipping"
$zipPath = Join-Path $root $Zip
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $export "*") -DestinationPath $zipPath -CompressionLevel Optimal
"== done: $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB)"
