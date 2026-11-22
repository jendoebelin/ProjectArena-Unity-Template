using System.Collections.Generic;
using ProjectArena.Online;
using ProjectArena.Sim;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using EventType = ProjectArena.Sim.EventType;
using Pose = ProjectArena.Sim.Pose;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

namespace ProjectArena
{
    // The game as the native main.cpp runs it in a headset: the lobby balcony with the menu at start, practice against
    // the bot, arena rooms on the LAN or the community relay (ArenaSession, Net), spectating (the hub of miniature
    // arenas, or one match from the gallery seat), voice chat. Drawn as natively: spawners, the balls (energy sphere +
    // spinning shell, team-tinted, dimmer when uncharged), the players as their avatars (not your own: first person;
    // remote players eased between their updates), their gauntlets, shields and knuckle glass, the room's waiting
    // players and the holding area (RoomView), the scoreboard and the jumbotron, the countries' flags (NATIONS), the
    // effects, the menus, the lobby and the avatar builder. humanPlayer false (editor demos): the lobby's bots play.
    public class MatchView : MonoBehaviour
    {
        public bool humanPlayer = true;
        public TextAsset arenaCollision;
        public GameObject ballModel, shellModel, spawnerModel, headModel, torsoModel;
        public GameObject shieldModel, knuckleModel;
        public AvatarLibrary avatarLibrary;   // the avatar parts and the gauntlets
        public GameObject strikezoneModel;
        public Shader fxShader;         // Shaders/Fx.shader (sprites, ribbons, glows)
        public Shader textShader;       // Shaders/Text.shader (the scoreboard's panels and text)
        public Texture2D fontTexture;   // open_font (the native 6 x 8 font)
        public Texture2D emblemTexture; // open_emblem (over the title menu)
        public Texture2D roomPictures;  // room_pictures (CHOOSE A ROOM: each room from above, a quarter each; native d4cf079)
        // strikezone.mdl is a disc of this radius (m), drawn scaled to the strike's own (native 3adf5dd, found by Grant,
        // ProjectArena-public pull request #26, 2026-11-18: drawn at 75 cm for a 70 cm strike, a ball could cross inside
        // the drawn ring and score no strike)
        public const float StrikeRingModel = 0.75f;
        public TextAsset soundEvents;   // audio/events.txt
        public AudioClip[] sounds;

        // each player's colour (native playerRGB): two players their end's; FOUR CORNERS four colours, or the teams'
        readonly Vector3[] TeamCol = { new Vector3(0.1f, 0.7f, 1.0f), new Vector3(1.0f, 0.4f, 0.1f), new Vector3(0.25f, 0.95f, 0.35f), new Vector3(0.75f, 0.4f, 1.0f) };
        AvatarDraw avatars;
        ArenaLook arenaLook;
        const int NP = Teams.MaxPlayers;   // (the places drawn: FOUR CORNERS' four)
        readonly PlayerInput[] drawIn = { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };   // a player as drawn: their input, their look

        Game g;
        readonly Transform[] core = new Transform[NP];   // BALL FEEL (TEST) CORE SOLID: a lit core inside the plasma
        readonly BallFeel feel = new BallFeel();
        readonly Transform[] ball = new Transform[NP], shell = new Transform[NP], head = new Transform[NP], torso = new Transform[NP];
        readonly Transform[] spawner = new Transform[NP];
        readonly Transform[,] shield = new Transform[NP, 2], knuckle = new Transform[NP, 2];
        readonly Transform[] strikeZone = new Transform[NP];
        readonly List<GameObject> arenaParts = new List<GameObject>(), hubParts = new List<GameObject>();
        List<CollisionTri> classicTris;
        // the EXPERIMENTAL rooms (task C3): their looks; the classic court's own models hidden there (native drawWorld:
        // the court, its floor and reflections, its glow layers), the stadium round them widened and lowered for CORNERS
        ExpArenaView expView;
        static readonly HashSet<string> ClassicOnly = new HashSet<string> { "arena", "floor", "floor_reflect", "floor_reflect_env", "court_fx", "court_pulse", "court_outline" };
        // FLOOR REFLECTIONS (TASKS.md E38, native c184b84): the glossy floor's mirrored court and stadium (off: the floor as it
        // is over the dark arena, only the mirrored things left out)
        static readonly HashSet<string> Mirrored = new HashSet<string> { "floor_reflect", "floor_reflect_env" };
        static readonly HashSet<string> Stadium = new HashSet<string> { "environment", "env_fx", "stadium_fx" };
        float leadFlash;   // FOUR CORNERS: a new leader washes the room in its colour (native leadFlash, seconds)
        // PASSTHROUGH (task D1): the frame's state (RoomPassthrough), the play space's boundary, the court's glass
        // and floor as drawn for your room, the fade toward it
        readonly RoomPassthrough pt = new RoomPassthrough();
        readonly List<Vector3> boundaryRaw = new List<Vector3>();
        readonly List<Vector2> boundary = new List<Vector2>();
        readonly List<XRInputSubsystem> inputSubs = new List<XRInputSubsystem>();
        float boundaryAt = -10f, boundaryNarrow, glassNow = 1f, floorNow = RoomPassthrough.FloorAlpha;
        bool mrNow, minimalNow, noCourtNow;
        List<RoomPassthrough.Glass> courtGlass = new List<RoomPassthrough.Glass>();
        readonly List<DesktopSpectator.GlowSection> courtGlow = new List<DesktopSpectator.GlowSection>();   // (ARENA GLASS)
        float arenaGlassNow = 1f;
        Renderer[] floorRs = new Renderer[0];
        MaterialPropertyBlock ptMpb;
        Material roomFade;
        // FIREBALL BLITZ (task A5): the fireballs (the ball on fire: projectile and shell), their flames, health, the card
        readonly Transform[] fireBall = new Transform[NP * Game.KMaxFire], fireShell = new Transform[NP * Game.KMaxFire];
        readonly Transform[] echoBall = new Transform[NP * Game.KMaxFire * 4], echoShell = new Transform[NP * Game.KMaxFire * 4];   // (four echoes each: BlitzView.Echo)
        readonly BlitzView blitz = new BlitzView();
        readonly Pose[] drawHeads = new Pose[NP];
        FxRenderer fx;
        readonly MatchFx mfx = new MatchFx();
        Deck deck;
        FxRenderer expFx;   // THE CUTAWAY (TASKS.md E42): the EXPERIMENTAL rooms' walls, lines and glows in their own batch, cut by a PC preset
        MaterialPropertyBlock mpb;
        float startTime;
        XrInput xr;
        AppSettings settings;
        AppState app;
        ArenaSession session;
        Online.Net net;
        Menu menu;
        Lobby lobby;
        Builder builder;
        RoomView room;
        Voice voice;
        Director director;
        CrowdStands stands;
        PanelBatch panels;
        bool prevMenuButton, startTitle = true;
        // the PC spectator for streamers (task D2): the PC build without a headset (or started with --spectate)
        DesktopSpectator desk;
        public bool Desktop => desk != null;
        int deskLayer;   // (this frame's VIEW LAYERS: 0 FULL, 1 NO WALLS, 2 MINIMAL, 3 KEY)
        uint viewKey = 0xFFFFFFFFu;
        int prevLocal = -1;
        PlayerInput myInput = new PlayerInput();   // your input as read
        NativeMixer mixer;
        int clockTick;   // the last second the last-10-seconds tick sounded for (0: none)
        double meetTick;   // the fist bump start: the last buzz (native a.meetTick)
        readonly bool[] reachCue = new bool[2];   // THE REACH ROUND THE FIST: a hand of yours could take your ball now (it brightens)
        // THE PUNCH LANDS (native f045e04; two players' report, 2026-11-17: a punch "kind of just felt like the ball hit the
        // slider in pong"): a knuckle hit's flash, and the ball squashed against the fist a moment
        struct PunchFx { public Vec3 pos; public float t, power; public int team; }
        readonly List<PunchFx> punches = new List<PunchFx>();
        readonly float[] squashT = { 9f, 9f, 9f, 9f }, squashPower = new float[4];
        readonly Vec3[] squashAxis = { new Vec3(0, 1, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0) };

        // a hit, and how the ball got there (TASKS.md E29, native de0f5c7): each ball's bounces since it was last thrown or
        // touched (the bounces themselves are too many for the log), with the bot's read of it when a bot is hit (the
        // owner's practice, 2026-11-17: UNBEATABLE hit by its own ball, punched back past it, 2-3 s later from behind)
        readonly Vec3[,] hitTrail = new Vec3[4, 8];
        readonly int[] hitTrailN = new int[4];
        void LogHitTrail(Game g, GameEvent e)
        {
            int own = e.type == EventType.Bounce || e.type == EventType.Throw || e.type == EventType.Spawn ? e.player : e.ball;
            if (own < 0 || own >= 4) return;
            if (e.type == EventType.Bounce)
            {
                if (hitTrailN[own] < 8) hitTrail[own, hitTrailN[own]] = e.pos;
                ++hitTrailN[own];
                return;
            }
            if (e.type == EventType.Throw || e.type == EventType.Spawn || e.type == EventType.KnuckleBlock ||
                e.type == EventType.ShieldBlock || e.type == EventType.Caught)
            {   // (thrown or touched: its trail starts again)
                hitTrailN[own] = 0;
                return;
            }
            if (e.type != EventType.HitPlayer) return;
            var sb = new System.Text.StringBuilder($"[Match] hit trail: ball {own} into player {e.player} after {hitTrailN[own]} bounces:");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < System.Math.Min(8, hitTrailN[own]); ++i)
                sb.Append(string.Format(ci, " ({0:F2} {1:F2} {2:F2})", hitTrail[own, i].x, hitTrail[own, i].y, hitTrail[own, i].z));
            if (e.player >= 0 && e.player < g.np && g.pl[e.player].isBot)
            {
                PlayerState s = g.pl[e.player];
                sb.Append(string.Format(ci, "; its read of its own ball: mode {0} plan {1} catch {2} hitT {3:+0.00;-0.00}, of the other: mode {4} plan {5}",
                                        s.botRead[1].mode, s.botRead[1].plan, s.botRead[1].catchIt ? 1 : 0,
                                        s.botRead[1].hitT >= 0 ? s.botRead[1].hitT - g.now : -1f, s.botRead[0].mode, s.botRead[0].plan));
            }
            Debug.Log(sb.ToString());
            hitTrailN[own] = 0;
        }

        // the flash where the ball met the knuckles, and that ball squashed along its new way (power 0.2 .. 1)
        void AddPunchFx(Game g, GameEvent e, float power)
        {
            if (punches.Count < 6) punches.Add(new PunchFx { pos = e.pos, t = 0, power = power, team = e.player & 1 });
            int b = e.ball;   // (a guest isn't sent which ball: the nearest)
            if (b < 0 || b >= g.np)
            {
                float best = 1e9f;
                for (int q = 0; q < g.np; ++q) { float d = M.Length(g.pl[q].ball.pos - e.pos); if (d < best) { best = d; b = q; } }
            }
            if (b >= 0 && b < 4)
            {
                Vec3 v = g.pl[b].ball.vel;
                squashT[b] = 0; squashPower[b] = power; squashAxis[b] = M.Length(v) > 1e-3f ? M.Normalize(v) : new Vec3(0, 1, 0);
            }
        }

        void DrawPunchFx(PunchFx f) => DrawPunchFlash(fx, f.pos, f.t, f.power, f.team, settings.fxLevel, app.viewHead.p);

        // a white-hot flash, quick to cool to the puncher's colour, and a wider glow rolling out (0.18 s; t s after the
        // punch, power 0.2 .. 1, team 0 blue 1 orange; HIT EFFECTS SOFT and LOW tone it down)
        public static void DrawPunchFlash(FxRenderer fx, Vec3 pos, float t, float power, int team, int fxLevel, Vec3 eye)
        {
            float[][] cols = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            float[] tc = cols[team & 1];
            float u = t / 0.18f;
            if (u >= 1f) return;
            float lvl = fxLevel == 0 ? 1f : fxLevel == 1 ? 0.55f : 0.25f;
            float k = (1f - u) * (1f - u) * lvl * (0.6f + 0.6f * power);
            var core = new Vector4((tc[0] * u + (1f - u)) * 1.4f * k, (tc[1] * u + (1f - u)) * 1.4f * k, (tc[2] * u + (1f - u)) * 1.4f * k, 1f);
            var glow = new Vector4(tc[0] * 0.8f * k, tc[1] * 0.8f * k, tc[2] * 0.8f * k, 1f);
            float EaseOut(float x) { x = M.Clampf(x, 0f, 1f); return 1f - (1f - x) * (1f - x); }
            fx.Sprite(pos, (0.10f + 0.14f * power) * (0.5f + 0.8f * EaseOut(u * 1.6f)), core, eye, 0);
            fx.Sprite(pos, (0.22f + 0.30f * power) * EaseOut(u), glow, eye, 0);
        }

        // a ball's squash this frame for the shader (its object's own axes): the axis and amount, zero when none
        Vector4 SquashOf(int p, BallState st, Quaternion objectRot) =>
            st != BallState.Flying ? Vector4.zero : SquashFor(squashT[p], squashPower[p], squashAxis[p], objectRot);

        // t s after a punch of this power along axis (native space): the shader's _Squash for an object turned objectRot
        // (squashed by up to a third along the axis and bulging half that round it, round again in 0.14 s)
        public static Vector4 SquashFor(float t, float power, Vec3 axis, Quaternion objectRot)
        {
            if (t < 0f || t >= 0.14f) return Vector4.zero;
            float amt = power * 0.32f * (1f - t / 0.14f) * (1f - t / 0.14f);
            Vector3 ax = Quaternion.Inverse(objectRot) * NativeSpace.ToUnity(axis);
            return new Vector4(ax.x, ax.y, ax.z, amt);
        }
        XROrigin origin;
        bool refreshAsked;
        float refreshTries;
        // The Crowd's sound (native updateCrowd): a murmur that swells with the hype, a cheer when an end passes 60
        double crowdAt = -1;
        static readonly int TintMul = Shader.PropertyToID("_TintMul"), UVOff = Shader.PropertyToID("_UVOff"),
            EmissiveAdd = Shader.PropertyToID("_EmissiveAdd"), Rim = Shader.PropertyToID("_Rim"),
            UseChannels = Shader.PropertyToID("_UseChannels"), ChanCols = Shader.PropertyToID("_ChanCols"),
            SquashId = Shader.PropertyToID("_Squash");

        // Android filters broadcast traffic on Wi-Fi unless a MulticastLock is held (the native game takes it through
        // JNI): without it LAN games are not found
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject multicastLock;
        static bool AcquireMulticastLock()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "projectarena");
                    multicastLock.Call("setReferenceCounted", false);
                    multicastLock.Call("acquire");
                    return true;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Net] multicast lock: " + e.Message); return false; }
        }
#endif

        // the community server of this build (servers.local.cfg, written into Resources/servers_default by the build;
        // not in git)
        static string DefaultServer()
        {
            var t = Resources.Load<TextAsset>("servers_default");
            return t != null ? t.text.Trim() : "";
        }

        void Start()
        {
            Reports.Install();
            Online.Net.Log = s => Debug.Log("[Net] " + s);
#if UNITY_ANDROID && !UNITY_EDITOR
            Online.Net.AcquireMulticastLock = AcquireMulticastLock;
#endif
            g = new Game();
            classicTris = ArenaCollision.Load(arenaCollision.bytes);
            g.SetArena(classicTris);
            g.tun.advanced = true;   // the default rules (owner: Advanced is the most played); settings.cfg advanced= wins
            settings = new AppSettings();
            if (humanPlayer) { settings.Load(g.tun); settings.LoadAvatar(); }   // (the rules: the MATCH OPTIONS last chosen)
            net = new Online.Net();
            app = new AppState { game = g, settings = settings, net = net, lobby = true };
            if (humanPlayer) app.mutes.Load(settings.Folder);   // (MUTED PLAYERS, TASKS.md E46: once app is made; before, Start threw here)
            string defServer = DefaultServer();
            menu = new Menu(app) { emblem = emblemTexture, defaultServer = defServer };
            lobby = new Lobby(app, menu);
            builder = new Builder(app);
            session = new ArenaSession(app) { menu = menu, builder = builder, classicArena = classicTris };
            session.StartNetwork(defServer, humanPlayer);
            if (humanPlayer && !string.IsNullOrEmpty(settings.server))
            {   // reports saved earlier
                var ob = Reports.OutboxFiles(app);
                if (ob.Count > 0) Reports.SendAll(app, ob);
            }
            room = new RoomView(session);
            voice = new Voice(session);
            director = new Director(session);
            if (humanPlayer) director.Load();
            app.director = director;
            bool deskWanted = humanPlayer && DesktopSpectator.Wanted();
            if (humanPlayer) ProcessPriority.Set(ProcessPriority.Arg() ?? (deskWanted ? "normal" : "above"));   // (PROCESS PRIORITY)
            if (deskWanted)
            {   // no headset: the spectator window (native desktopMain)
                desk = new DesktopSpectator(app, director) { hintUntil = Time.timeAsDouble + 8.0 };
                startTitle = false;
                Camera dc = Camera.main;
                if (dc != null)
                {
                    var tpd = dc.GetComponent<TrackedPoseDriver>();
                    if (tpd != null) tpd.enabled = false;   // (the window's camera is the spectator's, not a headset's)
                }
                Debug.Log("[Desktop] spectator window (" + (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--spectate") >= 0 ? "--spectate" : "no headset found") + ")");
            }
            if (humanPlayer)
            {
                xr = new XrInput { tunePitchDeg = settings.tunePitchDeg, tuneFwd = settings.tuneFwd, tuneUp = settings.tuneUp };
                origin = FindFirstObjectByType<XROrigin>();
                Debug.Log($"[Settings] {settings.name}: volume {settings.volume}, rules {Scoreboard.RulesLine(g.tun)}, help seen {settings.helpSeen}, " +
                          $"online {(settings.onlinePref ? "on" : "off")}, server {(string.IsNullOrEmpty(settings.server) ? "not set" : "set")}");
            }
            mpb = new MaterialPropertyBlock();
            startTime = Time.time;
            if (fxShader != null) fx = new FxRenderer(fxShader, textShader, fontTexture, emblemTexture, roomPictures);
            if (fxShader != null) expFx = new FxRenderer(fxShader, textShader, fontTexture, emblemTexture, roomPictures, cuttable: true);
            avatars = new AvatarDraw(avatarLibrary, textShader) { startTime = Time.timeAsDouble };
            deck = new Deck(Shader.Find("ProjectArena/Native"));   // (THE DECK, TASKS.md E40: drawn as the arena is)
            panels = new PanelBatch(textShader);
            if (desk != null) { desk.panels = panels; desk.closePanels = new PanelBatch(textShader, 305); }
            hsPanels = new PanelBatch(textShader);
            stands = new CrowdStands
            {
                foam = avatarLibrary != null ? avatarLibrary.crowdFoam : null, heart = avatarLibrary != null ? avatarLibrary.crowdHeart : null,
                star = avatarLibrary != null ? avatarLibrary.crowdStar : null, hotdog = avatarLibrary != null ? avatarLibrary.crowdHotdog : null,
                smile = avatarLibrary != null ? avatarLibrary.crowdSmile : null, flagTex = n => avatars.FlagTexture(n),
            };
            arenaLook = FindFirstObjectByType<ArenaLook>();
            Debug.Log($"[Avatars] {avatars.PartCount} part models, {avatars.SwingRigCount} hair swing rigs, {avatars.TuckCount} tuck maps; you: " +
                      $"{Avatar.OptionName(Avatar.Field.Suit, settings.myAvatar.suit)} / {Avatar.OptionName(Avatar.Field.Headgear, settings.myAvatar.headgear)}");
            var listener = FindFirstObjectByType<AudioListener>();
            if (listener != null && soundEvents != null)
            {
                mixer = listener.gameObject.AddComponent<NativeMixer>();
                mixer.Init(soundEvents.text, sounds);
            }
            voice.mixer = mixer;
            app.mixer = mixer;   // (the SOUND STUDIO swaps clips in it)
            // the arena's models: in the hub only the environment and the floor stay; the rest are drawn small
            var arenaRoot = GameObject.Find("Arena");
            var minis = new List<GameObject>();
            if (arenaRoot != null)
                foreach (Transform c in arenaRoot.transform)
                {
                    string n = c.gameObject.name;
                    (n == "environment" || n == "floor" ? hubParts : arenaParts).Add(c.gameObject);
                    if (n == "floor" || n == "arena" || n == "court_fx" || n == "court_pulse") minis.Add(c.gameObject);
                }
            room.miniModels = minis.ToArray();
            {   // FOUL LINE (TASKS.md E3): the court's models ready to be drawn with the line moved (the hub's miniatures too)
                int parts = 0;
                foreach (var go in arenaParts) if (System.Array.IndexOf(CourtStretch.Models, go.name) >= 0) parts += CourtStretch.Prepare(go);
                foreach (var go in hubParts) if (System.Array.IndexOf(CourtStretch.Models, go.name) >= 0) parts += CourtStretch.Prepare(go);
                Debug.Log($"[Court] {parts} sections follow the FOUL LINE");
            }
            var nativeShader = spawnerModel != null ? spawnerModel.GetComponentInChildren<Renderer>(true)?.sharedMaterial?.shader : null;
            {   // PASSTHROUGH: the court's glass sections and the floor (their opacity for your room), the fade
                ptMpb = new MaterialPropertyBlock();
                foreach (var go in arenaParts)
                {
                    if (go.name == "arena") courtGlass = RoomPassthrough.GlassOf(go.GetComponentsInChildren<Renderer>(true));
                    if (go.name == "arena" || go.name == "court_fx") courtGlow.AddRange(DesktopSpectator.GlowOf(go.GetComponentsInChildren<Renderer>(true)));   // (ARENA GLASS)
                    if (go.name == "court_outline") go.SetActive(false);
                }
                foreach (var go in hubParts) if (go.name == "floor") floorRs = go.GetComponentsInChildren<Renderer>(true);
                roomFade = RoomPassthrough.FadeMaterial(nativeShader);
                Debug.Log($"[Passthrough] {courtGlass.Count} glass sections, {floorRs.Length} floor renderers, " +
                          (arenaParts.Exists(x => x.name == "court_outline") ? "the court's outline" : "no court_outline in the scene"));
            }
            expView = new ExpArenaView(nativeShader, transform);
            for (int p = 0; p < NP; ++p)
            {
                spawner[p] = Spawn(spawnerModel, $"spawner {p}");
                Place(spawner[p], g.tun.spawner[p], Quat.Identity);   // (each frame too: a room's rules move them)
                strikeZone[p] = Spawn(strikezoneModel, $"strike zone {p}");
                ball[p] = Spawn(ballModel, $"ball {p}");
                shell[p] = Spawn(shellModel, $"ball shell {p}");
                core[p] = Spawn(ballModel, $"ball core {p}");   // (BALL FEEL CORE SOLID: the ball's model, lit and opaque)
                foreach (var r in core[p].GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = BallFeel.LitCopy(r.sharedMaterials);
                core[p].gameObject.SetActive(false);
                torso[p] = Spawn(torsoModel, $"torso {p}");
                head[p] = Spawn(headModel, $"head {p}");
                for (int h = 0; h < 2; ++h)
                {
                    shield[p, h] = Spawn(shieldModel, $"shield {p}.{h}");
                    knuckle[p, h] = Spawn(knuckleModel, $"knuckle glass {p}.{h}");
                }
                for (int k = 0; k < Game.KMaxFire; ++k)
                {
                    fireBall[p * Game.KMaxFire + k] = Spawn(ballModel, $"fireball {p}.{k}");
                    fireShell[p * Game.KMaxFire + k] = Spawn(shellModel, $"fireball shell {p}.{k}");
                    fireBall[p * Game.KMaxFire + k].gameObject.SetActive(false);
                    fireShell[p * Game.KMaxFire + k].gameObject.SetActive(false);
                    for (int e = 0; e < 4; ++e)
                    {
                        int j = (p * Game.KMaxFire + k) * 4 + e;
                        echoBall[j] = Spawn(ballModel, $"fireball {p}.{k} echo {e + 1}");
                        echoShell[j] = Spawn(shellModel, $"fireball shell {p}.{k} echo {e + 1}");
                        echoBall[j].gameObject.SetActive(false);
                        echoShell[j].gameObject.SetActive(false);
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (net == null) return;
            if (net.InRoom) net.Leave();
            if (net.Online) net.GoOffline();
            net.Shutdown();
        }

        void OnApplicationQuit() => OnDestroy();

        Transform Spawn(GameObject model, string name)
        {
            var go = Instantiate(model, transform);
            go.name = name;
            return go.transform;
        }

        static void Place(Transform t, Vec3 p, Quat q, float scale = 1f)
        {
            t.SetPositionAndRotation(NativeSpace.ToUnity(p), NativeSpace.ToUnity(q));
            t.localScale = new Vector3(scale, scale, scale);
        }

        // a draw's DrawParams: tint and alpha multipliers, UV offset, emissive added, rim light, the look's channels
        void Tint(Transform t, Vector3 mul, Vector2 uvOff = default, float alphaMul = 1f, Vector3 emissiveAdd = default,
                  Vector4 rim = default, Vector4[] channels = null, Vector4 squash = default)
        {
            mpb.SetVector(TintMul, new Vector4(mul.x, mul.y, mul.z, alphaMul));
            mpb.SetVector(SquashId, squash);   // (THE PUNCH LANDS: a ball just punched; zero for everything else)
            mpb.SetVector(UVOff, NativeSpace.WrapUV(uvOff));   // (v flipped for Unity's textures; kept in [0, 1): native wrapUV)
            mpb.SetVector(EmissiveAdd, emissiveAdd);
            mpb.SetVector(Rim, rim);
            mpb.SetFloat(UseChannels, channels != null ? 1f : 0f);
            if (channels != null) mpb.SetVectorArray(ChanCols, channels);
            foreach (var r in t.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        }

        // a fireball's tint: fire, with a third of its thrower's colour (native drawFireballs)
        static Vector3 FireTint(Vector3 team) => new Vector3(2.0f * (0.65f * BlitzView.Fire[0] + 0.35f * team.x),
                                                             2.0f * (0.65f * BlitzView.Fire[1] + 0.35f * team.y),
                                                             2.0f * (0.65f * BlitzView.Fire[2] + 0.35f * team.z));

        // READY UP (rooms for four, native c5eb50a): before a match, PRESS A WHEN READY in front of you until you have,
        // then who the match waits for; READY over the others who are
        void DrawReadyUp(FxRenderer fx)
        {
            Game g = app.game;
            int L = net.LocalSlot();
            if (!net.InRoom || g.np != 4 || g.match != MatchState.WaitingForPlayers || L < 0 || menu.open) return;
            var cyan = new Vector4(0.3f, 0.85f, 1.0f, 1); var green = new Vector4(0.3f, 1.0f, 0.45f, 1); var white = new Vector4(1, 1, 1, 1);
            Pose h = app.viewHead;
            Vec3 f = h.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            if (M.Length(f) < 1e-3f) return;
            f = M.Normalize(f);
            var card = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(-f.x, -f.z)), h.p + f * 1.3f + new Vec3(0, -0.15f, 0));
            int waiting = 0;
            for (int p = 0; p < g.np; ++p) waiting += !g.pl[p].isBot && g.pl[p].@in.headTracked && !g.pl[p].ready ? 1 : 0;
            if (!g.pl[L].ready)
            {
                fx.Text("PRESS " + app.ReadyName + " WHEN READY", card, 0.07f, cyan);   // (READY's button: A as standard)
                fx.Text("THE MATCH STARTS WHEN EVERYONE IS", new Pose(card.q, card.Transform(new Vec3(0, -0.10f, 0))), 0.03f, white);
            }
            else fx.Text(waiting != 0 ? $"READY - WAITING FOR {waiting} MORE" : "READY", card, 0.05f, green);
            for (int p = 0; p < g.np; ++p)
            {   // the others who are ready (people only: a bot always is)
                if (p == L || g.pl[p].isBot || !g.pl[p].ready) continue;
                Vec3 at = g.pl[p].@in.head.p + new Vec3(0, 0.35f, 0);
                Vec3 to = h.p - at;
                to.y = 0;
                if (M.Length(to) < 1e-3f) continue;
                fx.Text("READY", new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(to.x, to.z)), at), 0.08f, green);
            }
        }

        // the fist bump start, in a room or in practice (FIST BUMP FIRST): closer fists buzz harder (you can feel the other
        // hand coming); otherwise a light reminder every 2 s (native stepFrame)
        void MeetBuzz(Game g, int l)
        {
            if (l < 0 || l > 1) return;
            PlayerState o = g.pl[1 - l];
            float best = 9f;
            int bh = 1;
            for (int h = 0; h < 2; ++h)
                for (int oh = 0; oh < 2; ++oh)
                    if (g.pl[l].@in.hand[h].tracked && o.@in.hand[oh].tracked)
                    {
                        float d = g.FistGap(l, h, 1 - l, oh);
                        if (d < best) { best = d; bh = h; }
                    }
            if (g.meetLog)
            {
                g.meetLog = false;
                Debug.Log($"[Match] meet: closest fists {g.meetGap * 100:F0} cm (bump under {g.tun.fistBumpGap * 100:F0}), triggers {g.meetTrig[0]:F2} / {g.meetTrig[1]:F2}");
            }
            double t = Time.realtimeSinceStartupAsDouble;
            if (best < 0.45f && t - meetTick > 0.12) { meetTick = t; Buzz(bh, 0.15f + 0.6f * (1f - best / 0.45f), 0.05f); }
            else if (t - meetTick > 2.0) { meetTick = t; Buzz(0, 0.25f, 0.06f); Buzz(1, 0.25f, 0.06f); }
        }

        // a controller's motor (native xrApplyHapticFeedback)
        static void Buzz(int hand, float amplitude, float seconds)
        {
            var c = (hand == 0 ? XRController.leftHand : XRController.rightHand) as XRControllerWithRumble;
            c?.SendImpulse(amplitude, seconds);
        }

        // the native game asks the Quest for 90 Hz (xrRequestDisplayRefreshRateFB), a Quest 2 for 72 (native 0.3.17:
        // its GPU is about half a Quest 3's, and FIREBALL BLITZ keeps a Quest 3's ~65-80 % busy at 90, too close on a
        // Quest 2; 72 gives it a quarter more time a frame); retried while XR starts up
        public static float RefreshRateFor(string model) => model != null && model.Contains("Quest 2") ? 72f : 90f;

        void RequestRefreshRate()
        {
            if (refreshAsked || (refreshTries += Time.unscaledDeltaTime) > 10f) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            string model = "";
            try { using (var build = new AndroidJavaClass("android.os.Build")) model = build.GetStatic<string>("MODEL"); }   // (ro.product.model, as native)
            catch (System.Exception) { }
            float hz = RefreshRateFor(model);
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
                if (d.running && d.TryRequestDisplayRefreshRate(hz))
                {
                    refreshAsked = true;
                    Debug.Log($"[Match] display: {hz:F0} Hz asked ({model})");
                }
#else
            refreshAsked = true;
#endif
        }

        // The Crowd in the arena (native updateCrowd): the ends' lights flare with each burst of cheers, a cheer when an
        // end's hype passes 60, a short crowd blip as they cheer (SETTINGS > CROWD: full, flags only, off)
        // BALL IN FLIGHT (TASKS.md E52, native bee88ee updateFlightSound, 0.3.24; FlightSound.cs): every ball and fireball
        // that can hurt you, heard as it flies, louder as it comes at you; the loudest four, each kept on its own voice while
        // it flies (so it doesn't jump between them). Not your own, not a teammate's, not one that's harmless (fouled, on its
        // way home, already scored); as you see it (BALL LOOK-AHEAD's drawing), from your head as it turns. Playing only:
        // not watching, waiting outside or in the lobby
        double perfSince, perfLast, perfWorst; int perfFrames;   // (the spectator window's perf line)
        readonly Blink blinkT = new Blink();   // (FIREBALL BLITZ's moves through dark)
        int seenLateBlocks, seenLateHits;   // (BLOCKS ONLINE's session log)
        readonly int[] flightKey = { -1, -1, -1, -1 };
        readonly List<(int key, FlightParams f)> flightCands = new List<(int, FlightParams)>();
        void UpdateFlightSound()
        {
            if (mixer == null) return;
            mixer.SetFlightLevel(settings.ballSound ? 1f : 0f);
            // MUSIC (TASKS.md E60, native 1159a46): SETTINGS' MUSIC row, never in the SOUND STUDIO
            mixer.SetMusicLevel(NativeMixer.MusicLevel(app.InLobby, app.studio != null && app.studio.on, settings.musicMenu, settings.musicGame, settings.musicVolume));
            Game g = app.game;
            int L = session.L;
            flightCands.Clear();
            bool listening = humanPlayer && settings.ballSound && !app.viewing && !app.spectating && !app.InLobby && !net.LocalWaiting() &&
                             L >= 0 && L < g.np && g.Alive(L);
            if (listening)
            {
                Sim.Pose head = g.pl[L].@in.head;
                Vec3 r = head.q.Rotate(new Vec3(1, 0, 0));
                r = Sim.M.Normalize(new Vec3(r.x, 0, r.z));
                if (Sim.M.Length2(r) < 0.5f) { Vec3 f = g.Facing(L); r = new Vec3(-f.z, 0, f.x); }
                bool Hurts(Ball b) => b.instigator >= 0 && b.instigator < g.np && b.instigator != L && !g.Friendly(L, b.instigator);
                for (int p = 0; p < g.np; ++p)
                {
                    Ball b = g.pl[p].ball;
                    if (b.state == BallState.Flying && !b.fouled && !b.homing && !b.scored && Hurts(b))
                        flightCands.Add((p, FlightParams.Of(b.pos, b.vel, head.p, r)));
                }
                if (g.Octagon)
                    for (int p = 0; p < g.np; ++p)
                        for (int k = 0; k < Game.KMaxFire; ++k)
                        {
                            Ball f = g.fire[p][k];
                            if (f.state == BallState.Flying && Hurts(f)) flightCands.Add((8 + p * Game.KMaxFire + k, FlightParams.Of(f.pos, f.vel, head.p, r)));
                        }
                flightCands.Sort((x, y) => y.f.gain.CompareTo(x.f.gain));
                if (flightCands.Count > NativeMixer.KFlights) flightCands.RemoveRange(NativeMixer.KFlights, flightCands.Count - NativeMixer.KFlights);
            }
            int n = flightCands.Count;
            var used = new bool[NativeMixer.KFlights];
            var slotOf = new int[NativeMixer.KFlights];
            for (int i = 0; i < n; ++i)
            {   // (a ball keeps the voice it had)
                slotOf[i] = -1;
                for (int s = 0; s < NativeMixer.KFlights; ++s) if (flightKey[s] == flightCands[i].key && !used[s]) { slotOf[i] = s; used[s] = true; break; }
            }
            for (int i = 0; i < n; ++i)
                if (slotOf[i] < 0)
                    for (int s = 0; s < NativeMixer.KFlights; ++s) if (!used[s]) { slotOf[i] = s; used[s] = true; break; }
            var keyNow = new[] { -1, -1, -1, -1 };
            for (int i = 0; i < n; ++i) { keyNow[slotOf[i]] = flightCands[i].key; mixer.SetFlight(slotOf[i], flightCands[i].f); }
            for (int s = 0; s < NativeMixer.KFlights; ++s)
            {
                if (keyNow[s] < 0) mixer.SetFlight(s, FlightParams.Silent);
                flightKey[s] = keyNow[s];
            }
        }

        // (BALL IN FLIGHT: no frames, no whoosh; native 3fccaf9 Audio::silenceFlights)
        void OnApplicationPause(bool paused) { if (paused && mixer != null) { mixer.SilenceFlights(); mixer.SetMusicLevel(0); } }   // (no frames: no whoosh, no music)
        void OnApplicationFocus(bool focus) { if (!focus && mixer != null) { mixer.SilenceFlights(); mixer.SetMusicLevel(0); } }

        void UpdateCrowd(float dt)
        {
            CrowdInfo c = net.Crowd(app.watchIp);
            // (the fans' cheers make no sound in the arena: TASKS.md E53, native d511eb1; the owner, 2026-11-20: "change the
            // short cheer in game that comes from the cheers and just remove it, i think the flags and lights are enough".
            // Before: a short crowd blip as they cheered, and a cheer when an end's hype passed 60)
            bool show = settings.crowdMode < 2;   // SETTINGS > CROWD: FLAGS AND LIGHTS, or OFF
            if (!show) { app.crowdFlare[0] = app.crowdFlare[1] = 0f; stands.Clear(); }
            stands.Step(dt);
            if (c != null && c.at != crowdAt)
            {   // a new update (twice a second)
                crowdAt = c.at;
                for (int p = 0; p < 2; ++p)
                    if (c.cheers[p] > 0 && show)
                    {
                        app.crowdFlare[p] = Mathf.Max(app.crowdFlare[p], Mathf.Min(1f, 0.35f + c.cheers[p] / (4f + 2f * c.fans)));
                        stands.Burst(p, Mathf.Min(40, 2 * c.cheers[p]));   // rapid cheering pours more out
                    }
            }
        }

        void Update()
        {
            RequestRefreshRate();
            float dt = Time.deltaTime;
            int L = session.L;
            PlayerInput mine;
            Pose aim = Pose.Identity;
            bool aimValid = false;
            int ph = settings.pointerHand;
            if (desk != null) mine = myInput = DesktopFrame(dt);
            else if (humanPlayer)
            {
                // where you are placed (native viewTarget): moving between the lobby, your end, the meeting, the holding
                // area, the hub and a watched match places you again
                uint key = session.ViewKey();
                if (key != viewKey || L != prevLocal)
                {   // FIREBALL BLITZ's moves (to the dugout, back, the room closing in) through dark (TASKS.md E62, native
                    // 123205c startBlink: 0.12 s to dark, the move, 0.30 s back); every other move at once
                    bool blink = L == prevLocal && Blink.Wanted(session.game.Octagon, viewKey, key);
                    viewKey = key; prevLocal = L;
                    if (blink) blinkT.Start(); else xr.recentered = false;
                }
                if (blinkT.Step(dt)) xr.recentered = false;   // (dark: the move now)
                session.ViewTarget(L, out Vec3 spot, out Vec3 fwd);
                xr.ctlTilt = settings.ctlTilt; xr.ctlTwist = settings.ctlTwist; xr.ctlTurn = settings.ctlTurn;   // (CONTROLLER ANGLE, live)
                xr.buttons = settings.buttons;   // (BUTTONS: each job's buttons, live)
                mine = myInput = xr.Read(dt, g.now, spot, fwd);
                app.ctlKind = xr.ctlKind;
                if (xr.justRecentered && builder != null && builder.open)
                {   // (recentered: the builder's mirror and panel in front of you again, native 7ed11aa)
                    builder.Place(mine.head);
                    Debug.Log("[Builder] placed again (recentered)");
                }
                if (xr.namesClicked)
                {   // PLAYERS' NAMES on / off (native readInput, 0.3.19)
                    app.showNames = !app.showNames;
                    if (net.LocalWaiting())   // (waiting in a room: the queue screen comes and goes with them, native 0.3.20)
                        net.NoticeLocal(app.showNames ? "NAMES AND QUEUE SCREEN ON" : "NAMES AND QUEUE SCREEN OFF");
                    else net.NoticeLocal(app.showNames ? "PLAYERS' NAMES ON" : "PLAYERS' NAMES OFF");
                }
                app.recentered = xr.recentered;
                if (origin != null) xr.ApplyTo(origin);
                aim = xr.aim[ph]; aimValid = xr.aimValid[ph];
                voice.ptt = xr.talkValue > 0.6f;   // push to talk: TALK's buttons (the left grip as standard: BUTTONS; before the menu takes it)
                app.viewHead = mine.head;
                // the game starts with the title menu (and HOW TO PLAY on the first launch)
                if (startTitle && mine.headTracked && xr.recentered)
                {
                    startTitle = false;
                    menu.Open(mine.head, Lobby.Spot);
                    menu.titleOpen = true;
                    if (!settings.helpSeen) menu.helpOpen = true;
                }
                // the left menu button opens and closes the menu (and closes the builder)
                if (xr.menuPressed && !prevMenuButton)
                {
                    if (builder.open) builder.Close();
                    else if (menu.open) menu.Close();
                    else menu.Open(mine.head, Lobby.Spot);
                }
                prevMenuButton = xr.menuPressed;
                if (menu.Update(mine, aim, aimValid)) Buzz(ph, 0.4f, 0.03f);
                if (lobby.Update(mine, xr.rawTrigger[ph], aim, aimValid)) Buzz(ph, 0.4f, 0.03f);
                if (room.UpdateKiosk(mine, xr.rawTrigger[ph], aim, aimValid, menu)) Buzz(ph, 0.4f, 0.03f);
                if (room.UpdateHub(mine, aim, aimValid, menu)) Buzz(ph, 0.5f, 0.05f);
                if (app.openBuilder)
                {   // CUSTOMIZE AVATAR (the menu, the lobby's poster): the mirror ahead of you, the panel beside it
                    app.openBuilder = false;
                    if (avatars.PartCount > 0) builder.Open(mine);
                }
                if (builder.Update(mine, aim, aimValid)) Buzz(ph, 0.35f, 0.025f);
                // watching a match: the sticks fly, A / X / Y the cameras, the auto camera; DIRECTOR TOOLS
                director.Update(xr, mine, dt, menu.open);
                if (director.UpdatePanel(mine, aim, aimValid, xr, menu)) Buzz(ph, 0.35f, 0.025f);
                if (builder.notice.Length > 0) { net.NoticeLocal(builder.notice); builder.notice = ""; }
                if (app.quitRequested) { Debug.Log("[Menu] quit game"); Application.Quit(); }
                if (mixer != null) mixer.volume = settings.volume / 10f;
                mfx.fxLevel = settings.fxLevel;
            }
            else mine = new PlayerInput();   // (editor demo: watching the lobby's bots)
            List<GameEvent> events = session.Step(mine, dt);
            L = session.L;
            Game shown = session.Shown;
            if (humanPlayer) voice.Update();
            if (Reports.done != 0) { string st = Reports.Status; if (st.Length > 0) net.NoticeLocal(st.Length > 47 ? st.Substring(0, 47) : st); Reports.done = 0; }
            UpdateCrowd(dt);
            UpdateFlightSound();   // (BALL IN FLIGHT, TASKS.md E52)
            {   // BLOCKS ONLINE in the session log (TASKS.md E61, native 489711e): each late block counted, each wait that ended
                // in a hit (the host's game)
                Game hg = app.game;
                if (hg.lateBlocks > seenLateBlocks || hg.lateWaitHits > seenLateHits)
                {
                    string[] kind = { "a late shield block counted", "a late knuckle-guard punch counted", "no block came: the hit" };
                    int k = hg.lateLastKind >= 0 && hg.lateLastKind <= 2 ? hg.lateLastKind : 2;
                    Debug.Log($"[Net] blocks online: {kind[k]} (player {hg.lateLastPlayer}, round trip {hg.lateLastRtt * 1000f:F0} ms; {hg.lateBlocks} late blocks, {hg.lateWaitHits} waits ended in a hit so far)");
                }
                seenLateBlocks = hg.lateBlocks;
                seenLateHits = hg.lateWaitHits;
            }
            // the events: sounds from the listener's side, haptics on your hands (native handleEvents)
            Camera cam = Camera.main;
            Vec3 ear = cam != null ? XrInput.ToNative(cam.transform.position) : app.viewHead.p;
            bool playing = humanPlayer && !app.viewing;
            leadFlash = Mathf.Max(0f, leadFlash - dt);
            foreach (var e in events)
            {
                if (e.type == EventType.LeadChange) leadFlash = 2.5f;   // (FOUR CORNERS: the room washed in the new leader's colour)
                if (e.type == EventType.HitPlayer || e.type == EventType.RoundEnd || e.type == EventType.MatchEnd)
                    Debug.Log($"[Match] {Game.EventName(e.type)} p{e.player}: score {shown.pl[0].score}-{shown.pl[1].score}, rounds {shown.pl[0].roundWins}-{shown.pl[1].roundWins}");
                else if (e.type == EventType.Throw)
                    Debug.Log($"[Match] Throw p{e.player} hand {e.hand}: {e.value:F1} m/s");
                if (mixer != null)
                {
                    string skey = Feedback.SoundKey(e, L);
                    if (!mixer.Has(skey)) skey = Game.EventName(e.type);
                    if (!mixer.Has(skey) && e.type == EventType.FistBump) skey = "KnuckleBlock";   // a solid knock
                    // FIREBALL BLITZ (native, sounds of their own to come): a knockout the lead's chime, a fizzle a failed
                    // throw's puff, the room closing in the countdown's beep
                    if (!mixer.Has(skey) && e.type == EventType.Knockout) skey = mixer.Has("LeadChange") ? "LeadChange" : "ShieldBreak";
                    if (!mixer.Has(skey) && e.type == EventType.Fizzle) skey = "ThrowFail";
                    if (!mixer.Has(skey) && e.type == EventType.RingClose) skey = "Countdown";
                    mixer.PlayEvent(skey, Feedback.Gain(e, ear), Feedback.Pan(e, ear),
                                    e.type == EventType.RoundEnd || e.type == EventType.MatchEnd);   // (the crowd's roar: under voices)
                }
                if (blitz.Event(e, L, playing)) { Buzz(0, 1f, 0.6f); Buzz(1, 1f, 0.6f); }   // (FIREBALL BLITZ: you're out, a long buzz)
                if (e.type == EventType.KnuckleBlock) AddPunchFx(shown, e, Feedback.PunchPower(e));   // (THE PUNCH LANDS)
                LogHitTrail(shown, e);
                if (playing && Feedback.Haptic(e, e.player == L, out int hand, out float amp, out float sec))
                {
                    if (hand < 0) { Buzz(0, amp, sec); Buzz(1, amp, sec); }
                    else Buzz(hand, amp, sec);
                }
            }
            if (playing && session.MeetHere && !net.LocalWaiting() && !app.spectating) MeetBuzz(shown, L);
            for (int h = 0; h < 2; ++h)
            {   // THE REACH ROUND THE FIST (native 2ee3617, reachCue): your ball in a hand's reach brightens, and that hand gets
                // a light tick as it comes in reach (two players' report, 2026-11-17: "hard to tell where the hitbox is")
                bool inR = playing && !app.spectating && L >= 0 && L < shown.np && shown.pl[L].@in.hand[h].trigger < 0.5f && shown.BallInReach(L, h);
                if (inR && !reachCue[h]) Buzz(h, 0.25f, 0.03f);
                reachCue[h] = inR;
            }
            if (playing && (shown.tun.ballFeel & Tunables.BALL_WEIGHT) != 0 && L >= 0 && L < shown.np && !app.viewing && !app.spectating)
            {   // BALL FEEL WEIGHT HEAVY (the room's): holding your ball, the controller hums as you swing it (from 1 m/s, stronger faster)
                Ball hb = shown.pl[L].ball;
                if (hb.state == BallState.Held && hb.heldHand >= 0 && hb.heldHand < 2)
                {
                    float amp = BallFeel.HumAmp(M.Length(shown.RecentHandVel(L, hb.heldHand, shown.HoldLocal(hb.heldHand), shown.now)));
                    if (amp > 0.04f) Buzz(hb.heldHand, amp, BallFeel.HumSeconds);
                }
            }
            {   // the last 10 seconds of a timed match or round: a very soft tick each second (native, owner 2026-10-27)
                int left = shown.match == MatchState.InProgress && shown.tun.Timed() && !shown.overtime && shown.roundClock > 0f && !app.InLobby
                    ? (int)System.Math.Ceiling(shown.roundClock) : 0;
                if (left >= 1 && left <= 10 && left != clockTick) mixer?.Play("oa_ClockTick", 0.6f, 0f);
                clockTick = left;
            }
            mfx.Update(shown, dt, app.sideRGB);
            CourtStretch.Apply(CourtStretch.MoveFor(shown));   // (FOUL LINE: the court drawn with the line where it is)
            for (int i = 0; i < punches.Count;)
            {   // (THE PUNCH LANDS: the flashes age, the squashes run out)
                var f = punches[i]; f.t += dt; punches[i] = f;
                if (f.t > 0.2f) { punches[i] = punches[punches.Count - 1]; punches.RemoveAt(punches.Count - 1); } else ++i;
            }
            for (int q = 0; q < 4; ++q) squashT[q] += dt;
            int bf = shown.tun.ballFeel;   // (BALL FEEL: the room's, the host's for everyone at its arena; native 3129dc2)
            feel.core = (bf & Tunables.BALL_CORE) != 0; feel.trueSize = (bf & Tunables.BALL_TRUE) != 0;
            feel.spin = (bf & Tunables.BALL_SPIN) != 0; feel.weight = (bf & Tunables.BALL_WEIGHT) != 0;
            feel.Step(shown, dt);   // (BALL FEEL (TEST): each ball's drawn turn, and where a held one is drawn)
            mfx.feel = feel;
            if (app.training != null && app.training.Active)
            {   // TRAINING: what just happened (native trainingFeedback's sounds and buzz)
                foreach (var snd in app.training.sounds)
                    if (mixer != null) mixer.PlayEvent(mixer.Has(snd.key) || snd.fallback == null ? snd.key : snd.fallback, snd.gain, snd.pan);
                if (playing && app.training.buzzHand >= 0) Buzz(app.training.buzzHand, 0.6f, 0.08f);
            }
            blitz.Update(shown, L, playing, dt);
            for (int p = 0; p < NP; ++p)
            {   // (native playerRGB: two players their end's colour; FOUR CORNERS four, or the teams')
                float[] pc = ExpArenaView.PlayerRGB(shown, app.sideRGB, p < shown.np ? p : p & 1);
                TeamCol[p] = new Vector3(pc[0], pc[1], pc[2]);
            }
            if (arenaLook != null)
            {   // NATIONS: the ends' colours; The Crowd: an end's lights flare with its fans' cheers
                arenaLook.sidesOn = app.nationsOn || app.crowdFlare[0] > 0.01f || app.crowdFlare[1] > 0.01f;
                float f0 = 1f + 0.9f * app.crowdFlare[0], f1 = 1f + 0.9f * app.crowdFlare[1];
                arenaLook.side0 = new Color(app.sideRGB[0][0] * f0, app.sideRGB[0][1] * f0, app.sideRGB[0][2] * f0);
                arenaLook.side1 = new Color(app.sideRGB[1][0] * f1, app.sideRGB[1][1] * f1, app.sideRGB[1][2] * f1);
            }
            UpdatePassthrough(dt);
            bool hub = app.spectating && app.watchIp == 0;
            bool mr = mrNow = pt.mrView && !hub;   // (the hub stays as it is: native drawHub)
            if (hub) deskLayer = 0;
            bool minimal = minimalNow = deskLayer >= 2, noCourt = noCourtNow = deskLayer >= 3, noStadium = mr || minimal;   // (the PC's VIEW LAYERS)
            bool outline = (mr && settings.ptMode == RoomPassthrough.Outline) || deskLayer >= 1, noFloorPt = (mr && settings.ptFloor <= 0.01f) || noCourt;
            bool exp = !hub && ExpArenaView.Experimental(shown);
            {   // GRAPHICS (TASKS.md E38): the frame-rate guard, what is in force, onto the picture
                ArenaGraphics.Guard(settings, desk != null, app.spectating, shown.match, Time.realtimeSinceStartupAsDouble, ArenaGraphics.Hz());
                ArenaGraphics.Decide(settings, desk != null, app.spectating);
                ArenaGraphics.Apply(settings);
                avatars.farOn = true;                 // FAR AWAY (TASKS.md E39): the eye the avatars' distance is from
                avatars.eye = app.viewHead.p;
                avatars.palmHold = shown.tun.PalmHoldNow();   // (the grapple's tether to the ball in the palm, TASKS.md E56)
                // THE CUTAWAY (TASKS.md E42, native desktopRender's dcCut): the PC window's main view from a preset camera
                // (1-9 but the players' eyes), watching a match, not KEY
                bool cut = desk != null && app.watchIp != 0 && !hub && desk.view >= 1 && desk.view < DesktopSpectator.ViewName.Length && desk.view != 6 && desk.view != 7 && deskLayer < 3;
                Camera mc0 = Camera.main;
                Cutaway.Apply(cut && mc0 != null, shown, mc0 != null ? XrInput.ToNative(mc0.transform.position) : new Vec3(0, 0, 0), expView.OctWallR);
                ArenaPost.Apply(Camera.main, ArenaGraphics.Bloom, ArenaGraphics.Vignette, ArenaGraphics.Ao, deskLayer >= 3);   // (BLOOM, VIGNETTE, AMBIENT OCCLUSION; KEY: not darkened)
            }
            foreach (var go in arenaParts)
            {
                bool want = !hub && !(exp && ClassicOnly.Contains(go.name)) && RoomPassthrough.Shows(go.name, noStadium, outline, noFloorPt, minimal, noCourt) &&
                            (ArenaGraphics.Reflect || !Mirrored.Contains(go.name));   // (FLOOR REFLECTIONS)
                if (go.activeSelf != want) go.SetActive(want);
            }
            foreach (var go in hubParts)
            {
                bool want = !(exp && ClassicOnly.Contains(go.name)) && RoomPassthrough.Shows(go.name, noStadium, outline, noFloorPt, minimal, noCourt);
                if (go.activeSelf != want) go.SetActive(want);
            }
            {   // the stadium: as it stands, or widened and lowered round CORNERS' room
                ExpArenaView.Outside(hub ? g : shown, out Vec3 at, out Vec3 sc);
                for (int k = 0; k < 2; ++k)
                    foreach (var go in k == 0 ? arenaParts : hubParts)
                        if (Stadium.Contains(go.name)) { go.transform.localPosition = NativeSpace.ToUnity(at); go.transform.localScale = new Vector3(sc.x, sc.y, sc.z); }
            }
            if (!exp) expView.Hide();
            if (!hub && !mr && !minimal) deck?.Draw(shown);   // THE DECK (TASKS.md E40): solid out to the stands (not over your room)
            {   // PASSTHROUGH: GLASS WALLS and COURT FLOOR (as drawn for your room; set when they change)
                float gm = mr ? settings.ptGlass : 1f, fl = RoomPassthrough.FloorFor(mr, settings.ptFloor);
                if (gm != glassNow) { glassNow = gm; RoomPassthrough.SetGlass(courtGlass, ptMpb, gm); }
                if (fl != floorNow) { floorNow = fl; RoomPassthrough.SetFloor(floorRs, ptMpb, fl); }
                // ARENA GLASS (TASKS.md E62, native f7b9cc9): the PC spectator's picture only (no passthrough there)
                float ag = desk != null && !mr ? settings.dcGlass : 1f;
                if (ag != arenaGlassNow) { arenaGlassNow = ag; DesktopSpectator.SetGlow(courtGlow, ptMpb, ag); if (ag == 1f) { glassNow = -1f; } }
            }
            if (hub)
            {   // the spectator hub: the dark arena, its floor, the live arenas in miniature
                for (int p = 0; p < NP; ++p)
                {
                    ball[p].gameObject.SetActive(false); shell[p].gameObject.SetActive(false); core[p].gameObject.SetActive(false); torso[p].gameObject.SetActive(false);
                    head[p].gameObject.SetActive(false); spawner[p].gameObject.SetActive(false); strikeZone[p].gameObject.SetActive(false);
                    for (int h = 0; h < 2; ++h) { shield[p, h].gameObject.SetActive(false); knuckle[p, h].gameObject.SetActive(false); }
                }
                if (fx != null)
                {
                    room.DrawHub(fx, avatars, aim, aimValid, menu, desktop: desk != null);
                    if (desk != null) DrawDesktopHud(g);
                    else menu.Draw(fx, aim, aimValid);
                    fx.Flush();
                    panels.Flush();
                }
                return;
            }
            if (desk != null && fx != null && avatars.PartCount > 0 && desk.CloseUps(shown) && app.watchIp != 0)
            {   // the VS screen / WIN SCREEN > SPLIT: each player alone, rendered before the frame's own drawing
                Camera mc = Camera.main;
                desk.RenderCloseUps(shown, session.viewIn, mc != null ? mc.pixelWidth : 1280, mc != null ? mc.pixelHeight : 720, Time.deltaTime, DrawAlone, session.PlayerLabel);
            }
            DrawMatch(shown, L, ear, aim, aimValid);
            if (humanPlayer && desk == null) RenderHeadSelfie();
            if (humanPlayer && desk == null && fx != null) director.RenderPreview(fxShader, textShader, fontTexture);
        }

        // PASSTHROUGH (task D1; native: ROOM AT EDGES and PEEK, updatePassthrough, mrView): how near the edge of your
        // play space you are, Y held, then what the room needs (PassthroughFeature) and whether this frame is drawn for it
        void UpdatePassthrough(float dt)
        {
            if (PassthroughFeature.Failed && settings.ptMode != RoomPassthrough.Off)
            {
                settings.ptMode = RoomPassthrough.Off;   // (native: a failure turns it off for this run, N/A on the page)
                Debug.LogWarning("[Passthrough] it failed: OFF for this run");
            }
            float head = 99f, hand = 99f;
            bool bounds = false;
            if (humanPlayer && xr != null && settings.ptEdgeReveal && settings.ptMode != RoomPassthrough.Off)
            {
                if (Time.time > boundaryAt) { boundaryAt = Time.time + 3f; ReadBoundary(); }
                bounds = boundary.Count >= 3;
                IReadOnlyList<Vector2> b = bounds ? boundary : null;
                if (myInput.headTracked) head = RoomPassthrough.EdgeDistance(b, new Vector2(xr.headTrack.p.x, xr.headTrack.p.z));
                for (int h = 0; h < 2; ++h)
                    if (myInput.hand[h].tracked) hand = Mathf.Min(hand, RoomPassthrough.EdgeDistance(b, new Vector2(xr.gripTrack[h].x, xr.gripTrack[h].z)));
            }
            bool peek = humanPlayer && xr != null && xr.peekHeld && !app.spectating;   // PEEK: its button held (Y as standard: BUTTONS)
            pt.Step(settings, PassthroughFeature.Ready, app.spectating, peek, head, hand, bounds, boundaryNarrow, dt, Time.timeAsDouble);
            PassthroughFeature.Want(pt.want);
            PassthroughFeature.Style(settings.ptRoom, settings.ptMono, settings.ptEdges);
            pt.Frame(settings, PassthroughFeature.Running, menu.open && menu.settingsOpen && menu.gfxPage);   // (GRAPHICS: the arena)
            Camera cam = Camera.main;
            if (cam != null)
            {   // nothing drawn = your room
                Color bg = pt.mrView ? new Color(0, 0, 0, 0) : Color.black;
                if (cam.backgroundColor != bg) cam.backgroundColor = bg;
            }
        }

        // the play space's boundary (Quest: the largest rectangle inside it), tracking space in native axes; without one
        // (or under 0.5 m), RoomPassthrough's 1.5 m circle
        void ReadBoundary()
        {
            boundary.Clear();
            boundaryNarrow = 0f;
            SubsystemManager.GetSubsystems(inputSubs);
            foreach (var s in inputSubs)
                if (s.running && s.TryGetBoundaryPoints(boundaryRaw) && boundaryRaw.Count >= 3)
                {
                    foreach (Vector3 v in boundaryRaw) boundary.Add(new Vector2(v.x, -v.z));
                    break;
                }
            if (boundary.Count >= 3) boundaryNarrow = RoomPassthrough.MinWidth(boundary);
            if (boundaryNarrow < 0.5f) boundary.Clear();
        }

        // the PC spectator's frame (native desktopFrame): always spectating, the camera from the window's controls, no
        // hands and no menus (the camera is the spectator)
        PlayerInput DesktopFrame(float dt)
        {
            {   // performance, every 10 s in the log (native 78380d4, as the headset's): the frame rate and the slowest frame
                double t = Time.realtimeSinceStartupAsDouble;
                if (perfSince == 0) perfSince = t; else perfWorst = System.Math.Max(perfWorst, t - perfLast);
                perfLast = t;
                ++perfFrames;
                if (t - perfSince >= 10.0)
                {
                    Debug.Log($"[Desktop] perf: {perfFrames / (t - perfSince):F1} fps, slowest frame {perfWorst * 1000.0:F1} ms (the spectator window)");
                    perfSince = t; perfFrames = 0; perfWorst = 0;
                }
            }
            if (net.CurrentMode != Online.Net.Mode.Spectate) { net.Spectate(); app.watchIp = 0; }
            Game shown = session.Shown ?? g;
            Pose eye = desk.Controls(net.Matches, shown, session.viewIn, session.viewInValid, Time.timeAsDouble, dt);
            deskLayer = app.watchIp != 0 ? desk.layer : 0;
            menu.open = false;
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(NativeSpace.ToUnity(eye.p), NativeSpace.ToUnity(eye.q));
                cam.fieldOfView = desk.fov;
                // KEY: nothing behind the players, see-through (OBS: Game Capture, Allow Transparency)
                Color bg = deskLayer >= 3 ? new Color(0, 0, 0, 0) : new Color(0.01f, 0.012f, 0.02f, 1);
                if (cam.backgroundColor != bg) cam.backgroundColor = bg;
            }
            app.viewHead = eye;
            var o = new PlayerInput { headTracked = true, head = eye };
            return o;
        }

        void DrawDesktopHud(Game shown)
        {
            Camera cam = Camera.main;
            if (cam == null || fx == null) return;
            float ty = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            string[][] lines = { Scoreboard.CareerLines(app.careersOk, app.careers[0]), Scoreboard.CareerLines(app.careersOk, app.careers[1]) };
            desk.DrawHud(fx, avatars, session, net, shown, app.viewHead, ty, cam.aspect, session.viewIn, session.bumpFlash, net.NoticeText, lines);
        }

        // the controls panel (F1; native: a small window beside the spectator): what is on screen, and buttons for the
        // same commands as the keys. (It shows in a capture of this window: F1 hides it.)
        Vector2 deskScroll;
        int deskTab;   // the controls panel's page: 0 SETUP, 1 AUTO (native 0.3.20), 2 GRAPHICS (TASKS.md E38)
        void OnGUI()
        {
            if (desk == null || !desk.panelOn) return;
            float w = 440f, x = Screen.width - w - 10f;
            GUILayout.BeginArea(new Rect(x, 10f, w, Screen.height - 20f), GUI.skin.box);
            deskScroll = GUILayout.BeginScrollView(deskScroll);
            GUILayout.Label("SPECTATOR CONTROLS (F1 hides this)");
            GUILayout.Label(desk.Status(net.Matches));
            void B(string label, DesktopSpectator.Cmd c, bool on = true) { GUI.enabled = on; if (GUILayout.Button(label)) desk.Press(c); GUI.enabled = true; }
            GUILayout.BeginHorizontal(); B("TAB  Next match", DesktopSpectator.Cmd.Next); B("H  Hub", DesktopSpectator.Cmd.Hub); GUILayout.EndHorizontal();
            deskTab = GUILayout.Toolbar(deskTab, new[] { "Setup", "Auto", "Graphics" });   // (native 0.3.20: AUTO ROTATION's own tab; 0.3.22 GRAPHICS)
            if (deskTab == 2)
            {   // GRAPHICS (TASKS.md E38, native dcGfxLabels): each switch says how it is now; saved on this PC
                GUILayout.Label("Graphics (this PC's picture only)");
                string[] ms = { "off", "2x", "4x" };
                bool changed = false;
                void G(string label, ref bool v) { if (GUILayout.Button(label + ": " + (v ? "on" : "off"))) { v = !v; changed = true; } }
                if (GUILayout.Button("Smooth edges: " + ms[settings.dcMsaa % 3])) { settings.dcMsaa = (settings.dcMsaa + 1) % 3; changed = true; }
                G("Contact shadows", ref settings.dcContact);
                G("Color grading", ref settings.dcGrade);
                G("Bloom", ref settings.dcBloom);
                G("Ambient occlusion", ref settings.dcAo);
                G("Vignette", ref settings.dcVignette);
                G("Floor reflections", ref settings.dcReflect);
                if (ArenaGraphics.guard) GUILayout.Label("Frames were dropping: bloom, smooth edges and ambient occlusion off until this match ends.");
                if (changed)
                {
                    settings.Save(app.RulesToSave);
                    Debug.Log($"[Desktop] graphics: smooth edges {settings.dcMsaa}, contact {settings.dcContact}, grade {settings.dcGrade}, bloom {settings.dcBloom}, " +
                              $"ao {settings.dcAo}, vignette {settings.dcVignette}, floor reflections {settings.dcReflect}");
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            if (deskTab == 1)
            {   // AUTO ROTATION (native 0.3.20): the presets and my cameras AUTO plays, ticked, in order
                GUILayout.Label("Auto rotation");
                GUILayout.Label("AUTO plays the ticked cameras in this order, each for its hold time. Action cameras aren't listed: they still cut in on hits near them.");
                GUILayout.BeginHorizontal(); B("A  Auto: " + (desk.auto ? "on" : "off"), DesktopSpectator.Cmd.Auto); GUILayout.EndHorizontal();
                for (int i = 0; i < desk.autoList.Count; ++i)
                {
                    AutoRotation.Item it = desk.autoList[i];
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Toggle(it.on, "", GUILayout.Width(22)) != it.on) desk.Press(DesktopSpectator.Cmd.AutoTick + i);
                    bool picked = desk.autoSel == i;
                    if (GUILayout.Toggle(picked, (picked ? "> " : "") + desk.AutoRowName(it), GUI.skin.button) && !picked) desk.Press(DesktopSpectator.Cmd.AutoPick + i);
                    GUILayout.Label(desk.AutoRowHold(it), GUILayout.Width(48));
                    GUILayout.EndHorizontal();
                }
                bool sel = desk.autoSel >= 0;
                GUILayout.BeginHorizontal(); B("Hold time", DesktopSpectator.Cmd.AutoHold, sel); B("Move up", DesktopSpectator.Cmd.AutoUp, sel && desk.autoSel > 0);
                B("Move down", DesktopSpectator.Cmd.AutoDown, sel && desk.autoSel < desk.autoList.Count - 1); GUILayout.EndHorizontal();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label("Cameras");
            for (int r = 0; r < 3; ++r)
            {   // (1-9: CORNER and COURTSIDE, TASKS.md E42)
                GUILayout.BeginHorizontal();
                for (int v = 1 + r * 3; v <= Mathf.Min(9, 3 + r * 3); ++v) B(v + " " + DesktopSpectator.ViewName[v], DesktopSpectator.Cmd.View + v);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();   // (THE FOLLOW VIEWS, TASKS.md E57: T, B, U)
            B("T Tracking", DesktopSpectator.Cmd.View + 10); B("B Ball cam", DesktopSpectator.Cmd.View + 11); B("U Under fire", DesktopSpectator.Cmd.View + 12);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); B("F  Free camera", DesktopSpectator.Cmd.Free); B("A  Auto: " + (desk.auto ? "on" : "off"), DesktopSpectator.Cmd.Auto); GUILayout.EndHorizontal();
            GUILayout.Label("My cameras (Ctrl + 1-8 saves, Shift + 1-8 shows)");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 8; ++i) B((i + 1).ToString(), DesktopSpectator.Cmd.MyGo + i, i < director.warps.Count);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); B("Save the view", DesktopSpectator.Cmd.MySave, director.warps.Count < 8 || desk.view == DesktopSpectator.Mine); B("Clear all", DesktopSpectator.Cmd.MyClear, director.warps.Count > 0); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); B("Hold time", DesktopSpectator.Cmd.MyHold); B("Fade / cut", DesktopSpectator.Cmd.MyTrans); B("When", DesktopSpectator.Cmd.MyTrig, desk.view == DesktopSpectator.Mine); GUILayout.EndHorizontal();
            GUILayout.Label("The picture");
            GUILayout.BeginHorizontal(); B("V  Layers: " + DesktopSpectator.LayerName[desk.layer], DesktopSpectator.Cmd.Layer); B("R  Win: " + DesktopSpectator.WinName[desk.winStyle], DesktopSpectator.Cmd.WinStyle); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); B("M  VS screen: " + (desk.matchupForce ? "now" : "at the fist bump"), DesktopSpectator.Cmd.Matchup); B("F2  Help bar", DesktopSpectator.Cmd.Help); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); B("F11  Full screen", DesktopSpectator.Cmd.Full); B("Arena glass: " + DesktopSpectator.GlassName(settings.dcGlass), DesktopSpectator.Cmd.Glass); GUILayout.EndHorizontal();
            GUILayout.Label("Selfie (your webcam, on this PC only)");
            GUILayout.BeginHorizontal(); B("C  Selfie: " + (settings.selfieOn ? "on" : "off"), DesktopSpectator.Cmd.Selfie); B("Corner", DesktopSpectator.Cmd.SelfieCorner); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); B("Size", DesktopSpectator.Cmd.SelfieSize); B("Round / square", DesktopSpectator.Cmd.SelfieShape); B("Next webcam", DesktopSpectator.Cmd.SelfieCam); GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // a player alone, for one camera on its own layer (the PC spectator's close-ups)
        void DrawAlone(int p, Camera c)
        {
            PlayerInput vin = session.viewIn[p];
            if (!vin.headTracked) return;
            var din = new PlayerInput();
            din.CopyFrom(vin);
            din = avatars.Apart(din, din.avatar);   // (HANDS APART)
            AvatarDraw.Look look = avatars.LookOf(din.avatar, p, app.sideRGB);
            avatars.camera = c;
            avatars.layer = DesktopSpectator.CloseLayer;
            bool farWas = avatars.farOn;
            avatars.farOn = false;   // (a close-up: whole, FAR AWAY being for the main view's eye)
            avatars.DrawBody(din, look, p);
            for (int h = 0; h < 2; ++h)
            {
                if (!din.hand[h].tracked) continue;
                Matrix4x4 grip = HandPlacement.FromPose(din.hand[h].grip);
                avatars.DrawGauntlet(din.avatar, h == 0 ? grip * HandPlacement.Mirror : grip, look, din.hand[h].trigger, h);
            }
            avatars.farOn = farWas;
            avatars.camera = null;
            avatars.layer = 0;
        }

        // SELFIE in the headset (native 0.3.13 headSelfieRender, spectators who stream): 30 times a second a camera 0.85 m
        // in front of your face, looking back at you, draws the arena behind you and your own avatar (no one else sees
        // it: it's for your cast) into a picture in the lower right of your view
        Camera hsCam;
        RenderTexture hsTex;
        double hsAt;
        PanelBatch hsPanels;
        void RenderHeadSelfie()
        {
            if (!settings.headSelfie || !app.spectating || app.watchIp == 0 || !myInput.headTracked) return;
            if (Time.timeAsDouble - hsAt < 1.0 / 30) return;
            hsAt = Time.timeAsDouble;
            if (hsCam == null)
            {
                hsTex = new RenderTexture(384, 384, 24) { name = "headset selfie" };
                var go = new GameObject("Headset selfie camera") { hideFlags = HideFlags.HideAndDontSave };
                hsCam = go.AddComponent<Camera>();
                hsCam.enabled = false;
                hsCam.stereoTargetEye = StereoTargetEyeMask.None;
                hsCam.targetTexture = hsTex;
                hsCam.clearFlags = CameraClearFlags.SolidColor;
                hsCam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);
                hsCam.nearClipPlane = 0.05f;
                hsCam.farClipPlane = 2000f;
                hsCam.fieldOfView = 56f;
                hsCam.aspect = 1f;
                Debug.Log("[Selfie] headset selfie camera made");
            }
            Pose head = myInput.head;
            Vec3 f = head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(0, 0, -1);
            Pose eye = DesktopSpectator.LookFrom(head.p + f * 0.85f + new Vec3(0, 0.08f, 0), head.p - new Vec3(0, 0.12f, 0));
            hsCam.transform.SetPositionAndRotation(NativeSpace.ToUnity(eye.p), NativeSpace.ToUnity(eye.q));
            if (avatars.PartCount > 0)
            {   // your own avatar, for this camera only (a spectator has no body in the game)
                var me = new PlayerInput();
                me.CopyFrom(myInput);
                me.avatar = settings.myAvatar;
                me = avatars.Apart(me, me.avatar);   // (HANDS APART)
                AvatarDraw.Look look = avatars.LookOf(me.avatar, 2, app.sideRGB);
                avatars.camera = hsCam;
                avatars.DrawBody(me, look, 2, swingKey: 2000);   // (its own rig state: THE REACH)
                for (int h = 0; h < 2; ++h)
                {
                    if (!me.hand[h].tracked) continue;
                    Matrix4x4 grip = HandPlacement.FromPose(me.hand[h].grip);
                    avatars.DrawGauntlet(me.avatar, h == 0 ? grip * HandPlacement.Mirror : grip, look, me.hand[h].trigger, h);
                }
                avatars.camera = null;
            }
            hsCam.Render();
        }

        void DrawHeadSelfie()
        {   // in the lower right, framed, a red light: live
            Pose h = myInput.head;
            Vec3 c = h.Transform(new Vec3(0.19f, -0.15f, -0.55f));
            const float sz = 0.15f, t = 0.004f;
            var edge = new Vector4(0.3f, 0.85f, 1.0f, 0.9f);
            hsPanels.Quad(hsTex, new Pose(h.q, c), sz, sz, new Vector4(1, 1, 1, 1));
            for (int k = 0; k < 4; ++k)
            {
                Vec3 o = k < 2 ? new Vec3(0, (k != 0 ? 0.5f : -0.5f) * sz, 0.0005f) : new Vec3((k == 3 ? 0.5f : -0.5f) * sz, 0, 0.0005f);
                hsPanels.Quad(Texture2D.whiteTexture, new Pose(h.q, c + h.q.Rotate(o)), k < 2 ? sz + t : t, k < 2 ? t : sz + t, edge);
            }
            hsPanels.Quad(Texture2D.whiteTexture, new Pose(h.q, c + h.q.Rotate(new Vec3(-0.5f * sz + 0.012f, 0.5f * sz - 0.012f, 0.001f))), 0.009f, 0.009f, new Vector4(1.0f, 0.25f, 0.2f, 1));
            hsPanels.Flush(Camera.main);   // (the headset's view only: not in the selfie's own picture)
        }

        void DrawMatch(Game shown, int L, Vec3 ear, Pose aim, bool aimValid)
        {
            float tt = Time.time - startTime;
            RoomInfo wr = app.watchRoom;
            bool anyoneWatched = wr.botMask != 0 || wr.members.Count > 0;
            for (int p = 0; p < NP; ++p) anyoneWatched |= wr.slot[p] != 0;
            bool oct = shown.Octagon;
            for (int p = 0; p < NP; ++p)
            {
                if (p >= shown.np)
                {   // (a place this match doesn't have: FOUR CORNERS' corners 3 and 4 in a match of two)
                    ball[p].gameObject.SetActive(false); shell[p].gameObject.SetActive(false); core[p].gameObject.SetActive(false); torso[p].gameObject.SetActive(false);
                    head[p].gameObject.SetActive(false); spawner[p].gameObject.SetActive(false); strikeZone[p].gameObject.SetActive(false);
                    for (int h = 0; h < 2; ++h) { shield[p, h].gameObject.SetActive(false); knuckle[p, h].gameObject.SetActive(false); }
                    continue;
                }
                PlayerState ps = shown.pl[p];
                Vector3 team = TeamCol[p];
                spawner[p].gameObject.SetActive(!(oct && !shown.Alive(p)) && !minimalNow);   // (FIREBALL BLITZ: out, no tee; the PC's MINIMAL: none)
                strikeZone[p].gameObject.SetActive(!oct && !minimalNow);                      // (FIREBALL BLITZ: no strikes)
                Place(spawner[p], shown.tun.spawner[p], Quat.Identity);   // (the room's rules place them)
                BallState sst = ps.ball.state;
                float sglow = (sst == BallState.Spawning || sst == BallState.Dead) ? 0.55f : 0.16f;
                Tint(spawner[p], Vector3.one, emissiveAdd: team * sglow);
                // the strike-zone ring, flaring when a strike is scored against this player
                // (it faces along its player's facing: corners 3 and 4 a quarter turn round)
                Place(strikeZone[p], shown.StrikeZoneCenter(p), p < 2 ? Quat.Identity : Quat.AxisAngle(new Vec3(0, 1, 0), M.kPi * 0.5f),
                      shown.tun.StrikeRadius() / StrikeRingModel);   // (STRIKE ZONE: the ring as big as it plays)
                Tint(strikeZone[p], team * ((0.35f + 1.5f * mfx.strikeFlash[p]) * (desk != null ? settings.dcGlass : 1f)));   // (ARENA GLASS: the PC's picture)
                bool filled = app.spectating || net.SlotFilled(p);
                if (app.spectating && app.watchIp != 0 && wr.slot[p] == 0 && !wr.IsBot(p) && anyoneWatched) filled = false;
                // the ball (main.cpp: projectile + shell)
                Ball b = ps.ball;
                bool show = filled && b.state != BallState.Dead && (b.state != BallState.Spawning || b.pos.y > 0f);
                ball[p].gameObject.SetActive(show);
                shell[p].gameObject.SetActive(show);
                core[p].gameObject.SetActive(show && feel.core);
                if (show)
                {
                    // uncharged ball: "your Ball has lost its charge"; on its way home after a foul, a short throw or
                    // getting stuck: harmless, so faded (native, owner 2026-10-28)
                    bool harmless = b.state == BallState.Returning && b.scored;
                    float glow = harmless ? 0.35f : b.charged ? 2.0f : 0.6f;
                    if (p == L && (reachCue[0] || reachCue[1])) glow *= 1.6f;   // (in reach: brighter)
                    float s = b.scale * (shown.tun.projectileRadius / 0.08f);   // visual sphere is 8 cm; collision radius 10 cm
                    if (b.state == BallState.Spawning)
                    {
                        float r = M.Clampf((b.pos.y - shown.tun.spawnLauncherHeight) / M.Max(0.05f, shown.IdlePoint(p).y - shown.tun.spawnLauncherHeight), 0f, 1f);
                        s *= 0.35f + 0.65f * M.Min(1f, r * 2.5f);
                    }
                    Vector3 ballMul = oct ? FireTint(team) : team * glow;   // (FIREBALL BLITZ: on fire on the tee as in flight)
                    // BALL FEEL (TEST): GLOW TRUE SIZE draws its outside (the shell, 12.5 cm) at the 10 cm it hits; WEIGHT HEAVY a
                    // held ball a little behind the hand; SPIN VISIBLE the whole ball turned as it tumbles and spins
                    s *= feel.Vis;
                    Vec3 bpos = feel.DrawnAt(p, b);
                    // (a bot's ball in its fist as drawn: its arm turns through a throw, its rules' hand doesn't, Game.HandPose;
                    // TASKS.md E41, native a812558)
                    if (b.state == BallState.Held && ps.isBot && b.heldHand >= 0 && b.heldHand < 2 && !(feel.weight && feel.Lagging(p)))
                        bpos = (session.viewInValid[p] ? session.viewIn[p] : ps.@in).hand[b.heldHand].grip.Transform(shown.HoldLocal(b.heldHand));
                    Quat turn = feel.Turn(p);
                    Place(ball[p], bpos, turn, s);
                    // (THE PUNCH LANDS: just punched, squashed along its new way and bulging round it, round again in 0.14 s)
                    Tint(ball[p], ballMul, new Vector2(tt * 0.21f + mfx.SpinAngle(p) * 0.05f, tt * 0.13f), squash: SquashOf(p, b.state, ball[p].rotation));   // energy veins drift
                    float sl = M.Length(b.spin);
                    Place(shell[p], bpos, feel.spin ? turn : sl > 1e-3f ? Quat.AxisAngle(b.spin / sl, mfx.SpinAngle(p)) : Quat.Identity, s);
                    Tint(shell[p], ballMul, squash: SquashOf(p, b.state, shell[p].rotation));
                    if (feel.core)
                    {   // (CORE SOLID: a lit core inside the plasma, 62% of what hits)
                        float cs = shown.BallRadius(p) * BallFeel.CoreShare / 0.08f;
                        Place(core[p], bpos, turn, cs);
                        BallFeel.CoreTint(team, b.charged, out Vector3 cMul, out Vector3 cEm);
                        Tint(core[p], cMul, emissiveAdd: cEm, squash: SquashOf(p, b.state, core[p].rotation));
                    }
                }
                torso[p].gameObject.SetActive(false);
                head[p].gameObject.SetActive(false);
                for (int h = 0; h < 2; ++h) { shield[p, h].gameObject.SetActive(false); knuckle[p, h].gameObject.SetActive(false); }
                if (!filled) continue;
                // the hands and the body from the eased view of the player (remote players between their updates)
                PlayerInput vin = session.viewIn[p];
                bool local = p == L && !app.viewing && humanPlayer;
                drawHeads[p] = vin.head;
                if (oct && shown.match != MatchState.WaitingForPlayers && ((shown.startMask >> p) & 1) == 0) continue;   // (FIREBALL BLITZ: not in this match)
                if (oct && !shown.Alive(p) && shown.match != MatchState.WaitingForPlayers)
                {   // (knocked out: a faint ghost where they watch from)
                    if (fx != null && !local && !ps.isBot) BlitzView.Ghost(fx, team, vin.head.p, app.viewHead.p);
                    continue;
                }
                drawIn[p].CopyFrom(vin);
                drawIn[p].avatar = local ? settings.myAvatar : vin.avatar;
                PlayerInput dv = avatars.Apart(drawIn[p], drawIn[p].avatar);   // (HANDS APART: the body's arms and the hands drawn just apart)
                AvatarDraw.Look look = avatars.LookOf(drawIn[p].avatar, p, app.sideRGB);
                // opponent body (the local player has no body, like the original first-person view); spectators see both
                if (!local && !(desk != null && desk.eyesOf == p) && vin.headTracked && vin.head.p.y > 0.3f)   // (the PC's eyes camera: that player's own body left out)
                {
                    if (avatars.PartCount > 0) avatars.DrawBody(dv, look, p, flash: mfx.hitFlash[p]);
                    else
                    {
                        torso[p].gameObject.SetActive(true);
                        head[p].gameObject.SetActive(true);
                        Pose hpose = vin.head;
                        Vec3 f = hpose.q.Rotate(new Vec3(0, 0, -1));
                        float yaw = Mathf.Atan2(-f.x, -f.z);
                        // skeletal meshes face UE +Y (= gltf +Z); turn to face the head's heading
                        Place(torso[p], new Vec3(hpose.p.x, hpose.p.y - 1.66f, hpose.p.z), Quat.AxisAngle(new Vec3(0, 1, 0), yaw + M.kPi));
                        Quat headQ = hpose.q * Quat.AxisAngle(new Vec3(0, 1, 0), M.kPi);
                        Place(head[p], hpose.p - headQ.Rotate(new Vec3(0, 1.66f, 0)), headQ);
                        var bodyTint = new Vector3(0.7f + 0.3f * team.x, 0.7f + 0.3f * team.y, 0.7f + 0.3f * team.z);
                        var flash = new Vector3(mfx.hitFlash[p], mfx.hitFlash[p] * 0.3f, 0);
                        Tint(torso[p], bodyTint, emissiveAdd: flash);
                        Tint(head[p], bodyTint, emissiveAdd: flash);
                    }
                }
                // the hands (main.cpp drawWorld): gauntlet, shield plate, knuckle glass; the left hand mirrored across grip X
                for (int h = 0; h < 2; ++h)
                {
                    HandInput hi = dv.hand[h];
                    HandState hs = ps.hands[h];
                    if (!hi.tracked) continue;
                    shield[p, h].gameObject.SetActive(hs.shieldUp);
                    knuckle[p, h].gameObject.SetActive(hs.knuckleUp);
                    Matrix4x4 grip = HandPlacement.FromPose(hi.grip);
                    Matrix4x4 gm = h == 0 ? grip * HandPlacement.Mirror : grip;
                    avatars.DrawGauntlet(drawIn[p].avatar, gm, look, hi.trigger, h, avatars.Distant(hi.grip.p));   // (FAR AWAY: another player's gauntlets)
                    if (hs.shieldUp)
                    {   // the shield plate, team-coloured
                        // (SHIELD SIZE: the plate grown in its own width and height, shield space x and z, as it plays)
                        float ss = shown.tun.ShieldScale();
                        HandPlacement.Apply(shield[p, h], grip * HandPlacement.FromPose(shown.ShieldLocal(h)) * Matrix4x4.Scale(new Vector3(ss, 1f, ss)) * HandPlacement.PlateLocal);
                        Tint(shield[p, h], team, alphaMul: 0.7f, emissiveAdd: team * 0.4f);
                    }
                    if (hs.knuckleUp)
                    {   // clear glass shell over the front of the fist, faint team tint
                        HandPlacement.Apply(knuckle[p, h], gm * HandPlacement.KnuckleShift(shown.tun) * HandPlacement.KnuckleLocal);
                        Tint(knuckle[p, h], new Vector3(0.75f + 0.25f * team.x, 0.75f + 0.25f * team.y, 0.75f + 0.25f * team.z),
                             alphaMul: 0.35f, emissiveAdd: team * 0.06f);
                    }
                }
            }
            for (int i = 0; i < NP * Game.KMaxFire; ++i)
            {   // FIREBALL BLITZ: the ball you threw, as it was on the tee, on fire: its plasma sphere and shell in fire colours
                int p = i / Game.KMaxFire;
                Ball f = p < shown.np && oct ? shown.fire[p][i % Game.KMaxFire] : null;
                bool on = f != null && f.state == BallState.Flying;
                fireBall[i].gameObject.SetActive(on);
                fireShell[i].gameObject.SetActive(on);
                int k = i % Game.KMaxFire;
                blitz.TrackEcho(p, k, on ? f : null, tt);
                for (int e = 1; e <= 4; ++e)
                {   // its echoes (native 0.3.19): smaller, fainter copies along the path it just took
                    int j = i * 4 + e - 1;
                    Vec3 at = default;
                    float fade = 0f, size = 0f;
                    bool show = on && blitz.Echo(p, k, e, f, shown.tun.projectileRadius * f.scale, tt, out at, out fade, out size);
                    echoBall[j].gameObject.SetActive(show);
                    echoShell[j].gameObject.SetActive(show);
                    if (!show) continue;
                    float es = f.scale * (shown.tun.projectileRadius / 0.08f) * size;
                    Vector3 emul = FireTint(TeamCol[p]) * fade;
                    var euv = new Vector2(tt * 0.6f + e * 0.13f, tt * 0.4f);
                    Place(echoBall[j], at, Quat.Identity, es);
                    Tint(echoBall[j], emul, euv, fade);
                    Place(echoShell[j], at, Quat.AxisAngle(new Vec3(0, 1, 0), tt * 9f + e), es);
                    Tint(echoShell[j], emul, euv, fade);
                }
                if (!on) continue;
                float s = f.scale * (shown.tun.projectileRadius / 0.08f);
                Vector3 mul = FireTint(TeamCol[p]);
                Place(fireBall[i], f.pos, Quat.Identity, s);
                Tint(fireBall[i], mul, new Vector2(tt * 0.6f, tt * 0.4f));   // (its veins racing: flames)
                Place(fireShell[i], f.pos, Quat.AxisAngle(new Vec3(0, 1, 0), tt * 9f), s);
                Tint(fireShell[i], mul);
            }
            if (fx == null) return;
            // the effects: trails, arcs, glows, floor reflections, spawn and hit bursts, confetti (seen from your eyes)
            bool exp = ExpArenaView.Experimental(shown), corners = ExpArenaView.CornersRoom(shown);
            mfx.noFloor = exp || (mrNow && settings.ptFloor <= 0.01f) || noCourtNow;   // (the glossy floor's reflections: the classic court's; PASSTHROUGH: none without a floor)
            float ptGlassMul = mrNow ? settings.ptGlass : 1f, ptFloorMul = mrNow ? Mathf.Min(1f, settings.ptFloor) : 1f;   // (PASSTHROUGH: the EXPERIMENTAL rooms' glass and floor too)
            if (!mrNow && !minimalNow) Deck.LightBar(fx, shown, ear);   // (the widened rooms' light bar on the deck)
            mfx.reflect = ArenaGraphics.Reflect;
            mfx.Draw(fx, shown, ear, app.viewing ? -1 : L, TeamCol);
            if (ArenaGraphics.Contact && !mrNow && !noCourtNow)   // CONTACT SHADOWS (TASKS.md E38; not over passthrough's room)
                ArenaGraphics.DrawContact(fx, shown, session.viewIn, session.viewInValid, app.viewing || app.spectating ? -1 : L, exp);
            foreach (var f in punches) DrawPunchFx(f);   // (THE PUNCH LANDS: the knuckle hit's flash)
            if (exp && !noCourtNow)
            {   // the EXPERIMENTAL rooms' walls, lines and floating platforms
                bool meeting = shown.match == MatchState.Meet || session.bumpFlash > 0;
                FxRenderer rf = expFx ?? fx;   // (THE CUTAWAY: their own batch)
                if (shown.arenaKind == Arena.Diamond) expView.DrawDiamond(rf, shown, app.sideRGB, app.viewHead.p, tt, meeting, ptGlassMul);
                else if (oct) expView.DrawOctagon(rf, shown, app.sideRGB, app.viewHead.p, tt, ptGlassMul, ptFloorMul);
                else expView.DrawCorners(rf, shown, app.sideRGB, app.viewHead.p, tt, meeting, leadFlash, ptGlassMul, ptFloorMul);
            }
            if (mrNow && settings.ptMode == RoomPassthrough.Window && !exp)   // WINDOW: a night sky over the court, so only the glass walls look out
                fx.Wall(new Vec3(0, 4.0f, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 1), 19.0f, 5.0f, RoomPassthrough.WindowSky);
            if (mrNow && app.InLobby)
            {   // PASSTHROUGH: the balcony is gone; a platform to stand on instead
                fx.SpriteFlat(Lobby.Spot + new Vec3(0, 0.004f, 0), 1.8f, new Vector4(0.03f, 0.08f, 0.12f, 1), new Vec3(0, 1, 0), 0);
                fx.SpriteFlat(Lobby.Spot + new Vec3(0, 0.006f, 0), 2.0f, new Vector4(0.15f, 0.5f, 0.75f, 1), new Vec3(0, 1, 0), 1);
            }
            if (oct)
            {   // FIREBALL BLITZ: the flames, the warning ring, the puffs, everyone's health; the card while you wait
                blitz.Draw(fx, shown, L, humanPlayer && !app.viewing, TeamCol, drawHeads, app.viewHead.p, tt);
                if (humanPlayer && !app.spectating && net.LocalWaiting())
                    BlitzView.DrawHelp(fx, shown, ExpArenaView.HoldingPose(shown, net.LocalSpot()), Time.timeAsDouble);
            }
            if (app.spectating && desk == null)
            {   // the floating gallery seat the spectator stands on (CORNERS: outside the room, by a side wall); in a headset, not in
                // the PC spectator's window, where its ring lay across the bottom of HIGH SIDE (native b5ac971, the owner, 2026-11-19)
                Vec3 st = ExpArenaView.SpectatorSeat(shown).p;
                fx.SpriteFlat(st + new Vec3(0, 0.004f, 0), 1.8f, new Vector4(0.03f, 0.08f, 0.12f, 1), new Vec3(0, 1, 0), 0);
                fx.SpriteFlat(st + new Vec3(0, 0.006f, 0), 2.0f, new Vector4(0.15f, 0.5f, 0.75f, 1), new Vec3(0, 1, 0), 1);
            }
            if (app.nationsOn && !exp && !mrNow && !minimalNow)   // (EXPERIMENTAL: the stadium is widened; no wall flags yet, as natively; PASSTHROUGH: your room)
            {   // NATIONS: each country's flag on the walls outside the tank: the end wall and one on each side wall on its half
                for (int p = 0; p < 2; ++p)
                {
                    if (app.sideNation[p] == 0) continue;
                    float sx = p == 0 ? -1f : 1f;
                    avatars.DrawFlag(app.sideNation[p], new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), M.Deg2Rad(-90f * sx)), new Vec3(13.45f * sx, 2.1f, 0)), 3.2f);
                    for (int sd = -1; sd <= 1; sd += 2)
                        avatars.DrawFlag(app.sideNation[p], new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), sd < 0 ? 0f : M.kPi), new Vec3(sx * 8f, 2.1f, 7.45f * sd)), 2.6f);
                }
            }
            int queued = 0;
            if (net.InRoom) foreach (var m in net.Room.members) queued += m.queuePos >= 0 ? 1 : 0;
            var info = new Scoreboard.Info
            {
                status = net.Status(), spectating = app.spectating, watching = app.viewing, notice = net.NoticeText,
                bumpFlash = session.bumpFlash, player = humanPlayer && !app.viewing && net.InRoom, queued = queued, sideName = session.SideName, ready = app.ReadyName,
                waiting = net.LocalWaiting(), leadFlash = leadFlash,
                octMsg = blitz.msg, octMsgP = blitz.msgP, octMsgT = blitz.msgT,
            };
            bool training = app.training != null && app.training.Active;
            if (training) { app.training.ready = app.ReadyName; app.training.Draw(fx, shown, app.viewHead.p, tt); }   // (TRAINING: its targets and its own board)
            else Scoreboard.Draw(fx, shown, L, TeamCol, info, app.viewHead.p);
            string[] names = { session.PlayerLabel(0), session.PlayerLabel(1) };
            string[][] lines = { Scoreboard.CareerLines(app.careersOk, app.careers[0]), Scoreboard.CareerLines(app.careersOk, app.careers[1]) };
            if (!corners && !oct && !mrNow && !minimalNow)   // (the screen over centre court: CORNERS' room and FIREBALL BLITZ's have their ceiling there; PASSTHROUGH: none)
                Scoreboard.Jumbotron(fx, shown, app.sideRGB, app.sideNation, names, lines, net.Crowd(app.watchIp), net.Online,
                                     (n, at, w) => avatars.DrawFlag(n, at, w));
            // The Crowd: flags waving in the stands, the cheers pouring out of them; in the EXPERIMENTAL arenas in their
            // widened, lowered stadium (native 0.3.17)
            ExpArenaView.Outside(shown, out Vec3 standsAt, out Vec3 standsScale);
            if (!mrNow && !minimalNow) stands.Draw(panels, net.Crowd(app.watchIp), settings.crowdMode, Time.time - startTime, app.sideNation, app.sideRGB, shown.tun.shields,
                        app.viewHead.p, standsScale.x, standsAt.y);
            if (desk != null) DrawDesktopHud(shown);   // (the PC spectator: its overlay, no hands, menus or director panel)
            else if (humanPlayer)
            {
                director.Draw(fx, panels, aim, aimValid);
                float dark = director.Fade(xr, Time.deltaTime);
                if (dark > 0f)
                {   // the cross-fade: through dark, a panel just in front of the eyes
                    Pose hd = myInput.head;
                    panels.Quad(Texture2D.whiteTexture, new Pose(hd.q, hd.Transform(new Vec3(0, 0, -0.12f))), 1.2f, 1.2f, new Vector4(0, 0, 0, Mathf.Min(0.998f, dark)));
                }
                room.DrawRoomPeople(fx, avatars, aim, aimValid, menu);
                voice.DrawHud(fx, menu.open);
                DrawReadyUp(fx);
                lobby.Draw(fx, aim, aimValid);
                menu.Draw(fx, aim, aimValid);
                builder.Draw(fx, avatars, myInput, app.sideRGB, aim, aimValid);
                if (app.spectating && settings.headSelfie && hsTex != null) DrawHeadSelfie();
                Builder.DrawShowcase(fx, avatars, app, menu, app.sideRGB, Time.timeAsDouble);
            }
            fx.Flush();
            expFx?.Flush();
            panels.Flush();
            if (desk != null) desk.closePanels.Flush(Camera.main);
            if (pt.fadeNow > 0f) RoomPassthrough.DrawFade(roomFade, Camera.main, pt.fadeNow);
            if (blinkT.Dark > 0f) RoomPassthrough.DrawFade(roomFade, Camera.main, blinkT.Dark);   // (a blink: plain dark over the view)   // ROOM AT EDGES / PEEK: the world toward your room, not the menus
            // your own hands on the balcony (the court draws the two bots'): the gauntlets in your look, the neutral rim
            if (humanPlayer && app.InLobby)
            {
                AvatarDraw.Look mineLook = avatars.LookOf(settings.myAvatar, 2, app.sideRGB);
                PlayerInput mineA = avatars.Apart(myInput, settings.myAvatar);   // (HANDS APART)
                for (int h = 0; h < 2; ++h)
                {
                    if (!mineA.hand[h].tracked) continue;
                    Matrix4x4 grip = HandPlacement.FromPose(mineA.hand[h].grip);
                    avatars.DrawGauntlet(settings.myAvatar, h == 0 ? grip * HandPlacement.Mirror : grip, mineLook, mineA.hand[h].trigger, h);
                }
            }
        }
    }
    // A player's move through dark (native 123205c startBlink; a player in the dugout, 2026-11-14: "the room moved with his
    // head"): the view darkens over Out, the move, it lightens over In. FIREBALL BLITZ's moves: to the dugout and back
    // (view key 4), and the room closing in (32 + its ring)
    public sealed class Blink
    {
        public const float Out = 0.12f, In = 0.30f;
        float outT, inT;
        public static bool Wanted(bool octagon, uint from, uint to) => octagon && (from == 4u || to == 4u || (from >= 32u && to >= 32u));
        public void Start() { outT = Out; inT = 0f; }
        // true on the frame it is dark: move then
        public bool Step(float dt)
        {
            inT = Mathf.Max(0f, inT - dt);
            if (outT > 0f && (outT -= dt) <= 0f) { outT = 0f; inT = In; return true; }
            return false;
        }
        public float Dark => outT > 0f ? 1f - outT / Out : inT / In;
    }
    // PROCESS PRIORITY (TASKS.md E62, native 78380d4; issue #7, a player: "High priority should be set automatically"):
    // playing in PC VR the game asks Windows for ABOVE NORMAL, so a busy browser, Discord or OBS doesn't take the CPU from
    // its frames. Not HIGH: that can starve the VR runtime's compositor. The spectator window stays NORMAL (OBS streams
    // it). --priority normal|above sets it either way. Windows builds only
    public static class ProcessPriority
    {
        public static string Arg()
        {
            string[] a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; ++i) if (a[i] == "--priority") return a[i + 1];
            return null;
        }
        public static void Set(string how)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            bool above = how == "above";
            bool ok = SetPriorityClass(GetCurrentProcess(), above ? 0x00008000u : 0x00000020u);   // ABOVE_NORMAL / NORMAL_PRIORITY_CLASS
            Debug.Log($"[App] process priority: {(above ? "above normal" : "normal")}{(ok ? "" : " (refused)")}");
#endif
        }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern System.IntPtr GetCurrentProcess();
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetPriorityClass(System.IntPtr process, uint priorityClass);
#endif
    }
}
