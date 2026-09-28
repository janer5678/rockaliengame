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
- **Fast timers**: 10s ball drop and a 90s match instead of 3 min and 7 min.

Command line: `-host`, `-client <ip>`, `-port <n>`, `-solo`, `-fast`.

## Rules
- You start in your base (BLUE = host, RED = client) with a rock, and respawn there 5s after dying.
- **3 minutes** after both players join, the **ball** drops in the middle. A **7:00** timer starts, so a full game lasts **10 minutes**.
- Pick the ball up (E while looking at it) and **throw it with LMB** where you're looking. You carry it at full speed, but your hands are full. A beacon of light shoots into the sky when it rests in a base.
- When the timer hits 0, whoever has the ball resting in their base **wins**.
- If the ball is in no base, both players are teleported to the **sudden death arena**. It's rocks only, and the first kill wins.
- When you die you drop **everything** in a loot bag. It's take-only (nothing can be put back in) and disappears after 5 minutes or once it's empty.

## Game settings
**GAME SETTINGS** on the main menu lists every stat: timings, player HP and speeds, damage and gather rate for every tool, node HP, building costs and HP, crafting costs and more. Values are saved on your PC. When you host, your values are sent to your opponent and used for the whole match.

## Controls
| Key | Action |
|---|---|
| WASD / Shift / Space / Ctrl or C | move / sprint / jump / crouch |
| Mouse / LMB | look / attack, gather, place |
| Hold LMB (bow) | draw, release to fire (RMB cancels) |
| Spear | LMB stab · **hold RMB** to wind up, then **LMB** to throw |
| Hold LMB (ram) | wind up and slam the enemy piece you're looking at |
| RMB (berries) | eat (+15 HP) |
| 1-7, mouse wheel | hotbar slot |
| TAB | inventory (21 slots + 7 hotbar) and crafting |
| E | use whatever you're looking at: ball, door, chest, loot bag, berry bush, dropped spear, a spear stuck in someone (or in you) |
| Building plan | RMB next piece · R rotate stairs · F upgrade the piece you look at to stone |
| F1 / Esc | toggle help / pause |

## Inventory
- Wood, stone, arrows and berries are real items that take up slots. Tools and weapons go to the hotbar first.
- Drag to move or swap, **right-drag** to split a stack in half, **shift-click** to quick-move (between chest/bag and inventory, or hotbar and inventory).
- **Storage chest**: craft it, hold it and click to place it anywhere inside your base. Press E on it to open it next to your inventory (14 slots). Enemies who break in can loot it, and breaking it spills its contents into a bag.

## Crafting (anywhere)
| Item | Cost |
|---|---|
| Building Plan | 10 wood |
| Stone Hatchet (fast wood) | 60 wood, 20 stone |
| Stone Pickaxe (fast stone) | 60 wood, 20 stone |
| Spear | 150 wood, 25 stone |
| Bow | 200 wood, 30 stone |
| Arrows x10 | 30 wood, 10 stone |
| Battering Ram (3 hits) | 250 wood, 100 stone |
| Storage Chest | 100 wood |
| Wooden Barrier | 40 wood |

## Gathering
- Trees have an orange **X** and rocks a sparkling **star**. Hitting it gives double resources, and it jumps to a new spot facing you.
- **Berry bushes** around the map: press E to pick berries, hold them and press RMB to eat.

## Spears
- Thrown spears fly in an arc and stay in the world. Look at one and press **E** to pick it up.
- A spear that hits a player (up to 60 damage at full wind-up) sticks in them. Anyone can press **E** on them to pull it out and keep it, including the victim. Spears stuck in you go into your loot bag if you die.

## Building
- You can only build inside your own base (the tinted square with the grid). Pieces snap to a 3 m grid.
- Pieces: Foundation (30 wood), Wall (30), Doorway with a lockable door (40), Floor (25), Stairs (40).
- **Wooden barriers** (250 HP) are small free-standing walls you can place anywhere except the enemy base.
- Rust-style support: walls and stairs need a foundation or floor, and floors need a wall below or a neighbouring floor. Destroying support collapses what's on it.
- Upgrade to stone (60-100 stone) for 3-4x HP. Melee does only 20% damage to stone, so bring a ram.

## Combat & raid balance
| | Wood HP | Stone HP |
|---|---|---|
| Wall | 400 | 1500 |
| Doorway | 350 | 1200 |
| Foundation | 500 | 1800 |

- **Ram**: a hand-held log. Hold LMB for 1.5 s next to an enemy piece: **wooden pieces and chests break instantly**, **stone pieces are knocked back down to full-HP wood**. 3 hits per ram. You move 25% slower while holding it.
- Player damage: fists 6, rock 12, hatchet/pickaxe 14, spear 35 (stab) / 60 (thrown), arrow up to 50 at full draw. **Headshots do x2** for everything.
- Hits show blood where they land, a hit marker (orange on headshots, red on kills), a damage number, and a bit of screen shake.

## Project layout
- `Assets/Scripts/`: all game code (namespace `RockGame`). `Config.cs` holds every tuning number.
- `Assets/Scripts/Editor/ProjectSetup.cs`: **Rock Game > Rebuild Scene, Prefabs and Settings** regenerates the scene, network prefabs and materials.
- All art is procedural low-poly stand-ins built at runtime (`Art.cs`, `MapBuilder.cs`), except the player model.
- Player model: PSX-style grey alien "Low-spec Reticulans" by surt, CC0 ([OpenGameArt](https://opengameart.org/content/low-spec-reticulans)), in `Assets/Game/Resources/Alien/`. It is tinted in the team colour.
- `AutoTest.cs`: headless end-to-end test (`-autotest ball` or `-autotest sd` on a host + client build; `-autotest shots` takes screenshots).
- Sounds are synthesised in code (`Fx.cs`); item icons are rendered from the 3D models at startup (`ItemIcons.cs`); first-person hands and animations are in `ViewModel.cs`.

Networking: movement is owner-authoritative (NetworkTransform in Owner mode). Resources, crafting, building, damage, the ball, inventories, chests, bags, dropped spears and match flow are server-authoritative. Melee, arrow and thrown-spear hits use client-side hit detection that the server validates.
