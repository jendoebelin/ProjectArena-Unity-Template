// FIREBALL BLITZ's looks (task A5), as native main.cpp draws them (native a7230f8: drawFireballs, drawBlitzHelp,
// handleEvents' Knockout / RingClose / Fizzle, the fireDanger update): each fireball's flames (a white-hot core, a glow
// in its thrower's colour, a flickering tail, its light on the floor; red when it will hit you, with a red ring at your
// feet), the puffs as they fizzle out, everyone's health over their head (yours in front of you, at your waist), the
// knocked-out players' faint ghosts, who is out or "THE ROOM CLOSES IN", and the card in front of you while you wait:
// where a fireball hits and the ways out. The fireballs' own models (the ball on fire) are MatchView's.
using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;
using EventType = ProjectArena.Sim.EventType;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    public class BlitzView
    {
        public static readonly float[] Fire = { 1.0f, 0.42f, 0.08f };   // (a fireball's colour: the ball's tint, mixed with its thrower's)

        struct Puff { public Vec3 pos; public float t; public int player; }
        readonly List<Puff> puffs = new List<Puff>();
        public ushort danger;      // the fireballs (bit owner * 3 + k) that will hit you (red, and a red ring at your feet)
        public string msg = "";    // who is out, or the room closing in (the scoreboard shows it)
        public int msgP = -1;
        public float msgT;
        static readonly string[] kOut = { "BLUE IS OUT", "ORANGE IS OUT", "GREEN IS OUT", "VIOLET IS OUT" };

        static Vector4 V(float r, float g, float b, float a = 1) => new Vector4(r, g, b, a);

        // FIREBALL BLITZ's echoes (native 0.3.19, a player's idea, 2026-11-10): four smaller, fainter copies trailing each
        // fireball along the path it just took, 55 ms apart, from where this screen drew it (no rule or network change).
        // No core, floor light or warning on them, so the real one stands out. MatchView draws their models, Draw their glow
        const int EchoN = 32;
        class Echoes { public readonly Vec3[] p = new Vec3[EchoN]; public readonly float[] t = new float[EchoN]; public int n, head; }
        readonly Echoes[] echoes = new Echoes[Teams.MaxPlayers * Game.KMaxFire];

        // once a frame for each fireball place, before its echoes are asked for (f: null or not flying, none)
        public void TrackEcho(int p, int k, Ball f, float now)
        {
            var fe = echoes[p * Game.KMaxFire + k] ??= new Echoes();
            if (f == null || f.state != BallState.Flying) { fe.n = 0; return; }
            int last = (fe.head + EchoN - 1) % EchoN;
            if (fe.n > 0 && M.Length(f.pos - fe.p[last]) > 1.5f) fe.n = 0;   // (a new fireball in this place)
            if (fe.n == 0 || now - fe.t[last] >= 1f / 120f)
            {   // (once a frame, however often it's drawn)
                fe.p[fe.head] = f.pos; fe.t[fe.head] = now;
                fe.head = (fe.head + 1) % EchoN; fe.n = Mathf.Min(EchoN, fe.n + 1);
            }
        }

        // echo e (1..4): where the fireball was 55 ms x e ago, between two of the points, with its fade (0.6 .. 0.15) and
        // size (60 .. 24 %); false: too young, or bunched up behind a slow one. r: the fireball's radius
        public bool Echo(int p, int k, int e, Ball f, float r, float now, out Vec3 at, out float fade, out float size)
        {
            at = default;
            fade = 0.75f - 0.15f * e;
            size = 0.72f - 0.12f * e;
            var fe = echoes[p * Game.KMaxFire + k];
            if (fe == null) return false;
            float want = now - 0.055f * e;
            bool found = false;
            for (int i = 0; i + 1 < fe.n && !found; ++i)
            {
                int a1 = (fe.head + EchoN - 1 - i) % EchoN, a0 = (fe.head + EchoN - 2 - i) % EchoN;
                if (fe.t[a0] <= want && want <= fe.t[a1])
                {
                    float u = (want - fe.t[a0]) / Mathf.Max(1e-4f, fe.t[a1] - fe.t[a0]);
                    at = M.Lerp(fe.p[a0], fe.p[a1], u);
                    found = true;
                }
            }
            return found && M.Length(at - f.pos) >= r * 1.6f;
        }

        // the frame's events (native handleEvents); me: you are playing (not watching). Returns true for your knockout
        // (a long buzz on both hands)
        public bool Event(GameEvent e, int L, bool me)
        {
            switch (e.type)
            {
                case EventType.Knockout:
                {
                    bool mine = me && e.player == L;
                    msg = mine ? "YOU'RE OUT" : kOut[e.player & 3];
                    msgP = e.player; msgT = 2.0f;
                    return mine;
                }
                case EventType.RingClose: msg = "THE ROOM CLOSES IN"; msgP = -1; msgT = Mathf.Max(1f, e.value); break;
                case EventType.Fizzle: if (puffs.Count < 32) puffs.Add(new Puff { pos = e.pos, t = 0f, player = e.player }); break;
            }
            return false;
        }

        // once a frame: the message fades, the puffs age, the fireballs coming at you (you in the match, playing)
        public void Update(Game g, int L, bool playing, float dt)
        {
            msgT = Mathf.Max(0f, msgT - dt);
            for (int i = puffs.Count - 1; i >= 0; --i)
            {
                Puff u = puffs[i];
                u.t += dt;
                if (u.t > 0.6f) puffs.RemoveAt(i); else puffs[i] = u;
            }
            danger = 0;
            if (!g.Octagon || !playing || L < 0 || L >= g.np || !g.Alive(L) || g.match != MatchState.InProgress) return;
            for (int o = 0; o < g.np; ++o)
                for (int k = 0; k < Game.KMaxFire; ++k)
                {
                    Ball f = g.fire[o][k];
                    if (f.state == BallState.Flying && f.instigator != L && g.FireHitTime(f, g.pl[L].@in.head.p, 1.2f) >= 0)
                        danger |= (ushort)(1u << (o * Game.KMaxFire + k));
                }
        }

        // the flames round each fireball, the ring at your feet, the puffs and everyone's health (pass 1 of native
        // drawFireballs: the balls' own models are drawn by MatchView). heads: each player's head as drawn (eased)
        public void Draw(FxRenderer fx, Game g, int L, bool mineShown, Vector3[] teamCol, Pose[] heads, Vec3 eye, float now)
        {
            if (!g.Octagon) return;
            for (int p = 0; p < g.np; ++p)
                for (int k = 0; k < Game.KMaxFire; ++k)
                {
                    Ball f = g.fire[p][k];
                    if (f.state != BallState.Flying) continue;
                    Vector3 tc = teamCol[p & 3];
                    float r = g.tun.projectileRadius * f.scale;
                    // its flames: a white-hot core, an orange glow in its colour, a flickering tail behind it (coming at you: red)
                    bool red = ((danger >> (p * Game.KMaxFire + k)) & 1) != 0;
                    fx.Glow(f.pos, r * 0.6f, V(1.0f, red ? 0.5f : 0.9f, red ? 0.4f : 0.6f));
                    float pulse = 0.6f + 0.4f * Mathf.Sin(now * 18f);
                    fx.Sprite(f.pos, r * 6f, red ? V(1.0f * pulse, 0.08f, 0.05f) : V(0.5f + 0.5f * tc.x, 0.25f + 0.25f * tc.y, 0.05f + 0.2f * tc.z), eye, 0);
                    float sp = M.Length(f.vel);
                    Vec3 back = sp > 1e-3f ? -f.vel / sp : default;
                    for (int i = 1; i <= 6; ++i)
                    {
                        float flick = 0.8f + 0.2f * Mathf.Sin(now * 31f + i * 1.7f + p * 3.1f + k);
                        float fade = (1f - i / 7f) * flick;
                        fx.Sprite(f.pos + back * (0.07f * i), r * (4.5f - 0.5f * i), V(1.0f * fade, (0.45f + 0.2f * tc.y) * fade, 0.08f * fade), eye, 0);
                    }
                    fx.SpriteFlat(new Vec3(f.pos.x, 0.005f, f.pos.z), 0.6f + 0.4f * f.pos.y, V(0.35f, 0.14f, 0.03f), new Vec3(0, 1, 0), 0);   // (its light on the floor)
                    for (int e = 1; e <= 4; ++e)   // its echoes' glow (their models: MatchView)
                        if (Echo(p, k, e, f, r, now, out Vec3 at, out float fade, out float size))
                            fx.Sprite(at, r * 6f * size, V((0.5f + 0.5f * tc.x) * fade, (0.25f + 0.25f * tc.y) * fade, (0.05f + 0.2f * tc.z) * fade), eye, 0);
                }
            if (danger != 0 && L >= 0 && L < g.np)
            {   // one is coming at you: a red ring round your feet
                Vec3 hd = g.pl[L].@in.head.p;
                float pulse = 0.6f + 0.4f * Mathf.Sin(now * 18f);
                fx.SpriteFlat(new Vec3(hd.x, 0.012f, hd.z), 1.1f, V(0.9f * pulse, 0.08f, 0.05f), new Vec3(0, 1, 0), 1);
            }
            foreach (Puff u in puffs)
            {   // a fireball fizzling out: a puff of sparks and smoke, rising
                float kk = 1f - u.t / 0.6f;
                if (kk <= 0) continue;
                for (int i = 0; i < 6; ++i)
                {
                    float t = i * 1.047f;
                    var d = new Vec3(Mathf.Cos(t) * 0.25f * u.t / 0.6f, 0.3f * u.t, Mathf.Sin(t) * 0.25f * u.t / 0.6f);
                    fx.Sprite(u.pos + d, 0.12f * kk + 0.05f, V(1.0f * kk, 0.5f * kk, 0.12f * kk), eye, 0);
                }
            }
            // health: a pip a hit, over everyone's head (turned to you); yours in front of you at your waist
            for (int p = 0; p < g.np; ++p)
            {
                if (!g.Alive(p) || (g.match == MatchState.WaitingForPlayers && !g.pl[p].@in.headTracked)) continue;
                int maxH = g.MaxHealth(p);   // (3 v 1: THE ONE's three times HEALTH; TASKS.md E51)
                bool mine = p == L && mineShown;
                Vec3 head = mine ? g.pl[p].@in.head.p : heads[p].p;
                Vec3 at = head + new Vec3(0, 0.40f, 0);
                Vec3 to = eye - at;
                if (mine)
                {
                    Vec3 fw = g.Facing(p);
                    at = new Vec3(head.x, Mathf.Max(0.6f, head.y - 0.75f), head.z) + fw * 0.55f;
                    to = head - at;
                }
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(to.x, to.z)) * Quat.AxisAngle(new Vec3(1, 0, 0), mine ? -0.6f : 0f);
                float pip = Mathf.Min(0.07f, 0.6f / (maxH * 1.35f)), gap = pip * 1.35f;   // (at most 60 cm across: THE ONE has 15)
                Vector3 tc = teamCol[p & 3];
                fx.Panel(new Pose(q, at - q.Rotate(new Vec3(0, 0, 0.004f))), gap * maxH + 0.04f, pip + 0.04f, V(0.01f, 0.02f, 0.04f, 0.7f));
                for (int i = 0; i < maxH; ++i)
                {
                    bool full = i < g.pl[p].health;
                    Vector4 c = full ? V(tc.x * 0.7f + 0.3f, tc.y * 0.7f + 0.3f, tc.z * 0.7f + 0.3f, 0.95f) : V(0.12f, 0.12f, 0.14f, 0.6f);
                    fx.Panel(new Pose(q, at + q.Rotate(new Vec3((i - 0.5f * (maxH - 1)) * gap, 0, 0))), pip, pip, c);
                }
            }
        }

        // a knocked-out player (not you, not a bot): a faint ghost where they watch from, OUT over it
        public static void Ghost(FxRenderer fx, Vector3 tc, Vec3 head, Vec3 eye)
        {
            for (int k = 0; k < 4; ++k) fx.Sprite(head - new Vec3(0, 0.4f * k, 0), 0.45f - 0.05f * k, V(tc.x * 0.25f, tc.y * 0.25f, tc.z * 0.25f), eye, 0);
            Vec3 to = eye - head;
            fx.Text("OUT", new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(-to.x, -to.z) + M.kPi), head + new Vec3(0, 0.35f, 0)), 0.10f,
                    V(tc.x * 0.5f + 0.3f, tc.y * 0.5f + 0.3f, tc.z * 0.5f + 0.3f, 0.8f));
        }

        // FIREBALL BLITZ's card, in front of your waiting spot while you wait and count down (owner, 2026-11-02: "where on
        // the body can you get hit, and how to avoid it?"): a glowing figure of where a fireball hits, and the ways out
        public static void DrawHelp(FxRenderer fx, Game g, Pose spotPose, double now)
        {
            Vec3 f = spotPose.q.Rotate(new Vec3(0, 0, -1)), right = new Vec3(-f.z, 0, f.x);
            Quat q = ExpArenaView.YawToward(f);   // (facing you)
            Vec3 c = spotPose.p + f * 1.3f + new Vec3(0, 1.25f, 0);
            fx.Panel(new Pose(q, c + q.Rotate(new Vec3(0, 0, -0.01f))), 1.30f, 0.80f, V(0.004f, 0.008f, 0.018f, 0.85f));
            fx.Panel(new Pose(q, c + q.Rotate(new Vec3(0, 0.40f, -0.005f))), 1.30f, 0.008f, V(0.3f, 0.85f, 1.0f, 0.9f));
            void Txt(string t, float x, float y, float h, Vector4 col) => fx.Text(t, new Pose(q, c + q.Rotate(new Vec3(x, y, 0.004f))), h, col);
            Vector4 cyan = V(0.3f, 0.85f, 1.0f), white = V(1, 1, 1), grey = V(0.65f, 0.7f, 0.78f), red = V(1.0f, 0.35f, 0.25f);
            Txt("HOW NOT TO GET HIT", 0, 0.31f, 0.06f, cyan);
            // the figure: head and body in red (a hit there counts), the legs dim, and a fireball hopping toward it
            Vec3 fig = c + q.Rotate(new Vec3(-0.42f, 0.02f, 0.02f));
            Vector4 zone = V(0.45f, 0.06f, 0.04f);
            fx.Glow(fig + new Vec3(0, 0.20f, 0), 0.038f, zone);
            for (int i = 0; i <= 6; ++i) fx.Glow(fig + new Vec3(0, 0.13f - 0.036f * i, 0), i == 6 ? 0.04f : 0.034f, zone);   // (to the hips)
            Vector4 legs = V(0.10f, 0.12f, 0.16f);   // (the legs, dim: a hit there doesn't count)
            for (int i = 0; i <= 7; ++i)
                for (int sd = -1; sd <= 1; sd += 2) fx.Glow(fig + new Vec3(0, -0.08f - 0.026f * i, 0) + right * (0.022f * sd), 0.014f, legs);
            float t = (float)(now % 1.6) / 1.6f;   // (a fireball hops in from the side, up to the waist)
            Vec3 ball = fig + right * (-0.22f + 0.17f * t) + new Vec3(0, -0.27f + 0.20f * Mathf.Abs(Mathf.Sin(t * 6.28f)), 0);
            fx.Glow(ball, 0.016f, V(1.0f, 0.5f, 0.1f));
            Txt("HIPS AND UP", -0.42f, -0.33f, 0.026f, red);
            const float x0 = 0.17f;   // the ways out
            Txt("HITS COUNT FROM YOUR HIPS UP", x0, 0.19f, 0.032f, white);
            Txt("STEP ASIDE, OR DUCK AS IT RISES", x0, 0.09f, 0.032f, white);
            Txt("PUNCH IT AWAY: YOUR KNUCKLE GUARDS", x0, 0.0f, 0.032f, white);
            if (g.tun.shields) Txt("OR BLOCK IT WITH YOUR SHIELD", x0, -0.08f, 0.032f, white);
            Txt("A FIREBALL TURNS RED, AND A RED RING", x0, g.tun.shields ? -0.18f : -0.12f, 0.026f, red);
            Txt("SHOWS AT YOUR FEET, WHEN IT WILL HIT YOU", x0, g.tun.shields ? -0.23f : -0.17f, 0.026f, red);
            Txt("IT FIZZLES OUT AT ITS 4TH BOUNCE", x0, -0.31f, 0.024f, grey);
        }
    }
}
