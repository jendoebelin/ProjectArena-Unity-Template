// Renders views of the Arena scene to PNG files (checking the scene without a headset):
//   Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.SceneShots.Run -logFile -
// Writes Logs/shot_*.png (not committed).
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectArena.EditorTools
{
    public static class SceneShots
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var b = new Bounds();
            var shaders = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                b.Encapsulate(r.bounds);
                foreach (var m in r.sharedMaterials)
                {
                    var key = m == null ? "(no material)" : m.shader == null ? "(no shader)" : m.shader.name +
                        (m.shader.isSupported ? "" : " UNSUPPORTED");
                    shaders[key] = shaders.TryGetValue(key, out var n) ? n + 1 : 1;
                }
            }
            Debug.Log($"[SceneShots] arena bounds centre {b.center}, size {b.size}");
            foreach (var kv in shaders) Debug.Log($"[SceneShots] shader {kv.Key}: {kv.Value}");

            Shot("head_pos_z", new Vector3(0, 1.6f, 0), Quaternion.LookRotation(Vector3.forward));
            Shot("head_neg_z", new Vector3(0, 1.6f, 0), Quaternion.LookRotation(Vector3.back));
            // the native docs' views (native repo docs/images): arena.jpg from beside the court, overview.jpg from high
            // behind the blue end (positions in native axes, converted)
            ShotAt("side", NativeSpace.ToUnity(0f, 2.2f, 7.5f), NativeSpace.ToUnity(0f, 1.6f, 0f));
            ShotAt("overview", NativeSpace.ToUnity(-15f, 7.5f, 0f), NativeSpace.ToUnity(4f, 0f, 0f));
            ShotAt("blue_end", NativeSpace.ToUnity(-7.5f, 1.6f, 0f), NativeSpace.ToUnity(0f, 1.4f, 0f));
            // looking up from the court: what hangs over it
            ShotAt("up_centre", NativeSpace.ToUnity(0f, 1.6f, 0f), NativeSpace.ToUnity(0.01f, 20f, 0f));
            ShotAt("up_from_end", NativeSpace.ToUnity(-7.5f, 1.6f, 0f), NativeSpace.ToUnity(0f, 9f, 0f));
            ShotAt("up_side", NativeSpace.ToUnity(-7.5f, 1.6f, 0f), NativeSpace.ToUnity(-7.5f, 9f, 8f));
        }

        // Your hands as you see them (HandPlacement, as MatchView draws them): both controllers held forward at waist
        // height at your end, the shield up on the right hand, the knuckle glass on both. Writes Logs/shot_hands.png.
        public static void Hands()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            const string models = "Assets/ProjectArena/Art/Open/Models/";
            GameObject Make(string n) => (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(models + n + ".mdl"));
            Sim.Quat face = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.Deg2Rad(-90));   // looking down the court (+X)
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_Rim", new Vector4(0.1f, 0.7f, 1.0f, 0.5f));
            mpb.SetFloat("_UseChannels", 1);
            mpb.SetVectorArray("_ChanCols", DefaultLook.Channels[0]);
            for (int h = 0; h < 2; ++h)
            {
                var grip = new Sim.Pose(face, g.tun.start[0] + new Sim.Vec3(0.35f, 1.0f, h == 0 ? -0.22f : 0.22f));
                Matrix4x4 gm = HandPlacement.FromPose(grip) * (h == 0 ? HandPlacement.Mirror : Matrix4x4.identity);
                var hand = Make("gauntlet_0_hand"); HandPlacement.Apply(hand.transform, gm * HandPlacement.FistLocal);
                var cuff = Make("gauntlet_0_cuff");
                HandPlacement.Apply(cuff.transform, gm * HandPlacement.FromPose(HandPlacement.CuffPose(false)) * HandPlacement.LancerLocal);
                foreach (var r in hand.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
                foreach (var r in cuff.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
                var kn = Make("knuckleguard"); HandPlacement.Apply(kn.transform, gm * HandPlacement.KnuckleShift(g.tun) * HandPlacement.KnuckleLocal);
                if (h == 1) { var sh = Make("shield"); HandPlacement.Apply(sh.transform, HandPlacement.FromPose(grip) * HandPlacement.FromPose(g.ShieldLocal(1)) * HandPlacement.PlateLocal); }
            }
            Vector3 eye = NativeSpace.ToUnity(g.tun.start[0] + new Sim.Vec3(0, 1.65f, 0));
            Shot("hands", eye, Quaternion.LookRotation(NativeSpace.ToUnity(g.tun.start[0] + new Sim.Vec3(0.5f, 1.0f, 0)) - eye), 90f);
            Shot("hands_side", NativeSpace.ToUnity(g.tun.start[0] + new Sim.Vec3(0.35f, 1.1f, 1.0f)),
                 Quaternion.LookRotation(new Vector3(0, -0.1f, 1)), 60f);
        }

        // The scoreboard as you see it from your end, mid-match (Logs/shot_board.png).
        public static void Board()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            g.match = Sim.MatchState.InProgress; g.roundClock = 161.4f; g.pl[0].score = 2; g.pl[1].score = 3; g.pl[0].strikesAgainst = 1;
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            Vector3[] team = { new Vector3(0.1f, 0.7f, 1.0f), new Vector3(1.0f, 0.4f, 0.1f) };
            Scoreboard.Draw(fx, g, 0, team, "PRACTICE VS BOT", false);
            fx.Text("ROUND 1   3", new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.Deg2Rad(-90)), new Sim.Vec3(-1.5f, 2.2f, 0)), 0.15f,
                    new Vector4(1, 1, 1, 1));   // (a banner, as during the countdown)
            var go = new GameObject("board");
            go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            Vector3 eye = NativeSpace.ToUnity(g.tun.start[0] + new Sim.Vec3(0, 1.65f, 0));
            Shot("board", eye, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(0, 2.6f, 0)) - eye), 60f);
        }

        // The SHOT CLOCK rings (native d51ab4e, its --opts clock): blue's building (4 of 10 s left), orange's run out
        // (full and red). Writes Logs/shot_clock_blue.png and Logs/shot_clock_orange.png.
        public static void ShotClock()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            g.match = Sim.MatchState.InProgress; g.pl[0].shotClock = 4f; g.pl[1].shotClock = 0f;
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            Vector3[] team = { new Vector3(0.1f, 0.7f, 1.0f), new Vector3(1.0f, 0.4f, 0.1f) };
            for (int p = 0; p < 2; ++p) MatchFx.DrawShotClock(fx, g, p, team[p]);
            var go = new GameObject("clock");
            go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            for (int p = 0; p < 2; ++p)
            {
                Sim.Vec3 s = g.tun.spawner[p], toward = g.Facing(p);
                Vector3 from = NativeSpace.ToUnity(s + toward * 1.4f + new Sim.Vec3(0, 1.3f, 0)), to = NativeSpace.ToUnity(s);
                Shot(p == 0 ? "clock_blue" : "clock_orange", from, Quaternion.LookRotation(to - from), 60f);
            }
        }

        // BALL MARKS (task C9; native --marks): bounces on the floor, both side walls and the far end, from just now
        // to fading out, in both ends' colours. Writes Logs/shot_marks.png.
        public static void BallMarks()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.SetArena(Sim.ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            var mfx = new MatchFx();
            // native --marks' bounces (the side walls' just inside the glass), oldest first: each then ages to its own age
            (Sim.Vec3 p, int who, float speed, float age)[] k =
            {
                (new Sim.Vec3(-6.0f, 1.1f, -2.40f), 0, 7f, 2.0f), (new Sim.Vec3(-5.4f, 0.14f, 0.3f), 0, 6f, 1.1f),
                (new Sim.Vec3(-4.4f, 0.9f, 2.40f), 1, 8f, 0.9f), (new Sim.Vec3(-3.2f, 1.6f, -2.40f), 1, 12f, 0.5f),
                (new Sim.Vec3(-3.6f, 0.14f, -0.8f), 1, 9f, 0.35f), (new Sim.Vec3(-9.45f, 1.5f, 0.6f), 1, 15f, 0.2f),
                (new Sim.Vec3(-2.6f, 1.3f, 2.40f), 0, 16f, 0.12f), (new Sim.Vec3(-2.0f, 0.14f, 0.6f), 0, 14f, 0.04f),
            };
            for (int i = 0; i < k.Length; ++i)
            {
                mfx.AddMark(g, k[i].p, k[i].who, k[i].speed, side);
                float next = i + 1 < k.Length ? k[i + 1].age : 0f;
                mfx.Update(g, k[i].age - next, side);   // (everything so far ages by the gap to the next one's age)
            }
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            Vector3[] team = { new Vector3(0.1f, 0.7f, 1.0f), new Vector3(1.0f, 0.4f, 0.1f) };
            mfx.Draw(fx, g, g.tun.start[1] + new Sim.Vec3(0, 1.65f, 0), 1, team);
            var go = new GameObject("marks");
            go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            Vector3 from = NativeSpace.ToUnity(new Sim.Vec3(0.5f, 1.65f, 0)), to = NativeSpace.ToUnity(new Sim.Vec3(-5f, 0.8f, 0));
            Shot("marks", from, Quaternion.LookRotation(to - from), 80f);
        }

        // TRAINING (task A6; native --scene trainplay2 / trainplay9): PICK YOUR SPOT 7 s into play (its targets, the
        // lesson's board) and SHIELD with the coach at the far end, from your place. Writes Logs/shot_train_*.png.
        public static void Training()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var tris = Sim.ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes"));
            var av = NewAvatars();
            GameObject mesh = null;
            foreach (int n in new[] { 2, 9 })
            {
                var g = new Sim.Game();
                g.SetArena(tris);
                var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")) };
                var tr = new TrainingApp(app) { classic = tris };
                tr.trainer.Begin(g, n - 1, tris);
                tr.trainer.Start(g);
                Sim.Quat face = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.Deg2Rad(-90f));
                var @in = new Sim.PlayerInput[4];
                for (int k = 0; k < 4; ++k) @in[k] = new Sim.PlayerInput();
                for (int i = 0; i < 72 * 7; ++i)
                {
                    @in[0] = new Sim.PlayerInput { headTracked = true, head = new Sim.Pose(face, g.tun.start[0] + new Sim.Vec3(0, 1.65f, 0)) };
                    for (int h = 0; h < 2; ++h)
                    {
                        @in[0].hand[h].tracked = true;
                        @in[0].hand[h].grip = new Sim.Pose(face, g.tun.start[0] + new Sim.Vec3(0.25f, 1.05f, h != 0 ? 0.28f : -0.28f));
                    }
                    tr.trainer.Step(g, 1f / 72, @in);
                }
                Debug.Log($"[Shots] TRAINING lesson {n}: {tr.trainer.phase}, {tr.trainer.successes} / {tr.trainer.attempts}, clock {tr.trainer.clock:F1}");
                foreach (Transform c in GameObject.Find("Arena").transform) c.gameObject.SetActive(true);
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
                Sim.Vec3 eye = g.tun.start[0] + new Sim.Vec3(0, 1.65f, 0);
                tr.Draw(fx, g, eye, 0f);
                if (mesh != null) Object.DestroyImmediate(mesh);
                mesh = new GameObject("training " + n);
                mesh.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                mesh.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Sim.PlayerInput coach = g.pl[1].@in.Clone();
                coach.avatar = Avatar.PresetLook(4);
                bool there = tr.trainer.CoachThere();
                Vector3 me = NativeSpace.ToUnity(eye), centre = NativeSpace.ToUnity(new Sim.Vec3(4f, 1.6f, 0));
                ShotAt("train_" + n, me, centre, cam => { av.camera = cam; if (there) DrawStanding(av, coach, 1, 91); }, 90f);
            }
            if (mesh != null) Object.DestroyImmediate(mesh);
        }

        // The EXPERIMENTAL rooms (task C3; native --scene exp / expcorners): DIAMOND, CORNERS and FOUR CORNERS (the
        // waiting decks), from a player's place and from outside. The classic court's models hidden and the stadium moved
        // as MatchView does. Writes Logs/shot_exp_*.png.
        public static void ExpRooms()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var root = GameObject.Find("Arena");
            var native = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/spawner.mdl")
                ?.GetComponentInChildren<Renderer>(true)?.sharedMaterial?.shader;
            var view = new ExpArenaView(native, null);
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            (string name, int room, int players)[] rooms = { ("diamond", Sim.Room.Diamond, 2), ("corners", Sim.Room.Corners, 2), ("fourcorners", Sim.Room.Corners, 4) };
            GameObject last = null;
            foreach (var r in rooms)
            {
                var g = new Sim.Game();
                g.tun.experimental = true; g.tun.expRoom = r.room; g.tun.players = r.players;
                g.UseArena(r.room == Sim.Room.Diamond ? Sim.Arena.Diamond : r.players == 4 ? Sim.Arena.FourCorners : Sim.Arena.Corners, new System.Collections.Generic.List<Sim.CollisionTri>());
                g.ResetWithBots(r.players == 4 ? 0xEu : 2u);
                g.match = Sim.MatchState.InProgress;
                ExpArenaView.Outside(g, out Sim.Vec3 at, out Sim.Vec3 sc);
                foreach (Transform c in root.transform)
                {
                    string n = c.gameObject.name;
                    c.gameObject.SetActive(n != "arena" && n != "floor" && n != "floor_reflect" && n != "floor_reflect_env" && n != "court_fx" && n != "court_pulse");
                    if (n == "environment" || n == "env_fx" || n == "stadium_fx") { c.localPosition = NativeSpace.ToUnity(at); c.localScale = new Vector3(sc.x, sc.y, sc.z); }
                }
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
                Sim.Vec3 eye0 = g.tun.start[0] + new Sim.Vec3(0, 1.65f, 0);
                if (r.room == Sim.Room.Diamond) view.DrawDiamond(fx, g, side, eye0, 0f, false);
                else view.DrawCorners(fx, g, side, eye0, 0f, false, 0f);
                if (last != null) Object.DestroyImmediate(last);
                last = new GameObject("exp " + r.name);
                last.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                last.AddComponent<MeshRenderer>().sharedMaterials = mats;
                // from player 0's place toward the court, and from high outside the room
                Vector3 e = NativeSpace.ToUnity(eye0), look = NativeSpace.ToUnity(new Sim.Vec3(0, 1.2f, 0));
                Shot("exp_" + r.name, e, Quaternion.LookRotation(look - e), 90f);
                Vector3 hi = NativeSpace.ToUnity(new Sim.Vec3(-13f, 7.5f, 9f));
                Shot("exp_" + r.name + "_out", hi, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(0, 0.5f, 0)) - hi), 70f);
                if (r.players != 4) continue;
                // a FOUR CORNERS match from player 0's place: the other three on their platforms in their colours, the
                // board turned toward your corner with the four scores (everyone for themselves)
                Object.DestroyImmediate(last);
                g.pl[0].score = 3; g.pl[1].score = 1; g.pl[2].score = 4; g.pl[3].score = 0;
                var fx2 = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                         AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                         AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
                view.DrawCorners(fx2, g, side, eye0, 0f, false, 0f);
                var tc = new Vector3[4];
                for (int p = 0; p < 4; ++p) { float[] pc = ExpArenaView.PlayerRGB(g, side, p); tc[p] = new Vector3(pc[0], pc[1], pc[2]); }
                for (int p = 0; p < 4; ++p) MatchFx.DrawShotClock(fx2, g, p, tc[p]);   // (none: two players only)
                Scoreboard.Draw(fx2, g, 0, tc, new Scoreboard.Info { status = "ONLINE HOST - FOUR CORNERS" }, eye0);
                last = new GameObject("exp fourcorners match");
                last.AddComponent<MeshFilter>().sharedMesh = fx2.Snapshot(out var mats2);
                last.AddComponent<MeshRenderer>().sharedMaterials = mats2;
                var av = NewAvatars();
                var who = new Sim.PlayerInput[4];
                for (int p = 1; p < 4; ++p)
                {
                    Sim.Vec3 f = g.Facing(p);
                    who[p] = Stand(g.tun.start[p], Mathf.Atan2(-f.x, -f.z), Avatar.PresetLook(p * 3));
                }
                void Match(Camera cam) { av.camera = cam; for (int p = 1; p < 4; ++p) DrawStanding(av, who[p], p, 70 + p); }
                Vector3 me = NativeSpace.ToUnity(eye0), centre = NativeSpace.ToUnity(new Sim.Vec3(0, 1.5f, 0));
                ShotAt("exp_fourcorners_match", me, centre, Match, 90f);
            }
        }

        // FIREBALL BLITZ (task A5; native --scene blitz / blitzwait): four ALL-STAR bots 7.5 s into a match (HEALTH 5,
        // ONE BALL, seed 11 as natively), from player 0's place and from high outside; and the card in front of a waiting
        // spot. Writes Logs/shot_blitz*.png. Blitz3v1 (native --scene blitz3v1, TASKS.md E51): 3 V 1 with bot 0 THE ONE
        // (shot_blitz3v1*.png: THE ONE in orange with its 15 squares, the three in blue)
        public static void Blitz() => BlitzShots("blitz", Sim.Teams.FFA);
        public static void Blitz3v1() => BlitzShots("blitz3v1", Sim.Teams.ThreeVOne);

        static void BlitzShots(string name, int teams)
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var root = GameObject.Find("Arena");
            var native = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/spawner.mdl")
                ?.GetComponentInChildren<Renderer>(true)?.sharedMaterial?.shader;
            var view = new ExpArenaView(native, null);
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            var g = new Sim.Game();
            g.tun.experimental = true; g.tun.expRoom = Sim.Room.Blitz; g.tun.players = 4; g.tun.meetTime = 0; g.tun.botLevel = 3;
            g.tun.health = 5; g.tun.fireBalls = 1; g.tun.basicMatchTime = 0;
            g.tun.teamMode = teams; g.tun.theOne = 0;
            g.UseArena(Sim.Arena.Octagon, new System.Collections.Generic.List<Sim.CollisionTri>());
            g.RngSeed(11);
            g.ResetWithBots(0xF);
            for (int i = 0; i < (int)(72 * 7.5f); ++i) g.Step(1f / 72, new Sim.PlayerInput[4]);
            Debug.Log($"[Shots] FIREBALL BLITZ after 7.5 s: health {g.pl[0].health} {g.pl[1].health} {g.pl[2].health} {g.pl[3].health}, fireballs {g.FireCount(0) + g.FireCount(1) + g.FireCount(2) + g.FireCount(3)}");
            ExpArenaView.Outside(g, out Sim.Vec3 at, out Sim.Vec3 sc);
            foreach (Transform c in root.transform)
            {
                string n = c.gameObject.name;
                c.gameObject.SetActive(n != "arena" && n != "floor" && n != "floor_reflect" && n != "floor_reflect_env" && n != "court_fx" && n != "court_pulse");
                if (n == "environment" || n == "env_fx" || n == "stadium_fx") { c.localPosition = NativeSpace.ToUnity(at); c.localScale = new Vector3(sc.x, sc.y, sc.z); }
            }
            var tc = new Vector3[4];
            for (int p = 0; p < 4; ++p) { float[] pc = ExpArenaView.PlayerRGB(g, side, p); tc[p] = new Vector3(pc[0], pc[1], pc[2]); }
            // the balls on fire (the tees' and the fireballs'), as MatchView tints them
            var made = new System.Collections.Generic.List<GameObject>();
            var mpb = new MaterialPropertyBlock();
            void FireBall(Sim.Vec3 pos, float scale, Vector3 team)
            {
                var mul = new Vector4(2f * (0.65f * BlitzView.Fire[0] + 0.35f * team.x), 2f * (0.65f * BlitzView.Fire[1] + 0.35f * team.y),
                                      2f * (0.65f * BlitzView.Fire[2] + 0.35f * team.z), 1);
                foreach (string m in new[] { "projectile.mdl", "projectile_shell.mdl" })
                {
                    var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/" + m));
                    float s = scale * (g.tun.projectileRadius / 0.08f);
                    go.transform.SetPositionAndRotation(NativeSpace.ToUnity(pos), Quaternion.identity);
                    go.transform.localScale = new Vector3(s, s, s);
                    mpb.SetVector("_TintMul", mul);
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
                    made.Add(go);
                }
            }
            for (int p = 0; p < 4; ++p)
            {
                if (g.pl[p].ball.state == Sim.BallState.Idle || g.pl[p].ball.state == Sim.BallState.Held) FireBall(g.pl[p].ball.pos, g.pl[p].ball.scale, tc[p]);
                foreach (Sim.Ball f in g.fire[p]) if (f.state == Sim.BallState.Flying) FireBall(f.pos, f.scale, tc[p]);
            }
            var av = NewAvatars();
            var who = new Sim.PlayerInput[4];
            var heads = new Sim.Pose[4];
            for (int p = 0; p < 4; ++p)
            {
                who[p] = g.pl[p].@in.Clone();
                who[p].avatar = Avatar.PresetLook((3 * p + 1) % Avatar.PresetCount());
                heads[p] = who[p].head;
            }
            Sim.Vec3 eye0 = g.pl[0].@in.head.p;
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            view.DrawOctagon(fx, g, side, eye0, 0f);
            new BlitzView().Draw(fx, g, 0, true, tc, heads, eye0, 0f);
            Scoreboard.Draw(fx, g, 0, tc, new Scoreboard.Info { status = "PRACTICE VS BOTS" }, eye0);
            var mesh = new GameObject("blitz");
            mesh.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
            mesh.AddComponent<MeshRenderer>().sharedMaterials = mats;
            void Players(Camera cam, bool all) { av.camera = cam; for (int p = all ? 0 : 1; p < 4; ++p) DrawStanding(av, who[p], p, 80 + p); }
            Vector3 me = NativeSpace.ToUnity(eye0), centre = NativeSpace.ToUnity(new Sim.Vec3(0, 1.0f, 0));
            ShotAt(name, me, centre, cam => Players(cam, false), 90f);
            Vector3 hi = NativeSpace.ToUnity(new Sim.Vec3(-9f, 6.5f, 9f));
            ShotAt(name + "_out", hi, NativeSpace.ToUnity(new Sim.Vec3(0, 0.3f, 0)), cam => Players(cam, true), 70f);
            // the card in front of a waiting spot (native --scene blitzwait)
            Object.DestroyImmediate(mesh);
            if (teams != Sim.Teams.FFA) { foreach (var go in made) Object.DestroyImmediate(go); return; }
            var fx2 = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                     AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                     AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            Sim.Pose spot = ExpArenaView.HoldingPose(g, 1);
            view.DrawOctagon(fx2, g, side, spot.p + new Sim.Vec3(0, 1.65f, 0), 0f);
            BlitzView.DrawHelp(fx2, g, spot, 0.5);
            mesh = new GameObject("blitz card");
            mesh.AddComponent<MeshFilter>().sharedMesh = fx2.Snapshot(out var mats2);
            mesh.AddComponent<MeshRenderer>().sharedMaterials = mats2;
            Vector3 eyeW = NativeSpace.ToUnity(spot.p + new Sim.Vec3(0, 1.65f, 0));
            Sim.Vec3 fw = spot.q.Rotate(new Sim.Vec3(0, 0, -1));
            ShotAt("blitz_card", eyeW, NativeSpace.ToUnity(spot.p + fw * 1.3f + new Sim.Vec3(0, 1.25f, 0)), cam => Players(cam, true), 80f);
            foreach (var go in made) Object.DestroyImmediate(go);
        }

        // The start: the lobby balcony with the title menu, MY BESTS and your avatar, as you see it (Logs/shot_lobby.png,
        // Logs/shot_lobby_left.png: turned toward MY BESTS on the left).
        public static void LobbyStart()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")), lobby = true };
            app.settings.Load(g.tun);
            var menu = new Menu(app) { emblem = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png") };
            var lobby = new Lobby(app, menu);
            var head = new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.65f, 0));
            app.viewHead = head;
            menu.Open(head, Lobby.Spot);
            menu.titleOpen = true;
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"),
                                    menu.emblem);
            Vector3[] team = { new Vector3(0.1f, 0.7f, 1.0f), new Vector3(1.0f, 0.4f, 0.1f) };
            Scoreboard.Draw(fx, g, 0, team, "PRACTICE VS BOT", true);
            var aim = new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), Sim.M.Deg2Rad(-12)), head.p + new Sim.Vec3(0.2f, -0.45f, -0.2f));
            lobby.Draw(fx, aim, true);
            menu.Draw(fx, aim, true);
            var go = new GameObject("lobby");
            go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            Vector3 eye = NativeSpace.ToUnity(head.p);
            Shot("lobby", eye, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(0, 3.6f, 3.6f)) - eye), 90f);
            Shot("lobby_left", eye, Quaternion.LookRotation(NativeSpace.ToUnity(menu.BestsPose().p) - eye), 90f);
        }

        // HOW TO PLAY and its CREDITS page (task C10), facing the panel: Logs/shot_help.png, Logs/shot_credits.png.
        // FOUL LINE (TASKS.md E3, native --foul): the court from high behind the blue end and from beside it, the line
        // LONGER 2, at regulation and SHORTER 2. Writes Logs/shot_foul_*.png.
        public static void FoulLine()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var root = GameObject.Find("Arena");
            int n = 0;
            foreach (Transform c in root.transform)
                if (System.Array.IndexOf(CourtStretch.Models, c.gameObject.name) >= 0) n += CourtStretch.Prepare(c.gameObject);
            Debug.Log($"[SceneShots] {n} court sections follow the FOUL LINE");
            foreach (var (name, mv) in new[] { ("longer2", 1f), ("regulation", 0f), ("shorter2", -1f) })
            {
                CourtStretch.Apply(mv);
                ShotAt("foul_" + name, NativeSpace.ToUnity(-15f, 7.5f, 0f), NativeSpace.ToUnity(4f, 0f, 0f));
                ShotAt("foul_" + name + "_side", NativeSpace.ToUnity(0f, 2.2f, 7.5f), NativeSpace.ToUnity(0f, 1.6f, 0f));
            }
            CourtStretch.Apply(0f);
        }

        // THE PUNCH LANDS (TASKS.md E17, native --punch T): blue's ball just punched toward orange, as hard as a punch can,
        // 0, 0.04 and 0.09 s after (the flash, and the ball squashed along its new way, round again in 0.14 s), from beside
        // and from behind. Writes Logs/shot_punch_*.png.
        public static void Punch()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            Sim.Vec3 at = g.tun.start[0] + new Sim.Vec3(0.55f, 1.15f, 0.12f), axis = new Sim.Vec3(1, 0, 0);
            float r = g.tun.projectileRadius / 0.08f;
            var mpb = new MaterialPropertyBlock();
            foreach (float t in new[] { 0f, 0.04f, 0.09f })
            {
                var made = new System.Collections.Generic.List<GameObject>();
                Sim.Vec3 pos = at + axis * (16f * t);   // (16 m/s on its new way; the flash stays where it met the knuckles)
                foreach (string m in new[] { "projectile.mdl", "projectile_shell.mdl" })
                {
                    var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/" + m));
                    Quaternion rot = m.StartsWith("projectile_shell") ? Quaternion.Euler(20f, 35f, 0f) : Quaternion.identity;
                    go.transform.SetPositionAndRotation(NativeSpace.ToUnity(pos), rot);
                    go.transform.localScale = new Vector3(r, r, r);
                    mpb.SetVector("_TintMul", new Vector4(0.1f * 2f, 0.7f * 2f, 1.0f * 2f, 1));   // (blue's, charged, as MatchView tints it)
                    mpb.SetVector("_Squash", MatchView.SquashFor(t, 1f, axis, rot));
                    foreach (var rr in go.GetComponentsInChildren<Renderer>()) rr.SetPropertyBlock(mpb);
                    made.Add(go);
                }
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
                Sim.Vec3 side = at + new Sim.Vec3(0.25f, 0.05f, 1.1f), behind = at + new Sim.Vec3(-0.9f, 0.35f, 0.25f);
                foreach (var (name, eye) in new[] { ("side", side), ("behind", behind) })
                {
                    MatchView.DrawPunchFlash(fx, at, t, 1f, 0, 0, eye);
                    var mesh = new GameObject("punch");
                    mesh.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                    mesh.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    ShotAt($"punch_{name}_{(int)(t * 100 + 0.5f):D2}", NativeSpace.ToUnity(eye), NativeSpace.ToUnity(at + axis * 0.25f), null, 50f);
                    Object.DestroyImmediate(mesh);
                }
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }

        // BALL FEEL (TEST) (TASKS.md E18, native --ballfeel): blue's charged ball four ways, side by side from your end, left
        // to right: as it was; CORE SOLID; CORE SOLID with GLOW TRUE SIZE; all of them with SPIN VISIBLE's turn. The plasma,
        // the shell and the lit core as MatchView draws them; the arcs and the inner glow as MatchFx does.
        // Writes Logs/shot_ballfeel.png.
        public static void BallFeelShot()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            var mpb = new MaterialPropertyBlock();
            var made = new System.Collections.Generic.List<GameObject>();
            var team = new Vector3(0.1f, 0.7f, 1.0f);
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            Sim.Vec3 eye = g.tun.start[0] + new Sim.Vec3(0.9f, 1.25f, 0f);
            var turned = Sim.Quat.AxisAngle(Sim.M.Normalize(new Sim.Vec3(0.3f, 0.2f, 1f)), 0.9f);   // (native's picture turn)
            GameObject Make(string m, Sim.Vec3 at, Sim.Quat q, float s, Vector4 mul, Vector4 em, bool lit)
            {
                var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/" + m));
                go.transform.SetPositionAndRotation(NativeSpace.ToUnity(at), NativeSpace.ToUnity(q));
                go.transform.localScale = new Vector3(s, s, s);
                mpb.Clear();
                mpb.SetVector("_TintMul", mul);
                mpb.SetVector("_EmissiveAdd", em);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (lit) r.sharedMaterials = BallFeel.LitCopy(r.sharedMaterials);
                    r.SetPropertyBlock(mpb);
                }
                made.Add(go);
                return go;
            }
            for (int k = 0; k < 4; ++k)
            {
                var f = new BallFeel { core = k >= 1, trueSize = k >= 2, spin = k >= 3 };
                if (k >= 3) f.SetTurn(0, turned);
                Sim.Vec3 at = eye + new Sim.Vec3(0.75f, -0.05f, (k - 1.5f) * 0.30f);
                float s = g.tun.projectileRadius / 0.08f * f.Vis;
                Sim.Quat q = f.Turn(0), shellQ = f.spin ? q : Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), 0.6f);
                var mul = new Vector4(team.x * 2f, team.y * 2f, team.z * 2f, 1);   // (charged: as MatchView tints it)
                Make("projectile.mdl", at, q, s, mul, Vector4.zero, false);
                Make("projectile_shell.mdl", at, shellQ, s, mul, Vector4.zero, false);
                if (f.core)
                {
                    BallFeel.CoreTint(team, true, out Vector3 cm, out Vector3 ce);
                    float cs = g.tun.projectileRadius * BallFeel.CoreShare / 0.08f;
                    Make("projectile.mdl", at, q, cs, new Vector4(cm.x, cm.y, cm.z, 1), new Vector4(ce.x, ce.y, ce.z, 0), true);
                }
                // (MatchFx's inner glow, as big as GLOW TRUE SIZE draws it)
                fx.Glow(at, g.tun.projectileRadius * 0.55f * f.Vis, new Vector4(team.x, team.y, team.z, 0.9f));
            }
            var mesh = new GameObject("glow");
            mesh.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
            mesh.AddComponent<MeshRenderer>().sharedMaterials = mats;
            ShotAt("ballfeel", NativeSpace.ToUnity(eye), NativeSpace.ToUnity(eye + new Sim.Vec3(0.75f, -0.05f, 0f)), null, 60f);
            Object.DestroyImmediate(mesh);
            foreach (var go in made) Object.DestroyImmediate(go);
        }

        // SETTINGS > CONTROLLERS > BUTTONS (TASKS.md E20 / E21, native --menu buttons, --buttons, --ctlkind): STANDARD on
        // Touch controllers, a map with TALK on Y on Index controllers (named as they are on them), and VOICE CHAT with its
        // TALK BUTTON. Writes Logs/shot_buttons.png, shot_buttons_index.png, shot_buttons_voice.png.
        // ROOMS AND THEIR OPTIONS (TASKS.md E22, native --menu rooms / roomsquare / roomdiamond / roundspadN): CHOOSE A ROOM
        // with its pictures, the SQUARE's options (ROUNDS, MAIDEN, AIM ASSIST HIGH, a LARGE ring, a quarter of the growth),
        // BEST OF's pad with 4 typed, DIAMOND's page; and the strike rings SMALL, REGULATION and LARGE from the far end.
        // Writes Logs/shot_rooms*.png, shot_zone_*.png.
        public static void RoomsShot()
        {
            foreach (var (name, step, room, pad) in new[] { ("rooms", 0, 0, ""), ("rooms_square", 1, 0, ""), ("rooms_pad", 1, 0, "4"), ("rooms_diamond", 1, 1, "") })
            {
                EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
                var g = new Sim.Game();
                g.Reset(true);
                var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")), lobby = true };
                app.settings.Load(g.tun);
                var menu = new Menu(app) { emblem = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png") };
                var head = new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.65f, 0));
                app.viewHead = head;
                menu.Open(head, Lobby.Spot);
                menu.rulesOpen = true;
                menu.rulesStep = step;
                if (step == 1)
                {   // (native's: ROUNDS BEST OF 5, MAIDEN, AIM ASSIST HIGH, a LARGE ring, a QUARTER of the growth)
                    menu.optExperimental = room == 1; menu.optExpRoom = Sim.Room.Diamond; menu.optAdvanced = true; menu.optRounds = 3;
                    menu.optMaiden = true; menu.optAim = 3; menu.optZone = 2; menu.optGrowth = 1; menu.optShieldSize = 2;
                    if (pad.Length > 0) { menu.roundsPad = true; menu.roundsBuf = pad; }
                }
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"),
                                        menu.emblem, AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/room_pictures.png"));
                var aim = new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), Sim.M.Deg2Rad(-12)), head.p + new Sim.Vec3(0.2f, -0.45f, -0.2f));
                menu.Draw(fx, aim, false);
                var go = new GameObject("menu");
                go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Vector3 eye = NativeSpace.ToUnity(head.p);
                Shot(name, eye, Quaternion.LookRotation(NativeSpace.ToUnity(menu.pose.p) - eye), 70f);
            }
            {   // the strike rings as big as they play (MatchView scales the ring by StrikeRadius / the model's 75 cm)
                EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
                var g = new Sim.Game();
                var ring = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/strikezone.mdl"));
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_TintMul", new Vector4(0.1f * 0.35f * 3f, 0.7f * 0.35f * 3f, 1.0f * 0.35f * 3f, 1));
                foreach (var r in ring.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
                foreach (var (zname, zone) in new[] { ("small", 0), ("regulation", 1), ("large", 2) })
                {
                    g.tun.strikeZone = zone;
                    float s = g.tun.StrikeRadius() / MatchView.StrikeRingModel;
                    ring.transform.SetPositionAndRotation(NativeSpace.ToUnity(g.StrikeZoneCenter(0)), Quaternion.identity);
                    ring.transform.localScale = new Vector3(s, s, s);
                    ShotAt("zone_" + zname, NativeSpace.ToUnity(g.tun.start[1] + new Sim.Vec3(0, 1.65f, 0)), NativeSpace.ToUnity(g.StrikeZoneCenter(0)), null, 40f);
                }
                Object.DestroyImmediate(ring);
            }
        }

        // GRAPHICS (TASKS.md E38): its page; the court with COLOR GRADING off and on; two players and a ball with their CONTACT
        // SHADOWS; FLOOR REFLECTIONS off. Writes Logs/shot_gfx_*.png
        public static void GraphicsShots()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.SetArena(Sim.ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")), lobby = true };
            app.settings.Load(g.tun);
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/" + n + ".png");
            FxRenderer NewFx() => new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                                 AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"), Tex("open_font"), Tex("open_emblem"));
            {   // the page, as on a headset with FOVEATION
                var menu = new Menu(app) { emblem = Tex("open_emblem"), foveationShown = true };
                var head = new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.65f, 0));
                app.viewHead = head;
                menu.Open(head, Lobby.Spot);
                menu.settingsOpen = true;
                menu.gfxPage = true;
                var fx = NewFx();
                var aim = new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), Sim.M.Deg2Rad(-12)), head.p + new Sim.Vec3(0.2f, -0.45f, -0.2f));
                menu.Draw(fx, aim, false);
                var go = new GameObject("menu");
                go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Vector3 eye = NativeSpace.ToUnity(head.p);
                Shot("gfx_page", eye, Quaternion.LookRotation(NativeSpace.ToUnity(menu.pose.p) - eye), 70f);
                Object.DestroyImmediate(go);
            }
            var av = NewAvatars();
            var a0 = Stand(new Sim.Vec3(-0.75f, 0, 0), Sim.M.Deg2Rad(-90), Avatar.PresetLook(1));   // (over the centre emblem: a shadow shows on its light)
            var a1 = Stand(new Sim.Vec3(0.75f, 0, 0), Sim.M.Deg2Rad(90), Avatar.PresetLook(2));
            g.pl[0].@in = a0; g.pl[1].@in = a1;
            g.pl[0].ball.state = Sim.BallState.Flying;
            g.pl[0].ball.pos = new Sim.Vec3(0, 1.2f, 0.4f);
            var fxw = NewFx();
            GameObject holder = null;
            bool contact = false, post = false;
            void Draw(Camera cam)
            {
                av.camera = cam;
                ArenaPost.Apply(cam, post, post, false, false);   // (BLOOM and VIGNETTE: URP's, on this camera)
                DrawStanding(av, a0, 0, 130);
                DrawStanding(av, a1, 1, 131);
                if (contact) ArenaGraphics.DrawContact(fxw, g, new[] { a0, a1 }, new[] { true, true }, -1, false);
                fxw.Glow(g.pl[0].ball.pos, 0.08f, new Vector4(0.2f, 0.7f, 1f, 1));
                if (holder != null) Object.DestroyImmediate(holder);
                holder = new GameObject("fx");
                holder.AddComponent<MeshFilter>().sharedMesh = fxw.Snapshot(out var mats);
                holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            Vector3 from = NativeSpace.ToUnity(0f, 2.6f, 2.6f), to = NativeSpace.ToUnity(0f, 0.5f, 0f);
            Shader.SetGlobalFloat("_PA_Grade", 0f);
            ShotAt("gfx_plain", from, to, Draw, 70f);
            Shader.SetGlobalFloat("_PA_Grade", 1f);
            ShotAt("gfx_grade", from, to, Draw, 70f);
            contact = true;
            ShotAt("gfx_contact", from, to, Draw, 70f);
            foreach (Transform c in GameObject.Find("Arena").transform)
                if (c.name == "floor_reflect" || c.name == "floor_reflect_env") c.gameObject.SetActive(false);
            ShotAt("gfx_noreflect", from, to, Draw, 70f);
            post = true;
            ShotAt("gfx_bloom", from, to, Draw, 70f);
            ShotAt("gfx_bloom_wide", NativeSpace.ToUnity(0f, 3.0f, 7.5f), NativeSpace.ToUnity(0f, 1.0f, 0f), Draw, 70f);
            post = false;
            ShotAt("gfx_plain_wide", NativeSpace.ToUnity(0f, 3.0f, 7.5f), NativeSpace.ToUnity(0f, 1.0f, 0f), Draw, 70f);
            {   // AMBIENT OCCLUSION (the PC spectator's): URP's SSAO feature on for this picture only, then off again
                var rd = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/ProjectArena/Settings/ArenaRenderer.asset");
                foreach (var f in rd.rendererFeatures) if (f is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion) f.SetActive(true);
                ShotAt("gfx_ao_wide", NativeSpace.ToUnity(0f, 3.0f, 7.5f), NativeSpace.ToUnity(0f, 1.0f, 0f), Draw, 70f);
                ShotAt("gfx_ao", from, to, Draw, 70f);
                foreach (var f in rd.rendererFeatures) if (f is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion) f.SetActive(false);
            }
            Shader.SetGlobalFloat("_PA_Grade", 0f);
            // THE DECK (TASKS.md E40): from high over the stands' edge, with it and without
            var deck = new Deck(Shader.Find("ProjectArena/Native"));
            ShotAt("deck_without", NativeSpace.ToUnity(-6f, 7f, 11f), NativeSpace.ToUnity(-4f, 0f, 2f), _ => { }, 70f);
            ShotAt("deck", NativeSpace.ToUnity(-6f, 7f, 11f), NativeSpace.ToUnity(-4f, 0f, 2f), cam => deck.Draw(g, cam), 70f);
        }

        // THE CUTAWAY (TASKS.md E42): the PC spectator's CORNER, SIDE and BLUE END presets in the SQUARE, with the walls they are
        // outside of left out, and CORNER without. Writes Logs/shot_cut_*.png
        public static void CutawayShots()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            foreach (var (name, from, to, cut) in new[] {
                ("cut_corner", new Sim.Vec3(-11.0f, 6.0f, 5.0f), new Sim.Vec3(1.0f, 0.0f, -1.2f), true),
                ("cut_corner_without", new Sim.Vec3(-11.0f, 6.0f, 5.0f), new Sim.Vec3(1.0f, 0.0f, -1.2f), false),
                ("cut_side", new Sim.Vec3(0, 2.7f, 7.3f), new Sim.Vec3(0, 1.0f, 0), true),
                ("cut_courtside", new Sim.Vec3(0, 1.25f, 7.2f), new Sim.Vec3(0, 1.0f, 0), true),
                ("cut_blue_end", new Sim.Vec3(-9.25f, 3.3f, 0), new Sim.Vec3(-0.5f, 0.3f, 0), true) })
            {
                Cutaway.Apply(cut, g, from, 7.5f);
                ShotAt(name, NativeSpace.ToUnity(from), NativeSpace.ToUnity(to), _ => { }, 66f);
            }
            Cutaway.Apply(false, g, new Sim.Vec3(0, 0, 0), 7.5f);
        }

        public static void ButtonsShot()
        {
            foreach (var (name, kind, map, voice) in new[] { ("buttons", Controls.K_TOUCH, "104542350", false), ("buttons_index", Controls.K_INDEX, "004541350", false),
                                                             ("buttons_voice", Controls.K_TOUCH, "004541350", true) })
            {
                EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
                var g = new Sim.Game();
                g.Reset(true);
                var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")), lobby = true };
                app.settings.Load(g.tun);
                Controls.Load(app.settings.buttons, map);
                app.ctlKind = kind;
                var menu = new Menu(app) { emblem = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png") };
                var head = new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.65f, 0));
                app.viewHead = head;
                menu.Open(head, Lobby.Spot);
                menu.settingsOpen = true;
                if (voice) menu.voicePage = true; else { menu.ctlPage = true; menu.btnPage = true; }
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"),
                                        menu.emblem);
                var aim = new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), Sim.M.Deg2Rad(-12)), head.p + new Sim.Vec3(0.2f, -0.45f, -0.2f));
                menu.Draw(fx, aim, false);
                var go = new GameObject("menu");
                go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Vector3 eye = NativeSpace.ToUnity(head.p);
                Shot(name, eye, Quaternion.LookRotation(NativeSpace.ToUnity(menu.pose.p) - eye), 70f);
            }
        }

        public static void Credits()
        {
            foreach (bool credits in new[] { false, true })
            {
                EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
                var g = new Sim.Game();
                g.Reset(true);
                var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")), lobby = true };
                app.settings.Load(g.tun);
                var menu = new Menu(app) { emblem = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png") };
                var head = new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.65f, 0));
                app.viewHead = head;
                menu.Open(head, Lobby.Spot);
                menu.titleOpen = true;
                menu.helpOpen = true;
                menu.creditsOpen = credits;
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"),
                                        menu.emblem);
                var aim = new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), Sim.M.Deg2Rad(-12)), head.p + new Sim.Vec3(0.2f, -0.45f, -0.2f));
                menu.Draw(fx, aim, false);
                var go = new GameObject("menu");
                go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Vector3 eye = NativeSpace.ToUnity(head.p);
                Shot(credits ? "credits" : "help", eye, Quaternion.LookRotation(NativeSpace.ToUnity(menu.pose.p) - eye), 70f);
            }
        }

        // A view from a pose logged by DebugPoseLog on the headset (Unity space):
        //   ... -executeMethod ProjectArena.EditorTools.SceneShots.RunPose -pose x y z qx qy qz qw -fov 100
        // Writes Logs/shot_pose.png (square, like the Quest's system screenshot).
        public static void RunPose()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-pose");
            int f = System.Array.IndexOf(args, "-fov");
            float A(int k) => float.Parse(args[k], System.Globalization.CultureInfo.InvariantCulture);
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            Shot("pose", new Vector3(A(i + 1), A(i + 2), A(i + 3)), new Quaternion(A(i + 4), A(i + 5), A(i + 6), A(i + 7)),
                f > 0 ? A(f + 1) : 100f, 1440, 1440);
        }

        static void ShotAt(string name, Vector3 from, Vector3 to)
        {
            Shot(name, from, Quaternion.LookRotation(to - from));
        }

        static AvatarDraw NewAvatars() =>
            new AvatarDraw(AssetDatabase.LoadAssetAtPath<AvatarLibrary>("Assets/ProjectArena/Settings/AvatarLibrary.asset"),
                           AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"));

        // a player standing at p facing yaw (radians; 0 = native -Z), arms relaxed, fists a little forward (as the native
        // title showcase)
        static Sim.PlayerInput Stand(Sim.Vec3 p, float yaw, Sim.AvatarDesc look)
        {
            var q = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), yaw);
            var @in = new Sim.PlayerInput { headTracked = true, head = new Sim.Pose(q, p + new Sim.Vec3(0, 1.62f, 0)), avatar = look };
            for (int h = 0; h < 2; ++h)
            {
                float s = h == 1 ? 1f : -1f;
                @in.hand[h].tracked = true;
                @in.hand[h].grip = new Sim.Pose(q * Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), -1.05f) * Sim.Quat.AxisAngle(new Sim.Vec3(0, 0, 1), s * 0.25f),
                                                @in.head.p + q.Rotate(new Sim.Vec3(s * 0.24f, -0.62f, -0.22f)));
            }
            return @in;
        }

        static void DrawStanding(AvatarDraw av, Sim.PlayerInput @in, int team, int key)
        {
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            var look = av.LookOf(@in.avatar, team, side);
            av.DrawBody(@in, look, team, swingKey: key);
            for (int h = 0; h < 2; ++h)
            {
                Matrix4x4 grip = HandPlacement.FromPose(@in.hand[h].grip);
                av.DrawGauntlet(@in.avatar, h == 0 ? grip * HandPlacement.Mirror : grip, look, @in.hand[h].trigger, h);
            }
        }

        // Avatars (AvatarDraw, phase 4 step C): the lobby's two bots on court, close up; every preset in a row; a country's
        // kit from the back (its flag); hair under headgear. Writes Logs/shot_av_*.png.
        public static void Avatars()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            var av = NewAvatars();
            Debug.Log($"[SceneShots] avatars: {av.PartCount} parts, {av.SwingRigCount} swing rigs, {av.TuckCount} tuck maps");
            // the lobby's bots at their ends, facing each other, seen from the side of the court
            var p0 = Stand(g.tun.start[0], Sim.M.Deg2Rad(-90), Avatar.PresetLook(10));
            var p1 = Stand(g.tun.start[1], Sim.M.Deg2Rad(90), Avatar.PresetLook(12));
            void Court(Camera c) { av.camera = c; DrawStanding(av, p0, 0, 0); DrawStanding(av, p1, 1, 1); }
            ShotAt("av_court", NativeSpace.ToUnity(0f, 2.2f, 7.5f), NativeSpace.ToUnity(0f, 1.2f, 0f), Court);
            Sim.Vec3 s0 = g.tun.start[0], s1 = g.tun.start[1];
            ShotAt("av_p0", NativeSpace.ToUnity(s0.x + 1.6f, 1.5f, s0.z + 0.6f), NativeSpace.ToUnity(s0.x, 1.1f, s0.z), Court, 60f);
            ShotAt("av_p1", NativeSpace.ToUnity(s1.x - 1.6f, 1.5f, s1.z - 0.6f), NativeSpace.ToUnity(s1.x, 1.1f, s1.z), Court, 60f);
            ShotAt("av_p1_back", NativeSpace.ToUnity(s1.x + 1.4f, 1.6f, s1.z + 0.5f), NativeSpace.ToUnity(s1.x, 1.2f, s1.z), Court, 60f);

            // every preset, in a row across the court facing +Z (toward the camera)
            int n = Avatar.PresetCount();
            var row = new Sim.PlayerInput[n];
            for (int i = 0; i < n; ++i) row[i] = Stand(new Sim.Vec3(-(n - 1) * 0.45f + i * 0.9f, 0, 0), Sim.M.kPi, Avatar.PresetLook(i));
            void Row(Camera c) { av.camera = c; for (int i = 0; i < n; ++i) DrawStanding(av, row[i], i % 2, 10 + i); }
            ShotAt("av_presets_a", NativeSpace.ToUnity(-3.6f, 1.3f, 3.2f), NativeSpace.ToUnity(-3.6f, 1.1f, 0f), Row, 70f);
            ShotAt("av_presets_b", NativeSpace.ToUnity(2.7f, 1.3f, 3.2f), NativeSpace.ToUnity(2.7f, 1.1f, 0f), Row, 70f);

            // countries, faces and headgear: USA and FRANCE kits (front and back), FEMININE faces 2-4, a ponytail under a helmet
            int usa = 0, fra = 0;
            for (int k = 1; k <= Avatar.NationCount(); ++k)
            {
                if (Avatar.NationAt(k).code == "USA") usa = k;
                if (Avatar.NationAt(k).code == "FRA") fra = k;
            }
            Sim.AvatarDesc Look(int preset, System.Action<Sim.AvatarDesc[]> change) { var a = new[] { Avatar.PresetLook(preset) }; change(a); return a[0]; }
            var looks = new[]
            {
                Look(1, a => Avatar.SetNation(ref a[0], usa)),
                Look(11, a => Avatar.SetNation(ref a[0], fra)),
                Look(11, a => Avatar.Set(ref a[0], Avatar.Field.Face, 1)),
                Look(13, a => Avatar.Set(ref a[0], Avatar.Field.Face, 2)),
                Look(4, a => { Avatar.Set(ref a[0], Avatar.Field.Body, 1); Avatar.Set(ref a[0], Avatar.Field.Face, 3); }),
                Look(3, a => { a[0].hair = 3; a[0].headgear = 2; }),    // PONYTAIL under the RACER HELMET
                Look(3, a => { a[0].hair = 7; a[0].headgear = 0; a[0].visor = 0; }),   // TOP KNOT
                Look(0, a => { a[0].hair = 6; a[0].headgear = 6; }),    // BOB under the CAP
            };
            var set = new Sim.PlayerInput[looks.Length];
            for (int i = 0; i < looks.Length; ++i) set[i] = Stand(new Sim.Vec3(-(looks.Length - 1) * 0.45f + i * 0.9f, 0, 0), Sim.M.kPi, looks[i]);
            void Set(Camera c) { av.camera = c; for (int i = 0; i < set.Length; ++i) DrawStanding(av, set[i], i % 2, 40 + i); }
            ShotAt("av_looks", NativeSpace.ToUnity(0f, 1.4f, 4.2f), NativeSpace.ToUnity(0f, 1.1f, 0f), Set, 70f);
            ShotAt("av_looks_back", NativeSpace.ToUnity(0f, 1.4f, -4.2f), NativeSpace.ToUnity(0f, 1.1f, 0f), Set, 70f);
            ShotAt("av_looks_heads", NativeSpace.ToUnity(-1.35f, 1.65f, 1.5f), NativeSpace.ToUnity(-1.35f, 1.5f, 0f), Set, 70f);
            ShotAt("av_looks_heads2", NativeSpace.ToUnity(1.35f, 1.65f, 1.5f), NativeSpace.ToUnity(1.35f, 1.5f, 0f), Set, 70f);
            ShotAt("av_looks_side", NativeSpace.ToUnity(2.9f, 1.65f, -1.2f), NativeSpace.ToUnity(1.35f, 1.5f, 0f), Set, 60f);
        }

        // NATIONS flags printed on the jersey (task C12, native 0.3.17): a MASCULINE USA kit and a FEMININE FRANCE
        // kit, close up from behind and in front, standing and leaning forward (the print bends with the back).
        // Writes Logs/shot_flag_back.png, _front, _lean.
        public static void JerseyFlags()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var av = NewAvatars();
            int usa = 0, fra = 0;
            for (int k = 1; k <= Avatar.NationCount(); ++k)
            {
                if (Avatar.NationAt(k).code == "USA") usa = k;
                if (Avatar.NationAt(k).code == "FRA") fra = k;
            }
            var a0 = Avatar.PresetLook(1); Avatar.SetNation(ref a0, usa); Avatar.Set(ref a0, Avatar.Field.Body, 0);
            var a1 = Avatar.PresetLook(11); Avatar.SetNation(ref a1, fra); Avatar.Set(ref a1, Avatar.Field.Body, 1);
            var p = new[] { Stand(new Sim.Vec3(-0.45f, 0, 0), Sim.M.kPi, a0), Stand(new Sim.Vec3(0.45f, 0, 0), Sim.M.kPi, a1) };
            void Two(Camera c) { av.camera = c; for (int i = 0; i < 2; ++i) DrawStanding(av, p[i], i, 80 + i); }
            ShotAt("flag_back", NativeSpace.ToUnity(0f, 1.45f, -1.6f), NativeSpace.ToUnity(0f, 1.25f, 0f), Two, 50f);
            ShotAt("flag_front", NativeSpace.ToUnity(0f, 1.45f, 1.6f), NativeSpace.ToUnity(0f, 1.25f, 0f), Two, 50f);
            for (int i = 0; i < 2; ++i) p[i].head.p = p[i].head.p + new Sim.Vec3(0, -0.18f, 0.30f);   // (leaning forward, toward +Z)
            ShotAt("flag_lean", NativeSpace.ToUnity(0.6f, 1.7f, -1.5f), NativeSpace.ToUnity(0f, 1.15f, 0.1f), Two, 55f);
        }

        // The collarbones (native c758fd3, its --scene tuning --armsup DEG): MASCULINE and FEMININE bodies with straight
        // arms swung up from forward to overhead (0, 60, 90 degrees), the controllers along them; from the front and from
        // behind. Writes Logs/shot_av_armsup*.png.
        // THE REACH (TASKS.md E10, native --handstouch 7): issue #24's far reach, the right hand far out to the side, the left
        // at ready, both bodies: the torso turned and leant toward it, bending at the waist, the hips upright. Writes
        // Logs/shot_av_reach.png (from the front) and _side.
        public static void FarReach()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var av = NewAvatars();
            var row = new Sim.PlayerInput[2];
            for (int i = 0; i < 2; ++i)
            {
                var look = new[] { Avatar.PresetLook(1) };
                Avatar.Set(ref look[0], Avatar.Field.Body, i);
                look[0].lower = 2;   // (shorts and legs: the hips show)
                var at = new Sim.Vec3(-0.9f + i * 1.6f, 0, 0);
                var @in = Stand(at, Sim.M.kPi, look[0]);
                Sim.Quat face = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.kPi);
                (Sim.Vec3 p, float pitch, float yaw)[] hp = { (new Sim.Vec3(-0.21f, 1.08f, 0.30f), -22, 8), (new Sim.Vec3(0.80f, 1.10f, 0.35f), 0, -60) };
                for (int h = 0; h < 2; ++h)
                {
                    Sim.Quat q = face * Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.Deg2Rad(hp[h].yaw)) * Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), Sim.M.Deg2Rad(hp[h].pitch));
                    @in.hand[h].tracked = true;
                    @in.hand[h].grip = new Sim.Pose(q, at + face.Rotate(new Sim.Vec3(hp[h].p.x, 0, -hp[h].p.z)) + new Sim.Vec3(0, hp[h].p.y, 0));
                }
                row[i] = @in;
            }
            void Row(Camera c) { av.camera = c; for (int i = 0; i < row.Length; ++i) DrawStanding(av, row[i], i % 2, 80 + i); }
            ShotAt("av_reach", NativeSpace.ToUnity(0f, 1.3f, 3.2f), NativeSpace.ToUnity(0f, 1.1f, 0f), Row, 70f);
            ShotAt("av_reach_side", NativeSpace.ToUnity(4.0f, 1.3f, 0.6f), NativeSpace.ToUnity(0f, 1.1f, 0f), Row, 60f);
            // FOREARM TWIST (TASKS.md E11, native --handstouch 9 / 10): the arms out in front, the palms turned up, then down;
            // with the twist and without (av.noTwist), the forearms close up
            foreach (var (name, roll) in new[] { ("up", 110f), ("down", -110f) })
            {
                for (int i = 0; i < 2; ++i)
                {
                    var look = new[] { Avatar.PresetLook(1) };
                    Avatar.Set(ref look[0], Avatar.Field.Body, i);
                    var at = new Sim.Vec3(-0.9f + i * 1.6f, 0, 0);
                    var @in = Stand(at, Sim.M.kPi, look[0]);
                    Sim.Quat face = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.kPi);
                    for (int h = 0; h < 2; ++h)
                    {
                        float s = h == 1 ? 1f : -1f;
                        Sim.Quat q = face * Sim.Quat.AxisAngle(new Sim.Vec3(0, 0, 1), Sim.M.Deg2Rad(-s * roll));
                        @in.hand[h].tracked = true;
                        @in.hand[h].grip = new Sim.Pose(q, at + face.Rotate(new Sim.Vec3(s * 0.22f, 0, -0.45f)) + new Sim.Vec3(0, 1.22f, 0));
                    }
                    row[i] = @in;
                }
                ShotAt("av_twist_" + name, NativeSpace.ToUnity(0f, 1.4f, 1.6f), NativeSpace.ToUnity(0f, 1.2f, 0f), Row, 60f);
                av.noTwist = true;
                ShotAt("av_twist_" + name + "_off", NativeSpace.ToUnity(0f, 1.4f, 1.6f), NativeSpace.ToUnity(0f, 1.2f, 0f), Row, 60f);
                av.noTwist = false;
            }
        }

        // NEW CLOTHING (TASKS.md E12, native e21f83e: PR #25's): LONG SHORTS, the HORNED HELMET and the VECTOR HELMET on
        // both bodies, with hair under them (its tuck maps). Writes Logs/shot_av_clothing.png and _heads.
        public static void Clothing()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var av = NewAvatars();
            var row = new Sim.PlayerInput[4];
            for (int i = 0; i < 4; ++i)
            {
                var look = new[] { Avatar.PresetLook(1) };
                Avatar.Set(ref look[0], Avatar.Field.Body, i % 2);
                Avatar.Set(ref look[0], Avatar.Field.Lower, 4);
                Avatar.Set(ref look[0], Avatar.Field.Headgear, i < 2 ? 7 : 8);
                Avatar.Set(ref look[0], Avatar.Field.Hair, 3);
                row[i] = Stand(new Sim.Vec3(-1.35f + i * 0.9f, 0, 0), Sim.M.kPi, look[0]);
            }
            void Row(Camera c) { av.camera = c; for (int i = 0; i < row.Length; ++i) DrawStanding(av, row[i], i % 2, 90 + i); }
            ShotAt("av_clothing", NativeSpace.ToUnity(0f, 1.1f, 3.6f), NativeSpace.ToUnity(0f, 1.0f, 0f), Row, 60f);
            ShotAt("av_clothing_heads", NativeSpace.ToUnity(0f, 1.7f, 1.6f), NativeSpace.ToUnity(0f, 1.6f, 0f), Row, 60f);
            // THE SKIRTS (TASKS.md E13, native --swish X Z TWIST): the TENNIS SKIRT and the KILT on both bodies, at rest and
            // swung (a picture has no motion: every skirt swung the same)
            for (int i = 0; i < 4; ++i)
            {
                var look = new[] { Avatar.PresetLook(1) };
                Avatar.Set(ref look[0], Avatar.Field.Body, i % 2);
                Avatar.Set(ref look[0], Avatar.Field.Lower, i < 2 ? 5 : 6);
                row[i] = Stand(new Sim.Vec3(-1.35f + i * 0.9f, 0, 0), Sim.M.kPi, look[0]);
            }
            ShotAt("av_skirts", NativeSpace.ToUnity(0f, 1.0f, 3.2f), NativeSpace.ToUnity(0f, 0.8f, 0f), Row, 60f);
            av.shotSwish = new[] { 0.05f, 0.03f, 0.15f };
            ShotAt("av_skirts_swung", NativeSpace.ToUnity(0f, 1.0f, 3.2f), NativeSpace.ToUnity(0f, 0.8f, 0f), Row, 60f);
            ShotAt("av_skirts_swung_side", NativeSpace.ToUnity(3.2f, 1.0f, 0.4f), NativeSpace.ToUnity(0f, 0.8f, 0f), Row, 60f);
            av.shotSwish = null;
        }

        // THE COMMUNITY'S OUTFITS (TASKS.md E35): RONIN WRAP and RONIN KABUTO, HIGH PIGTAILS, DREADLOCKS, the BANDANA, and the
        // grapple gauntlets (GRAPPLER, ARC CLAW, REGALIA), the right hand's trigger pulled (open, the tether out), the left's
        // not. Writes Logs/shot_av_outfits*.png
        public static void Outfits()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var av = NewAvatars();
            var row = new Sim.PlayerInput[4];
            int[,] set = { { 16, 9, 0, 4, 0 }, { 1, 0, 9, 5, 1 }, { 3, 2, 10, 6, 0 }, { 16, 9, 9, 4, 1 } };   // suit, headgear, hair, gauntlet, body
            for (int i = 0; i < 4; ++i)
            {
                var look = new[] { Avatar.PresetLook(1) };
                Avatar.Set(ref look[0], Avatar.Field.Suit, set[i, 0]);
                Avatar.Set(ref look[0], Avatar.Field.Headgear, set[i, 1]);
                Avatar.Set(ref look[0], Avatar.Field.Hair, set[i, 2]);
                Avatar.Set(ref look[0], Avatar.Field.Gauntlet, set[i, 3]);
                Avatar.Set(ref look[0], Avatar.Field.Body, set[i, 4]);
                Avatar.Set(ref look[0], Avatar.Field.Mask, i == 0 ? 4 : 0);
                Avatar.Set(ref look[0], Avatar.Field.Visor, 0);
                var p = Stand(new Sim.Vec3(-1.35f + i * 0.9f, 0, 0), Sim.M.kPi, look[0]);
                var q = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Sim.M.kPi);
                for (int h = 0; h < 2; ++h)
                {   // the hands forward at the chest, fists level
                    float s = h == 1 ? 1f : -1f;
                    p.hand[h].grip = new Sim.Pose(q * Sim.Quat.AxisAngle(new Sim.Vec3(0, 0, 1), s * 0.3f), p.head.p + q.Rotate(new Sim.Vec3(s * 0.20f, -0.40f, -0.40f)));
                    p.hand[h].trigger = h == 1 ? 1f : 0f;
                }
                row[i] = p;
            }
            void Row(Camera c) { av.camera = c; for (int i = 0; i < row.Length; ++i) DrawStanding(av, row[i], i % 2, 110 + i); }
            ShotAt("av_outfits", NativeSpace.ToUnity(0f, 1.2f, 3.4f), NativeSpace.ToUnity(0f, 1.1f, 0f), Row, 60f);
            ShotAt("av_outfits_back", NativeSpace.ToUnity(0f, 1.4f, -2.8f), NativeSpace.ToUnity(0f, 1.3f, 0f), Row, 60f);
            ShotAt("av_outfits_hands", NativeSpace.ToUnity(-2.3f, 1.35f, 1.1f), NativeSpace.ToUnity(-1.35f, 1.2f, 0.4f), Row, 50f);
            ShotAt("av_outfits_hands2", NativeSpace.ToUnity(0.4f, 1.35f, 1.1f), NativeSpace.ToUnity(0.45f, 1.2f, 0.4f), Row, 50f);
            // the five full sets (presets 14-18: RONIN, PELURU TUXEDO, PELURU DRESS, VECTOR, NIGHTFORGE), front and back
            var sets = new Sim.PlayerInput[5];
            for (int i = 0; i < 5; ++i) sets[i] = Stand(new Sim.Vec3(-1.8f + i * 0.9f, 0, 0), Sim.M.kPi, Avatar.PresetLook(14 + i));
            void Sets(Camera c) { av.camera = c; for (int i = 0; i < sets.Length; ++i) DrawStanding(av, sets[i], i % 2, 120 + i); }
            ShotAt("av_sets", NativeSpace.ToUnity(0f, 1.1f, 4.0f), NativeSpace.ToUnity(0f, 1.0f, 0f), Sets, 60f);
            ShotAt("av_sets_back", NativeSpace.ToUnity(0f, 1.3f, -3.6f), NativeSpace.ToUnity(0f, 1.1f, 0f), Sets, 60f);
            ShotAt("av_sets_peluru", NativeSpace.ToUnity(-0.45f, 1.45f, 1.7f), NativeSpace.ToUnity(-0.45f, 1.35f, 0f), Sets, 50f);
            // FAR AWAY (TASKS.md E39): the same up close, drawn with their lighter triangles (as from beyond 3 m)
            av.farOn = true; av.eye = new Sim.Vec3(0, 1.6f, 40f);
            ShotAt("av_sets_peluru_far", NativeSpace.ToUnity(-0.45f, 1.45f, 1.7f), NativeSpace.ToUnity(-0.45f, 1.35f, 0f), Sets, 50f);
            av.farOn = false;
            // COUNTRY KIT = MY OUTFIT on the five (MEXICO): the prints in their places, the dress's along its flounce (native 37979ef)
            int mex = 0;
            for (int k = 1; k <= Avatar.NationCount(); ++k) if (Avatar.NationAt(k).code == "MEX") mex = k;
            for (int i = 0; i < 5; ++i) { Avatar.SetNation(ref sets[i].avatar, mex); Avatar.SetOwnKit(ref sets[i].avatar, true); }
            ShotAt("av_sets_kit", NativeSpace.ToUnity(0f, 1.3f, 2.6f), NativeSpace.ToUnity(0f, 1.25f, 0f), Sets, 60f);
            ShotAt("av_sets_kit_back", NativeSpace.ToUnity(0f, 1.4f, -2.6f), NativeSpace.ToUnity(0f, 1.3f, 0f), Sets, 60f);
            ShotAt("av_dress_kit", NativeSpace.ToUnity(0f, 1.45f, 0.9f), NativeSpace.ToUnity(0f, 1.35f, 0f), Sets, 45f);
        }

        public static void ArmsUp()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var av = NewAvatars();
            float[] degs = { 0f, 60f, 90f };
            var row = new Sim.PlayerInput[6];
            for (int i = 0; i < 6; ++i)
            {
                var look = new[] { Avatar.PresetLook(1) };
                Avatar.Set(ref look[0], Avatar.Field.Body, i < 3 ? 0 : 1);
                var @in = Stand(new Sim.Vec3(-2.5f * 0.9f + i * 0.9f, 0, 0), Sim.M.kPi, look[0]);
                var q = @in.head.q;
                float a = Sim.M.Deg2Rad(degs[i % 3]);
                for (int h = 0; h < 2; ++h)
                {
                    float s = h == 1 ? 1f : -1f;
                    var sh = new Sim.Vec3(s * 0.18f, -0.29f, 0.04f);
                    @in.hand[h].grip = new Sim.Pose(q * Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), a),
                                                    @in.head.p + q.Rotate(sh + new Sim.Vec3(s * 0.04f, Mathf.Sin(a) * 0.62f, -Mathf.Cos(a) * 0.62f)));
                }
                row[i] = @in;
            }
            void Row(Camera c) { av.camera = c; for (int i = 0; i < row.Length; ++i) DrawStanding(av, row[i], i % 2, 60 + i); }
            ShotAt("av_armsup", NativeSpace.ToUnity(0f, 1.5f, 4.0f), NativeSpace.ToUnity(0f, 1.5f, 0f), Row, 70f);
            ShotAt("av_armsup_back", NativeSpace.ToUnity(0f, 1.5f, -4.0f), NativeSpace.ToUnity(0f, 1.5f, 0f), Row, 70f);
        }

        // The avatar builder (phase 4 step D) as you see it at your end of the court, wearing a country's kit and
        // pointing at a row (Logs/shot_builder.png, _mirror: looking at the mirror), and the title page in the lobby with
        // your avatar on its pedestal (Logs/shot_showcase.png).
        public static void BuilderShots()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots")), lobby = false };
            app.settings.Load(g.tun);
            var look = Avatar.PresetLook(11);
            for (int k = 1; k <= Avatar.NationCount(); ++k) if (Avatar.NationAt(k).code == "BRA") Avatar.SetNation(ref look, k);
            Avatar.Set(ref look, Avatar.Field.Body, 1);
            app.settings.myAvatar = look;
            var av = NewAvatars();
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/" + n + ".png");
            FxRenderer NewFx() => new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                                 AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"), Tex("open_font"), Tex("open_emblem"));
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            Avatar.SideColors(Avatar.NationOf(look), 0, side);

            // you at your end looking down the court, hands forward; the builder opened
            var me = Stand(g.tun.start[0], Sim.M.Deg2Rad(-90), look);
            me.head.p = me.head.p + new Sim.Vec3(0, 0.03f, 0);
            me.hand[1].grip = new Sim.Pose(me.head.q * Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), -0.3f), me.head.p + me.head.q.Rotate(new Sim.Vec3(0.18f, -0.42f, -0.30f)));
            var b = new Builder(app);
            b.Open(me);
            Sim.Vec3 target = b.ControlPoint(Builder.HitCountryNext);
            var aim = new Sim.Pose(Sim.M.FromTo(new Sim.Vec3(0, 0, -1), Sim.M.Normalize(target - me.hand[1].grip.p)), me.hand[1].grip.p);
            b.Update(me, aim, true);
            var fx = NewFx();
            GameObject holder = null;
            void Draw(Camera cam)
            {
                av.camera = cam;
                b.Draw(fx, av, me, side, aim, true);
                if (holder != null) Object.DestroyImmediate(holder);
                holder = new GameObject("fx");
                holder.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Matrix4x4 grip = HandPlacement.FromPose(me.hand[1].grip);
                av.DrawGauntlet(look, grip, av.LookOf(look, 0, side));
            }
            Vector3 eye = NativeSpace.ToUnity(me.head.p);
            Shot("builder", eye, Quaternion.LookRotation(NativeSpace.ToUnity(b.pose.p) - eye), 90f, draw: Draw);
            Shot("builder_mirror", eye, Quaternion.LookRotation(NativeSpace.ToUnity(b.mirrorPoint + new Sim.Vec3(0, 1.2f, 0)) - eye), 90f, draw: Draw);
            Shot("builder_panel", eye, Quaternion.LookRotation(NativeSpace.ToUnity(b.pose.p) - eye), 50f, draw: Draw);
            Shot("builder_left", eye, Quaternion.LookRotation(NativeSpace.ToUnity(b.poseL.p) - eye), 50f, draw: Draw);   // (PRESET AND BODY: TASKS.md E36)

            // the title page on the lobby balcony, your avatar to the right
            b.Close();
            app.lobby = true;
            var menu = new Menu(app) { emblem = Tex("open_emblem") };
            var head = new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.65f, 0));
            app.viewHead = head;
            menu.Open(head, Lobby.Spot);
            menu.titleOpen = true;
            var fx2 = NewFx();
            void Title(Camera cam)
            {
                av.camera = cam;
                menu.Draw(fx2, aim, false);
                Builder.DrawShowcase(fx2, av, app, menu, side, 3.0);
                if (holder != null) Object.DestroyImmediate(holder);
                holder = new GameObject("fx");
                holder.AddComponent<MeshFilter>().sharedMesh = fx2.Snapshot(out var mats);
                holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            Vector3 eye2 = NativeSpace.ToUnity(head.p);
            Vec3Look(out Quaternion rot, eye2, NativeSpace.ToUnity(Lobby.Spot + new Sim.Vec3(-0.1f, 1.2f, -1.3f)));   // (the menu 1.3 m out, MY BESTS at its left, the figure at its right: native 1159a46)
            Shot("showcase", eye2, rot, 90f, draw: Title);

            // the builder opened on the balcony (a Quest 3, 2026-11-14: opened from the menu, no reflection to see): facing the
            // menu (toward the court) and turned 75 degrees left, where the CUSTOMIZE poster stood before native 1159a46
            // (Logs/shot_builder_lobby_menu.png, _side.png)
            menu.Close();
            var lobbyView = new Lobby(app, menu);
            foreach (var (nm, yawDeg) in new[] { ("menu", 0f), ("side", -75f) })
            {
                var meL = Stand(Lobby.Spot, Sim.M.Deg2Rad(yawDeg), look);
                app.viewHead = meL.head;
                var bl = new Builder(app);
                bl.Open(meL);
                var fx3 = NewFx();
                void DrawL(Camera cam)
                {
                    av.camera = cam;
                    lobbyView.Draw(fx3, aim, false);
                    bl.Draw(fx3, av, meL, side, aim, false);
                    if (holder != null) Object.DestroyImmediate(holder);
                    holder = new GameObject("fx");
                    holder.AddComponent<MeshFilter>().sharedMesh = fx3.Snapshot(out var mats);
                    holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                Vector3 eyeL = NativeSpace.ToUnity(meL.head.p);
                Vector3 at = NativeSpace.ToUnity(new Sim.Vec3(bl.mirrorPoint.x, meL.head.p.y - 0.5f, bl.mirrorPoint.z));
                Shot("builder_lobby_" + nm, eyeL, Quaternion.LookRotation(at - eyeL), 100f, draw: DrawL);
                Debug.Log($"[SceneShots] builder on the balcony facing the {nm}: head {meL.head.p.x:F2},{meL.head.p.y:F2},{meL.head.p.z:F2}, mirror {bl.mirrorPoint.x:F2},{bl.mirrorPoint.y:F2},{bl.mirrorPoint.z:F2}");
                bl.Close();
            }
        }

        static void Vec3Look(out Quaternion rot, Vector3 from, Vector3 to) => rot = Quaternion.LookRotation(to - from);

        // The spectator's DIRECTOR TOOLS with two cameras and the info preview (Logs/shot_director.png), and The Crowd in
        // the stands: USA flags waving for the blue end, foam fingers for the orange end, cheer bursts
        // (Logs/shot_crowd.png, _crowd_end: the end stand).
        public static void DirectorAndCrowd()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var g = new Sim.Game();
            g.Reset(true);
            var app = new AppState { game = g, settings = new AppSettings(System.IO.Path.Combine(Application.temporaryCachePath, "shots_dir")), net = new Online.Net(), lobby = false, spectating = true, watchIp = 1 };
            app.settings.Load(g.tun);
            var session = new ArenaSession(app);
            var d = new Director(session);
            app.director = d;
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/" + n + ".png");
            Shader fxS = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"), textS = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader");
            var fx = new FxRenderer(fxS, textS, Tex("open_font"), Tex("open_emblem"));
            var pb = new PanelBatch(textS);
            var head = new Sim.PlayerInput { headTracked = true, head = new Sim.Pose(Sim.Quat.Identity, new Sim.Vec3(0, 2.6f, 5.4f)) };
            app.localIn = head;
            d.warps.Add(new Director.WarpCam { pose = new Sim.Pose(Sim.Quat.Identity, new Sim.Vec3(0, 2.6f, 5.4f)) });
            d.warps.Add(new Director.WarpCam { pose = new Sim.Pose(Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), 1.2f), new Sim.Vec3(6, 2, 3)), trig = 1, hold = 2 });
            d.warpIdx = 1;
            d.Open(head);
            d.infoKind = 4;   // ACTION CAMERA
            d.infoStart = Time.realtimeSinceStartupAsDouble - 4.0;   // (the camera's own view)
            GameObject holder = null;
            void Flush()
            {
                if (holder != null) Object.DestroyImmediate(holder);
                holder = new GameObject("fx");
                holder.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            d.RenderPreview(fxS, textS, Tex("open_font"));
            void Dir(Camera c)
            {
                d.Draw(fx, pb, new Sim.Pose(head.head.q, head.head.p + new Sim.Vec3(0.2f, -0.4f, -0.1f)), false);
                Flush();
                pb.Flush(c);
            }
            Vector3 eye = NativeSpace.ToUnity(head.head.p);
            Shot("director", eye, Quaternion.LookRotation(NativeSpace.ToUnity(d.pose.p + new Sim.Vec3(0.45f, 0, 0)) - eye), 80f, draw: Dir);

            // The Crowd: a country at the blue end (USA: flags), none at the orange end (foam fingers); bursts from both
            var lib = AssetDatabase.LoadAssetAtPath<AvatarLibrary>("Assets/ProjectArena/Settings/AvatarLibrary.asset");
            var av = NewAvatars();
            var stands = new CrowdStands { foam = lib.crowdFoam, heart = lib.crowdHeart, star = lib.crowdStar, hotdog = lib.crowdHotdog, smile = lib.crowdSmile, flagTex = n => av.FlagTexture(n) };
            int usa = 0;
            for (int k = 1; k <= Avatar.NationCount(); ++k) if (Avatar.NationAt(k).code == "USA") usa = k;
            int[] nation = { usa, 0 };
            float[][] side = { new float[3], new float[3] };
            Avatar.SideColors(usa, 0, side);
            var crowd = new Online.CrowdInfo { fans = 60 };
            crowd.waves[0] = 30; crowd.waves[1] = 25; crowd.hype[0] = 80; crowd.hype[1] = 40;
            for (int i = 0; i < 4; ++i) { stands.Burst(0, 40); stands.Burst(1, 40); }
            for (int i = 0; i < 60; ++i) stands.Step(0.011f);
            void Crowd(Camera c)
            {
                stands.Draw(pb, crowd, 0, 3.3f, nation, side, true, XrInput.ToNative(c.transform.position));
                pb.Flush(c);
            }
            Vector3 e2 = NativeSpace.ToUnity(new Sim.Vec3(0f, 1.7f, 0f));
            Shot("crowd", e2, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(-6f, 6f, 10f)) - e2), 90f, draw: Crowd);
            Shot("crowd_orange", e2, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(6f, 6f, -10f)) - e2), 90f, draw: Crowd);
        }

        // The Crowd in the four-player arenas (task C11, native 0.3.17): flags and cheer bursts in FOUR CORNERS' and
        // FIREBALL BLITZ's widened, lowered stadium (Logs/shot_crowd_fourcorners.png, shot_crowd_blitz.png).
        public static void CrowdWide()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var root = GameObject.Find("Arena");
            var lib = AssetDatabase.LoadAssetAtPath<AvatarLibrary>("Assets/ProjectArena/Settings/AvatarLibrary.asset");
            var av = NewAvatars();
            Shader textS = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader");
            int usa = 0;
            for (int k = 1; k <= Avatar.NationCount(); ++k) if (Avatar.NationAt(k).code == "USA") usa = k;
            int[] nation = { usa, 0 };
            float[][] side = { new float[3], new float[3] };
            Avatar.SideColors(usa, 0, side);
            var crowd = new Online.CrowdInfo { fans = 60 };
            crowd.waves[0] = 30; crowd.waves[1] = 25; crowd.hype[0] = 80; crowd.hype[1] = 40;
            foreach (var (name, room, arena) in new[] { ("fourcorners", Sim.Room.Corners, Sim.Arena.FourCorners), ("blitz", Sim.Room.Blitz, Sim.Arena.Octagon) })
            {
                var g = new Sim.Game();
                g.tun.experimental = true; g.tun.expRoom = room; g.tun.players = 4;
                g.UseArena(arena, new System.Collections.Generic.List<Sim.CollisionTri>());
                g.ResetWithBots(0xEu);
                ExpArenaView.Outside(g, out Sim.Vec3 at, out Sim.Vec3 sc);
                foreach (Transform c in root.transform)
                {
                    string n = c.gameObject.name;
                    c.gameObject.SetActive(n != "arena" && n != "floor" && n != "floor_reflect" && n != "floor_reflect_env" && n != "court_fx" && n != "court_pulse");
                    if (n == "environment" || n == "env_fx" || n == "stadium_fx") { c.localPosition = NativeSpace.ToUnity(at); c.localScale = new Vector3(sc.x, sc.y, sc.z); }
                }
                var stands = new CrowdStands { foam = lib.crowdFoam, heart = lib.crowdHeart, star = lib.crowdStar, hotdog = lib.crowdHotdog, smile = lib.crowdSmile, flagTex = n => av.FlagTexture(n) };
                for (int i = 0; i < 4; ++i) { stands.Burst(0, 40); stands.Burst(1, 40); }
                for (int i = 0; i < 60; ++i) stands.Step(0.011f);
                var pb = new PanelBatch(textS);
                void Draw(Camera c)
                {
                    stands.Draw(pb, crowd, 0, 3.3f, nation, side, true, XrInput.ToNative(c.transform.position), sc.x, at.y);
                    pb.Flush(c);
                }
                Vector3 e = NativeSpace.ToUnity(g.tun.start[0] + new Sim.Vec3(0, 1.65f, 0));
                Shot("crowd_" + name, e, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(-8f, 5f, 13f)) - e), 100f, draw: Draw);
            }
        }

        // Online play (phase 5): three players through a private local relay (127.0.0.1:47797, started and stopped
        // here; never the live server): ALICE hosts and plays the bot, BOB waits queued, CAROL waits; DAVE spectates.
        // Writes Logs/shot_net_room.png (ALICE in the tank looking at the holding area), _kiosk (BOB at his spot),
        // _menu (BOB's MULTIPLAYER page), _jumbo (the jumbotron), _hub (DAVE in the spectator hub).
        public static void Online()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pa_shots_relay_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string relayPy = System.IO.Path.Combine(Builds.FindNativeRepo(), "server", "projectarena_relay.py");
            var psi = new System.Diagnostics.ProcessStartInfo("py", $"-3.12 -I \"{relayPy}\" --port 47797 --bind 127.0.0.1 --stats \"{dir}\\s.db\" --reports \"{dir}\\r\" --bans \"{dir}\\b.txt\" --latest \"{dir}\\l.txt\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
            var relay = System.Diagnostics.Process.Start(psi);
            System.Threading.Thread.Sleep(1200);
            var col = Sim.ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes"));
            var players = new System.Collections.Generic.List<(AppState a, ArenaSession s, Menu m, RoomView r, Sim.PlayerInput local)>();
            (AppState a, ArenaSession s, Menu m, RoomView r, Sim.PlayerInput local) Make(string name, int look)
            {
                var g = new Sim.Game();
                g.SetArena(col);
                var st = new AppSettings(System.IO.Path.Combine(dir, name));
                st.Load(g.tun);
                st.SaveName(name);
                st.LoadAvatar((uint)look);
                st.myAvatar = Avatar.PresetLook(look);
                st.server = "127.0.0.1:47797"; st.onlinePref = true; st.statsNoticeSeen = true;
                var a = new AppState { game = g, settings = st, net = new Online.Net(), lobby = true };
                var m = new Menu(a) { emblem = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png") };
                var s = new ArenaSession(a) { menu = m, builder = new Builder(a) };
                s.StartNetwork("");
                var p = (a, s, m, new RoomView(s), new Sim.PlayerInput { headTracked = true });
                players.Add(p);
                return p;
            }
            void Stand((AppState a, ArenaSession s, Menu m, RoomView r, Sim.PlayerInput local) p)
            {
                p.s.ViewTarget(p.s.L, out Sim.Vec3 pos, out Sim.Vec3 fwd);
                var q = Sim.Quat.AxisAngle(new Sim.Vec3(0, 1, 0), Mathf.Atan2(-fwd.x, -fwd.z));
                p.local.head = new Sim.Pose(q, pos + new Sim.Vec3(0, 1.62f, 0));
                for (int h = 0; h < 2; ++h)
                {
                    float sd = h == 1 ? 1f : -1f;
                    p.local.hand[h].tracked = true;
                    p.local.hand[h].grip = new Sim.Pose(q * Sim.Quat.AxisAngle(new Sim.Vec3(1, 0, 0), -1.05f) * Sim.Quat.AxisAngle(new Sim.Vec3(0, 0, 1), sd * 0.25f),
                                                        p.local.head.p + q.Rotate(new Sim.Vec3(sd * 0.24f, -0.62f, -0.22f)));
                }
            }
            void Pump(double secs)
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                while (t.Elapsed.TotalSeconds < secs)
                {
                    foreach (var p in players) { Stand(p); p.s.Step(p.local.Clone(), 1f / 60f); }
                    System.Threading.Thread.Sleep(16);
                }
            }
            try
            {
                var A = Make("ALICE", 10); var B = Make("BOB", 1); var C = Make("CAROL", 5); var D = Make("DAVE", 7);
                Pump(1.5);
                A.m.Open(A.local.head, Lobby.Spot);
                A.m.Activate(A.m.Items().Find(i => i.label == "HOST A ROOM"));
                A.m.Activate(A.m.Items().Find(i => i.label == "ADVANCED"));
                A.m.Activate(A.m.Items().Find(i => i.label == "OPEN MY ARENA"));
                Pump(0.5);
                A.a.net.Queue(true);
                Pump(2.0);
                B.m.Open(B.local.head, Lobby.Spot);
                B.m.Activate(B.m.Items().Find(i => i.label == "MULTIPLAYER"));
                Pump(1.0);
                var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"),
                                        AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png"));
                var av = NewAvatars();
                GameObject holder = null;
                void Flush()
                {
                    if (holder != null) Object.DestroyImmediate(holder);
                    holder = new GameObject("fx");
                    holder.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                    holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                var aimB = new Sim.Pose(B.local.head.q, B.local.head.p + new Sim.Vec3(0.2f, -0.4f, 0));
                void MenuB(Camera c) { av.camera = c; Builder.DrawShowcase(fx, av, B.a, B.m, B.a.sideRGB, 3.0); B.m.Draw(fx, aimB, false); Flush(); }
                Vector3 eyeB = NativeSpace.ToUnity(B.local.head.p);
                Shot("net_menu", eyeB, Quaternion.LookRotation(NativeSpace.ToUnity(B.m.pose.p) - eyeB), 70f, draw: MenuB);
                B.m.Activate(B.m.Items().Find(i => i.act == Menu.Act.JoinRoom));
                C.m.Open(C.local.head, Lobby.Spot);
                C.m.Activate(C.m.Items().Find(i => i.label == "MULTIPLAYER"));
                Pump(1.0);
                C.m.Activate(C.m.Items().Find(i => i.act == Menu.Act.JoinRoom));
                Pump(1.5);
                B.a.net.Queue(true);
                D.a.net.Spectate();
                Pump(2.0);
                Debug.Log($"[SceneShots] room: ALICE slot {A.a.net.LocalSlot()}, BOB waiting {B.a.net.LocalWaiting()} queue {B.a.net.LocalQueuePos()}, " +
                          $"CAROL waiting {C.a.net.LocalWaiting()}, DAVE matches {D.a.net.Matches.Count}; {A.a.net.Status()}");

                // ALICE in the tank, turned toward the holding area outside the glass (-Z)
                void DrawPeople((AppState a, ArenaSession s, Menu m, RoomView r, Sim.PlayerInput local) p, Camera c)
                {
                    av.camera = c;
                    for (int k = 0; k < 2; ++k)
                    {   // the tank's players (not yourself), as MatchView draws them
                        var vin = p.s.viewIn[k];
                        if (k == p.s.L && !p.a.viewing) continue;
                        if (!p.a.net.SlotFilled(k) || !vin.headTracked) continue;
                        DrawStanding(av, vin, k, 300 + k);
                    }
                    p.r.DrawRoomPeople(fx, av, new Sim.Pose(p.local.head.q, p.local.head.p), false, p.m);
                    Scoreboard.Draw(fx, p.s.Shown, p.s.L, new[] { new Vector3(0.1f, 0.7f, 1f), new Vector3(1f, 0.4f, 0.1f) },
                                    new Scoreboard.Info { status = p.a.net.Status(), watching = p.a.viewing, notice = p.a.net.NoticeText, sideName = p.s.SideName }, p.local.head.p);
                    string[] names = { p.s.PlayerLabel(0), p.s.PlayerLabel(1) };
                    string[][] lines = { Scoreboard.CareerLines(p.a.careersOk, p.a.careers[0]), Scoreboard.CareerLines(p.a.careersOk, p.a.careers[1]) };
                    Scoreboard.Jumbotron(fx, p.s.Shown, p.a.sideRGB, p.a.sideNation, names, lines, null, true, (n, at, w) => av.DrawFlag(n, at, w));
                    Flush();
                }
                Vector3 eyeA = NativeSpace.ToUnity(A.local.head.p);
                Shot("net_room", eyeA, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(-3f, 1.2f, -4.2f)) - eyeA), 90f, draw: c => DrawPeople(A, c));
                Vector3 eyeJ = NativeSpace.ToUnity(new Sim.Vec3(-5f, 5.5f, 0.8f));
                Shot("net_jumbo", eyeJ, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(0, 8f, 0)) - eyeJ), 60f, draw: c => DrawPeople(A, c));
                Vector3 eyeBk = NativeSpace.ToUnity(B.local.head.p);
                Shot("net_kiosk", eyeBk, Quaternion.LookRotation(NativeSpace.ToUnity(B.local.head.p + new Sim.Vec3(0, -0.35f, 1f)) - eyeBk), 90f, draw: c => DrawPeople(B, c));
                if (C.a.net.LocalWaiting())
                {   // the QUEUE SCREEN (TASKS.md E5): to CAROL's right at her waiting spot, turned toward her (as with
                    // PLAYERS' NAMES on); and looking ahead, only its edge at the bottom right
                    C.r.showQueueScreen = true;
                    Sim.Pose qs = RoomView.QueueScreenPose(C.r.KioskPose());
                    Vector3 eyeC = NativeSpace.ToUnity(C.local.head.p);
                    Sim.Vec3 ahead = Sim.M.Normalize(C.r.KioskPose().p - C.local.head.p);
                    Shot("net_queue_screen", eyeC, Quaternion.LookRotation(NativeSpace.ToUnity(qs.p) - eyeC), 90f, draw: c => DrawPeople(C, c));
                    Shot("net_queue_ahead", eyeC, Quaternion.LookRotation(NativeSpace.ToUnity(C.local.head.p + new Sim.Vec3(ahead.x, 0, ahead.z) * 3f) - eyeC), 90f, draw: c => DrawPeople(C, c));
                    C.r.showQueueScreen = false;
                }
                // DAVE in the hub
                foreach (var go in GameObject.Find("Arena").GetComponentsInChildren<Transform>(true))
                    if (go.parent != null && go.parent.name == "Arena" && go.name != "environment" && go.name != "floor") go.gameObject.SetActive(false);
                D.r.miniModels = new[] { "floor", "arena", "court_fx", "court_pulse" }.Select(n => GameObject.Find("Arena").transform.Find(n)?.gameObject).Where(x => x != null).ToArray();
                Vector3 eyeD = NativeSpace.ToUnity(D.local.head.p);
                Shot("net_hub", eyeD, Quaternion.LookRotation(NativeSpace.ToUnity(new Sim.Vec3(1.35f, 0.9f, 0)) - eyeD), 80f,
                     draw: c => { av.camera = c; D.r.DrawHub(fx, av, new Sim.Pose(D.local.head.q, D.local.head.p), false, D.m, c); Flush(); });
            }
            finally
            {
                foreach (var p in players) { if (p.a.net.Online) p.a.net.GoOffline(); p.a.net.Shutdown(); }
                try { relay.Kill(); } catch (System.Exception) { }
            }
        }

        // PASSTHROUGH (task D1): the court as drawn for your room (RoomPassthrough's rules, as MatchView applies them),
        // ARENA / WINDOW / OUTLINE and OFF from beside your end, over a stand-in for your room (a warm grey checker where
        // the picture is see-through), composited as the headset does (premultiplied: the game's colour + the room x
        // (1 - its alpha)). Writes Logs/shot_pt_*.png and logs how much of each is your room.
        public static void Passthrough()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            Transform arena = GameObject.Find("Arena").transform;
            if (arena.Find("court_outline") == null)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectArena/Art/Open/Models/court_outline.mdl");
                var o = (GameObject)Object.Instantiate(model, arena);
                o.name = "court_outline";
            }
            var glass = RoomPassthrough.GlassOf(arena.Find("arena").GetComponentsInChildren<Renderer>(true));
            Renderer[] floor = arena.Find("floor").GetComponentsInChildren<Renderer>(true);
            var mpb = new MaterialPropertyBlock();
            Debug.Log($"[SceneShots] passthrough: {glass.Count} glass sections, {floor.Length} floor renderers");
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            foreach (var (name, mode) in new[] { ("arena", RoomPassthrough.ArenaMode), ("window", RoomPassthrough.Window),
                                                  ("outline", RoomPassthrough.Outline), ("off", RoomPassthrough.Off) })
            {
                bool mr = mode != RoomPassthrough.Off, outline = mode == RoomPassthrough.Outline;
                float ptFloor = RoomPassthrough.ModeFloor[mode];
                foreach (Transform c in arena) c.gameObject.SetActive(RoomPassthrough.Shows(c.name, mr, outline, mr && ptFloor <= 0.01f));
                RoomPassthrough.SetGlass(glass, mpb, 1f);
                RoomPassthrough.SetFloor(floor, mpb, RoomPassthrough.FloorFor(mr, ptFloor));
                GameObject sky = null;
                if (mode == RoomPassthrough.Window)
                {   // (MatchView's night sky over the court)
                    fx.Wall(new Sim.Vec3(0, 4f, 0), new Sim.Vec3(1, 0, 0), new Sim.Vec3(0, 0, 1), 19f, 5f, RoomPassthrough.WindowSky);
                    sky = new GameObject("window sky");
                    sky.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                    sky.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                Vector3 from = NativeSpace.ToUnity(new Sim.Vec3(-8.6f, 1.65f, 1.7f)), to = NativeSpace.ToUnity(new Sim.Vec3(0f, 1.3f, -0.9f));
                RoomShot("pt_" + name, from, Quaternion.LookRotation(to - from), 100f, mr);   // (OFF: cleared solid, as the game does)
                if (sky != null) Object.DestroyImmediate(sky);
            }
        }

        static void RoomShot(string name, Vector3 pos, Quaternion rot, float fov, bool forRoom, int w = 1280, int h = 720)
        {
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = forRoom ? new Color(0, 0, 0, 0) : new Color(0, 0, 0, 1);   // nothing drawn = your room (drawn for it)
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = null;
            Color32[] px = tex.GetPixels32();
            int room = 0, part = 0;
            for (int i = 0; i < px.Length; ++i)
            {
                int x = i % w, y = i / w;
                bool light = ((x / 40) + (y / 40)) % 2 == 0;
                Vector3 rc = light ? new Vector3(0.66f, 0.60f, 0.50f) : new Vector3(0.44f, 0.40f, 0.34f);
                float a = px[i].a / 255f;
                if (a < 0.02f) ++room; else if (a < 0.98f) ++part;
                byte C(byte c, float r) => (byte)Mathf.Clamp(Mathf.RoundToInt(c + 255f * r * (1f - a)), 0, 255);
                px[i] = new Color32(C(px[i].r, rc.x), C(px[i].g, rc.y), C(px[i].b, rc.z), 255);
            }
            tex.SetPixels32(px);
            System.IO.File.WriteAllBytes($"Logs/shot_{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(go);
            rt.Release();
            Debug.Log($"[SceneShots] Logs/shot_{name}.png: {100.0 * room / px.Length:F0}% your room, {100.0 * part / px.Length:F0}% partly");
        }

        // the PC spectator (task D2): its picture with the overlay, as the window shows it: a match watched from SIDE
        // (the banner, the match's name and the camera's, the help bar, the SELFIE's place), its win splash, and the hub's
        // title line. The overlay as DesktopSpectator draws it, on the scene's arena. Writes Logs/shot_desk_*.png.
        public static void Desktop()
        {
            EditorSceneManager.OpenScene("Assets/ProjectArena/Scenes/Arena.unity");
            var av = NewAvatars();
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pa_desk_shot");
            var g = new Sim.Game();
            g.SetArena(Sim.ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            var app = new AppState { game = g, settings = new AppSettings(folder), spectating = true, net = new Online.Net() };
            var session = new ArenaSession(app);
            var dir = new Director(session);
            var d = new DesktopSpectator(app, dir) { help = true };
            app.watchIp = 5;
            app.net.Matches.Add(new Online.SpecMatch { ip = 5, name = "SATURDAY LEAGUE", game = g });
            app.net.Matches.Add(new Online.SpecMatch { ip = 6, name = "PRACTICE", game = g });
            var viewIn = new Sim.PlayerInput[] { new Sim.PlayerInput(), new Sim.PlayerInput(), new Sim.PlayerInput(), new Sim.PlayerInput() };
            foreach (var (name, setup) in new (string, System.Action)[]
            {
                ("match", () => { g.match = Sim.MatchState.InProgress; g.pl[0].score = 3; g.pl[1].score = 5; }),
                ("win", () => { g.match = Sim.MatchState.MatchSummary; g.matchWinner = 1; d.winStyle = 1; d.help = false; }),
                ("hub", () => { app.watchIp = 0; d.help = false; }),
            })
            {
                setup();
                Sim.Pose eye = d.Preset(1);
                d.DrawHud(fx, av, session, app.net, g, eye, Mathf.Tan(66f * 0.5f * Mathf.Deg2Rad), 16f / 9f, viewIn, 0f, "");
                var go = new GameObject("hud");
                go.AddComponent<MeshFilter>().sharedMesh = fx.Snapshot(out var mats);
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                Shot("desk_" + name, NativeSpace.ToUnity(eye.p), NativeSpace.ToUnity(eye.q), 66f);
                Object.DestroyImmediate(go);
            }
            if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        }

        static void ShotAt(string name, Vector3 from, Vector3 to, System.Action<Camera> draw, float fov = 90f)
        {
            Shot(name, from, Quaternion.LookRotation(to - from), fov, draw: draw);
        }

        static void Shot(string name, Vector3 pos, Quaternion rot, float fov = 90f, int w = 1280, int h = 720, System.Action<Camera> draw = null)
        {
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;   // as the scene's head camera
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            draw?.Invoke(cam);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            System.IO.File.WriteAllBytes($"Logs/shot_{name}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(go);
            Debug.Log($"[SceneShots] Logs/shot_{name}.png");
        }
    }
}
