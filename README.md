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
- **Fast timers**: 10s ball drop and a 90s match instead of 30s and 5 min.

Command line: `-host`, `-client <ip>`, `-port <n>`, `-solo`, `-fast`.

## Rules
- You start in your base (BLUE = host, RED = client) and respawn there 5s after dying.
- **30 s** after both players join, the **ball** drops in the middle. A **5:00** timer starts.
- Carry the ball (E to pick up) and drop it (E) or throw it (G) inside your base. A beacon of light shoots into the sky.
- When the timer hits 0, whoever has the ball resting in their base **wins**.
- If the ball is in no base, both players are teleported to the **sudden death arena**. It's rocks only, and the first kill wins.

## Controls
| Key | Action |
|---|---|
| WASD / Shift / Space | move / sprint / jump |
| Mouse / LMB | look / attack, gather, place |
| Hold LMB (bow) | draw, release to fire (RMB cancels) |
| 1-8, mouse wheel | switch item |
| TAB | crafting menu |
| E | pick up/drop the ball, open/close your doors, **hold** next to a ram to push it |
| G | throw the ball |
| Building plan | RMB next piece · R rotate stairs · F upgrade the piece you look at to stone |
| F1 / Esc | toggle help / pause |

## Crafting (the crafting table must be crafted and placed first; everything else needs you next to it)
| Item | Cost |
|---|---|
| Crafting Table | 100 wood |
| Building Plan | 30 wood |
| Stone Hatchet (fast wood) | 100 wood, 50 stone |
| Stone Pickaxe (fast stone) | 100 wood, 50 stone |
| Spear | 150 wood, 25 stone |
| Bow | 200 wood, 30 stone |
| Arrows x10 | 30 wood, 10 stone |
| Battering Ram | 1000 wood, 150 stone |

## Building
- You can only build inside your own base (the tinted square with the grid). Pieces snap to a 3 m grid.
- Pieces: Foundation (50 wood), Wall (50), Doorway with a lockable door (60), Floor (40), Stairs (60).
- Rust-style support: walls and stairs need a foundation or floor, and floors need a wall below or a neighbouring floor. Destroying support collapses what's on it.
- Building has a ~1 s cooldown between placements and pieces grow into place.
- Upgrade to stone (100-150 stone) for 3-4x HP. Melee does only 20% damage to stone, so bring a ram.

## Raid balance
| | Wood HP | Stone HP |
|---|---|---|
| Wall | 400 | 1500 |
| Doorway | 350 | 1200 |
| Foundation | 500 | 1800 |

- **Ram**: 800 HP, hits for 200 every 3 s. That's 2 hits (6 s) on a wood wall and 8 hits (~24 s) on a stone wall. Defenders can kill it with arrows (20), spear (40) or hatchet/pickaxe (30).
- Melee vs a wood wall: a hatchet takes ~25 s. Stone walls are effectively ram-only.
- Player damage: rock 12, hatchet/pickaxe 20, spear 35, arrow up to 50 at full draw. Headshots do x1.5.

## Project layout
- `Assets/Scripts/`: all game code (namespace `RockGame`). `Config.cs` holds every tuning number.
- `Assets/Scripts/Editor/ProjectSetup.cs`: **Rock Game > Rebuild Scene, Prefabs and Settings** regenerates the scene, network prefabs and materials.
- All art is procedural low-poly stand-ins built at runtime (`Art.cs`, `MapBuilder.cs`).
- `AutoTest.cs`: headless end-to-end test (`-autotest ball` or `-autotest sd` on a host + client build).

Networking: movement is owner-authoritative (NetworkTransform in Owner mode). Resources, crafting, building, damage, the ball, rams and match flow are server-authoritative. Melee and arrow hits use client-side hit detection that the server validates.
