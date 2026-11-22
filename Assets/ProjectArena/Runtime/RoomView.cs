using System.Collections.Generic;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // An arena room's people and the spectator hub (native drawRoomPeople, queueKioskPose, updateQueueKiosk,
    // drawMiniMatch, drawHub, updateHub): the waiting players in the holding area outside the tank (full avatars, a
    // neutral rim light, NEXT / 2ND over the queued), the four holding spots, blue lights for spectators at this arena,
    // your own JOIN QUEUE kiosk (point and pull the trigger, or press it by hand); and, spectating, the hub: every live
    // arena as a miniature on a pedestal in an arc in front of you, its players, balls and room, its name and score.
    public class RoomView
    {
        public static readonly Vector4 Red = new Vector4(1.0f, 0.18f, 0.12f, 1), Blue = new Vector4(0.15f, 0.45f, 1.0f, 1);
        readonly ArenaSession s;
        AppState a => s.a;
        Online.Net net => s.net;
        public int queueHover = -1, miniHover = -1;
        bool prevQueueTrig = true, prevQueuePoke, prevMiniTrig = true;
        // the arena's own models, drawn small for the hub's miniatures (the floor, the glass tank, the glow, the pulses)
        public GameObject[] miniModels = new GameObject[0];
        public GameObject courtPulse;
        readonly List<(Mesh mesh, Material[] mats, Matrix4x4 local)> mini = new List<(Mesh, Material[], Matrix4x4)>();
        MaterialPropertyBlock miniBlock;

        public RoomView(ArenaSession s) => this.s = s;

        public static bool InsideTank(Vec3 p) => Mathf.Abs(p.x) < 9.9f && Mathf.Abs(p.z) < 2.9f;

        public RoomInfo CurrentRoom()
        {
            if (net.InRoom) return net.Room;
            if (a.spectating && a.watchIp != 0) return a.watchRoom;
            return null;
        }

        // the kiosk in front of the local waiting player's spot: JOIN QUEUE / LEAVE QUEUE (quad +Z faces the player)
        public Pose KioskPose()
        {
            // (native queueKioskPose: before the spot, the way it faces; CORNERS' spots face into the room)
            Game g = a.shown ?? a.game;
            int spot = net.LocalSpot();
            Vec3 sp = ExpArenaView.HoldingPose(g, spot).p, f = ExpArenaView.HoldingFwd(g, spot);
            return new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(-f.x, -f.z)), new Vec3(sp.x, 0.95f, sp.z) + f * 0.55f);
        }

        // rawTrigger: the pointer hand's trigger before the menu cleared it. Returns true when the kiosk was pressed.
        public bool UpdateKiosk(PlayerInput @in, float rawTrigger, Pose aim, bool aimValid, Menu menu)
        {
            queueHover = -1;
            // the menu takes the laser only where it is pointing at the menu (it can be open in front of the kiosk)
            if (!net.LocalWaiting() || (menu.open && menu.hitValid) || !aimValid) { prevQueueTrig = true; return false; }
            Pose k = KioskPose();
            Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
            Vec3 nz = k.q.Rotate(new Vec3(0, 0, 1));
            float den = M.Dot(d, nz);
            if (den < -1e-3f)
            {
                float t = M.Dot(k.p - o, nz) / den;
                Vec3 lp = k.q.Conj().Rotate(o + d * t - k.p);
                if (t > 0 && t < 3 && Mathf.Abs(lp.x) < 0.25f && Mathf.Abs(lp.y) < 0.08f) queueHover = 0;
            }
            bool poked = false;   // pressing it with either hand counts too (the button is at hand height in front of the spot)
            for (int h = 0; h < 2; ++h)
            {
                if (!@in.hand[h].tracked) continue;
                Vec3 lp = k.q.Conj().Rotate(@in.hand[h].grip.p - k.p);
                if (Mathf.Abs(lp.x) < 0.22f && Mathf.Abs(lp.y) < 0.08f && lp.z < 0.04f && lp.z > -0.10f) poked = true;
            }
            bool trig = rawTrigger > 0.7f;
            bool press = (trig && !prevQueueTrig && queueHover == 0) || (poked && !prevQueuePoke);
            if (press)
            {
                bool join = net.LocalQueuePos() < 0;
                Debug.Log($"[Room] queue kiosk: {(join ? "JOIN QUEUE" : "LEAVE QUEUE")} ({(poked ? "pressed by hand" : "trigger")})");
                net.Queue(join);
            }
            prevQueueTrig = trig;
            prevQueuePoke = poked;
            return press;
        }

        static void Gauntlets(AvatarDraw av, PlayerInput @in, AvatarDraw.Look look, Matrix4x4 pre, bool skipInsideTank)
        {
            for (int h = 0; h < 2; ++h)
            {
                if (!@in.hand[h].tracked || (skipInsideTank && InsideTank(@in.hand[h].grip.p))) continue;
                Matrix4x4 grip = HandPlacement.FromPose(@in.hand[h].grip);
                av.DrawGauntlet(@in.avatar, pre * (h == 0 ? grip * HandPlacement.Mirror : grip), look, @in.hand[h].trigger, h);
            }
        }

        // PLAYERS' NAMES (native drawNameTag): a name over a head, turned to face you, on a dark plate so it reads against
        // the arena
        void DrawNameTag(FxRenderer fx, string name, Vec3 at)
        {
            Vec3 f = M.Normalize(new Vec3(a.viewHead.p.x - at.x, 0, a.viewHead.p.z - at.z));
            var p = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(f.x, f.z)), at);
            float h = 0.075f, w = Mathf.Max(0.2f, h * 0.75f * name.Length + 0.06f);
            fx.Panel(p * new Pose(Quat.Identity, new Vec3(0, 0, -0.003f)), w, h * 1.5f, new Vector4(0.01f, 0.02f, 0.04f, 0.78f));
            fx.Text(name, p, h, new Vector4(1, 1, 1, 1));
        }

        public void DrawRoomPeople(FxRenderer fx, AvatarDraw av, Pose aim, bool aimValid, Menu menu)
        {
            RoomInfo rm = CurrentRoom();
            if (rm == null) return;
            bool inTank = net.InRoom && !net.LocalWaiting();
            uint me = net.Session;
            // waiting players stand in the holding area: full avatars, a neutral rim light; the players may hide them
            if (!(inTank && a.settings.hideOutside))
                foreach (var m in rm.members)
                {
                    if (m.id == me || !m.@in.headTracked || InsideTank(m.@in.head.p)) continue;
                    AvatarDraw.Look look = av.LookOf(m.@in.avatar, 2, a.sideRGB);
                    PlayerInput min = av.Apart(m.@in, m.@in.avatar);   // (HANDS APART)
                    av.DrawBody(min, look, 2, swingKey: 1000 + (int)(m.id & 0xFFFFF));
                    Gauntlets(av, min, look, Matrix4x4.identity, true);
                    if (m.queuePos >= 0)
                    {
                        Vec3 f = M.Normalize(new Vec3(a.viewHead.p.x - m.@in.head.p.x, 0, a.viewHead.p.z - m.@in.head.p.z));
                        fx.Text(m.queuePos == 0 ? "NEXT" : "2ND", new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(f.x, f.z)), m.@in.head.p + new Vec3(0, 0.32f, 0)), 0.07f, Red);
                    }
                    if (a.showNames && rm.NameOf(m.id) is string name)
                        DrawNameTag(fx, name, m.@in.head.p + new Vec3(0, m.queuePos >= 0 ? 0.44f : 0.32f, 0));
                }
            // your own hands while you wait outside (the tank only draws the two players' hands), and a pointer for the kiosk
            if (net.LocalWaiting())
            {
                if (!menu.open && aimValid)
                {
                    Pose k = KioskPose();
                    Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                    Vec3 nz = k.q.Rotate(new Vec3(0, 0, 1));
                    float len = 1.2f, den = M.Dot(d, nz);
                    if (queueHover == 0 && den < -1e-3f) len = M.Dot(k.p - o, nz) / den;
                    var laser = new Vector4(1.0f, 0.35f, 0.25f, 1);
                    fx.Box(new Pose(aim.q, o + d * (len * 0.5f)), new Vec3(0.003f, 0.003f, len * 0.5f), laser);
                    if (queueHover == 0) fx.Glow(o + d * len, 0.008f, laser);
                }
                var mine = a.localIn.Clone();
                mine.avatar = a.settings.myAvatar;
                mine = av.Apart(mine, mine.avatar);   // (HANDS APART)
                Gauntlets(av, mine, av.LookOf(mine.avatar, 2, a.sideRGB), Matrix4x4.identity, false);
            }
            if (a.showNames)
            {   // PLAYERS' NAMES: the players in the match, not yourself
                Game g = a.shown ?? a.game;
                int L = a.watchIp != 0 ? -1 : s.L;
                for (int p = 0; p < g.np; ++p)
                    if (p != L && g.pl[p].@in.headTracked) DrawNameTag(fx, s.PlayerLabel(p), g.pl[p].@in.head.p + new Vec3(0, 0.36f, 0));
            }
            // the holding area: a glowing strip with four spots (everyone in the room sees it, players included)
            for (int sp = 0; sp < 4; ++sp)
            {
                Vec3 c = ExpArenaView.HoldingPose(a.shown ?? a.game, sp).p;
                fx.SpriteFlat(new Vec3(c.x, 0.006f, c.z), 1.1f, new Vector4(0.35f, 0.08f, 0.06f, 1), new Vec3(0, 1, 0), 1);
            }
            // spectators near this arena: small blue lights, only for people outside the tank (never for the players)
            if (!inTank)
                foreach (var w in rm.watchers)
                    if (w.where == 1 && w.id != me) fx.Sprite(w.pos, 0.09f, Blue, a.viewHead.p, 0);
            // our own JOIN QUEUE kiosk, and the queue screen beside it
            if (net.LocalWaiting())
            {
                Pose k = KioskPose();
                int q = net.LocalQueuePos();
                Vector4 bg = new Vector4(0.02f, 0.02f, 0.03f, 0.9f), btn = new Vector4(0.10f, 0.03f, 0.03f, 0.95f), hov = new Vector4(0.75f, 0.15f, 0.10f, 0.98f);
                Vector4 white = new Vector4(1, 1, 1, 1), grey = new Vector4(0.55f, 0.6f, 0.68f, 1);
                fx.Panel(k * new Pose(Quat.Identity, new Vec3(0, 0.03f, -0.002f)), 0.46f, 0.20f, bg);
                fx.Panel(k, 0.40f, 0.09f, queueHover == 0 ? hov : btn);
                fx.Text(q < 0 ? "JOIN QUEUE" : "LEAVE QUEUE", k * new Pose(Quat.Identity, new Vec3(0, 0, 0.004f)), 0.04f, white);
                int queued = 0;
                foreach (var m in rm.members) queued += m.queuePos >= 0 ? 1 : 0;
                string line = q >= 0 ? $"YOU ARE {(q == 0 ? "NEXT" : "SECOND")} IN LINE" : $"{queued} OF 2 IN THE QUEUE";
                fx.Text(line, k * new Pose(Quat.Identity, new Vec3(0, 0.085f, 0.004f)), 0.022f, grey);
                fx.Text(Menu.RoomName(s.game.tun), k * new Pose(Quat.Identity, new Vec3(0, -0.055f, 0.004f)), 0.020f, grey);   // (SQUARE - BEST OF 3)
                if (a.showNames || showQueueScreen)
                {   // (the screen: only with the left stick's click, PLAYERS' NAMES)
                    string[] next = new string[2];   // who is next and after them (the queue's first two)
                    foreach (var m in rm.members)
                        if ((m.queuePos == 0 || m.queuePos == 1) && rm.NameOf(m.id) is string n) next[m.queuePos] = m.id == me ? "YOU" : n;
                    DrawQueueScreen(fx, QueueScreenPose(k), next);
                }
            }
        }

        public bool showQueueScreen;   // (pictures: the queue screen without the stick's click)

        // The QUEUE SCREEN (native drawQueueStation; a player's report, 2026-11-11: "in the queue, the score and the
        // players are hard to see over the arena"; the owner, 2026-11-15: "have it appear to your right offcenter so it
        // doesn't block play of the arena and maybe only when you press in the left thumbstick like when you display
        // the player name"): to the player's right (the kiosk's +X) and a little nearer, at about eye height, turned
        // toward them (68 degrees) with its top tilted back, clear of the court ahead
        public static Pose QueueScreenPose(Pose kiosk) =>
            kiosk * new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), M.Deg2Rad(-68f)) * Quat.AxisAngle(new Vec3(1, 0, 0), M.Deg2Rad(-15f)),
                             new Vec3(0.68f, 0.36f, 0.34f));

        // the match's players in their colours, the score and strikes (FOUR CORNERS: the corners or teams; FIREBALL
        // BLITZ: health and how many are still in), the clock or the round, the queue's next two, and the round's end
        void DrawQueueScreen(FxRenderer fx, Pose sc, string[] next)
        {
            Game g = a.shown ?? s.game;
            const float W = 0.62f, H = 0.27f;
            Pose At(float u, float v) => sc * new Pose(Quat.Identity, new Vec3(u, v, 0.004f));
            Vector4 bg = new Vector4(0.02f, 0.02f, 0.03f, 0.9f), frame = new Vector4(0.25f, 0.30f, 0.38f, 0.9f);
            Vector4 white = new Vector4(1, 1, 1, 1), grey = new Vector4(0.55f, 0.6f, 0.68f, 1), gold = new Vector4(1.0f, 0.85f, 0.3f, 1);
            fx.Panel(sc * new Pose(Quat.Identity, new Vec3(0, 0, -0.003f)), W + 0.015f, H + 0.015f, frame);
            fx.Panel(sc, W, H, bg);
            Vector4 Col(int p)
            {
                float[] t = g.np == 4 ? ExpArenaView.TeamRGB(g, a.sideRGB, p) : a.sideRGB[p & 1];
                return new Vector4(t[0] * 0.6f + 0.4f, t[1] * 0.6f + 0.4f, t[2] * 0.6f + 0.4f, 1);
            }
            string Cut(string x, int n) => x.Length > n ? x.Substring(0, n) : x;
            if (g.np == 2)
            {
                Vector4 c0 = Col(0), c1 = Col(1);
                fx.Text(Cut(s.PlayerLabel(0), 12), At(-0.20f, 0.085f), 0.030f, c0);
                fx.Text(Cut(s.PlayerLabel(1), 12), At(0.20f, 0.085f), 0.030f, c1);
                fx.Text(g.pl[0].score.ToString(), At(-0.075f, 0.03f), 0.075f, c0);
                fx.Text(g.pl[1].score.ToString(), At(0.075f, 0.03f), 0.075f, c1);
                fx.Text("-", At(0, 0.03f), 0.05f, grey);
                fx.Text("STRIKES " + g.pl[0].strikesAgainst, At(-0.20f, 0.03f), 0.020f, grey);
                fx.Text("STRIKES " + g.pl[1].strikesAgainst, At(0.20f, 0.03f), 0.020f, grey);
                string line = g.tun.advanced
                    ? $"ROUND {System.Math.Max(1, g.round)}  -  ROUNDS {g.pl[0].roundWins} - {g.pl[1].roundWins}  {Scoreboard.ClockLine(g)}"
                    : Scoreboard.ClockLine(g) + (g.tun.shields ? "" : "  -  HOTDOG");
                fx.Text(line, At(0, -0.035f), 0.024f, g.overtime ? gold : white);
            }
            else
            {   // four: each corner or team, its score (FIREBALL BLITZ: its health); how many are still in
                bool teams = !g.Octagon && g.tun.teamMode != Teams.FFA;
                int n = teams ? 2 : 4;
                for (int t = 0; t < n; ++t)
                {
                    Vector4 c = Col(t);
                    if (g.Octagon)
                    {   // (FIREBALL BLITZ: always the four players, each in its colour: its team's, in teams; TASKS.md E51)
                        float[] pc = ExpArenaView.PlayerRGB(g, a.sideRGB, t);
                        c = new Vector4(pc[0] * 0.6f + 0.4f, pc[1] * 0.6f + 0.4f, pc[2] * 0.6f + 0.4f, 1);
                    }
                    float u = (t - 0.5f * (n - 1)) * (W / n);
                    if (g.Octagon)
                    {   // its health: a square for each point left (as over their heads), OUT when gone
                        if (!g.Alive(t)) fx.Text("OUT", At(u, 0.04f), 0.04f, grey);
                        else
                        {
                            int hp = g.MaxHealth(t);   // (3 v 1: THE ONE's three times HEALTH)
                            float pip = System.Math.Min(0.024f, (W / n - 0.02f) / (hp * 1.35f)), gap = pip * 1.35f;
                            for (int i = 0; i < hp; ++i)
                            {
                                bool full = i < g.pl[t].health;
                                fx.Panel(At(u - 0.5f * gap * (hp - 1) + i * gap, 0.04f), pip, pip,
                                         full ? c : new Vector4(0.14f, 0.14f, 0.16f, 0.7f));
                            }
                        }
                    }
                    else fx.Text(g.TeamScore(t).ToString(), At(u, 0.04f), 0.07f, c);
                    string nm = teams ? (t == 0 ? "BLUE TEAM" : "ORANGE TEAM") : s.PlayerLabel(t);
                    fx.Text(Cut(nm, n == 2 ? 14 : 9), At(u, 0.095f), 0.022f, c);
                }
                fx.Text(g.Octagon ? $"{g.AliveCount()} STILL IN" : Scoreboard.ClockLine(g), At(0, -0.035f), 0.024f, white);
            }
            // the queue's next two (YOU when it is us), or the round's end
            string end = g.match == MatchState.RoundSummary ? "ROUND OVER" : g.match == MatchState.MatchSummary ? "MATCH OVER" : null;
            if (end != null) fx.Text(end, At(0, -0.09f), 0.028f, gold);
            else if (!string.IsNullOrEmpty(next[0]))
            {
                string t = "NEXT: " + next[0] + (string.IsNullOrEmpty(next[1]) ? "" : "   THEN: " + next[1]);
                fx.Text(t, At(0, -0.09f), t.Length > 34 ? 0.018f : 0.022f, next[0] == "YOU" ? gold : grey);
            }
            else fx.Text("NOBODY IN THE QUEUE", At(0, -0.09f), 0.020f, grey);
        }

        // ---------------------------------------------------------------- the spectator hub
        static void MiniSpot(int i, int n, out Vec3 at, out Quat q)
        {   // an arc in front of the viewer (the hub faces +X), up to 4 per row, rows step back and up
            int row = i / 4, col = i % 4, inRow = Mathf.Min(4, n - row * 4);
            float ang = inRow == 1 ? 0f : M.Deg2Rad(-40f + 80f * col / (inRow - 1));
            float rad = 1.35f + 0.9f * row;
            at = new Vec3(rad * Mathf.Cos(ang), 0.92f + 0.35f * row, rad * Mathf.Sin(ang));
            q = Quat.AxisAngle(new Vec3(0, 1, 0), -ang + M.kPi * 0.5f);   // long axis across the view
        }

        // hub pointing: the pointer's ray against each miniature's footprint; the trigger watches it
        public bool UpdateHub(PlayerInput @in, Pose aim, bool aimValid, Menu menu)
        {
            miniHover = -1;
            if (!a.spectating || a.watchIp != 0 || menu.open || !aimValid) { prevMiniTrig = true; return false; }
            var ms = net.Matches;
            Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
            float best = 1e9f;
            for (int i = 0; i < ms.Count; ++i)
            {
                MiniSpot(i, ms.Count, out Vec3 at, out _);
                Vec3 c = at + new Vec3(0, 0.12f, 0);
                Vec3 oc = c - o;
                float t = M.Dot(oc, d);
                if (t < 0) continue;
                float dist = M.Length(oc - d * t);
                if (dist < 0.38f && t < best) { best = t; miniHover = i; }
            }
            bool trig = @in.hand[a.settings.pointerHand].trigger > 0.7f;
            bool pressed = false;
            if (trig && !prevMiniTrig && miniHover >= 0)
            {
                a.watchIp = ms[miniHover].ip;
                Debug.Log($"[Spectate] watching {ms[miniHover].name}");
                pressed = true;
            }
            prevMiniTrig = trig;
            return pressed;
        }

        void BuildMini()
        {
            if (mini.Count > 0 || miniModels.Length == 0) return;
            foreach (var go in miniModels)
            {
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null) continue;
                    // relative to the arena (the models sit at the origin)
                    mini.Add((mf.sharedMesh, mr.sharedMaterials, mf.transform.localToWorldMatrix));
                }
            }
            miniBlock = new MaterialPropertyBlock();
        }

        static readonly int TintMul = Shader.PropertyToID("_TintMul"), UVOff = Shader.PropertyToID("_UVOff");

        void DrawMiniArena(Vec3 at, Quat q, float sc, bool hover, Camera cam)
        {
            BuildMini();
            Matrix4x4 place = Matrix4x4.TRS(NativeSpace.ToUnity(at), NativeSpace.ToUnity(q), new Vector3(sc, sc, sc));
            miniBlock.SetVector(TintMul, hover ? new Vector4(1.6f, 1.6f, 1.6f, 1) : Vector4.one);
            miniBlock.SetVector(UVOff, NativeSpace.WrapUV(new Vector2(0, -(float)(Time.timeAsDouble * 0.9 % 1.0))));   // (kept in [0, 1): native wrapUV)
            foreach (var (mesh, mats, local) in mini)
                for (int k = 0; k < mesh.subMeshCount && k < mats.Length; ++k)
                    Graphics.RenderMesh(new RenderParams(mats[k])
                    {
                        matProps = miniBlock, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = cam,
                        worldBounds = new Bounds(NativeSpace.ToUnity(at), new Vector3(2, 1, 2)),
                    }, mesh, k, place * local);
        }

        // one arena's match, small: the court, its players (lod avatars), balls, and the people in its room
        void DrawMiniMatch(FxRenderer fx, AvatarDraw av, Game g, Vec3 at, Quat q, float sc, bool hover, RoomInfo rmini, Camera cam)
        {
            Vec3 W(Vec3 p) => at + q.Rotate(p * sc);
            Matrix4x4 preN = HandPlacement.FromPose(new Pose(q, at)) * Matrix4x4.Scale(new Vector3(sc, sc, sc));   // native space
            Mat4 pre = Mat4.FromPose(new Pose(q, at), new Vec3(sc, sc, sc));
            DrawMiniArena(at, q, sc, hover, cam);
            uint me = net.Session;
            foreach (var m in rmini.members)
                if (m.id != me && m.@in.headTracked) fx.Sprite(W(m.@in.head.p), 0.035f, Red, a.viewHead.p, 0);
            foreach (var w in rmini.watchers)
                if (w.where == 1 && w.id != me) fx.Sprite(W(w.pos), 0.03f, Blue, a.viewHead.p, 0);
            float[][] teamCol = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            for (int p = 0; p < 2; ++p)
            {
                if (rmini.slot[p] == 0 && !(p == 1 && rmini.bot)) continue;   // empty tank slot
                PlayerState ps = g.pl[p];
                AvatarDraw.Look look = av.LookOf(ps.@in.avatar, p, teamCol);
                PlayerInput pin = av.Apart(ps.@in, ps.@in.avatar);   // (HANDS APART)
                if (ps.@in.head.p.y > 0.3f) av.DrawBody(pin, look, p, pre, lod: true, swingKey: 3000 + p);   // (its own, not the court's)
                Gauntlets(av, pin, look, preN, false);
                Ball b = ps.ball;
                if (b.state != BallState.Dead && b.pos.y > 0f)
                {
                    var c = new Vector4(teamCol[p][0] * 1.4f, teamCol[p][1] * 1.4f, teamCol[p][2] * 1.4f, 1);
                    fx.Sprite(W(b.pos), g.BallRadius(p) * sc * 5.0f, c, a.viewHead.p, 0);
                    fx.Sprite(W(b.pos), g.BallRadius(p) * sc * 1.8f, new Vector4(0.9f, 0.9f, 0.9f, 1), a.viewHead.p, 0);
                }
            }
        }

        // a miniature's line under its name, as native drawHub: the rules and score, FIREBALL BLITZ everyone's health, FOUR
        // CORNERS the four scores (free-for-all) or the teams'
        public static string HubLine(Game g)
        {
            string clock = Scoreboard.ClockLine(g);
            // (ROOMS AND THEIR OPTIONS, native d4cf079: the room and its match)
            if (g.tun.advanced)
                return $"{Menu.RoomTitle[Menu.RoomOf(g.tun)]} BEST OF {2 * g.tun.RoundsToWinNow() - 1}{(g.tun.shields ? "" : " HOTDOG")}   {g.pl[0].score}  -  {g.pl[1].score}   ROUND {Mathf.Max(1, g.round)}";
            if (g.Octagon)   // (FIREBALL BLITZ: everyone's health; 0.3.24, TASKS.md E51: its teams)
                return $"FIREBALL BLITZ{(g.TeamsNow() == Teams.ThreeVOne ? " 3 V 1" : g.TeamsNow() == Teams.TwoVTwo ? " 2 V 2" : "")}   {g.pl[0].health} - {g.pl[1].health} - {g.pl[2].health} - {g.pl[3].health}   {clock}";
            if (g.np == 4 && g.TeamsNow() == Teams.FFA)
                return $"FOUR CORNERS   {g.pl[0].score} - {g.pl[1].score} - {g.pl[2].score} - {g.pl[3].score}   {clock}";
            if (g.np == 4)
                return $"FOUR CORNERS {(g.tun.teamMode == Teams.ThreeVOne ? "3 V 1" : "2 V 2")}   {g.TeamScore(0)}  -  {g.TeamScore(1)}   {clock}";
            return $"{(g.tun.experimental ? "DIAMOND" : "SQUARE")}{(g.tun.shields ? "" : " HOTDOG")}   {g.pl[0].score}  -  {g.pl[1].score}   {clock}";
        }

        public void DrawHub(FxRenderer fx, AvatarDraw av, Pose aim, bool aimValid, Menu menu, Camera cam = null, bool desktop = false)
        {
            var ms = net.Matches;
            int n = ms.Count;
            const float sc = 1f / 17f;
            Vec3 eye = a.viewHead.p;
            Vector4 white = new Vector4(1, 1, 1, 1), grey = new Vector4(0.55f, 0.6f, 0.68f, 1), cyan = new Vector4(0.3f, 0.85f, 1.0f, 1);
            // the hub platform: a glowing disc in the dark arena
            fx.SpriteFlat(new Vec3(0, 0.004f, 0), 5.0f, new Vector4(0.03f, 0.09f, 0.14f, 1), new Vec3(0, 1, 0), 0);
            fx.SpriteFlat(new Vec3(0, 0.006f, 0), 5.4f, new Vector4(0.12f, 0.45f, 0.65f, 1), new Vec3(0, 1, 0), 1);
            for (int i = 0; i < n; ++i)
            {
                MiniSpot(i, n, out Vec3 at, out Quat q);
                bool hover = miniHover == i;
                // (the native pedestal, an opaque box under each miniature, is not drawn: the panels here draw after the arena)
                DrawMiniMatch(fx, av, ms[i].game, at, q, sc, hover, ms[i].room, cam);
                // its glow on the floor round the pedestal (native 0.3.16: drawn at the table top, the miniature's own
                // floor, it showed through the arena's glass, testers 2026-11-06)
                fx.SpriteFlat(new Vec3(at.x, 0.008f, at.z), 1.5f, new Vector4(hover ? 0.4f : 0.08f, hover ? 0.9f : 0.3f, hover ? 1.0f : 0.45f, 1), new Vec3(0, 1, 0), 1);
                Vec3 lf = M.Normalize(new Vec3(eye.x - at.x, 0, eye.z - at.z));
                Quat lq = Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(lf.x, lf.z));
                Game g = ms[i].game;
                fx.Text(ms[i].name.Length > 0 ? ms[i].name : "LIVE GAME", new Pose(lq, at + new Vec3(0, 0.46f, 0)), 0.05f, hover ? cyan : white);
                string line = HubLine(g);
                if (!net.Live(ms[i])) line = "WAITING FOR PLAYERS";   // (native 0.3.16: no game going there)
                fx.Text(line, new Pose(lq, at + new Vec3(0, 0.39f, 0)), 0.04f, grey);
            }
            {   // other spectators in the lobby: blue lights where they stand (each host lists them)
                var seen = new HashSet<uint>();
                foreach (var m in ms)
                    foreach (var w in m.room.watchers)
                    {
                        if (w.where != 0 || w.id == net.Session || !seen.Add(w.id)) continue;
                        fx.Sprite(w.pos, 0.09f, Blue, a.viewHead.p, 0);
                    }
            }
            Quat fq = Quat.AxisAngle(new Vec3(0, 1, 0), -M.kPi * 0.5f);   // facing a viewer looking +X
            if (!desktop) fx.Text("SPECTATOR HUB", new Pose(fq, new Vec3(2.4f, 2.1f, 0)), 0.14f, cyan);   // (the PC window: its own title line, native 0.3.16)
            if (desktop) { }
            else if (n == 0)
            {
                fx.Text("NO LIVE GAMES ON YOUR NETWORK YET", new Pose(fq, new Vec3(2.4f, 1.6f, 0)), 0.07f, white);
                fx.Text("WHEN A PLAYER HOSTS A GAME IT APPEARS HERE", new Pose(fq, new Vec3(2.4f, 1.45f, 0)), 0.05f, grey);
            }
            else fx.Text("POINT AT AN ARENA AND PULL THE TRIGGER TO WATCH IT", new Pose(fq, new Vec3(2.4f, 1.85f, 0)), 0.045f, grey);
            if (aimValid && !menu.open)
            {   // pointer laser in the hub
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                fx.Box(new Pose(aim.q, o + d * 1.0f), new Vec3(0.0025f, 0.0025f, 2.0f), new Vector4(0.12f, 0.45f, 0.8f, 1));
            }
        }
    }
}
