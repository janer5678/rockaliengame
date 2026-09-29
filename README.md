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

### Menu options (set by the host)
- **Solo test**: the match starts without an opponent, so you can try everything alone.
- **Fast timers**: 10s wall/ball drop and a 90s match instead of 5 min and 7 min.
- **Map**: *Plains* (flat) or *Highlands* (the wild map: random hilly terrain with minable rocks and watch towers, new every match), each in **Big** or **Small**. Picked by the host; the client builds the same map from the synced seed.
- **Mode**: *Normal* or *Wood mode* (no stone anywhere, every rock is a tree, everything costs wood only, no pickaxe, no stone upgrades).
- **Airdrops**: *Anywhere* (one at a time, random spot) or *One per side* (each half of the map gets its own airdrop at a random spot, each on its own timer).
- **Respawn**: *Normal* or *With an airdrop item* (every time you respawn you get a random airdrop item).

Command line: `-host`, `-client <ip>`, `-port <n>`, `-solo`, `-fast`, `-map plains|highlands`, `-small`, `-big`, `-wood`, `-normal`, `-sides`, `-anywhere`, `-seed <n>`.

## Pause menu (Esc)
- **Sound**: master volume, hit sounds volume (hit markers, flesh hits, kills) and voice chat volume.
- **Proximity voice chat**: Off / Open mic / Push to talk (V), pick the microphone, mic volume, open-mic sensitivity and a live mic level meter. You only hear people near you (3D, fades out by ~45 m).
- **Dev settings**: drop the wall now, pause/resume the timer, +/- 1 minute, timer to 10 s, start sudden death, win now, spawn an airdrop, ball to me / to the middle, regrow nodes, spawn a horse or car, +1000 wood/stone, +50 arrows, all airdrop items, one of every craftable, clear inventory, heal, god mode, kill me, and teleports (my base, enemy base, ball, airdrop). Everyone is told when someone uses one.
- **Leave game**.

## Rules
- In the middle of each base is an unbreakable silver **bedrock** (2x2 cells). You spawn on it. It counts as a foundation (walls and stairs can stand on it) but nothing can be placed on it.
- On the bedrock stands your **alien machine** (with room to build walls behind it, and room for chests around it). It has a **socket** (the glowing cradle under the arch) where the ball has to sit.
- **Crafting works anywhere inside your own base** (TAB).
- A **big glass wall** splits the map in half for the first **5 minutes**. When it drops, the **ball** drops in the middle and a **7:00** timer starts (12 minutes in total).
- Pick the ball up (E) and **throw it with LMB**. When it gets close to a machine's socket it snaps in. While you carry it, it fills the bottom half of your screen. A beacon of light shoots into the sky while it sits in a socket. Enemies can take it back out with E.
- When the timer hits 0, whoever has the ball **in their machine's socket wins**. Lying somewhere in your base doesn't count.
- The last 10 seconds before the end count down huge in the middle of the screen with a pulsing red edge and ticks.
- If the ball is in no socket, both players are teleported to the **sudden death stadium**: a huge round arena with tiered stands full of cheering alien spectators, floodlights and jumbotrons. Everyone is healed, inventories are emptied and all armour and helmets are taken off, then a big 5-4-3-2-1-FIGHT countdown. Rocks only, first kill wins.
- There is no rock item: whenever your selected hotbar slot is empty, you're holding your rock.
- When you die **all your items burst out of your body** onto the ground, including armour and a helmet you were wearing. Anyone can pick them up with E. Items on the ground disappear after 5 minutes.
- Respawning: while the glass wall is up you always come back on your bedrock. After that you choose: **respawn in base** or **respawn in the wild** (a random spot in the enemy's half of the map).

## Airdrops
Once the wall is down, a giant alien ship comes down from very high up and **beams an airdrop crate** to a random spot (never close to a base; follow the purple beam; a big "AIRDROP INCOMING" shows in the middle of the screen). Only one exists at a time; the next one comes **1 minute after the last one was emptied**. An **Airdrop Signal** (craft it in your base for 2000 wood) beams one straight onto your bedrock. Each crate holds one random item:

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
| Tree Camo | While it's in your hand, everyone else sees a tree where you stand. |
| Airstrike | Opens a map: click a spot and a few seconds later everything there (players, buildings, trees, rocks) is flattened. |
| Wallhack Glasses | While you hold them, enemies glow red through walls. |

## Game settings
**GAME SETTINGS** on the main menu lists every stat: timings, airdrops, player HP and speeds, damage and gather rate for every tool, node HP, building costs and HP, crafting costs and more. Only the values you change are saved on your PC (so new defaults still reach you). When you host, your values are sent to your opponent and used for the whole match.

## Controls
| Key | Action |
|---|---|
| WASD / Shift / Space / Ctrl or C | move / sprint / jump / crouch (also with the inventory open) |
| Mouse / LMB | look / attack, gather, place |
| Hold LMB (bow) | draw, release to fire (RMB cancels) |
| Spear | LMB stab · **hold RMB** to wind up, then **LMB** to throw |
| Hold LMB (ram) | wind up and slam the enemy piece you're looking at |
| RMB (berries) | eat (+15 HP) |
| 1-7, mouse wheel | hotbar slot |
| TAB | inventory (21 slots + 7 hotbar) |
| E | use whatever you're looking at: your machine (put the ball in), ball, door, chest, airdrop, berry bush, dropped items and arrows, horse or car (E again to get off), a spear stuck in someone (or in you) |
| Building plan | **hold RMB: building wheel** (Rust style, with a picture of each piece: Foundation, Wall, Doorway, Window, Demolish at the bottom, Stairs, Floor, Upgrade) · R rotate stairs · F upgrade to stone · X demolish your own piece (half the wood back) |
| Crossbow | LMB fire · hold RMB aim · reloads itself (uses an arrow) |
| Helmet / armour / potion | LMB (or RMB) to put on / drink |
| C4 / fort tower | LMB throws it |
| V | push to talk (proximity voice chat) |
| F1 / Esc | toggle help / pause |

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
| Building Plan | 5 wood |
| Stone Hatchet (fast wood) | 30 wood, 10 stone |
| Stone Pickaxe (fast stone) | 30 wood, 10 stone |
| Spear | 75 wood |
| Bow | 100 wood, 15 stone |
| Arrow | 10 wood |
| Crossbow (55 damage, flat and fast) | 500 wood |
| Battering Ram (3 hits) | 125 wood, 50 stone |
| Storage Chest | 50 wood |
| Wooden Barrier | 20 wood |
| Saddle (ride a wild horse; in your team's colour) | 1000 wood |
| Airdrop Signal (an airdrop beams onto your bedrock) | 2000 wood |

In wood mode the stone part is added to the wood cost and there's no pickaxe.

## Gathering
- Felling a whole tree gives a +100 wood bonus ("TIMBER!").
- The first hit on a tree or rock reveals its weak spot: an orange **X** on trees, a sparkling **star** on rocks. Hitting it gives double resources, and it jumps to a new spot facing you.
- On the Highlands map every rock on the hills is a minable stone node.
- **Berry bushes** are loaded with berries: press E to pick the whole bush (it disappears), hold the berries and press RMB to eat (+30 HP). A new bush grows up out of the ground somewhere else in the same half a while later.
- Arrows that miss stay stuck where they land; press E to pick them back up.

## Spears
- Thrown spears fly in an arc and stay in the world. Look at one and press **E** to pick it up.
- A spear that hits a player (up to 60 damage at full wind-up) sticks in them. Anyone can press **E** on them to pull it out and keep it, including the victim. Spears stuck in you go into your loot bag if you die.

## Building
- You can only build inside your own base (the tinted square with the grid). Pieces snap to a 3 m grid. There's no delay between placements, and you can place pieces right where you stand (you get lifted on top).
- Pieces: Foundation (15 wood), Wall (15), Doorway with a lockable door (20), Window (15), Floor (12), Stairs (20).
- You don't have to aim at the bottom edge: look along where you want the wall (eye height or above works) and it goes to the farthest spot along your aim where it can actually go (so it lands where you're pointing).
- **Wooden barriers** (250 HP) are small free-standing walls you can place anywhere except the enemy base.
- Rust-style support: walls and stairs need a foundation or floor, and floors need a wall below or a neighbouring floor. Destroying support collapses what's on it.
- Upgrade to stone (30-50 stone) for 3-4x HP. Melee does only 20% damage to stone, so bring a ram.

## Horses and the car
- **Wild horses** wander each half of the map. Craft a **saddle** (it's in your team colour), walk up to one and press E to saddle it and get on. It rides like a Minecraft horse: it goes where you look, Shift gallops, Space jumps. Your view sits high up so you can see ahead.
- Horses can be killed: they drop **horse meat** (eat it to heal fully) and their saddle.
- (The wooden car is switched off for now.)

## Combat & raid balance
| | Wood HP | Stone HP |
|---|---|---|
| Wall | 400 | 1500 |
| Doorway | 350 | 1200 |
| Foundation | 500 | 1800 |

- **Ram**: a hand-held log. Hold LMB for 1.5 s next to an enemy piece: **wooden pieces and chests break instantly**, **stone pieces are knocked back down to full-HP wood**. 3 hits per ram. You move 25% slower while holding it.
- Player damage: rock 12, hatchet/pickaxe 14, spear 35 (stab) / 60 (thrown), arrow up to 50 at full draw, crossbow 55. **Headshots do x1.5** for everything.
- Weapons do double damage to buildings compared to before.
- You can't shoot through the glass wall by standing right up against it.
- The bow shoots slower arrows with more drop (55 m/s, normal gravity), like the original bow. It's held like Rust's hunting bow: left arm in from the lower left, and when drawn the arrow lines up with the crosshair.
- Hitboxes (radius 0.52 m) wrap the alien, and there's hit assist like most games: a melee swing counts if it passes within 0.35 m of a player, arrows/spears within 0.15 m (both adjustable in GAME SETTINGS, as are the hitbox size, bow speed and arrow drop).
- Melee lands on the swing's impact frame. The rock is swung two-handed: a hit bounces your hands back up with a short hit-stop, a miss follows through down.
- Other players are rigged and animated Mixamo-style: idle breathing, walk / run / sprint forwards, backwards and strafing, crouch walk, jump tuck and landing dip, swings with one or two hands, carrying the ball, falling over on death. Your own first-person hands bob in a figure-8, drop into a sprint pose with the free hand pumping, lag on jumps and dip on landings.
- Hits show blood where they land, a hit marker (orange on headshots, red on kills), a damage number, and a bit of screen shake.

## Project layout
- `Assets/Scripts/`: all game code (namespace `RockGame`). `Config.cs` holds every tuning number.
- `Assets/Scripts/Editor/ProjectSetup.cs`: **Rock Game > Rebuild Scene, Prefabs and Settings** regenerates the scene, network prefabs and materials.
- All art is procedural low-poly stand-ins built at runtime (`Art.cs`, `MapBuilder.cs`, `Airdrop.cs`, `Vehicle.cs`), except the player model.
- `Vehicle.cs`: horses and the car (a networked prefab owned by whoever drives it). `VoiceChat.cs`: proximity voice chat and the sound/mic settings. `PlayerNet.Extras.cs`: crossbow, fort tower, riding, dev settings and voice RPCs.
- Player model: PSX-style grey alien "Low-spec Reticulans" by surt, CC0 ([OpenGameArt](https://opengameart.org/content/low-spec-reticulans)), in `Assets/Game/Resources/Alien/`. `AlienRigged.fbx` is the same mesh skinned to a humanoid skeleton (Hips, Spine, Chest, Neck, Head, Left/Right UpperArm/LowerArm/Hand/UpperLeg/LowerLeg/Foot) and animated procedurally in `BodyAnimator.cs`. It is tinted in the team colour.
- `AutoTest.cs`: headless end-to-end test (`-autotest ball` or `-autotest sd` on a host + client build; `-autotest shots` takes screenshots).
- Sound effects are CC0 recordings by Kenney (www.kenney.nl) in `Assets/Game/Resources/Sfx` (several variations each, picked at random); anything missing falls back to sounds synthesised in `Fx.cs`. Item icons are rendered from the 3D models at startup (`ItemIcons.cs`); first-person hands and animations are in `ViewModel.cs`.

Networking: movement is owner-authoritative (NetworkTransform in Owner mode). Resources, crafting, building, damage, the ball and its socket, inventories, chests, airdrops, dropped items and match flow are server-authoritative. Melee, arrow, thrown-spear and C4 hits use client-side hit detection that the server validates; the death wand is resolved on the server.
