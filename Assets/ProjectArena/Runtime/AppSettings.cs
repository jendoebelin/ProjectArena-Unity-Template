using System;
using System.Globalization;
using System.IO;
using ProjectArena.Sim;
using UnityEngine;

namespace ProjectArena
{
    // The player's settings, kept as the native game keeps them (main.cpp saveSettings / loadSettings,
    // savePlayerName / loadPlayerName, help_seen): settings.cfg ("key=value" lines, the same keys, ranges and cfgver),
    // player_name.txt and help_seen, in the app's own storage (Application.persistentDataPath; the native game uses its
    // external data folder). Settings this port does not use yet (voice, passthrough, servers, crowd, ...) are read and
    // written back unchanged, so a file stays complete.
    public class AppSettings
    {
        public const int CfgVersion = 9;   // (9: BALL LOOK-AHEAD on by default, native 860b45e)

        // the native defaults
        public int volume = 8;            // 0..10
        public int pointerHand = 1;       // 1 = right
        public int fxLevel = 0;           // hit effects: 0 full, 1 soft, 2 low (photosensitivity)
        public int crowdMode = 0, turnMode = 0, voiceMode = 0;
        public bool hideOutside, muteSpectators, mutePlayers, muteAll, onlinePref = true, botFill = true, statsNoticeSeen;   // (online: the community server by default, as natively)
        public bool privateTalk;          // ROOM TALK: PRIVATE in rooms we host (native 0.3.13)
        public bool ballSound = true;     // BALL IN FLIGHT (TASKS.md E52, native 0.3.24): hear the balls that can hurt you as they fly
        // MUSIC (TASKS.md E60, native 1159a46, 0.3.24): SETTINGS' MUSIC row: on in the lobby (MENU), off elsewhere (GAME);
        // musicVolume 0..10 (30% to start: the owner's pick). settings.cfg musicmenu=, musicgame=, musicvol=
        public bool musicMenu = true, musicGame;
        public int musicVolume = 3;
        // ARENA GLASS (TASKS.md E62, native f7b9cc9): the PC spectator's court glass, glowing lines and strike-zone rings
        // this strong in its picture (1 NORMAL, 0.35 FAINT, 0.2 FAINTER); settings.cfg dcglass=
        public float dcGlass = 1f;
        public bool recordOn;             // RECORD MY MATCHES (Sim/Record.cs; MY STATS): off unless the player turns it on
        // BALL LOOK-AHEAD (native 0.3.15, SETTINGS > GRAPHICS AND VIEW): on by default from 0.3.21 (TASKS.md E24, native
        // 860b45e; the owner, 2026-11-17, after a player's lag report: in a room someone else hosts the ball wobbled in the
        // hand); an older settings.cfg (cfgver under 9: every one says lookahead=0 unless it was turned on) turns it on once
        public bool lookAhead = true;
        public bool trainMine;            // CUSTOM TRAINING (native 3c285d9): the lessons page's mode, MY OPTIONS (TrainingApp.mine)
        public int roomFeel;              // FIELD OF VIEW (native roomFeel): -5..+5, 10 % a step (RoomFeelFeature)
        // GRAPHICS (TASKS.md E38, native c97c364 / 9b00fbc / c184b84; Runtime/ArenaGraphics.cs): FOVEATION 0 OFF .. 3 HIGH
        // (HIGH: measured natively, 1.2 ms back, no blur seen); SMOOTH EDGES 0 OFF, 1 2X, 2 4X; CONTACT SHADOWS, COLOR
        // GRADING, BLOOM and FLOOR REFLECTIONS 0 OFF, 1 SPECTATING (only while you watch), 2 ON
        public int gfxFoveation = 3, gfxMsaa = 0, gfxContact = 1, gfxGrade = 1, gfxBloom = 1, gfxReflect = 1;
        // the PC spectator's own (Spectator Controls > Graphics; saved on that PC), all on: dcMsaa as gfxMsaa
        public int dcMsaa = 2;
        public bool dcContact = true, dcGrade = true, dcBloom = true, dcAo = true, dcVignette = true, dcReflect = true;
        // CONTROLLER ANGLE (native 0.3.20, TASKS.md E4; a player through the owner, 2026-11-15: going from one headset's
        // controllers to another's, throws that felt natural had to be learned again): your own TILT, TWIST and TURN in
        // degrees on top of the standard angle (XrInput's TUNING), saved on this headset. Your hands as the game sees
        // them follow it (the gauntlet, the throw, the shield, the knuckle guard), and a guest sends its hands turned
        // already, so the host judges them the same; the laser keeps the controller's own pointing
        public const int CtlStep = 5, CtlMax = 30;
        public int ctlTilt, ctlTwist, ctlTurn;
        // BUTTONS (native controls.h, TASKS.md E20 / E21): each button's job, settings.cfg buttons= (nine digits; one this
        // game can't read: STANDARD, and an old settings.cfg plays as before)
        public Controls.Map buttons = Controls.Standard();
        // SELFIE (native 0.3.13, task D2): the PC spectator's webcam (its corner 0-3: top left, top right, bottom left,
        // bottom right; size 0-2; round or square; which webcam), and SELFIE in the headset while spectating
        public bool selfieOn, selfieRound = true, headSelfie;
        public int selfieCorner = 3, selfieSize = 1, selfieCam;
        public int ptMode;
        public float ptFloor = 0.8f, ptGlass = 1f, ptRoom = 1f;   // (native's: the floor ARENA starts with)
        public bool ptMono, ptEdges, ptEdgeReveal = true, ptPeekOn = true;
        public string team = "", server = "";
        public float tunePitchDeg = 27f, tuneFwd = 0.004f, tuneUp = -0.014f;   // (the gauntlet tuning: TUNING tool, later)
        public string name = "";
        public bool helpSeen;
        bool portNet;   // settings.cfg portnet=1: written by builds with online play
        public AvatarDesc myAvatar = AvatarDesc.Default, botAvatar = AvatarDesc.Default;   // avatar.cfg; the practice bot's look
        // NEW BOT (TASKS.md E55, native 75f3f1f, d866da5, 0.3.23): the bots' outfits and stances come from this (new at every
        // start of the game, never saved; 0: not yet); each corner's outfit (botLooks[1] is botAvatar, the practice bot's).
        // botEveryMatch (settings.cfg botnew=): a new bot as each match comes, or only on NEW BOT NOW
        public uint botSeed;
        public bool botEveryMatch = true;
        public readonly AvatarDesc[] botLooks = { AvatarDesc.Default, AvatarDesc.Default, AvatarDesc.Default, AvatarDesc.Default };
        // looks.cfg (TASKS.md E44 / E45, native 10668f0 / ddd18a1, 0.3.23): how often each country was chosen (the COUNTRY
        // order: those first, then A-Z) and MY LOOKS, three saved looks in the builder's left panel; this device only, never sent
        public readonly int[] nationPicks = new int[256];
        public readonly AvatarDesc[] myLooks = { AvatarDesc.Default, AvatarDesc.Default, AvatarDesc.Default };
        public readonly bool[] myLookSaved = new bool[3];

        public string Folder;   // where the files are (tests use their own)

        public AppSettings(string folder = null) => Folder = folder ?? Application.persistentDataPath;

        string PathOf(string f) => System.IO.Path.Combine(Folder, f);

        static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

        // native saveSettings: the rules (MATCH OPTIONS) come from the tunables, everything else from here
        public void Save(Tunables t)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var s = new System.Text.StringBuilder();
                void L(string k, string v) => s.Append(k).Append('=').Append(v).Append('\n');
                L("cfgver", CfgVersion.ToString());
                L("advanced", t.advanced ? "1" : "0"); L("shield", t.shieldPlacement.ToString());
                L("pitch", F(tunePitchDeg, "0.0")); L("fwd", F(tuneFwd, "0.000")); L("up", F(tuneUp, "0.000"));
                L("volume", volume.ToString()); L("hand", pointerHand.ToString()); L("fx", fxLevel.ToString());
                L("trainmine", trainMine ? "1" : "0");   // (CUSTOM TRAINING)
                L("lookahead", lookAhead ? "1" : "0"); L("roomfeel", roomFeel.ToString());
                L("foveation", gfxFoveation.ToString()); L("msaa", gfxMsaa.ToString()); L("contact", gfxContact.ToString());   // (GRAPHICS: older
                L("grade", gfxGrade.ToString()); L("bloom", gfxBloom.ToString()); L("reflect", gfxReflect.ToString());         // versions skip them)
                L("dcmsaa", dcMsaa.ToString()); L("dccontact", dcContact ? "1" : "0"); L("dcgrade", dcGrade ? "1" : "0");     // (the PC spectator's)
                L("dcbloom", dcBloom ? "1" : "0"); L("dcao", dcAo ? "1" : "0"); L("dcvignette", dcVignette ? "1" : "0"); L("dcreflect", dcReflect ? "1" : "0");
                L("headselfie", headSelfie ? "1" : "0"); L("selfie", selfieOn ? "1" : "0"); L("selfiecorner", selfieCorner.ToString());
                L("selfiesize", selfieSize.ToString()); L("selfieround", selfieRound ? "1" : "0"); L("selfiecam", selfieCam.ToString());
                L("hideoutside", hideOutside ? "1" : "0"); L("voice", voiceMode.ToString());
                L("mutespec", muteSpectators ? "1" : "0"); L("muteplayers", mutePlayers ? "1" : "0"); L("muteall", muteAll ? "1" : "0");
                L("ballsound", ballSound ? "1" : "0");   // (BALL IN FLIGHT, 0.3.24)
                L("dcglass", F(dcGlass, "0.00"));   // (the PC spectator's ARENA GLASS)
                L("musicmenu", musicMenu ? "1" : "0"); L("musicgame", musicGame ? "1" : "0"); L("musicvol", musicVolume.ToString());   // (MUSIC)
                L("online", onlinePref ? "1" : "0"); L("portnet", "1"); L("botfill", botFill ? "1" : "0"); L("passthrough", ptMode.ToString());
                L("ptfloor", F(ptFloor, "0.00")); L("ptglass", F(ptGlass, "0.00")); L("ptroom", F(ptRoom, "0.00"));
                L("ptmono", ptMono ? "1" : "0"); L("ptedges", ptEdges ? "1" : "0"); L("ptreveal", ptEdgeReveal ? "1" : "0");
                L("ptpeek", ptPeekOn ? "1" : "0"); L("team", team); L("server", server);
                L("statsnotice", statsNoticeSeen ? "1" : "0"); L("crowd", crowdMode.ToString());
                L("shields", t.shields ? "1" : "0"); L("knuckles", t.knuckleMode.ToString());
                L("matchtime", F(t.basicMatchTime, "0")); L("roundtime", F(t.advancedRoundTime, "0")); L("turn", turnMode.ToString());
                // the practice bot (task A8): its level, MATCH MY LEVEL and where it got to, FIST BUMP FIRST
                L("botlevel", t.botLevel.ToString()); L("botadapt", t.botAdapt ? "1" : "0"); L("botmatch", F(t.botMatch, "0.00"));
                L("privatetalk", privateTalk ? "1" : "0"); L("record", recordOn ? "1" : "0"); L("botmeet", t.botMeet ? "1" : "0");
                // the EXPERIMENTAL rules last chosen (native's keys)
                L("experimental", t.experimental ? "1" : "0"); L("exproom", t.expRoom.ToString()); L("teams", t.teamMode.ToString());
                L("oneturns", t.oneTurns ? "1" : "0"); L("health", t.health.ToString()); L("fireballs", t.fireBalls.ToString());
                L("octplayers", t.octPlayers.ToString());
                L("ctltilt", ctlTilt.ToString()); L("ctltwist", ctlTwist.ToString()); L("ctlturn", ctlTurn.ToString());   // (CONTROLLER ANGLE)
                L("botnew", botEveryMatch ? "1" : "0");   // (NEW BOT, 0.3.23: EVERY MATCH or ON PUSH; the bot itself never)
                L("buttons", Controls.Save(buttons));   // (BUTTONS)
                L("strikespeed", t.strikeSpeed.ToString());   // (native 0.3.19: STRIKE SPEED)
                L("foulline", t.foulShift.ToString());        // (native 0.3.20: FOUL LINE)
                // (ROOMS AND THEIR OPTIONS, BOT STYLE: native 50b11ae keeps them with the other match options)
                L("maiden", t.maiden ? "1" : "0"); L("aimassist", t.aimAssist.ToString()); L("strikezone", t.strikeZone.ToString());
                L("ballgrowth", t.ballGrowth.ToString()); L("roundswin", t.advancedRoundsToWin.ToString()); L("botplay", t.botPlay.ToString());
                L("shieldsize", t.shieldSize.ToString());   // (SHIELD SIZE)
                L("ballfeel", (t.ballFeel & Tunables.BALL_FEEL_ALL).ToString());   // (BALL FEEL, the room's)
                L("wallslively", t.wallsLively ? "1" : "0"); L("wallgrip", t.wallGrip ? "1" : "0"); L("bankassist", t.bankAssist.ToString());   // (BOUNCES)
                File.WriteAllText(PathOf("settings.cfg"), s.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[Settings] could not save: " + e.Message); }
        }

        // native loadSettings: unknown keys and bad lines are skipped; numbers are clamped to the native ranges
        public void Load(Tunables t)
        {
            string path = PathOf("settings.cfg");
            int fileCfg = 0;   // (the file's cfgver)
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq), sv = raw.Substring(eq + 1).TrimEnd('\r', '\n', ' ');
                    if (k == "team") { team = sv; continue; }
                    if (k == "server") { server = sv; continue; }
                    if (k == "buttons")
                    {   // BUTTONS (one this game can't read: STANDARD kept)
                        if (!Controls.Load(buttons, sv)) UnityEngine.Debug.LogWarning($"[Settings] buttons={sv} not read (STANDARD kept)");
                        continue;
                    }
                    if (!float.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) continue;
                    switch (k)
                    {
                        case "advanced": t.advanced = v != 0; break;
                        case "shield": t.shieldPlacement = ((int)v) & 3; break;
                        case "pitch": tunePitchDeg = M.Clampf(v, -90, 90); break;
                        case "fwd": tuneFwd = M.Clampf(v, -0.2f, 0.2f); break;
                        case "up": tuneUp = M.Clampf(v, -0.2f, 0.2f); break;
                        case "volume": volume = Math.Max(0, Math.Min(10, (int)v)); break;
                        case "hand": pointerHand = ((int)v) != 0 ? 1 : 0; break;
                        case "fx": fxLevel = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "lookahead": lookAhead = v != 0; break;
                        case "trainmine": trainMine = v != 0; break;
                        case "cfgver": fileCfg = (int)v; break;
                        case "roomfeel": roomFeel = Math.Max(-5, Math.Min(5, (int)v)); break;
                        case "foveation": gfxFoveation = Math.Max(0, Math.Min(3, (int)v)); break;   // (GRAPHICS)
                        case "msaa": gfxMsaa = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "contact": gfxContact = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "grade": gfxGrade = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "bloom": gfxBloom = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "reflect": gfxReflect = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "dcmsaa": dcMsaa = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "dccontact": dcContact = v != 0; break;
                        case "dcgrade": dcGrade = v != 0; break;
                        case "dcbloom": dcBloom = v != 0; break;
                        case "dcao": dcAo = v != 0; break;
                        case "dcvignette": dcVignette = v != 0; break;
                        case "dcreflect": dcReflect = v != 0; break;
                        case "headselfie": headSelfie = v != 0; break;
                        case "selfie": selfieOn = v != 0; break;
                        case "selfiecorner": selfieCorner = Math.Max(0, Math.Min(3, (int)v)); break;
                        case "selfiesize": selfieSize = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "selfieround": selfieRound = v != 0; break;
                        case "selfiecam": selfieCam = Math.Max(0, Math.Min(15, (int)v)); break;
                        case "ctltilt": ctlTilt = Math.Max(-CtlMax, Math.Min(CtlMax, (int)v)); break;
                        case "ctltwist": ctlTwist = Math.Max(-CtlMax, Math.Min(CtlMax, (int)v)); break;
                        case "ctlturn": ctlTurn = Math.Max(-CtlMax, Math.Min(CtlMax, (int)v)); break;
                        case "crowd": crowdMode = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "turn": turnMode = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "shields": t.shields = v != 0; break;   // MATCH OPTIONS (the last ones chosen)
                        case "knuckles": t.knuckleMode = Math.Max(-1, Math.Min(1, (int)v)); break;
                        case "matchtime": t.basicMatchTime = M.Clampf(v, 0f, 3600f); break;
                        case "roundtime": t.advancedRoundTime = M.Clampf(v, 0f, 3600f); break;
                        case "strikespeed": t.strikeSpeed = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "foulline": t.foulShift = Math.Max(-2, Math.Min(2, (int)v)); break;
                        case "maiden": t.maiden = v != 0; break;   // (ROOMS AND THEIR OPTIONS, BOT STYLE)
                        case "aimassist": t.aimAssist = Math.Max(-1, Math.Min(3, (int)v)); break;
                        case "strikezone": t.strikeZone = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "ballgrowth": t.ballGrowth = Math.Max(0, Math.Min(3, (int)v)); break;
                        case "roundswin": t.advancedRoundsToWin = Math.Max(1, Math.Min(5, (int)v)); break;
                        case "botplay": t.botPlay = ((int)v) & 1; break;
                        case "shieldsize": t.shieldSize = Math.Max(0, Math.Min(4, (int)v)); break;
                        case "ballfeel": t.ballFeel = (int)v & Tunables.BALL_FEEL_ALL; break;
                        case "wallslively": t.wallsLively = v != 0; break;   // (BOUNCES)
                        case "wallgrip": t.wallGrip = v != 0; break;
                        case "bankassist": t.bankAssist = Math.Max(0, Math.Min(3, (int)v)); break;   // (BALL FEEL, the room's; the test's own keys left behind)
                        case "botlevel": t.botLevel = Math.Max(0, Math.Min(Sim.Bot.Levels - 1, (int)v)); break;
                        case "botadapt": t.botAdapt = v != 0; break;
                        case "botmatch": t.botMatch = M.Clampf(v, 0f, Sim.Bot.Levels - 1f); break;
                        case "botmeet": t.botMeet = v != 0; break;
                        case "hideoutside": hideOutside = v != 0; break;
                        case "passthrough": break;   // always OFF when the game starts (owner, 2026-10-18); the page keeps the rest
                        case "ptfloor": ptFloor = M.Clampf(v, 0f, 1f); break;
                        case "ptglass": ptGlass = M.Clampf(v, 0f, 2f); break;
                        case "ptroom": ptRoom = M.Clampf(v, 0.1f, 1f); break;
                        case "ptmono": ptMono = v != 0; break;
                        case "ptedges": ptEdges = v != 0; break;
                        case "ptreveal": ptEdgeReveal = v != 0; break;
                        case "ptpeek": ptPeekOn = v != 0; break;
                        case "statsnotice": statsNoticeSeen = v != 0; break;
                        case "voice": voiceMode = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "mutespec": muteSpectators = v != 0; break;
                        case "ballsound": ballSound = v != 0; break;
                        case "musicmenu": musicMenu = v != 0; break;   // (MUSIC)
                        case "dcglass": dcGlass = M.Clampf(v, 0.1f, 1f); break;   // (ARENA GLASS)
                        case "musicgame": musicGame = v != 0; break;
                        case "musicvol": musicVolume = Math.Max(0, Math.Min(10, (int)v)); break;
                        case "botnew": botEveryMatch = v != 0; break;   // (NEW BOT: EVERY MATCH or ON PUSH)
                        case "muteplayers": mutePlayers = v != 0; break;
                        case "muteall": muteAll = v != 0; break;
                        case "online": onlinePref = v != 0; break;
                        case "portnet": portNet = true; break;
                        case "botfill": botFill = v != 0; break;
                        case "privatetalk": privateTalk = v != 0; break;
                        case "record": recordOn = v != 0; break;
                        case "experimental": t.experimental = v != 0; break;
                        case "exproom": t.expRoom = Math.Max(0, Math.Min(Sim.Room.Count - 1, (int)v)); break;   // (DIAMOND, FOUR CORNERS, FIREBALL BLITZ)
                        case "health": t.health = Math.Max(1, Math.Min(10, (int)v)); break;
                        case "fireballs": t.fireBalls = Math.Max(1, Math.Min(4, (int)v)); break;
                        case "octplayers": t.octPlayers = Math.Max(2, Math.Min(4, (int)v)); break;
                        case "teams": t.teamMode = Math.Max(0, Math.Min(2, (int)v)); break;
                        case "oneturns": t.oneTurns = v != 0; break;
                    }
                }
            }
            // files from this port's builds before online play held ONLINE: OFF only as a placeholder: on, once (native default)
            if (File.Exists(path) && !portNet) onlinePref = true;
            if (File.Exists(path) && fileCfg < 9 && !lookAhead)
            {   // BALL LOOK-AHEAD on by default (native 860b45e, owner 2026-11-17): once, for everyone
                lookAhead = true;
                Debug.Log("[Settings] ball look-ahead on (new default)");
                Save(t);
            }
            helpSeen = File.Exists(PathOf("help_seen"));
            LoadName();
        }

        // the name: player_name.txt, made once as "PLAYER nnn" (the clock's milliseconds); at most 16 characters
        void LoadName()
        {
            string path = PathOf("player_name.txt");
            string n = File.Exists(path) ? File.ReadAllText(path).Split('\n')[0].TrimEnd('\r') : "";
            if (n.Length == 0)
            {
                n = $"PLAYER {(int)(DateTime.Now.TimeOfDay.TotalMilliseconds % 1000.0):000}";
                SaveName(n);
            }
            name = n.Length > 16 ? n.Substring(0, 16) : n;
        }

        public void SaveName(string n)
        {
            name = n.Length > 16 ? n.Substring(0, 16) : n;
            try { Directory.CreateDirectory(Folder); File.WriteAllText(PathOf("player_name.txt"), name + "\n"); }
            catch (Exception e) { Debug.LogWarning("[Settings] could not save the name: " + e.Message); }
        }

        // NEW BOT (TASKS.md E55, native applyBot / newBot; the owner, 2026-11-20: "a randomize new bot button to give them a
        // random stance ... and a different outfit ... hosts may not want it to change so we need a push to change button";
        // then, 11-21: "a bot option that allows you to have it randomize every match or on push but no matter what it
        // changes when the game restarts"): the bots' outfits from botSeed (the practice bot's, and each four-player corner's
        // own) and their stances (Game.botLook). A new seed at every start of the game (never saved); then, by the room
        // page's option (botEveryMatch, saved): a new one as each match comes, or only on NEW BOT NOW
        public void ApplyBot(Game g, uint startSeed = 0)
        {
            if (botSeed == 0)
            {   // (the game starting: a bot of its own this time; native: the clock, then the name)
                uint st = startSeed;
                if (st == 0) { st = (uint)(ulong)(DateTime.Now.TimeOfDay.TotalSeconds * 1000003.0); foreach (char c in name) st = st * 31u + (byte)c; }
                botSeed = st != 0 ? st : 1u;
            }
            botAvatar = Avatar.RandomLook(botSeed ^ 0xB07B07u);
            for (int p = 0; p < botLooks.Length; ++p)
                botLooks[p] = p == 1 ? botAvatar : Avatar.RandomLook(botSeed * 2654435761u + 0x51ED27u * (uint)(p + 1));
            if (g != null) g.botLook = botSeed;
        }

        // a new bot: a new outfit and stance for every bot, at once (hosting: everyone sees them); the notice's words (NEW
        // BOT NOW shows them; EVERY MATCH's, between matches, doesn't)
        public string NewBot(Game g)
        {
            uint seed = (uint)(ulong)(DateTime.Now.TimeOfDay.TotalSeconds * 1000003.0 + DateTime.Now.Ticks / 10000) ^ (botSeed * 2654435761u);
            botSeed = seed != 0 ? seed : 1u;
            ApplyBot(g);
            for (int p = 0; p < g.np; ++p) if (g.pl[p].isBot) g.pl[p].botStance = -1;   // (its new stance at once)
            // the notice names a bot's stance as it will play it: the first corner with a bot (the practice bot's, 1, before
            // the match has one), at the level it plays at, as Game.StepBot works it out (native 3fccaf9, review 2026-11-21:
            // it named corner 1's at the room's level, which could be no bot's)
            int bp = 1;
            for (int q = 1; q < g.np + 1; ++q) if (g.pl[q % g.np].isBot) { bp = q % g.np; break; }
            int own = g.pl[bp].botLevel;
            float level = own >= 0 ? own : g.tun.botAdapt ? g.tun.botMatch : g.tun.botLevel;
            return $"NEW BOT: {Bot.StanceName(g.BotStanceFor(bp, level))} STANCE";
        }

        // native loadAvatar: avatar.cfg (key=value lines); a first run picks a random look (the bots' looks: ApplyBot)
        public void LoadAvatar(uint? seedOverride = null)
        {
            uint seed = seedOverride ?? (uint)(long)(DateTime.Now.TimeOfDay.TotalMilliseconds);
            foreach (char c in name) seed = seed * 31u + (byte)c;
            if (botSeed == 0) ApplyBot(null, seed);   // (NEW BOT: this start's bot, from the clock and the name, as natively)
            bool ok = false;
            string path = PathOf("avatar.cfg");
            try { if (File.Exists(path)) { var d = AvatarDesc.Default; ok = Avatar.FromText(File.ReadAllText(path), ref d); if (ok) myAvatar = d; } }
            catch (Exception e) { Debug.LogWarning("[Settings] could not read avatar.cfg: " + e.Message); }
            if (!ok) { myAvatar = Avatar.RandomLook(seed); SaveAvatar(); }
            LoadLooks();
        }

        // looks.cfg: "countries=USA:3,CAN:1" (how often each country was chosen, by its code), then MY LOOKS, each filled
        // slot under "[look N]" in avatar.cfg's lines (native saveLooks / loadLooks). Builds before 0.3.23 never read it
        public void SaveLooks()
        {
            var t = new System.Text.StringBuilder("countries=");
            bool first = true;
            for (int n = 1; n <= Avatar.NationCount(); ++n)
                if (nationPicks[n] > 0) { t.Append(first ? "" : ",").Append(Avatar.NationAt(n).code).Append(':').Append(nationPicks[n]); first = false; }
            t.Append('\n');
            for (int k = 0; k < 3; ++k)
                if (myLookSaved[k]) t.Append("[look ").Append(k + 1).Append("]\n").Append(Avatar.ToText(myLooks[k]));
            try { Directory.CreateDirectory(Folder); File.WriteAllText(PathOf("looks.cfg"), t.ToString()); }
            catch (Exception e) { Debug.LogWarning("[Settings] could not save looks.cfg: " + e.Message); }
        }

        public void LoadLooks(string from = null)
        {
            Array.Clear(nationPicks, 0, nationPicks.Length);
            for (int k = 0; k < 3; ++k) { myLookSaved[k] = false; myLooks[k] = AvatarDesc.Default; }
            string path = from ?? PathOf("looks.cfg");
            try
            {
                if (File.Exists(path))
                {
                    var part = new System.Text.StringBuilder[] { new System.Text.StringBuilder(), new System.Text.StringBuilder(), new System.Text.StringBuilder(), new System.Text.StringBuilder() };
                    int at = 0;
                    foreach (string raw in File.ReadAllText(path).Split('\n'))
                    {
                        string ln = raw.TrimEnd('\r');
                        if (ln.StartsWith("[look ", StringComparison.Ordinal) && ln.EndsWith("]") && int.TryParse(ln.Substring(6, ln.Length - 7), out int k))
                        { at = k >= 1 && k <= 3 ? k : -1; continue; }
                        if (at >= 0) part[at].Append(ln).Append('\n');
                    }
                    string head = part[0].ToString();
                    int c = head.IndexOf("countries=", StringComparison.Ordinal);
                    if (c >= 0)
                    {
                        string list = head.Substring(c + 10);
                        int nl = list.IndexOf('\n');
                        if (nl >= 0) list = list.Substring(0, nl);
                        foreach (string item in list.Split(','))
                        {
                            int colon = item.IndexOf(':');
                            if (colon < 0) continue;
                            string code = item.Substring(0, colon);
                            int.TryParse(item.Substring(colon + 1), out int times);
                            for (int n = 1; n <= Avatar.NationCount(); ++n)
                                if (code == Avatar.NationAt(n).code) nationPicks[n] = Math.Max(0, Math.Min(times, 1000000));
                        }
                    }
                    for (int s = 0; s < 3; ++s)
                        if (part[s + 1].Length > 0) { var d = AvatarDesc.Default; myLookSaved[s] = Avatar.FromText(part[s + 1].ToString(), ref d); myLooks[s] = d; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Settings] could not read looks.cfg: " + e.Message); }
            // (the country you wear counts as chosen once: it heads the list the first time you open the builder)
            int nw = Avatar.NationOf(myAvatar);
            if (nw >= 1 && nw <= Avatar.NationCount() && nationPicks[nw] == 0) nationPicks[nw] = 1;
            int looks = 0;
            foreach (bool sv in myLookSaved) looks += sv ? 1 : 0;
            Debug.Log($"[Settings] avatar: {looks} saved looks");
        }

        public void SaveAvatar()
        {
            try { Directory.CreateDirectory(Folder); File.WriteAllText(PathOf("avatar.cfg"), Avatar.ToText(myAvatar)); }
            catch (Exception e) { Debug.LogWarning("[Settings] could not save avatar.cfg: " + e.Message); }
        }

        // the private key the community server keeps this player's statistics under (made once, stats_key.bin; native
        // loadStatsKey). Never shown or logged.
        public byte[] LoadStatsKey()
        {
            string path = PathOf("stats_key.bin");
            try { if (File.Exists(path)) { byte[] k = File.ReadAllBytes(path); if (k.Length == 16) return k; } }
            catch (Exception) { }
            var key = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(key);
            try { Directory.CreateDirectory(Folder); File.WriteAllBytes(path, key); } catch (Exception e) { Debug.LogWarning("[Settings] could not save the stats key: " + e.Message); }
            Debug.Log("[Stats] a new player key");
            return key;
        }

        public void MarkHelpSeen()
        {
            helpSeen = true;
            try { Directory.CreateDirectory(Folder); File.WriteAllText(PathOf("help_seen"), ""); }
            catch (Exception e) { Debug.LogWarning("[Settings] could not save help_seen: " + e.Message); }
        }
    }
}
