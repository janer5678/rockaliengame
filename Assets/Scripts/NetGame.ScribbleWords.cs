using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RockGame
{
    /// <summary>
    /// The scribble notebook's big word list: what people (gamers) actually write - guns, food, "aimbot", "god mode",
    /// "nuke", "free robux", "noob", "rizz", "fanum tax", dragons, Lambos... Each word does something (NetGame.ServerScribble
    /// asks here first, after an item's own name). Words are matched whole (so "elephant" isn't an ant), and when two
    /// match the longest wins ("diamond sword" is a sword, not money; "ice cream" is food, not a freeze).
    /// </summary>
    public partial class NetGame
    {
        struct ScribbleWord
        {
            public string[] Keys;
            public Func<PlayerNet, Vector3, string, string> Act; // (who, where on the ground, the word they wrote) -> what it says; null = it fizzled
        }

        List<ScribbleWord> m_ScribbleWords;

        /// <summary>The word made plain: lower case, letters and numbers only, one space between words.</summary>
        static string ScribbleNorm(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append(' ');
            foreach (char ch in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch == '\'') continue; // (mcdonald's = mcdonalds)
                else if (sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            if (sb[sb.Length - 1] != ' ') sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>Server: one of the gamer words? `said` = what it says (null and true: it fizzled - nobody to hit).</summary>
        bool ServerScribbleWords(PlayerNet p, string word, Vector3 ground, out string said)
        {
            said = null;
            if (m_ScribbleWords == null) m_ScribbleWords = BuildScribbleWords();
            string w = ScribbleNorm(word);
            int best = -1, bestLen = 0;
            for (int i = 0; i < m_ScribbleWords.Count; i++)
            {
                foreach (var key in m_ScribbleWords[i].Keys)
                {
                    if (key.Length <= bestLen) continue;
                    if (w.Contains(" " + key + " ") || w.Contains(" " + key + "s ") || w.Contains(" " + key + "es ")) { best = i; bestLen = key.Length; }
                }
            }
            if (best < 0) return false;
            said = m_ScribbleWords[best].Act(p, ground, word.Trim()) ?? "It fizzled - nobody to hit";
            return true;
        }

        /// <summary>For the tests: how many words the notebook knows.</summary>
        public int ScribbleWordCount
        {
            get
            {
                if (m_ScribbleWords == null) m_ScribbleWords = BuildScribbleWords();
                int n = 0;
                foreach (var e in m_ScribbleWords) n += e.Keys.Length;
                return n;
            }
        }

        // ---------------- what the words do ----------------

        double ScribbleNow => NetworkManager.ServerTime.Time;

        void ScribbleGive(PlayerNet p, Item it, int count = 1)
        {
            var st = Cfg.GiftStack(it);
            int left = p.ServerGive(st.Id, Mathf.Max(count, st.Count), st.Data);
            if (left > 0) ServerDropItem(ItemStack.Of(st.Id, left, st.Data), p.transform.position + p.transform.forward, p.transform.forward, p.EyePos);
        }

        string Gives(PlayerNet p, Item it, int count = 1, string say = null)
        {
            if (it == Item.Chainsaw && Cfg.JonahBruh) { it = Item.BetterHatchet; say = null; } // (Jonah Test Mode Bruh: no chainsaws)
            if (Cfg.Removed(it) || (it == Item.DeathWand && Cfg.JonahBruh)) return null; // (Jonah Test Mode Bruh: the death wand only from airdrops)
            ScribbleGive(p, it, count);
            return say ?? $"{(count > 1 ? count + " x " : "A ")}{Cfg.ItemName(it)}!";
        }

        /// <summary>The nearest enemy alive (within `range`), or null.</summary>
        static PlayerNet ScribbleEnemy(PlayerNet p, float range = 9999f)
        {
            PlayerNet best = null; float bd = range;
            foreach (var o in PlayerNet.All)
            {
                if (o == null || o.Dead.Value || o.Team.Value == p.Team.Value) continue;
                float d = Vector3.Distance(o.transform.position, p.transform.position);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        string Smite(PlayerNet p, string word)
        {
            var e = ScribbleEnemy(p, 150f);
            if (e == null || e.WackySafe) return null;
            var at = e.transform.position;
            Fx.Server(FxKind.WackyBeam, at + Vector3.up * 40f, Vector3.down * 40f);
            Fx.Server(FxKind.ShellBoom, at + Vector3.up, Vector3.up);
            e.ServerDamage(Cfg.ScribbleSmite, p, (byte)Item.Scribble);
            if (!e.Dead.Value) e.KnockUntil.Value = ScribbleNow + 1.5f;
            return $"{word.ToUpperInvariant()}! {e.DisplayName} got struck!";
        }

        string Giant(PlayerNet p, string say) { p.GiantUntil.Value = ScribbleNow + Cfg.GiantTime; return say; }
        string Speedy(PlayerNet p, string say) { p.SpeedUntil.Value = ScribbleNow + Cfg.SpeedJuiceTime; return say; }

        string EnemyDances(PlayerNet p, string fmt)
        {
            var e = ScribbleEnemy(p, 200f);
            if (e == null || e.WackySafe) return null;
            ServerDanceAt(e.transform.position, p);
            return string.Format(fmt, e.DisplayName);
        }

        string EnemySlowed(PlayerNet p, string fmt)
        {
            var e = ScribbleEnemy(p);
            if (e == null || e.WackySafe) return null;
            e.SlowUntil.Value = ScribbleNow + Cfg.SlowTime;
            Fx.Server(FxKind.Goo, e.transform.position, Vector3.up);
            return string.Format(fmt, e.DisplayName);
        }

        string EnemyFlat(PlayerNet p, string fmt)
        {
            var e = ScribbleEnemy(p);
            if (e == null || e.WackySafe) return null;
            e.KnockUntil.Value = ScribbleNow + Cfg.KnockDownTime;
            if (e.CarryingBall) Ball.Instance.ServerDrop(e.transform.position + Vector3.up * 1.2f, Vector3.up * 4f);
            e.ServerDismount();
            Fx.Server(FxKind.Bonk, e.transform.position + Vector3.up, Vector3.up);
            return string.Format(fmt, e.DisplayName);
        }

        string RandomAirdrop(PlayerNet p)
        {
            var pool = new List<Item>();
            var had = BushItemsHad(p); // (never one they've had this game)
            foreach (var i in Cfg.AirdropLoot) if (i != Item.Wood && i != Item.Stone && i != Item.Scribble && i != Item.FakeFlare && !had.Contains(i)) pool.Add(i);
            if (pool.Count == 0) return null;
            var it = Cfg.PickAirdropItem(pool);
            NoteAirdropItem(p, it);
            ScribbleGive(p, it);
            return $"Loot! A {Cfg.ItemName(it)}!";
        }

        string Steal(PlayerNet p)
        {
            var e = ScribbleEnemy(p);
            if (e == null) return null;
            var slots = new List<int>();
            for (int i = 0; i < e.Inv.Count; i++) if (!e.Inv[i].Empty) slots.Add(i);
            if (slots.Count == 0) return $"{e.DisplayName} has nothing to steal!";
            int s = slots[Random.Range(0, slots.Count)];
            var st = e.Inv[s];
            e.Inv[s] = default;
            int left = p.ServerGive(st.Id, st.Count, st.Data);
            if (left > 0) ServerDropItem(ItemStack.Of(st.Id, left, st.Data), p.transform.position + p.transform.forward, p.transform.forward, p.EyePos);
            Fx.Server(FxKind.Swap, e.transform.position + Vector3.up, Vector3.up);
            e.WackyNoteRpc(new FixedString128Bytes($"{p.DisplayName} stole your {Cfg.ItemName(st.Id)}!"));
            return $"You stole {e.DisplayName}'s {Cfg.ItemName(st.Id)}!";
        }

        string Creature(PlayerNet p, Vector3 ground, string word)
        {
            Vehicle.ServerSpawnSlender(ground + Vector3.up * 0.2f, p.Team.Value);
            return $"{word.ToUpperInvariant()} appeared... it hunts your enemies!";
        }

        string Home(PlayerNet p)
        {
            if (p.CarryingBall) Ball.Instance.ServerDrop(p.transform.position + Vector3.up * 1.5f, Vector3.up * 3f); // (not the ball too!)
            p.ServerDismount();
            SpawnPoint(p.Team.Value, false, p.Slot.Value, out var pos, out var yaw);
            Fx.Server(FxKind.Poof, p.transform.position + Vector3.up, Vector3.up);
            p.TeleportRpc(pos, yaw);
            return "Home sweet home!";
        }

        string Clones(PlayerNet p, Vector3 ground, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var off = n == 1 ? Vector3.zero : Quaternion.Euler(0, i * 360f / n, 0) * Vector3.forward * 1.8f;
                Vehicle.ServerSpawnDecoy(ground + off + Vector3.up * 0.1f, p.transform.eulerAngles.y, p, p.HeldItem);
            }
            return n == 1 ? "A copy of you!" : $"An army of {n} of you!";
        }

        string Sus(PlayerNet p)
        {
            if (!Cfg.TeamGame)
            {
                p.InvisUntil.Value = ScribbleNow + Cfg.InvisTime;
                return "You vented. (invisible)";
            }
            int enemy = (p.Team.Value + 1) % Mathf.Max(2, Cfg.TeamCount);
            p.DisguiseTeam.Value = (byte)enemy;
            Fx.Server(FxKind.Poof, p.transform.position + Vector3.up, Vector3.up);
            return $"Kinda sus... disguised as {Cfg.TeamLabel(enemy)}";
        }

        string Jail(PlayerNet p)
        {
            var e = ScribbleEnemy(p);
            if (e == null || e.WackySafe) return null;
            e.PrisonUntil.Value = ScribbleNow + Cfg.PrisonTime;
            if (e.CarryingBall) Ball.Instance.ServerDrop(e.transform.position + Vector3.up * 1.5f, Vector3.up * 4f);
            e.ServerDismount();
            return $"FBI OPEN UP! {e.DisplayName} is locked up";
        }

        List<ScribbleWord> BuildScribbleWords()
        {
            var l = new List<ScribbleWord>();
            void Add(string keys, Func<PlayerNet, Vector3, string, string> act)
            {
                var ks = keys.Split(',');
                for (int i = 0; i < ks.Length; i++) ks[i] = ScribbleNorm(ks[i]).Trim();
                l.Add(new ScribbleWord { Keys = ks, Act = act });
            }

            // ---- guns and weapons ----
            Add("ak,ak47,ak 47,m4,m4a1,m16,ar,ar15,assault rifle,scar,gold scar,golden scar,gun,guns,minigun,machine gun,smg,uzi,mp5,glock,deagle,desert eagle,pistol,handgun,weapon,weapons,pew pew,john wick,rambo,cowboy,tommy gun,mac 10,vector,p90",
                (p, g, w) => { ScribbleGive(p, Item.Revolver); ScribbleGive(p, Item.RevolverAmmo, 18); return "A revolver (and bullets)!"; });
            Add("sniper,sniper rifle,awp,barrett,50 cal,rifle,scope,quickscope,quick scope,noscope,no scope,360 no scope,mlg,hunting rifle,kar98",
                (p, g, w) => Gives(p, Item.Sniper, 1, "A sniper rifle! Go get that quickscope"));
            Add("shotgun,pump,spas,double barrel,boomstick,shotty",
                (p, g, w) => { ScribbleGive(p, Item.Shotgun); ScribbleGive(p, Item.ShotgunShell, 8); return "A shotgun (and shells)!"; });
            Add("rpg,bazooka,rocket,rocket launcher,missile,javelin,launcher,stinger",
                (p, g, w) => Gives(p, Item.RocketLauncher, 1, "A rocket launcher!"));
            Add("bow,arrow,archer,legolas,hawkeye,robin hood,bow and arrow,cupid",
                (p, g, w) => { ScribbleGive(p, Item.Bow); ScribbleGive(p, Item.Arrow, 20); return "A bow (and arrows)!"; });
            Add("sword,katana,lightsaber,light saber,blade,excalibur,diamond sword,netherite sword,master sword,zelda,samurai,knife,dagger,machete,scythe,keyblade,buster sword",
                (p, g, w) => Gives(p, Item.Sword, 1, "A sword!"));
            Add("spear,trident,lance,pitchfork,javelin spear,poseidon",
                (p, g, w) => Gives(p, Item.Spear, 1, "A spear!"));
            Add("axe,hatchet,pickaxe,diamond pickaxe,kratos,lumberjack,pick,battle axe,minecraft,steve,crafting table",
                (p, g, w) => Gives(p, Item.Hatchet, 1, "A hatchet!"));
            Add("chainsaw,leatherface,doom,doom guy,doomguy",
                (p, g, w) => Gives(p, Item.Chainsaw, 1, "A chainsaw! VRRRRRM"));
            Add("c4,tnt,dynamite,sticky bomb,explosives,semtex,claymore,stickies",
                (p, g, w) => Gives(p, Item.C4, 2, "C4 x 2!"));
            Add("creeper,aw man",
                (p, g, w) => Gives(p, Item.C4, 2, "Aw man... (C4 x 2)"));
            Add("death wand,avada kedavra,avada,voldemort,elder wand,harry potter,thanos,infinity gauntlet,snap",
                (p, g, w) => Gives(p, Item.DeathWand, 1, "A DEATH WAND!"));
            Add("wand,magic,wizard,witch,spell,gandalf,merlin,sorcerer,mage,dumbledore",
                (p, g, w) => { var picks = new[] { Item.Connect4Wand, Item.GameshowWand, Item.SlowGun, Item.AntGun }; return Gives(p, picks[Random.Range(0, picks.Length)]); });
            Add("shrink ray,shrink gun,ant gun,ant man gun",
                (p, g, w) => Gives(p, Item.AntGun));
            Add("freeze ray,ice gun,freeze gun,slow gun,goo gun",
                (p, g, w) => Gives(p, Item.SlowGun));
            Add("knockback,kb,bonk,bat,baseball bat,slap,punch,fist,stick,boxing,boxing glove,kill aura,killaura,yeet,home run,homerun,hammer time,frying pan,pan",
                (p, g, w) => Gives(p, Item.KnockStick, 1, "A knockback stick! YEET"));
            Add("blue shell,mario kart,shell,turtle shell,red shell,green shell",
                (p, g, w) => Gives(p, Item.BlueShell));
            Add("trap,stun,stun trap,bear trap,mine,landmine,land mine,taser",
                (p, g, w) => Gives(p, Item.BearTrap, 1, "A stun trap!"));
            Add("turret,sentry,drone,robot,robots,mech,gundam,transformer,optimus prime,cyborg,camera,cctv,terminator,r2d2,wall e,walle",
                (p, g, w) => Gives(p, Item.DroneBot, 1, "A turret bot!"));
            Add("ladder,stairs,staircase,climb,climbing",
                (p, g, w) => Gives(p, Item.Ladder, 1, "A ladder!"));
            Add("bomb vest,self destruct,kamikaze,explode myself,suicide",
                (p, g, w) => Gives(p, Item.SuicideBomb));

            // ---- big boom from the sky ----
            Add("nuke,tactical nuke,nuclear,atomic bomb,atom bomb,hydrogen bomb,moab,nuke them,nuke the base,nuclear bomb,icbm",
                (p, g, w) => { var e = ScribbleEnemy(p); ServerAirstrike(e != null ? e.transform.position : g, p); return "TACTICAL NUKE INCOMING!"; });
            Add("helicopter,heli,chopper,apache,attack helicopter,bomber,b52,stealth bomber,fighter jet,f16,f35,drone strike,air support,airplane,plane,jet,meteor,asteroid,meteor shower",
                (p, g, w) => { ServerAirstrike(g, p); return "Incoming from the sky!"; });
            Add("kill,kill him,kill her,kill them,kill everyone,kill all,die,murder,smite,lightning,lightning bolt,thunder,thor,zeus,pikachu,thunderbolt,kamehameha,hadouken,laser,lasers,death ray,headshot,orbital strike,orbital cannon,ion cannon,sniped,instakill,one shot",
                (p, g, w) => Smite(p, w));

            // ---- cheats ----
            Add("aimbot,aim bot,hacks,hacker,hack,hacking,cheats,cheat,cheater,cheating,esp,wallhack,wallhacks,wall hack,xray,x ray,uav,radar,spy plane,see through walls,mod menu",
                (p, g, w) => Gives(p, Item.Wallhack, 1, "Hacks enabled: wallhack glasses!"));
            Add("god mode,godmode,god,immortal,immortality,invincible,invulnerable,shield,bubble,force field,forcefield,plot armor,plot armour,unkillable",
                (p, g, w) => { p.ShieldUntil.Value = ScribbleNow + Cfg.ShieldTime; return "GOD MODE! (shield up)"; });
            Add("fly,flying,fly hack,flyhack,wings,superman,iron man,ironman,jetpack,jet pack,angel,bird,rocket boots,creative mode,elytra",
                (p, g, w) => Gives(p, Item.Jetpack, 1, "A jetpack - fly!"));
            Add("noclip,no clip,tp,teleport,teleporter,blink,ender,enderman,ender pearl,warp,flash step",
                (p, g, w) => Gives(p, Item.EnderPearl, 2, "Ender pearls x 2!"));
            Add("portal,portals,portal gun,rick and morty,rick",
                (p, g, w) => Gives(p, Item.PortalGun, 1, "A portal gun!"));
            Add("home,go home,tp home,base,spawn,respawn,recall,back to base,take me home",
                (p, g, w) => Home(p));

            // ---- money ----
            Add("free robux,robux,vbucks,v bucks,free vbucks,free money,bitcoin,crypto,dogecoin,stonks,bank,atm,rich,money,cash,gold,diamond,diamonds,emerald,emeralds,coins,treasure,infinite wood,free wood,wood,unlimited wood,millionaire,billionaire,dollar,dollars,elon musk,elon,jeff bezos",
                (p, g, w) => { p.ServerGive(Cfg.GatherItem(Item.Wood), 1000); return "1000 wood!"; });
            Add("mrbeast,mr beast",
                (p, g, w) => { p.ServerGive(Cfg.GatherItem(Item.Wood), 1000); return "MrBeast gave you 1000 wood!"; });

            // ---- loot ----
            Add("airdrop,air drop,supply drop,care package,loot,lootbox,loot box,lucky block,mystery box,gift,present,santa,christmas,op,overpowered,op item,best item,best weapon,everything,legendary,mythic,random,surprise,anything,crate,chest of loot,gacha,spin,jackpot,mythic item,golden item",
                (p, g, w) => RandomAirdrop(p));

            // ---- you, powered up ----
            Add("speed,fast,faster,sonic,flash,the flash,cheetah,zoom,zoomies,turbo,coffee,caffeine,energy,monster energy,red bull,prime,energy drink,speed hack,speedhack,sprint,nitro,gotta go fast,usain bolt,speedrun",
                (p, g, w) => Speedy(p, "SUPER SPEED!"));
            Add("giant,huge,titan,hulk,godzilla,kaiju,big,tall,grow,mega,the rock,dwayne johnson,dwayne,colossal,giga",
                (p, g, w) => Giant(p, "You're a GIANT!"));
            Add("sigma,chad,gigachad,giga chad,alpha,goat,sigma male,mewing,mog,mogging,looksmaxxing",
                (p, g, w) => Giant(p, "SIGMA. You're a GIANT"));
            Add("goku,super saiyan,saiyan,vegeta,naruto,over 9000,9000,power up,powerup,buff,steroids,one punch man,saitama,anime,ultra instinct,beast mode",
                (p, g, w) => { Giant(p, null); return Speedy(p, "POWER LEVEL OVER 9000! (giant and fast)"); });
            Add("tank,juggernaut,armored,armoured,armor,armour,knight,helmet,iron golem,master chief",
                (p, g, w) => { ScribbleGive(p, Item.HeavyArmor); return "Armour on!"; });
            Add("invisible,invis,invisibility,ninja,stealth,vanish,ghost mode,batman,predator,spy,sneaky,sneak,hide,hiding,disappear",
                (p, g, w) => { p.InvisUntil.Value = ScribbleNow + Cfg.InvisTime; return "You're invisible!"; });
            Add("camo,camouflage,ghillie,ghillie suit,tree,trees,forest,leaf,leaves,groot",
                (p, g, w) => Gives(p, Item.TreeCamo, 1, "Tree camo!"));
            Add("bear,grizzly,panda,polar bear,teddy,teddy bear,winnie,pooh,winnie the pooh,five nights at freddys bear",
                (p, g, w) => { p.ServerMakeBear(); return "You're a BEAR!"; });
            Add("jump,jumps,boots,boot,shoes,sneakers,jordans,air jordans,nike,crocs,frog,kangaroo,bunny,rabbit,spring,pogo,pogo stick,parkour,bhop,bunny hop,triple jump,double jump",
                (p, g, w) => Gives(p, Item.JumpBoots, 1, "Triple jump boots!"));
            Add("mario,luigi,wahoo,its a me",
                (p, g, w) => Gives(p, Item.JumpBoots, 1, "Wahoo! Triple jump boots!"));
            Add("gravity,antigravity,anti gravity,zero g,zero gravity,moon,space,upside down,astronaut,flip,australia",
                (p, g, w) => Gives(p, Item.GravityStaff));
            Add("lift,levitate,levitation,float,floating,telekinesis,jedi,the force,force,yoda,sith,darth vader,vader,magneto,psychic,wingardium leviosa,leviosa",
                (p, g, w) => Gives(p, Item.StickOfTruth, 1, "A Lifting Stick!"));

            // ---- heal / food ----
            Add("pizza,burger,cheeseburger,hamburger,mcdonalds,mcdonald,big mac,kfc,nuggets,chicken nuggets,fries,french fries,hotdog,hot dog,taco,burrito,sushi,ramen,noodles,pasta,spaghetti,donut,doughnut,ice cream,candy,chocolate,cookie,cake,sandwich,steak,bacon,apple,banana,watermelon,popcorn,chips,doritos,cheetos,cheese,egg,bread,meal,snack,food,eat,lunch,dinner,breakfast,grimace shake,milk,soda,cola,coke,pepsi,juice,chicken,meat,subway,dominos,five guys,taco bell,wendys,burger king,fanta,gatorade,cereal,pancake,waffle,lasagna,kebab,mango,strawberry,orange",
                (p, g, w) => { ScribbleGive(p, Item.Meat, 2); return "Food! (it heals you fully)"; });
            Add("heal,healing,medkit,med kit,bandage,bandages,first aid,health,hp,potion,chug jug,chug,slurp,slurp juice,big pot,mini shield,doctor,medic,nurse,hospital,jesus,revive,regen,regeneration,golden apple,totem",
                (p, g, w) => { p.Health.Value = Cfg.MaxHealth; return "Healed to full!"; });
            Add("sleep,nap,bed,tired,zzz,afk,sleepy,go to bed,bedtime",
                (p, g, w) => { p.Health.Value = Cfg.MaxHealth; p.KnockUntil.Value = ScribbleNow + 2f; return "You took a nap. Healed to full!"; });

            // ---- at the enemy ----
            Add("dance,dancing,griddy,the griddy,floss,flossing,emote,default dance,orange justice,take the l,fortnite,fortnite dance,party,disco,rave,club,music,macarena,gangnam style,twerk,dab,dabbing,boogie,shuffle,moonwalk",
                (p, g, w) => EnemyDances(p, "{0} has to DANCE!"));
            Add("rick roll,rickroll,rick rolled,never gonna give you up,rick astley",
                (p, g, w) => EnemyDances(p, "Never gonna give {0} up! DANCE!"));
            Add("67,six seven,6 7",
                (p, g, w) => EnemyDances(p, "6... 7... {0} has to DANCE!"));
            Add("rizz,rizzler,the rizzler,unspoken rizz,love,kiss,heart,date,girlfriend,boyfriend,gf,bf,valentine,marry me,crush,simp,uwu,owo,flirt,cute,baddie,hot",
                (p, g, w) => EnemyDances(p, "{0} got rizzed up. They're dancing!"));
            Add("noob,n00b,newb,l,loser,trash,bot,ratio,skill issue,cringe,bozo,nerd,idiot,dumb,stupid,clown,ez,gg ez,get rekt,rekt,owned,pwned,you suck,u suck,garbage,washed,npc,bad,dog water,dogwater,touch grass,cry,cry about it,mad,cope,seethe,fatherless,no friends,banana peel",
                (p, g, w) => EnemyFlat(p, "{0} took the L (knocked flat)"));
            Add("freeze,frozen,ice,snow,time stop,stop time,za warudo,the world,zawarudo,pause,statue,slow motion,slowmo,matrix,stun them,cold",
                (p, g, w) => EnemySlowed(p, "{0} is FROZEN (slowed)!"));
            Add("lag,laggy,lagging,ping,high ping,wifi,internet,lag switch,dial up,buffering,low fps,disconnect,dc",
                (p, g, w) => EnemySlowed(p, "{0} is LAGGING (slowed)!"));
            Add("slow,sloth,turtle,glue,goo,mud,sticky,honey,snail mail,molasses,quicksand,swamp,shrek,ogre",
                (p, g, w) => EnemySlowed(p, w.ToLowerInvariant().Contains("shrek") || w.ToLowerInvariant().Contains("swamp") || w.ToLowerInvariant().Contains("ogre") ? "GET OUT OF MY SWAMP, {0}! (slowed)" : "{0} is stuck in goo!"));
            Add("ant,ants,bug,bugs,insect,insects,tiny,small,shrink,ant man,antman,minimize,mini me,smol",
                (p, g, w) => { var e = ScribbleEnemy(p); if (e == null || e.WackySafe) return null; e.AntUntil.Value = ScribbleNow + Cfg.AntTime; Fx.Server(FxKind.AntZap, e.transform.position + Vector3.up, Vector3.up); return $"{e.DisplayName} is an ant!"; });
            Add("steal,rob,robbery,thief,yoink,heist,fanum tax,fanum,tax,taxes,pickpocket,loot him,loot them,mine now,gimme,give me",
                (p, g, w) => Steal(p));
            Add("jail,prison,cage,arrest,police,cop,cops,fbi,fbi open up,swat,handcuffs,timeout,time out,grounded,banned,ban,ban hammer,kick",
                (p, g, w) => Jail(p));
            Add("sus,amogus,among us,impostor,imposter,vent,emergency meeting,red sus,crewmate,sussy,sussy baka",
                (p, g, w) => Sus(p));

            // ---- creatures (they hunt your enemies) ----
            Add("dragon,dinosaur,dino,t rex,trex,raptor,velociraptor,shark,megalodon,jaws,lion,tiger,wolf,werewolf,gorilla,monkey,ape,king kong,bigfoot,yeti,snake,spider,scorpion,bee,killer bees,dog,doggo,puppy,pet,cat,kitten,hellhound,herobrine,freddy,fnaf,freddy fazbear,springtrap,huggy wuggy,huggy,skibidi,skibidi toilet,toilet,siren head,sirenhead,backrooms,entity,jeff the killer,scp,clown monster,pennywise,skeleton,zombie,zombies,vampire,mummy,boss,demogorgon,xenomorph,godzilla monster,cthulhu,kraken,hydra,minotaur,troll,goblin,orc,witch monster,nextbot,obunga,granny,baldi,mimic,warden,wither,ender dragon",
                (p, g, w) => Creature(p, g, w));
            Add("horse,pony,stallion,donkey,camel,zebra,llama,alpaca,elephant,mount,steed,giraffe,cow,mule,reindeer,deer",
                (p, g, w) => { Vehicle.ServerSpawn(Vehicle.Horse, g + Vector3.up * 0.3f, p.transform.eulerAngles.y); return "Something to ride!"; });
            Add("unicorn,pegasus,rainbow horse",
                (p, g, w) => { Vehicle.ServerSpawn(Vehicle.Horse, g + Vector3.up * 0.3f, p.transform.eulerAngles.y, true); return "A UNICORN!"; });
            Add("car,cars,lambo,lamborghini,ferrari,tesla,bmw,bugatti,porsche,mclaren,mercedes,audi,toyota,honda,supra,gtr,go kart,kart,monster truck,jeep,limo,limousine,taxi,uber,f1,race car,racecar,batmobile,truck,bus,van,lightning mcqueen,kachow,vehicle,ride",
                (p, g, w) => { Vehicle.ServerSpawn(Vehicle.Car, g + Vector3.up * 0.6f, p.transform.eulerAngles.y); return w.ToLowerInvariant().Contains("kachow") || w.ToLowerInvariant().Contains("mcqueen") ? "KACHOW! A car!" : "A car appeared!"; });
            Add("clone,twin,decoy,copy,mirror,doppelganger,friend,buddy,ally,bodyguard,teammate,backup,duplicate,me",
                (p, g, w) => Clones(p, g, 1));
            Add("army,clones,clone army,squad,team,gang,crew,minions,reinforcements,friends,the boys",
                (p, g, w) => Clones(p, g, 3));

            // ---- the world ----
            Add("storm,tornado,hurricane,earthquake,ohio,only in ohio,black hole,vortex,twister,tsunami,flood,volcano,lava,the floor is lava,apocalypse,end of the world,armageddon,fire,inferno,flamethrower,rain,thunderstorm,explosion of chaos,chaos,anarchy,madness,mayhem,disaster",
                (p, g, w) => { ServerStickOfTruth(g, p); return w.ToLowerInvariant().Contains("ohio") ? "Only in Ohio... A STORM!" : "A STORM!"; });
            Add("house,bunker,fort,fortress,building,skyscraper,mansion,tower,castle tower,lookout,hut,cabin,igloo,treehouse",
                (p, g, w) => { ServerScribblePiece(PieceType.Tower, g, p); return "A tower popped up!"; });
            Add("wall,great wall,fence,barrier,cover,shield wall,the wall",
                (p, g, w) => { ServerScribblePiece(PieceType.Barrier, g, p); return "A wall appeared!"; });
            Add("trampoline,bouncy,bouncy house,bounce house,jump pad,bouncy castle,castle,bounce",
                (p, g, w) => { ServerAddGadget(Gadget.Castle, g, p.transform.eulerAngles.y + 180f, p.Team.Value, Cfg.CastleLife, Random.Range(1, 99999)); return "A bouncy castle!"; });
            Add("poop,poo,fart,farts,toot,stinky,smelly,shit,crap,gas,smoke,smoke bomb,fog,cloud,vape,vaping,smoke screen,flashbang",
                (p, g, w) => { ServerAddGadget(Gadget.Smoke, p.transform.position, 0f, p.Team.Value, 8f, Random.Range(1, 99999)); return w.ToLowerInvariant().Contains("smoke") || w.ToLowerInvariant().Contains("fog") || w.ToLowerInvariant().Contains("cloud") || w.ToLowerInvariant().Contains("vape") || w.ToLowerInvariant().Contains("flash") ? "Smoke!" : "Ew. (smoke cloud)"; });
            Add("glitter,confetti,sparkle,sparkles,fireworks,firework,rainbow,junk,mess,clutter,trash pile,hoarder",
                (p, g, w) => { ServerAddGadget(Gadget.Clutter, g, 0f, p.Team.Value, Cfg.ClutterTime, Random.Range(1, 99999)); return "What a mess!"; });
            Add("snail,slug,attention,look at me,notice me,pick me,main character,look over there,distraction",
                (p, g, w) => { ServerAddGadget(Gadget.Snail, g, p.transform.eulerAngles.y + 180f, p.Team.Value, Cfg.SnailTime, Random.Range(1, 99999)); return "Everyone, look at the snail!"; });
            Add("bush,berry,berries,fruit,plant,garden,farm,grass",
                (p, g, w) => { ServerSpawnBushAt(g); return "A berry bush grew!"; });
            Add("bomb,explosion,explode,grenade,kaboom,boom,frag,airstrike here,blow up,blow it up,detonate",
                (p, g, w) => { ServerRocket(g + Vector3.up * 0.5f, p); return "KABOOM!"; });

            // ---- the game ----
            Add("win,i win,victory,winner,dub,w,gg,good game,wp,gg wp,champion,winning,first place,1st place,victory royale,number one,1,ez win,free win,winner winner chicken dinner",
                (p, g, w) => { p.GlitterUntil.Value = ScribbleNow + Cfg.GlitterTime; Fx.Server(FxKind.GoldBurst, p.transform.position + Vector3.up, Vector3.up); return "Nice try. You still have to score! (you're golden though)"; });
            Add("ball,the ball,magnet,grab,goal,score,touchdown,football,soccer,basketball,messi,ronaldo,cr7,siu,siuu,siuuu,goat ball,get the ball",
                (p, g, w) => Gives(p, Item.BallGrabber, 1, w.ToLowerInvariant().Contains("siu") || w.ToLowerInvariant().Contains("ronaldo") ? "SIUUU! A Ball Grabber 3000!" : "A Ball Grabber 3000!"));
            Add("swap,switch,trade,exchange,switcheroo,trade offer",
                (p, g, w) => Gives(p, Item.InvSwapper));
            Add("map,compass,gps,google maps,waypoint,minimap,where are they,find them",
                (p, g, w) => Gives(p, Item.OmenMap));
            Add("notebook,pen,pencil,wish,wishes,more wishes,genie,infinite wishes,scribblenauts,write,another one,dj khaled",
                (p, g, w) => Gives(p, Item.Scribble, 1, "Nice try... one more page!"));
            Add("draw,paint,art,picasso,ms paint,mspaint,drawing,crayon,crayons,doodle,sketch,paint bucket",
                (p, g, w) => Gives(p, Item.DrawMonster));
            Add("gameshow,game show,quiz,trivia,jeopardy,who wants to be a millionaire,question",
                (p, g, w) => Gives(p, Item.GameshowWand));
            Add("connect 4,connect four,connect4,board game,tic tac toe,chess",
                (p, g, w) => Gives(p, Item.Connect4Wand));
            Add("troll,trolling,prank,fake,fake airdrop,bait,gotcha,april fools",
                (p, g, w) => Gives(p, Item.FakeFlare, 1, "Time to troll: a fake airdrop!"));
            Add("timeout ball,hamster ball,ball pit",
                (p, g, w) => Gives(p, Item.TimeoutBall));
            Add("rock of immortality,golden rock,gold rock,immortal rock",
                (p, g, w) => Gives(p, Item.GoldenRock));
            Add("dance bomb,disco bomb,party bomb",
                (p, g, w) => Gives(p, Item.DanceBomb));
            return l;
        }
    }
}
