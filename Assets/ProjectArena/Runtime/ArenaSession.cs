using System;
using System.Collections.Generic;
using System.IO;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
using EventType = ProjectArena.Sim.EventType;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The app's state the menus, the lobby, the rooms and the match share (a slice of the native App struct).
    public class AppState
    {
        public Game game;
        public AppSettings settings;
        public Online.Net net;
        public bool lobby = true;          // on the lobby balcony, the bots playing below (InLobby: offline as well)
        public bool builderOpen;           // the avatar builder is open (Builder)
        public bool openBuilder;           // the menu or the lobby asked for the builder (the frame loop opens it)
        public bool quitRequested;
        public bool startPractice;         // the menu asked for a practice match against the bot
        public bool rulesPending;          // MATCH OPTIONS changed during a match against a person: in force after it
        public Pose viewHead = Pose.Identity;   // where the viewer's eyes are (native viewHead)
        // BUTTONS: the controllers in your hands (Controls.K_*, XrInput; 0 until known), the map as it works on them, and
        // READY's button by name (PRESS A as standard)
        public int ctlKind;
        public Controls.Map Buttons => Controls.Effective(settings.buttons, ctlKind);
        public string ReadyName => Controls.ReadyName(Buttons, ctlKind);
        public PlayerInput localIn = new PlayerInput();   // the latest local input (native localIn)
        // spectating: the hub of miniature arenas (watchIp 0), or one match watched from the gallery seat
        public bool spectating;
        public uint watchIp;
        public RoomInfo watchRoom = new RoomInfo();
        public Game shown;                 // the game drawn this frame: ours, or the watched match's (ArenaSession.Shown)
        public double watchGone;           // when the watched room stopped being listed (it may come back under a new host)
        public int watchPeople;            // the people in the watched room (two or more: it can be handed over)
        public bool viewing;               // watching rather than playing: the lobby, waiting outside, spectating
        // player statistics (the community server): the careers of the two players in our room / the watched match
        public Online.Net.CareerStats[] careers = new Online.Net.CareerStats[2];
        public bool careersOk;
        public Online.Net.CareerStats myCareer;
        public bool myCareerOk;
        public bool statsNoticeOpen;       // the one-time notice before the first online match
        public double myStatsAskAt, deleteArmedUntil;
        public double defaultsArmedUntil;  // DEFAULTS (native 0.3.19): which settings page's button is armed (-1: none)
        public int defaultsArmed = -1;
        // REPORT / MUTE A PLAYER
        public List<uint> mutedIds = new List<uint>(), kickVoted = new List<uint>();
        // NATIONS: each end's country (0 none) and colour; on when either end has a country
        public int[] sideNation = { 0, 0 };
        public float[][] sideRGB = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
        public bool nationsOn;
        public float[] crowdFlare = { 0, 0 };
        public Director director;          // the spectator's cameras (DIRECTOR TOOLS)
        public TrainingApp training;       // TRAINING (task A6): its lesson, its pages' state, your bests
        public SoundStudio studio;         // SOUND STUDIO (task C8)
        // the player's own rules, as saved: a host's while we are its guest, or a lesson's (TRAINING, and the SOUND STUDIO's
        // moments) while one runs, are never saved as theirs (native saveSettings; 0.3.19, owner 2026-11-12: FIST BUMP FIRST
        // went off after the studio, whose lesson turns it off). Every settings save goes through it
        public Tunables guestOwnRules;     // (ArenaSession: our own rules while we play by a host's; null otherwise)
        public Tunables RulesToSave => guestOwnRules ?? (training != null && training.Active ? training.trainer.SavedRules : game.tun);
        public NativeMixer mixer;          // the game's sound (MatchView makes it; the studio swaps clips in it)
        public bool recentered = true;     // the headset has placed you where you stand (TRAINING's card waits for it)
        // PLAYERS' NAMES (native 0.3.19, owner 2026-11-11: a quick way to see who's who, nothing over heads all the time):
        // the left stick's click shows everyone's name over their head until it's clicked again
        public bool showNames;

        public bool InLobby => lobby && !spectating && (net == null || net.CurrentMode == Online.Net.Mode.Offline);
        public bool IsMuted(uint id) => mutedIds.Contains(id) || mutes.NowContains(id);
        public readonly Mutes mutes = new Mutes();   // MUTED PLAYERS (TASKS.md E46): kept mutes, mutes.cfg
        // a match against a person is on in our arena (its rules stay until it ends)
        public bool RulesLive
        {
            get
            {
                if (net == null || net.CurrentMode != Online.Net.Mode.Host || game.pl[1].isBot) return false;
                MatchState m = game.match;
                return m == MatchState.Meet || m == MatchState.Countdown || m == MatchState.InProgress || m == MatchState.RoundSummary;
            }
        }
    }

    // One frame of the game for the local viewer, without the headset or the drawing (native stepFrame, viewTarget,
    // smoothPlayers, handleEvents' statistics, updateStats, updateSides, updateCrowd's LIVE): the network exchange, the
    // rules a guest plays by, the lobby's bots, spectating, the match statistics the host reports and the players'
    // careers. MatchView reads the headset and draws; tests drive it directly.
    public class ArenaSession
    {
        public readonly AppState a;
        public readonly Game game;
        public readonly Online.Net net;
        public Menu menu;
        public Builder builder;
        // remote players eased toward their latest update (native viewIn)
        public readonly PlayerInput[] viewIn = { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };   // (four players: four)
        public readonly bool[] viewInValid = { false, false, false, false };   // (the PC spectator's eyes cameras read them)
        // the game shown: ours, or (spectating) the watched match's
        public Game Shown { get; private set; }
        StatCounter matchCount = new StatCounter();   // this match's statistics (the native StatCounter, Sim/Stats.cs)
        public readonly MatchTally[] liveStats = new MatchTally[4];   // (four players too: The Crowd, TASKS.md E47)
        public float bumpFlash;
        bool fairTold;   // FAIR MATCHES: this match's countdown has told the host what plays at regulation

        // FAIR MATCHES: at a match's countdown, the host is told when an option it chose plays at regulation because a
        // player's game doesn't have it (its own rules stay chosen for the next match)
        void FairMatchNotice()
        {
            Tunables t = game.tun;
            // (hosting; and practice, for FIREBALL BLITZ's teams with a team nobody plays in: the version notices never come
            // offline, where every option plays; native 3fccaf9)
            bool host = net.CurrentMode == Online.Net.Mode.Host, practice = net.CurrentMode == Online.Net.Mode.Offline && !(a.training != null && a.training.Active);
            if (!(host || practice) || game.match != MatchState.Countdown) { fairTold = false; return; }
            if (fairTold) return;
            fairTold = true;
            string what = null;
            if (game.np == 2 && !t.experimental && t.foulShift != 0 && t.FoulShiftNow() == 0) what = "FOUL LINE: REGULATION (A PLAYER NEEDS 0.3.20)";
            else if (game.np == 2 && t.strikeSpeed != 0 && t.StrikeSpeedNow() == 0) what = "STRIKES: REGULATION (A PLAYER NEEDS 0.3.19)";
            else if (game.np == 2 && !t.RoomOptionsNow() && (!t.experimental || t.expRoom == Room.Diamond) &&
                     ((t.maiden && t.shields) || t.aimAssist >= 0 || t.strikeZone != 1 || t.ballGrowth != 3 || (t.advanced && t.advancedRoundsToWin != 2) ||
                      (t.shields && t.shieldSize != 0) || t.wallsLively || t.wallGrip || t.bankAssist > 0))
                what = "ROOM OPTIONS: AS THE RULES SAY (A PLAYER NEEDS 0.3.21)";
            else if (game.np == 2 && t.RoomOptionsNow() && !t.BouncesNow() && (t.wallsLively || t.wallGrip || t.bankAssist > 0))
                what = "BOUNCES: STANDARD (A PLAYER NEEDS 0.3.22)";   // (BOUNCES, native 5d3bbec)
            else if (game.Octagon && t.teamMode != Teams.FFA && !t.BlitzTeamsNow())
                what = "TEAMS: FREE-FOR-ALL (A PLAYER NEEDS 0.3.24)";   // (FIREBALL BLITZ in teams, TASKS.md E51)
            else if (game.Octagon && t.teamMode != Teams.FFA && game.TeamsNow() == Teams.FFA)
                what = "TEAMS: FREE-FOR-ALL (A TEAM HAS NOBODY IN IT)";   // (Game.TeamsNow: who started; native 3fccaf9)
            if (what != null)
            {
                net.NoticeLocal(what);
                Debug.Log($"[Rules] fair match: {what} (the oldest player's game: {t.matchVersion})");
            }
        }
        Tunables ownRules;
        bool guestRules;
        int lobbyPrevMode = -1;
        float lobbySummaryT;
        uint stateKey = 0xFFFFFFFFu;
        uint statsHost;
        double statsAskAt;
        readonly Online.Net.CareerStats[] careerBuf = new Online.Net.CareerStats[2];
        readonly PlayerInput[] stepIn = { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };   // (four players: four)

        public int L => net.LocalIndex();

        public ArenaSession(AppState a)
        {
            this.a = a;
            game = a.game;
            net = a.net;
            Shown = game;
            ownRules = Copy(game.tun);
            a.training ??= new TrainingApp(a);
        }

        static Tunables Copy(Tunables t)
        {
            var o = new Tunables();
            RulesInto(t, o);
            return o;
        }

        // the rules a room sets (MATCH OPTIONS, and its EXPERIMENTAL room, players and teams, FIREBALL BLITZ's health,
        // balls and players: a guest leaving FOUR CORNERS gets its own back, native 0.3.12)
        static void RulesInto(Tunables from, Tunables to)
        {
            to.advanced = from.advanced; to.shields = from.shields; to.knuckleMode = from.knuckleMode;
            to.basicMatchTime = from.basicMatchTime; to.advancedRoundTime = from.advancedRoundTime; to.strikeSpeed = from.strikeSpeed;
            to.foulShift = from.foulShift;
            to.experimental = from.experimental; to.expRoom = from.expRoom; to.players = from.players;
            to.teamMode = from.teamMode; to.theOne = from.theOne; to.oneTurns = from.oneTurns;
            to.health = from.health; to.fireBalls = from.fireBalls; to.octPlayers = from.octPlayers;   // (FIREBALL BLITZ)
            // (... and the room's options, BALL FEEL and BOUNCES: native 5d3bbec, they stayed the host's until 0.3.22, pull
            // request #26 found AIM ASSIST's)
            to.maiden = from.maiden; to.aimAssist = from.aimAssist; to.strikeZone = from.strikeZone; to.ballGrowth = from.ballGrowth;
            to.advancedRoundsToWin = from.advancedRoundsToWin; to.shieldSize = from.shieldSize; to.ballFeel = from.ballFeel;
            to.wallsLively = from.wallsLively; to.wallGrip = from.wallGrip; to.bankAssist = from.bankAssist;
        }

        // native startUp's network part: the name, the statistics key, BOT FILLS IN, online at start when chosen
        public void StartNetwork(string defaultServer, bool goOnline = true)
        {
            net.Init();
            net.SetName(a.settings.name);
            net.SetStatsKey(a.settings.LoadStatsKey());
            net.BotFill = a.settings.botFill;
            net.LookAhead = a.settings.lookAhead;   // BALL LOOK-AHEAD (native 0.3.15)
            RoomFeelFeature.Step = a.settings.roomFeel;   // FIELD OF VIEW (the eyes as drawn; the headset gets the real ones)
            net.PrivateTalk = a.settings.privateTalk;   // ROOM TALK: PRIVATE (native 0.3.13; its menu item: task C4)
            net.SetAppVersion(Online.Net.GameVersion);  // (told to the relay in HELLO; from 0.3.20 at the end of JOIN too)
            if (string.IsNullOrEmpty(a.settings.server) && !string.IsNullOrEmpty(defaultServer)) a.settings.server = defaultServer;
            if (goOnline && a.settings.onlinePref && !string.IsNullOrEmpty(a.settings.server)) net.GoOnline(a.settings.server);
            a.training.Load();   // (TRAINING: your bests, each sent again once online)
        }

        // holding area outside the tank (waiting players): four spots along the -Z side, facing the glass
        public static Vec3 HoldingSpot(int spot) => new Vec3(-4.5f + 3.0f * (spot & 3), 0, -4.2f);

        // where the viewer stands and looks (native viewTarget)
        public void ViewTarget(int local, out Vec3 pos, out Vec3 fwd)
        {
            if (a.InLobby) { pos = Lobby.Spot; fwd = new Vec3(0, 0, -1); }
            else if (net.LocalWaiting())
            {   // (CORNERS: the waiting spots outside the room's glass, facing in)
                pos = ExpArenaView.HoldingPose(game, net.LocalSpot()).p;
                fwd = ExpArenaView.HoldingFwd(game, net.LocalSpot());
            }
            else if (a.spectating)
            {
                if (a.watchIp != 0) { Pose st = ExpArenaView.SpectatorSeat(Shown); pos = st.p; fwd = st.q.Rotate(new Vec3(0, 0, -1)); }
                else { pos = new Vec3(0, 0, 0); fwd = new Vec3(1, 0, 0); }
            }
            else if (MeetHere)
            {   // face to face in the centre (fist bump start)
                pos = new Vec3(local == 0 ? -game.tun.meetSpot : game.tun.meetSpot, 0, 0);
                fwd = local == 0 ? new Vec3(1, 0, 0) : new Vec3(-1, 0, 0);
            }
            else if (OutInDugout(local))
            {   // FIREBALL BLITZ, knocked out: the dugout, the waiting row across (owner, 2026-11-02)
                Pose d = ExpArenaView.HoldingPose(game, 4 + (local & 3));
                pos = d.p;
                fwd = d.q.Rotate(new Vec3(0, 0, -1));
            }
            else
            {   // (corners 3 and 4 of a room for four face the centre; FIREBALL BLITZ: everyone, round the ring)
                pos = game.tun.start[local];
                fwd = local < 2 && !game.Octagon ? (local == 0 ? new Vec3(1, 0, 0) : new Vec3(-1, 0, 0)) : game.Facing(local);
            }
        }

        // FIREBALL BLITZ: you were in this match and are out (native viewTarget's dugout)
        public bool OutInDugout(int local) =>
            game.Octagon && !a.spectating && !net.LocalWaiting() && local >= 0 && local < game.np && !game.Alive(local) &&
            ((game.startMask >> local) & 1) != 0 && game.match != MatchState.WaitingForPlayers;

        // RECORD MY MATCHES (native recordStep): from a match's start to its end, while this player plays it (not
        // spectating, waiting or in the lobby; TRAINING: a lesson from its GO to its end), in the settings' folder's
        // recordings (the newest Rec.Keep kept). MATCH MY LEVEL's level is kept when a match ends.
        public readonly Recorder recorder = new Recorder();
        void RecordStep(float dt)
        {
            bool start = false, over = false;
            foreach (GameEvent e in game.events)
            {
                start |= e.type == EventType.RoundStart && e.value <= 1f;
                over |= e.type == EventType.MatchEnd;
            }
            var tr = a.training.trainer;
            if (tr.Active && tr.phase != Sim.Training.Phase.Play && tr.phase != Sim.Training.Phase.Countdown) over = true;
            if (over && game.tun.botAdapt) a.settings.Save(a.RulesToSave);
            bool playing = a.settings.recordOn && !a.viewing && !a.InLobby && net.CurrentMode != Online.Net.Mode.Spectate;
            if (recorder.Active && (!playing || game.match == MatchState.WaitingForPlayers || game.match == MatchState.Meet))
            {
                recorder.End(game);
                Debug.Log("[Record] stopped (" + recorder.Path + ")");
            }
            if (playing && start && !recorder.Active)
            {
                string folder = System.IO.Path.Combine(a.settings.Folder, "recordings");
                try { System.IO.Directory.CreateDirectory(folder); } catch (Exception) { }
                Rec.Prune(folder, Rec.Keep - 1);
                string path = System.IO.Path.Combine(folder, DateTime.Now.ToString("'match_'yyyyMMdd'_'HHmmss'.par'"));
                var m = new RecMeta { local = net.LocalIndex(), version = Application.version, lesson = tr.Active ? tr.L.id : 0 };
                if (recorder.Begin(path, game, m)) Debug.Log("[Record] " + path);
                else Debug.LogWarning("[Record] can't write " + path);
            }
            if (!recorder.Active) return;
            recorder.Frame(game, dt);
            if (over)
            {
                recorder.End(game);
                Debug.Log("[Record] match over (" + recorder.Path + ")");
            }
        }

        // the classic court's collision (MatchView loads it; the EXPERIMENTAL rooms bring their own)
        public List<CollisionTri> classicArena;

        // the arena the rules in play ask for (native arenaFor: practice, our room, the room we joined, the match we watch);
        // the lobby and the spectators' hub are classic
        public int ArenaFor(Game g)
        {
            Tunables t = g.tun;
            if (!t.experimental || a.InLobby || (a.spectating && a.watchIp == 0)) return Arena.Classic;
            if (t.expRoom == Sim.Room.Blitz) return Arena.Octagon;
            if (t.expRoom == Sim.Room.Corners) return t.players == 4 ? Arena.FourCorners : Arena.Corners;
            return Arena.Diamond;
        }

        // the fist bump start, face to face in the centre: in a room, and in practice against the bot (FIST BUMP FIRST,
        // native meetHere)
        public bool MeetHere => game.match == MatchState.Meet && (net.InRoom || (net.CurrentMode == Online.Net.Mode.Offline && !a.InLobby));

        // moving between playing / waiting / the hub / watching a match / the meeting / the lobby re-places the viewer
        // (FIREBALL BLITZ: the dugout and back, and the room closing in: your new place)
        public uint ViewKey() =>
            a.spectating ? (a.watchIp != 0 ? a.watchIp : 1u) : net.LocalWaiting() ? 10u + (uint)net.LocalSpot()
            : MeetHere ? 2u : a.InLobby ? 3u
            : OutInDugout(L) ? 4u : game.Octagon ? 32u + game.ringMask : 0u;

        // One frame (native stepFrame). mine: the local input (head, hands; its avatar is set here). Returns the events
        // of the game shown, this frame.
        public List<GameEvent> Step(PlayerInput mine, float dt)
        {
            // (as sent: with a country the team kit, which older builds can show too; or COUNTRY KIT = MY OUTFIT, which they
            // show as the team kit: native readInput's sentLook, TASKS.md E34)
            mine.avatar = Avatar.SentLook(a.settings.myAvatar);
            a.localIn.CopyFrom(mine);
            a.viewHead = mine.head;
            bumpFlash = Math.Max(0f, bumpFlash - dt);
            {   // a guest plays by the host's rules; ours come back when we leave (and are never saved over)
                // (a room taken over from its host plays that room's rules too)
                bool guest = net.CurrentMode == Online.Net.Mode.Client || net.CurrentMode == Online.Net.Mode.Spectate || net.Inherited;
                if (!guest && guestRules)
                {
                    RulesInto(ownRules, game.tun);
                    guestRules = false;
                    if (game.match == MatchState.WaitingForPlayers || net.CurrentMode == Online.Net.Mode.Offline) game.Reset(game.pl[1].isBot);
                    Debug.Log($"[Rules] -> {Scoreboard.RulesLine(game.tun)} (our own again)");
                }
                else if (!guest) ownRules = Copy(game.tun);
                else guestRules = true;
                a.guestOwnRules = guestRules ? ownRules : null;
            }
            // FAIR MATCHES (native 0.3.20): hosting, an option an older player's game lacks plays at regulation this match
            // (offline and as a guest: everything; a guest gets the host's rules as they play)
            game.tun.matchVersion = net.MatchVersion();
            if (net.CurrentMode != Online.Net.Mode.Client && net.CurrentMode != Online.Net.Mode.Spectate)
            {   // (a guest: its host's)
                game.tun.palmHold = true;     // (TASKS.md E43)
                NewBotBetweenMatches();   // (NEW BOT: EVERY MATCH; TASKS.md E55)
                game.botLook = a.settings.botSeed;   // (the bots' stances: the same each match until a new bot)
                game.tun.blitzTeams = true;   // (FIREBALL BLITZ may play in teams: FAIR MATCHES decides, matchVersion; TASKS.md E51)
            }
            FairMatchNotice();
            // TRAINING is played offline, on the court (native: out of it online, in the lobby, spectating)
            if (a.training.Active && (net.CurrentMode != Online.Net.Mode.Offline || a.InLobby || a.spectating)) a.training.Stop();
            if (a.rulesPending && !a.RulesLive && menu != null)
            {   // MATCH OPTIONS changed during a match: in force from now on
                a.rulesPending = false;
                bool reset = game.match == MatchState.WaitingForPlayers;
                menu.OptionsIntoGame();
                if (reset) game.Reset(game.pl[1].isBot);
                a.settings.Save(a.RulesToSave);
                net.NoticeLocal("NEW RULES IN FORCE");
            }
            {   // moving into a room, the tank, spectating or back out closes the menu (a note being typed stays open)
                uint key = (uint)net.CurrentMode * 64u + (uint)(net.LocalSlot() + 2) * 4u + (a.spectating ? 1u : 0u);
                if (stateKey != 0xFFFFFFFFu && key != stateKey && menu != null && menu.open && !menu.nameEdit && !menu.bugOpen && !menu.reportOpen)
                {
                    menu.Close();
                    Debug.Log($"[Menu] closed (moved to {net.Status()})");
                }
                stateKey = key;
            }
            // the bots' outfits (NEW BOT's, each corner its own; TASKS.md E55). Hosting or practice only: a guest and a
            // spectator show the host's bots as its snapshots have them (until 0.3.23 each guest dressed the host's bot)
            if (net.CurrentMode != Online.Net.Mode.Client && net.CurrentMode != Online.Net.Mode.Spectate)
            {
                for (int p = 1; p < game.np; ++p)
                    if (game.pl[p].isBot) game.pl[p].@in.avatar = a.settings.botLooks[p];
                if (game.np == 4 && game.pl[0].isBot) game.pl[0].@in.avatar = a.settings.botLooks[0];   // (a bot in the host's corner, after MOVE PLAYERS)
            }
            {   // the EXPERIMENTAL rooms (native 0.3.12): by the rules in play; the lobby stays classic, two bots. Our own
                // rules (practice, hosting) take their players from the room chosen (FOUR CORNERS: four); a guest's and a
                // spectator's come with the host's snapshots
                var nm = net.CurrentMode;
                if (a.InLobby) game.tun.players = 2;
                else if (nm == Online.Net.Mode.Offline || nm == Online.Net.Mode.Searching || nm == Online.Net.Mode.Host)
                    game.tun.players = game.tun.experimental && Sim.Room.FourPlayer(game.tun.expRoom) ? 4 : 2;
                if (classicArena != null) game.UseArena(ArenaFor(game), classicArena);
            }
            List<GameEvent> events;
            if (net.CurrentMode == Online.Net.Mode.Spectate)
            {
                a.spectating = true;
                net.SetPresence(a.watchIp != 0 ? (byte)1 : (byte)0, mine.head.p, a.watchIp);
                net.Exchange(mine, stepIn, game, dt);
                SpecMatch w = null;
                foreach (var m in net.Matches) if (m.ip == a.watchIp) w = m;
                if (a.watchIp != 0 && w == null)
                {   // the match is gone: handed over to another host (follow it), or ended (native 0.3.13)
                    uint next = 0;
                    bool listed = false;
                    foreach (var lg in net.Games()) { if (lg.continues == a.watchIp) next = lg.ip; listed |= lg.ip == a.watchIp; }
                    // (a host gone quiet with others in its room: it may turn up under the next host within ~10 s; wait
                    // that long for it. Nobody else there: nobody to take it over)
                    double t = Time.realtimeSinceStartupAsDouble;
                    if (!listed && a.watchGone == 0) a.watchGone = t;
                    double wait = a.watchPeople >= 2 ? 12.0 : 0.0;
                    if (next != 0) { a.watchIp = next; a.watchGone = 0; Debug.Log("[Spectate] the room has a new host, still watching it"); }
                    else if (!listed && t - a.watchGone >= wait) { a.watchIp = 0; a.watchGone = 0; Debug.Log("[Spectate] match ended, back to the hub"); }
                }
                else
                {
                    a.watchGone = 0;
                    if (w != null)
                    {   // (how many people its room has: a host and at least one more can hand it over)
                        a.watchPeople = w.room.members.Count;
                        for (int p = 0; p < 4; ++p) a.watchPeople += w.room.slot[p] != 0 ? 1 : 0;
                    }
                }
                if (w != null)
                {   // watched match: its state and events drive the normal renderer, audio and effects
                    w.game.arenaKind = ArenaFor(w.game);   // (its room, for the drawing: the copy isn't stepped)
                    Shown = w.game;
                    Shown.events.Clear();
                    Shown.events.AddRange(w.events);
                    w.events.Clear();
                    a.watchRoom = w.room;
                }
                else { Shown = game; game.events.Clear(); }
                events = Shown.events;
            }
            else
            {
                if (a.spectating) { a.spectating = false; a.watchIp = 0; Shown = game; }
                {   // back to Offline from a room or spectating: back to the lobby
                    int m = (int)net.CurrentMode;
                    if (m == (int)Online.Net.Mode.Offline && lobbyPrevMode != m && lobbyPrevMode >= 0) a.lobby = true;
                    lobbyPrevMode = m;
                    if (a.InLobby)
                    {
                        if (!game.pl[0].isBot) StartLobbyMatch();
                        if (game.match == MatchState.MatchSummary) { if ((lobbySummaryT += dt) > 6f) StartLobbyMatch(); }
                        else lobbySummaryT = 0;
                    }
                }
                if (a.startPractice)
                {   // START PRACTICE: into the court against the bot (the rules are set)
                    a.startPractice = false;
                    if (net.CurrentMode == Online.Net.Mode.Offline) game.Reset(true);
                    Debug.Log("[Match] practice against the bot: " + Scoreboard.RulesLine(game.tun));
                }
                stepIn[0] = new PlayerInput(); stepIn[1] = new PlayerInput();
                a.studio?.Update();   // (SOUND STUDIO: the synced library in; out of it if its lesson stopped)
                if (a.training.Active)
                {   // TRAINING: the lesson steps the game (and its coach), offline (native: instead of the exchange)
                    a.training.classic = classicArena;
                    a.training.Step(mine, stepIn, dt, menu, a.recentered);
                }
                else if (net.Exchange(mine, stepIn, game, dt))
                {
                    // practice is paused while you customize your avatar or the title menu is open (offline only)
                    bool paused = (a.builderOpen || (menu != null && menu.open && menu.titleOpen)) && net.CurrentMode != Online.Net.Mode.Host && !a.InLobby;
                    game.octCap = net.CurrentMode == Online.Net.Mode.Offline ? game.tun.octPlayers : 4;   // (FIREBALL BLITZ: practice's PLAYERS)
                    if (!paused) game.Step(dt, stepIn);
                    else game.events.Clear();
                    net.SendState(game);
                }
                Shown = game;
                events = game.events;
            }
            a.viewing = a.spectating || net.LocalWaiting() || a.InLobby;
            a.shown = Shown;
            if (a.director != null)
            {
                if (a.spectating) a.director.Events(events);   // action cameras (native directorEvents)
                else { a.director.open = false; a.director.autoCam = false; }
                // team cameras shared by a teammate
                if (net.TakeCameras(out string team, out byte[] blob) && a.settings.team.Length > 0 && a.settings.team == team && a.director.Unpack(blob))
                {
                    a.director.Save();
                    net.NoticeLocal("TEAM CAMERAS RECEIVED");
                }
            }
            HandleStats(events);
            RecordStep(dt);
            SmoothPlayers(dt);
            UpdateSides(dt);
            UpdateStats();
            UpdateLive();
            return events;
        }

        // the lobby: the two bots play below the balcony (native startLobbyMatch)
        void StartLobbyMatch()
        {
            game.Reset(true);
            game.pl[0].isBot = true;
            game.pl[0].@in.avatar = Avatar.PresetLook(10);
            game.pl[1].@in.avatar = Avatar.PresetLook(12);
            lobbySummaryT = 0;
            Debug.Log("[Lobby] bots playing");
        }

        // ---------------------------------------------------------------- statistics (native countStat, handleEvents)
        public static byte[] FinalScore(Game g)
        {   // what both players' games agree on (CONFIRM)
            byte C(int v) => (byte)Math.Max(0, Math.Min(255, v));
            return new[] { C(g.pl[0].score), C(g.pl[1].score), C(g.pl[0].roundWins), C(g.pl[1].roundWins) };
        }

        void HandleStats(List<GameEvent> events)
        {
            foreach (var e in events)
            {
                if (e.type == EventType.FistBump) bumpFlash = 1.0f;
                if (net.CurrentMode == Online.Net.Mode.Host && net.Online) CountStat(e);
                if (e.type == EventType.MatchEnd && net.CurrentMode == Online.Net.Mode.Client && net.Online && net.LocalSlot() >= 0 &&
                    e.player >= 0 && e.player < 2)
                    net.ConfirmMatch(e.player, FinalScore(game));   // (the host's result counts once we agree)
            }
        }

        // NEW BOT: EVERY MATCH: a new bot as each match comes (the last one's summary over: before the next one's fist bump or
        // countdown, so it doesn't change in front of you); ON PUSH: only NEW BOT NOW. Hosting or practice (native stepFrame)
        MatchState botPrevMatch = MatchState.WaitingForPlayers;
        public void NewBotBetweenMatches()
        {
            if (a.settings.botEveryMatch && botPrevMatch == MatchState.MatchSummary && game.match != MatchState.MatchSummary)
                Debug.Log("[Bot] " + a.settings.NewBot(game) + " (every match)");
            botPrevMatch = game.match;
        }

        void CountStat(GameEvent e)
        {
            // (native countStat: each throw on target once, points from returns apart; native 8f35f26, 6eca485)
            var s = matchCount.s;
            int p = e.player, np = game.np == 4 ? 4 : 2;
            matchCount.np = np;   // (FOUR CORNERS and FIREBALL BLITZ: counted for The Crowd's page; careers stay one-on-one, TASKS.md E47)
            switch (e.type)
            {
                case EventType.RoundStart:
                    if ((int)e.value <= 1) matchCount = new StatCounter { np = np };
                    break;
                case EventType.MatchEnd:
                    for (int k = 0; k < 4; ++k) liveStats[k] = s[k];
                    if (np == 2 && p >= 0 && p < 2 && !game.pl[1].isBot) net.ReportMatch(p, s, FinalScore(game), game.tun.StatsMode());
                    matchCount = new StatCounter { np = np };
                    return;
                default:
                    matchCount.Add(e);
                    break;
            }
            s = matchCount.s;
            for (int k = 0; k < 4; ++k) liveStats[k] = s[k];
        }

        // the careers of the two players in our room (or the watched match), every 5 s; the one-time notice before the
        // first online match (native updateStats)
        void UpdateStats()
        {
            if (!a.settings.statsNoticeSeen && !a.statsNoticeOpen && net.Online && net.InRoom && menu != null && !menu.open && !a.viewing)
            {
                a.statsNoticeOpen = true;
                menu.OpenNotice(a.viewHead);
            }
            uint hv = a.watchIp != 0 ? a.watchIp : net.RoomHostVip();
            double t = Time.realtimeSinceStartupAsDouble;
            if (hv != statsHost) { statsHost = hv; a.careersOk = false; statsAskAt = 0; }
            if (hv != 0 && net.OnlineReady && t > statsAskAt) { statsAskAt = t + 5.0; net.RequestStats(hv); }
            if (hv != 0 && net.StatsFor(hv, careerBuf)) { a.careers[0] = careerBuf[0]; a.careers[1] = careerBuf[1]; a.careersOk = true; }
        }

        // NATIONS (native updateSides): each end's country and colour from the players' looks as drawn
        void UpdateSides(float dt)
        {
            Game g = Shown;
            for (int p = 0; p < 2; ++p)
            {
                AvatarDesc d = (p == L && !a.viewing) ? a.settings.myAvatar : viewIn[p].avatar;
                a.sideNation[p] = net.SlotFilled(p) || a.spectating || a.viewing ? Avatar.NationOf(d) : 0;
            }
            Avatar.SideColors(a.sideNation[0], a.sideNation[1], a.sideRGB);
            a.nationsOn = a.sideNation[0] != 0 || a.sideNation[1] != 0;
            for (int p = 0; p < 2; ++p) a.crowdFlare[p] = Math.Max(0f, a.crowdFlare[p] - dt * 1.2f);
        }

        // The Crowd: an online host tells the relay about its match every second (native updateCrowd's LIVE)
        void UpdateLive()
        {
            if (net.CurrentMode != Online.Net.Mode.Host || !net.Online) return;
            Game g = game;
            var li = new Online.Net.LiveInfo { state = (byte)g.match };
            byte[] sc = FinalScore(g);
            Array.Copy(sc, li.score, 4);
            for (int p = 0; p < 2; ++p)
            {
                li.nation[p] = (byte)a.sideNation[p];
                for (int k = 0; k < 3; ++k) li.rgb[p, k] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(a.sideRGB[p][k] * 255f)));
                string n = PlayerLabel(p);
                li.name[p] = n.Length > 16 ? n.Substring(0, 16) : n;
            }
            Tunables t = g.tun;
            li.flags = (byte)((t.advanced ? 1 : 0) | (g.pl[1].isBot ? 2 : 0) | (!t.shields ? 4 : 0) | (t.FreePlay() ? 8 : 0) |
                              (t.KnuckleGuards() ? 16 : 0) | (t.advanced && t.advancedRoundTime > 0 ? 32 : 0));
            li.clock = g.roundClock;
            li.st[0] = liveStats[0]; li.st[1] = liveStats[1];
            if (g.np == 4)
            {   // FOUR CORNERS and FIREBALL BLITZ: all four players, for The Crowd's page (TASKS.md E47, native 530f6fb, 0.3.23)
                li.np = 4;
                li.room = (byte)(t.experimental ? 1 + t.expRoom : 0);   // (2 FOUR CORNERS, 3 FIREBALL BLITZ)
                // (the teams as the match plays them; FIREBALL BLITZ, 0.3.24: with 128 added, so the page knows it plays in
                // teams: an older host sends its TEAMS setting there too, which FIREBALL BLITZ never played; TASKS.md E51)
                li.teamMode = (byte)(g.TeamsNow() | (g.Octagon ? 0x80 : 0));
                li.theOne = (byte)t.theOne;
                for (int p = 0; p < 4; ++p)
                {
                    string n = net.SlotFilled(p) ? PlayerLabel(p) : "OPEN";
                    li.name4[p] = n.Length > 16 ? n.Substring(0, 16) : n;
                    AvatarDesc d = (p == L && !a.viewing) ? a.settings.myAvatar : viewIn[p].avatar;
                    li.nation4[p] = (byte)(net.SlotFilled(p) ? Avatar.NationOf(d) : 0);
                    float[] c = ExpArenaView.PlayerRGB(g, a.sideRGB, p);
                    for (int k = 0; k < 3; ++k) li.rgb4[p, k] = (byte)Math.Round(Math.Clamp(c[k], 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
                    li.team4[p] = (byte)g.Team(p);
                    li.points4[p] = (short)Math.Max(-32000, Math.Min(32000, g.pl[p].score));
                    li.health4[p] = (byte)Math.Max(0, Math.Min(255, g.pl[p].health));
                    li.out4[p] = (byte)(g.Octagon && !g.Alive(p) ? 1 : 0);
                    li.st[p] = liveStats[p];
                }
                if (g.TeamCount() == 2)
                {   // (teams: the two ends are the two teams, their scores the teams')
                    li.score[0] = (byte)Math.Max(0, Math.Min(255, g.TeamScore(0)));
                    li.score[1] = (byte)Math.Max(0, Math.Min(255, g.TeamScore(1)));
                }
            }
            li.round = (byte)Math.Max(0, Math.Min(255, g.round));
            li.overtime = g.overtime ? (byte)1 : (byte)0;
            net.SetLive(li);
        }

        // a tank player's name on banners: the room's name for them, their career's, or the end's (BLUE, a country)
        // (four players, TASKS.md E54, native d511eb1: any corner a bot plays says BOT; a nameless corner 2 or 3 its colour;
        // the two ends' careers and names for corners 0 and 1 only)
        public string PlayerLabel(int p)
        {
            if (p < 0 || p >= Teams.MaxPlayers) return "OPEN";
            Game g = Shown;
            if (p < g.np && g.pl[p].isBot && (p == 1 || g.np == 4)) return "BOT";
            RoomInfo rm = a.watchIp != 0 ? a.watchRoom : net.Room;
            string n = rm.NameOf(rm.slot[p]);
            if (n != null) return n;
            if (p >= 2) return p == 2 ? "GREEN" : "VIOLET";   // (FOUR CORNERS' far places have no career or end of their own)
            if (a.careersOk && !string.IsNullOrEmpty(a.careers[p].name)) return a.careers[p].name;
            return SideName(p);
        }

        // the name a side goes by on banners: the country (UNITED STATES) or the end's colour
        public string SideName(int p)
        {
            Avatar.Nation N = Avatar.NationAt(a.sideNation[p]);
            if (N != null) return N.name;
            return a.sideRGB[p][2] > a.sideRGB[p][0] ? "BLUE" : "ORANGE";
        }

        // Remote players arrive at the network's packet rhythm; draw them eased toward the latest update (~35 ms) so
        // their avatars move smoothly. The local player and the bot are drawn as simulated. Big jumps snap.
        void SmoothPlayers(float dt)
        {
            Game g = Shown;
            var m = net.CurrentMode;
            bool networked = a.viewing || m == Online.Net.Mode.Host || m == Online.Net.Mode.Client;
            float k = 1f - MathF.Exp(-Math.Max(dt, 0f) * 28f);
            for (int p = 0; p < g.np; ++p)   // (FOUR CORNERS: four)
            {
                PlayerInput t = g.pl[p].@in;
                PlayerInput v = viewIn[p];
                bool remote = networked && (a.viewing || p != L) && !g.pl[p].isBot;
                if (!remote || !viewInValid[p] || M.Length(t.head.p - v.head.p) > 0.6f) { v.CopyFrom(t); viewInValid[p] = true; continue; }
                Pose head = Blend(v.head, t.head, k);
                var grips = new Pose[2];
                for (int h = 0; h < 2; ++h)
                    grips[h] = t.hand[h].tracked && v.hand[h].tracked && M.Length(t.hand[h].grip.p - v.hand[h].grip.p) < 0.6f
                        ? Blend(v.hand[h].grip, t.hand[h].grip, k) : t.hand[h].grip;
                v.CopyFrom(t);
                v.head = head;
                for (int h = 0; h < 2; ++h) v.hand[h].grip = grips[h];
            }
        }

        static Pose Blend(Pose from, Pose to, float k) => new Pose(M.Nlerp(from.q, to.q, k), M.Lerp(from.p, to.p, k));
    }
}
