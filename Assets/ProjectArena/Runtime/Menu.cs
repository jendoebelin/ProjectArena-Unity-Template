using System;
using System.Collections.Generic;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The native menu (main.cpp menuItems / menuItemsBase, menuActivate, updateMenu, drawMenu, drawStats, openMenu): a
    // panel in front of you with a side bar of the game's areas; point with the pointer hand's aim ray and pull the
    // trigger. Pages: title, CHOOSE THE RULES -> MATCH OPTIONS (practice, or your room: OPEN MY ARENA), MULTIPLAYER (the
    // games list, HOST A ROOM, JOIN, JOIN / LEAVE QUEUE, FORFEIT, ONLINE on / off), the spectator's page, SETTINGS and
    // VOICE AND MUTES, MY STATS, REPORT OR MUTE A PLAYER (vote to remove, the host's kick), REPORT A BUG, HOW TO PLAY,
    // the keyboard (name, server, team, notes) and the one-time statistics notice. Not in this port (greyed): PASSTHROUGH
    // (phase 6), EXPERIMENTAL (the native test link to another server).
    public class Menu
    {
        public enum Act
        {
            None, MainMenu, Nav, Quit, Bug, RulesPick, RulesBack, Report, Experimental, Host, Leave, Rules, Close, Spectate,
            Avatar, Name, Key, KeyDel, KeyDone, Help, HelpDone, Play, Multi, Settings, Back, VolDown, VolUp, Hand, Fx, Online,
            Server, VoicePage, BotFill, Passthrough, Crowd, Turn, HideOutside, OptShields, OptKnuckle, OptTime, OptGo, OptBack, OptStrike,
            Join, JoinRoom, Watch, Hub, Queue, Forfeit, Director, ClearWarps, DeleteStats, StatsNoticeOk, BugCat, BugNote, BugSend,
            BugCancel, ReportWho, ReportWhy, ReportNote, ReportMute, ReportKick, ReportHostKick, ReportSend, ReportBack,
            VoiceMode, MuteSpec, MutePlayers, MuteAll, ServerDefault,
            // (task C4, native 0.3.12 - 0.3.13: EXPERIMENTAL's rooms and teams, MOVE PLAYERS, HAND OFF HOST, ROOM TALK)
            OptRoom, OptTeams, OptHealth, OptBalls, OptPlayers, MovePlayers, MoveCorner, MoveOne, MoveBack, HandOff, HandOffTo, PrivateTalk,
            // (task A6, native 0.3.12: TRAINING's pages)
            Record, Train, TrainPick, TrainStart, TrainRetry, TrainNext, TrainList, TrainLeave, TrainBoard, TrainKind, TrainBoardBack, TrainShow, TrainVideo,
            // (task C10, native 0.3.17: HOW TO PLAY > CREDITS)
            Credits, CreditsBack,
            // (task B5 / C6, native 0.3.13 - 0.3.15: SETTINGS > GRAPHICS AND VIEW, BALL LOOK-AHEAD, FIELD OF VIEW)
            GfxPage, LookAhead, FovDown, FovUp,
            // (task C8, native 0.3.16: the SOUND STUDIO)
            Studio, StudioSection, StudioPrev, StudioNext, StudioPlay, StudioVote, StudioMoment, StudioLeave, StudioDelete,
            // (task D1: the PASSTHROUGH page)
            PtMode, PtAdjust, PtMono, PtEdges, PtPreset, PtReveal, PtPeek,
            HeadSelfie,   // (task D2: SELFIE in the headset while spectating)
            Defaults,     // (native 0.3.19: DEFAULTS on each settings page; ip: the page, Def*)
            // (task A8: the practice bot's level, its slider and MATCH MY LEVEL; FIST BUMP FIRST)
            OptBot, OptMeet,
            OptFoul,   // (TASKS.md E3, native 0.3.20: FOUL LINE)
            CtlPage, CtlAdj,   // (TASKS.md E4, native 0.3.20: SETTINGS > CONTROLLERS, CONTROLLER ANGLE)
            BallPage, BallCore, BallTrue, BallSpin, BallWeight, BallBack,   // (TASKS.md E18 / E29, native a2e53ce / 3129dc2: BALL FEEL)
            BtnPage, BtnSlot, BtnSticks, BtnPreset,   // (TASKS.md E20 / E21, native eeb17b7: SETTINGS > CONTROLLERS > BUTTONS)
            // (TASKS.md E22, native d4cf079: ROOMS AND THEIR OPTIONS, CHOOSE A ROOM and BEST OF's number pad)
            RoomPick, OptAim, OptZone, OptGrowth, OptRounds, RoundsPad, PadKey, PadDel, PadOk, PadCancel,
            OptBotPlay,   // (TASKS.md E23, native 50b11ae: BOT STYLE beside the bot's level)
            OptShieldSize,   // (TASKS.md E25, native 7ed11aa: SHIELD SIZE)
            GfxFoveation, GfxMsaa, GfxContact, GfxGrade, GfxBloom, GfxReflect,   // (TASKS.md E38, native c97c364 / c184b84: GRAPHICS)
            MutedPage, Unmute, MutedPrev, MutedNext,   // (TASKS.md E46, native 43397af: MUTED PLAYERS)
            BallSound,   // (TASKS.md E52, native bee88ee: BALL IN FLIGHT)
            NewBot, BotEveryMatch,   // (TASKS.md E55, native 75f3f1f, d866da5: NEW BOT NOW; EVERY MATCH or ON PUSH)
            BouncePage, BounceWalls, BounceGrip, BounceAssist, BounceBack,   // (TASKS.md E32, native 5d3bbec: BOUNCES)
            TrainMode, TrainOpts, TrainOptsDone, TrainOptMaiden,   // (TASKS.md E33, native 3c285d9: CUSTOM TRAINING)
            MusicDown, MusicUp, MusicMenu, MusicGame,   // (TASKS.md E60, native 1159a46: MUSIC)
        }

        // HOW TO PLAY > CREDITS (native 0.3.17, kCredits): one page, film-credit style, small grey headings with the names
        // under them. The names as native's, word for word; the small print names this build's own components.
        public enum CreditKind { Head, Name, Small }
        public static readonly (CreditKind kind, string text)[] Credits =
        {
            (CreditKind.Head, "CREATED AND DIRECTED BY"),
            (CreditKind.Name, "JENNIFER DOEBELIN"),
            (CreditKind.Head, "STUDIO"),
            (CreditKind.Name, "CROSSGENGAMING"),
            (CreditKind.Head, "MADE WITH"),
            (CreditKind.Name, "CLAUDE"),
            (CreditKind.Head, "COMMUNITY"),
            (CreditKind.Name, "ASTROBLEEM    R0GUEICE    THEINSANIAC"),
            (CreditKind.Name, "SPEKKIOCT    SKANDI"),
            (CreditKind.Head, "THANK YOU"),
            (CreditKind.Name, "EVERY ALPHA TESTER"),
            (CreditKind.Small, "UNITY  -  OPENXR PLUGIN  -  META OPENXR  -  XR INTERACTION TOOLKIT"),
            (CreditKind.Small, "LICENCES: LICENSE AND LICENSE-ASSETS.MD WITH THIS PROJECT"),
        };

        public struct Item
        {
            public string label;
            public float x, y, w, h;
            public Act act;
            public uint ip;
            public bool enabled;
            public Item(string label, float x, float y, float w, float h, Act act, uint ip, bool enabled)
            { this.label = label; this.x = x; this.y = y; this.w = w; this.h = h; this.act = act; this.ip = ip; this.enabled = enabled; }
        }

        // side menu (left of the panel): the areas of the game, the current one lit
        public enum Nav { Lobby, Home, Practice, Multi, Spectate, Avatar, Stats, Settings, Report, Bug, Help, Quit, Count }
        static readonly string[] NavLabels = { "LOBBY", "MAIN MENU", "PRACTICE", "MULTIPLAYER", "SPECTATE", "AVATAR", "MY STATS",
                                               "SETTINGS", "REPORT PLAYER", "REPORT BUG", "HOW TO PLAY", "QUIT GAME" };
        public const float NavX = -0.67f, NavTop = 0.30f, NavStep = 0.0745f;
        // MY BESTS (TASKS.md E59, native 1159a46, 0.3.24; the owner, 2026-11-21: "the local leaderboard with the training
        // button to appear to the left of the menu"): the title page's board of your standard TRAINING bests, kept on the
        // headset (no network). A wing folded out of the side menu's left edge toward you, as tall as the menu: its own
        // plane with its own pointer test (Update)
        public const float BestsW = 0.44f, BestsH = 0.94f, BestsY = -0.09f;
        public const float BestsFold = 35f;                      // degrees it is folded toward you
        public const float BestsHinge = NavX - 0.15f - 0.015f;   // its right edge: the side menu's left edge, and a gap
        public const float BestsBtnY = -BestsH * 0.5f + 0.07f, BestsBtnW = BestsW - 0.08f, BestsBtnH = 0.075f;   // TRAINING
        public Pose BestsPose()   // its centre, facing you (+Z)
        {
            float t = M.Deg2Rad(BestsFold), h = BestsW * 0.5f;
            return pose * new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), t), new Vec3(BestsHinge - h * Mathf.Cos(t), BestsY, h * Mathf.Sin(t)));
        }
        public bool BestsShown() => open && titleOpen && !helpOpen && !nameEdit && !settingsOpen && !a.builderOpen;
        public bool bestsHover;   // MY BESTS' TRAINING button under the pointer
        // the title's avatar (native kShowcase*): a 60% figure of you right of the menu, its hologram disc at this point of
        // the menu's frame
        public const float ShowcaseScale = 0.6f, ShowcaseX = 0.82f, ShowcaseZ = 0.12f, ShowcaseDiscY = -0.36f;

        public bool open, titleOpen, settingsOpen, rulesOpen, helpOpen, nameEdit, quitArmed;
        public bool creditsOpen;   // HOW TO PLAY > CREDITS (shown while helpOpen)
        public bool statsOpen, voicePage, bugOpen, reportOpen, xpOpen;
        public bool gfxPage;       // SETTINGS > GRAPHICS AND VIEW (native 0.3.13)
        public bool mutedPage;     // SETTINGS > VOICE CHAT > MUTED PLAYERS (TASKS.md E46)
        public int mutedFirst;     // (the first one on its page)
        public bool foveationShown;   // (tests and pictures: FOVEATION's button as on a headset that has it)
        public bool ctlPage;       // SETTINGS > CONTROLLERS (POINTER and CONTROLLER ANGLE; native 0.3.20)
        public bool roomBallPage;  // the room's BALL FEEL: its four switches, a page of the room's own (native 3129dc2; Runtime/BallFeel.cs)
        public bool btnPage;       // SETTINGS > CONTROLLERS > BUTTONS, or VOICE CHAT's TALK BUTTON (native eeb17b7; Runtime/Controls.cs)
        public string btnNote = ""; public double btnNoteUntil;   // the BUTTONS page's word on a change it can't make
        public bool ptPage;        // SETTINGS > PASSTHROUGH (task D1)
        public int rulesStep;      // 0 CHOOSE A ROOM, 1 the room's options
        public bool rulesEdit;     // changing the rules from SETTINGS (back to SETTINGS after)
        public bool rulesForHost;  // the rules of the room we open (HOST A ROOM) or host
        public int editTarget;     // keyboard: 0 your name, 1 the community server address, 2 team, 4 bug note, 5 report note
        public string nameBuf = "";
        public Pose pose = Pose.Identity;
        public int hover = -1;
        public bool hitValid;
        public Vec3 hit;
        bool prevTrig = true;
        // MATCH OPTIONS: the page's choices (nothing changes until START / APPLY)
        public bool optAdvanced, optShields = true;
        public int optKnuckle = -1;
        public float optMatchTime = 180f, optRoundTime;
        public int optStrikeSpeed;   // STRIKE SPEED (native 0.3.19): 0 REGULATION, 1 FAST, 2 FASTER
        public int optFoulShift;     // FOUL LINE (native 0.3.20): -2 .. +2 steps of 0.5 m, + LONGER (toward the centre)
        // ROOMS AND THEIR OPTIONS (native 0.3.21): SHIELDS ALWAYS ON (MAIDEN), AIM ASSIST (-1 the rules'), STRIKE ZONE, BALL
        // GROWTH, and ROUNDS (rounds to win, 1 to 5: BEST OF 1 to 9), BEST OF typed on a number pad (digits only)
        public bool optMaiden;
        public int optAim = -1, optZone = 1, optGrowth = 3, optRounds = 2;
        public int optShieldSize;   // SHIELD SIZE: 0 REGULATION .. 4 DOUBLE
        public int optBallFeel = Tunables.BALL_FEEL_DEFAULT;   // BALL FEEL: Tunables.BALL_* bits
        public bool optWallsLively, optWallGrip;   // BOUNCES (native 0.3.22): WALLS: LIVELY, WALL GRIP
        public int optBankAssist;                  // ... BANK ASSIST: 0 OFF, 1 LOW, 2 MEDIUM, 3 HIGH
        public bool roomBouncePage;                // ... its page
        public bool roundsPad;
        public string roundsBuf = "";
        public int optBotLevel = 2;  // the practice bot's level (Bot.Skill: ROOKIE 0 .. UNBEATABLE 5; task A8)
        public bool optBotAdapt;     // MATCH MY LEVEL (Tunables.botAdapt)
        public bool optBotMeet;      // FIST BUMP FIRST (Tunables.botMeet)
        public int optBotPlay;       // BOT STYLE: 0 BRAWLER, 1 DUELLIST (with the bot's level; Tunables.botPlay)
        // EXPERIMENTAL (native 0.3.12): its room (0 DIAMOND, 1 FOUR CORNERS, 2 FIREBALL BLITZ), FOUR CORNERS' teams, and
        // (hosting 3 v 1) THE ONE taking turns; FIREBALL BLITZ's HEALTH, BALLS (4 = RAPID FIRE) and PLAYERS (practice)
        public bool optExperimental, optOneTurns;
        public int optExpRoom, optTeams;
        public int optHealth = 5, optBalls = 3, optOctPlayers = 4;
        public static readonly string[] BallsName = { "", "ONE", "TWO", "THREE", "RAPID FIRE" };
        // MOVE PLAYERS (hosting four players): swap two corners, make one THE ONE; moveHost: its HAND OFF HOST form instead
        public bool movePlayersOpen, moveHost;
        public int moveSel = -1;   // the corner picked first (-1: none)
        public const int RoomsHere = Sim.Room.Count;   // the EXPERIMENTAL rooms offered (DIAMOND, FOUR CORNERS, FIREBALL BLITZ)
        // REPORT A BUG / REPORT PLAYER
        public int bugCat = -1, reportWhy = -1;
        public string bugNote = "", reportNote = "", reportName = "";
        public uint reportWho;
        bool bugReturnTitle;
        public Texture2D emblem;
        public string defaultServer = "";   // the community server of this build (servers.local.cfg, not in git)

        readonly AppState a;
        public Menu(AppState a) => this.a = a;

        Tunables Tun => a.game.tun;
        TrainingApp Tr => a.training ??= new TrainingApp(a);
        SoundStudio St => a.studio ??= new SoundStudio(a);
        static float StudioRowY(int k) => 0.125f - 0.052f * k;   // SOUND STUDIO's rows, an event each
        // TRAINING's lessons page: a column a track, a row a lesson
        static float TrainColX(int track) => -0.325f + 0.325f * track;
        static float TrainRowY(int place) => 0.165f - 0.0705f * place;
        // a page other than TRAINING's is open
        bool OtherPage() => titleOpen || settingsOpen || statsOpen || helpOpen || bugOpen || reportOpen || rulesOpen || xpOpen ||
                            a.statsNoticeOpen || movePlayersOpen;
        // TRAINING's own pages (a lesson's card and its result): no side menu, the court and the coach in view
        public bool TrainSide() => Tr.Active && !Tr.open && Tr.board == -2 && !OtherPage() &&
                                   (Tr.trainer.phase == Sim.Training.Phase.Card || Tr.trainer.phase == Sim.Training.Phase.Done);
        Online.Net net => a.net;
        Online.Net.Mode NetMode => net != null ? net.CurrentMode : Online.Net.Mode.Offline;

        public static string StrikeLabel(Tunables t)
        {
            string[] k = { "STRIKES: REGULATION", "STRIKES: FAST", "STRIKES: FASTER" };
            return k[Math.Max(0, Math.Min(2, t.strikeSpeed))];
        }

        // FOUL LINE: REGULATION, LONGER 1 / 2 (toward the centre: a longer way to throw), SHORTER 1 / 2
        public static string FoulLabel(Tunables t)
        {
            int s = Math.Max(-2, Math.Min(2, t.foulShift));
            return s == 0 ? "FOUL LINE: REGULATION" : "FOUL LINE: " + (s > 0 ? "LONGER " : "SHORTER ") + Math.Abs(s);
        }

        void OptionsFrom(Tunables t)
        {
            optAdvanced = t.advanced; optShields = t.shields; optKnuckle = t.knuckleMode;
            optMatchTime = t.basicMatchTime; optRoundTime = t.advancedRoundTime;
            optStrikeSpeed = t.strikeSpeed;
            optFoulShift = t.foulShift;
            optMaiden = t.maiden; optAim = t.aimAssist; optZone = t.strikeZone; optGrowth = t.ballGrowth;
            optRounds = t.advancedRoundsToWin;
            optShieldSize = t.shieldSize;
            optBallFeel = t.ballFeel & Tunables.BALL_FEEL_ALL;
            optWallsLively = t.wallsLively; optWallGrip = t.wallGrip; optBankAssist = t.bankAssist;
            optExperimental = t.experimental; optExpRoom = Math.Min(t.expRoom, RoomsHere - 1); optTeams = t.teamMode; optOneTurns = t.oneTurns;
            optHealth = t.health; optBalls = t.fireBalls; optOctPlayers = t.octPlayers;
            optBotLevel = t.botLevel; optBotAdapt = t.botAdapt; optBotMeet = t.botMeet;
            optBotPlay = t.botPlay;
        }

        void OptionsInto(Tunables t)
        {
            // (the SQUARE and DIAMOND play either match; the four-player rooms a timed one)
            t.advanced = optAdvanced && (!optExperimental || optExpRoom == Sim.Room.Diamond); t.shields = optShields; t.knuckleMode = optKnuckle;
            t.basicMatchTime = optMatchTime; t.advancedRoundTime = optRoundTime;
            t.strikeSpeed = Math.Max(0, Math.Min(2, optStrikeSpeed));
            t.foulShift = Math.Max(-2, Math.Min(2, optFoulShift));
            t.maiden = optMaiden && optShields;
            t.aimAssist = Math.Max(-1, Math.Min(3, optAim));
            t.strikeZone = Math.Max(0, Math.Min(2, optZone));
            t.ballGrowth = Math.Max(0, Math.Min(3, optGrowth));
            t.advancedRoundsToWin = Math.Max(1, Math.Min(5, optRounds));
            t.shieldSize = Math.Max(0, Math.Min(4, optShieldSize));
            t.ballFeel = optBallFeel & Tunables.BALL_FEEL_ALL;
            t.wallsLively = optWallsLively; t.wallGrip = optWallGrip; t.bankAssist = Math.Max(0, Math.Min(3, optBankAssist));
            t.experimental = optExperimental;
            t.expRoom = Math.Max(0, Math.Min(RoomsHere - 1, optExpRoom));
            t.players = t.experimental && Sim.Room.FourPlayer(t.expRoom) ? 4 : 2;
            t.health = optHealth; t.fireBalls = optBalls;
            t.botLevel = optBotLevel; t.botAdapt = optBotAdapt; t.botMeet = optBotMeet;
            t.botPlay = optBotPlay & 1;
            if (!rulesForHost) t.octPlayers = optOctPlayers;   // (practice only: a hosted room plays every corner, Game.octCap)
            t.teamMode = Math.Max(0, Math.Min(2, optTeams));
            t.oneTurns = optOneTurns && rulesForHost;
            if (!rulesForHost && (net == null || !net.InRoom)) t.theOne = 0;   // (practice 3 v 1: you are THE ONE; hosting, the room decides)
        }

        public void OptionsIntoGame() => OptionsInto(Tun);

        // ROOMS AND THEIR OPTIONS (native d4cf079; the owner's design, 2026-11-17): a room is chosen by its shape, then set up
        // with its own options. 0 the SQUARE (BASIC and ADVANCED), 1 DIAMOND, 2 FOUR CORNERS, 3 FIREBALL BLITZ
        public static int RoomOf(Tunables t) => t.experimental ? 1 + Math.Max(0, Math.Min(RoomsHere - 1, t.expRoom)) : 0;
        public static readonly string[] RoomTitle = { "SQUARE", "DIAMOND", "FOUR CORNERS", "FIREBALL BLITZ" };
        // the room's page: its options' rows (two to a row), then the bot's level just under them
        // (with BALL FEEL; BOUNCES, native 0.3.22: the SQUARE's seventh row, its rows closer, so the bot's level stays where it was)
        static int OptionRows(Tunables t) { int r = RoomOf(t); return r == 0 ? 7 : r == 1 ? 6 : 3; }
        static float OptionPitch(Tunables t) => OptionRows(t) > 6 ? 0.052f : 0.06f;
        public const int MaxBestOf = 9;   // ROUNDS: at most BEST OF 9 (the owner: a most we allow; Tunables: 5 rounds won)
        // the number typed on BEST OF's pad, when it can be one: odd (no ties), 1 to MaxBestOf; otherwise 0
        int PadBestOf()
        {
            if (roundsBuf.Length == 0 || roundsBuf.Length > 2) return 0;
            int n = int.Parse(roundsBuf);
            return n >= 1 && n <= MaxBestOf && (n & 1) != 0 ? n : 0;
        }
        static float BotRowY(Tunables t) => 0.165f - OptionPitch(t) * OptionRows(t) - 0.005f;
        public static readonly string[] AimName = { "OFF", "LOW", "MEDIUM", "HIGH" };
        public static readonly string[] ZoneName = { "SMALL", "REGULATION", "LARGE" };
        public static readonly string[] GrowthName = { "NONE", "QUARTER", "HALF", "REGULATION" };
        public static readonly string[] ShieldSizeName = { "REGULATION", "+25%", "+50%", "+75%", "DOUBLE" };
        // the room and its match as the boards and lists name them (the owner, 2026-11-17: no BASIC and ADVANCED on screen;
        // the SQUARE's two ways to play are its match: a TIMED MATCH, or BEST OF 3 rounds, first to 4)
        // ---- CUSTOM TRAINING (TASKS.md E33, native 3c285d9): MY OPTIONS, your own room options in the lessons
        // "AIM ASSIST HIGH - SHIELDS +50% - WALL GRIP": MY OPTIONS as a lesson plays them (a lesson is the SQUARE's, Basic)
        public static string MyOptionsLine(Tunables own)
        {
            Tunables t = own.Clone();
            t.experimental = false; t.advanced = false; t.matchVersion = Tunables.kVerAll;
            string s = "";
            void Add(string x) => s += (s.Length == 0 ? "" : " - ") + x;
            if (t.AimAssistNow() != 2) Add("AIM ASSIST " + AimName[t.AimAssistNow()]);
            if (t.ShieldSizeNow() != 0) Add("SHIELDS " + ShieldSizeName[t.ShieldSizeNow()]);
            if (t.maiden) Add("MAIDEN");
            if (t.StrikeZoneNow() != 1) Add(ZoneName[t.StrikeZoneNow()] + " STRIKE ZONE");
            if (t.StrikeSpeedNow() != 0) Add(t.StrikeSpeedNow() >= 2 ? "FASTER STRIKES" : "FAST STRIKES");
            if (t.BallGrowthNow() != 3) Add("BALL GROWTH " + GrowthName[t.BallGrowthNow()]);
            if (t.WallsLivelyNow()) Add("LIVELY WALLS");
            if (t.WallGripNow()) Add("WALL GRIP");
            if (t.BankAssistNow() > 0) Add("BANK ASSIST " + AimName[t.BankAssistNow()]);
            return s.Length == 0 ? "AS THE RULES SAY" : s;
        }
        // MY OPTIONS's page: the choices into your own rules at once (the same as your room's), and saved
        void ApplyMyOptions()
        {
            var t = new Tunables();
            OptionsInto(t);
            Tunables own = Tr.MyRules;
            own.aimAssist = t.aimAssist; own.shieldSize = t.shieldSize; own.maiden = optMaiden; own.strikeZone = t.strikeZone;
            own.strikeSpeed = t.strikeSpeed; own.ballGrowth = t.ballGrowth;
            own.wallsLively = t.wallsLively; own.wallGrip = t.wallGrip; own.bankAssist = t.bankAssist;
            a.settings.Save(a.RulesToSave);
        }

        public static string RoomName(Tunables t)
        {
            int r = RoomOf(t);
            if (r > 1) return RoomTitle[r];
            return RoomTitle[r] + (t.advanced ? " - BEST OF " + (2 * t.RoundsToWinNow() - 1) : " - TIMED MATCH");
        }

        // DEFAULTS (native 0.3.19, owner 2026-11-11): each settings page back as a fresh install has it, that page only,
        // pressed twice within 4 s (the first press arms it: RESET? PRESS AGAIN). Never: a person muted by name, the name,
        // the avatar, the stats, the server (its own USE DEFAULT); MATCH OPTIONS keeps the bot's level and which rules and
        // room you picked, and changes nothing until START / APPLY
        public const int DefSettings = 0, DefGraphics = 1, DefVoice = 2, DefPassthrough = 3, DefMatch = 4, DefControllers = 5, DefBall = 6, DefButtons = 7, DefBounces = 8, DefMyOpts = 9;
        string DefaultsLabel(int page)
        {
            bool armed = a.defaultsArmed == page && Time.realtimeSinceStartupAsDouble < a.defaultsArmedUntil;
            return armed ? (page == DefMatch ? "PRESS AGAIN" : "RESET? PRESS AGAIN") : "DEFAULTS";
        }
        void ApplyDefaults(int page)
        {
            var s = a.settings;
            var d = new AppSettings("");   // (a fresh install's values: AppSettings' own; nothing is read or written)
            switch (page)
            {
                case DefSettings:
                    s.volume = d.volume; s.hideOutside = d.hideOutside;   // (POINTER: on CONTROLLERS' own, native 0.3.20)
                    s.musicMenu = d.musicMenu; s.musicGame = d.musicGame; s.musicVolume = d.musicVolume;   // (MUSIC)
                    s.crowdMode = d.crowdMode; s.turnMode = d.turnMode;
                    if (net == null || !net.Inherited)
                    {   // (a room taken over keeps its own)
                        s.botFill = d.botFill;
                        if (net != null) net.BotFill = d.botFill;
                    }
                    break;
                case DefGraphics:   // (native's Discord status too: not in the template)
                    s.roomFeel = d.roomFeel; RoomFeelFeature.Step = s.roomFeel;
                    s.fxLevel = d.fxLevel;
                    s.gfxFoveation = d.gfxFoveation; s.gfxMsaa = d.gfxMsaa; s.gfxContact = d.gfxContact;   // (GRAPHICS: as AppSettings has them)
                    s.gfxGrade = d.gfxGrade; s.gfxBloom = d.gfxBloom; s.gfxReflect = d.gfxReflect;
                    s.lookAhead = d.lookAhead;
                    if (net != null) net.LookAhead = s.lookAhead;
                    break;
                case DefVoice:   // (anyone muted by name stays muted)
                    s.voiceMode = d.voiceMode;
                    s.muteSpectators = d.muteSpectators; s.mutePlayers = d.mutePlayers; s.muteAll = d.muteAll;
                    s.privateTalk = d.privateTalk;
                    s.ballSound = d.ballSound;   // (BALL SOUND: on)
                    if (net != null) net.PrivateTalk = s.privateTalk;
                    break;
                case DefControllers:   // (POINTER, and the standard angle)
                    s.pointerHand = d.pointerHand;
                    s.ctlTilt = s.ctlTwist = s.ctlTurn = 0;
                    break;
                case DefButtons:   // (BUTTONS: STANDARD, the pointer in the right hand)
                    s.buttons = Controls.Standard();
                    s.pointerHand = d.pointerHand;
                    break;
                case DefMyOpts:
                {   // (CUSTOM TRAINING's MY OPTIONS: the options a lesson takes, as the rules have them; in force at once)
                    var mt = new Tunables();
                    optAim = mt.aimAssist; optShieldSize = mt.shieldSize; optMaiden = mt.maiden; optZone = mt.strikeZone;
                    optStrikeSpeed = mt.strikeSpeed; optGrowth = mt.ballGrowth;
                    optWallsLively = mt.wallsLively; optWallGrip = mt.wallGrip; optBankAssist = mt.bankAssist;
                    break;
                }
                case DefBounces:
                {   // (the room's BOUNCES: as the rules have them; nothing changes until START / APPLY)
                    var bt = new Tunables();
                    optWallsLively = bt.wallsLively; optWallGrip = bt.wallGrip; optBankAssist = bt.bankAssist;
                    break;
                }
                case DefBall:   // (the room's BALL FEEL: the standard ball, the owner's pick; nothing changes until START / APPLY)
                    optBallFeel = Tunables.BALL_FEEL_DEFAULT;
                    break;
                case DefPassthrough:
                    s.ptMode = d.ptMode; s.ptFloor = d.ptFloor; s.ptGlass = d.ptGlass; s.ptRoom = d.ptRoom;
                    s.ptMono = d.ptMono; s.ptEdges = d.ptEdges; s.ptEdgeReveal = d.ptEdgeReveal; s.ptPeekOn = d.ptPeekOn;
                    break;
                case DefMatch:
                {   // the rules' own: as a fresh Tunables has them (Basic 3 min, Advanced untimed rounds, shields on, knuckle
                    // guards as the rules say, regulation strikes; FIREBALL BLITZ: its own, no shields, knuckle guards on)
                    var t = new Tunables();
                    optShields = t.shields; optKnuckle = t.knuckleMode;
                    optMatchTime = t.basicMatchTime; optRoundTime = t.advancedRoundTime;
                    optHealth = t.health; optBalls = t.fireBalls; optOctPlayers = t.octPlayers;
                    optTeams = t.teamMode; optOneTurns = t.oneTurns;
                    optStrikeSpeed = t.strikeSpeed;
                    optFoulShift = t.foulShift;
                    optMaiden = t.maiden; optAim = t.aimAssist; optZone = t.strikeZone; optGrowth = t.ballGrowth;   // (the rules')
                    optRounds = t.advancedRoundsToWin;
                    optShieldSize = t.shieldSize;
                    optBallFeel = t.ballFeel;   // (BALL FEEL: the standard ball)
                    optWallsLively = t.wallsLively; optWallGrip = t.wallGrip; optBankAssist = t.bankAssist;   // (BOUNCES)
                    if (optExperimental && optExpRoom == Sim.Room.Blitz) { optShields = false; optKnuckle = 1; }
                    break;
                }
            }
        }

        public static string TimeLabel(float secs, bool rounds)
        {
            if (secs <= 0f) return rounds ? "OFF (NO LIMIT)" : "FREE PLAY (NO LIMIT)";
            return $"{(int)secs / 60}:{(int)secs % 60:00}";
        }

        public int NavCurrent()
        {
            if (titleOpen || xpOpen) return (int)Nav.Home;
            if (a.InLobby && !open) return (int)Nav.Lobby;
            if (rulesOpen) return rulesForHost ? (int)Nav.Multi : (int)Nav.Practice;
            if (movePlayersOpen) return (int)Nav.Multi;
            if (settingsOpen) return (int)Nav.Settings;
            if (statsOpen) return (int)Nav.Stats;
            if (reportOpen) return (int)Nav.Report;
            if (bugOpen) return (int)Nav.Bug;
            if (helpOpen) return (int)Nav.Help;
            return NetMode == Online.Net.Mode.Spectate ? (int)Nav.Spectate : (int)Nav.Multi;
        }

        List<Online.Net.Person> People() => net != null ? net.PeopleHere() : new List<Online.Net.Person>();

        public List<Item> Items()
        {
            var v = ItemsBase();
            if (!nameEdit && !TrainSide())
            {   // the side menu: every area one click away
                var m = NetMode;
                bool offline = m == Online.Net.Mode.Offline || m == Online.Net.Mode.Searching;
                bool people = (net != null && net.InRoom) || m == Online.Net.Mode.Spectate;
                for (int i = 0; i < (int)Nav.Count; ++i)
                {
                    bool on = true;
                    if (i == (int)Nav.Spectate) on = net == null || !net.InRoom;
                    if (i == (int)Nav.Avatar) on = offline;
                    if (i == (int)Nav.Report) on = people;
                    if (i == (int)Nav.Quit) { v.Add(new Item(quitArmed ? "SURE? QUIT" : "QUIT GAME", NavX, NavTop - NavStep * i, 0.24f, 0.062f, Act.Quit, 0, true)); continue; }
                    if (i == (int)Nav.Lobby) on = !a.InLobby;
                    v.Add(new Item(NavLabels[i], NavX, NavTop - NavStep * i, 0.24f, 0.062f, Act.Nav, (uint)i, on));
                }
            }
            return v;
        }

        List<Item> ItemsBase()
        {
            var v = new List<Item>();
            var s = a.settings;
            if (a.statsNoticeOpen && !nameEdit)
            {   // the one-time notice before the first online match
                v.Add(new Item("OK", 0, -0.33f, 0.30f, 0.07f, Act.StatsNoticeOk, 0, true));
                return v;
            }
            if (Tr.board >= -1 && !nameEdit)
            {   // a TRAINING leaderboard
                bool timed = Tr.board >= 0 && Sim.Training.Lessons.All[Tr.board].Timed();
                if (timed)
                {
                    v.Add(new Item("FASTEST", -0.19f, -0.40f, 0.34f, 0.06f, Act.TrainKind, 0, Tr.boardKind != 0));
                    v.Add(new Item("MOST ACCURATE", 0.19f, -0.40f, 0.34f, 0.06f, Act.TrainKind, 1, Tr.boardKind != 1));
                }
                v.Add(new Item("BACK", 0, -0.48f, 0.30f, 0.06f, Act.TrainBoardBack, 0, true));
                return v;
            }
            if (Tr.open && Tr.optsPage && !nameEdit)
            {   // CUSTOM TRAINING: MY OPTIONS (your room's options a lesson takes)
                var t = new Tunables();
                OptionsInto(t);
                t.experimental = false; t.advanced = false;   // (a lesson is the SQUARE's, Basic)
                int n = 0;
                void Put(string label, Act act)
                {
                    v.Add(new Item(label, n % 2 != 0 ? 0.19f : -0.19f, 0.135f - 0.072f * (n / 2), 0.36f, 0.058f, act, 0, true));
                    ++n;
                }
                Put("AIM ASSIST: " + AimName[t.AimAssistNow()], Act.OptAim);
                Put("SHIELD SIZE: " + ShieldSizeName[t.ShieldSizeNow()], Act.OptShieldSize);
                Put(optMaiden ? "MAIDEN SHIELD: ON" : "MAIDEN SHIELD: OFF", Act.TrainOptMaiden);
                Put("STRIKE ZONE: " + ZoneName[t.StrikeZoneNow()], Act.OptZone);
                Put(StrikeLabel(t), Act.OptStrike);
                Put("BALL GROWTH: " + GrowthName[t.BallGrowthNow()], Act.OptGrowth);
                Put(optWallsLively ? "WALLS: LIVELY" : "WALLS: STANDARD", Act.BounceWalls);
                Put(optWallGrip ? "WALL GRIP: ON" : "WALL GRIP: OFF", Act.BounceGrip);
                Put("BANK ASSIST: " + AimName[Math.Max(0, Math.Min(3, optBankAssist))], Act.BounceAssist);
                bool std_ = MyOptionsLine(Tr.MyRules) == "AS THE RULES SAY";
                v.Add(new Item(std_ ? "AS THE RULES SAY" : "BACK TO STANDARD", -0.19f, -0.355f, 0.36f, 0.065f, Act.Defaults, DefMyOpts, !std_));
                v.Add(new Item("DONE", 0.19f, -0.355f, 0.36f, 0.065f, Act.TrainOptsDone, 0, true));
                return v;
            }
            if (Tr.open && !nameEdit)
            {   // the lessons, a column a track; each open once the one before is passed
                var Ls = Sim.Training.Lessons.All;
                for (int i = 0; i < Ls.Count; ++i)
                {
                    int place = Sim.Training.Lessons.PlaceInTrack(i, out _);
                    v.Add(new Item($"{place}  {Ls[i].name}", TrainColX((int)Ls[i].track), TrainRowY(place - 1), 0.30f, 0.05f, Act.TrainPick, (uint)i,
                                   Tr.Unlocked(i) && NetMode == Online.Net.Mode.Offline));
                }
                // (CUSTOM TRAINING: the mode; with MY OPTIONS, their page instead of the overall board, which is STANDARD's)
                if (Tr.mine) v.Add(new Item("SET MY OPTIONS", -0.27f, -0.46f, 0.25f, 0.055f, Act.TrainOpts, 0, true));
                else v.Add(new Item("LEADERBOARD", -0.27f, -0.46f, 0.25f, 0.055f, Act.TrainBoard, 0xFFFFFFFFu, true));
                v.Add(new Item(Tr.mine ? "MODE: MY OPTIONS" : "MODE: STANDARD", 0, -0.46f, 0.25f, 0.055f, Act.TrainMode, 0, true));
                v.Add(new Item("MAIN MENU", 0.27f, -0.46f, 0.25f, 0.055f, Act.TrainLeave, 0, true));
                return v;
            }
            if (a.studio != null && a.studio.on && !nameEdit && !OtherPage())
            {   // SOUND STUDIO (native 0.3.16): the sections, each event's choice, PLAY and the votes
                var S = StudioCatalog.Sections;
                for (int i = 0; i < S.Length; ++i)
                    v.Add(new Item(S[i].name, -0.3125f + 0.125f * i, 0.205f, 0.12f, 0.05f, Act.StudioSection, (uint)i, true));   // (the open one lit)
                var sec = S[St.section];
                for (int k = 0; k < sec.events.Length; ++k)
                {
                    float y = StudioRowY(k);
                    v.Add(new Item("<", -0.13f, y, 0.045f, 0.045f, Act.StudioPrev, (uint)k, true));
                    v.Add(new Item(">", 0.15f, y, 0.045f, 0.045f, Act.StudioNext, (uint)k, true));
                    v.Add(new Item("PLAY", 0.22f, y, 0.075f, 0.045f, Act.StudioPlay, (uint)k, true));
                    int cs = St.Chosen(sec.events[k]);
                    if (cs >= 0 && !St.sounds[cs].official && !St.sounds[cs].mine)
                    {   // a library sound: your vote, lit
                        v.Add(new Item("UP", 0.29f, y, 0.055f, 0.045f, Act.StudioVote, (uint)k | 0x100u, true));
                        v.Add(new Item("DOWN", 0.35f, y, 0.06f, 0.045f, Act.StudioVote, (uint)k, true));
                    }
                    else if (cs >= 0 && St.sounds[cs].mine)
                    {   // your own file: DELETE (press twice), to free the headset's space
                        bool armed = St.delArmed == St.sounds[cs].file && Time.realtimeSinceStartupAsDouble < St.delUntil;
                        v.Add(new Item(armed ? "SURE?" : "DELETE", 0.32f, y, 0.115f, 0.045f, Act.StudioDelete, (uint)k, true));
                    }
                }
                v.Add(new Item("HIDE THIS PAGE", -0.19f, -0.455f, 0.36f, 0.055f, Act.StudioMoment, 0, true));
                v.Add(new Item("MAIN MENU", 0.19f, -0.455f, 0.36f, 0.055f, Act.StudioLeave, 0, true));
                return v;
            }
            if (Tr.Active && !nameEdit && !OtherPage())
            {   // in a lesson: its card, its result, or (the menu button) a pause
                var tr = Tr.trainer;
                int i = tr.lesson;
                bool last = Sim.Training.Lessons.NextInTrack(i) < 0;
                bool video = TrainingApp.HaveVideo;
                if (tr.phase == Sim.Training.Phase.Card)
                {
                    v.Add(new Item("START", 0.19f, -0.25f, 0.36f, 0.08f, Act.TrainStart, 0, true));
                    v.Add(new Item(video ? "SHOW ME HOW" : "VIDEO COMING SOON", -0.19f, -0.25f, 0.36f, 0.08f, Act.TrainVideo, 0, video));
                    v.Add(new Item("TRAINING MENU", -0.19f, -0.355f, 0.36f, 0.06f, Act.TrainList, 0, true));
                    v.Add(new Item("LEADERBOARD", 0.19f, -0.355f, 0.36f, 0.06f, Act.TrainBoard, (uint)i, true));
                    v.Add(new Item("MAIN MENU", 0, -0.445f, 0.36f, 0.06f, Act.TrainLeave, 0, true));
                }
                else if (tr.phase == Sim.Training.Phase.Done)
                {
                    bool next = tr.result.passed && !last;
                    v.Add(new Item(next ? "NEXT LESSON" : "TRAINING MENU", 0.19f, -0.29f, 0.36f, 0.08f, next ? Act.TrainNext : Act.TrainList, 0, true));
                    v.Add(new Item("TRY AGAIN", -0.19f, -0.29f, 0.36f, 0.08f, Act.TrainRetry, 0, true));
                    v.Add(new Item("LEADERBOARD", -0.19f, -0.39f, 0.36f, 0.06f, Act.TrainBoard, (uint)i, true));
                    v.Add(new Item("TRAINING MENU", 0.19f, -0.39f, 0.36f, 0.06f, Act.TrainList, 0, next));
                    v.Add(new Item("MAIN MENU", 0, -0.47f, 0.36f, 0.06f, Act.TrainLeave, 0, true));
                }
                else
                {
                    v.Add(new Item("RESUME", 0, 0.10f, 0.56f, 0.08f, Act.Close, 0, true));
                    v.Add(new Item(video ? "SHOW ME HOW" : "VIDEO COMING SOON", 0, 0.00f, 0.56f, 0.08f, Act.TrainShow, 0, video));
                    v.Add(new Item("RESTART THE LESSON", 0, -0.10f, 0.56f, 0.08f, Act.TrainRetry, 0, true));
                    v.Add(new Item("TRAINING MENU", 0, -0.20f, 0.56f, 0.08f, Act.TrainList, 0, true));
                    v.Add(new Item("MAIN MENU", 0, -0.30f, 0.56f, 0.08f, Act.TrainLeave, 0, true));
                }
                return v;
            }
            if (statsOpen && !nameEdit)
            {   // MY STATS: the dashboard is drawn by DrawStats; here its one button
                bool armed = Time.realtimeSinceStartupAsDouble < a.deleteArmedUntil;
                v.Add(new Item(a.settings.recordOn ? "RECORD MY MATCHES: ON" : "RECORD MY MATCHES: OFF", 0.24f, -0.41f, 0.40f, 0.05f, Act.Record, 0, true));
                v.Add(new Item(armed ? "PRESS AGAIN TO DELETE" : "DELETE MY STATS", 0.24f, -0.495f, 0.40f, 0.055f, Act.DeleteStats, 0,
                               net != null && net.OnlineReady && a.myCareerOk && a.myCareer.known));
                return v;
            }
            if (bugOpen && !nameEdit)
            {
                for (int i = 0; i < 6; ++i)
                    v.Add(new Item(Reports.BugCats[i], (i % 2) != 0 ? 0.21f : -0.21f, 0.13f - 0.085f * (i / 2), 0.40f, 0.07f, Act.BugCat, (uint)i, true));
                string note = bugNote.Length == 0 ? "ADD A NOTE (OPTIONAL)" : "NOTE: " + bugNote;
                if (note.Length > 40) note = note.Substring(0, 37) + "...";
                v.Add(new Item(note, 0, -0.145f, 0.82f, 0.07f, Act.BugNote, 0, true));
                v.Add(new Item(Reports.Sending ? "SENDING..." : "SEND REPORT", 0.19f, -0.25f, 0.36f, 0.08f, Act.BugSend, 0, bugCat >= 0 && !Reports.Sending));
                v.Add(new Item("CANCEL", -0.19f, -0.25f, 0.36f, 0.08f, Act.BugCancel, 0, true));
                return v;
            }
            if (movePlayersOpen && moveHost && !nameEdit)
            {   // HAND OFF HOST (native 0.3.13): everyone in the room, a button each
                int i = 0;
                foreach (var p in People())
                {
                    if (p.role == Online.Net.VOICE_SPECTATOR || i >= 8) continue;
                    v.Add(new Item("MAKE " + p.name + " HOST", (i % 2) != 0 ? 0.21f : -0.21f, 0.15f - 0.085f * (i / 2), 0.40f, 0.07f,
                                   Act.HandOffTo, p.id, NetMode == Online.Net.Mode.Host));
                    ++i;
                }
                v.Add(new Item("BACK", 0, -0.30f, 0.36f, 0.08f, Act.MoveBack, 0, true));
                return v;
            }
            if (movePlayersOpen && !nameEdit)
            {   // MOVE PLAYERS (native 0.3.12): a row a corner (pick two to swap them), and THE ONE
                Game g = a.game;
                RoomInfo r = net.Room;
                // (the teams the room plays: FIREBALL BLITZ is everyone for themselves unless it can play in teams, whatever FOUR
                // CORNERS was last set to; TASKS.md E50, native 658b8f2, and E51, native 61589a0: Game.TeamsNow)
                bool one = g.TeamsNow() == Teams.ThreeVOne, host = NetMode == Online.Net.Mode.Host && g.np == 4;
                string[] colour = { "BLUE", "ORANGE", "GREEN", "VIOLET" };
                for (int c = 0; c < 4; ++c)
                {
                    string who = r.slot[c] != 0 ? r.NameOf(r.slot[c]) : null;
                    string label = $"{c + 1}  " + (r.slot[c] != 0 ? (!string.IsNullOrEmpty(who) ? who : "A PLAYER") : r.IsBot(c) || g.pl[c].isBot ? "BOT" : "EMPTY");
                    if (g.TeamsNow() == Teams.TwoVTwo) label += c < 2 ? "  - BLUE TEAM" : "  - ORANGE TEAM";
                    else if (one) label += c == g.tun.theOne ? "  - THE ONE" : "  - THE THREE";
                    else label += "  - " + colour[c];
                    v.Add(new Item(label, one ? -0.10f : 0, 0.15f - 0.085f * c, one ? 0.54f : 0.74f, 0.07f, Act.MoveCorner, (uint)c, host));
                    if (one) v.Add(new Item("MAKE THE ONE", 0.28f, 0.15f - 0.085f * c, 0.18f, 0.07f, Act.MoveOne, (uint)c, host && c != g.tun.theOne));
                }
                v.Add(new Item("BACK", 0, -0.30f, 0.36f, 0.08f, Act.MoveBack, 0, true));
                return v;
            }
            if (rulesOpen && rulesStep == 1 && roomBouncePage && !nameEdit)
            {   // the room's BOUNCES: its three options (nothing changes until START / APPLY)
                bool std_ = !optWallsLively && !optWallGrip && optBankAssist == 0;
                v.Add(new Item(optWallsLively ? "WALLS: LIVELY" : "WALLS: STANDARD", -0.19f, 0.12f, 0.36f, 0.075f, Act.BounceWalls, 0, true));
                v.Add(new Item(optWallGrip ? "WALL GRIP: ON" : "WALL GRIP: OFF", 0.19f, 0.12f, 0.36f, 0.075f, Act.BounceGrip, 0, true));
                v.Add(new Item("BANK ASSIST: " + AimName[Math.Max(0, Math.Min(3, optBankAssist))], 0, 0.02f, 0.40f, 0.075f, Act.BounceAssist, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.335f, 0.36f, 0.065f, Act.BounceBack, 0, true));
                v.Add(new Item(std_ ? "STANDARD BOUNCES" : "BACK TO STANDARD", 0.19f, -0.335f, 0.36f, 0.065f, Act.Defaults, DefBounces, !std_));
                return v;
            }
            if (rulesOpen && rulesStep == 1 && roomBallPage && !nameEdit)
            {   // the room's BALL FEEL: its four switches (nothing changes until START / APPLY)
                int f = optBallFeel;
                v.Add(new Item((f & Tunables.BALL_CORE) != 0 ? "CORE: SOLID" : "CORE: GLOW", -0.19f, 0.10f, 0.36f, 0.075f, Act.BallCore, 0, true));
                v.Add(new Item((f & Tunables.BALL_TRUE) != 0 ? "GLOW: TRUE SIZE" : "GLOW: WIDE", 0.19f, 0.10f, 0.36f, 0.075f, Act.BallTrue, 0, true));
                v.Add(new Item((f & Tunables.BALL_SPIN) != 0 ? "SPIN: VISIBLE" : "SPIN: SUBTLE", -0.19f, 0.00f, 0.36f, 0.075f, Act.BallSpin, 0, true));
                v.Add(new Item((f & Tunables.BALL_WEIGHT) != 0 ? "WEIGHT: HEAVY" : "WEIGHT: LIGHT", 0.19f, 0.00f, 0.36f, 0.075f, Act.BallWeight, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.335f, 0.36f, 0.065f, Act.BallBack, 0, true));
                v.Add(new Item(f == Tunables.BALL_FEEL_DEFAULT ? "STANDARD BALL" : "BACK TO STANDARD", 0.19f, -0.335f, 0.36f, 0.065f,
                               Act.Defaults, DefBall, f != Tunables.BALL_FEEL_DEFAULT));
                return v;
            }
            if (rulesOpen && rulesStep == 1 && roundsPad && !nameEdit)
            {   // BEST OF n typed: digits only, odd, 1 to MaxBestOf
                const string keys = "123456789";
                for (int k = 0; k < 9; ++k)
                    v.Add(new Item(keys[k].ToString(), -0.12f + 0.12f * (k % 3), 0.085f - 0.08f * (k / 3), 0.10f, 0.068f, Act.PadKey, keys[k], true));
                v.Add(new Item("DEL", -0.12f, -0.155f, 0.10f, 0.068f, Act.PadDel, 0, roundsBuf.Length > 0));
                v.Add(new Item("0", 0f, -0.155f, 0.10f, 0.068f, Act.PadKey, '0', roundsBuf.Length > 0));
                int nb = PadBestOf();
                v.Add(new Item("BACK", -0.19f, -0.335f, 0.36f, 0.065f, Act.PadCancel, 0, true));
                v.Add(new Item(nb > 0 ? "SET BEST OF " + nb : "SET", 0.19f, -0.335f, 0.36f, 0.065f, Act.PadOk, 0, nb > 0));
                return v;
            }
            if (rulesOpen && rulesStep == 1 && !nameEdit)
            {   // THE ROOM'S OPTIONS (ROOMS AND THEIR OPTIONS, native d4cf079)
                var t = new Tunables();
                OptionsInto(t);
                // practice, and a room where the bot fills in (native, owner 2026-10-29: FOUR CORNERS' bots): the bot's level too
                bool bot = !rulesForHost || (net != null ? net.BotFill : a.settings.botFill);   // (the room's: a room taken over keeps its own)
                int room = RoomOf(t);
                bool two = room <= 1;   // (the SQUARE and DIAMOND: every option; the four-player rooms theirs for now)
                int n = 0;   // the options two to a row, top down (the SQUARE and DIAMOND: the match first; the SQUARE's seven rows closer)
                float pitch = OptionPitch(t), bh = pitch - 0.008f;
                void Put(string label, Act act, uint ip = 0)
                {
                    v.Add(new Item(label, n % 2 != 0 ? 0.19f : -0.19f, 0.165f - pitch * (n / 2), 0.36f, bh, act, ip, true));
                    ++n;
                }
                void PutTime()
                {   // the clock in half a row: - TIME +
                    float x = n % 2 != 0 ? 0.19f : -0.19f, y = 0.165f - pitch * (n / 2);
                    bool rounds = t.advanced && room <= 1;
                    float secs = rounds ? t.advancedRoundTime : t.basicMatchTime;
                    string l = (rounds ? "ROUND: " : "TIME: ") + (secs > 0f ? $"{(int)secs / 60}:{(int)secs % 60:00}" : rounds ? "NO LIMIT" : "FREE PLAY");
                    v.Add(new Item("-", x - 0.155f, y, 0.05f, bh, Act.OptTime, 0, true));
                    v.Add(new Item(l, x, y, 0.24f, bh, Act.None, 0, true));
                    v.Add(new Item("+", x + 0.155f, y, 0.05f, bh, Act.OptTime, 1, true));
                    ++n;
                }
                string knuckles = t.KnuckleGuards() ? "KNUCKLES: ON" : "KNUCKLES: OFF";
                string shields = !t.shields ? "SHIELDS: OFF (HOTDOG)" : t.maiden && two ? "SHIELDS: ALWAYS (MAIDEN)" : "SHIELDS: ON";
                if (room <= 1)
                {   // the SQUARE and DIAMOND: the match, a TIMED MATCH or ROUNDS (BEST OF n: - / +, or typed), then all
                    int bo = 2 * t.RoundsToWinNow() - 1;
                    v.Add(new Item("TIMED MATCH", -0.28f, 0.165f, 0.18f, bh, Act.RulesPick, 0, true));
                    v.Add(new Item("ROUNDS", -0.09f, 0.165f, 0.18f, bh, Act.RulesPick, 1, true));
                    v.Add(new Item("-", 0.04f, 0.165f, 0.05f, bh, Act.OptRounds, 0, t.advanced && bo > 1));
                    v.Add(new Item("BEST OF " + bo, 0.19f, 0.165f, 0.24f, bh, Act.RoundsPad, 0, t.advanced));
                    v.Add(new Item("+", 0.34f, 0.165f, 0.05f, bh, Act.OptRounds, 1, t.advanced && bo < MaxBestOf));
                    n = 2;
                }
                if (room == 3)
                {   // FIREBALL BLITZ: KNUCKLES, HEALTH, BALLS, the TEAMS (0.3.24, as FOUR CORNERS'; TASKS.md E51), the clock
                    string[] teams = { "TEAMS: FREE-FOR-ALL", "TEAMS: 2 V 2", "3 V 1: YOU ARE THE ONE" };
                    Put(knuckles, Act.OptKnuckle);
                    Put("HEALTH: " + t.health + (t.teamMode == Teams.ThreeVOne ? " - THE ONE " + 3 * Math.Max(1, t.health) : ""), Act.OptHealth);   // (3 v 1: THE ONE three times as much)
                    Put("BALLS: " + BallsName[Math.Max(1, Math.Min(4, t.fireBalls))], Act.OptBalls);
                    Put(t.teamMode == Teams.ThreeVOne && t.oneTurns ? "3 V 1: ONE TAKES TURNS" : teams[Math.Max(0, Math.Min(2, t.teamMode))], Act.OptTeams);
                    PutTime();
                }
                else if (room == 2)
                {   // FOUR CORNERS: the teams (3 v 1: you are THE ONE; hosting, it can take turns instead, owner 2026-10-30)
                    string[] teams = { "TEAMS: FREE-FOR-ALL", "TEAMS: 2 V 2", "3 V 1: YOU ARE THE ONE" };
                    Put(shields, Act.OptShields);
                    Put(knuckles, Act.OptKnuckle);
                    Put(t.teamMode == Teams.ThreeVOne && t.oneTurns ? "3 V 1: ONE TAKES TURNS" : teams[Math.Max(0, Math.Min(2, t.teamMode))], Act.OptTeams);
                    PutTime();
                }
                else
                {   // the SQUARE and DIAMOND
                    Put(shields, Act.OptShields);
                    Put("SHIELD SIZE: " + ShieldSizeName[t.ShieldSizeNow()], Act.OptShieldSize);
                    Put(knuckles, Act.OptKnuckle);
                    Put("AIM ASSIST: " + AimName[t.AimAssistNow()], Act.OptAim);
                    Put("STRIKE ZONE: " + ZoneName[t.StrikeZoneNow()], Act.OptZone);
                    Put(StrikeLabel(t), Act.OptStrike);
                    Put("BALL GROWTH: " + GrowthName[t.BallGrowthNow()], Act.OptGrowth);
                    if (room == 0) Put(FoulLabel(t), Act.OptFoul);   // (DIAMOND's foul zone is its platform)
                    PutTime();
                }
                // BALL FEEL, every room's (the owner, 2026-11-17): how the ball looks and feels for everyone at the arena
                Put((t.ballFeel & Tunables.BALL_FEEL_ALL) == Tunables.BALL_FEEL_DEFAULT ? "BALL FEEL: STANDARD" : "BALL FEEL: CUSTOM", Act.BallPage);
                // BOUNCES (native 0.3.22, the SQUARE and DIAMOND; pull request #26's ideas as the host's options): its page
                if (two) Put(t.BouncesStandard() ? "BOUNCES: STANDARD" : "BOUNCES: CUSTOM", Act.BouncePage);
                string go = rulesEdit ? (a.RulesLive ? "APPLY FROM THE NEXT MATCH" : "APPLY") : rulesForHost ? "OPEN MY ARENA" : "START PRACTICE";
                if (bot)
                {   // - / +, the slider (one step a level, lit up to the one chosen), and MATCH MY LEVEL
                    string bl = t.botAdapt ? "BOT: MATCH MY LEVEL (NOW " + Bot.SkillAt(Tun.botMatch).name + ")" : "BOT: " + Bot.Skill(t.botLevel).name;
                    float yb = BotRowY(t);
                    if (room <= 1)
                    {   // (the SQUARE, DIAMOND: its style beside its level, any match: the owner, 11-17)
                        v.Add(new Item("-", -0.345f, yb, 0.05f, 0.058f, Act.OptBot, 0, t.botAdapt || t.botLevel > 0));
                        v.Add(new Item(bl, -0.155f, yb, 0.32f, 0.058f, Act.None, 0, true));
                        v.Add(new Item("+", 0.035f, yb, 0.05f, 0.058f, Act.OptBot, 1, t.botAdapt || t.botLevel < Bot.Levels - 1));
                        v.Add(new Item(t.botPlay != 0 ? "STYLE: DUELLIST" : "STYLE: BRAWLER", 0.235f, yb, 0.27f, 0.058f, Act.OptBotPlay, 0, true));
                    }
                    else
                    {
                        v.Add(new Item("-", -0.335f, yb, 0.07f, 0.058f, Act.OptBot, 0, t.botAdapt || t.botLevel > 0));
                        v.Add(new Item(bl, 0, yb, 0.56f, 0.058f, Act.None, 0, true));
                        v.Add(new Item("+", 0.335f, yb, 0.07f, 0.058f, Act.OptBot, 1, t.botAdapt || t.botLevel < Bot.Levels - 1));
                    }
                    for (int l = 0; l < Bot.Levels; ++l)
                        v.Add(new Item("", -0.33f + 0.07f * l, yb - 0.05f, 0.062f, 0.03f, Act.OptBot, (uint)(10 + l), true));
                    v.Add(new Item("MATCH MY LEVEL", 0.225f, yb - 0.05f, 0.29f, 0.03f, Act.OptBot, 2, true));
                    if (!rulesForHost && room == 3)   // (FIREBALL BLITZ practice: how many play, you and bots)
                        v.Add(new Item("PLAYERS: " + Math.Max(2, Math.Min(4, optOctPlayers)), -0.235f, -0.415f, 0.27f, 0.038f, Act.OptPlayers, 0, true));
                    else if (!rulesForHost) v.Add(new Item(t.botMeet ? "FIST BUMP FIRST: ON" : "FIST BUMP FIRST: OFF", -0.235f, -0.415f, 0.27f, 0.038f, Act.OptMeet, 0, true));
                    // (NEW BOT, 0.3.23, TASKS.md E55: EVERY MATCH or ON PUSH, and the push itself: a new one now, either way)
                    v.Add(new Item(a.settings.botEveryMatch ? "NEW BOT: EVERY MATCH" : "NEW BOT: ON PUSH", 0.065f, -0.415f, 0.31f, 0.038f, Act.BotEveryMatch, 0, true));
                    v.Add(new Item("NEW BOT NOW", 0.30f, -0.415f, 0.14f, 0.038f, Act.NewBot, 0, true));
                }
                if (rulesEdit && rulesForHost && NetMode == Online.Net.Mode.Host && a.game.np == 4)   // (who plays in which corner)
                    v.Add(new Item("MOVE PLAYERS", bot ? -0.235f : 0f, -0.415f, bot ? 0.27f : 0.46f, 0.038f, Act.MovePlayers, 0, true));
                v.Add(new Item(go, 0.19f, -0.36f, 0.36f, 0.06f, Act.OptGo, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.36f, 0.36f, 0.06f, Act.OptBack, 0, true));
                // (beside the title: the rules' own settings; the bot's level stays as you chose it)
                v.Add(new Item(DefaultsLabel(DefMatch), 0.355f, 0.29f, 0.15f, 0.05f, Act.Defaults, DefMatch, true));
                return v;
            }
            if (rulesOpen && !nameEdit)
            {   // CHOOSE A ROOM: each room's picture (Draw puts them on these)
                for (int k = 0; k < 4; ++k)
                    v.Add(new Item("", k % 2 != 0 ? 0.205f : -0.205f, k < 2 ? 0.07f : -0.195f, 0.39f, 0.25f, Act.RoomPick, (uint)k, true));
                v.Add(new Item("BACK", 0, -0.375f, 0.36f, 0.055f, Act.RulesBack, 0, true));
                return v;
            }
            if (reportOpen && !nameEdit)
            {
                var people = People();
                for (int i = 0; i < people.Count && i < 8; ++i)
                {
                    var p = people[i];
                    string l = p.name + (a.IsMuted(p.id) ? " - MUTED" : " (" + Reports.RoleText(p.role) + ")");
                    v.Add(new Item(l, (i % 2) != 0 ? 0.21f : -0.21f, 0.15f - 0.07f * (i / 2), 0.40f, 0.06f, Act.ReportWho, p.id, true));
                }
                bool here = false;
                foreach (var p in people) here |= p.id == reportWho;
                for (int i = 0; i < 4; ++i)
                    v.Add(new Item(Reports.ReportWhy[i], (i % 2) != 0 ? 0.21f : -0.21f, -0.15f - 0.07f * (i / 2), 0.40f, 0.06f, Act.ReportWhy, (uint)i, here));
                string note = reportNote.Length == 0 ? "ADD A NOTE (OPTIONAL)" : "NOTE: " + reportNote;
                if (note.Length > 40) note = note.Substring(0, 39) + ".";
                v.Add(new Item(note, 0, -0.30f, 0.82f, 0.06f, Act.ReportNote, 0, here));
                v.Add(new Item(here && a.IsMuted(reportWho) ? "UNMUTE" : "MUTE", -0.375f, -0.39f, 0.17f, 0.07f, Act.ReportMute, 0, here));
                // VOTE TO REMOVE: people in this arena room only (not spectators), never the host
                bool voted = a.kickVoted.Contains(reportWho);
                bool inRoomHere = false;
                if (net.InRoom) foreach (var p in people) inRoomHere |= p.id == reportWho && p.role != Online.Net.VOICE_SPECTATOR;
                if (NetMode == Online.Net.Mode.Host)   // (owner, 2026-10-21: the host has kick rights regardless of votes)
                    v.Add(new Item("REMOVE FROM ARENA", -0.135f, -0.39f, 0.29f, 0.07f, Act.ReportHostKick, 0, here && inRoomHere && reportWho != net.Session));
                else
                    v.Add(new Item(voted ? "VOTED" : "VOTE TO REMOVE", -0.135f, -0.39f, 0.29f, 0.07f, Act.ReportKick, 0,
                                   here && inRoomHere && !voted && reportWho != net.HostSession));
                v.Add(new Item(Reports.Sending ? "SENDING..." : "SEND REPORT", 0.135f, -0.39f, 0.23f, 0.07f, Act.ReportSend, 0, here && reportWhy >= 0 && !Reports.Sending));
                v.Add(new Item("BACK", 0.365f, -0.39f, 0.17f, 0.07f, Act.ReportBack, 0, true));
                return v;
            }
            if (helpOpen && creditsOpen)   // HOW TO PLAY > CREDITS
            {
                v.Add(new Item("BACK", 0, -0.33f, 0.30f, 0.07f, Act.CreditsBack, 0, true));
                return v;
            }
            if (helpOpen)
            {
                v.Add(new Item("GOT IT", -0.20f, -0.33f, 0.30f, 0.07f, Act.HelpDone, 0, true));
                v.Add(new Item("CREDITS", 0.20f, -0.33f, 0.30f, 0.07f, Act.Credits, 0, true));
                return v;
            }
            if (settingsOpen && ptPage)
            {   // PASSTHROUGH (native's page): the mode, two presets, the court and your room, and the room at edges / PEEK
                for (int pm = 0; pm < RoomPassthrough.Count; ++pm)
                    v.Add(new Item(RoomPassthrough.ModeName[pm], -0.30f + 0.20f * pm, 0.15f, 0.18f, 0.065f, Act.PtMode, (uint)pm, true));   // (the chosen one lit)
                v.Add(new Item("INDOOR PRESET", -0.19f, 0.075f, 0.36f, 0.06f, Act.PtPreset, 0, true));
                v.Add(new Item("OUTDOOR PRESET", 0.19f, 0.075f, 0.36f, 0.06f, Act.PtPreset, 1, true));
                bool on = s.ptMode != RoomPassthrough.Off;
                void Row(string label, float val, float y, int id, bool en)
                {
                    v.Add(new Item($"{label} {Mathf.RoundToInt(val * 100)}%", 0, y, 0.40f, 0.06f, Act.None, 0, en));
                    v.Add(new Item("-", -0.27f, y, 0.08f, 0.06f, Act.PtAdjust, (uint)(id * 2), en));
                    v.Add(new Item("+", 0.27f, y, 0.08f, 0.06f, Act.PtAdjust, (uint)(id * 2 + 1), en));
                }
                Row("COURT FLOOR", s.ptFloor, -0.005f, 0, on);
                Row("GLASS WALLS", s.ptGlass, -0.075f, 1, on && s.ptMode != RoomPassthrough.Outline);
                Row("ROOM BRIGHTNESS", s.ptRoom, -0.145f, 2, true);
                v.Add(new Item(s.ptMono ? "ROOM: GREY" : "ROOM: COLOUR", -0.19f, -0.22f, 0.36f, 0.06f, Act.PtMono, 0, true));
                v.Add(new Item(s.ptEdges ? "OBJECT EDGES: ON" : "OBJECT EDGES: OFF", 0.19f, -0.22f, 0.36f, 0.06f, Act.PtEdges, 0, true));
                v.Add(new Item(s.ptEdgeReveal ? "ROOM AT EDGES: ON" : "ROOM AT EDGES: OFF", -0.19f, -0.29f, 0.36f, 0.06f, Act.PtReveal, 0, true));
                int ps = Controls.FirstSlot(a.Buttons, Controls.J_PEEK, a.ctlKind);   // (PEEK's button: BUTTONS)
                if (ps < 0) v.Add(new Item("PEEK: NO BUTTON", 0.19f, -0.29f, 0.36f, 0.06f, Act.None, 0, false));
                else v.Add(new Item($"PEEK (HOLD {Controls.SlotName(a.ctlKind, ps)}): {(s.ptPeekOn ? "ON" : "OFF")}", 0.19f, -0.29f, 0.36f, 0.06f, Act.PtPeek, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.375f, 0.36f, 0.06f, Act.Back, 0, true));
                v.Add(new Item(DefaultsLabel(DefPassthrough), 0.19f, -0.375f, 0.36f, 0.06f, Act.Defaults, DefPassthrough, true));
                return v;
            }
            if (settingsOpen && ctlPage && !btnPage)   // (BUTTONS opens over it)
            {   // CONTROLLERS: POINTER, and CONTROLLER ANGLE (your hands follow it as you set it)
                v.Add(new Item(s.pointerHand == 1 ? "POINTER: RIGHT" : "POINTER: LEFT", -0.19f, 0.15f, 0.36f, 0.07f, Act.Hand, 0, true));
                v.Add(new Item("BUTTONS", 0.19f, 0.15f, 0.36f, 0.07f, Act.BtnPage, 0, true));   // (what each button does)
                string[] name = { "TILT", "TWIST", "TURN" };
                int[] val = { s.ctlTilt, s.ctlTwist, s.ctlTurn };
                for (int k = 0; k < 3; ++k)
                {
                    float y = -0.005f - 0.085f * k;
                    v.Add(new Item(val[k] == 0 ? name[k] + ": STANDARD" : $"{name[k]}: {(val[k] > 0 ? "+" : "")}{val[k]}", 0, y, 0.40f, 0.07f, Act.None, 0, true));
                    v.Add(new Item("-", -0.27f, y, 0.09f, 0.07f, Act.CtlAdj, (uint)(k * 2), val[k] > -AppSettings.CtlMax));
                    v.Add(new Item("+", 0.27f, y, 0.09f, 0.07f, Act.CtlAdj, (uint)(k * 2 + 1), val[k] < AppSettings.CtlMax));
                }
                v.Add(new Item("BACK", -0.19f, -0.385f, 0.36f, 0.055f, Act.Back, 0, true));
                v.Add(new Item(DefaultsLabel(DefControllers), 0.19f, -0.385f, 0.36f, 0.055f, Act.Defaults, DefControllers, true));
                return v;
            }
            if (settingsOpen && btnPage)
            {   // BUTTONS (Controls.cs): each button's job, the left hand's on the left
                Controls.Map bm = a.Buttons;
                int[][] col = { new[] { Controls.S_LGRIP, Controls.S_X, Controls.S_Y, Controls.S_LSTICK }, new[] { Controls.S_RGRIP, Controls.S_A, Controls.S_B, Controls.S_RSTICK } };
                for (int c = 0; c < 2; ++c)
                    for (int r = 0; r < 4; ++r)
                    {
                        int sl = col[c][r];
                        if (!Controls.OnController(a.ctlKind, sl)) continue;   // (one these controllers haven't: not shown)
                        v.Add(new Item(Controls.SlotName(a.ctlKind, sl) + ": " + Controls.JobName(bm.job[sl]), c != 0 ? 0.19f : -0.19f, 0.15f - 0.08f * r,
                                       0.36f, 0.065f, Act.BtnSlot, (uint)sl, true));
                    }
                v.Add(new Item(bm.leftSticks ? "SPECTATING: THE LEFT STICK TURNS" : "SPECTATING: THE RIGHT STICK TURNS", 0, -0.175f, 0.74f, 0.06f, Act.BtnSticks, 0, true));
                v.Add(new Item("STANDARD", -0.19f, -0.255f, 0.36f, 0.065f, Act.BtnPreset, 0, true));
                v.Add(new Item("LEFT-HANDED", 0.19f, -0.255f, 0.36f, 0.065f, Act.BtnPreset, 1, true));
                v.Add(new Item("BACK", -0.19f, -0.385f, 0.36f, 0.055f, Act.Back, 0, true));
                v.Add(new Item(DefaultsLabel(DefButtons), 0.19f, -0.385f, 0.36f, 0.055f, Act.Defaults, DefButtons, true));
                return v;
            }
            if (settingsOpen && gfxPage)
            {   // GRAPHICS: the effects (TASKS.md E38, native c97c364: two columns over the field of view, each this headset's
                // picture only), field of view (room size feel), hit effects and BALL LOOK-AHEAD (native's page)
                const float cx = 0.205f, cw = 0.39f, rh = 0.062f;
                bool fov = ArenaGraphics.HasFoveation || foveationShown;
                v.Add(new Item(fov ? "FOVEATION: " + ArenaGraphics.FovName[s.gfxFoveation & 3] : "FOVEATION: NOT ON THIS HEADSET", -cx, 0.215f, cw, rh, Act.GfxFoveation, 0, fov));
                v.Add(new Item("SMOOTH EDGES: " + ArenaGraphics.MsName[s.gfxMsaa % 3], cx, 0.215f, cw, rh, Act.GfxMsaa, 0, true));
                v.Add(new Item("CONTACT SHADOWS: " + ArenaGraphics.OnName[s.gfxContact % 3], -cx, 0.145f, cw, rh, Act.GfxContact, 0, true));
                v.Add(new Item("COLOR GRADING: " + ArenaGraphics.OnName[s.gfxGrade % 3], cx, 0.145f, cw, rh, Act.GfxGrade, 0, true));
                v.Add(new Item("BLOOM: " + ArenaGraphics.OnName[s.gfxBloom % 3], -cx, 0.075f, cw, rh, Act.GfxBloom, 0, true));
                v.Add(new Item("FLOOR REFLECTIONS: " + ArenaGraphics.OnName[s.gfxReflect % 3], cx, 0.075f, cw, rh, Act.GfxReflect, 0, true));
                v.Add(new Item(s.roomFeel == 0 ? "NORMAL" : $"{(s.roomFeel > 0 ? "+" : "")}{s.roomFeel * 10}%", 0, -0.105f, 0.20f, 0.075f, Act.None, 0, true));   // (10 % a step, +-50 %: owner 2026-10-30)
                v.Add(new Item("-", -0.17f, -0.105f, 0.09f, 0.075f, Act.FovDown, 0, s.roomFeel > -5));
                v.Add(new Item("+", 0.17f, -0.105f, 0.09f, 0.075f, Act.FovUp, 0, s.roomFeel < 5));
                string[] fx = { "HIT EFFECTS: FULL", "HIT EFFECTS: SOFT", "HIT EFFECTS: LOW" };
                v.Add(new Item(fx[s.fxLevel % 3], -cx, -0.275f, cw, 0.06f, Act.Fx, 0, true));
                // BALL LOOK-AHEAD (native 0.3.15): playing in someone else's room, the balls where the host will have them
                // (two-player rooms, and from 0.3.24 FOUR CORNERS and FIREBALL BLITZ with a host on 0.3.24 or later; in an older
                // host's four-player room it stands down, Game.LookAhead; TASKS.md E48, E49)
                bool la = net != null ? net.LookAhead : s.lookAhead;
                v.Add(new Item(la ? "BALL LOOK-AHEAD (ONLINE): ON" : "BALL LOOK-AHEAD (ONLINE): OFF", cx, -0.275f, cw, 0.06f, Act.LookAhead, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.39f, 0.36f, 0.055f, Act.Back, 0, true));
                v.Add(new Item(DefaultsLabel(DefGraphics), 0.19f, -0.39f, 0.36f, 0.055f, Act.Defaults, DefGraphics, true));
                return v;
            }
            if (settingsOpen && voicePage && mutedPage)
            {   // MUTED PLAYERS (TASKS.md E46, native 43397af): six a page, each with UNMUTE
                var ml = a.mutes.list;
                int n = ml.Count;
                mutedFirst = Math.Max(0, Math.Min(mutedFirst, (n - 1) / 6 * 6));
                for (int i = 0; i < 6 && mutedFirst + i < n; ++i)
                {
                    Mutes.Entry me = ml[mutedFirst + i];
                    float y = 0.13f - i * 0.085f;
                    v.Add(new Item(me.name.Length == 0 ? "PLAYER" : me.name, -0.12f, y, 0.54f, 0.07f, Act.None, 0, true));
                    v.Add(new Item("UNMUTE", 0.29f, y, 0.20f, 0.07f, Act.Unmute, (uint)(mutedFirst + i), true));
                }
                if (n > 6)
                {
                    v.Add(new Item("<", -0.33f, -0.385f, 0.10f, 0.055f, Act.MutedPrev, 0, mutedFirst > 0));
                    v.Add(new Item(">", 0.33f, -0.385f, 0.10f, 0.055f, Act.MutedNext, 0, mutedFirst + 6 < n));
                }
                v.Add(new Item("BACK", 0, -0.385f, 0.36f, 0.055f, Act.Back, 0, true));
                return v;
            }
            if (settingsOpen && voicePage)
            {
                string[] modes = { "VOICE: PUSH TO TALK", "VOICE: OPEN MIC", "VOICE: OFF" };
                v.Add(new Item(modes[s.voiceMode % 3], -0.205f, 0.12f, 0.39f, 0.08f, Act.VoiceMode, 0, true));
                {   // TALK BUTTON: TALK's button (+: more than one); it opens BUTTONS, where every button's job is chosen
                    Controls.Map bm = a.Buttons;
                    int ts = Controls.FirstSlot(bm, Controls.J_TALK, a.ctlKind);
                    string b = "TALK BUTTON: " + (ts < 0 ? "NONE" : Controls.SlotName(a.ctlKind, ts)) + (Controls.Count(bm, Controls.J_TALK, a.ctlKind) > 1 ? " +" : "");
                    v.Add(new Item(b, 0.205f, 0.12f, 0.39f, 0.08f, Act.BtnPage, 0, true));
                }
                v.Add(new Item(s.muteSpectators ? "SPECTATORS: MUTED" : "SPECTATORS: ON", -0.205f, 0.02f, 0.39f, 0.08f, Act.MuteSpec, 0, true));
                v.Add(new Item(s.mutePlayers ? "OTHER PLAYERS: MUTED" : "OTHER PLAYERS: ON", 0.205f, 0.02f, 0.39f, 0.08f, Act.MutePlayers, 0, true));
                // (BALL IN FLIGHT, 0.3.24, TASKS.md E52: hear the balls that can hurt you as they fly, louder as they come at you)
                v.Add(new Item(s.ballSound ? "BALL SOUND: ON - HEAR THE BALLS COMING AT YOU" : "BALL SOUND: OFF", 0, -0.08f, 0.80f, 0.08f, Act.BallSound, 0, true));
                v.Add(new Item(s.muteAll ? "MUTE ALL: ON" : "MUTE ALL: OFF", -0.205f, -0.18f, 0.39f, 0.08f, Act.MuteAll, 0, true));
                v.Add(new Item($"MUTED PLAYERS ({a.mutes.list.Count})", 0.205f, -0.18f, 0.39f, 0.08f, Act.MutedPage, 0, true));   // (TASKS.md E46)
                // (hosting: whether spectators hear the room; owner, 2026-11-02: practise your strategy where rivals can watch)
                bool priv = net != null ? net.PrivateTalk : s.privateTalk;
                v.Add(new Item(priv ? "ROOM TALK: PRIVATE (SPECTATORS DON'T HEAR IT)" : "ROOM TALK: OPEN (SPECTATORS HEAR IT)", 0, -0.28f, 0.80f, 0.07f, Act.PrivateTalk, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.385f, 0.36f, 0.055f, Act.Back, 0, true));
                v.Add(new Item(DefaultsLabel(DefVoice), 0.19f, -0.385f, 0.36f, 0.055f, Act.Defaults, DefVoice, true));
                return v;
            }
            if (settingsOpen)
            {
                v.Add(new Item($"MUSIC {s.musicVolume * 10}%", -0.19f, 0.215f, 0.20f, 0.065f, Act.None, 0, true));   // (MUSIC, native 1159a46)
                v.Add(new Item("-", -0.335f, 0.215f, 0.07f, 0.065f, Act.MusicDown, 0, s.musicVolume > 0));
                v.Add(new Item("+", -0.045f, 0.215f, 0.07f, 0.065f, Act.MusicUp, 0, s.musicVolume < 10));
                v.Add(new Item(s.musicMenu ? "MENU: ON" : "MENU: OFF", 0.10f, 0.215f, 0.17f, 0.065f, Act.MusicMenu, 0, true));
                v.Add(new Item(s.musicGame ? "GAME: ON" : "GAME: OFF", 0.28f, 0.215f, 0.17f, 0.065f, Act.MusicGame, 0, true));
                v.Add(new Item($"VOLUME {s.volume * 10}%", -0.19f, 0.13f, 0.20f, 0.075f, Act.None, 0, true));
                v.Add(new Item("-", -0.335f, 0.13f, 0.07f, 0.075f, Act.VolDown, 0, s.volume > 0));
                v.Add(new Item("+", -0.045f, 0.13f, 0.07f, 0.075f, Act.VolUp, 0, s.volume < 10));
                bool ptOk = PassthroughFeature.Ready;   // (made once the headset's session exists; N/A without it, or after a failure)
                v.Add(new Item(ptOk ? "PASSTHROUGH: " + RoomPassthrough.ModeName[s.ptMode % RoomPassthrough.Count] : "PASSTHROUGH: N/A",
                               0.19f, 0.13f, 0.36f, 0.075f, Act.Passthrough, 0, ptOk));
                v.Add(new Item("CONTROLLERS", -0.19f, 0.04f, 0.36f, 0.075f, Act.CtlPage, 0, true));   // (POINTER and the angle, native 0.3.20)
                bool fills = net != null ? net.BotFill : s.botFill;   // (the room's: a room taken over keeps its own)
                v.Add(new Item(fills ? "BOT FILLS IN: ON" : "BOT FILLS IN: OFF", 0.19f, 0.04f, 0.36f, 0.075f, Act.BotFill, 0, true));
                v.Add(new Item("GRAPHICS AND VIEW", -0.19f, -0.05f, 0.36f, 0.075f, Act.GfxPage, 0, true));   // (native 0.3.13: HIT EFFECTS moved there)
                v.Add(new Item(s.hideOutside ? "OUTSIDE: HIDDEN" : "OUTSIDE: SHOWN", 0.19f, -0.05f, 0.36f, 0.075f, Act.HideOutside, 0, true));
                v.Add(new Item("RULES: " + Scoreboard.RulesLine(Tun), -0.19f, -0.14f, 0.36f, 0.075f, Act.Rules, 0,
                               NetMode != Online.Net.Mode.Client && NetMode != Online.Net.Mode.Spectate));
                v.Add(new Item("VOICE AND MUTES", 0.19f, -0.14f, 0.36f, 0.075f, Act.VoicePage, 0, true));
                // The Crowd (fans on phones): flags in the stands and the ends' flaring lights (no sound from the fans since
                // 0.3.23, TASKS.md E53: FLAGS AND LIGHTS, or OFF)
                v.Add(new Item(s.crowdMode < 2 ? "CROWD: FLAGS AND LIGHTS" : "CROWD: OFF", -0.19f, -0.23f, 0.36f, 0.075f, Act.Crowd, 0, true));
                string[] turn = { "TURNING: SNAP", "TURNING: SMOOTH", "TURNING: SMOOTH FAST" };
                v.Add(new Item(turn[s.turnMode % 3], 0.19f, -0.23f, 0.36f, 0.075f, Act.Turn, 0, false));   // (the spectator's flying: later)
                // (the build's own server address is not shown: COMMUNITY (DEFAULT); one you typed is)
                bool custom = defaultServer.Length > 0 && s.server != defaultServer;
                string sv = string.IsNullOrEmpty(s.server) ? "SERVER: NOT SET" : custom || defaultServer.Length == 0 ? "SERVER: " + s.server : "SERVER: COMMUNITY (DEFAULT)";
                if (sv.Length > 34) sv = sv.Substring(0, 33) + ".";
                v.Add(new Item(sv, custom ? -0.12f : 0f, -0.31f, custom ? 0.50f : 0.74f, 0.065f, Act.Server, 0, true));
                if (custom) v.Add(new Item("USE DEFAULT", 0.26f, -0.31f, 0.22f, 0.065f, Act.ServerDefault, 0, true));
                v.Add(new Item("BACK", -0.19f, -0.385f, 0.36f, 0.055f, Act.Back, 0, true));
                v.Add(new Item(DefaultsLabel(DefSettings), 0.19f, -0.385f, 0.36f, 0.055f, Act.Defaults, DefSettings, true));
                return v;
            }
            // (the keyboard before the title page: natively the title page came first, and CHANGE NAME on it showed no
            // keyboard; reported to the owner)
            if (nameEdit)
            {   // keyboard: four rows of keys, then SPACE / DELETE / DONE
                string[] rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL-", "ZXCVBNM.:" };
                for (int r = 0; r < 4; ++r)
                    for (int c = 0; c < rows[r].Length; ++c)
                    {
                        float x = -0.3825f + 0.085f * c + (r == 3 ? 0.085f * 0.5f : 0f);
                        v.Add(new Item(rows[r][c].ToString(), x, 0.10f - 0.09f * r, 0.075f, 0.075f, Act.Key, rows[r][c], true));
                    }
                v.Add(new Item("SPACE", -0.25f, -0.27f, 0.30f, 0.07f, Act.Key, ' ', true));
                v.Add(new Item("DELETE", 0.06f, -0.27f, 0.24f, 0.07f, Act.KeyDel, 0, nameBuf.Length > 0));
                v.Add(new Item("DONE", 0.31f, -0.27f, 0.20f, 0.07f, Act.KeyDone, 0, true));
                return v;
            }
            if (titleOpen)
            {
                v.Add(new Item("PLAY PRACTICE", -0.145f, 0.08f, 0.27f, 0.085f, Act.Play, 0, true));
                v.Add(new Item("TRAINING", 0.145f, 0.08f, 0.27f, 0.085f, Act.Train, 0, NetMode == Online.Net.Mode.Offline));
                v.Add(new Item("MULTIPLAYER", 0, -0.02f, 0.56f, 0.085f, Act.Multi, 0, true));
                v.Add(new Item("CUSTOMIZE AVATAR", -0.145f, -0.12f, 0.27f, 0.085f, Act.Avatar, 0, true));
                v.Add(new Item("SOUND STUDIO", 0.145f, -0.12f, 0.27f, 0.085f, Act.Studio, 0, NetMode == Online.Net.Mode.Offline));   // (native 0.3.16)
                v.Add(new Item("SETTINGS", -0.145f, -0.22f, 0.27f, 0.07f, Act.Settings, 0, true));
                v.Add(new Item("HOW TO PLAY", 0.145f, -0.22f, 0.27f, 0.07f, Act.Help, 0, true));
                v.Add(new Item("CHANGE NAME", 0, -0.31f, 0.56f, 0.07f, Act.Name, 0, true));
                // (native 0.3.17: the unfinished cross-play test, EXPERIMENTAL, is off the title page; not ported)
                v.Add(new Item("REPORT A BUG", 0, -0.385f, 0.56f, 0.045f, Act.Bug, 0, true));
                return v;
            }
            var m = NetMode;
            bool inGame = m == Online.Net.Mode.Host || m == Online.Net.Mode.Client;
            const float lx = -0.25f, rx = 0.25f, bw = 0.42f;
            bool spec = m == Online.Net.Mode.Spectate;
            if (spec)
            {   // spectators: their own column (no hosting, avatar or spectate entries)
                v.Add(new Item("STOP SPECTATING", lx, 0.12f, bw, 0.08f, Act.Leave, 0, true));
                v.Add(new Item(s.headSelfie ? "SELFIE: ON" : "SELFIE: OFF", rx, 0.175f, bw, 0.06f, Act.HeadSelfie, 0, true));   // (native 0.3.13)
                v.Add(new Item("BACK TO HUB", lx, 0.02f, bw, 0.08f, Act.Hub, 0, a.watchIp != 0));
                v.Add(new Item("DIRECTOR TOOLS", lx, -0.08f, bw, 0.08f, Act.Director, 0, a.watchIp != 0 && a.director != null));
                v.Add(new Item("SETTINGS", lx, -0.17f, bw, 0.08f, Act.Settings, 0, true));
                v.Add(new Item("CLEAR CAMERAS", lx, -0.26f, bw, 0.08f, Act.ClearWarps, 0, a.director != null && a.director.warps.Count > 0));
                v.Add(new Item("CLOSE MENU", lx, -0.35f, bw, 0.06f, Act.Close, 0, true));
                v.Add(new Item("CHANGE NAME", rx, -0.35f, bw, 0.06f, Act.Name, 0, true));
                bool full = false;
                foreach (var lg in net.Games()) if (lg.ip == a.watchIp) full = lg.full;
                v.Add(new Item(full ? "ARENA FULL" : "JOIN THIS ARENA", rx, -0.17f, bw, 0.07f, Act.Join, a.watchIp, a.watchIp != 0 && !full));
                v.Add(new Item(net.Online ? "ONLINE: ON" : "ONLINE: OFF (LAN)", rx, -0.26f, bw, 0.06f, Act.Online, 0, true));
            }
            else
            {
                if (net != null && net.LocalWaiting())
                    v.Add(new Item(net.LocalQueuePos() < 0 ? "JOIN QUEUE" : "LEAVE QUEUE", lx, 0.12f, bw, 0.08f, Act.Queue, 0, true));
                else if (inGame)
                    v.Add(new Item("FORFEIT MATCH", lx, 0.12f, bw, 0.08f, Act.Forfeit, 0, true));
                else
                    v.Add(new Item(m == Online.Net.Mode.Host ? "HOSTING" : "HOST A ROOM", lx, 0.12f, bw, 0.08f, Act.Host, 0, m != Online.Net.Mode.Host && net != null));
                v.Add(new Item(inGame ? "LEAVE GAME" : "PRACTICE VS BOT", lx, 0.02f, bw, 0.08f, Act.Leave, 0, inGame));
                v.Add(new Item("SETTINGS", lx, -0.08f, bw, 0.08f, Act.Settings, 0, true));
                v.Add(new Item("SPECTATE GAMES", lx, -0.17f, bw, 0.08f, Act.Spectate, 0, !inGame && net != null));
                if (m == Online.Net.Mode.Host)
                {   // HOST HANDOFF (native 0.3.13): someone else hosts the room from now on. In CUSTOMIZE AVATAR's place
                    // (never open while hosting): over the games list it covered GAMES ONLINE (owner's report, 2026-11-02)
                    bool anyone = false;
                    foreach (var p in People()) anyone |= p.role != Online.Net.VOICE_SPECTATOR;
                    v.Add(new Item("HAND OFF HOST", lx, -0.26f, bw, 0.08f, Act.HandOff, 0, anyone));
                }
                else v.Add(new Item("CUSTOMIZE AVATAR", lx, -0.26f, bw, 0.08f, Act.Avatar, 0, m == Online.Net.Mode.Offline || m == Online.Net.Mode.Searching));
                v.Add(new Item("CLOSE MENU", lx, -0.35f, bw, 0.06f, Act.Close, 0, true));
                v.Add(new Item("CHANGE NAME", rx, -0.35f, bw, 0.06f, Act.Name, 0, true));
                v.Add(new Item(net != null && net.Online ? "ONLINE: ON" : "ONLINE: OFF (LAN)", rx, -0.26f, bw, 0.06f, Act.Online, 0, !inGame && net != null));
            }
            if (net != null)
            {
                var games = net.Games();
                for (int i = 0; i < games.Count && i < (spec ? 3 : 4); ++i)
                {   // spectators: JOIN THIS ARENA sits below
                    var lg = games[i];
                    bool mine = m == Online.Net.Mode.Client && lg.ip == net.PeerIp;
                    if (spec)
                    {
                        string b = $"{lg.name}  {(lg.advanced ? "ROUNDS" : "TIMED")}{(lg.shields ? "" : " HOTDOG")}  {lg.score0}-{lg.score1}{(lg.ip == a.watchIp ? "  (WATCHING)" : "")}";
                        v.Add(new Item(b, rx, 0.08f - 0.09f * i, bw, 0.07f, Act.Watch, lg.ip, lg.ip != a.watchIp));
                    }
                    else
                    {
                        // (and its EXPERIMENTAL room, so DIAMOND and FOUR CORNERS rooms say so)
                        var lt = new Tunables { advanced = lg.advanced, shields = lg.shields, knuckleMode = lg.knuckleMode, basicMatchTime = lg.matchTime, advancedRoundTime = lg.roundTime,
                                                experimental = lg.experimental, expRoom = lg.expRoom, teamMode = lg.teamMode, players = lg.players };
                        string b = $"JOIN  {lg.name}  {Scoreboard.RulesLine(lt)}{(mine ? "  (IN)" : lg.full ? "  FULL" : "")}";
                        v.Add(new Item(b, rx, 0.08f - 0.09f * i, bw, 0.07f, Act.JoinRoom, lg.ip, !lg.full && !mine));   // into the room's waiting area
                    }
                }
            }
            return v;
        }

        // native openMenu: in front of you, 0.9 m out and a little down; in the lobby always in the middle of the balcony,
        // facing it, 1.3 m out (native 1159a46; the owner, 2026-11-21: at 0.95 m it was "too close to you and fills up the
        // entire screen")
        public void Open(Pose head, Vec3 lobbySpot)
        {
            Vec3 f = head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(1, 0, 0);
            Vec3 at = f;   // (in front of you, SOUND STUDIO too: native 0.3.19, owner 2026-11-12: off to the left was off-putting)
            float yaw = Mathf.Atan2(-at.x, -at.z);   // panel +Z faces the player
            pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), yaw), head.p + at * 0.9f + new Vec3(0, -0.15f, 0));
            if (a.InLobby) pose = new Pose(Quat.Identity, new Vec3(lobbySpot.x, head.p.y - 0.22f, lobbySpot.z - 1.3f));
            open = true;
            prevTrig = true;   // ignore a trigger that is already held
            Debug.Log("[Menu] open");
        }

        // the one-time statistics notice, straight ahead of you (native updateStats)
        public void OpenNotice(Pose head)
        {
            open = true;
            titleOpen = settingsOpen = statsOpen = reportOpen = bugOpen = helpOpen = rulesOpen = false;
            pose = new Pose(head.q, head.Transform(new Vec3(0, -0.05f, -0.95f)));
            prevTrig = true;
        }

        public void Close()
        {
            open = false; nameEdit = false; helpOpen = false; settingsOpen = false; titleOpen = false; quitArmed = false; rulesOpen = false;
            xpOpen = false; voicePage = false; gfxPage = false; ptPage = false; ctlPage = false; roomBallPage = false; roomBouncePage = false; btnPage = false; movePlayersOpen = false; moveHost = false; moveSel = -1;
            Debug.Log("[Menu] closed");
        }

        // the rules chosen (native applyRules): during a match against a person they wait for the next match
        void ApplyRules()
        {
            if (a.RulesLive)
            {
                a.rulesPending = true;
                var t = new Tunables();
                OptionsInto(t);
                string n = "NEXT MATCH: " + Scoreboard.RulesLine(t);
                net.NoticeLocal(n.Length > 47 ? n.Substring(0, 47) : n);
                return;
            }
            OptionsInto(Tun);
            a.game.Reset(a.game.pl[1].isBot);
            a.settings.Save(a.RulesToSave);
        }

        public void Activate(Item it)
        {
            Debug.Log("[Menu] " + it.label);
            if (it.act != Act.Quit) quitArmed = false;
            var s = a.settings;
            double now = Time.realtimeSinceStartupAsDouble;
            switch (it.act)
            {
                case Act.Quit:
                    if (!quitArmed) { quitArmed = true; break; }   // the second press quits
                    s.Save(a.RulesToSave);
                    if (net != null && net.InRoom) net.Leave();
                    a.quitRequested = true;
                    break;
                case Act.Nav:
                    titleOpen = settingsOpen = voicePage = xpOpen = bugOpen = reportOpen = rulesOpen = helpOpen = false;
                    gfxPage = ptPage = ctlPage = roomBallPage = roomBouncePage = btnPage = false;
                    statsOpen = false; movePlayersOpen = false; moveHost = false; moveSel = -1;
                    Tr.open = false; Tr.board = -2;
                    nameEdit = false;
                    switch ((Nav)it.ip)
                    {
                        case Nav.Lobby:   // leave whatever you are in; the frame loop puts the bots back on the court
                            if (net != null && NetMode != Online.Net.Mode.Offline) net.Leave();
                            a.lobby = true;
                            open = false;
                            break;
                        case Nav.Home: titleOpen = true; break;
                        case Nav.Practice: Go(Act.Play); break;
                        case Nav.Multi: break;   // the multiplayer page (rooms; the spectator's page while spectating)
                        case Nav.Spectate: if (NetMode != Online.Net.Mode.Spectate) Go(Act.Spectate); break;
                        case Nav.Avatar: Go(Act.Avatar); break;
                        case Nav.Settings: settingsOpen = true; break;
                        case Nav.Stats: statsOpen = true; a.myStatsAskAt = 0; a.deleteArmedUntil = 0; break;
                        case Nav.Report: Go(Act.Report); break;
                        case Nav.Bug: Go(Act.Bug); break;
                        case Nav.Help: helpOpen = true; creditsOpen = false; break;
                    }
                    break;
                case Act.MainMenu: titleOpen = true; settingsOpen = false; break;
                // TRAINING (native menuActivate's Train...)
                case Act.Train: titleOpen = false; Tr.open = true; Tr.optsPage = false; break;
                // CUSTOM TRAINING (native 0.3.22): STANDARD or MY OPTIONS (saved), and MY OPTIONS's page (its buttons: the
                // room page's own, OptAim and the rest, on choices taken from your rules; a lesson is Basic, so AIM ASSIST's
                // "as the rules say" is MEDIUM)
                case Act.TrainMode: Tr.mine = !Tr.mine; s.trainMine = Tr.mine; s.Save(a.RulesToSave); break;
                case Act.TrainOpts: OptionsFrom(Tr.MyRules); optAdvanced = false; Tr.optsPage = true; break;
                case Act.TrainOptsDone: ApplyMyOptions(); OptionsFrom(Tr.MyRules); Tr.optsPage = false; break;
                case Act.TrainOptMaiden: optMaiden = !optMaiden; break;
                case Act.TrainPick: Tr.BeginLesson((int)it.ip, false, this, Tr.mine); break;
                case Act.TrainStart: Tr.trainer.Start(a.game); open = false; break;
                case Act.TrainRetry: Tr.BeginLesson(Tr.trainer.lesson, true, this, Tr.trainer.custom); break;
                case Act.TrainNext:
                    if (Sim.Training.Lessons.NextInTrack(Tr.trainer.lesson) >= 0) Tr.BeginLesson(Sim.Training.Lessons.NextInTrack(Tr.trainer.lesson), false, this, Tr.trainer.custom);
                    break;
                case Act.TrainList: Tr.open = true; Tr.optsPage = false; Tr.board = -2; break;
                case Act.TrainLeave:   // MAIN MENU: out of training (to the lobby), the main menu open
                    Tr.open = false;
                    Tr.board = -2;
                    Tr.menuAt = 0;
                    if (Tr.Active)
                    {
                        Tr.Stop();
                        a.lobby = true;
                        Open(a.localIn.head, Lobby.Spot);
                    }
                    titleOpen = true;
                    break;
                case Act.TrainBoard:
                    Tr.board = it.ip == 0xFFFFFFFFu ? -1 : (int)it.ip;
                    Tr.boardKind = Tr.board >= 0 && Sim.Training.Lessons.All[Tr.board].Timed() ? 0 : 1;
                    Tr.AskBoard();
                    break;
                case Act.TrainKind: Tr.boardKind = (int)it.ip; Tr.AskBoard(); break;
                case Act.TrainBoardBack: Tr.board = -2; break;
                case Act.TrainShow: case Act.TrainVideo: break;
                case Act.Record:   // RECORD MY MATCHES (Sim/Record.cs): on this headset only
                    a.settings.recordOn = !a.settings.recordOn;
                    a.settings.Save(a.RulesToSave);   // (in a lesson: your own rules, not the lesson's)
                    net?.NoticeLocal(a.settings.recordOn ? "RECORDING YOUR MATCHES - THEY STAY ON THIS HEADSET" : "NOT RECORDING YOUR MATCHES");
                    Debug.Log("[Menu] record my matches: " + (a.settings.recordOn ? "on" : "off"));
                    break;   // (SHOW ME HOW: no lesson videos in the open assets yet)
                case Act.DeleteStats:   // two presses within 4 s
                    if (now < a.deleteArmedUntil)
                    {
                        net.DeleteMyStats();
                        a.deleteArmedUntil = 0;
                        a.myCareer = default;
                        a.myCareerOk = false;
                        a.myStatsAskAt = now + 1.0;
                        net.NoticeLocal("YOUR STATS ARE DELETED");
                    }
                    else a.deleteArmedUntil = now + 4.0;
                    break;
                case Act.Defaults:   // two presses within 4 s, on the same page (native 0.3.19)
                    if (a.defaultsArmed == (int)it.ip && now < a.defaultsArmedUntil)
                    {
                        ApplyDefaults((int)it.ip);
                        a.defaultsArmed = -1;
                        a.defaultsArmedUntil = 0;
                        if (it.ip != DefMatch) s.Save(a.RulesToSave);   // (MATCH OPTIONS: nothing changes until START / APPLY)
                        net?.NoticeLocal(it.ip == DefMatch ? "THE RULES' OWN SETTINGS - PRESS APPLY OR START" : "BACK TO THE DEFAULTS");
                        Debug.Log("[Menu] defaults on page " + it.ip);
                    }
                    else { a.defaultsArmed = (int)it.ip; a.defaultsArmedUntil = now + 4.0; }
                    break;
                case Act.Bug:
                    bugReturnTitle = titleOpen; titleOpen = false; settingsOpen = false; xpOpen = false;
                    bugOpen = true; bugCat = -1; bugNote = "";
                    break;
                case Act.BugCat: bugCat = (int)it.ip; break;
                case Act.BugNote: nameEdit = true; editTarget = 4; nameBuf = bugNote; break;
                case Act.BugSend:
                    Reports.Submit(a, Reports.Text(a, "Project Arena bug report", null, bugCat, bugNote),
                                   $"bug report: {(bugCat >= 0 ? Reports.BugCats[bugCat] : "-")}, \"{bugNote}\"");
                    bugOpen = false; titleOpen = bugReturnTitle;
                    break;
                case Act.BugCancel: bugOpen = false; titleOpen = bugReturnTitle; break;
                case Act.Report:
                    settingsOpen = false; voicePage = false; reportOpen = true;
                    reportWho = 0; reportName = ""; reportWhy = -1; reportNote = "";
                    break;
                case Act.ReportWho:
                    reportWho = it.ip;
                    foreach (var p in People()) if (p.id == it.ip) reportName = p.name;
                    break;
                case Act.ReportWhy: reportWhy = (int)it.ip; break;
                case Act.ReportNote: nameEdit = true; editTarget = 5; nameBuf = reportNote; break;
                case Act.ReportMute:
                    a.mutes.Toggle(reportWho, reportName, net, a.mutedIds);   // (kept on this headset: MUTED PLAYERS, TASKS.md E46)
                    Debug.Log($"[Voice] {reportName} {(a.IsMuted(reportWho) ? "muted" : "unmuted")}");
                    break;
                case Act.ReportKick:
                    if (net.VoteKick(reportWho))
                    {
                        a.kickVoted.Add(reportWho);
                        string msg = $"YOUR VOTE TO REMOVE {reportName} IS IN";
                        net.NoticeLocal(msg.Length > 47 ? msg.Substring(0, 47) : msg);
                    }
                    break;
                case Act.ReportHostKick:
                    if (net.HostKick(reportWho)) { reportOpen = false; open = false; }
                    break;
                case Act.StatsNoticeOk: a.statsNoticeOpen = false; s.statsNoticeSeen = true; open = false; s.Save(a.RulesToSave); break;
                case Act.ReportSend: SubmitPlayerReport(); reportOpen = false; break;
                case Act.ReportBack: reportOpen = false; break;
                case Act.Forfeit: net.Forfeit(); open = false; break;
                case Act.Host:   // CHOOSE THE RULES, then the room opens
                    rulesOpen = true; rulesForHost = true; rulesEdit = false; rulesStep = 0;
                    OptionsFrom(Tun);
                    break;
                case Act.RoomPick:
                {   // CHOOSE A ROOM: then its options (nothing changes until START / APPLY)
                    int was = optExperimental ? 1 + optExpRoom : 0, k = (int)Math.Min(3u, it.ip);
                    optExperimental = k > 0;
                    if (k > 0) optExpRoom = k - 1;
                    if (k >= 2) optAdvanced = false;   // (the four-player rooms: a timed match; the SQUARE and DIAMOND: either)
                    roundsPad = false; roomBallPage = false; roomBouncePage = false;
                    if (k == 3 && was != 3) { optShields = false; optKnuckle = 1; }   // FIREBALL BLITZ's own: no shields, knuckles on
                    if (k != 3 && was == 3) { optShields = true; optKnuckle = -1; }   // (and back from it: the rules')
                    rulesStep = 1;
                    break;
                }
                case Act.RulesPick: optAdvanced = it.ip == 1; break;   // the SQUARE, DIAMOND: a TIMED MATCH or ROUNDS
                case Act.OptShields:
                {   // ON, OFF (HOTDOG), and in the SQUARE and DIAMOND ALWAYS (MAIDEN)
                    bool two = !optExperimental || optExpRoom == Sim.Room.Diamond;
                    if (optShields && !optMaiden) optShields = false;
                    else if (!optShields && two) { optShields = true; optMaiden = true; }
                    else { optShields = true; optMaiden = false; }
                    break;
                }
                case Act.OptAim:
                {   // OFF, LOW, MEDIUM, HIGH (kept as "the rules'" when it is theirs)
                    int rules = optAdvanced ? 1 : 2, cur = optAim >= 0 ? optAim : rules, next = (cur + 1) % 4;
                    optAim = next == rules ? -1 : next;
                    break;
                }
                case Act.OptZone: optZone = (optZone + 1) % 3; break;       // SMALL, REGULATION, LARGE
                case Act.OptGrowth: optGrowth = (optGrowth + 1) % 4; break;   // NONE, QUARTER, HALF, REGULATION
                case Act.OptRounds: optRounds = Math.Max(1, Math.Min((MaxBestOf + 1) / 2, optRounds + (it.ip != 0 ? 1 : -1))); break;
                case Act.RoundsPad: roundsPad = true; roundsBuf = ""; break;   // BEST OF n typed
                case Act.PadKey:   // (digits only: the pad has no other keys; two at most, no leading zero)
                    if (roundsBuf.Length < 2 && !(roundsBuf.Length == 0 && it.ip == '0')) roundsBuf += (char)it.ip;
                    break;
                case Act.PadDel: if (roundsBuf.Length > 0) roundsBuf = roundsBuf.Substring(0, roundsBuf.Length - 1); break;
                case Act.PadOk:
                    if (PadBestOf() > 0) optRounds = (PadBestOf() + 1) / 2;
                    roundsPad = false; roundsBuf = "";
                    break;
                case Act.PadCancel: roundsPad = false; roundsBuf = ""; break;
                case Act.OptBot:   // - / + or a step of the slider (a set level), or MATCH MY LEVEL
                    if (it.ip == 2) { optBotAdapt = true; break; }
                    if (optBotAdapt) optBotLevel = (int)MathF.Round(Tun.botMatch, MidpointRounding.AwayFromZero);   // (- / + go on from where it got to)
                    optBotLevel = it.ip >= 10 ? (int)it.ip - 10 : optBotLevel + (optBotAdapt ? 0 : it.ip != 0 ? 1 : -1);
                    optBotLevel = Math.Max(0, Math.Min(Bot.Levels - 1, optBotLevel));
                    optBotAdapt = false;
                    break;
                case Act.OptMeet: optBotMeet = !optBotMeet; break;   // FIST BUMP FIRST (practice)
                case Act.NewBot:   // NEW BOT NOW (0.3.23, TASKS.md E55): a new outfit and stance for the bots, now
                {
                    string nb = s.NewBot(a.game);
                    net?.NoticeLocal(nb);
                    Debug.Log($"[Menu] {nb} (NEW BOT NOW)");
                    break;
                }
                case Act.BotEveryMatch: s.botEveryMatch = !s.botEveryMatch; s.Save(a.RulesToSave); break;   // (... or every match)
                case Act.OptBotPlay: optBotPlay = 1 - (optBotPlay & 1); break;   // BOT STYLE: BRAWLER, DUELLIST
                case Act.OptShieldSize: optShieldSize = (optShieldSize + 1) % 5; break;   // SHIELD SIZE: REGULATION .. DOUBLE
                case Act.OptStrike: optStrikeSpeed = (optStrikeSpeed + 1) % 3; break;   // REGULATION, FAST, FASTER
                case Act.OptFoul: optFoulShift = optFoulShift >= 2 ? -2 : optFoulShift + 1; break;   // 0, LONGER 1, 2, SHORTER 2, 1
                case Act.OptRoom:   // DIAMOND, FOUR CORNERS, FIREBALL BLITZ (its own defaults: knuckle guards on, no shields)
                    optExpRoom = (optExpRoom + 1) % RoomsHere;
                    if (optExpRoom == Sim.Room.Blitz) { optShields = false; optKnuckle = 1; }
                    break;
                case Act.OptHealth:
                {   // 1, 3, 5, 10
                    int[] kHealth = { 1, 3, 5, 10 };
                    int i = 0;
                    while (i < 4 && kHealth[i] <= optHealth) ++i;
                    optHealth = kHealth[i % 4];
                    break;
                }
                case Act.OptBalls: optBalls = optBalls % 4 + 1; break;   // ONE, TWO, THREE, RAPID FIRE
                case Act.OptPlayers: optOctPlayers = optOctPlayers >= 4 ? 2 : optOctPlayers + 1; break;
                case Act.OptTeams:   // FREE-FOR-ALL, 2 V 2, 3 V 1 with you THE ONE, (hosting) 3 V 1 taking turns
                    if (optTeams == Teams.ThreeVOne && !optOneTurns && rulesForHost) optOneTurns = true;
                    else { optTeams = (optTeams + 1) % 3; optOneTurns = false; }
                    break;
                case Act.MovePlayers: rulesOpen = false; movePlayersOpen = true; moveHost = false; moveSel = -1; break;
                case Act.HandOff: movePlayersOpen = true; moveHost = true; break;
                case Act.HandOffTo:   // (the match pauses while the room changes hands, then goes on)
                    if (net.HostHandOff(it.ip, a.game)) { movePlayersOpen = false; moveHost = false; open = false; }
                    break;
                case Act.MoveCorner:   // the first corner picked, then the one it swaps with (the same again: unpicked)
                    if (moveSel < 0) moveSel = (int)it.ip;
                    else if (moveSel == (int)it.ip) moveSel = -1;
                    else { net.HostSwap(moveSel, (int)it.ip, a.game); moveSel = -1; }
                    break;
                case Act.MoveOne: net.HostMakeOne((int)it.ip, a.game); moveSel = -1; break;
                case Act.MoveBack: movePlayersOpen = false; moveSel = -1; rulesOpen = !moveHost; moveHost = false; break;
                case Act.PrivateTalk:   // ROOM TALK (saved; for rooms you host)
                    if (net != null) net.PrivateTalk = !net.PrivateTalk;
                    s.privateTalk = net != null ? net.PrivateTalk : !s.privateTalk;
                    s.Save(a.RulesToSave);
                    break;
                case Act.OptKnuckle:
                {   // stored as "as the rules say" when it matches them
                    var t = new Tunables();
                    OptionsInto(t);
                    bool on = !t.KnuckleGuards();
                    optKnuckle = on == !optAdvanced ? -1 : (on ? 1 : 0);
                    break;
                }
                case Act.OptTime:
                {   // - / + through the choices (Basic: FREE PLAY 0, 1 to 15 min; Advanced rounds: OFF, 1 to 5 min)
                    float[] basic = { 0, 60, 120, 180, 300, 600, 900 }, rounds = { 0, 60, 120, 180, 300 };
                    float[] l = optAdvanced ? rounds : basic;
                    int n = l.Length, k = 0;
                    float val = optAdvanced ? optRoundTime : optMatchTime;
                    for (int i = 0; i < n; ++i) if (Mathf.Abs(l[i] - val) < 1f) k = i;
                    k = Math.Max(0, Math.Min(n - 1, k + (it.ip != 0 ? 1 : -1)));
                    if (optAdvanced) optRoundTime = l[k]; else optMatchTime = l[k];
                    break;
                }
                case Act.OptBack: rulesStep = 0; roundsPad = false; roomBallPage = false; roomBouncePage = false; break;
                case Act.OptGo:
                    ApplyRules();
                    Debug.Log($"[Menu] rules -> {Scoreboard.RulesLine(Tun)} ({(rulesEdit ? "changed" : rulesForHost ? "host a room" : "practice")})");
                    rulesOpen = false;
                    rulesStep = 0;
                    if (rulesEdit) { settingsOpen = true; rulesEdit = false; break; }
                    if (rulesForHost) net.Host();
                    else
                    {
                        titleOpen = false; open = false;
                        a.lobby = false;   // into the court against the bot
                        a.startPractice = true;
                    }
                    break;
                case Act.RulesBack:
                    rulesOpen = false;
                    if (rulesEdit) { settingsOpen = true; rulesEdit = false; }
                    else if (!rulesForHost) titleOpen = true;
                    break;
                case Act.Leave: net?.Leave(); break;
                case Act.Join: case Act.JoinRoom: net.Join(it.ip, false); open = false; break;   // the room's waiting area, then JOIN QUEUE
                case Act.BotFill:   // (a room taken over keeps its own setting: ours stays as we saved it)
                    if (net == null) { s.botFill = !s.botFill; s.Save(a.RulesToSave); break; }
                    net.BotFill = !net.BotFill;
                    if (!net.Inherited) { s.botFill = net.BotFill; s.Save(a.RulesToSave); }
                    break;
                case Act.Rules:   // the rules and MATCH OPTIONS of this arena (or of practice)
                    settingsOpen = false;
                    rulesOpen = true; rulesStep = 0; rulesEdit = true;
                    rulesForHost = NetMode == Online.Net.Mode.Host;
                    OptionsFrom(Tun);
                    break;
                case Act.Close: open = false; nameEdit = false; helpOpen = false; settingsOpen = false; titleOpen = false; xpOpen = false; rulesOpen = false; movePlayersOpen = false; moveHost = false; break;
                case Act.Spectate: net.Spectate(); a.watchIp = 0; open = false; break;
                case Act.Watch: a.watchIp = it.ip; open = false; break;
                case Act.Hub: a.watchIp = 0; if (a.director != null) { a.director.autoCam = false; a.director.open = false; } break;
                case Act.Director: if (a.director != null) { a.director.Open(a.localIn); open = false; } break;
                case Act.ClearWarps:
                    if (a.director != null) { a.director.warps.Clear(); a.director.warpIdx = -1; a.director.autoCam = false; a.director.Save(); }
                    break;
                case Act.Queue: net.Queue(net.LocalQueuePos() < 0); break;
                case Act.HideOutside: s.hideOutside = !s.hideOutside; s.Save(a.RulesToSave); break;
                case Act.Avatar:   // the builder (it closes the menu)
                    titleOpen = false; open = false; nameEdit = false; helpOpen = false; settingsOpen = false; rulesOpen = false;
                    a.openBuilder = true;
                    break;
                case Act.Play:   // CHOOSE THE RULES first (then MATCH OPTIONS)
                    Tr.Stop();
                    titleOpen = false; rulesOpen = true; rulesForHost = false; rulesEdit = false; rulesStep = 0;
                    OptionsFrom(Tun);
                    break;
                case Act.Multi: titleOpen = false; break;
                case Act.Settings: settingsOpen = true; break;
                case Act.Back: if (voicePage && mutedPage) mutedPage = false; else if (btnPage) btnPage = false; else if (ctlPage) ctlPage = false; else if (voicePage) voicePage = false; else if (gfxPage) gfxPage = false; else if (ptPage) ptPage = false; else settingsOpen = false; s.Save(a.RulesToSave); break;
                case Act.Passthrough: ptPage = true; break;
                case Act.HeadSelfie: s.headSelfie = !s.headSelfie; s.Save(a.RulesToSave); break;
                case Act.PtMode: RoomPassthrough.SetMode(s, (int)it.ip); s.Save(a.RulesToSave); break;
                case Act.PtAdjust: RoomPassthrough.Adjust(s, (int)it.ip); s.Save(a.RulesToSave); break;
                case Act.PtPreset: RoomPassthrough.Preset(s, (int)it.ip); s.Save(a.RulesToSave); break;
                case Act.PtMono: s.ptMono = !s.ptMono; s.Save(a.RulesToSave); break;
                case Act.PtEdges: s.ptEdges = !s.ptEdges; s.Save(a.RulesToSave); break;
                case Act.PtReveal: s.ptEdgeReveal = !s.ptEdgeReveal; s.Save(a.RulesToSave); break;
                case Act.PtPeek: s.ptPeekOn = !s.ptPeekOn; s.Save(a.RulesToSave); break;
                case Act.GfxPage: gfxPage = true; break;
                case Act.CtlPage: ctlPage = true; break;
                case Act.BallPage: roomBallPage = true; roundsPad = false; break;   // (the room's BALL FEEL)
                case Act.BallBack: roomBallPage = false; break;
                case Act.BouncePage: roomBouncePage = true; roundsPad = false; break;   // (the room's BOUNCES)
                case Act.BounceBack: roomBouncePage = false; break;
                case Act.BounceWalls: optWallsLively = !optWallsLively; break;   // (nothing changes until START / APPLY)
                case Act.BounceGrip: optWallGrip = !optWallGrip; break;
                case Act.BounceAssist: optBankAssist = (Math.Max(0, Math.Min(3, optBankAssist)) + 1) % 4; break;   // OFF .. HIGH
                case Act.BtnPage: btnPage = true; break;
                case Act.BtnSlot:
                {   // BUTTONS: that button's next job (a job that must keep a button keeps its last: say so)
                    Controls.Map m = a.Buttons;
                    int sl = (int)(it.ip % Controls.S_COUNT);
                    if (!Controls.CanLeave(m, sl, a.ctlKind))
                    {
                        btnNote = Controls.JobName(m.job[sl]) + " NEEDS A BUTTON: GIVE IT ANOTHER ONE FIRST";
                        btnNoteUntil = Time.realtimeSinceStartupAsDouble + 4.0;
                        break;
                    }
                    m.job[sl] = Controls.NextJob(m, sl, a.ctlKind);
                    s.buttons = m;
                    btnNoteUntil = 0;
                    s.Save(a.RulesToSave);
                    break;
                }
                case Act.BtnSticks: s.buttons = a.Buttons; s.buttons.leftSticks = !s.buttons.leftSticks; s.Save(a.RulesToSave); break;
                case Act.BtnPreset:   // STANDARD (the pointer in the right hand) or LEFT-HANDED (all mirrored, the pointer left)
                    s.buttons = it.ip != 0 ? Controls.LeftHanded() : Controls.Standard();
                    s.pointerHand = it.ip != 0 ? 0 : 1;
                    btnNoteUntil = 0;
                    s.Save(a.RulesToSave);
                    break;
                case Act.BallCore: optBallFeel ^= Tunables.BALL_CORE; break;   // (nothing changes until START / APPLY)
                case Act.BallTrue: optBallFeel ^= Tunables.BALL_TRUE; break;
                case Act.BallSpin: optBallFeel ^= Tunables.BALL_SPIN; break;
                case Act.BallWeight: optBallFeel ^= Tunables.BALL_WEIGHT; break;
                case Act.CtlAdj:
                {   // (k * 2 + up): a step of 5 degrees, live on your hands
                    int k = (int)Math.Min(2u, it.ip / 2), step = (it.ip & 1) != 0 ? AppSettings.CtlStep : -AppSettings.CtlStep;
                    int Clamp(int x) => Math.Max(-AppSettings.CtlMax, Math.Min(AppSettings.CtlMax, x + step));
                    if (k == 0) s.ctlTilt = Clamp(s.ctlTilt); else if (k == 1) s.ctlTwist = Clamp(s.ctlTwist); else s.ctlTurn = Clamp(s.ctlTurn);
                    s.Save(a.RulesToSave);
                    break;
                }
                case Act.Studio: St.Open(this); break;
                case Act.StudioSection: St.OpenSection((int)it.ip, this); break;
                case Act.StudioPrev: St.Step((int)it.ip, -1); break;
                case Act.StudioNext: St.Step((int)it.ip, 1); break;
                case Act.StudioPlay: St.Play((int)it.ip); break;
                case Act.StudioVote: St.Vote((int)(it.ip & 0xFFu), (it.ip & 0x100u) != 0 ? 1 : -1); break;
                case Act.StudioMoment: open = false; break;
                case Act.StudioLeave: St.Leave(this); break;
                case Act.StudioDelete: St.Delete((int)it.ip); break;
                case Act.FovDown: s.roomFeel = Math.Max(-5, s.roomFeel - 1); RoomFeelFeature.Step = s.roomFeel; s.Save(a.RulesToSave); break;
                case Act.FovUp: s.roomFeel = Math.Min(5, s.roomFeel + 1); RoomFeelFeature.Step = s.roomFeel; s.Save(a.RulesToSave); break;
                case Act.LookAhead:
                    s.lookAhead = !(net != null ? net.LookAhead : s.lookAhead);
                    if (net != null) net.LookAhead = s.lookAhead;
                    s.Save(a.RulesToSave);
                    Debug.Log($"[Menu] ball look-ahead {(s.lookAhead ? "on" : "off")}");
                    break;
                case Act.VoicePage: voicePage = true; mutedPage = false; break;
                case Act.MutedPage: mutedPage = true; mutedFirst = 0; break;
                case Act.MutedPrev: mutedFirst = Math.Max(0, mutedFirst - 6); break;
                case Act.MutedNext: mutedFirst += 6; break;
                case Act.Unmute: a.mutes.Unmute((int)it.ip, net, a.mutedIds); break;   // (the kept mute goes; whoever here it covered is heard again)
                case Act.VoiceMode: s.voiceMode = (s.voiceMode + 1) % 3; s.Save(a.RulesToSave); break;
                case Act.MuteSpec: s.muteSpectators = !s.muteSpectators; s.Save(a.RulesToSave); break;
                case Act.BallSound: s.ballSound = !s.ballSound; s.Save(a.RulesToSave); Debug.Log($"[Menu] ball sound {(s.ballSound ? "on" : "off")}"); break;
                case Act.MutePlayers: s.mutePlayers = !s.mutePlayers; s.Save(a.RulesToSave); break;
                case Act.MuteAll: s.muteAll = !s.muteAll; s.Save(a.RulesToSave); break;
                case Act.VolDown: s.volume = Math.Max(0, s.volume - 1); s.Save(a.RulesToSave); break;
                case Act.MusicDown: s.musicVolume = Math.Max(0, s.musicVolume - 1); s.Save(a.RulesToSave); break;   // (MUSIC)
                case Act.MusicUp: s.musicVolume = Math.Min(10, s.musicVolume + 1); s.Save(a.RulesToSave); break;
                case Act.MusicMenu: s.musicMenu = !s.musicMenu; s.Save(a.RulesToSave); Debug.Log($"[Menu] music in the menu {(s.musicMenu ? "on" : "off")}"); break;
                case Act.MusicGame: s.musicGame = !s.musicGame; s.Save(a.RulesToSave); Debug.Log($"[Menu] music in the game {(s.musicGame ? "on" : "off")}"); break;
                case Act.VolUp: s.volume = Math.Min(10, s.volume + 1); s.Save(a.RulesToSave); break;
                case Act.Hand: s.pointerHand = 1 - s.pointerHand; s.Save(a.RulesToSave); break;
                case Act.Fx: s.fxLevel = (s.fxLevel + 1) % 3; s.Save(a.RulesToSave); break;
                // GRAPHICS (TASKS.md E38): each round its values (OFF, SPECTATING, ON for the four), saved at once
                case Act.GfxFoveation: s.gfxFoveation = (s.gfxFoveation + 1) % 4; s.Save(a.RulesToSave); ArenaGraphics.Log(s); break;
                case Act.GfxMsaa: s.gfxMsaa = (s.gfxMsaa + 1) % 3; s.Save(a.RulesToSave); ArenaGraphics.Log(s); break;
                case Act.GfxContact: s.gfxContact = (s.gfxContact + 1) % 3; s.Save(a.RulesToSave); ArenaGraphics.Log(s); break;
                case Act.GfxGrade: s.gfxGrade = (s.gfxGrade + 1) % 3; s.Save(a.RulesToSave); ArenaGraphics.Log(s); break;
                case Act.GfxBloom: s.gfxBloom = (s.gfxBloom + 1) % 3; s.Save(a.RulesToSave); ArenaGraphics.Log(s); break;
                case Act.GfxReflect: s.gfxReflect = (s.gfxReflect + 1) % 3; s.Save(a.RulesToSave); ArenaGraphics.Log(s); break;
                case Act.Crowd: s.crowdMode = s.crowdMode < 2 ? 2 : 0; s.Save(a.RulesToSave); break;   // (FLAGS AND LIGHTS <-> OFF)
                case Act.Turn: s.turnMode = (s.turnMode + 1) % 3; s.Save(a.RulesToSave); break;
                case Act.Name: nameEdit = true; editTarget = 0; nameBuf = s.name; break;
                case Act.Server: nameEdit = true; editTarget = 1; nameBuf = defaultServer.Length > 0 && s.server == defaultServer ? "" : s.server; break;
                case Act.ServerDefault:
                    s.server = defaultServer;
                    s.Save(a.RulesToSave);
                    Debug.Log("[Menu] server -> the default");
                    if (net.Online) { net.GoOffline(); net.GoOnline(s.server); }
                    break;
                case Act.Online:
                {
                    bool wasSpec = NetMode == Online.Net.Mode.Spectate;
                    if (net.Online) net.GoOffline();
                    else if (string.IsNullOrEmpty(s.server)) { nameEdit = true; editTarget = 1; nameBuf = ""; }
                    else net.GoOnline(s.server);
                    if (wasSpec) { net.Spectate(); a.watchIp = 0; }   // keep spectating, now on the other network
                    s.onlinePref = net.Online;
                    s.Save(a.RulesToSave);
                    break;
                }
                case Act.Help: helpOpen = true; creditsOpen = false; break;
                case Act.Credits: creditsOpen = true; break;
                case Act.CreditsBack: creditsOpen = false; break;
                case Act.HelpDone:
                    helpOpen = false;
                    creditsOpen = false;
                    if (!titleOpen) open = false;
                    s.MarkHelpSeen();
                    break;
                case Act.Key:
                {
                    int cap = editTarget >= 4 ? Reports.NoteMax : editTarget == 1 ? 48 : 16;
                    if (nameBuf.Length < cap && !(it.ip == ' ' && (nameBuf.Length == 0 || nameBuf[nameBuf.Length - 1] == ' '))) nameBuf += (char)it.ip;
                    break;
                }
                case Act.KeyDel: if (nameBuf.Length > 0) nameBuf = nameBuf.Substring(0, nameBuf.Length - 1); break;
                case Act.KeyDone:
                    nameBuf = nameBuf.TrimEnd(' ');
                    if (editTarget == 5) reportNote = nameBuf;
                    else if (editTarget == 4) bugNote = nameBuf;
                    else if (editTarget == 2) { s.team = nameBuf.Length > 16 ? nameBuf.Substring(0, 16) : nameBuf; s.Save(a.RulesToSave); open = false; }
                    else if (editTarget == 1)
                    {
                        s.server = nameBuf;
                        if (s.server.Length > 0) { s.onlinePref = true; net?.GoOnline(s.server); }
                        s.Save(a.RulesToSave);
                    }
                    else if (nameBuf.Length > 0) { s.SaveName(nameBuf); net?.SetName(s.name); }
                    nameEdit = false;
                    break;
            }
            // CUSTOM TRAINING's MY OPTIONS: each change in force at once, and saved (the same options as your room's)
            if (Tr.open && Tr.optsPage && it.act != Act.TrainOptsDone) ApplyMyOptions();
        }

        void Go(Act act) => Activate(new Item("", 0, 0, 0, 0, act, 0, true));

        // a player report: who, why, where, and everyone else who was there (for the moderators)
        void SubmitPlayerReport()
        {
            var people = People();
            string role = "-";
            foreach (var p in people) if (p.id == reportWho) role = Reports.RoleText(p.role);
            var w = new System.Text.StringBuilder();
            w.Append($"reported: {reportName} (session {reportWho:X8}, {role})\n");
            w.Append("reason: ").Append(reportWhy >= 0 ? Reports.ReportWhy[reportWhy] : "-").Append('\n');
            w.Append("note: ").Append(reportNote.Length == 0 ? "-" : reportNote).Append('\n');
            w.Append($"reporter: {net.Name} (session {net.Session:X8})\n");
            string here = net.HereName();
            w.Append("where: room ").Append(here.Length == 0 ? "-" : here).Append(a.spectating ? " (spectating)" : "").Append('\n');
            w.Append("people here:");
            foreach (var p in people) w.Append($" {p.name} ({p.id:X8}, {Reports.RoleText(p.role)});");
            w.Append('\n');
            Debug.Log($"[Report] player report: {reportName}, {(reportWhy >= 0 ? Reports.ReportWhy[reportWhy] : "-")}");
            Reports.Submit(a, Reports.Text(a, "Project Arena player report", w.ToString(), -1, null), "player report");
        }

        // pointer: the pointer hand's aim ray against the panel plane; a trigger click activates the hovered item.
        // Returns true when something was clicked (the caller buzzes the hand). The game gets no triggers while open.
        public bool Update(PlayerInput @in, Pose aim, bool aimValid)
        {
            hover = -1;
            hitValid = false;
            if (!open) return false;
            bool clicked = false;
            int ph = a.settings.pointerHand;
            bool trig = @in.hand[ph].trigger > 0.7f;
            if (aimValid)
            {
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                Vec3 nz = pose.q.Rotate(new Vec3(0, 0, 1));
                float den = M.Dot(d, nz);
                if (den < -1e-3f)
                {
                    float t = M.Dot(pose.p - o, nz) / den;
                    if (t > 0 && t < 4)
                    {
                        Vec3 p = o + d * t;
                        Vec3 lp = pose.q.Conj().Rotate(p - pose.p);
                        bool onPanel = Mathf.Abs(lp.x) < 0.5f && Mathf.Abs(lp.y + 0.09f) < 0.47f;
                        bool onSide = !nameEdit && lp.x > NavX - 0.15f && lp.x < NavX + 0.15f && Mathf.Abs(lp.y + 0.09f) < 0.47f;
                        if (onPanel || onSide) { hit = p; hitValid = true; }
                        var items = Items();
                        for (int i = 0; i < items.Count; ++i)
                        {
                            Item it = items[i];
                            if (it.enabled && Mathf.Abs(lp.x - it.x) < it.w * 0.5f && Mathf.Abs(lp.y - it.y) < it.h * 0.5f) hover = i;
                        }
                        if (trig && !prevTrig && hover >= 0)
                        {
                            Activate(items[hover]);
                            clicked = true;
                        }
                    }
                }
            }
            bestsHover = false;
            if (BestsShown() && aimValid)
            {   // MY BESTS (its own plane): the pointer on it, its TRAINING button
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                Pose bp = BestsPose();
                Vec3 nz = bp.q.Rotate(new Vec3(0, 0, 1));
                float den = M.Dot(d, nz);
                if (den < -1e-3f)
                {
                    float t = M.Dot(bp.p - o, nz) / den;
                    Vec3 p = o + d * t, lp = bp.q.Conj().Rotate(p - bp.p);
                    if (t > 0 && t < 4 && Mathf.Abs(lp.x) < BestsW * 0.5f && Mathf.Abs(lp.y) < BestsH * 0.5f)
                    {
                        hit = p;
                        hitValid = true;
                        hover = -1;
                        bool can = NetMode == Online.Net.Mode.Offline;   // (TRAINING is played offline, as the title's button)
                        bestsHover = can && Mathf.Abs(lp.x) < BestsBtnW * 0.5f && Mathf.Abs(lp.y - BestsBtnY) < BestsBtnH * 0.5f;
                        if (bestsHover && trig && !prevTrig && !clicked)
                        {
                            Activate(new Item("TRAINING", 0, 0, 0, 0, Act.Train, 0, true));
                            clicked = true;
                        }
                    }
                }
            }
            prevTrig = trig;
            for (int h = 0; h < 2; ++h) { @in.hand[h].trigger = 0; @in.hand[h].squeeze = 0; }   // no grabbing / shields in the menu
            return clicked;
        }

        static Vector4 V(float r, float g, float b, float al = 1) => new Vector4(r, g, b, al);

        // MY BESTS (native 1159a46 drawMenu's): your TRAINING bests, the standard ones as the leaderboards have them, on
        // its own plane turned to face you (x, y on it from its centre)
        void DrawBests(FxRenderer fx, Vector4 edge, Vector4 bg, Vector4 btn, Vector4 hov, Vector4 dis, Vector4 white, Vector4 grey, Vector4 cyan)
        {
            Pose bp = BestsPose();
            void Bq(float x, float y, float w, float h, float z, Vector4 c) => fx.Panel(bp * new Pose(Quat.Identity, new Vec3(x, y, z)), w, h, c);
            void Bt(string s, float x, float y, float h, Vector4 c) => fx.Text(s, bp * new Pose(Quat.Identity, new Vec3(x, y, 0.006f)), h, c);
            Bq(0, 0, BestsW + 0.04f, BestsH + 0.04f, -0.002f, edge);
            Bq(0, 0, BestsW, BestsH, 0, bg);
            float left = -BestsW * 0.5f + 0.03f, right = BestsW * 0.5f - 0.03f, top = BestsH * 0.5f;
            foreach (var (s, x, y, h, c) in BestsLines(Tr.bests, top, left, right, white, grey, cyan)) Bt(s, x, y, h, c);
            bool can = NetMode == Online.Net.Mode.Offline;   // TRAINING (played offline, as the title's button)
            Bq(0, BestsBtnY, BestsBtnW, BestsBtnH, 0.002f, !can ? dis : bestsHover ? hov : btn);
            Bt("TRAINING", 0, BestsBtnY, 0.032f, can ? white : grey);
            if (!can) Bt("LEAVE THE ROOM TO TRAIN", 0, BestsBtnY + 0.06f, 0.016f, grey);
        }

        // MY BESTS' lines (text, centre x, y, height, colour), as native draws them: the title, then NO BESTS YET or the
        // TRAINING SCORE, the lessons passed and up to eight of them (name left, the best right)
        public static List<(string s, float x, float y, float h, Vector4 c)> BestsLines(Sim.Training.Bests B, float top, float left, float right, Vector4 white, Vector4 grey, Vector4 cyan)
        {
            var o = new List<(string, float, float, float, Vector4)>();
            o.Add(("MY BESTS", 0, top - 0.06f, 0.042f, cyan));
            var Ls = Sim.Training.Lessons.All;
            int passed = 0;
            foreach (var L in Ls) passed += B.b[L.id].passed ? 1 : 0;
            if (passed == 0)
            {
                o.Add(("NO BESTS YET", 0, top - 0.20f, 0.03f, white));
                o.Add(("EACH TRAINING LESSON", 0, top - 0.29f, 0.021f, grey));
                o.Add(("TEACHES ONE SKILL:", 0, top - 0.328f, 0.021f, grey));
                o.Add(("THROWING, DEFENDING,", 0, top - 0.366f, 0.021f, grey));
                o.Add(("CATCHING", 0, top - 0.404f, 0.021f, grey));
                return o;
            }
            int tot = B.Total();
            o.Add(($"TRAINING SCORE {tot / 10}.{tot % 10}", 0, top - 0.125f, 0.024f, white));
            o.Add(($"{passed} OF {Ls.Count} LESSONS PASSED", 0, top - 0.165f, 0.018f, grey));
            int row = 0;   // the lessons passed, in the TRAINING page's order (the first eight)
            for (int i = 0; i < Ls.Count && row < 8; ++i)
            {
                var b = B.b[Ls[i].id];
                if (!b.passed) continue;
                float y = top - 0.225f - 0.052f * row++;
                string name = Ls[i].name;
                if (name.Length > 14) name = name.Substring(0, 14);
                string best = Ls[i].Timed() ? $"{b.time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} S {b.Permille() / 10}%" : $"{b.successes} / {b.attempts}";
                const float hh = 0.019f;
                o.Add((name, left + 0.375f * hh * name.Length, y, hh, white));    // (left-aligned: the font's glyphs 0.75 x their height)
                o.Add((best, right - 0.375f * hh * best.Length, y, hh, cyan));   // (right-aligned)
            }
            return o;
        }

        // MENU > MY STATS: the player's career from the community server as a dashboard: three rings (win rate, throw
        // accuracy, blocks), four number tiles, the top speed on a bar. Asked again every 5 s while open.
        void DrawStats(FxRenderer fx, Func<float, float, float, Vec3> At, Action<string, float, float, float, Vector4> Text)
        {
            void Quad(float x, float y, float w, float h, float z, Vector4 c, float ang = 0f) =>
                fx.Panel(new Pose(pose.q * Quat.AxisAngle(new Vec3(0, 0, 1), ang), At(x, y, z)), w, h, c);
            Vector4 white = V(1, 1, 1), grey = V(0.45f, 0.5f, 0.56f), cyan = V(0.3f, 0.85f, 1.0f);
            Vector4 dim = V(0.06f, 0.09f, 0.13f), tile = V(0.02f, 0.04f, 0.07f);
            double t = Time.realtimeSinceStartupAsDouble;
            if (net.OnlineReady && t > a.myStatsAskAt) { a.myStatsAskAt = t + 5.0; net.RequestMyStats(); }
            var mine = new Online.Net.CareerStats[2];
            if (net.StatsFor(Online.Net.kMyStats, mine)) { a.myCareer = mine[0]; a.myCareerOk = true; }
            Text("MY STATS", 0, 0.29f, 0.055f, cyan);
            Text(net.Name, 0, 0.235f, 0.026f, white);
            var c = a.myCareer;
            if (!net.OnlineReady && !a.myCareerOk) { Text("CONNECT TO THE COMMUNITY SERVER TO SEE YOUR STATS", 0, 0.05f, 0.022f, white); return; }
            if (!a.myCareerOk) { Text("ASKING THE SERVER...", 0, 0.05f, 0.022f, grey); return; }
            if (c.matches == 0)
            {
                Text("NO MATCHES YET", 0, 0.08f, 0.032f, white);
                Text("PLAY AN ONLINE MATCH AGAINST ANOTHER PLAYER TO START YOUR STATS", 0, 0.02f, 0.019f, grey);
                return;
            }
            Vector4 green = V(0.25f, 0.95f, 0.55f), gold = V(1.0f, 0.78f, 0.25f);
            string[] labels = { "WIN RATE", "THROW ACCURACY", "BLOCKS" };
            float[] frac = { (float)c.wins / c.matches, c.throws != 0 ? (float)c.onTarget / c.throws : 0f, c.faced != 0 ? (float)c.blocks / c.faced : 0f };
            Vector4[] col = { cyan, green, gold };
            const int N = 48;
            for (int r = 0; r < 3; ++r)
            {   // the rings: a background track, the filled share clockwise from the top, the number in the middle
                float cx = -0.32f + 0.32f * r, cy = 0.075f, R = 0.085f;
                for (int i = 0; i < N; ++i)
                {
                    float ang = M.kPi * 0.5f - (i + 0.5f) * 2f * M.kPi / N;
                    bool on = (i + 0.5f) / N <= frac[r];
                    Quad(cx + R * MathF.Cos(ang), cy + R * MathF.Sin(ang), 2f * M.kPi * R / N * 1.08f, 0.018f, on ? 0.003f : 0.002f, on ? col[r] : dim, ang - M.kPi * 0.5f);
                }
                Text($"{(int)MathF.Round(frac[r] * 100f)}%", cx, cy, 0.040f, white);
                Text(labels[r], cx, cy - 0.125f, 0.018f, grey);
            }
            string[] tl = { "MATCHES", "WINS", "HITS", "CATCHES" };
            uint[] tv = { c.matches, c.wins, c.hits, c.catches };
            for (int k = 0; k < 4; ++k)
            {   // the tiles
                float x = -0.36f + 0.24f * k, y = -0.17f;
                Quad(x, y, 0.22f, 0.10f, 0.002f, tile);
                Text(tv[k].ToString(), x, y + 0.012f, 0.040f, white);
                Text(tl[k], x, y - 0.033f, 0.015f, grey);
            }
            // the top speed on a 0-100 km/h bar
            float kmh = c.topSpeed * 3.6f, fr = Math.Min(1f, kmh / 100f), yb = -0.31f, wb = 0.80f;
            Quad(0, yb, wb, 0.026f, 0.002f, dim);
            Quad(-wb * 0.5f + wb * fr * 0.5f, yb, wb * fr, 0.026f, 0.003f, gold);
            Text($"TOP THROW SPEED  {(int)MathF.Round(kmh)} KM/H", 0, yb + 0.04f, 0.020f, white);
            Text("0", -wb * 0.5f, yb - 0.035f, 0.014f, grey);
            Text("100 KM/H", wb * 0.5f - 0.04f, yb - 0.035f, 0.014f, grey);
            Text("YOUR STATS ARE SHOWN TO OTHER PLAYERS AND SPECTATORS", -0.16f, -0.475f, 0.0125f, grey);
            Text("DELETE ERASES THEM FROM THE SERVER", -0.16f, -0.505f, 0.0125f, grey);
        }

        // native drawMenu (the pages this port has); emblem: open_emblem drawn over the title page
        // TRAINING's pages, as native drawMenu writes them
        // SOUND STUDIO's page (native drawMenu's)
        void DrawStudio(System.Action<string, float, float, float, Vector4> Text, Vector4 white, Vector4 grey, Vector4 cyan)
        {
            var sec = StudioCatalog.Sections[St.section];
            var gold = V(1.0f, 0.85f, 0.35f);
            Text("SOUND STUDIO", 0, 0.31f, 0.05f, cyan);
            var st = St.lib.CurrentState;
            string head;
            if (st == SoundLibrary.State.Loading) head = $"GETTING THE LIBRARY FROM THE SERVER...  {St.lib.Progress} / {St.lib.Total}";
            else if (st == SoundLibrary.State.Failed) head = $"NO NEW SOUNDS: {St.lib.Error} (ONLINE: ON)  -  THE GAME'S OWN CAN BE TRIED IN ANY SLOT";
            else
            {
                int lib = 0;
                foreach (var s in St.sounds) lib += s.official || s.mine ? 0 : 1;
                head = lib > 0 ? $"{lib} NEW SOUNDS TO TRY, ONLY IN HERE  -  VOTE UP OR DOWN (AGAIN: TAKEN BACK)"
                               : "NO NEW SOUNDS YET: TRY THE GAME'S OWN IN ANY SLOT, OR PROPOSE ONE (DOCS/SOUND_STUDIO.MD)";
            }
            St.MineSummary(out int mine, out double mb);
            Text(mine > 0 ? $"MINE: {mine} .WAV FILE{(mine == 1 ? "" : "S")}, {mb:F1} MB, IN THE GAME'S MY_SOUNDS FOLDER  -  DELETE ONE WITH ITS DELETE BUTTON"
                          : "TRY YOUR OWN: PUT .WAV FILES (WAV ONLY) IN THE GAME'S MY_SOUNDS FOLDER, THEN OPEN THE STUDIO AGAIN", 0, -0.404f, 0.0105f, grey);
            Text(head, 0, 0.27f, 0.0135f, st == SoundLibrary.State.Failed ? gold : white);
            Text(sec.how, 0, 0.245f, 0.0125f, grey);
            Text("YOUR VOTE", 0.32f, 0.158f, 0.0125f, cyan);   // (over UP / DOWN: owner 2026-11-06, the votes weren't obvious)
            Text("SOUND", 0.01f, 0.158f, 0.0125f, cyan);
            Text("THE MOMENT PLAYS ON WHILE THIS PAGE IS OPEN: CHANGE A SOUND AND HEAR IT. MENU BRINGS THE PAGE BACK", 0, -0.383f, 0.0115f, grey);
            for (int k = 0; k < sec.events.Length; ++k)
            {
                float y = StudioRowY(k);
                string ev = sec.events[k];
                string sl = StudioCatalog.SlotName(ev);   // its slot: the catalog's code and name, smaller when long
                Text(sl, -0.265f, y - 0.006f, Mathf.Min(0.0165f, 0.215f / (0.75f * sl.Length)), white);
                int s = St.Chosen(ev);
                Text(s < 0 ? "OFFICIAL" : St.sounds[s].name, 0.01f, y - 0.002f, 0.0155f, s < 0 ? grey : cyan);
                if (s >= 0 && St.sounds[s].mine) Text("MINE - ONLY ON THIS HEADSET", 0.01f, y - 0.019f, 0.0095f, grey);
                else if (s >= 0 && St.sounds[s].official) Text("THE GAME'S OWN, TRIED HERE", 0.01f, y - 0.019f, 0.0095f, grey);
                else if (s >= 0 && St.sounds[s].eventName != ev) Text("MADE FOR " + StudioCatalog.SlotName(St.sounds[s].eventName), 0.01f, y - 0.019f, 0.0095f, grey);
            }
        }

        void DrawTraining(System.Action<string, float, float, float, Vector4> Text, Vector4 white, Vector4 grey, Vector4 cyan)
        {
            var Ls = Sim.Training.Lessons.All;
            var gold = new Vector4(1.0f, 0.85f, 0.35f, 1);
            if (Tr.board >= -1)
            {   // a TRAINING leaderboard
                bool overall = Tr.board < 0;
                Sim.Training.Lesson L = overall ? null : Ls[Tr.board];
                int kind = overall ? 1 : Tr.boardKind;
                Text("LEADERBOARD", 0, 0.29f, 0.050f, cyan);
                Text(overall ? "OVERALL - EVERY LESSON'S BEST ACCURACY ADDED UP" : L.name + (kind != 0 ? " - MOST ACCURATE" : " - FASTEST"), 0, 0.225f, 0.024f, white);
                var bd = net?.BoardOf(overall ? 0 : L.id, kind);
                string Value(uint v) => overall ? $"{v / 10}.{v % 10}" : kind == 0 ? $"{v / 1000}.{(v / 100) % 10} S" : $"{v / 10}.{v % 10}%";
                if (bd != null)
                {
                    for (int i = 0; i < bd.rows.Count && i < 10; ++i)
                    {
                        float y = 0.16f - 0.045f * i;
                        Text((i + 1).ToString(), -0.36f, y, 0.026f, i == 0 ? cyan : white);
                        Text(bd.rows[i].name, -0.08f, y, 0.026f, white);
                        Text(Value(bd.rows[i].value), 0.28f, y, 0.026f, white);
                    }
                    if (bd.rows.Count == 0) Text("NOBODY YET - BE THE FIRST", 0, 0.10f, 0.026f, grey);
                    Text(bd.myPlace > 0 ? $"YOU: NUMBER {bd.myPlace} OF {bd.players}  -  {Value(bd.myValue)}" : "YOU: NOT ON THIS ONE YET (PASS THE LESSON)", 0, -0.32f, 0.022f, cyan);
                }
                else if (net == null || !net.OnlineReady)
                {
                    Text("GO ONLINE TO SEE THE LEADERBOARDS", 0, 0.08f, 0.028f, white);
                    Text("MULTIPLAYER - ONLINE: ON. YOUR BESTS ARE KEPT AND SENT WHEN YOU ARE", 0, 0.03f, 0.018f, grey);
                }
                else Text(Time.realtimeSinceStartupAsDouble - Tr.boardAsked < 4.0 ? "ASKING THE SERVER..." : "NO ANSWER: THE SERVER MAY NOT HAVE LEADERBOARDS YET", 0, 0.08f, 0.024f, grey);
                return;
            }
            if (Tr.open && Tr.optsPage)
            {   // CUSTOM TRAINING: MY OPTIONS
                Text("MY OPTIONS", 0, 0.31f, 0.05f, cyan);
                Text("TRAIN WITH THE OPTIONS YOU PLAY: YOUR OWN ROOM'S (CHANGED HERE, CHANGED THERE TOO)", 0, 0.255f, 0.0165f, white);
                Text("SHIELDS, KNUCKLE GUARDS AND THE CLOCK STAY AS EACH LESSON HAS THEM", 0, 0.222f, 0.0145f, grey);
                Text("WITH MY OPTIONS, A LESSON'S BESTS STAY ON THIS HEADSET: NOT FOR THE LEADERBOARDS", 0, -0.255f, 0.0155f, grey);
                Text("NOW: " + MyOptionsLine(Tr.MyRules), 0, -0.29f, 0.0145f, cyan);
                return;
            }
            if (Tr.open)
            {   // the lessons
                var Bm = Tr.mine ? Tr.bestsMine : Tr.bests;   // (CUSTOM TRAINING: the mode's)
                Text("TRAINING", 0, 0.31f, 0.05f, cyan);
                Text(Tr.mine ? "WITH MY OPTIONS - THE BESTS STAY ON THIS HEADSET" : "THREE TRACKS - PASS A LESSON TO OPEN THE NEXT IN ITS TRACK", 0, 0.262f, 0.019f, white);
                for (int t = 0; t < Sim.Training.Lessons.Tracks; ++t) Text(Sim.Training.Lessons.TrackName(t), TrainColX(t), 0.215f, 0.026f, cyan);
                for (int i = 0; i < Ls.Count; ++i)
                {
                    var b = Bm.b[Ls[i].id];
                    float x = TrainColX((int)Ls[i].track), y = TrainRowY(Sim.Training.Lessons.PlaceInTrack(i, out _) - 1) - 0.034f;
                    string l = !Tr.Unlocked(i) ? "LOCKED" : !b.passed ? Ls[i].teach
                             : Ls[i].Timed() ? $"BEST {b.time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} S - {b.Permille() / 10}%"
                             : $"BEST {b.successes} / {b.attempts}";
                    Text(l, x, y, 0.0125f, b.passed ? cyan : grey);
                }
                if (Tr.mine) Text("MY OPTIONS: " + MyOptionsLine(Tr.MyRules), 0, -0.405f, 0.0155f, cyan);
                else
                {
                    int tot = Tr.bests.Total();
                    Text($"TRAINING SCORE {tot / 10}.{tot % 10}  (EVERY LESSON'S BEST ACCURACY ADDED UP)", 0, -0.405f, 0.017f, grey);
                }
                if (NetMode != Online.Net.Mode.Offline) Text("LEAVE THE ROOM TO TRAIN", 0, -0.51f, 0.02f, white);
                return;
            }
            {   // a lesson: its card, its result, or paused
                var tr = Tr.trainer;
                var Ln = tr.L;
                int i = tr.lesson;
                string F1(float v) => v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                int place = Sim.Training.Lessons.PlaceInTrack(i, out int n);
                Text($"{Sim.Training.Lessons.TrackName((int)Ln.track)} - LESSON {place} OF {n}{(tr.custom ? " - WITH MY OPTIONS" : "")}", 0, 0.30f, 0.022f, cyan);
                Text(Ln.name, 0, 0.245f, 0.05f, gold);
                string Goal()
                {
                    switch (Ln.kind)
                    {
                        case Sim.Training.Kind.THROW: return $"TAKE ALL {Ln.Goal()} TARGETS IN {Ln.budget} THROWS";
                        case Sim.Training.Kind.STRIKES: return $"{Ln.pass} STRIKES IN {Ln.budget} THROWS";
                        case Sim.Training.Kind.CATCH: return $"CATCH {Ln.pass} OF {Ln.budget}";
                    }
                    if (Ln.guard == Sim.Training.Guard.G_SENDBACK) return $"SEND THROWS INTO ALL {Ln.Goal()} TARGETS - {Ln.budget} THROWS COME";
                    string verb = Ln.guard == Sim.Training.Guard.G_SHIELD ? "BLOCK" : Ln.guard == Sim.Training.Guard.G_KNUCKLE ? "PUNCH AWAY"
                                : Ln.guard == Sim.Training.Guard.G_EITHER ? "BLOCK OR PUNCH" : "DODGE";
                    return $"{verb} {Ln.pass} OF {Ln.budget} THROWS";
                }
                var b = (tr.custom ? Tr.bestsMine : Tr.bests).b[Ln.id];   // (CUSTOM TRAINING: the mode's)
                string yb = tr.custom ? "YOUR BEST WITH MY OPTIONS" : "YOUR BEST";
                string best = !b.passed ? yb + ": NOT PASSED YET"
                            : Ln.Timed() ? $"{yb}: {F1(b.time)} S - {b.successes} / {b.attempts} ({b.Permille() / 10}%)"
                            : $"{yb}: {b.successes} / {b.attempts} ({b.Permille() / 10}%)";
                if (tr.phase == Sim.Training.Phase.Card)
                {
                    Text(Ln.teach, 0, 0.18f, 0.028f, white);
                    Text(Ln.tip1, 0, 0.11f, 0.021f, white);
                    Text(Ln.tip2, 0, 0.065f, 0.021f, white);
                    Text(Goal(), 0, -0.01f, 0.024f, gold);
                    Text(best, 0, -0.07f, 0.019f, grey);
                    Text("A VIDEO OF THIS LESSON IS COMING", 0, -0.13f, 0.019f, cyan);
                    Text("START (OR PRESS " + a.ReadyName + (Ln.Timed() ? "): 3-2-1, THEN THE CLOCK RUNS" : ") - ONLY ACCURACY COUNTS HERE"), 0, -0.17f, 0.019f, grey);
                }
                else if (tr.phase == Sim.Training.Phase.Done)
                {
                    var r = tr.result;
                    Text(r.passed ? "PASSED!" : "NOT THIS TIME", 0, 0.16f, 0.06f, r.passed ? gold : white);
                    if (Ln.Timed() && r.passed) Text($"TIME {F1(r.time)} S", 0, 0.08f, 0.032f, white);
                    Text($"ACCURACY {r.successes} / {r.attempts} ({r.Permille() / 10}%)", 0, 0.03f, 0.028f, white);
                    if (Tr.newBest) Text("A NEW BEST!", 0, -0.03f, 0.03f, gold);
                    if (!r.passed) Text(Goal(), 0, -0.03f, 0.022f, grey);
                    Text(best, 0, -0.09f, 0.019f, grey);
                    int nx = Sim.Training.Lessons.NextInTrack(i);
                    if (r.passed && nx >= 0) Text($"NEXT: {Ls[nx].name} - {Ls[nx].teach}", 0, -0.15f, 0.018f, cyan);
                    if (r.passed && tr.custom) Text("WITH MY OPTIONS: KEPT ON THIS HEADSET, NOT FOR THE LEADERBOARDS", 0, -0.20f, 0.016f, grey);
                    else if (r.passed) Text(net != null && net.OnlineReady ? "SENT TO THE LEADERBOARDS" : "KEPT ON THIS HEADSET - SENT WHEN YOU GO ONLINE", 0, -0.20f, 0.016f, grey);
                    Text("PRESS " + a.ReadyName + (r.passed && nx >= 0 ? " FOR THE NEXT LESSON" : " TO TRY AGAIN"), 0, -0.235f, 0.018f, cyan);
                }
                else Text("PAUSED", 0, 0.178f, 0.028f, white);
            }
        }

        public void Draw(FxRenderer fx, Pose aim, bool aimValid)
        {
            if (!open) return;
            Vec3 At(float x, float y, float z) => pose.Transform(new Vec3(x, y, z));
            void Quad(float x, float y, float w, float h, float z, Vector4 c) => fx.Panel(new Pose(pose.q, At(x, y, z)), w, h, c);
            void Text(string s, float x, float y, float h, Vector4 c)
            {
                if (Mathf.Abs(x) < 0.5f)
                {   // on the panel: keep inside its edges (the font's glyphs are 0.75 x their height)
                    float room = 2f * (0.485f - Mathf.Abs(x)), w = 0.75f * h * s.Length;
                    if (w > room) h *= room / w;
                }
                fx.Text(s, new Pose(pose.q, At(x, y, 0.006f)), h, c);
            }
            Vector4 bg = V(0.004f, 0.007f, 0.016f, 0.90f), edge = V(0.05f, 0.35f, 0.60f, 0.9f);
            Vector4 btn = V(0.03f, 0.05f, 0.08f, 0.95f), hov = V(0.05f, 0.40f, 0.75f, 0.98f), dis = V(0.015f, 0.015f, 0.02f, 0.9f);
            Vector4 white = V(1, 1, 1), grey = V(0.35f, 0.38f, 0.42f), cyan = V(0.3f, 0.85f, 1.0f), amber = V(1.0f, 0.75f, 0.2f);
            Quad(0, -0.09f, 1.04f, 0.98f, -0.002f, edge);
            Quad(0, -0.09f, 1.0f, 0.94f, 0, bg);
            if (!nameEdit && !TrainSide())
            {   // side menu
                Quad(NavX, -0.09f, 0.30f, 0.98f, -0.002f, edge);
                Quad(NavX, -0.09f, 0.28f, 0.94f, 0, bg);
            }
            if (!a.statsNoticeOpen && !nameEdit && a.studio != null && a.studio.on && !OtherPage() && Tr.board < -1 && !Tr.open)
                DrawStudio(Text, white, grey, cyan);
            else if (!a.statsNoticeOpen && !nameEdit && (Tr.board >= -1 || Tr.open || (Tr.Active && !OtherPage())))
            {   // TRAINING's pages (native drawMenu's): a leaderboard, the lessons, a lesson's card, result or pause
                DrawTraining(Text, white, grey, cyan);
            }
            else if (a.statsNoticeOpen && !nameEdit)
            {
                Text("ONLINE MATCH STATISTICS", 0, 0.27f, 0.045f, cyan);
                Text("WHEN YOU PLAY AN ONLINE MATCH AGAINST ANOTHER PLAYER,", 0, 0.16f, 0.021f, white);
                Text("THE COMMUNITY SERVER KEEPS YOUR CAREER STATISTICS:", 0, 0.12f, 0.021f, white);
                Text("MATCHES, WINS, THROWS, HITS, BLOCKS, CATCHES, TOP SPEED", 0, 0.07f, 0.019f, grey);
                Text("THEY ARE SHOWN TO OTHER PLAYERS AND SPECTATORS WITH YOUR NAME", 0, 0.01f, 0.021f, white);
                Text("NO ACCOUNT, NO EMAIL: THEY ARE TIED TO A RANDOM KEY YOUR GAME MADE", 0, -0.04f, 0.019f, grey);
                Text("SEE OR DELETE THEM ANY TIME IN MENU - MY STATS", 0, -0.11f, 0.021f, white);
                Text("TO PLAY WITHOUT THEM: MULTIPLAYER - ONLINE: OFF", 0, -0.16f, 0.019f, grey);
                Text("PRIVACY POLICY: GITHUB.COM/JENDOEBELIN/PROJECTARENA-PUBLIC", 0, -0.22f, 0.016f, grey);
            }
            else if (statsOpen && !nameEdit) DrawStats(fx, At, Text);
            else if (bugOpen && !nameEdit)
            {
                Text("REPORT A BUG", 0, 0.29f, 0.055f, cyan);
                Text("WHAT KIND OF PROBLEM? PICK ONE, ADD A NOTE IF YOU LIKE", 0, 0.215f, 0.022f, white);
                Text("SENDS THE NOTE, YOUR SETTINGS AND THE GAME'S RECENT LOG - NOTHING ELSE", 0, -0.33f, 0.018f, grey);
                Text("TIP: REPORT RIGHT AFTER IT HAPPENS", 0, -0.37f, 0.018f, grey);
            }
            else if (rulesOpen && rulesStep == 1 && roomBouncePage && !nameEdit)
            {   // the room's BOUNCES (native 0.3.22)
                var t = new Tunables();
                OptionsInto(t);
                Text(RoomTitle[RoomOf(t)], 0, 0.29f, 0.055f, cyan);
                Text("BOUNCES: HOW THE BALL COMES OFF THE COURT, FOR EVERYONE IN THE MATCH", 0, 0.205f, 0.019f, white);
                Text("WALLS: LIVELY - A SIDE WALL TAKES LESS OF THE BALL'S SPEED", 0, -0.08f, 0.017f, grey);
                Text("WALL GRIP: THE COURT GRIPS THE BALL - IT LEAVES SPINNING, AND CURVES", 0, -0.12f, 0.017f, grey);
                Text("BANK ASSIST: A BANK'S LAST LEG IS NUDGED TOWARD THE OTHER PLAYER", 0, -0.16f, 0.017f, grey);
                Text("THESE CHANGE PLAY: WITH AN OLDER VERSION IN THE MATCH, STANDARD", 0, -0.21f, 0.017f, grey);
                Text("A MATCH WITH ANY OF THEM COUNTS AS CUSTOM IN THE STATS", 0, -0.25f, 0.017f, grey);
            }
            else if (rulesOpen && rulesStep == 1 && roomBallPage && !nameEdit)
            {   // the room's BALL FEEL
                var t = new Tunables();
                OptionsInto(t);
                Text(RoomTitle[RoomOf(t)], 0, 0.29f, 0.055f, cyan);
                Text("BALL FEEL: HOW THE BALL LOOKS AND FEELS FOR EVERYONE AT YOUR ARENA", 0, 0.205f, 0.019f, white);
                Text("CORE: A SOLID CENTRE IN THE GLOW - GLOW: TRUE SIZE SHOWS EXACTLY WHAT HITS", 0, -0.10f, 0.017f, grey);
                Text("SPIN: IT TURNS AS IT FLIES - WEIGHT: IT TRAILS YOUR HAND, THE CONTROLLER HUMS", 0, -0.14f, 0.017f, grey);
                Text("ONLY HOW IT LOOKS AND FEELS: NOTHING CHANGES IN PLAY", 0, -0.20f, 0.017f, grey);
            }
            else if (rulesOpen && rulesStep == 1 && roundsPad && !nameEdit)
            {   // BEST OF n typed
                var t = new Tunables();
                OptionsInto(t);
                Text(RoomTitle[RoomOf(t)], 0, 0.29f, 0.055f, cyan);
                Text("HOW MANY ROUNDS?", 0, 0.225f, 0.022f, white);
                Text("BEST OF " + roundsBuf + "_", 0, 0.165f, 0.045f, white);
                int nb = PadBestOf(), typed = roundsBuf.Length > 0 ? int.Parse(roundsBuf) : 0;
                if (roundsBuf.Length == 0) Text("AN ODD NUMBER, 1 TO 9", 0, -0.24f, 0.02f, grey);
                else if (nb > 0) Text(nb == 1 ? "ONE ROUND DECIDES THE MATCH" : "THE FIRST TO WIN " + (nb + 1) / 2 + " ROUNDS WINS THE MATCH", 0, -0.24f, 0.02f, grey);
                else if (typed > MaxBestOf) Text("AT MOST BEST OF 9", 0, -0.24f, 0.02f, amber);
                else Text("AN ODD NUMBER, SO A MATCH CAN'T END TIED: 1, 3, 5, 7 OR 9", 0, -0.24f, 0.02f, amber);
            }
            else if (rulesOpen && rulesStep == 1 && !nameEdit)
            {   // THE ROOM'S OPTIONS
                var t = new Tunables();
                OptionsInto(t);
                int room = RoomOf(t);
                Text(RoomTitle[room], -0.02f, 0.29f, Math.Min(0.055f, 0.50f / (0.75f * RoomTitle[room].Length)), cyan);   // (clear of DEFAULTS)
                bool withBot = !rulesForHost || (net != null ? net.BotFill : a.settings.botFill);
                float yb = BotRowY(t);
                Text(rulesForHost || NetMode == Online.Net.Mode.Host ? "ONLY THE HOST CAN CHANGE THESE" : "FOR PRACTICE AGAINST THE BOT", 0, 0.225f, 0.02f, white);
                bool blitz = room == 3;
                string hint = a.RulesLive && rulesEdit ? "A MATCH IS ON: THE NEW RULES START WITH THE NEXT MATCH"
                            : blitz && t.teamMode != Teams.FFA   // (0.3.24, TASKS.md E51: in teams, no friendly fire)
                                ? (t.Timed() ? "THE LAST TEAM STANDING WINS - NO FRIENDLY FIRE - AT TIME THE MOST HEALTH" : "THE LAST TEAM STANDING WINS - NO FRIENDLY FIRE")
                            : blitz ? (t.Timed() ? "LAST ONE STANDING WINS - AT TIME THE MOST HEALTH, A TIE: SUDDEN DEATH" : "NO CLOCK: THE LAST ONE STANDING WINS")
                            : !t.shields ? "HOTDOG MODE: NO SHIELDS - DODGE, CATCH AND PUNCH"
                            : t.MaidenNow() && room <= 1 ? "MAIDEN: A SHIELD WHILE YOU HOLD YOUR BALL, AND IT NEVER BREAKS"
                            : t.FreePlay() ? "FREE PLAY: NO CLOCK AND NO WINNER - PRACTICE OR PLAY ON"
                            : t.advanced && t.advancedRoundTime > 0 ? "AT TIME, MORE POINTS WINS THE ROUND - A TIE GOES TO SUDDEN DEATH"
                            : t.advanced ? (t.RoundsToWinNow() == 1 ? "NO TIME LIMIT: FIRST TO 4 POINTS WINS THE MATCH" : "NO TIME LIMIT: FIRST TO 4 POINTS WINS A ROUND")
                            : "AT TIME, MORE POINTS WINS - A TIE GOES TO SUDDEN DEATH";
                Text(hint, 0, withBot ? yb - 0.106f : yb + 0.02f, 0.017f, grey);
                if (withBot)
                {   // what the level means (every level moves as a person can: reach, speed, reaction)
                    string[] botLine = { "SLOW TO REACT, OFTEN MISREADS THE BALL - A GENTLE START", "BLOCKS SOME, MISSES CURVES AND BANK SHOTS",
                                         "A SOLID PLAYER - READS CURVES AND BANKS", "QUICK HANDS, ANGLES ITS BLOCKS, PICKS ITS SHOTS",
                                         "RARELY MISSES - GUARDS ITS STRIKE ZONE", "READS EVERY BALL - CAN YOU SCORE ONE?" };
                    Text(t.botAdapt ? "IT GETS BETTER WHEN YOU SCORE, EASIER WHEN IT DOES - CLOSE MATCHES"
                                    : botLine[Math.Max(0, Math.Min(Bot.Levels - 1, t.botLevel))], 0, yb - 0.08f, 0.017f, white);
                }
            }
            else if (rulesOpen && !nameEdit)
            {   // CHOOSE A ROOM: each room from above (the pictures over the buttons: after them, below), its name, who it's for
                Text("CHOOSE A ROOM", 0, 0.29f, 0.055f, cyan);
                Text(rulesForHost ? "FOR YOUR ROOM - EVERYONE WHO JOINS PLAYS ITS RULES" : "FOR PRACTICE AGAINST THE BOT", 0, 0.222f, 0.02f, white);
            }
            else if (movePlayersOpen && moveHost && !nameEdit)
            {
                Text("HAND OFF HOST", 0, 0.29f, 0.055f, cyan);
                Text("WHO HOSTS THIS ROOM FROM NOW ON? YOU STAY IN IT", 0, 0.215f, 0.022f, white);
                Text("A MATCH PAUSES WHILE THE ROOM CHANGES HANDS, THEN GOES ON", 0, -0.22f, 0.019f, grey);
            }
            else if (movePlayersOpen && !nameEdit)
            {
                Text("MOVE PLAYERS", 0, 0.29f, 0.055f, cyan);
                Text("POINT AT ONE CORNER, THEN ANOTHER: THEY SWAP", 0, 0.215f, 0.022f, white);
                Text(a.game.np == 4 && NetMode == Online.Net.Mode.Host ? "TEAMS GO WITH THE CORNER - THE MATCH STARTS AGAIN"
                                                                       : "ONLY WHILE YOU HOST A ROOM FOR FOUR", 0, -0.22f, 0.019f, grey);
            }
            else if (reportOpen && !nameEdit)
            {
                Text("REPORT OR MUTE A PLAYER", 0, 0.29f, 0.050f, cyan);
                if (People().Count == 0)
                {
                    Text("NOBODY ELSE IS HERE", 0, 0.12f, 0.030f, white);
                    Text("JOIN A ROOM, OR STAND AT AN ARENA WHILE SPECTATING", 0, 0.06f, 0.020f, grey);
                }
                else Text("WHO? PICK THE PERSON, THEN WHY", 0, 0.215f, 0.022f, white);
                Text("SENT TO THE PROJECT ARENA TEAM WITH THE NAMES HERE AND THE RECENT LOG", 0, -0.50f, 0.017f, grey);
                Text("MUTE SILENCES THEIR VOICE FOR YOU UNTIL YOU QUIT THE GAME", 0, -0.53f, 0.017f, grey);
            }
            else if (titleOpen && !helpOpen && !nameEdit && !settingsOpen)
            {
                if (emblem != null)
                {   // the emblem floats above the panel, a soft glow behind it
                    Quad(0, 0.585f, 0.36f, 0.36f, -0.004f, edge);   // a badge like the panel (hides the scoreboard behind)
                    Quad(0, 0.585f, 0.33f, 0.33f, -0.002f, bg);
                    fx.Sprite(At(0, 0.585f, 0.0f), 0.42f, V(0.10f, 0.45f, 0.75f, 0.35f), a.viewHead.p, 1);
                    fx.Emblem(new Pose(pose.q, At(0, 0.585f, 0.004f)), 0.29f, 0.29f, white);
                }
                Text("PROJECT ARENA", 0, 0.27f, 0.085f, cyan);
                Text($"WELCOME, {a.settings.name}", 0, 0.185f, 0.03f, white);
                if (BestsShown()) DrawBests(fx, edge, bg, btn, hov, dis, white, grey, cyan);
            }
            else if (settingsOpen)
            {
                Text(btnPage ? "BUTTONS" : ctlPage ? "CONTROLLERS" : voicePage ? (mutedPage ? "MUTED PLAYERS" : "VOICE CHAT") : gfxPage ? "GRAPHICS AND VIEW" : ptPage ? "PASSTHROUGH" : "SETTINGS", 0, 0.29f, 0.055f, cyan);
                if (btnPage)
                {   // BUTTONS
                    Text("POINT AT A BUTTON TO CHANGE ITS JOB - THE TRIGGER AND MENU STAY AS THEY ARE", 0, 0.205f, 0.018f, white);
                    if (Time.realtimeSinceStartupAsDouble < btnNoteUntil) Text(btnNote, 0, -0.32f, 0.018f, amber);
                    else Text("A GRIP: TALK, PEEK, NAMES OR NOTHING - TALK, READY AND RECENTER KEEP A BUTTON", 0, -0.32f, 0.016f, grey);
                }
                if (ctlPage && !btnPage)
                {
                    Text("CONTROLLER ANGLE", 0, 0.065f, 0.032f, white);
                    Text("HOW YOUR HANDS SIT ON THE CONTROLLERS: WATCH THEM AS YOU SET IT", 0, -0.27f, 0.018f, grey);
                    Text("TILT: UP / DOWN - TWIST: ROLL - TURN: LEFT / RIGHT - THIS HEADSET ONLY", 0, -0.305f, 0.016f, grey);
                }
                if (ptPage) Text(RoomPassthrough.Help(a.settings.ptMode), 0, 0.205f, 0.022f, grey);
                if (gfxPage)
                {   // FIELD OF VIEW (native's): a strip behind the slider only, the arena showing round it
                    Quad(0, -0.085f, 0.88f, 0.25f, -0.001f, new Vector4(0.004f, 0.007f, 0.016f, 0.985f));
                    // (the effects above it: what SPECTATING means, or the frame-rate guard's doing; TASKS.md E38)
                    if (ArenaGraphics.guard) Text("FRAMES WERE DROPPING: BLOOM AND SMOOTH EDGES OFF UNTIL THIS MATCH ENDS", 0, 0.018f, 0.016f, V(1.0f, 0.8f, 0.3f));
                    else Text("SPECTATING: ONLY WHILE YOU WATCH - THESE CHANGE ONLY WHAT THIS HEADSET SHOWS", 0, 0.018f, 0.016f, grey);
                    Text("FIELD OF VIEW", 0, -0.022f, 0.034f, white);
                    Text("HOW BIG THE ARENA FEELS: + WIDER, - CLOSER. LOOK AROUND WHILE YOU SET IT", 0, -0.058f, 0.017f, grey);
                    for (int k = -5; k <= 5; ++k)   // a notch for each step, the chosen one lit
                    {
                        bool lit = k == a.settings.roomFeel;
                        Quad(0.032f * k, -0.165f, lit ? 0.022f : 0.012f, lit ? 0.04f : 0.022f, 0.004f, lit ? new Vector4(0.3f, 0.85f, 1.0f, 1) : new Vector4(0.10f, 0.16f, 0.22f, 1));
                    }
                }
                if (voicePage && !btnPage)
                    Text(mutedPage ? (a.mutes.list.Count == 0 ? "NOBODY IS MUTED. MUTE SOMEONE FROM REPORT PLAYER" : "KEPT ON THIS HEADSET: MUTED WHEREVER YOU MEET THEM")
                                   : "ONLY PEOPLE AT YOUR ARENA CAN HEAR YOU", 0, 0.205f, 0.024f, grey);
            }
            else if (helpOpen && creditsOpen)   // HOW TO PLAY > CREDITS (native's sizes and spacing)
            {
                Text("CREDITS", 0, 0.29f, 0.055f, cyan);
                float y = 0.205f;
                foreach (var (kind, text) in Credits)
                {
                    if (kind == CreditKind.Small && y > -0.20f) y = -0.20f;   // (the components and licences: at the foot)
                    Text(text, 0, y, kind == CreditKind.Head ? 0.016f : kind == CreditKind.Name ? 0.025f : 0.014f, kind == CreditKind.Name ? white : grey);
                    y -= kind == CreditKind.Head ? 0.028f : kind == CreditKind.Name ? 0.045f : 0.028f;
                }
            }
            else if (helpOpen)
            {
                Text("HOW TO PLAY", 0, 0.29f, 0.055f, cyan);
                // (BUTTONS: the names' and recenter's buttons as they are; the standard: LEFT STICK CLICK, B / RIGHT STICK CLICK)
                string names = Controls.JobButtons(a.Buttons, a.ctlKind, Controls.J_NAMES, true), rec = Controls.JobButtons(a.Buttons, a.ctlKind, Controls.J_RECENTER, true);
                string[] lines =
                {
                    "GRAB: REACH FOR YOUR BALL, HOLD THE TRIGGER",
                    "THROW: SWING AND RELEASE - FLICK TO CURVE IT",
                    "HIT YOUR OPPONENT TO SCORE",
                    "HOLDING A CHARGED BALL RAISES A SHIELD",
                    "PUNCH BALLS AWAY WITH YOUR KNUCKLE GUARD",
                    "BALL THROUGH THEIR RING = STRIKE: BIGGER, FASTER",
                    "THROW PAST YOUR FOUL LINE TO RECHARGE",
                    names.Length == 0 ? "PLAYERS' NAMES: NO BUTTON (SETTINGS > CONTROLLERS)" : names + ": PLAYERS' NAMES",
                    rec + ": RECENTER   MENU: GAMES",
                };
                for (int i = 0; i < 9; ++i) Text(lines[i], 0, 0.20f - 0.055f * i, 0.024f, i >= 7 ? grey : white);
            }
            else if (nameEdit)
            {
                Text(editTarget == 5 ? "REPORT NOTE" : editTarget == 4 ? "BUG NOTE" : editTarget == 2 ? "TEAM NAME" : editTarget == 1 ? "COMMUNITY SERVER" : "YOUR NAME", 0, 0.29f, 0.055f, cyan);
                string shown = nameBuf + "_";
                if (shown.Length > 34) shown = "..." + shown.Substring(shown.Length - 31);   // the end of a long note
                Text(shown, 0, 0.205f, 0.040f, white);
                int cap = editTarget >= 4 ? Reports.NoteMax : editTarget == 1 ? 48 : 16;
                int left = Math.Max(0, cap - nameBuf.Length);
                Text($"{left} LEFT", 0.40f, 0.155f, 0.020f, left < 10 ? amber : grey);   // bottom right of the field
            }
            else if (!titleOpen && !settingsOpen)
            {
                Text("MULTIPLAYER", 0, 0.29f, 0.055f, cyan);
                string status = net != null ? net.Status() : "PRACTICE VS BOT";
                if (net != null && net.InRoom) Text($"YOU: {a.settings.name}   -   {status}   -   {RoomName(Tun)}", 0, 0.215f, 0.026f, white);
                else Text($"YOU: {a.settings.name}   -   {(a.InLobby ? "LOBBY" : status)}", 0, 0.215f, 0.026f, white);
                if (net != null && net.Online && !net.OnlineReady) Text("CONNECTING TO THE SERVER...", 0.25f, 0.165f, 0.020f, amber);
                else Text(net != null && net.Online ? "GAMES ONLINE" : "GAMES ON YOUR NETWORK", 0.25f, 0.165f, 0.026f, grey);
            }
            var items = Items();
            int cur = NavCurrent(), games = 0;
            for (int i = 0; i < items.Count; ++i)
            {
                Item it = items[i];
                bool row = (it.act == Act.Join || it.act == Act.JoinRoom) && it.ip != 0;
                if ((it.act == Act.Join || it.act == Act.JoinRoom || it.act == Act.Watch) && it.ip != 0) ++games;
                bool picked = (it.act == Act.Nav && (int)it.ip == cur) || (it.act == Act.BugCat && (int)it.ip == bugCat) ||
                              (it.act == Act.RulesPick && (int)it.ip == (optAdvanced ? 1 : 0)) ||
                              (it.act == Act.RoomPick && (int)it.ip == RoomOf(Tun)) || (it.act == Act.ReportWho && it.ip == reportWho) ||
                              (it.act == Act.ReportWhy && (int)it.ip == reportWhy) || (it.act == Act.MoveCorner && (int)it.ip == moveSel) ||
                              (it.act == Act.PtMode && (int)it.ip == a.settings.ptMode) ||
                              (it.act == Act.BtnPreset && Controls.SameMap(a.settings.buttons, it.ip != 0 ? Controls.LeftHanded() : Controls.Standard())) ||
                              (it.act == Act.OptBot && it.ip >= 10 &&
                               (int)it.ip - 10 <= (optBotAdapt ? (int)MathF.Round(Tun.botMatch, MidpointRounding.AwayFromZero) : optBotLevel)) ||
                              (it.act == Act.OptBot && it.ip == 2 && optBotAdapt) ||
                              (it.act == Act.StudioSection && a.studio != null && (int)it.ip == a.studio.section) ||
                              (it.act == Act.StudioVote && a.studio != null && a.studio.VoteLit((int)(it.ip & 0xFFu), (it.ip & 0x100u) != 0 ? 1 : -1));
                Quad(it.x, it.y, it.w, it.h, 0.003f, !it.enabled && !row ? dis : (i == hover || picked) ? hov : btn);
                float th = row ? 0.026f : 0.034f;
                float tw = th * 0.75f * it.label.Length;          // the font's glyphs are 0.75 x their height
                if (tw > it.w * 0.90f) th *= it.w * 0.90f / tw;    // long labels shrink instead of spilling over
                Text(it.label, it.x, it.y, th, it.enabled || row ? white : grey);
            }
            if (rulesOpen && rulesStep == 0 && !nameEdit)
            {   // CHOOSE A ROOM: each room from above (room_pictures: a quarter each), its name, who it's for (how it plays is
                // the room's options; the owner: the match's format is one of them)
                string[] line = { "TWO PLAYERS", "TWO PLAYERS", "FOUR PLAYERS", "UP TO FOUR PLAYERS" };
                // (raised buttons, the owner 2026-11-17, native 7ed11aa: each stands out from the panel, its face 1.1 cm proud
                // (1.6 lit: pointed at, or the room now), a shadow on the panel below and to the right, its top and left
                // edges lit, the others shaded)
                Vector4 shadow = V(0f, 0f, 0f, 0.6f), edgeHi = V(0.35f, 0.65f, 0.95f, 1f), edgeLo = V(0.008f, 0.015f, 0.03f, 1f);
                for (int k = 0; k < 4; ++k)
                {
                    float x = k % 2 != 0 ? 0.205f : -0.205f, y = k < 2 ? 0.07f : -0.195f, w = 0.39f, h = 0.25f;
                    bool lit = hover == k || RoomOf(Tun) == k;   // (the first four items: these buttons)
                    float z = lit ? 0.016f : 0.011f;
                    Quad(x + 0.010f, y - 0.012f, w, h, 0.0015f, shadow);
                    Quad(x, y, w, h, z - 0.002f, lit ? hov : btn);
                    Quad(x, y + h * 0.5f - 0.004f, w, 0.008f, z - 0.001f, edgeHi);
                    Quad(x - w * 0.5f + 0.004f, y, 0.008f, h, z - 0.001f, edgeHi);
                    Quad(x, y - h * 0.5f + 0.004f, w, 0.008f, z - 0.001f, edgeLo);
                    Quad(x + w * 0.5f - 0.004f, y, 0.008f, h, z - 0.001f, edgeLo);
                    // (the picture's quarter: native's uv offset 0.5 (k % 2, k / 2), its first rows at the top of the image;
                    // the words higher, clear of the button's bottom edge: the owner, 11-17)
                    float u0 = 0.5f * (k % 2), v1 = 1f - 0.5f * (k / 2);
                    fx.Picture(new Pose(pose.q, At(x, y + 0.032f, z)), 0.29f, 0.18125f, white, u0, u0 + 0.5f, v1 - 0.5f, v1);
                    fx.Text(RoomTitle[k], new Pose(pose.q, At(x, y - 0.078f, z + 0.001f)), 0.025f, white);
                    fx.Text(line[k], new Pose(pose.q, At(x, y - 0.100f, z + 0.001f)), 0.014f, white);
                }
            }
            if (games == 0 && !nameEdit && !helpOpen && !titleOpen && !settingsOpen && !xpOpen && !bugOpen && !reportOpen && !rulesOpen &&
                !statsOpen && !a.statsNoticeOpen && net != null)
            {
                int ov = net.OtherVersion;
                if (ov != 0)
                {
                    Text($"A GAME ON VERSION {ov} IS NEARBY", 0.25f, -0.12f, 0.022f, amber);
                    Text("UPDATE BOTH HEADSETS TO PLAY", 0.25f, -0.16f, 0.022f, amber);
                }
                Text(a.spectating ? "NO LIVE GAMES YET" : "NO GAMES FOUND YET", 0.25f, 0.08f, 0.026f, grey);
                Text("ON THE OTHER HEADSET:", 0.25f, 0.0f, 0.022f, grey);
                Text("MENU - HOST A ROOM", 0.25f, -0.05f, 0.022f, grey);
                if (ov == 0) Text(net.Online ? "WITH ONLINE: ON" : "WITH ONLINE: OFF (LAN)", 0.25f, -0.10f, 0.022f, grey);
            }
            if (!statsOpen) Text("POINT AND PULL THE TRIGGER TO SELECT", 0, -0.455f, 0.022f, grey);
            if ((titleOpen || settingsOpen) && !nameEdit)   // the version, bottom right (native, owner 2026-10-30)
                Text("VERSION " + Application.version, 0.37f, -0.487f, 0.016f, grey);
            if (a.spectating && a.watchIp != 0 && !titleOpen && !settingsOpen && !nameEdit && !helpOpen && a.director != null)
                Text($"STICKS FLY - A SAVE CAMERA - X NEXT - Y AUTO CAMERA   ({a.director.warps.Count} CAMERAS)", 0, -0.495f, 0.02f, grey);
            // laser + cursor
            if (aimValid)
            {
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                float len = hitValid ? M.Length(hit - o) : 1.5f;
                fx.Box(new Pose(aim.q, o + d * (len * 0.5f)), new Vec3(0.003f, 0.003f, len * 0.5f), V(0.15f, 0.6f, 1.0f));
                if (hitValid) fx.Glow(hit, 0.008f, cyan);
            }
        }
    }
}
