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
- **Game mode** - separate ways to play; they don't mix:
  - *Classic*: the original game.
  - *Tutorial* (**start here if you're new**): Primitive's rules with the clock stopped, always on the **small Plains map with normal materials** (the menu's Map / Size / Materials rows are locked to that while Tutorial is picked, and a `-map` / `-big` / `-wood` on the command line is ignored). A small panel on the left gives one simple step at a time - a title, a sentence or two and a goal - and you have to actually **do** each one to move on (there's no skipping): look at your machine, walk, run and jump, slide, chop a tree, hit the X, get 100 wood, open your bag, craft a hatchet and chop with it, go home, craft the building plan, place a foundation, a wall (the building wheel) and a doorway, get stone, craft a spear, throw it and pick it up, craft a ram, pick a berry and eat it, then **the glass wall**: walk up to it, it drops, run to the ball, grab it, put it in your machine and guard it (enemies can steal it). A pulsing marker points at what the step is about, and if a step takes over 40 s a one-line hint shows up. While the inventory is open the goal shows along the bottom.
    - **Multiplayer**: friends can join a tutorial any time (up to the players picked) - the match starts as soon as the host is in, and anyone who joins later is dropped straight into their base. Everyone goes through the steps at their own pace. The glass wall drops **once every player has walked up to it** (the others see "waiting for N more players"), or 90 s after the first one got there so nobody's stuck waiting for someone who wandered off; nobody wins by the others leaving. Code: `Tutorial.cs` (the steps, the panel, and `ServerTick` for the server side), `PlayerNet.TutAtWall` / `TutorialRpc`. `-autotest modes -host -solo -rules tutorial` checks it alone; the same without `-solo` plus a second copy with `-autotest modes -client 127.0.0.1 -rules tutorial` checks two players.
  - *Arsenal*: normal prices (except the crossbow, 350 wood instead of 500), plus **POWER ITEMS** (a POWER category in the crafting screen, see below).
  - *Auto Wood*: Arsenal, but **wood piles up at your base by itself**: a **wood machine** stands on the bedrock to the right of the alien machine (as you look at it from your spawn), sawing wood out of nothing and pushing it down its chute onto a growing pile in front of it - 5 a second. Walk up and press E to take the lot: the pile keeps stacking past 1000 and splits into 1000-stacks as it goes into your inventory. **Wood Gen upgrade** (power item, Auto Wood only), two levels: level 1 for 1000 wood (12 a second), level 2 for 3000 (25 a second) - all in CHANGE VALUES > Auto Wood. The wood machine changes in front of you with each one: a plank hopper with one saw, then iron bands, a bigger saw, a smoking chimney and a lamp, then a riveted steel housing in your team colour with twin saws, two chimneys, glowing gauges and a light on top (and lit pips on its front for the level).
  - *Primitive*: the classic game, but the only things you can craft are the hatchet, spear, building plan and battering ram.
  - *Building Primitive*: Builder's rules (below), but only those four primitive items and no power items.
  - *DNA*: the classic game, but **trees and rocks give DNA** (a glowing double helix) instead of wood and stone - rocks give 1.5x as much per hit, and there are fewer than half as many of them. **Everything is bought with DNA** at the same ratios: wood prices are DNA 1:1, every stone becomes 1.5 DNA (crafting, building, upgrading, refunds; airdrop wood/stone comes as DNA). Every base has a **gambling machine** on the bedrock to the left of the alien machine: a slot machine with a lever, three reels and a payout hatch. E opens it like a chest with 3 slots that only take DNA (your bet) and a big **GAMBLE** button under them. Press it and the menu closes: the lever drops and the reels spin on the machine (everyone sees it), landing one by one. 50/50 (Gamble Win Chance): three DNA = confetti, a jingle, JACKPOT on your screen and **double your bet** spat out of the hatch; anything else = the screen goes red with X's, a loud buzzer, the cabinet shakes, a voice says "LOSER" and a bouncing **YOU LOST** fills your screen. The numbers are in CHANGE VALUES > DNA mode. (The "LOSER" voice is `Assets/Game/Resources/Gamble/loser.wav`, made with Windows' speech synthesiser.)
  - *Builder*: **no bases, no machines**. Arsenal's items and prices, but most crafts take a few seconds: small timers stack up in the top right: the top one is being made (its bar runs down), and anything else you buy meanwhile waits under it with a full bar, moving up and starting when its turn comes (like Rust - one thing at a time, paid for up front). The building plan and Fortify All Walls are instant; you can **craft, build and put chests anywhere**; pieces **lock on to each other** Fortnite style (walls hang off the walls they touch, floors and stairs hold on to walls around them, ground-level walls reach down into the ground). **The ball** can't be thrown: anyone can pick it up, and **LMB (or E) puts it down** right in front of you - a block in your team's colour grows up under it and it's your team's ball. The ball always has a flag pointing at the sky (white when loose, the colour of whoever has it); set **Builder Flag Always Up** to Off in CHANGE VALUES to go back to a flag that only grows out while the ball is planted. When anyone picks the ball up, the block sinks away again. Whoever's ball it is (planted, not carried) when time runs out wins; otherwise it's sudden death.
  - *FUN MODE*: no building phase - the wall is down and the ball is in from the start - and a 10 minute match. Everybody gets the **same** random item straight away and then every 45 s (any item in the game, the unused ones too). Free items land on the right of your hotbar, like wood does (then the inventory). No airdrops in the Fun modes.
  - *FUN RANDOM MODE*: the same, but only airdrop items (the unused ones too) and everyone gets a **different** one.
  - *RANDOM FUN MODE LIMITED*: Fun Random, but you can only craft the hatchet, spear, building plan and battering ram.
  - In both Fun modes the **Game length** row changes to the match length and the free item interval (both - / +).
- **Game length**: - / + for the time behind the glass wall (building, 30 s steps) and the time with the ball (1 min steps). Saved on this PC; the host's are used.
- While waiting for everyone to join, players brawl with rocks in the stadium; when the lobby is full everyone is sent to their base and the match starts.
- **Graphics**: *Normal*, *PSX* or *AI PSX TEST*. **Normal is untouched** by everything below. PSX swaps the looks for the PSX asset pack: only the looks change (the same colliders, sizes and rules), and it switches live both ways. **The host's graphics are used by everyone in the match** (a client's own choice comes back when they leave; Settings > Display can't change it during a match). Normal is our own look, where new features get made first. On PSX trees the weak spot X sits right on the trunk you see (it's placed by a ray against the PSX trunk's own triangles), and aiming at it counts.
  - **Items** (held, in first person, dropped, thrown) - each one is held by the same spot the Normal one is (the grip lands where the hand is): rock, stone, spear, bow, crossbow, arrow (also in flight), hatchet, tree cracker (a double axe), sword, C4, death wand, staff of the giant, horse meat, revolver, waterpipe shotgun, rocket launcher, storage chest and high external wall. Items with no PSX model (pickaxe, building plan, portal gun, chainsaw...) keep their own look.
  - **The world**: dark PSX grass ground (Highlands: grass and cobblestone), concrete bedrock and map walls, the alien machine is the PSX alien creature (the ball socket and its arch stay where they are), the distant mountains are the big PSX terrain rocks, minable stone nodes are the six PSX rocks, berry bushes are the PSX bush, horses are the PS1 horse (its ripped texture came out blank, so it's a plain chestnut coat), chests are the cardboard box, the high external wall is the PSX log palisade, doors are the PSX wooden door.
  - **Building pieces** wear the PSX textures: warm brown planks (wood, close to the Normal wood), bricks (stone), rusty corrugated sheets (sheet metal) and diamond plate (refined).
  - **Trees are twice the size** and their trunk collider wraps the PSX trunk you see, so you hit what you see; the weak spot X is **printed onto the bark**: a decal made from the trunk's own triangles around the spot, so it wraps round the trunk instead of sticking out past it, sized to the trunk.
  - **First person**: the alien's own forearms and clawed hands (cut from the player model by `Tools/psx_convert.py`), tinted in your team colour like your body. (The pack's first-person arms are human, so they aren't used.) **Dead players** are a skeleton on the ground (the PSX dead model). **Hits** leave PSX blood splats on the ground for 30 s.
  - Item icons always show the Normal look.
  - The models are made from the pack by `Tools/psx_convert.py` (Blender, headless): each one is joined, turned to the game's conventions, centred, its textures shrunk to at most 256 px, and exported to `Assets/Game/Resources/PsxModels/` with `psx_manifest.txt` (each model's textures by material slot). The texture packs are 3x3 atlases, cut into `tex_<pack>_<row><col>.png` tiles. `PsxModels.cs` swaps them in, fitted into the space the Normal look takes up. `-autotest psxmodels -host -solo` photographs every item next to its Normal look, the world, building pieces and first person, then checks Normal comes back exactly.
  - *AI PSX TEST* is a separate test mode (Normal and PSX are untouched by it): using the PSX trees as the reference, the whole wild map is redone in that style - the PSX trees, grass tufts made like their foliage cards, and every other model re-skinned with chunky 32x32 pixel textures generated in the game (grass, dirt, rock, bark, planks, brick, snow, sand, fur...) on a PS1-style shader (`Assets/Game/Resources/AiPsx/AiPsx.shader`: vertices snap to a coarse grid so they wobble, textures warp because they're mapped without perspective correction, lighting per vertex, 15-bit colour with dithering), rendered at half resolution with blocky upscaling, and distance fog. Code in `AiPsxArt.cs`. `-autotest aipsx -aipsx` takes pictures of it and checks Normal and PSX come back exactly as they were.
- **Testing** (on/off buttons): **Alien outlines** (any mode, **on by default**) - enemies get a faint glow in their team colour so they're easier to spot, with **Glow strength** and **Glow thickness** sliders (also in CHANGE VALUES > Test; the host's setting is used). **Solo test** - the match starts without an opponent, so you can try everything alone. **Fast timers** - 10s wall/ball drop and a 90s match instead of 5 min behind the wall and then 15 min.
- **Map**: *Plains* (flat), *Highlands* (the wild map: random hilly terrain with minable rocks and watch towers, new every match), or one of the five theme maps: *Beach* (palm island in the middle ringed by shallow water - wading is slow - with channels down the sides; craft a **Boat** for 1500 wood, put it on open water and E to drive it round), *Canyon* (red desert, tall mesas split the map into lanes, lots of stone), *Frostlake* (snowy hills round a frozen lake; the ice is slippery), *Volcano* (black ash, a broken ring of lava round the middle that burns you, a volcano smoking on the horizon), *Ruins* (broken walls and pillars for cover, extra stone). Each in **Small**, **Medium**, **Large** (1.5x) or **Huge** (2x) (the bigger maps get more trees, rocks, bushes, horses and towers). Picked by the host; the client builds the same map from the synced seed.
- **Materials**: *Normal* or *Wood mode* (no stone anywhere, every rock is a tree, everything costs wood only, no pickaxe, no stone upgrades).
- **MODE OPTIONS** (button on the main menu):
  - **Airdrops per match** (0-20): evenly spaced over the time after the glass wall drops - 1 comes half way through (7:30 after the wall drops), 2 at a third and two thirds, and so on. None come while the wall is up.
  - **Where**: *Anywhere* (one at a random spot), *One per side* (every side gets its own) or *Middle of the map* (always right by the centre).
  - **Airdrop items**: tick which items airdrops can have: C4, Death Wand, Portal Gun, Rocket Launcher, Tree Camo, Invisibility Potion, Jetpack, Wallhack Glasses, Fake Bomb Bush.
  - **Respawn**: *Normal* or *With an airdrop item* (every time you respawn you get a random one of the picked airdrop items).

Command line: `-host`, `-client <ip>`, `-port <n>`, `-solo`, `-fast`, `-map plains|highlands`, `-small`, `-big` (medium), `-wood`, `-normal`, `-sides`, `-anywhere`, `-seed <n>`, `-mode 1v1|2v2|3v3|4v4|ffa3|ffa4|2v2v2|2v2v2v2`, `-large`, `-huge`, `-rules tutorial|classic|arsenal|autowood|builder|fun|funrandom|funrandomlimited|primitive|buildingprimitive|dna`, `-map beach|canyon|frostlake|volcano|ruins`.

### Power items (Arsenal, Auto Wood and Builder)
Bought with wood from the **POWER** category of the crafting screen (in your base; anywhere in Builder). Fortify and Wood Gen show their next step's price:

| Item | Wood | What it does |
|---|---|---|
| Sword | 500 | A slow, heavy swing (its wind-up and swing time are **Sword Swing Time** in CHANGE VALUES): 150 to the head, 95 to the body. |
| Waterpipe Shotgun | 2000 | Comes empty - buy shells. Like Rust's waterpipe: one shell at a time (loads the next by itself, or R). Fires 10 pellets that each hit on their own: within a metre they all land for **200**; further out the spread and falloff take most of it. Pellets in the head do x1.5 (a helmet stops them). |
| Shotgun Shell | 250 each | |
| Revolver | 2500 | Comes empty - buy bullets. Hitscan, holds 3 rounds: 30 to the body, 50 to the head. R reloads (from your bullets), and it reloads by itself when empty. |
| Revolver Bullet | 200 each | |
| C4 | 2500 | The same C4 as the airdrop one (thrown, wrecks the building pieces around it - but see fortified pieces below). |
| Headshot Helmet | 800 | The alien helmet: put it on and the next headshot does no damage and breaks it. |
| Fortify All Walls | 1000, then 2000, then 2500 | Goes up a step **every time your team buys it**: every piece your team has placed - **and every piece you build from then on** - is **stone** (2 ram hits), then **sheet metal** (3 ram hits, 2x stone's HP, rusty corrugated sheets with bolts), then **refined** (4 ram hits, 3x stone's HP, dark plate with brass trim). Melee barely scratches sheet metal and refined. Each ram hit knocks a piece down one step. |

**Explosives against fortified pieces**: wood and stone go as usual. **C4** on sheet metal only blows out that layer: the piece it's stuck to and the ones right next to it on the same side (not the walls behind it, and not floors under a wall); on refined it only breaks the one piece it's stuck to. **Rockets** do 45% to sheet metal and 20% to refined (Metal / Refined Rocket Mul). An **airstrike** flattens wood and stone and knocks sheet metal and refined down a step.

While you hold the revolver or the shotgun, the rounds loaded and your spare ammo show big in the top left. (The pistol isn't sold any more; it's still in the Fun mode item pool.)

All these numbers are in CHANGE VALUES under "Arsenal and Builder" (and "Fun modes" for the item interval and Fun match length).

### Removing the theme maps
Everything for the five theme maps is in `Assets/Scripts/ThemeMaps/` (terrain, props, palms, water/ice/lava, the boat). To remove them: delete that folder, then delete every line or block marked `// THEME MAPS` (search the scripts for it: Config, Bootstrap, MapBuilder, NetGame, ResourceNode, Vehicle, PlayerNet, PlayerController, Hud.Menus, AutoTest.Modes (the maps test), and the second line of the `MapKind` enum). The `Boat` item can stay in the `Item` enum (items are saved by number).

## Pause menu (Esc)
- **Resume**, **Settings**, **Controls**, **Dev settings**, **Suicide** (click twice: you die, drop everything as usual and respawn; once every 30 s - also `/kill` in the chat), **Leave game**. Esc goes back a page.
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
- Pick the ball up (E) and **throw it with LMB**. When it gets close to a machine's socket it snaps in. While you carry it, it fills the bottom half of your screen. A beam of light shoots up from the top of the ball once it's sat still for 3 seconds (not while it's carried or rolling). Enemies can take it back out with E.
- When the timer hits 0, whoever has the ball **in their machine's socket wins**. Lying somewhere in your base doesn't count.
- The last 10 seconds before the end count down huge in the middle of the screen with a pulsing red edge and ticks.
- If the ball is in no socket, both players are teleported to the **sudden death stadium**: a huge round arena with tiered stands full of cheering alien spectators, floodlights and jumbotrons. Everyone is healed, inventories are emptied and all armour and helmets are taken off, then a big 5-4-3-2-1-FIGHT countdown. Rocks only, first kill wins.
- There is no rock item: whenever your selected hotbar slot is empty, you're holding your rock.
- When you die **all your items burst out of your body** onto the ground, including armour and a helmet you were wearing. Anyone can pick them up with E. Items on the ground disappear after 5 minutes.
- Respawning: while the glass wall is up you always come back on your bedrock. After that you choose: **respawn in base** or **respawn in the wild** (a truly random spot anywhere out in the wild - any side of the map, never in a base, the middle, water or lava).
- **Standing in your own base heals you** slowly (2 HP a second; Base Regen in CHANGE VALUES). Not in sudden death, and Builder has no bases.
- **Ball buff**: while your team's ball is in your base (in your machine, or lying in your base; Builder: planted for your team) everything you gather gives 25% more (Ball Gather Mul). It shows bottom left, above the armour.

## Airdrops
10 seconds before each airdrop lands, a banner says **AIRDROP DROPPING IN 10 SECONDS** and a countdown shows top left. Once the wall is down, a giant alien ship comes down from very high up and **beams an airdrop crate** to a random spot (never close to a base; follow the purple beam; a big "AIRDROP INCOMING" shows in the middle of the screen). How many come per match and which items they can have are set in **MODE OPTIONS**; a crate nobody emptied stays put. Each crate holds one random item from the picked ones. The items in the game (only the first nine can be picked for now; the rest are unused):

| Item | What it does |
|---|---|
| C4 | Thrown. After 3 s it destroys every building piece, high external wall and chest within 5 m - yours too and hurts players nearby; right on top of it you die. |
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
| Rocket Launcher | One rocket. Wrecks buildings in a small radius (yours too) and hurts players; a **direct hit on a player kills them**, and the blast hurts a lot more (140, falling off with distance). |
| Fake Bomb Bush | Thrown: looks exactly like a berry bush. Whoever picks it blows up. |
| Tree Camo | While it's in your hand you're a tree to everyone, and your camera pulls back to third person so you can see it. |
| Ender Pearl | Thrown: wherever it lands, you teleport there (5 damage). Pickable in MODE OPTIONS. |
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
| Sprint (or run), then Ctrl / C | **slide** - **any way you're moving**: W, A, S, D or a mix (backwards too); (like Apex / Titanfall; pressed in the air it starts when you land): you slide off where you look with the speed you had (at least sprint speed) plus a **boost - but only once every 1.5 s**, so spamming slide can't build up speed. Friction slows you on the flat (about a second of slide); **slopes pull you down them** and slow you going up - hard (Slide Uphill Mul); crouching while running down a slope starts a slide too. It starts **the moment you press crouch** while sprinting or running (no crouch first). You stick to the ground going downhill, steer a little, and can jump out of it keeping your speed. Nothing goes past **Slide Max Speed** (11 m/s). Tuning in CHANGE VALUES: Slide Slipperiness (0 = grippy, 10 = ice), Slide Boost, Slide Boost Cooldown, Slide Max Speed, Slide Slope Accel, Slide Uphill Mul, Slide Min Speed, Slide Steer |
| Mouse / LMB | look / attack, gather, place |
| Hold LMB (bow) | draw, release to fire (RMB cancels). It fires the moment you let go, but a quick shot is weak and drops short (6 damage, 30% speed); the longer you hold, the faster and harder it flies, up to 50 at a full draw (the damage builds slowly at first and fastest near the end) |
| Spear | LMB stab · **hold RMB** to wind up, then **LMB** to throw |
| Hold LMB (ram) | wind up and slam the enemy piece you're looking at |
| RMB (berries) | eat (+15 HP) |
| 1-7, mouse wheel | hotbar slot |
| TAB | inventory (21 slots + 7 hotbar) and the CRAFTING button |
| E | use whatever you're looking at: your machine (put the ball in), ball, door, chest, airdrop, berry bush, dropped items and arrows, horse or car (E again to get off), a spear stuck in someone (or in you) |
| Building plan | **hold RMB: building wheel** (Rust style, light blue, with a picture of each piece; clockwise from the top: Foundation, Ceiling, Wall (right), Window, Demolish (trash can, bottom), Stairs, Doorway, Upgrade) · R rotate stairs · F upgrade to stone · X demolish your own piece (half the wood back) |
| Crossbow | LMB fire · hold RMB aim · reloads itself (uses an arrow) |
| Helmet / armour / potion | LMB (or RMB) to put on / drink |
| C4 / fort tower | LMB throws it |
| V | push to talk (proximity voice chat) |
| Esc | pause (the controls list is in the pause menu, and every key above can be rebound there) |
| Enter | **text chat**: type and Enter to send to everyone (in your team colour), Esc cancels. `/kill` kills yourself (not again within 30 s, same as the pause menu's Suicide), `/help` lists commands |

## Inventory
- Wood, stone, arrows and berries are real items that take up slots. Tools and weapons go to the hotbar first.
- Materials stack onto what you already have; otherwise they go on the hotbar from **slot 7 backwards** (never into the empty slot you're holding your rock in), then the main inventory.
- Everything that goes into your inventory pops up in the bottom right ("+30 Wood"); materials spent on building and crafting show up there in red ("-15 Wood").
- Chests and high external walls can go right up against your walls and the machine: if the exact spot is a little too tight they slide into the nearest free spot (up to 0.7 m).
- A chest you open shows up to the **right** of your inventory.
- You always hold something: the hotbar only scrolls through slots that have an item. The rock stays on your hotbar - it can't be dropped or put in a chest.
- **Drag an item outside the inventory** to throw it on the ground; look at it and press E to pick it up.
- Drag to move or swap, **right-drag** to split a stack in half, **shift-click** to quick-move (between chest/bag and inventory, or hotbar and inventory).
- **Shift-clicking things into a chest sorts it**: stacks of the same thing are topped up and everything is laid out in order - wood and stone, ammo, weapons, tools, things you place, armour, food, then the rest. (Dragging things in by hand leaves them where you put them.)
- **Storage chest**: craft it, hold it and click to place it anywhere inside your base. Press E on it to open it next to your inventory (14 slots). Enemies who break in can loot it, and breaking it spills its contents into a bag.

## Crafting (anywhere in your base, TAB)
TAB shows your inventory with a big **CRAFTING** button on its right (a chest you're looting shows there instead). The button opens a Rust-style crafting screen:
- **◄ INVENTORY** tab at the top goes back (so does Esc; TAB closes everything).
- **Categories** down the left with how many items each has: COMMON (everything), CONSTRUCTION (chest, high external wall, fortify, wood gen), ITEMS (armour, saddle, boat, helmet), TOOLS (hatchet, pickaxe, chainsaw, building plan, ram), WEAPONS, AMMO, and POWER in the modes that have power items.
- An **icon grid** (greyed out when you can't make it right now; hover for the name; a gold dot marks power items) with a **Search** box under it (searches every category; game keys are off while you type, Enter or Esc leaves the box), and the **crafting queue** under that (Builder's timers).
- The picked item's **details**: name, a badge for where it can be made (green *CRAFT ANYWHERE* / *IN YOUR BASE*, red *CRAFT IN YOUR BASE* when you're out of it), a description, the craft time (Builder) and how many one craft makes, a cost table (AMOUNT / ITEM TYPE / TOTAL / HAVE - HAVE goes red when you're short), why you can't craft it if you can't, then **- [n] + ►|** (►| = as many as you can afford; the mouse wheel works too) and **CRAFT**, which crafts that many.
- `-autotest craftui -host -solo -fast -rules <mode> -shotdir DIR` photographs it (inventory, categories, search, power items, out of base) and checks crafting several at once.

| Item | Cost |
|---|---|
| Stone Hatchet (fast wood) | 50 wood |
| Stone Pickaxe (fast stone) | 30 wood, 10 stone |
| Spear | 100 wood |
| Building Plan | 5 wood |
| Storage Chest | 50 wood |
| Bow | 100 wood, 15 stone |
| Arrows (5) | 50 wood |
| Crossbow (55 damage, faster and flatter than the bow) | 500 wood |
| Armour (goes straight on: 50 extra health) | 250 wood |
| Chainsaw (67 uses) | 500 wood |
| Battering Ram (1 hit) | 125 wood, 50 stone |
| High External Wall | 40 wood |
| Saddle (ride a wild horse; in your team's colour) | 750 wood |

In wood mode the stone part is added to the wood cost and there's no pickaxe. In DNA mode everything costs DNA: the wood part 1:1 plus 1.5 DNA for every stone.

## Gathering
- Normal graphics: the grass is covered in little tufts of grass (crossed cards of pointed blades in three greens; not on bases or the ball drop zone, and not on steep rock in the Highlands).
- Felling a whole tree gives a +100 wood bonus ("TIMBER!").
- The first hit on a tree or rock reveals its weak spot: an orange **X** on trees (a chunky pixel-art X on PSX trees, sitting on the bark you see), a sparkling **star** on rocks. Hitting it gives double resources, and it jumps to a new spot facing you. Hitting a tree's X plays a chime; hits in a row on the same tree climb up the scale (C D E G A C).
- On the Highlands map every rock on the hills is a minable stone node.
- **Berry bushes** are loaded with berries: press E to pick the whole bush (it disappears), hold the berries and press RMB to eat (takes 1.5 s, +25 HP). A new bush grows up out of the ground somewhere else in the same half a couple of minutes later. Bushes are fairly rare.
- Arrows that miss stay stuck where they land; press E to pick them back up.

## Spears
- Hold RMB to ready the spear and click LMB to throw (a longer wind-up throws harder). Let go of RMB and it's a normal spear again.
- Thrown spears fly in an arc and stay in the world. Look at one and press **E** to pick it up.
- A spear that hits a player (up to 60 damage at full wind-up) sticks in them. Anyone can press **E** on them to pull it out and keep it, including the victim. Spears stuck in you go into your loot bag if you die.

## Building
- You can only build inside your own base (the tinted square with the grid). Pieces snap to a 3 m grid. There's no delay between placements, and you can place pieces right where you stand (you get lifted on top).
- Pieces: Foundation (15 wood), Wall (15), Doorway with a lockable door (20), Window (15), Floor (12), Stairs (20).
- You don't have to aim at the bottom edge: look along where you want the wall (eye height or above works) and it goes to the farthest spot along your aim where it can actually go (so it lands where you're pointing).
- **High external walls** (500 HP, like Rust's): a free-standing palisade of sharpened logs 4 m wide and about 5.5 m tall. Place them out on the map or inside your own base - never in the enemy base (no end of the wall may poke into it) and never on or right next to the enemy's building pieces (in every mode, Builder too), and not on the bedrock.
- Rust-style support: walls and stairs need a foundation or floor, and floors need a wall below or a neighbouring floor. Destroying support collapses what's on it.
- Floors (ceilings) can be built off the top of a ramp (stairs) in every mode, and the floor ghost looks at every level your aim crosses - so from low on the stairs you already see the ceiling go green where you point.
- Upgrade to stone (30-50 stone) for 3-4x HP. Melee does only 20% damage to stone, so bring a ram.

## Horses and the car
- **Wild horses** wander each half of the map. Craft a **saddle** (it's in your team colour), walk up to one and press E to saddle it and get on. It rides like a Minecraft horse: it goes where you look, Shift gallops, Space jumps. Your view sits high up so you can see ahead.
- **The ball on horseback**: you can get on a horse while carrying the ball, and pick the ball up from the saddle (look at it and press E - E only gets you off when you're not looking at the ball). LMB throws it from the horse; it keeps the horse's speed. (Cars still can't carry the ball.)
- Horses have 60 HP, and a hit on the **head** does double damage (their HP shows when you look at one once it's been hurt), bleed when hit and bolt away from whoever hurt them. They can be killed: they drop **horse meat** (takes 3 s to eat, heals you fully) and their saddle.
- (The wooden car is switched off for now.)

## Combat & raid balance
| | Wood HP | Stone HP |
|---|---|---|
| Wall | 400 | 1500 |
| Doorway | 350 | 1200 |
| Foundation | 500 | 1800 |

- **Ram**: a hand-held log. Hold LMB for 1.5 s next to an enemy piece: **wooden pieces, chests and high external walls break instantly** (in every mode) - and a high external wall takes the ones stacked right behind it with it (up to 5 in a row; not the ones beside it), **stone, metal and refined pieces are knocked down one step at full HP** (refined to metal to stone to wood). 1 hit per ram. You move 25% slower while holding it.
- Player damage: rock 12, hatchet/pickaxe 14, spear 35 (stab) / 60 (thrown), arrow 6 (instant) up to 50 at full draw, crossbow 55. **Headshots do x2** for everything (the sword and guns have their own headshot numbers).
- **Crossbow**: the bolt flies at 58 m/s with a bit more drop than an arrow (Crossbow Speed / Crossbow Gravity). It reloads by itself; the reload is **one crank for all your crossbows**: fire one and swap to another loaded crossbow and that one has to wait out the reload too, and swapping away mid-reload doesn't reset it. (Fixed: a client's crossbow used to reload twice before it would fire.)
- Weapons do double damage to buildings compared to before.
- You can't shoot through the glass wall by standing right up against it, and while it's up **nothing hurts anyone on the other side of it** (hits are checked on the server, so hitboxes poking through the glass don't count).
- The bow shoots slower arrows with more drop (55 m/s, normal gravity), like the original bow. It's held like Rust's hunting bow: left arm in from the lower left, and when drawn the arrow lines up with the crosshair.
- Hitboxes (radius 0.45 m) wrap the alien (headshots are anything above its neck), and there's hit assist like most games: a melee swing counts if it passes within 0.35 m of a player, arrows/spears within 0.15 m (both adjustable in CHANGE VALUES, as are the hitbox size, bow speed and arrow drop).
- Melee lands on the swing's impact frame. The rock is swung two-handed: a hit bounces your hands back up with a short hit-stop, a miss follows through down.
- Other players are rigged (a skinned mesh, so the body really bends) and animated procedurally, and you see what they're doing: idle breathing, walk / run / sprint forwards, backwards and strafing, crouch walk, jump tuck and landing dip, **sliding** (low, leaning back, front leg out), holding each item, **drawing a bow** (bow arm out, string hand to the cheek), **winding up a spear** (cocked over the shoulder), **aiming the crossbow / sniper** (shouldered), **eating / drinking**, **charging the ram**, **running the chainsaw**, **throwing** (overhand), swings with one or two hands, carrying the ball, riding, and falling over on death. What each player is doing is synced (`PlayerNet.Action`), so it shows on every screen. `-autotest rig -host -solo` photographs every pose from the front and the side.
- Hits show blood where they land, a hit marker (orange on headshots, red on kills), a damage number, and a bit of screen shake.

## Project layout
- `Assets/Scripts/`: all game code (namespace `RockGame`). `Config.cs` holds every tuning number.
- `Assets/Scripts/Editor/ProjectSetup.cs`: **Rock Game > Rebuild Scene, Prefabs and Settings** regenerates the scene, network prefabs and materials.
- All art is procedural low-poly stand-ins built at runtime (`Art.cs`, `MapBuilder.cs`, `Airdrop.cs`, `Vehicle.cs`), except the player model.
- `Vehicle.cs`: horses and the car (a networked prefab owned by whoever drives it). `VoiceChat.cs`: proximity voice chat and the sound/mic settings. `PlayerNet.Extras.cs`: crossbow, fort tower, riding, dev settings and voice RPCs.
- Player model: "PS1 Low Poly Grey Alien" (supplied by the user as `ps1low-poly-grey-alien.zip`, a Sketchfab-style download with no licence file; source OBJ + texture kept in `Tools/Alien2/`). `Tools/rig_alien2.py` (run headless: `blender -b --python Tools/rig_alien2.py`, add `-- --render <dir>` for test-pose renders) scales it to 1.8 m, skins it to a humanoid skeleton (Hips, Spine, Chest, Neck, Head, Left/Right UpperArm/LowerArm/Hand/UpperLeg/LowerLeg/Foot) and exports `Assets/Game/Resources/Alien/AlienRigged.fbx` + `Alien2.png`; it is animated procedurally in `BodyAnimator.cs` and tinted in the team colour (body strongly, head lighter). The previous model, "Low-spec Reticulans" by surt, CC0 ([OpenGameArt](https://opengameart.org/content/low-spec-reticulans)), is still there as `Alien.fbx` (fallback if the rig fails to load).
- `AutoTest.cs`: headless end-to-end test (`-autotest ball` or `-autotest sd` on a host + client build; `-autotest shots` takes screenshots; `-autotest psx -host -solo -psx` screenshots the PSX trees, their X and the hit effects, then the same in normal graphics).
- PSX graphics: `PsxArt.cs`; the tree models and their 128x128 textures are in `Assets/Game/Resources/PsxTrees/` (from `tree_pack_1.1`, 36 trees; drawn with the alpha-cutout `PsxCutout.mat`, import settings in `Editor/PsxImport.cs`). Each tree's trunk thickness is measured so the X sits on it, and its bark and leaf colours colour the chips. The tree X chime notes are in `Assets/Game/Resources/TreeHit/` (from the Samples pack, Sound_1 high notes).
- Sound effects are synthesised procedurally at startup in `Fx.cs`. Everything in the world is a 3D sound with no spread and a steep, realistic falloff, so you can tell where it is and how far: arrows, spears and thrown items whoosh while they fly (with doppler as they pass), bow shots, spear throws and impacts carry about 100 m, other players' footsteps and slides and horses' hooves can be heard from where they are (crouch-walking is silent), and explosions and sniper shots carry across the map. Hand-drawn item icons in `Assets/Game/Resources/Icons/<item>.png` (e.g. `hatchet.png`) replace the rendered ones. Item icons are rendered from the 3D models at startup (`ItemIcons.cs`); first-person hands and animations are in `ViewModel.cs`.

Networking: movement is owner-authoritative (NetworkTransform in Owner mode). Resources, crafting, building, damage, the ball and its socket, inventories, chests, airdrops, dropped items and match flow are server-authoritative. Melee, arrow, thrown-spear and C4 hits use client-side hit detection that the server validates; the death wand is resolved on the server.
