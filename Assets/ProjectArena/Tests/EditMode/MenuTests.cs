// The menus, settings and lobby (Runtime/Menu.cs, AppSettings.cs, Lobby.cs), as the native menuActivate,
// saveSettings / loadSettings and lobbyRayHit behave.
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class MenuTests
    {
        string folder;
        AppState app;
        Menu menu;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "pa_menu_test_" + System.Guid.NewGuid().ToString("N"));
            var g = new Game();
            g.Reset(true);
            app = new AppState { game = g, settings = new AppSettings(folder), lobby = true };
            app.settings.Load(g.tun);
            menu = new Menu(app);
            menu.Open(new Pose(Quat.Identity, Lobby.Spot + new Vec3(0, 1.6f, 0)), Lobby.Spot);
            menu.titleOpen = true;
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        Menu.Item Find(string label) => menu.Items().First(i => i.label == label);
        void Click(string label) => menu.Activate(Find(label));
        void VolUp() => menu.Activate(menu.Items().First(i => i.act == Menu.Act.VolUp));
        // CHOOSE A ROOM (TASKS.md E22): its picture buttons, 0 SQUARE, 1 DIAMOND, 2 FOUR CORNERS, 3 FIREBALL BLITZ
        void PickRoom(int k) => menu.Activate(menu.Items().First(i => i.act == Menu.Act.RoomPick && i.ip == (uint)k));

        [Test]
        public void PracticeGoesThroughTheRulesAndMatchOptions()
        {
            Assert.That(Find("MULTIPLAYER").enabled, "MULTIPLAYER is on (phase 5)");
            Click("PLAY PRACTICE");
            Assert.That(menu.rulesOpen && menu.rulesStep == 0, "CHOOSE A ROOM first");
            PickRoom(0);
            Assert.That(menu.rulesStep, Is.EqualTo(1), "then the SQUARE's options");
            Click("ROUNDS");
            Assert.That(Find("KNUCKLES: OFF").enabled, "ROUNDS (Advanced's rules): no knuckle guards as the rules say");
            Click("SHIELDS: ON");
            Assert.That(Find("SHIELDS: OFF (HOTDOG)").enabled);
            Click("STRIKES: REGULATION");   // (STRIKE SPEED beside SHIELDS, native 0.3.19)
            Assert.That(menu.optStrikeSpeed, Is.EqualTo(1), "STRIKES: REGULATION -> FAST");
            Click("KNUCKLES: OFF");
            Assert.That(menu.optKnuckle, Is.EqualTo(1), "knuckle guards turned on: stored as 'on'");
            Click("KNUCKLES: ON");
            Assert.That(menu.optKnuckle, Is.EqualTo(-1), "back to the rules' own setting: 'as the rules say'");
            menu.Activate(menu.Items().First(i => i.act == Menu.Act.OptTime && i.ip == 1));
            Assert.That(menu.optRoundTime, Is.EqualTo(60f), "round time: OFF -> 1:00");
            // FOUL LINE beside KNUCKLES (TASKS.md E3, native 0.3.20): REGULATION, LONGER 1, 2, SHORTER 2, 1, round again
            string[] foul = { "FOUL LINE: REGULATION", "FOUL LINE: LONGER 1", "FOUL LINE: LONGER 2", "FOUL LINE: SHORTER 2", "FOUL LINE: SHORTER 1" };
            for (int k = 0; k < 5; ++k)
            {
                Assert.That(Find(foul[k]).enabled, foul[k]);
                Click(foul[k]);
            }
            Click("FOUL LINE: REGULATION");
            Assert.That(menu.optFoulShift, Is.EqualTo(1), "FOUL LINE: LONGER 1");
            Assert.That(app.game.tun.advanced, Is.False, "nothing changes until START PRACTICE");
            Click("START PRACTICE");
            var t = app.game.tun;
            Assert.That(t.advanced && !t.shields && t.advancedRoundTime == 60f && t.knuckleMode == -1 && t.strikeSpeed == 1 && t.foulShift == 1,
                        "the match takes the options");
            Assert.That(Scoreboard.RulesLine(t), Does.EndWith(" - FOUL LINE LONGER 1"), "the rules line says the line moved");
            Assert.That(app.startPractice && !app.lobby && !menu.open, "into the court against the bot, the menu closed");
            var reread = new Tunables();
            new AppSettings(folder).Load(reread);
            Assert.That(reread.advanced && !reread.shields && reread.advancedRoundTime == 60f && reread.strikeSpeed == 1 && reread.foulShift == 1,
                        "the options are saved (settings.cfg; foulline as natively)");
        }

        // ROOMS AND THEIR OPTIONS (TASKS.md E22, native d4cf079): CHOOSE A ROOM's four pictures, the SQUARE's options
        // (SHIELDS ON / OFF / ALWAYS, AIM ASSIST, STRIKE ZONE, BALL GROWTH), TIMED MATCH or ROUNDS with BEST OF - / + and
        // its number pad (odd, 1 to 9), DIAMOND with either match and no FOUL LINE, the rules line naming the room and match
        [Test]
        public void RoomsAndTheirOptions()
        {
            Click("PLAY PRACTICE");
            Assert.That(menu.Items().Count(i => i.act == Menu.Act.RoomPick), Is.EqualTo(4), "CHOOSE A ROOM: four pictures");
            PickRoom(0);
            Assert.That(Find("TIMED MATCH").enabled && !Find("BEST OF 3").enabled && Find("SHIELDS: ON").enabled && Find("AIM ASSIST: MEDIUM").enabled &&
                        Find("STRIKE ZONE: REGULATION").enabled && Find("BALL GROWTH: REGULATION").enabled && Find("FOUL LINE: REGULATION").enabled,
                        "the SQUARE: a TIMED MATCH (BEST OF only for ROUNDS), each option at the rules' own (AIM ASSIST MEDIUM: Basic's)");
            Click("SHIELDS: ON"); Click("SHIELDS: OFF (HOTDOG)");
            Assert.That(Find("SHIELDS: ALWAYS (MAIDEN)").enabled && menu.optMaiden, "SHIELDS: ON, OFF (HOTDOG), ALWAYS (MAIDEN)");
            Click("SHIELD SIZE: REGULATION"); Click("SHIELD SIZE: +25%");   // (TASKS.md E25: REGULATION, +25%, +50%, +75%, DOUBLE)
            Assert.That(Find("SHIELD SIZE: +50%").enabled && menu.optShieldSize == 2, "SHIELD SIZE: REGULATION -> +25% -> +50%");
            Click("AIM ASSIST: MEDIUM");
            Assert.That(Find("AIM ASSIST: HIGH").enabled && menu.optAim == 3, "AIM ASSIST: MEDIUM -> HIGH");
            Click("AIM ASSIST: HIGH"); Click("AIM ASSIST: OFF"); Click("AIM ASSIST: LOW");
            Assert.That(Find("AIM ASSIST: MEDIUM").enabled && menu.optAim == -1, "... OFF, LOW, and MEDIUM again: kept as the rules' own");
            Click("AIM ASSIST: MEDIUM");
            Click("STRIKE ZONE: REGULATION");
            Assert.That(Find("STRIKE ZONE: LARGE").enabled, "STRIKE ZONE: REGULATION -> LARGE");
            Click("BALL GROWTH: REGULATION");
            Assert.That(Find("BALL GROWTH: NONE").enabled, "BALL GROWTH: REGULATION -> NONE");
            Click("BALL GROWTH: NONE");
            Click("ROUNDS");
            Assert.That(Find("BEST OF 3").enabled && Find("AIM ASSIST: HIGH").enabled, "ROUNDS: BEST OF 3; the aim assist chosen stays");
            Menu.Item Rounds(uint up) => menu.Items().First(i => i.act == Menu.Act.OptRounds && i.ip == up);
            menu.Activate(Rounds(1));
            Assert.That(Find("BEST OF 5").enabled, "+: BEST OF 5");
            for (int i = 0; i < 5; ++i) menu.Activate(Rounds(1));
            Assert.That(Find("BEST OF 9").enabled && !Rounds(1).enabled, "at most BEST OF 9");
            Click("BEST OF 9");
            Assert.That(menu.roundsPad, "BEST OF: typed on a number pad");
            Click("4");
            Assert.That(!Find("SET").enabled, "4: no (an odd number, so a match can't end tied)");
            Click("DEL"); Click("1"); Click("1");
            Assert.That(!Find("SET").enabled, "11: no (at most BEST OF 9)");
            Click("DEL"); Click("DEL"); Click("5");
            Click("SET BEST OF 5");
            Assert.That(!menu.roundsPad && Find("BEST OF 5").enabled && menu.optRounds == 3, "SET BEST OF 5: three rounds won");
            // BOUNCES (TASKS.md E32, native 5d3bbec): its own page, WALLS, WALL GRIP, BANK ASSIST, BACK TO STANDARD
            Click("BOUNCES: STANDARD");
            Assert.That(menu.roomBouncePage && Find("WALLS: STANDARD").enabled && Find("WALL GRIP: OFF").enabled && Find("BANK ASSIST: OFF").enabled &&
                        !Find("STANDARD BOUNCES").enabled, "BOUNCES: all STANDARD");
            Click("WALL GRIP: OFF"); Click("BANK ASSIST: OFF"); Click("BANK ASSIST: LOW");
            Assert.That(menu.optWallGrip && menu.optBankAssist == 2 && Find("BACK TO STANDARD").enabled, "WALL GRIP ON, BANK ASSIST MEDIUM");
            Click("BACK");
            Assert.That(!menu.roomBouncePage && Find("BOUNCES: CUSTOM").enabled, "BACK: BOUNCES: CUSTOM on the room's page");
            Click("START PRACTICE");
            var t = app.game.tun;
            Assert.That(t.advanced && !t.experimental && t.MaidenNow() && t.AimAssistNow() == 3 && t.StrikeZoneNow() == 2 && t.BallGrowthNow() == 1 &&
                        t.RoundsToWinNow() == 3 && t.ShieldSizeNow() == 2 && t.StatsMode() == 2, "the match takes them (a custom match)");
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - BEST OF 5 - MAIDEN - AIM ASSIST HIGH - LARGE STRIKE ZONE - BALL GROWTH QUARTER - SHIELDS +50% - WALL GRIP - BANK ASSIST MEDIUM"),
                        "the rules line: the room, its match and what differs");
            // DIAMOND: either match, all the SQUARE's options but the FOUL LINE
            menu.Open(new Pose(Quat.Identity, Lobby.Spot + new Vec3(0, 1.6f, 0)), Lobby.Spot);
            menu.titleOpen = true;
            Click("PLAY PRACTICE");
            PickRoom(1);
            Assert.That(Find("ROUNDS").enabled && Find("AIM ASSIST: HIGH").enabled && !menu.Items().Any(i => i.act == Menu.Act.OptFoul),
                        "DIAMOND: ROUNDS too, the options, no FOUL LINE (its foul zone is its platform)");
            Click("START PRACTICE");
            Assert.That(app.game.tun.experimental && app.game.tun.expRoom == Room.Diamond && app.game.tun.advanced &&
                        Scoreboard.RulesLine(app.game.tun).StartsWith("DIAMOND - BEST OF 5"), "DIAMOND - BEST OF 5");
        }

        // the practice bot on MATCH OPTIONS (task A8, native b1552c0 and 0804989): its level with - / + and the slider,
        // MATCH MY LEVEL, FIST BUMP FIRST; saved as natively (botlevel, botadapt, botmatch, botmeet)
        [Test]
        public void PracticeBotLevelMatchMyLevelAndFistBumpFirst()
        {
            Menu.Item Bot(uint ip) => menu.Items().First(i => i.act == Menu.Act.OptBot && i.ip == ip);
            string Level() => menu.Items().First(i => i.label.StartsWith("BOT: ")).label;
            Click("PLAY PRACTICE");
            PickRoom(0);
            Assert.That(Level(), Is.EqualTo("BOT: PRO"), "PRO by default");
            Assert.That(menu.Items().Count(i => i.act == Menu.Act.OptBot && i.ip >= 10), Is.EqualTo(6), "the slider: a step a level");
            menu.Activate(Bot(1));
            Assert.That(Level(), Is.EqualTo("BOT: ALL-STAR"), "+: a level up");
            menu.Activate(Bot(1));
            menu.Activate(Bot(1));
            Assert.That(Level() == "BOT: UNBEATABLE" && !Bot(1).enabled, "up to UNBEATABLE, then + stops");
            menu.Activate(Bot(10));
            Assert.That(Level() == "BOT: ROOKIE" && !Bot(0).enabled && Bot(1).enabled, "the slider's first step: ROOKIE, - stops");
            menu.Activate(Bot(14));
            Assert.That(Level(), Is.EqualTo("BOT: CHAMPION"), "the slider's fifth step");
            // BOT STYLE (TASKS.md E23, native 50b11ae): beside its level in the SQUARE and DIAMOND, BRAWLER by default
            Click("STYLE: BRAWLER");
            Assert.That(menu.optBotPlay == 1 && Find("STYLE: DUELLIST").enabled, "STYLE: BRAWLER -> DUELLIST");
            Click("STYLE: DUELLIST");
            Click("MATCH MY LEVEL");
            Assert.That(menu.optBotAdapt && Level() == "BOT: MATCH MY LEVEL (NOW PRO)", "MATCH MY LEVEL: from where it got to (PRO)");
            Assert.That(Bot(0).enabled && Bot(1).enabled, "- and + both lead out of it");
            app.game.tun.botMatch = 3.4f;   // (a few points against it)
            Assert.That(Level(), Is.EqualTo("BOT: MATCH MY LEVEL (NOW ALL-STAR)"), "the level it got to, the nearer's name");
            menu.Activate(Bot(0));
            Assert.That(!menu.optBotAdapt && menu.optBotLevel == 3, "-: a set level again, where MATCH MY LEVEL got to");
            Click("MATCH MY LEVEL");
            Assert.That(Find("FIST BUMP FIRST: OFF").enabled, "FIST BUMP FIRST: off by default");
            Click("FIST BUMP FIRST: OFF");
            Assert.That(menu.optBotMeet && Find("FIST BUMP FIRST: ON").enabled);
            Assert.That(!app.game.tun.botMeet && !app.game.tun.botAdapt, "nothing changes until START PRACTICE");
            Click("DEFAULTS");
            Click("PRESS AGAIN");
            Assert.That(menu.optBotAdapt && menu.optBotMeet && menu.optBotLevel == 3, "DEFAULTS: the bot stays as you chose it");
            Click("START PRACTICE");
            var t = app.game.tun;
            Assert.That(t.botAdapt && t.botMeet && t.botLevel == 3 && t.botMatch == 3.4f, "the match takes them");
            var reread = new Tunables();
            new AppSettings(folder).Load(reread);
            Assert.That(reread.botAdapt && reread.botMeet && reread.botLevel == 3 && System.MathF.Abs(reread.botMatch - 3.4f) < 1e-4f,
                        "saved as natively (settings.cfg)");
            string txt = File.ReadAllText(Path.Combine(folder, "settings.cfg"));
            StringAssert.Contains("\nbotlevel=3\nbotadapt=1\nbotmatch=3.40\n", txt);
            StringAssert.Contains("\nbotmeet=1\n", txt);
            // a native file's values clamped as natively
            File.WriteAllText(Path.Combine(folder, "settings.cfg"), "cfgver=8\nbotlevel=9\nbotadapt=0\nbotmatch=-2\nbotmeet=0\n");
            var c = new Tunables();
            new AppSettings(folder).Load(c);
            Assert.That(c.botLevel == 5 && !c.botAdapt && c.botMatch == 0f && !c.botMeet, "botlevel up to 5, botmatch 0 to 5");
            // hosting a room: the bot's level only where the bot fills in; FIST BUMP FIRST is practice's
            menu.open = true; menu.titleOpen = false;
            menu.rulesOpen = true; menu.rulesStep = 1; menu.rulesForHost = true;
            app.settings.botFill = true;
            Assert.That(menu.Items().Any(i => i.act == Menu.Act.OptBot) && !menu.Items().Any(i => i.act == Menu.Act.OptMeet),
                        "a room where the bot fills in: its level, no FIST BUMP FIRST");
            app.settings.botFill = false;
            Assert.That(!menu.Items().Any(i => i.act == Menu.Act.OptBot), "no bot in the room: no bot rows");
        }

        // EXPERIMENTAL (task C4, native 0.3.12): its rooms and FOUR CORNERS' teams on MATCH OPTIONS
        [Test]
        public void ExperimentalPracticeInFourCorners()
        {
            Click("PLAY PRACTICE");
            PickRoom(1);
            Assert.That(menu.rulesStep, Is.EqualTo(1), "DIAMOND, then its options");
            Assert.That(!menu.Items().Any(i => i.act == Menu.Act.OptTeams), "DIAMOND is for two: no teams");
            Click("BACK");
            PickRoom(3);   // (FIREBALL BLITZ takes the shields off: its own default)
            Click("BACK");
            PickRoom(2);   // (and back from it: the rules' own)
            Assert.That(menu.optShields && menu.optKnuckle == -1, "FOUR CORNERS after FIREBALL BLITZ: the rules' own shields and knuckle guards");
            Click("TEAMS: FREE-FOR-ALL");
            Click("TEAMS: 2 V 2");
            Assert.That(menu.Items().Any(i => i.label == "3 V 1: YOU ARE THE ONE"), "FREE-FOR-ALL, 2 V 2, 3 V 1");
            Click("3 V 1: YOU ARE THE ONE");
            Assert.That(menu.Items().Any(i => i.label == "TEAMS: FREE-FOR-ALL"), "practice: THE ONE doesn't take turns, back round");
            Click("TEAMS: FREE-FOR-ALL");   // (-> 2 V 2)
            Click("START PRACTICE");
            var t = app.game.tun;
            Assert.That(t.experimental && t.expRoom == Room.Corners && t.players == 4 && t.teamMode == Teams.TwoVTwo && !t.advanced,
                        "practice in FOUR CORNERS, 2 v 2, Basic's rules");
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("FOUR CORNERS - 2 V 2"), "the rules line names the room");
            var reread = new Tunables();
            new AppSettings(folder).Load(reread);
            Assert.That(reread.experimental && reread.expRoom == Room.Corners && reread.teamMode == Teams.TwoVTwo, "saved as natively");
        }

        // FIREBALL BLITZ (task A5): its room's own defaults (no shields, knuckle guards on), HEALTH, BALLS and practice's
        // PLAYERS, the rules line, practice in the octagon, saved as natively
        [Test]
        public void ExperimentalPracticeInFireballBlitz()
        {
            Click("PLAY PRACTICE");
            PickRoom(3);
            // (TEAMS, as FOUR CORNERS', from 0.3.24: TASKS.md E51; FREE-FOR-ALL first, 3 V 1 gives THE ONE three times HEALTH)
            Assert.That(menu.Items().Any(i => i.act == Menu.Act.OptTeams && i.label == "TEAMS: FREE-FOR-ALL"), "TEAMS: everyone for themselves first");
            Click("TEAMS: FREE-FOR-ALL");
            Click("TEAMS: 2 V 2");
            Assert.That(menu.Items().Any(i => i.label == "3 V 1: YOU ARE THE ONE") && menu.Items().Any(i => i.label == "HEALTH: 5 - THE ONE 15"),
                        "3 V 1: YOU ARE THE ONE, with three times the HEALTH");
            Click("3 V 1: YOU ARE THE ONE");
            Assert.That(menu.Items().Any(i => i.label == "TEAMS: FREE-FOR-ALL"), "round again (practice: no taking turns)");
            Assert.That(!menu.optShields && !menu.Items().Any(i => i.act == Menu.Act.OptShields) && menu.Items().Any(i => i.label == "KNUCKLES: ON"),
                        "its own defaults: no shields (none to choose), knuckle guards on");
            Assert.That(menu.Items().Any(i => i.label == "HEALTH: 5") && menu.Items().Any(i => i.label == "BALLS: THREE") &&
                        menu.Items().Any(i => i.label == "PLAYERS: 4"), "HEALTH 5, BALLS: THREE, four playing");
            Click("HEALTH: 5");
            Assert.That(menu.Items().Any(i => i.label == "HEALTH: 10"), "HEALTH: 1, 3, 5, 10");
            Click("HEALTH: 10");
            Click("HEALTH: 1");
            Click("BALLS: THREE");
            Assert.That(menu.Items().Any(i => i.label == "BALLS: RAPID FIRE"), "BALLS: ONE, TWO, THREE, RAPID FIRE");
            Click("PLAYERS: 4");
            Assert.That(menu.Items().Any(i => i.label == "PLAYERS: 2"), "PLAYERS: 2, 3, 4");
            Click("PLAYERS: 2");
            Click("START PRACTICE");
            var t = app.game.tun;
            Assert.That(t.experimental && t.expRoom == Room.Blitz && t.players == 4 && t.health == 3 && t.fireBalls == 4 && t.octPlayers == 3 &&
                        !t.shields && t.KnuckleGuards(), "practice in FIREBALL BLITZ: HEALTH 3, RAPID FIRE, three playing, no shields");
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("FIREBALL BLITZ - HEALTH 3 - RAPID FIRE - HOTDOG"),
                        "the rules line: the room, its health and balls");
            var reread = new Tunables();
            new AppSettings(folder).Load(reread);
            Assert.That(reread.expRoom == Room.Blitz && reread.health == 3 && reread.fireBalls == 4 && reread.octPlayers == 3, "saved as natively");
        }

        // TRAINING (task A6): the lessons page (each track's first open), a lesson's card on the court, START and the
        // 3-2-1, the pause, the leaderboard page offline, MAIN MENU out to the lobby, the bests kept in training.cfg
        [Test]
        public void MovePlayersInFireballBlitzHasItsOwnTeams()
        {   // (TASKS.md E50, native 658b8f2: the page went by the team setting FOUR CORNERS left, 3 V 1 here; E51, native
            // 61589a0: FIREBALL BLITZ plays in teams from 0.3.24, the page by the teams it plays, Game.TeamsNow)
            app.net = new Online.Net();
            Game g = app.game;
            g.tun.experimental = true; g.tun.expRoom = Room.Corners; g.tun.players = 4; g.tun.teamMode = Teams.ThreeVOne;
            g.arenaKind = Arena.FourCorners;
            menu.titleOpen = false;
            menu.movePlayersOpen = true;
            Assert.That(menu.Items().Any(i => i.label == "MAKE THE ONE") && menu.Items().Any(i => i.label.EndsWith("  - THE ONE")),
                        "FOUR CORNERS 3 V 1: THE ONE and MAKE THE ONE");
            g.tun.expRoom = Room.Blitz;
            g.arenaKind = Arena.Octagon;
            Assert.That(menu.Items().Any(i => i.label == "MAKE THE ONE"), "FIREBALL BLITZ in 3 V 1 (0.3.24): THE ONE too");
            g.tun.matchVersion = Tunables.kVerBlitzTeams - 1;   // (a player before 0.3.24 in the room: everyone for themselves)
            var items = menu.Items();
            Assert.That(!items.Any(i => i.label == "MAKE THE ONE" || i.label.Contains("THE ONE") || i.label.Contains("THE THREE")),
                        "FIREBALL BLITZ everyone for themselves: no THE ONE, whatever the room's TEAMS says");
            Assert.That(items.Count(i => i.act == Menu.Act.MoveCorner) == 4 && items.Any(i => i.label.EndsWith("  - VIOLET")),
                        "each place by its colour");
            g.tun.teamMode = Teams.TwoVTwo;
            Assert.That(!menu.Items().Any(i => i.label.Contains(" TEAM")), "nor teams");
        }

        [Test]
        public void TrainingPagesAndALesson()
        {
            Assert.That(Find("TRAINING").enabled, "TRAINING on the title page (offline)");
            Click("TRAINING");
            Assert.That(app.training.open, "the lessons page");
            Assert.That(Find("1  FIRST THROWS").enabled && Find("1  SHIELD").enabled && Find("1  SIDESTEP").enabled, "each track's first lesson is open");
            Assert.That(!Find("2  PICK YOUR SPOT").enabled, "the next opens once one is passed");
            Click("LEADERBOARD");   // (native 3c285d9: LEADERBOARD beside MODE, TASKS.md E33)
            Assert.That(app.training.board == -1 && menu.Items().Any(i => i.label == "BACK"), "the overall leaderboard (offline: go online to see it)");
            Click("BACK");
            Click("1  FIRST THROWS");
            var tr = app.training.trainer;
            Assert.That(tr.Active && tr.phase == Sim.Training.Phase.Card && !app.lobby && !menu.open, "its card, on the court");
            var @in = new PlayerInput[4];
            for (int k = 0; k < 4; ++k) @in[k] = new PlayerInput();
            var mine = new PlayerInput { headTracked = true, head = new Pose(Quat.Identity, app.game.tun.start[0] + new Vec3(0, 1.65f, 0)) };
            app.localIn.CopyFrom(mine);
            app.training.menuAt = 1e-6;   // (as soon as you are on the court)
            app.training.Step(mine, @in, 1f / 72, menu, true);
            Assert.That(menu.open && menu.TrainSide() && Find("START").enabled && !Find("VIDEO COMING SOON").enabled,
                        "the card opens in front of you: START, no video yet, no side menu");
            Click("START");
            for (int i = 0; i < 72 * 4 && tr.phase != Sim.Training.Phase.Play; ++i) app.training.Step(mine, @in, 1f / 72, menu, true);
            Assert.That(tr.phase == Sim.Training.Phase.Play && tr.targets.Count(x => x.live && !x.down) == 1, "3-2-1, then the lesson: one target up");
            menu.Open(mine.head, app.game.tun.start[0]);
            Assert.That(Find("RESUME").enabled && Find("RESTART THE LESSON").enabled, "the menu button mid-lesson: a pause");
            float clock = tr.clock;
            app.training.Step(mine, @in, 1f / 72, menu, true);
            Assert.That(tr.clock, Is.EqualTo(clock), "paused while its menu is up");
            Assert.That(app.game.tun.basicMatchTime == 0f && app.RulesToSave.basicMatchTime == 180f, "the lesson plays without the match clock; yours is kept");
            app.settings.Save(app.RulesToSave);   // (a save during the lesson: native 0.3.19 saved the lesson's rules as yours)
            var mid = new Tunables();
            new AppSettings(folder).Load(mid);
            Assert.That(mid.basicMatchTime, Is.EqualTo(180f), "a save during a lesson keeps your own rules (the 3-minute match)");
            Click("MAIN MENU");
            Assert.That(!tr.Active && app.lobby && menu.titleOpen, "MAIN MENU: out of training, to the lobby");
            Assert.That(app.game.tun.start[1].y, Is.EqualTo(0f), "the game's rules as they were");
            var r = new Sim.Training.Result { lesson = 0, passed = true, successes = 5, attempts = 6, time = 31.5f };
            app.training.bests.Add(r);
            app.training.Save();
            var again = new TrainingApp(app);
            again.Load();
            Assert.That(again.bests.b[1].passed && again.bests.b[1].time == 31.5f && again.bests.Unlocked(1), "bests kept in training.cfg; the next lesson open");
        }

        [Test]
        public void CrowdIsFlagsAndLightsOrOff()
        {   // (TASKS.md E53, native d511eb1: no sound from the fans; SETTINGS > CROWD: FLAGS AND LIGHTS or OFF)
            Click("SETTINGS");
            Assert.That(Find("CROWD: FLAGS AND LIGHTS").enabled, "CROWD: FLAGS AND LIGHTS by default");
            Click("CROWD: FLAGS AND LIGHTS");
            Assert.That(app.settings.crowdMode == 2 && menu.Items().Any(i => i.label == "CROWD: OFF"), "then OFF");
            Click("CROWD: OFF");
            Assert.That(app.settings.crowdMode == 0, "and back: two states");
            app.settings.crowdMode = 1;   // (an old file's FLAGS ONLY)
            Assert.That(menu.Items().Any(i => i.label == "CROWD: FLAGS AND LIGHTS"), "an old file's FLAGS ONLY reads as FLAGS AND LIGHTS");
            Click("CROWD: FLAGS AND LIGHTS");
            Assert.That(app.settings.crowdMode == 2, "... and turns OFF");
        }

        [Test]
        public void FourPlayerBotsAreNamedBot()
        {   // (TASKS.md E54, native d511eb1: corners 2 and 3 said ORANGE on The Crowd and over their heads)
            Game g = app.game;
            g.tun.experimental = true; g.tun.expRoom = Room.Corners; g.tun.players = 4;
            g.UseArena(Arena.FourCorners, new System.Collections.Generic.List<CollisionTri>());
            g.ResetWithBots(0xE);
            app.net = new Online.Net();
            var s = new ArenaSession(app);
            Assert.That(s.PlayerLabel(1) == "BOT" && s.PlayerLabel(2) == "BOT" && s.PlayerLabel(3) == "BOT", "any corner a bot plays: BOT");
            g.ResetWithBots(0x2);
            Assert.That(s.PlayerLabel(1) == "BOT" && s.PlayerLabel(2) == "GREEN" && s.PlayerLabel(3) == "VIOLET",
                        "a nameless corner 2 or 3: its colour");
            Assert.That(s.PlayerLabel(5) == "OPEN", "no such corner: OPEN");
        }

        [Test]
        public void NewBotEveryMatchOrOnPush()
        {   // (TASKS.md E55, native 75f3f1f, d866da5: a new bot at every start; NEW BOT: EVERY MATCH or ON PUSH, saved; NEW BOT NOW)
            app.net = new Online.Net();
            app.settings.LoadAvatar(77);
            uint first = app.settings.botSeed;
            Assert.That(first != 0, "a bot of its own at the start");
            app.settings.ApplyBot(app.game);
            AvatarDesc wasBot = app.settings.botLooks[1];
            Click("PLAY PRACTICE");
            PickRoom(0);
            Assert.That(Find("NEW BOT NOW").x > Find("NEW BOT: EVERY MATCH").x && Find("NEW BOT: EVERY MATCH").x > menu.Items().First(i => i.act == Menu.Act.OptMeet).x,
                        "FIST BUMP FIRST, NEW BOT: EVERY MATCH (to start with), NEW BOT NOW, left to right");
            Click("NEW BOT NOW");
            Assert.That(app.settings.botSeed != first && app.game.botLook == app.settings.botSeed, "NEW BOT NOW: a new seed, the game's bots by it");
            Assert.That(Avatar.ToText(app.settings.botLooks[1]) != Avatar.ToText(wasBot) && Avatar.ToText(app.settings.botAvatar) == Avatar.ToText(app.settings.botLooks[1]),
                        "a new outfit for the practice bot");
            Click("NEW BOT: EVERY MATCH");
            Assert.That(!app.settings.botEveryMatch && Find("NEW BOT: ON PUSH").enabled, "ON PUSH");
            var back = new AppSettings(folder);
            back.Load(new Tunables());
            Assert.That(!back.botEveryMatch && back.botSeed == 0 && File.ReadAllText(Path.Combine(folder, "settings.cfg")).Contains("botnew=0") &&
                        !File.ReadAllText(Path.Combine(folder, "settings.cfg")).Contains("botseed"), "botnew= kept; the bot itself never saved");
            // EVERY MATCH: when a match's summary ends, before the next one's fist bump or countdown
            var s = new ArenaSession(app);
            app.settings.botEveryMatch = true;
            uint before = app.settings.botSeed;
            app.game.match = MatchState.MatchSummary; s.NewBotBetweenMatches();
            Assert.That(app.settings.botSeed == before, "not during the summary");
            app.game.match = MatchState.Countdown; s.NewBotBetweenMatches();
            uint after = app.settings.botSeed;
            Assert.That(after != before, "EVERY MATCH: a new bot as the next match comes");
            app.settings.botEveryMatch = false;
            app.game.match = MatchState.MatchSummary; s.NewBotBetweenMatches();
            app.game.match = MatchState.Countdown; s.NewBotBetweenMatches();
            Assert.That(app.settings.botSeed == after, "ON PUSH: the same bot match after match");
        }

        [Test]
        public void BasicMatchTimeChoices()
        {
            Click("PLAY PRACTICE");
            PickRoom(0);
            var minus = menu.Items().First(i => i.act == Menu.Act.OptTime && i.ip == 0);
            var plus = menu.Items().First(i => i.act == Menu.Act.OptTime && i.ip == 1);
            menu.Activate(plus);
            Assert.That(menu.optMatchTime, Is.EqualTo(300f), "3:00 -> 5:00");
            for (int i = 0; i < 9; ++i) menu.Activate(plus);
            Assert.That(menu.optMatchTime, Is.EqualTo(900f), "stops at 15:00");
            for (int i = 0; i < 9; ++i) menu.Activate(minus);
            Assert.That(menu.optMatchTime, Is.EqualTo(0f), "down to FREE PLAY");
            Assert.That(menu.Items().Any(i => i.label == "TIME: FREE PLAY"));
        }

        [Test]
        public void SettingsAndName()
        {
            Click("SETTINGS");
            VolUp();   // (VOLUME's +: MUSIC's row above it has one too)
            VolUp();
            Assert.That(app.settings.volume, Is.EqualTo(10), "8 -> 10");
            Assert.That(menu.Items().First(i => i.act == Menu.Act.VolUp).enabled, Is.False, "the volume stops at 10");
            Click("CONTROLLERS");   // (native 0.3.20: POINTER moved to SETTINGS > CONTROLLERS)
            Click("POINTER: RIGHT");
            Click("BACK");
            Click("GRAPHICS AND VIEW");   // (native 0.3.13: HIT EFFECTS and, from 0.3.15, BALL LOOK-AHEAD there)
            Assert.That(menu.gfxPage, "GRAPHICS AND VIEW opens its page");
            Click("HIT EFFECTS: FULL");
            Assert.That(app.settings.pointerHand == 0 && app.settings.fxLevel == 1);
            // (BALL LOOK-AHEAD on by default from 0.3.21: TASKS.md E24, native 860b45e)
            Click("BALL LOOK-AHEAD (ONLINE): ON");
            Assert.That(!app.settings.lookAhead, "BALL LOOK-AHEAD turned off (on by default)");
            Assert.That(Find("BALL LOOK-AHEAD (ONLINE): OFF").enabled);
            // GRAPHICS (TASKS.md E38, native c97c364 / 9b00fbc / c184b84): the effects over the field of view, as measured
            Assert.That(Find("SMOOTH EDGES: OFF").enabled && Find("CONTACT SHADOWS: SPECTATING").enabled && Find("COLOR GRADING: SPECTATING").enabled &&
                        Find("BLOOM: SPECTATING").enabled && Find("FLOOR REFLECTIONS: SPECTATING").enabled,
                        "SMOOTH EDGES OFF; CONTACT SHADOWS, COLOR GRADING, BLOOM and FLOOR REFLECTIONS only while you watch");
            Assert.That(!Find("FOVEATION: NOT ON THIS HEADSET").enabled, "no headset in the editor: FOVEATION not on it");
            menu.foveationShown = true;
            Assert.That(Find("FOVEATION: HIGH").enabled, "a headset that has it: FOVEATION HIGH");
            Click("FOVEATION: HIGH"); Click("SMOOTH EDGES: OFF"); Click("FLOOR REFLECTIONS: SPECTATING"); Click("FLOOR REFLECTIONS: ON");
            Assert.That(app.settings.gfxFoveation == 0 && app.settings.gfxMsaa == 1 && app.settings.gfxReflect == 0, "each round its values");
            var gs = new AppSettings(folder);
            gs.Load(new Tunables());
            Assert.That(gs.gfxFoveation == 0 && gs.gfxMsaa == 1 && gs.gfxReflect == 0 && gs.gfxBloom == 1 && gs.dcAo, "saved at once (settings.cfg)");
            Click("DEFAULTS");
            Click("RESET? PRESS AGAIN");
            Assert.That(app.settings.gfxFoveation == 3 && app.settings.gfxMsaa == 0 && app.settings.gfxReflect == 1 && app.settings.lookAhead && app.settings.fxLevel == 0,
                        "GRAPHICS AND VIEW's DEFAULTS: the effects as measured");
            Click("HIT EFFECTS: FULL");                    // (as the checks below had them)
            Click("BALL LOOK-AHEAD (ONLINE): ON");
            menu.foveationShown = false;
            Click("BACK");
            Assert.That(menu.settingsOpen && !menu.gfxPage, "BACK: to SETTINGS");
            Assert.That(Find("PASSTHROUGH: N/A").enabled, Is.False, "no headset in the editor: PASSTHROUGH N/A (PassthroughTests has the page)");
            Click("BACK");
            menu.titleOpen = true;
            Click("CHANGE NAME");
            Click("DELETE");
            while (menu.nameBuf.Length > 0) Click("DELETE");
            foreach (char c in "JEN 1") menu.Activate(menu.Items().First(i => i.act == Menu.Act.Key && i.ip == c));
            Click("DONE");
            Assert.That(app.settings.name, Is.EqualTo("JEN 1"));
            var s = new AppSettings(folder);
            s.Load(new Tunables());
            Assert.That(s.volume == 10 && s.pointerHand == 0 && s.fxLevel == 1 && s.name == "JEN 1", "kept in settings.cfg and player_name.txt");
            Assert.That(!s.lookAhead, "lookahead=0 kept (native's key; turned off above, on by default)");
        }

        [Test]
        public void SettingsFileAsTheNativeOne()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "settings.cfg"),
                "cfgver=8\nvolume=42\nhand=0\nfx=7\nmatchtime=99999\nknuckles=5\nserver=relay.example:47780\nnonsense\nunknown=3\n");
            var s = new AppSettings(folder);
            var t = new Tunables();
            s.Load(t);
            Assert.That(s.volume, Is.EqualTo(10), "volume clamped to 0..10");
            Assert.That(s.fxLevel, Is.EqualTo(2), "fx clamped to 0..2");
            Assert.That(t.basicMatchTime, Is.EqualTo(3600f), "match time clamped to an hour");
            Assert.That(t.knuckleMode, Is.EqualTo(1));
            Assert.That(s.server, Is.EqualTo("relay.example:47780"), "the server address kept for phase 5");
            Assert.That(s.name, Does.StartWith("PLAYER "), "a first name is made: PLAYER nnn");
            s.Save(t);
            string txt = File.ReadAllText(Path.Combine(folder, "settings.cfg"));
            StringAssert.StartsWith("cfgver=9\nadvanced=0\nshield=1\npitch=27.0\nfwd=0.004\nup=-0.014\nvolume=10\n", txt);   // (9: TASKS.md E24)
            StringAssert.Contains("\nserver=relay.example:47780\n", txt);
            StringAssert.Contains("\nmatchtime=3600\nroundtime=0\nturn=0\n", txt);
        }

        [Test]
        public void QuitNeedsTwoPressesAndTheLobbyComesBack()
        {
            var quit = menu.Items().First(i => i.act == Menu.Act.Quit);
            menu.Activate(quit);
            Assert.That(app.quitRequested, Is.False, "the first press only arms it");
            Assert.That(menu.Items().Any(i => i.label == "SURE? QUIT"));
            menu.Activate(menu.Items().First(i => i.act == Menu.Act.Quit));
            Assert.That(app.quitRequested, "the second press quits");
            app.lobby = false;
            menu.Activate(menu.Items().First(i => i.act == Menu.Act.Nav && i.ip == (uint)Menu.Nav.Lobby));
            Assert.That(app.lobby && !menu.open, "LOBBY: back on the balcony");
        }

        [Test]
        public void LobbyPointerFindsTheCourt()
        {   // (the six posters taken out, native 1159a46: the court is the lobby's one button)
            Vec3 eye = Lobby.Spot + new Vec3(0, 1.6f, 0);
            Assert.That(Lobby.RayHit(eye, Normalize(new Vec3(0, -0.5f, -1)), out float d), Is.EqualTo(Lobby.Court), "looking down at the court");
            Assert.That(d, Is.GreaterThan(1f).And.LessThan(10f));
            float a75 = Deg2Rad(-75), R = 1.4f;   // where PLAY THE BOT's poster stood: nothing there now
            Vec3 was = Lobby.Spot + new Vec3(R * UnityEngine.Mathf.Sin(a75), 1.40f, -R * UnityEngine.Mathf.Cos(a75));
            Assert.That(Lobby.RayHit(eye, Normalize(was - eye), out _), Is.EqualTo(-1), "where a poster stood: nothing");
            Assert.That(Lobby.RayHit(eye, new Vec3(0, 1, 0), out _), Is.EqualTo(-1), "the sky: nothing");
            Assert.That(Lobby.SignLine2, Is.EqualTo("POINT AT THE COURT AND PULL THE TRIGGER TO PLAY THE BOT"), "the rail sign's second line");
        }

        // the lobby's main menu 1.3 m out from the lobby spot (native 1159a46; it was 0.95), elsewhere 0.9 m from the head
        [Test]
        public void LobbyMenuStandsFartherOut()
        {
            Assert.That(menu.pose.p.z, Is.EqualTo(Lobby.Spot.z - 1.3f).Within(1e-4f), "in the lobby: 1.3 m out");
            app.lobby = false;
            var head = new Pose(Quat.Identity, new Vec3(1, 1.6f, 2));
            menu.Open(head, Lobby.Spot);
            Assert.That(Length(menu.pose.p - (head.p + new Vec3(0, -0.15f, -0.9f))), Is.LessThan(1e-4f), "elsewhere: 0.9 m from the head");
        }

        // MY BESTS (native 1159a46): the title page's board of your TRAINING bests, folded out of the side menu's left edge
        [Test]
        public void MyBestsFoldsOutOfTheSideMenu()
        {
            Assert.That(menu.BestsShown(), "on the title page");
            Pose bp = menu.BestsPose();
            Vec3 lp = menu.pose.q.Conj().Rotate(bp.p - menu.pose.p);
            float t = Deg2Rad(Menu.BestsFold);
            Vec3 rightEdge = lp + bp.q.Rotate(new Vec3(Menu.BestsW * 0.5f, 0, 0));   // (the menu's yaw is 0 here)
            Assert.That(rightEdge.x, Is.EqualTo(Menu.NavX - 0.15f - 0.015f).Within(1e-4f), "its right edge at the side menu's left edge, and a gap");
            Assert.That(rightEdge.z, Is.EqualTo(0f).Within(1e-4f), "hinged in the menu's plane");
            Assert.That(lp.y, Is.EqualTo(-0.09f).Within(1e-5f), "level with the menu");
            Assert.That(lp.z, Is.EqualTo(0.22f * UnityEngine.Mathf.Sin(t)).Within(1e-4f), "folded 35 degrees toward you");
            Assert.That(Menu.BestsW == 0.44f && Menu.BestsH == 0.94f, "0.44 m wide, as tall as the menu");
            menu.settingsOpen = true;
            Assert.That(!menu.BestsShown(), "not on the other pages");
        }

        [Test]
        public void MyBestsLines()
        {
            var B = new Sim.Training.Bests();
            var first = Menu.BestsLines(B, 0.47f, -0.19f, 0.19f, default, default, default).Select(l => l.s).ToList();
            Assert.That(first, Is.EqualTo(new[] { "MY BESTS", "NO BESTS YET", "EACH TRAINING LESSON", "TEACHES ONE SKILL:", "THROWING, DEFENDING,", "CATCHING" }),
                        "a first start: no bests yet, what training is");
            B.Load("1 1 14.2 5 5\n2 1 25.3 8 11\n3 1 16.0 5 7\n4 1 21.7 4 9\n");   // (native's --menu titlebests)
            var L = Menu.BestsLines(B, 0.47f, -0.19f, 0.19f, default, default, default);
            var all = Sim.Training.Lessons.All;
            int passed = all.Count(x => B.b[x.id].passed);
            Assert.That(L[1].s, Is.EqualTo($"TRAINING SCORE {B.Total() / 10}.{B.Total() % 10}"));
            Assert.That(L[2].s, Is.EqualTo($"{passed} OF {all.Count} LESSONS PASSED"));
            Assert.That(passed, Is.GreaterThan(0));
            Assert.That(L.Count, Is.EqualTo(3 + 2 * System.Math.Min(8, passed)), "a name and a best for each lesson passed");
            int i0 = all.FindIndex(x => B.b[x.id].passed);
            var les = all[i0];
            string nm = les.name.Length > 14 ? les.name.Substring(0, 14) : les.name;
            var b = B.b[les.id];
            string best = les.Timed() ? $"{b.time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} S {b.Permille() / 10}%" : $"{b.successes} / {b.attempts}";
            Assert.That(L[3].s == nm && L[4].s == best, $"the first lesson passed in the TRAINING page's order: {L[3].s} {L[4].s}");
            Assert.That(L[3].x - 0.375f * L[3].h * nm.Length, Is.EqualTo(-0.19f).Within(1e-5f), "its name from the left");
            Assert.That(L[4].x + 0.375f * L[4].h * best.Length, Is.EqualTo(0.19f).Within(1e-5f), "its best to the right");
            foreach (var x in all) B.b[x.id] = new Sim.Training.Best { passed = true, successes = 1, attempts = 1 };
            Assert.That(Menu.BestsLines(B, 0.47f, -0.19f, 0.19f, default, default, default).Count, Is.EqualTo(3 + 2 * System.Math.Min(8, all.Count)), "at most eight lessons");
        }

        [Test]
        public void MyBestsTrainingButton()
        {
            Pose bp = menu.BestsPose();
            Vec3 target = bp.Transform(new Vec3(0, Menu.BestsBtnY, 0));
            Vec3 eye = Lobby.Spot + new Vec3(0, 1.6f, 0);
            Vec3 d = Normalize(target - eye);
            var aim = new Pose(FromTo(new Vec3(0, 0, -1), d), eye);
            var input = new PlayerInput();
            int ph = app.settings.pointerHand;
            menu.Update(input, aim, true);
            Assert.That(menu.hitValid && menu.bestsHover && menu.hover == -1, "the pointer on MY BESTS' TRAINING");
            input.hand[ph].trigger = 1;
            Assert.That(menu.Update(input, aim, true), "pulled: clicked");
            Assert.That(!menu.titleOpen && app.training.open, "TRAINING's lessons open");
        }

        // MUSIC (native 1159a46): SETTINGS' top row; on in the lobby, off in the game, 30% to start; kept
        [Test]
        public void MusicRow()
        {
            var s = app.settings;
            Assert.That(s.musicMenu && !s.musicGame && s.musicVolume == 3, "MENU: ON, GAME: OFF, 30% to start");
            Click("SETTINGS");
            Assert.That(Find("MUSIC 30%").y, Is.EqualTo(0.215f), "the top row");
            Click("MENU: ON");
            Click("GAME: OFF");
            menu.Activate(menu.Items().First(i => i.act == Menu.Act.MusicUp));
            Assert.That(!s.musicMenu && s.musicGame && s.musicVolume == 4 && Find("MUSIC 40%").enabled);
            var back = new AppSettings(folder);
            back.Load(new Tunables());
            Assert.That(!back.musicMenu && back.musicGame && back.musicVolume == 4, "kept (musicmenu=, musicgame=, musicvol=)");
            for (int k = 0; k < 12; ++k) { var dn = menu.Items().First(i => i.act == Menu.Act.MusicDown); if (dn.enabled) menu.Activate(dn); }
            Assert.That(s.musicVolume == 0 && !menu.Items().First(i => i.act == Menu.Act.MusicDown).enabled, "0% at the bottom");
            Assert.That(NativeMixer.MusicLevel(true, false, true, false, 3), Is.EqualTo(0.033f).Within(1e-6f), "the lobby, MENU on: 0.11 x 30%");
            Assert.That(NativeMixer.MusicLevel(false, false, true, false, 3), Is.EqualTo(0f), "playing, GAME off: silent");
            Assert.That(NativeMixer.MusicLevel(false, false, false, true, 10), Is.EqualTo(0.11f).Within(1e-6f), "playing, GAME on, 100%");
            Assert.That(NativeMixer.MusicLevel(true, true, true, true, 10), Is.EqualTo(0f), "never in the SOUND STUDIO");
            float g = 0;
            for (int f = 0; f < 48000; ++f) g = NativeMixer.MusicGlide(g, 0.1f, 48000);
            Assert.That(g, Is.GreaterThan(0.08f).And.LessThan(0.095f), "about a second to its level");
        }

        // DEFAULTS on each settings page (native 0.3.19): two presses within 4 s, that page only
        [Test]
        public void DefaultsOnEachSettingsPage()
        {
            Click("SETTINGS");
            VolUp();
            Click("CONTROLLERS");
            Click("POINTER: RIGHT");
            Click("BACK");
            Click("GRAPHICS AND VIEW");
            Click("HIT EFFECTS: FULL");
            Click("BACK");
            Click("DEFAULTS");
            Assert.That(Find("RESET? PRESS AGAIN").enabled && app.settings.volume == 9, "the first press only arms it");
            Click("RESET? PRESS AGAIN");
            Assert.That(app.settings.volume == 8, "SETTINGS back as a fresh install has it");
            Assert.That(app.settings.pointerHand, Is.EqualTo(0), "POINTER is CONTROLLERS' own now (native 0.3.20)");
            Assert.That(app.settings.fxLevel, Is.EqualTo(1), "that page only: GRAPHICS AND VIEW keeps HIT EFFECTS: SOFT");
            Assert.That(Find("DEFAULTS").enabled, "the button says DEFAULTS again");
            var saved = new AppSettings(folder);
            saved.Load(new Tunables());
            Assert.That(saved.volume == 8 && saved.pointerHand == 0 && saved.fxLevel == 1, "and it is saved");
            Click("GRAPHICS AND VIEW");
            Click("DEFAULTS");
            Click("RESET? PRESS AGAIN");
            Assert.That(app.settings.fxLevel, Is.EqualTo(0), "GRAPHICS AND VIEW's own DEFAULTS");
            Click("BACK");
            Click("BACK");
            // MATCH OPTIONS: the rules' own settings, applied with START
            menu.titleOpen = true;
            Click("PLAY PRACTICE");
            PickRoom(0);
            Click("SHIELDS: ON");
            Click("STRIKES: REGULATION");
            Click("DEFAULTS");
            Assert.That(Find("PRESS AGAIN").enabled && !menu.optShields && menu.optStrikeSpeed == 1, "armed, nothing changed yet");
            Click("PRESS AGAIN");
            Assert.That(menu.optShields && menu.optStrikeSpeed == 0, "the rules' own: shields on, regulation strikes");
            Assert.That(!menu.optAdvanced && menu.rulesStep == 1, "the rules you picked stay");
            Assert.That(app.game.tun.shields && app.game.tun.strikeSpeed == 0, "nothing changes until START PRACTICE");
        }

        // SETTINGS > CONTROLLERS (TASKS.md E4, native 07d36c4): POINTER, and CONTROLLER ANGLE's TILT, TWIST and TURN, 5
        // degrees a step and 30 each way on top of the standard angle, mirrored on the left hand; DEFAULTS puts them back
        [Test]
        public void ControllerAngle()
        {
            Menu.Item Adj(int k, bool up) => menu.Items().First(i => i.act == Menu.Act.CtlAdj && i.ip == (uint)(k * 2 + (up ? 1 : 0)));
            Click("SETTINGS");
            Click("CONTROLLERS");
            Assert.That(menu.ctlPage && Find("TILT: STANDARD").enabled && Find("TWIST: STANDARD").enabled && Find("TURN: STANDARD").enabled,
                        "CONTROLLERS: the angle at the standard one");
            Assert.That(Find("POINTER: RIGHT").enabled && Find("BUTTONS").enabled, "POINTER is here, BUTTONS beside it");
            var xr = new XrInput();
            Vec3 grip = new Vec3(0.2f, 1.1f, -0.3f);
            Pose before = xr.ToolFrame(grip, Quat.Identity, 1), beforeL = xr.ToolFrame(grip, Quat.Identity, 0);
            menu.Activate(Adj(0, true));
            menu.Activate(Adj(0, true));
            menu.Activate(Adj(1, false));
            Assert.That(app.settings.ctlTilt == 10 && app.settings.ctlTwist == -5 && Find("TILT: +10").enabled && Find("TWIST: -5").enabled,
                        "TILT +10, TWIST -5: 5 degrees a step");
            for (int i = 0; i < 10; ++i) menu.Activate(Adj(2, true));
            Assert.That(app.settings.ctlTurn == 30 && !Adj(2, true).enabled && Adj(2, false).enabled, "TURN stops at 30");
            xr.ctlTilt = app.settings.ctlTilt; xr.ctlTwist = app.settings.ctlTwist; xr.ctlTurn = app.settings.ctlTurn;
            Pose after = xr.ToolFrame(grip, Quat.Identity, 1), afterL = xr.ToolFrame(grip, Quat.Identity, 0);
            Vec3 f0 = before.q.Rotate(new Vec3(0, 0, -1)), f1 = after.q.Rotate(new Vec3(0, 0, -1));
            Vec3 l0 = beforeL.q.Rotate(new Vec3(0, 0, -1)), l1 = afterL.q.Rotate(new Vec3(0, 0, -1));
            Assert.That(Dot(f0, f1) < 0.95f, "your hand turns with it");
            Assert.That(System.Math.Abs((f1.x - f0.x) + (l1.x - l0.x)) < 1e-3f && System.Math.Abs((f1.y - f0.y) - (l1.y - l0.y)) < 1e-3f,
                        "mirrored on the left hand");
            var saved = new AppSettings(folder);
            saved.Load(new Tunables());
            Assert.That(saved.ctlTilt == 10 && saved.ctlTwist == -5 && saved.ctlTurn == 30, "saved on this headset (ctltilt, ctltwist, ctlturn)");
            Click("POINTER: RIGHT");
            Click("DEFAULTS");
            Click("RESET? PRESS AGAIN");
            Assert.That(app.settings.ctlTilt == 0 && app.settings.ctlTwist == 0 && app.settings.ctlTurn == 0 && app.settings.pointerHand == 1,
                        "DEFAULTS: the standard angle and POINTER: RIGHT");
            Click("BACK");
            Assert.That(menu.settingsOpen && !menu.ctlPage, "BACK: to SETTINGS");
        }

        // BALL FEEL, every room's option (TASKS.md E29, native 3129dc2; was E18's GRAPHICS AND VIEW test page): STANDARD (the
        // owner's pick: glow core, true size, visible spin, heavy) or CUSTOM, the room's own page of the four switches, BACK
        // TO STANDARD; nothing changes until START; kept in settings.cfg (ballfeel=)
        [Test]
        public void BallFeelPage()
        {
            Click("SETTINGS");
            Click("GRAPHICS AND VIEW");
            Assert.That(menu.Items().Any(i => i.label == "BALL FEEL (TEST)"), Is.False, "GRAPHICS AND VIEW's test page is gone");
            Click("BACK");
            Click("BACK");
            menu.titleOpen = true;
            Click("PLAY PRACTICE");
            PickRoom(0);
            Assert.That(Find("BALL FEEL: STANDARD").enabled, "the SQUARE's page: BALL FEEL: STANDARD");
            Click("BALL FEEL: STANDARD");
            Assert.That(menu.roomBallPage && Find("CORE: GLOW").enabled && Find("GLOW: TRUE SIZE").enabled && Find("SPIN: VISIBLE").enabled &&
                        Find("WEIGHT: HEAVY").enabled && !Find("STANDARD BALL").enabled, "the standard ball: glow core, true size, visible spin, heavy");
            Click("CORE: GLOW");
            Click("WEIGHT: HEAVY");
            Assert.That(menu.optBallFeel == (Tunables.BALL_CORE | Tunables.BALL_TRUE | Tunables.BALL_SPIN) && Find("BACK TO STANDARD").enabled,
                        "CORE: SOLID, WEIGHT: LIGHT; BACK TO STANDARD offered");
            Assert.That(app.game.tun.ballFeel, Is.EqualTo(Tunables.BALL_FEEL_DEFAULT), "nothing changes until START");
            Click("BACK");
            Assert.That(!menu.roomBallPage && Find("BALL FEEL: CUSTOM").enabled, "BACK: the room's page, BALL FEEL: CUSTOM");
            Click("START PRACTICE");
            Assert.That(app.game.tun.ballFeel == (Tunables.BALL_CORE | Tunables.BALL_TRUE | Tunables.BALL_SPIN) && app.game.tun.StatsMode() == 0,
                        "the match takes it; never custom in the stats");
            var saved = new Tunables();
            new AppSettings(folder).Load(saved);
            Assert.That(saved.ballFeel, Is.EqualTo(Tunables.BALL_CORE | Tunables.BALL_TRUE | Tunables.BALL_SPIN), "kept in settings.cfg (ballfeel=)");
            menu.Open(new Pose(Quat.Identity, Lobby.Spot + new Vec3(0, 1.6f, 0)), Lobby.Spot);
            menu.titleOpen = true;
            Click("PLAY PRACTICE");
            PickRoom(3);
            Click("BALL FEEL: CUSTOM");
            Click("BACK TO STANDARD");
            Click("BACK TO STANDARD");   // (two presses, as every DEFAULTS)
            Assert.That(menu.optBallFeel == Tunables.BALL_FEEL_DEFAULT && Find("STANDARD BALL").enabled == false, "FIREBALL BLITZ has it too; BACK TO STANDARD");
        }

        // SETTINGS > CONTROLLERS > BUTTONS (TASKS.md E20 / E21, native eeb17b7): each button and its job, the next job on a
        // press, the last button of TALK kept, STANDARD and LEFT-HANDED, saved; VOICE CHAT's TALK BUTTON opens it; every
        // word that named a button names it as it is
        [Test]
        public void ButtonsPage()
        {
            Click("SETTINGS");
            Click("CONTROLLERS");
            Click("BUTTONS");
            Assert.That(menu.btnPage && Find("LEFT GRIP: TALK").enabled && Find("RIGHT GRIP: NOTHING").enabled && Find("A: READY").enabled &&
                        Find("B: RECENTER").enabled && Find("X: READY").enabled && Find("Y: PEEK").enabled && Find("LEFT STICK: NAMES").enabled &&
                        Find("RIGHT STICK: RECENTER").enabled && Find("STANDARD").enabled && Find("LEFT-HANDED").enabled, "BUTTONS: STANDARD, as the game always had it");
            Click("LEFT GRIP: TALK");
            Assert.That(menu.btnNote.StartsWith("TALK NEEDS A BUTTON") && Find("LEFT GRIP: TALK").enabled, "TALK's last button keeps it, and the page says why");
            Click("Y: PEEK"); Click("Y: NAMES"); Click("Y: READY"); Click("Y: RECENTER"); Click("Y: NOTHING");
            Assert.That(Find("Y: TALK").enabled, "Y: PEEK, NAMES, READY, RECENTER, NOTHING, TALK in turn");
            Click("LEFT GRIP: TALK");
            Assert.That(Find("LEFT GRIP: PEEK").enabled, "with Y talking, the grip may give TALK up");
            var saved = new AppSettings(folder);
            saved.Load(new Tunables());
            Assert.That(Controls.Save(saved.buttons), Is.EqualTo("204541350"), "saved on this headset (buttons=)");
            Assert.That(app.ReadyName == "A" && Controls.TalkHint(app.Buttons, app.ctlKind) == "HOLD Y TO TALK", "the hint names TALK's button");
            Click("LEFT-HANDED");
            Assert.That(Controls.SameMap(app.settings.buttons, Controls.LeftHanded()) && app.settings.pointerHand == 0 &&
                        Find("SPECTATING: THE LEFT STICK TURNS").enabled, "LEFT-HANDED: mirrored, the pointer in the left hand, the left stick turns");
            Click("DEFAULTS");
            Click("RESET? PRESS AGAIN");
            Assert.That(Controls.SameMap(app.settings.buttons, Controls.Standard()) && app.settings.pointerHand == 1, "DEFAULTS: STANDARD, the pointer right");
            Click("BACK");
            Assert.That(menu.ctlPage && !menu.btnPage && Find("POINTER: RIGHT").enabled, "BACK: to CONTROLLERS");
            Click("BACK");
            Click("VOICE AND MUTES");
            Assert.That(Find("VOICE: PUSH TO TALK").enabled && Find("TALK BUTTON: LEFT GRIP").enabled, "VOICE CHAT: TALK BUTTON beside the voice mode");
            Click("TALK BUTTON: LEFT GRIP");
            Assert.That(menu.btnPage, "TALK BUTTON opens BUTTONS");
            // Index controllers: the buttons named as they are on them; a Vive's: no Y, no sticks
            app.ctlKind = Controls.K_INDEX;
            Assert.That(Find("LEFT A: READY").enabled && Find("LEFT PAD: PEEK").enabled && Find("RIGHT A: READY").enabled, "Index: LEFT A, LEFT PAD");
            app.ctlKind = Controls.K_VIVE;
            Assert.That(menu.Items().Any(i => i.label.StartsWith("Y:")), Is.False, "a Vive's: no Y shown");
            Assert.That(menu.Items().Any(i => i.label.Contains("STICK:")), Is.False, "and no sticks");
            Assert.That(app.ReadyName, Is.EqualTo("RIGHT PAD"), "PRESS RIGHT PAD on a Vive");
        }

        // BALL LOOK-AHEAD on by default (TASKS.md E24, native 860b45e): a fresh install has it on; an older settings.cfg
        // (cfgver under 9, lookahead=0 as every one wrote it) turns it on once; a newer one that turned it off keeps it off
        [Test]
        public void LookAheadOnByDefault()
        {
            Assert.That(new AppSettings(folder + "_none").lookAhead, "a fresh install: on");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "settings.cfg"), new[] { "cfgver=8", "lookahead=0" });
            var old = new AppSettings(folder);
            old.Load(new Tunables());
            Assert.That(old.lookAhead, "an older file: turned on, once");
            var again = new AppSettings(folder);
            again.Load(new Tunables());
            Assert.That(again.lookAhead, "... and saved so");
            again.lookAhead = false;
            again.Save(new Tunables());
            var off = new AppSettings(folder);
            off.Load(new Tunables());
            Assert.That(off.lookAhead, Is.False, "turned off since: it stays off");
        }

        // TRAINING's MODE and MY OPTIONS (TASKS.md E33, native 3c285d9): STANDARD or MY OPTIONS (saved); SET MY OPTIONS
        // changes your own rules at once (and your room's); a lesson picked with MY OPTIONS plays them
        [Test]
        public void CustomTrainingPage()
        {
            Click("TRAINING");
            Assert.That(Find("MODE: STANDARD").enabled && Find("LEADERBOARD").enabled, "the lessons page: MODE: STANDARD, the leaderboard");
            Click("MODE: STANDARD");
            Assert.That(app.training.mine && app.settings.trainMine && Find("SET MY OPTIONS").enabled, "MODE: MY OPTIONS (saved); SET MY OPTIONS in place of the board");
            Click("SET MY OPTIONS");
            Assert.That(app.training.optsPage && Find("AIM ASSIST: MEDIUM").enabled && Find("AS THE RULES SAY").enabled == false, "MY OPTIONS: as the rules say");
            Click("AIM ASSIST: MEDIUM");
            Click("WALL GRIP: OFF");
            Assert.That(app.game.tun.aimAssist == 3 && app.game.tun.wallGrip, "each change in force at once: your own rules (your room's too)");
            Assert.That(Menu.MyOptionsLine(app.game.tun), Is.EqualTo("AIM ASSIST HIGH - WALL GRIP"));
            Click("DONE");
            Assert.That(!app.training.optsPage && Find("MODE: MY OPTIONS").enabled, "DONE: the lessons");
            var saved = new Tunables();
            new AppSettings(folder).Load(saved);
            Assert.That(saved.aimAssist == 3 && saved.wallGrip, "saved");
            app.training.classic = new System.Collections.Generic.List<CollisionTri>();   // (no court here: a lesson's rules only)
            menu.Activate(menu.Items().First(i => i.act == Menu.Act.TrainPick && i.ip == 0));
            Assert.That(app.training.trainer.Active && app.training.trainer.custom && app.game.tun.AimAssistNow() == 3 && app.game.tun.WallGripNow(),
                        "a lesson with MY OPTIONS plays them");
        }

        // HOW TO PLAY > CREDITS (task C10, native 0.3.17)
        [Test]
        public void HowToPlayHasTheCredits()
        {
            Click("HOW TO PLAY");
            Assert.That(menu.helpOpen && !menu.creditsOpen, "HOW TO PLAY opens on the rules");
            Assert.That(Find("GOT IT").enabled && Find("CREDITS").enabled, "GOT IT and CREDITS under it");
            Click("CREDITS");
            Assert.That(menu.helpOpen && menu.creditsOpen, "CREDITS opens its page");
            Assert.That(menu.Items().Where(i => i.act != Menu.Act.Nav && i.act != Menu.Act.Quit).Select(i => i.label).ToArray(),
                        Is.EqualTo(new[] { "BACK" }), "the credits page has only BACK (beside the side menu)");
            Click("BACK");
            Assert.That(menu.helpOpen && !menu.creditsOpen, "BACK goes back to HOW TO PLAY");
            Click("CREDITS");
            Click("BACK");
            Click("GOT IT");
            Assert.That(!menu.helpOpen && !menu.creditsOpen, "GOT IT closes both");
            Click("HOW TO PLAY");
            Assert.That(!menu.creditsOpen, "and HOW TO PLAY opens on the rules again");
            // the names as native's, word for word; the small print at the foot
            var names = Menu.Credits.Where(c => c.kind == Menu.CreditKind.Name).Select(c => c.text).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "JENNIFER DOEBELIN", "CROSSGENGAMING", "CLAUDE", "ASTROBLEEM    R0GUEICE    THEINSANIAC",
                                                   "SPEKKIOCT    SKANDI", "EVERY ALPHA TESTER" }));
            Assert.That(Menu.Credits.Last().kind, Is.EqualTo(Menu.CreditKind.Small));
        }

        // Quest 2 at 72 Hz, the rest at 90 (task C13, native 0.3.17: by ro.product.model)
        [Test]
        public void Quest2AsksFor72Hz()
        {
            Assert.That(MatchView.RefreshRateFor("Quest 2"), Is.EqualTo(72f));
            foreach (var m in new[] { "Quest 3", "Quest 3S", "Quest Pro", "", null })
                Assert.That(MatchView.RefreshRateFor(m), Is.EqualTo(90f), m ?? "(no model)");
        }
    }
}
