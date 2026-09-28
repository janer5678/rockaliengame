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

Command line: `-host`, `-client <ip>`, `-port <n>`, `-solo`, `-fast`, `-map plains|highlands`, `-small`, `-big`, `-wood`, `-seed <n>`.

## Rules
- In the middle of each base is an unbreakable silver **bedrock** (2x2 cells). You spawn on it. It counts as a foundation (walls and stairs can stand on it) but nothing can be placed on it.
- On the back of the bedrock stands your **alien machine**. Press **E** on it to craft. It has a **socket** (the glowing cradle under the arch) where the ball has to sit.
- A **big glass wall** splits the map in half for the first **5 minutes**. When it drops, the **ball** drops in the middle and a **7:00** timer starts (12 minutes in total).
- Pick the ball up (E) and **throw it with LMB**, or press **E at your machine** while carrying it to put it in the socket. A ball thrown or rolled into a socket locks in too. A beacon of light shoots into the sky while it sits in a socket. Enemies can take it back out with E.
- When the timer hits 0, whoever has the ball **in their machine's socket wins**. Lying somewhere in your base doesn't count.
- If the ball is in no socket, both players are teleported to the **sudden death arena**. It's rocks only, and the first kill wins.
- When you die **all your items burst out of your body** onto the ground (you keep your rock). Anyone can pick them up with E. Items on the ground disappear after 5 minutes.
- Respawning: while the glass wall is up you always come back on your bedrock. After that you choose: **respawn in base** or **respawn in the wild** (a random spot in the enemy's half of the map).

## Airdrops
Once the wall is down, a giant alien ship comes down from very high up and **beams an airdrop crate** to a random spot (never close to a base; follow the purple beam, the HUD shows the direction). Only one exists at a time; the next one comes **1 minute after the last one was emptied**. Each crate holds one random OP item:

| Item | What it does |
|---|---|
| C4 | LMB throws it. After a 3s fuse it destroys every enemy building piece, barrier and chest within 5 m (any tier) and hurts players nearby. |
| Death Wand | One shot. Anyone the bolt passes close to, or who is near where it hits, dies instantly. |
| Alien Helmet | LMB puts it on: 100 extra health for your head (headshots hit the helmet first) until it breaks. If you're killed with it still on, it drops with its remaining health and the killer can wear it. |
| 1000 wood or stone | (always wood in wood mode) |
| Invisibility Potion | 30 s of invisibility. Attacking shows you for a moment. |
| Chainsaw | Hold LMB: cuts wood and stone very fast. Breaks after 200 hits. |

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
| E | use whatever you're looking at: your machine (crafting / put the ball in), ball, door, chest, airdrop, berry bush, dropped items and arrows, a spear stuck in someone (or in you) |
| Building plan | RMB next piece · R rotate stairs · F upgrade the piece you look at to stone · **X demolish your own piece** (half the wood back) |
| Helmet / potion | LMB (or RMB) to put on / drink |
| F1 / Esc | toggle help / pause |

## Inventory
- Wood, stone, arrows and berries are real items that take up slots. Tools and weapons go to the hotbar first.
- Everything that goes into your inventory pops up in the bottom right ("+30 Wood").
- You always hold something: the hotbar only scrolls through slots that have an item. The rock stays on your hotbar - it can't be dropped or put in a chest.
- **Drag an item outside the inventory** to throw it on the ground; look at it and press E to pick it up.
- Drag to move or swap, **right-drag** to split a stack in half, **shift-click** to quick-move (between chest/bag and inventory, or hotbar and inventory).
- **Storage chest**: craft it, hold it and click to place it anywhere inside your base. Press E on it to open it next to your inventory (14 slots). Enemies who break in can loot it, and breaking it spills its contents into a bag.

## Crafting (at your alien machine, E)
| Item | Cost |
|---|---|
| Building Plan | 5 wood |
| Stone Hatchet (fast wood) | 30 wood, 10 stone |
| Stone Pickaxe (fast stone) | 30 wood, 10 stone |
| Spear | 75 wood |
| Bow | 100 wood, 15 stone |
| Arrows x10 | 15 wood, 5 stone |
| Battering Ram (3 hits) | 125 wood, 50 stone |
| Storage Chest | 50 wood |
| Wooden Barrier | 20 wood |

In wood mode the stone part is added to the wood cost and there's no pickaxe.

## Gathering
- The first hit on a tree or rock reveals its weak spot: an orange **X** on trees, a sparkling **star** on rocks. Hitting it gives double resources, and it jumps to a new spot facing you.
- On the Highlands map every rock on the hills is a minable stone node.
- **Berry bushes** around the map hold a single berry: press E to pick it, hold it and press RMB to eat.
- Arrows that miss stay stuck where they land; press E to pick them back up.

## Spears
- Thrown spears fly in an arc and stay in the world. Look at one and press **E** to pick it up.
- A spear that hits a player (up to 60 damage at full wind-up) sticks in them. Anyone can press **E** on them to pull it out and keep it, including the victim. Spears stuck in you go into your loot bag if you die.

## Building
- You can only build inside your own base (the tinted square with the grid). Pieces snap to a 3 m grid. There's no delay between placements, and you can place pieces right where you stand (you get lifted on top).
- Pieces: Foundation (15 wood), Wall (15), Doorway with a lockable door (20), Floor (12), Stairs (20).
- **Wooden barriers** (250 HP) are small free-standing walls you can place anywhere except the enemy base.
- Rust-style support: walls and stairs need a foundation or floor, and floors need a wall below or a neighbouring floor. Destroying support collapses what's on it.
- Upgrade to stone (30-50 stone) for 3-4x HP. Melee does only 20% damage to stone, so bring a ram.

## Combat & raid balance
| | Wood HP | Stone HP |
|---|---|---|
| Wall | 400 | 1500 |
| Doorway | 350 | 1200 |
| Foundation | 500 | 1800 |

- **Ram**: a hand-held log. Hold LMB for 1.5 s next to an enemy piece: **wooden pieces and chests break instantly**, **stone pieces are knocked back down to full-HP wood**. 3 hits per ram. You move 25% slower while holding it.
- Player damage: rock 12, hatchet/pickaxe 14, spear 35 (stab) / 60 (thrown), arrow up to 50 at full draw. **Headshots do x2** for everything.
- The bow shoots slower arrows with more drop (55 m/s, normal gravity), like the original bow.
- Hitboxes (radius 0.52 m) wrap the alien, and there's hit assist like most games: a melee swing counts if it passes within 0.35 m of a player, arrows/spears within 0.15 m (both adjustable in GAME SETTINGS, as are the hitbox size, bow speed and arrow drop).
- Melee lands on the swing's impact frame. The rock is swung two-handed: a hit bounces your hands back up with a short hit-stop, a miss follows through down.
- Other players are rigged and animated Mixamo-style: idle breathing, walk / run / sprint forwards, backwards and strafing, crouch walk, jump tuck and landing dip, swings with one or two hands, carrying the ball, falling over on death. Your own first-person hands bob in a figure-8, drop into a sprint pose with the free hand pumping, lag on jumps and dip on landings.
- Hits show blood where they land, a hit marker (orange on headshots, red on kills), a damage number, and a bit of screen shake.

## Project layout
- `Assets/Scripts/`: all game code (namespace `RockGame`). `Config.cs` holds every tuning number.
- `Assets/Scripts/Editor/ProjectSetup.cs`: **Rock Game > Rebuild Scene, Prefabs and Settings** regenerates the scene, network prefabs and materials.
- All art is procedural low-poly stand-ins built at runtime (`Art.cs`, `MapBuilder.cs`, `Airdrop.cs`), except the player model.
- Player model: PSX-style grey alien "Low-spec Reticulans" by surt, CC0 ([OpenGameArt](https://opengameart.org/content/low-spec-reticulans)), in `Assets/Game/Resources/Alien/`. `AlienRigged.fbx` is the same mesh skinned to a humanoid skeleton (Hips, Spine, Chest, Neck, Head, Left/Right UpperArm/LowerArm/Hand/UpperLeg/LowerLeg/Foot) and animated procedurally in `BodyAnimator.cs`. It is tinted in the team colour.
- `AutoTest.cs`: headless end-to-end test (`-autotest ball` or `-autotest sd` on a host + client build; `-autotest shots` takes screenshots).
- Sounds are synthesised in code (`Fx.cs`); item icons are rendered from the 3D models at startup (`ItemIcons.cs`); first-person hands and animations are in `ViewModel.cs`.

Networking: movement is owner-authoritative (NetworkTransform in Owner mode). Resources, crafting, building, damage, the ball and its socket, inventories, chests, airdrops, dropped items and match flow are server-authoritative. Melee, arrow, thrown-spear and C4 hits use client-side hit detection that the server validates; the death wand is resolved on the server.
