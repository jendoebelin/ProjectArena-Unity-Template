using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;
using EventType = ProjectArena.Sim.EventType;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The native match effects (main.cpp drawWorld, updateFx / addHitFx / drawHitFx, drawSpawnFx, the trail update),
    // drawn through FxRenderer in native positions: ball trails with spin tracers, electric arcs and the glowing core,
    // the dissolve burst at the back wall, the balls' reflections and light pools on the glossy floor, the spawn glow,
    // the plasma burst of a hit (and the wash in front of your eyes when you are hit), the winner's confetti.
    // fxLevel: the native hit-effects setting (0 full, 1 soft, 2 low; menus come with phase 4).
    public class MatchFx
    {
        public int fxLevel = 0;
        public bool noFloor;   // no glossy floor under the balls (the EXPERIMENTAL rooms)
        public bool reflect = true;   // FLOOR REFLECTIONS (TASKS.md E38): the balls mirrored in the glossy floor
        public BallFeel feel;  // BALL FEEL (TEST): the arcs and the inner glow where the ball is drawn, and as big (none: as it was)
        public readonly float[] hitFlash = new float[Teams.MaxPlayers];   // the body flashes when hit (emissive, decays)
        public readonly float[] strikeFlash = new float[Teams.MaxPlayers];

        class Trail
        {
            public readonly Vec3[] core = new Vec3[48], helixA = new Vec3[48], helixB = new Vec3[48];
            public int n;
            public int stale;   // frames the ball hasn't moved (a picture from the host only every few frames)
            public float spinAngle;
            public Vec3 burstPos;
            public float burst;   // dissolve burst at the back wall
        }

        class HitFx { public Vec3 pos, dir; public float t; public int team, victim; public uint seed; }

        // BALL MARKS (task C9, native 0.3.17 addMark / drawMarks, a community idea): where a ball bounces off the
        // floor, a wall or the glass, a ripple in the surface: a thin faint ring in the thrower's colour spreading out in
        // 0.9 s, hue-shifted rings just inside and outside it, and a faint haze that fades over 4 s
        class Mark { public Vec3 p, n; public float age, life = 4f, size; public readonly float[] rgb = { 1, 1, 1 }; }
        readonly List<Mark> marks = new List<Mark>();
        public int MarkCount => marks.Count;
        public int TrailCount(int p) => trail[p].n;            // (tests: a ball's trail, its points)
        public Vec3 TrailPoint(int p, int i) => trail[p].core[i];
        uint lowBounces;   // (HIT EFFECTS: LOW: the bounces counted, every other one marked)
        public Vec3 MarkAt(int i) => marks[i].p;
        public Vec3 MarkNormal(int i) => marks[i].n;
        public float MarkSize(int i) => marks[i].size;
        public float[] MarkRGB(int i) => marks[i].rgb;

        // the surface the ball bounced off (asked of the arena's own collision, the same on every machine: the event
        // carries only where the ball was), and a mark on it, sized by the ball's speed
        static readonly Vec3[] markDirs = { new Vec3(0, -1, 0), new Vec3(0, 1, 0), new Vec3(1, 0, 0), new Vec3(-1, 0, 0), new Vec3(0, 0, 1), new Vec3(0, 0, -1),
                                            new Vec3(0.7071f, 0, 0.7071f), new Vec3(-0.7071f, 0, 0.7071f), new Vec3(0.7071f, 0, -0.7071f), new Vec3(-0.7071f, 0, -0.7071f) };
        public void AddMark(Game g, Vec3 at, int player, float speed, float[][] sideRGB)
        {
            // (HIT EFFECTS: LOW: every other bounce; native 0.3.19, 2026-11-11: it used to skip whenever the count of
            // marks showing was odd, so after the first one every bounce was refused until it faded)
            if (fxLevel >= 2 && (lowBounces++ & 1) != 0) return;
            // (SweepArena tests a sphere at the end point against surfaces it is moving into: a sphere round the bounce
            // point nudged toward each direction finds the surfaces on that side; the nearest wins)
            const float R = 0.22f;
            float best = 1e9f;
            Vec3 bp = default, bn = default;
            foreach (Vec3 d in markDirs)
            {
                if (!g.SweepArena(at - d * 0.01f, at, R, out Vec3 hp, out Vec3 hn, out _)) continue;
                Vec3 surf = hp - hn * R;
                if (M.Length(surf - at) < best) { best = M.Length(surf - at); bp = surf; bn = hn; }
            }
            if (best > 1e8f || M.Length(bn) < 0.5f) return;
            if (M.Dot(bn, at - bp) < 0) bn = bn * -1f;   // (facing the ball's side)
            var m = new Mark { n = M.Normalize(bn) };
            m.p = bp + m.n * 0.006f;
            m.size = M.Clampf(0.20f + 0.016f * speed, 0.20f, 0.55f) * (fxLevel >= 1 ? 0.85f : 1f);
            float[] c = player >= 0 && sideRGB != null ? ExpArenaView.PlayerRGB(g, sideRGB, player) : null;
            for (int k = 0; k < 3; ++k) m.rgb[k] = c != null ? c[k] : 1f;
            if (marks.Count >= 48) marks.RemoveAt(0);
            marks.Add(m);
        }

        void DrawMarks(FxRenderer fx)
        {
            float soft = fxLevel >= 1 ? 0.7f : 1f;
            foreach (var m in marks)
            {
                float t = m.age / m.life;
                Vec3 at = m.p + m.n * 0.002f;
                float[] c = m.rgb;
                float i0 = 0.5f * c[0] + 0.5f * c[2], i1 = 0.5f * c[1] + 0.5f * c[0], i2 = 0.5f * c[2] + 0.5f * c[1];   // (the hue a little
                float o0 = 0.5f * c[0] + 0.5f * c[1], o1 = 0.5f * c[1] + 0.5f * c[2], o2 = 0.5f * c[2] + 0.5f * c[0];   // either way round)
                float haze = 0.10f * Mathf.Pow(1f - t, 1.5f) * soft * Mathf.Min(1f, m.age / 0.05f);
                fx.SpriteFlat(at, m.size * 0.55f, V((0.5f + 0.5f * c[0]) * haze, (0.5f + 0.5f * c[1]) * haze, (0.5f + 0.5f * c[2]) * haze), m.n, 0);
                float rt = m.age / 0.9f;   // the ripple: out to the mark's size in 0.9 s, fading as it goes
                if (rt < 1f)
                {
                    float e = 1f - rt * (2f - rt);   // (eases out)
                    float k = (1f - rt) * (1f - rt) * 0.55f * soft;
                    float R = m.size * (0.25f + 0.85f * (1f - e));
                    float ki = k * 0.45f;
                    fx.SpriteFlat(at, R, V(c[0] * k, c[1] * k, c[2] * k), m.n, 1);
                    fx.SpriteFlat(at, R * 0.93f, V(i0 * ki, i1 * ki, i2 * ki), m.n, 1);
                    fx.SpriteFlat(at, R * 1.07f, V(o0 * ki, o1 * ki, o2 * ki), m.n, 1);
                }
            }
        }

        readonly Trail[] trail = { new Trail(), new Trail(), new Trail(), new Trail() };   // (four players: four)
        readonly List<HitFx> hits = new List<HitFx>();
        readonly Vec3[] pts = new Vec3[20];
        uint fxSeed;
        float winAge = -1;
        float time;

        public float SpinAngle(int p) => trail[p].spinAngle;

        static uint FxHash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
        static float FxRnd(uint seed, uint k) => (FxHash(seed * 2654435761u + k * 40503u) >> 8) * (1f / 16777216f);
        static float EaseOut(float u) { u = M.Clampf(u, 0f, 1f); return 1f - (1f - u) * (1f - u) * (1f - u); }

        static Vector4 V(float r, float g, float b, float a = 1) => new Vector4(r, g, b, a);

        // once per frame, after the rules stepped: the game's events, then the trails (native updateFx + trail update)
        // sideRGB: the ends' colours (BALL MARKS take the thrower's, as native playerRGB)
        public void Update(Game g, float dt, float[][] sideRGB = null)
        {
            time += dt;
            foreach (var m in marks) m.age += dt;   // BALL MARKS fade out
            marks.RemoveAll(m => m.age > m.life);
            foreach (var e in g.events)
            {
                if (e.type == EventType.Bounce) AddMark(g, e.pos, e.player, e.value, sideRGB);
                if (e.type == EventType.HitPlayer && e.player >= 0 && e.player < g.np)
                {
                    hitFlash[e.player] = fxLevel == 0 ? 0.6f : fxLevel == 1 ? 0.3f : 0.1f;
                    if (hits.Count < 6)
                    {
                        int scorer = (int)e.value;
                        fxSeed = FxHash(fxSeed + 0x9e37u);
                        hits.Add(new HitFx
                        {
                            pos = e.pos, dir = e.player == 1 ? new Vec3(1, 0, 0) : new Vec3(-1, 0, 0),   // toward the victim's end
                            t = 0, team = scorer & 1, victim = e.player, seed = fxSeed,
                        });
                    }
                }
                if (e.type == EventType.Strike && e.player >= 0) strikeFlash[e.player] = 1.0f;
                if (e.type == EventType.Returned && e.player >= 0) { trail[e.player].burst = 0.45f; trail[e.player].burstPos = e.pos; trail[e.player].n = 0; }
            }
            for (int i = hits.Count - 1; i >= 0; --i) if ((hits[i].t += dt) > 2.2f) hits.RemoveAt(i);
            for (int p = 0; p < g.np; ++p)
            {
                hitFlash[p] = Mathf.Max(0f, hitFlash[p] - dt);
                Trail tr = trail[p];
                Ball b = g.pl[p].ball;
                strikeFlash[p] = Mathf.Max(0f, strikeFlash[p] - dt);
                tr.burst = Mathf.Max(0f, tr.burst - dt);
                bool rising = b.state == BallState.Spawning && b.pos.y > 0f;
                if (b.state != BallState.Flying && b.state != BallState.Returning && !rising) { tr.n = 0; tr.stale = 0; continue; }
                float sl = rising ? 10f : M.Length(b.spin);
                tr.spinAngle += sl * dt;
                Vec3 axis = rising ? new Vec3(0, 1, 0) : sl > 1e-3f ? b.spin / sl : new Vec3(0, 1, 0);
                Vec3 perp = M.Normalize(M.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)));
                float hr = g.BallRadius(p) * 1.3f;
                // (native 0.3.19: someone waiting or watching gets the host's picture 30 times a second, the headset draws
                // 72-90: the ball stood still for frames while its spiral turned on the spot, then jumped, drawn as arrows
                // (owner, 2026-11-12, in the queue). A point only when it has moved, the frames between filled along the way,
                // as smooth and as long as a player's)
                if (tr.n > 0 && M.Length(b.pos - tr.core[tr.n - 1]) < 1e-5f) { ++tr.stale; continue; }
                int steps = tr.n > 0 ? Mathf.Min(tr.stale + 1, 8) : 1;
                Vec3 from = tr.n > 0 ? tr.core[tr.n - 1] : b.pos;
                tr.stale = 0;
                for (int j = 1; j <= steps; ++j)
                {
                    Vec3 at = from + (b.pos - from) * ((float)j / steps);
                    Vec3 offA = Quat.AxisAngle(axis, tr.spinAngle - (steps - j) * sl * dt).Rotate(perp) * hr;
                    if (tr.n == 48)
                    {
                        for (int i = 1; i < 48; ++i) { tr.core[i - 1] = tr.core[i]; tr.helixA[i - 1] = tr.helixA[i]; tr.helixB[i - 1] = tr.helixB[i]; }
                        tr.n = 47;
                    }
                    tr.core[tr.n] = at;
                    tr.helixA[tr.n] = at + offA;
                    tr.helixB[tr.n] = at - offA;
                    tr.n++;
                }
            }
            if (g.match == MatchState.MatchSummary && g.matchWinner >= 0) winAge = winAge < 0 ? 0 : winAge + dt;
            else winAge = -1;
        }

        // the effects in the native drawing order; eye = the viewer (native viewHead), local = the viewer's player
        // (-1: watching), team: the ends' colours
        public void Draw(FxRenderer fx, Game g, Vec3 eye, int local, Vector3[] team)
        {
            if (marks.Count > 0) DrawMarks(fx);   // BALL MARKS where the balls bounced
            for (int p = 0; p < 2 && g.np == 2; ++p) DrawShotClock(fx, g, p, team[p]);
            // the winner's confetti: bursts in their team colour and gold (fewer with softer effects). Four players in teams:
            // over each player of the winning team (native 3fccaf9, review 2026-11-21: the winner is a team there, and the
            // confetti went over corner 0 or 1 by the team's number, a loser of 3 V 1 when THE ONE won)
            var winners = new List<int>();
            if (winAge >= 0 && winAge < 6.5f && g.match == MatchState.MatchSummary && g.matchWinner >= 0)
            {
                bool byTeam = g.np == 4 && g.TeamsNow() != Teams.FFA;
                for (int p = 0; p < g.np; ++p)
                    if (byTeam ? g.Team(p) == g.matchWinner && (!g.Octagon || ((g.startMask >> p) & 1) != 0) : p == g.matchWinner) winners.Add(p);
            }
            int all = fxLevel == 0 ? 90 : fxLevel == 1 ? 40 : 0;
            int n = winners.Count <= 1 ? all : 2 * all / (winners.Count + 1);   // (a team: a little less over each)
            foreach (int wn in winners)
            {
                Vec3 o = g.pl[wn].@in.head.p + new Vec3(0, 0.45f, 0);
                Vector3[] tcol = { new Vector3(0.25f, 0.75f, 1.0f), new Vector3(1.0f, 0.55f, 0.2f), default, default };
                if (g.np == 4) tcol[wn] = team[wn] * 0.83f + new Vector3(0.17f, 0.17f, 0.17f);   // (its colour, or its team's)
                for (int burst = 0; burst < 3; ++burst)
                {
                    float bt = winAge - burst * 1.6f;
                    if (bt < 0 || bt > 2.6f) continue;
                    for (int i = 0; i < n; ++i)
                    {
                        uint h = (uint)(i * 2654435761u) ^ (uint)(burst * 40503u + wn * 977u);
                        float Rnd() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xFFFF) / 65535f; }
                        float az = Rnd() * 6.2832f, el = 0.6f + Rnd() * 0.9f, sp = 2.0f + Rnd() * 2.2f;
                        var v = new Vec3(Mathf.Cos(el) * Mathf.Cos(az) * sp, Mathf.Sin(el) * sp, Mathf.Cos(el) * Mathf.Sin(az) * sp);
                        float drag = Mathf.Exp(-bt * 1.4f);
                        Vec3 p = o + v * ((1f - drag) / 1.4f) + new Vec3(0, -1.2f * bt * bt, 0);
                        bool goldBit = (i % 3) == 0;
                        float fade = Mathf.Max(0f, 1f - bt / 2.6f);
                        Vector4 c = goldBit ? V(1.0f, 0.85f, 0.3f, fade) : V(tcol[wn].x, tcol[wn].y, tcol[wn].z, fade);
                        fx.Sprite(p, 0.07f + 0.05f * Rnd(), c, eye, 0);
                    }
                }
            }
            // the glossy floor: the balls' reflections and the light pool under each ball (the classic court's: noFloor in
            // the EXPERIMENTAL rooms)
            for (int p = 0; p < g.np && !noFloor; ++p)
            {
                Ball b = g.pl[p].ball;
                if (b.state == BallState.Dead || b.pos.y < 0f) continue;
                var m = new Vec3(b.pos.x, -b.pos.y, b.pos.z);
                float k = 0.45f / (1f + 0.6f * b.pos.y);
                if (reflect)
                {
                    fx.Sprite(m, 0.35f * b.scale, V(team[p].x * k, team[p].y * k, team[p].z * k), eye, 0);
                    fx.Sprite(m, 0.12f * b.scale, V(0.5f * k, 0.5f * k, 0.5f * k), eye, 0);
                }
                float pk = 0.25f / (1f + b.pos.y * b.pos.y);
                fx.SpriteFlat(new Vec3(b.pos.x, 0.004f, b.pos.z), 0.9f + 0.4f * b.pos.y, V(team[p].x * pk, team[p].y * pk, team[p].z * pk), new Vec3(0, 1, 0), 0);
            }
            // the balls: trails with spin tracers, electric arcs, the glowing core
            for (int p = 0; p < g.np; ++p)
            {
                Ball b = g.pl[p].ball;
                bool show = b.state != BallState.Dead && (b.state != BallState.Spawning || b.pos.y > 0f);
                if (!show) continue;
                Trail tr = trail[p];
                if (tr.n > 1)
                {
                    fx.Ribbon(tr.core, tr.n, g.BallRadius(p) * 0.7f, V(team[p].x * 1.2f, team[p].y * 1.2f, team[p].z * 1.2f), eye);
                    if (M.Length(b.spin) > 3.0f || b.state == BallState.Spawning)
                    {   // twisting tracers: spin, or the spawn rise
                        fx.Ribbon(tr.helixA, tr.n, 0.012f, V(0.9f, 0.9f, 0.9f), eye);
                        fx.Ribbon(tr.helixB, tr.n, 0.012f, V(0.9f, 0.9f, 0.9f), eye);
                    }
                }
                float vis = feel != null ? feel.Vis : 1f;
                Vec3 bpos = feel != null ? feel.DrawnAt(p, b) : b.pos;
                if (b.state != BallState.Spawning)
                {   // electric arcs crackling over the plasma sphere: jagged great-circle segments, re-rolled ~18x/s (none on a
                    // harmless ball, on its way home after a foul, a short throw or getting stuck: native ac8dfff)
                    uint tick = (uint)(time * 18f);
                    float R0 = g.BallRadius(p) * 1.08f * vis;
                    bool harmless = b.state == BallState.Returning && b.scored;
                    int nArcs = harmless ? 0 : b.charged ? 3 : 1;
                    for (int k = 0; k < nArcs; ++k)
                    {
                        uint sd = FxHash(tick * 131u + (uint)p * 7919u + (uint)k * 104729u);
                        Vec3 ax = M.Normalize(new Vec3(FxRnd(sd, 1) - 0.5f, FxRnd(sd, 2) - 0.5f, FxRnd(sd, 3) - 0.5f) + new Vec3(1e-3f, 0, 0));
                        Vec3 u0 = M.Normalize(M.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)));
                        float start = FxRnd(sd, 4) * 6.2832f, span = 1.2f + 1.4f * FxRnd(sd, 5);
                        for (int i = 0; i < 12; ++i)
                        {
                            float t = i / 11f;
                            Vec3 d = Quat.AxisAngle(ax, start + span * t).Rotate(u0);
                            float jit = (i == 0 || i == 11) ? 0f : (FxRnd(sd, 10 + (uint)i) - 0.5f) * 0.35f;
                            Vec3 side = M.Normalize(M.Cross(ax, d));
                            pts[i] = bpos + (d * (1f + 0.12f * jit) + side * jit * 0.45f) * R0;
                        }
                        float flick = 0.55f + 0.45f * FxRnd(sd, 30);
                        fx.Ribbon(pts, 12, 0.0035f + 0.002f * flick,
                                  V(0.45f + 0.55f * team[p].x * flick, 0.45f + 0.55f * team[p].y * flick, 0.45f + 0.55f * team[p].z * flick), eye);
                    }
                }
                fx.Glow(bpos, g.BallRadius(p) * 0.55f * vis, V(team[p].x, team[p].y, team[p].z, b.charged ? 0.9f : 0.25f));
            }
            for (int p = 0; p < g.np; ++p)
                if (trail[p].burst > 0)
                {   // ball dissolving at the back wall
                    float k = trail[p].burst / 0.45f;
                    fx.Glow(trail[p].burstPos, 0.1f + (1 - k) * 0.6f, V(team[p].x * k * 2, team[p].y * k * 2, team[p].z * k * 2));
                }
            foreach (var h in hits) DrawHit(fx, g, h, eye, local);
            for (int p = 0; p < g.np; ++p) DrawSpawn(fx, g, p, team[p]);
        }

        // a plasma burst made of soft glow sprites and feathered ribbons, in the scorer's team colour (native drawHitFx)
        void DrawHit(FxRenderer fx, Game g, HitFx h, Vec3 eye, int local)
        {
            Vector3 tc = h.team == 0 ? new Vector3(0.1f, 0.7f, 1.0f) : new Vector3(1.0f, 0.4f, 0.1f);
            float t = h.t;
            float lvl = fxLevel == 0 ? 1f : fxLevel == 1 ? 0.55f : 0.25f;
            Vector4 Col(float white, float k) =>
                V((tc.x * (1 - white) + white) * k * lvl, (tc.y * (1 - white) + white) * k * lvl, (tc.z * (1 - white) + white) * k * lvl);
            Vec3 ax = h.dir;
            Vec3 s1 = M.Normalize(M.Cross(ax, new Vec3(0, 1, 0))), s2 = M.Cross(s1, ax);
            // 1. flash: white-hot core that blooms and cools to the team colour
            if (t < 0.45f)
            {
                float u = t / 0.45f;
                fx.Sprite(h.pos, 0.35f + 0.9f * EaseOut(t / 0.12f), Col(0.85f * (1 - u), 1.6f * (1 - u) * (1 - u)), eye, 0);
                fx.Sprite(h.pos, 0.18f, Col(1f, 1.2f * (1 - u) * (1 - u)), eye, 0);
            }
            // 2. shock ring rolling outward
            if (t < 0.7f)
            {
                float u = t / 0.7f;
                fx.Sprite(h.pos, 0.25f + 2.4f * EaseOut(u), Col(0.3f * (1 - u), 0.9f * (1 - u) * (1 - u)), eye, 1, FxRnd(h.seed, 7) * 6.28f);
            }
            // 3. plasma cloud: soft puffs that burst out, slow down (drag), swirl around the hit axis and fade
            for (uint k = 0; k < 28; ++k)
            {
                float life = 0.9f + 0.6f * FxRnd(h.seed, 10 + k);
                if (t > life) continue;
                float u = t / life;
                var r = new Vec3(FxRnd(h.seed, 40 + k) - 0.5f, FxRnd(h.seed, 70 + k) - 0.5f, FxRnd(h.seed, 100 + k) - 0.5f);
                Vec3 d = M.Normalize(M.Normalize(r + new Vec3(1e-3f, 0, 0)) + ax * 0.55f);
                float speed = 1.4f + 2.2f * FxRnd(h.seed, 130 + k);
                float dist = speed / 3f * (1f - Mathf.Exp(-3f * t));
                float swirl = ((k & 1) != 0 ? 1f : -1f) * (1.5f + 2f * FxRnd(h.seed, 160 + k)) * t;
                Vec3 off = d * dist;
                float along = M.Dot(off, ax);
                Vec3 radial = off - ax * along;
                Vec3 pos = h.pos + ax * along + Quat.AxisAngle(ax, swirl).Rotate(radial) + new Vec3(0, 0.12f * t * t, 0);
                float fade = Mathf.Min(1f, u * 12f) * Mathf.Pow(1f - u, 1.6f);
                fx.Sprite(pos, 0.12f + 0.5f * EaseOut(u), Col(0.25f * (1 - u), 0.55f * fade), eye, 0, swirl);
            }
            // 4. curling wisps: feathered ribbons that uncoil from the hit and dissolve tail-first
            for (int k = 0; k < 7; ++k)
            {
                float ang = k * 6.2832f / 7f + FxRnd(h.seed, 200 + (uint)k);
                Vec3 r0 = s1 * Mathf.Cos(ang) + s2 * Mathf.Sin(ang);
                float sgn = (k & 1) != 0 ? 1f : -1f;
                float head = EaseOut(t / 0.6f), tail = M.Clampf((t - 0.25f) / 0.9f, 0f, 1f);
                if (tail >= head - 0.02f) continue;
                for (int i = 0; i < 20; ++i)
                {
                    float sp = tail + (head - tail) * (i / 19f);
                    Vec3 radial = Quat.AxisAngle(ax, sgn * sp * 2.6f).Rotate(r0) * (0.15f + 0.85f * sp);
                    pts[i] = h.pos + radial + ax * (0.55f * sp) + new Vec3(0, 0.15f * sp * sp, 0);
                }
                float fade = M.Clampf(1f - t / 1.3f, 0f, 1f);
                fx.Ribbon(pts, 20, 0.035f, Col(0.4f * fade, 0.9f * fade), eye);
            }
            // 5. lingering sparks that drift up and twinkle out
            for (uint k = 0; k < 24; ++k)
            {
                float life = 1.2f + 0.9f * FxRnd(h.seed, 300 + k);
                if (t > life) continue;
                float u = t / life;
                var v = new Vec3(FxRnd(h.seed, 330 + k) - 0.5f, FxRnd(h.seed, 360 + k) * 0.6f, FxRnd(h.seed, 390 + k) - 0.5f);
                v = v * 1.6f + ax * 0.5f;
                Vec3 pos = h.pos + v * (1f - Mathf.Exp(-1.5f * t)) / 1.5f + new Vec3(0, 0.25f * t, 0);
                float tw = 0.65f + 0.35f * Mathf.Sin(18f * t + k * 1.7f);
                fx.Sprite(pos, 0.035f, Col(0.6f, 0.9f * (1 - u) * tw), eye, 0);
            }
            // 6. you were hit: a brief soft wash of the scorer's colour in front of your eyes
            if (h.victim == local && t < 0.6f)
            {
                float u = t / 0.6f;
                Pose hd = g.pl[local].@in.head;
                fx.Sprite(hd.p + hd.q.Rotate(new Vec3(0, 0, -0.25f)), 0.9f, Col(0.2f, 0.35f * (1 - u) * (1 - u)), hd.p, 0);
            }
        }

        // the SHOT CLOCK (native d51ab4e, owner 2026-11-03: as the original showed it, but simple): a ring round the
        // launchpad that builds while your ball sits in your end, full and red once it has run out (each throw of the
        // other player is then their strike); nothing while the clock is full. Two players only, as the clock itself.
        public static void DrawShotClock(FxRenderer fx, Game g, int p, Vector3 team)
        {
            float clock = g.pl[p].shotClock, full = Mathf.Max(0.1f, g.tun.shotClockSeconds);
            if (!g.tun.useShotClock || g.match != MatchState.InProgress || clock >= full - 0.05f) return;
            const int kSeg = 48;
            int lit = clock <= 0f ? kSeg : (int)Mathf.Ceil(kSeg * (1f - clock / full));
            const float R = 0.42f;
            float pulse = clock <= 0f ? 0.75f + 0.25f * Mathf.Sin((float)Time.timeAsDouble * 8f) : 1f;
            Vector4 col = clock <= 0f ? V(1.0f * pulse, 0.18f * pulse, 0.12f * pulse) : V(team.x * 0.9f, team.y * 0.9f, team.z * 0.9f);
            Vec3 c = g.tun.spawner[p] + new Vec3(0, 0.03f, 0);
            Vec3 face = g.Facing(p);
            float start = Mathf.Atan2(face.z, face.x);   // (it starts toward the court and goes round)
            for (int k = 0; k < lit; ++k)
            {
                float ang = start + 2f * M.kPi * (k + 0.5f) / kSeg;
                var at = c + new Vec3(R * Mathf.Cos(ang), 0, R * Mathf.Sin(ang));
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), -ang);   // (each piece along the circle)
                fx.Box(new Pose(q, at), new Vec3(0.011f, 0.003f, M.kPi * R / kSeg * 0.92f), col);
            }
        }

        // the spawn: a soft glow gathers on the platform while the ball is hidden, then fades as it floats up
        static void DrawSpawn(FxRenderer fx, Game g, int p, Vector3 tc)
        {
            Ball b = g.pl[p].ball;
            if (b.state != BallState.Spawning) return;
            // (tc: the player's colour; four in FOUR CORNERS)
            Vec3 bas = g.tun.spawner[p] + new Vec3(0, 0.035f, 0);
            float glow;
            if (b.pos.y < 0) glow = M.Clampf((b.pos.y + 2f * g.tun.spawnDepth) / g.tun.spawnDepth, 0f, 1f);   // gathering
            else glow = 1f - M.Clampf((b.pos.y - g.tun.spawnLauncherHeight) / Mathf.Max(0.05f, g.IdlePoint(p).y - g.tun.spawnLauncherHeight), 0f, 1f);
            glow = glow * glow * (3 - 2 * glow);
            fx.SpriteFlat(bas, 0.25f + 0.2f * glow, V(tc.x * 0.7f * glow, tc.y * 0.7f * glow, tc.z * 0.7f * glow), new Vec3(0, 1, 0), 0);
            fx.SpriteFlat(bas + new Vec3(0, 0.002f, 0), 0.1f,
                          V((tc.x * 0.5f + 0.5f) * 0.5f * glow, (tc.y * 0.5f + 0.5f) * 0.5f * glow, (tc.z * 0.5f + 0.5f) * 0.5f * glow), new Vec3(0, 1, 0), 0);
        }
    }
}
