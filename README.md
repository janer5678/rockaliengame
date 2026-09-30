# Rock Base Brawl

A 1v1 first-person "gather, build, raid" game for Unity 6 (6000.0.42f1, URP) using **Netcode for GameObjects 2.4**.
Both players start with a rock, gather wood and stone, build a Rust-style base, craft tools and weapons, and fight over a ball that drops in the middle of the map.

## How to test

### Option A: two players inside the editor (Multiplayer Play Mode)
1. Open the project and open `Assets/Scenes/Game.unity`.
2. Open **Window > Multiplayer > Multiplayer Play Mode** and tick **Player 2**. Wait for it to boot.
3. Press Play. In the main editor window click **HOST GAME**. In the Player 2 window click **JOIN GAME** (IP `127.0.0.1`).

### Option B: standalone builds (best for LAN play with a friend)
1. **Rock Game > Build Windows Player**. The build goes to `Builds/Windows/RockBaseBrawl.exe`.
2. Run it twice (or on two PCs). One clicks **HOST GAME**, the other types the host's IP and clicks **JOIN GAME**.
   Port 7777/UDP must be reachable; allow the Windows firewall prompt.
   You can also press Play in the editor and host or join from there against a build.

### Main menu
Laid out top to bottom: **MATCH SETUP** (what the host picks), then **PLAY** (host, or type the host's IP and join; the **port ▼** button next to the IP folds out the port if you need to change it), then **SETTINGS** and **QUIT**. The option you've picked shows pressed in with gold text. Buttons blip when you point at them and click when you press them; sliders tick as they move.

### Match setup (set by the host)
- **Players** (one button that folds open the eight choices): red vs blue in *1v1*, *2v2*, *3v3* or *4v4*; or the X-shaped map with 3 or 4 bases (glass walls in an X, the map laid out four ways round): *FFA 3* / *FFA 4* (free for all, one player per base), *2v2v2* or *2v2v2v2* (teams of two). Sudden death is last team standing.
- While waiting for everyone to join, players brawl with rocks in the stadium; when the lobby is full everyone is sent to their base and the match starts.
- **Graphics**: *Normal* or *PSX*. PSX swaps the 3D models for low-res PSX-style ones (trees so far, more to come); only the looks change - trees are the same size to hit, give the same wood and have the same weak spots, and everything else in the game works the same in both. Each player picks their own (also in Settings > Display, and it switches live). Normal is our own look, where new features get made first.
- **Testing** (on/off buttons): **Solo test** - the match starts without an opponent, so you can try everything alone. **Fast timers** - 10s wall/ball drop and a 90s match instead of 5 min behind the wall and then 15 min.
- **Map**: *Plains* (flat) or *Highlands* (the wild map: random hilly terrain with minable rocks and watch towers, new every match), each in **Small**, **Medium**, **Large** (1.5x) or **Huge** (2x) (the bigger maps get more trees, rocks, bushes, horses and towers). Picked by the host; the client builds the same map from the synced seed.
- **Mode**: *Normal* or *Wood mode* (no stone anywhere, every rock is a tree, everything costs wood only, no pickaxe, no stone upgrades).
- **MODE OPTIONS** (button on the main menu):
  - **Airdrops per match** (0-20): evenly spaced over the time after the glass wall drops - 1 comes half way through (7:30 after the wall drops), 2 at a third and two thirds, and so on. None come while the wall is up.
  - **Where**: *Anywhere* (one at a random spot), *One per side* (every side gets its own) or *Middle of the map* (always right by the centre).
  - **Airdrop items**: tick which items airdrops can have: C4, Death Wand, Portal Gun, Rocket Launcher, Tree Camo, Invisibility Potion, Jetpack, Wallhack Glasses, Fake Bomb Bush.
  - **Respawn**: *Normal* or *With an airdrop item* (every time you respawn you get a random one of the picked airdrop items).

Command line: `-host`, `-client <ip>`, `-port <n>`, `-solo`, `-fast`, `-map plains|highlands`, `-small`, `-big` (medium), `-wood`, `-normal`, `-sides`, `-anywhere`, `-seed <n>`, `-mode 1v1|2v2|3v3|4v4|ffa3|ffa4|2v2v2|2v2v2v2`, `-large`, `-huge`.

## Pause menu (Esc)
- **Resume**, **Settings**, **Controls**, **Dev settings**, **Leave game**. Esc goes back a page.
- **Settings** (also on the main menu), in tabs:
  - **Sound**: Master, Sound effects (every game and menu sound) and Voice chat volume.
  - **Controls**: mouse sensitivity, and every key laid out by what it does (movement, actions, building, voice, hotbar) with a note on what each is for. Click a key and press the new key or mouse button to rebind it; every action has a main and a second key. Esc cancels, Backspace clears, **Reset controls** puts the defaults back.
  - **Display**: Fullscreen / Borderless window / Windowed, resolution and refresh rate. The game runs at your screen's highest refresh rate unless you pick another; **Use my screen's best** picks your screen's resolution and highest refresh rate; **Apply** switches.
  - **Voice chat** (proximity): Off / Open mic / Push to talk, the microphone, mic volume, open-mic sensitivity and a live mic level meter. You only hear people near you (3D, fades out by ~45 m).
- **Dev settings**: drop the wall now, pause/resume the timer, +/- 1 minute, timer to 10 s, start sudden death, win now, spawn an airdrop, ball to me / to the middle, regrow nodes, spawn a horse or car, +1000 wood/stone, +50 arrows, all airdrop items, one of every craftable, clear inventory, heal, god mode, kill me, and teleports (my base, enemy base, ball, airdrop). Everyone is told when someone uses one.

## Rules
- In the middle of each base is an unbreakable silver **bedrock** (2x2 cells). You spawn on it. It counts as a foundation (walls and stairs can stand on it) but nothing can be placed on it.
- On the bedrock stands your **alien machine** (with room to build walls behind it, and room for chests around it). It has a **socket** (the glowing cradle under the arch) where the ball has to sit.
- **Spears and hatchets can be crafted anywhere; everything else only inside your own base** (TAB).
- A **big glass wall** splits the map in half for the first **5 minutes**. When it drops, the **ball** drops in the middle and a **7:00** timer starts (12 minutes in total).
- Pick the ball up (E) and **throw it with LMB**. When it gets close to a machine's socket it snaps in. While you carry it, it fills the bottom half of your screen. A beacon of light shoots into the sky while it sits in a socket. Enemies can take it back out with E.
- When the timer hits 0, whoever has the ball **in their machine's socket wins**. Lying somewhere in your base doesn't count.
- The last 10 seconds before the end count down huge in the middle of the screen with a pulsing red edge and ticks.
- If the ball is in no socket, both players are teleported to the **sudden death stadium**: a huge round arena with tiered stands full of cheering alien spectators, floodlights and jumbotrons. Everyone is healed, inventories are emptied and all armour and helmets are taken off, then a big 5-4-3-2-1-FIGHT countdown. Rocks only, first kill wins.
- There is no rock item: whenever your selected hotbar slot is empty, you're holding your rock.
- When you die **all your items burst out of your body** onto the ground, including armour and a helmet you were wearing. Anyone can pick them up with E. Items on the ground disappear after 5 minutes.
- Respawning: while the glass wall is up you always come back on your bedrock. After that you choose: **respawn in base** or **respawn in the wild** (a random spot in the enemy's half of the map).

## Airdrops
Once the wall is down, a giant alien ship comes down from very high up and **beams an airdrop crate** to a random spot (never close to a base; follow the purple beam; a big "AIRDROP INCOMING" shows in the middle of the screen). How many come per match and which items they can have are set in **MODE OPTIONS**; a crate nobody emptied stays put. An **Airdrop Signal** (craft it in your base for 2000 wood) beams one straight onto your bedrock. Each crate holds one random item from the picked ones. The items in the game (only the first nine can be picked for now; the rest are unused):

| Item | What it does |
|---|---|
| C4 | Thrown. After 3 s it destroys every enemy building piece, barrier and chest within 5 m and hurts players nearby; right on top of it you die. |
| Death Wand | One shot. Anyone the bolt passes close to, or who is near where it hits, dies instantly. |
| Alien Helmet | Put it on: the next headshot does no damage and breaks it. Drops if you die wearing it. |
| Armour | Put it on: 100 extra health in a second bar, used up first. Drops with the health it had left. |
| 1000 wood or stone | (always wood in wood mode) |
| Invisibility Potion | 30 s of invisibility. Attacking shows you for a moment. |
| Chainsaw | Hold LMB: cuts wood and stone very fast. Breaks after 67 hits. |
| Fort Tower | Thrown: an enclosed lookout tower pops up where it lands (doorway facing you, ladder inside). |
| Sniper Rifle | 3 shots. RMB scope. Anything it hits dies, unless it's a headshot on someone with a helmet (the helmet breaks). |
| Portal Gun | 2 shots = one linked pair of portals on any surface (ground too). Every portal has its own colour, starting with Portal's blue and orange. They last the whole game. Walk in one, come out the other; you can't bounce straight back. |
| Jetpack | Hold it and hold Space to fly up. Limited fuel. |
| Slenderman Egg | Thrown: Slenderman hatches and hunts your enemy. It kills on touch. Kill it (150 HP) or wait 60 s for it to vanish. |
| Build Egg | Thrown: slabs appear under its flight path - a staircase you can walk along (Bedwars style). |
| Staff of the Giant | Your nearest enemy becomes a giant for 30 s: huge and easy to spot, same small hitbox, and you look tiny to them. |
| Rocket Launcher | One rocket. Wrecks enemy buildings in a small radius and hurts players. |
| Fake Bomb Bush | Thrown: looks exactly like a berry bush. Whoever picks it blows up. |
| Tree Camo | While it's in your hand you're a tree to everyone, and your camera pulls back to third person so you can see it. |
| Airstrike | Opens a map: click a spot and a few seconds later everything there (players, buildings, trees, rocks) is flattened. |
| Wallhack Glasses | While you hold them, enemies glow red through walls. |

## Game settings (CHANGE VALUES)
**CHANGE VALUES** on the main menu lists every stat: timings, airdrops, player HP and speeds, damage and gather rate for every tool, node HP, building costs and HP, crafting costs and more. They're folded into sections: click a heading to open it (each shows how many values it has and how many you changed). Only the values you change are saved on your PC (so new defaults still reach you). When you host, your values are used for the whole match and sent to everyone.

- **Export to a file** writes `RockBaseBrawl-settings.txt` next to the game (the project folder in the editor): every value, plus the main menu choices and mode options, with the changed ones marked. Send it over and say what it's for - new default settings, or the base of a new game mode.
- **Import from the file** reads it back in; **Open the folder** shows where it is.

## Controls
| Key | Action |
|---|---|
| WASD / Shift / Space / Ctrl or C | move / sprint / jump / crouch (also with the inventory open) |
| Sprint, then Ctrl / C | **slide** (Crab Game style): you keep your speed, go faster downhill and slower uphill, steer a little, and can jump out of it without losing speed. Hold crouch to keep sliding; how slippery it is is **Slide Slipperiness** in CHANGE VALUES (0 = grippy, 10 = ice, default 7.5 - a long glide), along with Slide Boost, Slide Min Speed and Slide Steer |
| Mouse / LMB | look / attack, gather, place |
| Hold LMB (bow) | draw, release to fire (RMB cancels) |
| Spear | LMB stab · **hold RMB** to wind up, then **LMB** to throw |
| Hold LMB (ram) | wind up and slam the enemy piece you're looking at |
| RMB (berries) | eat (+15 HP) |
| 1-7, mouse wheel | hotbar slot |
| TAB | inventory (21 slots + 7 hotbar) |
| E | use whatever you're looking at: your machine (put the ball in), ball, door, chest, airdrop, berry bush, dropped items and arrows, horse or car (E again to get off), a spear stuck in someone (or in you) |
| Building plan | **hold RMB: building wheel** (Rust style, light blue, with a picture of each piece; clockwise from the top: Foundation, Ceiling, Wall (right), Window, Demolish (trash can, bottom), Stairs, Doorway, Upgrade) · R rotate stairs · F upgrade to stone · X demolish your own piece (half the wood back) |
| Crossbow | LMB fire · hold RMB aim · reloads itself (uses an arrow) |
| Helmet / armour / potion | LMB (or RMB) to put on / drink |
| C4 / fort tower | LMB throws it |
| V | push to talk (proximity voice chat) |
| Esc | pause (the controls list is in the pause menu, and every key above can be rebound there) |

## Inventory
- Wood, stone, arrows and berries are real items that take up slots. Tools and weapons go to the hotbar first.
- Materials stack onto what you already have; otherwise they go on the hotbar from **slot 7 backwards** (never into the empty slot you're holding your rock in), then the main inventory.
- Everything that goes into your inventory pops up in the bottom right ("+30 Wood"); materials spent on building and crafting show up there in red ("-15 Wood").
- A chest you open shows up to the **right** of your inventory.
- You always hold something: the hotbar only scrolls through slots that have an item. The rock stays on your hotbar - it can't be dropped or put in a chest.
- **Drag an item outside the inventory** to throw it on the ground; look at it and press E to pick it up.
- Drag to move or swap, **right-drag** to split a stack in half, **shift-click** to quick-move (between chest/bag and inventory, or hotbar and inventory).
- **Storage chest**: craft it, hold it and click to place it anywhere inside your base. Press E on it to open it next to your inventory (14 slots). Enemies who break in can loot it, and breaking it spills its contents into a bag.

## Crafting (anywhere in your base, TAB)
| Item | Cost |
|---|---|
| Stone Hatchet (fast wood) | 50 wood |
| Stone Pickaxe (fast stone) | 30 wood, 10 stone |
| Spear | 75 wood |
| Building Plan | 5 wood |
| Bow | 100 wood, 15 stone |
| Arrow | 10 wood |
| Crossbow (55 damage, flat and fast) | 500 wood |
| Armour (goes straight on: 100 extra health) | 500 wood |
| Chainsaw (67 uses) | 500 wood |
| Battering Ram (3 hits) | 125 wood, 50 stone |
| Storage Chest | 50 wood |
| High External Wall | 40 wood |
| Saddle (ride a wild horse; in your team's colour) | 1000 wood |
| Airdrop Signal (an airdrop beams onto your bedrock) | 2000 wood |

In wood mode the stone part is added to the wood cost and there's no pickaxe.

## Gathering
- Felling a whole tree gives a +100 wood bonus ("TIMBER!").
- The first hit on a tree or rock reveals its weak spot: an orange **X** on trees (a chunky pixel-art X on PSX trees, sitting on the bark you see), a sparkling **star** on rocks. Hitting it gives double resources, and it jumps to a new spot facing you. Hitting a tree's X plays a chime; hits in a row on the same tree climb up the scale (C D E G A C).
- On the Highlands map every rock on the hills is a minable stone node.
- **Berry bushes** are loaded with berries: press E to pick the whole bush (it disappears), hold the berries and press RMB to eat (takes 1.5 s, +25 HP). A new bush grows up out of the ground somewhere else in the same half a couple of minutes later. Bushes are fairly rare.
- Arrows that miss stay stuck where they land; press E to pick them back up.

## Spears
- Thrown spears fly in an arc and stay in the world. Look at one and press **E** to pick it up.
- A spear that hits a player (up to 60 damage at full wind-up) sticks in them. Anyone can press **E** on them to pull it out and keep it, including the victim. Spears stuck in you go into your loot bag if you die.

## Building
- You can only build inside your own base (the tinted square with the grid). Pieces snap to a 3 m grid. There's no delay between placements, and you can place pieces right where you stand (you get lifted on top).
- Pieces: Foundation (15 wood), Wall (15), Doorway with a lockable door (20), Window (15), Floor (12), Stairs (20).
- You don't have to aim at the bottom edge: look along where you want the wall (eye height or above works) and it goes to the farthest spot along your aim where it can actually go (so it lands where you're pointing).
- **High external walls** (500 HP, like Rust's): a free-standing palisade of sharpened logs 4 m wide and about 5.5 m tall. Place them out on the map or inside your own base (not in the enemy base, not on the bedrock).
- Rust-style support: walls and stairs need a foundation or floor, and floors need a wall below or a neighbouring floor. Destroying support collapses what's on it.
- Upgrade to stone (30-50 stone) for 3-4x HP. Melee does only 20% damage to stone, so bring a ram.

## Horses and the car
- **Wild horses** wander each half of the map. Craft a **saddle** (it's in your team colour), walk up to one and press E to saddle it and get on. It rides like a Minecraft horse: it goes where you look, Shift gallops, Space jumps. Your view sits high up so you can see ahead.
- **The ball on horseback**: you can get on a horse while carrying the ball, and pick the ball up from the saddle (look at it and press E - E only gets you off when you're not looking at the ball). LMB throws it from the horse; it keeps the horse's speed. (Cars still can't carry the ball.)
- Horses have 120 HP (shown when you look at one once it's been hurt), bleed when hit and bolt away from whoever hurt them. They can be killed: they drop **horse meat** (takes 3 s to eat, heals you fully) and their saddle.
- (The wooden car is switched off for now.)

## Combat & raid balance
| | Wood HP | Stone HP |
|---|---|---|
| Wall | 400 | 1500 |
| Doorway | 350 | 1200 |
| Foundation | 500 | 1800 |

- **Ram**: a hand-held log. Hold LMB for 1.5 s next to an enemy piece: **wooden pieces and chests break instantly**, **stone pieces are knocked back down to full-HP wood**. 3 hits per ram. You move 25% slower while holding it.
- Player damage: rock 12, hatchet/pickaxe 14, spear 35 (stab) / 60 (thrown), arrow up to 50 at full draw, crossbow 55. **Headshots do x2** for everything.
- Weapons do double damage to buildings compared to before.
- You can't shoot through the glass wall by standing right up against it.
- The bow shoots slower arrows with more drop (55 m/s, normal gravity), like the original bow. It's held like Rust's hunting bow: left arm in from the lower left, and when drawn the arrow lines up with the crosshair.
- Hitboxes (radius 0.52 m) wrap the alien, and there's hit assist like most games: a melee swing counts if it passes within 0.35 m of a player, arrows/spears within 0.15 m (both adjustable in CHANGE VALUES, as are the hitbox size, bow speed and arrow drop).
- Melee lands on the swing's impact frame. The rock is swung two-handed: a hit bounces your hands back up with a short hit-stop, a miss follows through down.
- Other players are rigged (a skinned mesh, so the body really bends) and animated procedurally, and you see what they're doing: idle breathing, walk / run / sprint forwards, backwards and strafing, crouch walk, jump tuck and landing dip, **sliding** (low, leaning back, front leg out), holding each item, **drawing a bow** (bow arm out, string hand to the cheek), **winding up a spear** (cocked over the shoulder), **aiming the crossbow / sniper** (shouldered), **eating / drinking**, **charging the ram**, **running the chainsaw**, **throwing** (overhand), swings with one or two hands, carrying the ball, riding, and falling over on death. What each player is doing is synced (`PlayerNet.Action`), so it shows on every screen. `-autotest rig -host -solo` photographs every pose from the front and the side.
- Hits show blood where they land, a hit marker (orange on headshots, red on kills), a damage number, and a bit of screen shake.

## Project layout
- `Assets/Scripts/`: all game code (namespace `RockGame`). `Config.cs` holds every tuning number.
- `Assets/Scripts/Editor/ProjectSetup.cs`: **Rock Game > Rebuild Scene, Prefabs and Settings** regenerates the scene, network prefabs and materials.
- All art is procedural low-poly stand-ins built at runtime (`Art.cs`, `MapBuilder.cs`, `Airdrop.cs`, `Vehicle.cs`), except the player model.
- `Vehicle.cs`: horses and the car (a networked prefab owned by whoever drives it). `VoiceChat.cs`: proximity voice chat and the sound/mic settings. `PlayerNet.Extras.cs`: crossbow, fort tower, riding, dev settings and voice RPCs.
- Player model: PSX-style grey alien "Low-spec Reticulans" by surt, CC0 ([OpenGameArt](https://opengameart.org/content/low-spec-reticulans)), in `Assets/Game/Resources/Alien/`. `AlienRigged.fbx` is the same mesh skinned to a humanoid skeleton (Hips, Spine, Chest, Neck, Head, Left/Right UpperArm/LowerArm/Hand/UpperLeg/LowerLeg/Foot) and animated procedurally in `BodyAnimator.cs`. It is tinted in the team colour.
- `AutoTest.cs`: headless end-to-end test (`-autotest ball` or `-autotest sd` on a host + client build; `-autotest shots` takes screenshots; `-autotest psx -host -solo -psx` screenshots the PSX trees, their X and the hit effects, then the same in normal graphics).
- PSX graphics: `PsxArt.cs`; the tree models and their 128x128 textures are in `Assets/Game/Resources/PsxTrees/` (from `tree_pack_1.1`, 36 trees; drawn with the alpha-cutout `PsxCutout.mat`, import settings in `Editor/PsxImport.cs`). Each tree's trunk thickness is measured so the X sits on it, and its bark and leaf colours colour the chips. The tree X chime notes are in `Assets/Game/Resources/TreeHit/` (from the Samples pack, Sound_1 high notes).
- Sound effects are synthesised procedurally at startup in `Fx.cs`. Everything in the world is a 3D sound with no spread and a steep, realistic falloff, so you can tell where it is and how far: arrows, spears and thrown items whoosh while they fly (with doppler as they pass), bow shots, spear throws and impacts carry about 100 m, other players' footsteps and slides and horses' hooves can be heard from where they are (crouch-walking is silent), and explosions and sniper shots carry across the map. Hand-drawn item icons in `Assets/Game/Resources/Icons/<item>.png` (e.g. `hatchet.png`) replace the rendered ones. Item icons are rendered from the 3D models at startup (`ItemIcons.cs`); first-person hands and animations are in `ViewModel.cs`.

Networking: movement is owner-authoritative (NetworkTransform in Owner mode). Resources, crafting, building, damage, the ball and its socket, inventories, chests, airdrops, dropped items and match flow are server-authoritative. Melee, arrow, thrown-spear and C4 hits use client-side hit detection that the server validates; the death wand is resolved on the server.
