// The EXPERIMENTAL rooms' looks (task C3, A5), as native main.cpp draws them (native a7230f8: drawPlatforms,
// drawDiamondArena, drawExpArena, drawOctArena): DIAMOND, the court as a glass tube rolled 45 degrees; CORNERS / FOUR
// CORNERS, glass walls in a diamond round the court with a neon grid, the corners behind the players lit in their colours,
// the room leaning toward whoever leads, and (rooms for four) the waiting decks outside the glass. Both stand the players
// on floating platforms. FIREBALL BLITZ: the octagon, its floor level with yours, closing in as players are knocked out.
// The walls, lines and glows go through FxRenderer; the platforms are solid discs (Native.shader). FIREBALL BLITZ's
// fireballs, health and card: BlitzView.
using System;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    public class ExpArenaView
    {
        // FOUR CORNERS' four colours (native kFourRGB): free-for-all and FIREBALL BLITZ
        public static readonly float[][] kFourRGB =
            { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f }, new[] { 0.25f, 0.95f, 0.35f }, new[] { 0.75f, 0.4f, 1.0f } };

        // each player's colour (native playerRGB): two players their end's (a country's with NATIONS); FOUR CORNERS
        // free-for-all four colours, teams their team's
        public static float[] PlayerRGB(Game g, float[][] sideRGB, int p)
        {
            if (g.np == 2) return sideRGB[p & 1];
            return g.TeamsNow() == Teams.FFA ? kFourRGB[p & 3] : sideRGB[g.Team(p) & 1];   // (FIREBALL BLITZ in teams too: 0.3.24, TASKS.md E51)
        }

        // a team's colour (native teamRGB): the same as its players'
        public static float[] TeamRGB(Game g, float[][] sideRGB, int t) =>
            g.np == 4 && g.TeamsNow() == Teams.FFA ? kFourRGB[t & 3] : sideRGB[t & 1];

        public static bool CornersRoom(Game g) => g.arenaKind == Arena.Corners || g.arenaKind == Arena.FourCorners;
        public static bool Experimental(Game g) => g.arenaKind == Arena.Diamond || CornersRoom(g) || g.Octagon;

        public static Quat YawToward(Vec3 f) => Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z));   // -Z along f

        // FIREBALL BLITZ: its waiting spots in a row outside the glass behind the wall between places 0 and 2 (its floor is
        // level with the stadium's walkway); 4-7 the row across, the dugout during a match (native octWaiting)
        static Pose OctWaiting(int spot)
        {
            Vec3 outw = M.Normalize(new Vec3(-1, 0, -1)), along = M.Normalize(new Vec3(1, 0, -1));
            float sd = (spot & 4) != 0 ? -1f : 1f;
            return new Pose(YawToward(-outw * sd), outw * (9.2f * sd) + along * (sd * 1.5f * ((spot & 3) - 1.5f)));
        }

        // a waiting spot (its floor point) and the way it faces (native holdingPose): CORNERS' room fills the classic
        // holding area, so its spots stand outside the room's glass, along the wall between corners 0 and 2, facing in
        // (4-7: the deck on the other side)
        public static Pose HoldingPose(Game g, int spot)
        {
            if (g.Octagon) return OctWaiting(spot);
            if (CornersRoom(g))
            {
                var c0 = new Vec3(-ExpArena.kCorner, 0, 0); var c1 = new Vec3(0, 0, -ExpArena.kCorner);
                Vec3 inw = M.Normalize(new Vec3(1, 0, 1));
                float sd = (spot & 4) != 0 ? -1f : 1f;
                return new Pose(YawToward(inw * sd), (M.Lerp(c0, c1, 0.2f + 0.2f * (spot & 3)) - inw * 0.9f) * sd);
            }
            return new Pose(Quat.Identity, ArenaSession.HoldingSpot(spot));
        }

        public static Vec3 HoldingFwd(Game g, int spot) => CornersRoom(g) || g.Octagon ? HoldingPose(g, spot).q.Rotate(new Vec3(0, 0, -1)) : new Vec3(0, 0, 1);

        // the spectator's floating gallery seat at the arena it watches (native spectatorSeat: CORNERS outside the room,
        // by a side wall)
        public static Pose SpectatorSeat(Game g)
        {
            if (g.Octagon) return new Pose(YawToward(M.Normalize(new Vec3(-1, 0, 1))), new Vec3(6.9f, 1.0f, -6.9f));
            if (CornersRoom(g)) return new Pose(YawToward(M.Normalize(new Vec3(1, 0, -1))), new Vec3(-5.6f, 1.0f, 5.6f));
            return new Pose(Quat.Identity, new Vec3(0, 1.0f, 5.4f));
        }

        // where the stadium round the room stands (native drawWorld's `outside`): CORNERS reaches 9.5 m to the sides, so
        // the stadium is widened to stand round it and lowered below the room's floor (FIREBALL BLITZ: as wide, its floor
        // level with yours)
        public static void Outside(Game g, out Vec3 at, out Vec3 scale)
        {
            if (CornersRoom(g)) { at = new Vec3(0, ExpArena.kFloor - 0.4f, 0); scale = new Vec3(1.35f, 1f, 1.35f); }
            else if (g.Octagon) { at = new Vec3(0, -0.4f, 0); scale = new Vec3(1.35f, 1f, 1.35f); }
            else { at = default; scale = new Vec3(1, 1, 1); }
        }

        readonly Transform[] plat = new Transform[Teams.MaxPlayers];
        readonly Transform meetDisc;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintMul = Shader.PropertyToID("_TintMul"), EmissiveAdd = Shader.PropertyToID("_EmissiveAdd");

        // nativeShader: Shaders/Native.shader (the models' own); parent: where the platforms hang
        public ExpArenaView(Shader nativeShader, Transform parent)
        {
            if (nativeShader == null) return;
            var mat = new Material(nativeShader) { name = "exp platform" };
            mat.SetVector("_Tint", new Vector4(1, 1, 1, 1));
            mat.SetFloat("_LitOnly", 1f);
            mat.EnableKeyword("_PA_LIT");
            mat.SetFloat("_SrcBlend", (float)BlendMode.One); mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_SrcBlendA", (float)BlendMode.One); mat.SetFloat("_DstBlendA", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            mat.renderQueue = (int)RenderQueue.Geometry;
            Mesh cyl = CylinderMesh();
            Transform Disc(string n)
            {
                var go = new GameObject(n);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = cyl;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.SetActive(false);
                return go.transform;
            }
            for (int p = 0; p < plat.Length; ++p) plat[p] = Disc($"exp platform {p}");
            meetDisc = Disc("exp meeting platform");
        }

        // native Renderer's cylinder: radius 1, y from -1 to 0 (its top), capped
        static Mesh CylinderMesh()
        {
            const int n = 48;
            var v = new System.Collections.Generic.List<Vector3>();
            var nr = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= n; ++i)
            {   // the side
                float a = 2f * Mathf.PI * i / n;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d); nr.Add(d);
                v.Add(d + Vector3.down); nr.Add(d);
            }
            for (int i = 0; i < n; ++i) { int k = 2 * i; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            for (int cap = 0; cap < 2; ++cap)
            {   // the top (y 0) and the bottom (y -1)
                float y = cap == 0 ? 0f : -1f;
                Vector3 up = cap == 0 ? Vector3.up : Vector3.down;
                int c = v.Count;
                v.Add(new Vector3(0, y, 0)); nr.Add(up);
                for (int i = 0; i <= n; ++i)
                {
                    float a = 2f * Mathf.PI * i / n;
                    v.Add(new Vector3(Mathf.Cos(a), y, Mathf.Sin(a))); nr.Add(up);
                }
                for (int i = 0; i < n; ++i)
                    if (cap == 0) t.AddRange(new[] { c, c + 2 + i, c + 1 + i });
                    else t.AddRange(new[] { c, c + 1 + i, c + 2 + i });
            }
            var m = new Mesh { name = "exp cylinder" };
            m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(t, 0);
            var uv = new Vector2[v.Count];
            m.uv = uv; m.uv2 = uv;
            m.RecalculateBounds();
            return m;
        }

        public void Hide()
        {
            foreach (var t in plat) if (t != null) t.gameObject.SetActive(false);
            if (meetDisc != null) meetDisc.gameObject.SetActive(false);
        }

        void Disc(Transform t, Vec3 at, float r, float thick, Vector3 mul, Vector3 add)
        {
            if (t == null) return;
            t.gameObject.SetActive(true);
            t.SetPositionAndRotation(NativeSpace.ToUnity(at), Quaternion.identity);
            t.localScale = new Vector3(r, thick, r);
            mpb.SetVector(TintMul, new Vector4(mul.x, mul.y, mul.z, 1));
            mpb.SetVector(EmissiveAdd, add);
            t.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }

        static Vector4 C(float[] c, float k, float a = 1) => new Vector4(c[0] * k, c[1] * k, c[2] * k, a);

        // the floating platforms (and the fist bump's, in the centre): the solid discs, then their lines, rims and glow
        void Platforms(FxRenderer fx, Game g, float[][] sideRGB, Vec3 eye, float now, float belowY, bool meeting)
        {
            float R = ExpArena.kPlatformR, T = ExpArena.kPlatformThick;
            if (meeting) Disc(meetDisc, new Vec3(0, 0, 0), 1.4f, T, new Vector3(0.30f, 0.30f, 0.30f), default);
            else if (meetDisc != null) meetDisc.gameObject.SetActive(false);
            for (int p = 0; p < plat.Length; ++p)
            {
                if (p >= g.np) { if (plat[p] != null) plat[p].gameObject.SetActive(false); continue; }
                float[] pc = PlayerRGB(g, sideRGB, p);   // a solid slab you stand on, steel with your colour's glow
                Disc(plat[p], g.tun.start[p], R, T, new Vector3(0.30f + 0.12f * pc[0], 0.30f + 0.12f * pc[1], 0.30f + 0.12f * pc[2]),
                     new Vector3(0.05f * pc[0], 0.05f * pc[1], 0.05f * pc[2]));
            }
            var seg = new Vec3[2];
            for (int p = 0; p < g.np; ++p)
            {   // its top: rings and lines of light, so it reads as a floor to stand on
                float px = g.tun.start[p].x, pz = g.tun.start[p].z;
                Vector4 lineC = C(PlayerRGB(g, sideRGB, p), 0.45f);
                for (int k = -2; k <= 2; ++k)
                {
                    float off = 0.4f * k, half = MathF.Sqrt(MathF.Max(0f, R * R - off * off)) - 0.05f;
                    seg[0] = new Vec3(px + off, 0.006f, pz - half); seg[1] = new Vec3(px + off, 0.006f, pz + half);
                    fx.Ribbon(seg, 2, 0.006f, lineC, eye, false);
                    seg[0] = new Vec3(px - half, 0.006f, pz + off); seg[1] = new Vec3(px + half, 0.006f, pz + off);
                    fx.Ribbon(seg, 2, 0.006f, lineC, eye, false);
                }
            }
            if (meeting)
                for (int p = 0; p < 2; ++p) fx.SpriteFlat(new Vec3(p != 0 ? 0.02f : -0.02f, 0.008f, 0), 2.9f, C(sideRGB[p], 0.8f), new Vec3(0, 1, 0), 1);
            for (int p = 0; p < g.np; ++p)
            {   // a glowing rim in the player's colour, a soft glow underneath (it floats)
                Vec3 c = g.tun.start[p];
                float[] pc = PlayerRGB(g, sideRGB, p);
                float pulse = 0.85f + 0.15f * MathF.Sin(now * 2f + p * 3f);
                fx.SpriteFlat(c + new Vec3(0, 0.008f, 0), 2f * R + 0.12f, C(pc, pulse * 0.6f), new Vec3(0, 1, 0), 1);
                fx.SpriteFlat(c + new Vec3(0, -T - 0.02f, 0), 3.4f, C(pc, 0.25f), new Vec3(0, -1, 0), 0);
                fx.SpriteFlat(new Vec3(c.x, belowY + 0.02f, c.z), 2.6f, C(pc, 0.25f), new Vec3(0, 1, 0), 0);   // its light on what is below
            }
        }

        // DIAMOND (the owner's drawing 2026-10-29): the court as a glass tube rolled 45 degrees, its end at each end (its
        // player's back wall, in its colour), a neon ring every metre along the court and the foul lines in the players'
        // colours (no foul zone here: the line recharges the ball), the floating platforms
        public void DrawDiamond(FxRenderer fx, Game g, float[][] sideRGB, Vec3 eye, float now, bool meeting, float glassMul = 1f)
        {
            float cy = ExpArena.kRollCY, X = ExpArena.kRollLen, len = 2f * X;
            var v = new Vec3[4];
            ExpArena.DiamondEnd(v);
            Platforms(fx, g, sideRGB, eye, now, cy - ExpArena.kRollReach, meeting);
            var glass = new Vector4(0.02f, 0.06f, 0.11f, 0.22f * glassMul);
            for (int k = 0; k < 4; ++k)
            {   // the four long walls (two 4.96 m across, two 3.88 m)
                Vec3 m = (v[k] + v[(k + 1) % 4]) * 0.5f, e = v[(k + 1) % 4] - v[k];
                float wide = M.Length(e);
                fx.Wall(m, new Vec3(1, 0, 0), e / wide, len, wide, glass);
            }
            for (int p = 0; p < 2; ++p)
            {   // the ends: the back walls
                var o = new Vec3(p != 0 ? X : -X, 0, 0);
                Vec3 ax = v[0] - v[1], ay = v[0] - v[3];
                fx.Wall((v[0] + v[2]) * 0.5f + o, M.Normalize(ax), M.Normalize(ay), M.Length(ax), M.Length(ay), C(sideRGB[p], 0.4f, 0.30f));
            }
            var grid = new Vector4(0.06f, 0.32f, 0.55f, 1); var bright = new Vector4(0.25f, 0.8f, 1.0f, 1);
            var ln = new Vec3[2];
            for (int k = 0; k < 4; ++k)
            {   // the four long edges
                ln[0] = v[k] + new Vec3(-X, 0, 0); ln[1] = v[k] + new Vec3(X, 0, 0);
                fx.Ribbon(ln, 2, 0.03f, bright, eye, false);
            }
            var ring = new Vec3[5];
            for (float x = -X; x <= X + 0.01f; x += 1f)
            {   // a diamond ring every metre
                bool end = MathF.Abs(x) > X - 0.01f;
                var o = new Vec3(x, 0, 0);
                for (int k = 0; k < 5; ++k) ring[k] = v[k & 3] + o;
                fx.Ribbon(ring, 5, end ? 0.035f : 0.010f, end ? bright : grid, eye, false);
            }
            for (int p = 0; p < 2; ++p)
            {   // the foul lines, round the tube in each player's colour
                var o = new Vec3(p != 0 ? 4.73f : -4.73f, 0, 0);
                for (int k = 0; k < 5; ++k) ring[k] = v[k & 3] + o;
                fx.Ribbon(ring, 5, 0.03f, C(sideRGB[p], 1f), eye, false);
            }
        }

        // CORNERS / FOUR CORNERS: glass walls in a diamond round the court with a neon grid on them and on the floor below,
        // the corners behind the players (their back walls) lit in their colours, the floating platforms. FOUR CORNERS
        // (0.3.12): the room leans toward whoever leads: their corners pulse brighter, the grid takes on their colour, and
        // a new leader washes the whole room in it for a moment (leadFlash: seconds left of the lead change's cue)
        public void DrawCorners(FxRenderer fx, Game g, float[][] sideRGB, Vec3 eye, float now, bool meeting, float leadFlash,
                                float glassMul = 1f, float floorMul = 1f)
        {
            float K = ExpArena.kCorner, F = ExpArena.kFloor, Ce = ExpArena.kCeiling;
            int lead = g.np == 4 && g.match != MatchState.WaitingForPlayers ? g.Leader() : -1;
            float[] lc = lead >= 0 ? TeamRGB(g, sideRGB, lead) : null;
            float flash = lead >= 0 ? MathF.Min(1f, leadFlash / 1.5f) : 0f;   // (held, then fading over the last 1.5 s)
            float pulse = 0.5f + 0.5f * MathF.Sin(now * 3.0f);
            Vec3[] c = { new Vec3(-K, 0, 0), new Vec3(0, 0, K), new Vec3(K, 0, 0), new Vec3(0, 0, -K) };
            float side = M.Length(c[1] - c[0]), midY = 0.5f * (F + Ce), tall = Ce - F;
            Platforms(fx, g, sideRGB, eye, now, F, meeting);
            // the glass: four walls, the floor below (nearly solid, so it reads as a floor) and the ceiling
            var glass = new Vector4(0.02f, 0.06f, 0.11f, 0.20f * glassMul);
            var floorC = new Vector4(0.006f, 0.014f, 0.03f, 0.92f * floorMul);
            var ceil = new Vector4(0.012f, 0.03f, 0.06f, 0.35f * glassMul);
            var up = new Vec3(0, 1, 0);
            for (int w = 0; w < 4; ++w)
            {
                Vec3 d = M.Normalize(c[(w + 1) % 4] - c[w]), m = (c[w] + c[(w + 1) % 4]) * 0.5f;
                fx.Wall(m + new Vec3(0, midY, 0), d, up, side, tall, glass);
            }
            Vec3 fa = M.Normalize(c[1] - c[0]), fb = M.Normalize(c[3] - c[0]);   // (the floor's and the ceiling's edges)
            fx.Wall(new Vec3(0, F, 0), fa, fb, side, side, floorC);
            fx.Wall(new Vec3(0, Ce, 0), fa, fb, side, side, ceil);
            var ln = new Vec3[2];
            int[] cornerOf = { 0, 2, 3, 1 };   // (players 2 and 3: the corners at z = -9.5 and +9.5)
            for (int p = 0; p < g.np; ++p)
            {   // the back walls: the corner behind each player, in its colour
                int ci = cornerOf[p];
                float[] pc = PlayerRGB(g, sideRGB, p);
                bool leads = lead >= 0 && g.Team(p) == lead;
                Vector4 tint = C(pc, 0.35f, leads ? 0.26f + 0.12f * pulse + 0.15f * flash : 0.22f);
                for (int nb = -1; nb <= 1; nb += 2)
                {   // the two walls meeting in that corner, as far as the back wall goes
                    Vec3 o = c[(ci + nb + 4) % 4], d = M.Normalize(o - c[ci]);
                    float len = (K - ExpArena.kBackX) / MathF.Max(MathF.Abs(d.x), MathF.Abs(d.z));
                    Vec3 m = c[ci] + d * (0.5f * len);
                    fx.Wall(m + new Vec3(0, midY, 0) - M.Normalize(new Vec3(m.x, 0, m.z)) * 0.01f, d, up, len, tall, tint);
                    Vec3 e = c[ci] + d * len;
                    ln[0] = e + new Vec3(0, F, 0); ln[1] = e + new Vec3(0, Ce, 0);
                    fx.Ribbon(ln, 2, leads ? 0.035f + 0.015f * pulse : 0.025f, C(pc, 1f), eye, false);
                }
            }
            // the neon grid: lines up the walls at every strip, rings round the room every metre, the corners brighter
            // (FOUR CORNERS: a quarter of the way to the leader's colour, all the way for a moment when the lead changes)
            var grid = new Vector4(0.06f, 0.32f, 0.55f, 1); var bright = new Vector4(0.25f, 0.8f, 1.0f, 1);
            if (lc != null)
            {
                float k = 0.25f + 0.75f * flash;
                for (int i = 0; i < 3; ++i)
                {
                    grid[i] += (lc[i] * 0.55f - grid[i]) * k;
                    bright[i] += (lc[i] - bright[i]) * k;
                }
            }
            for (int w = 0; w < 4; ++w)
                for (int i = 0; i <= 8; ++i)
                {
                    Vec3 q = M.Lerp(c[w], c[(w + 1) % 4], i / 8f);
                    ln[0] = q + new Vec3(0, F, 0); ln[1] = q + new Vec3(0, Ce, 0);
                    fx.Ribbon(ln, 2, i == 0 ? 0.035f : 0.010f, i == 0 ? bright : grid, eye, false);
                }
            var ring = new Vec3[5];
            for (float y = F; y <= Ce + 0.01f; y += 1f)
            {
                bool rim = y <= F + 0.01f || y >= Ce - 0.01f;
                for (int k = 0; k < 5; ++k) ring[k] = c[k & 3] + new Vec3(0, y, 0);
                fx.Ribbon(ring, 5, rim ? 0.03f : 0.010f, rim ? bright : grid, eye, false);
            }
            for (int i = 1; i < 8; ++i)   // the floor below: a grid along the walls
                for (int k = 0; k < 2; ++k)
                {
                    Vec3 a0 = M.Lerp(c[0], c[k != 0 ? 3 : 1], i / 8f), a1 = M.Lerp(c[k != 0 ? 1 : 3], c[2], i / 8f);
                    ln[0] = a0 + new Vec3(0, F + 0.01f, 0); ln[1] = a1 + new Vec3(0, F + 0.01f, 0);
                    fx.Ribbon(ln, 2, 0.012f, grid, eye, false);
                }
            if (g.np == 4)
            {   // the waiting decks outside the glass (between corners 0 and 2, and its twin across the room): a floor for
                // the waiting spots, which are at the platforms' height while the stadium round the room is lowered
                // (testers 2026-10-30: you waited in mid-air, beside the stands, nothing under you); a room for four holds
                // four playing and four waiting, or eight waiting with nobody playing
                var deck = new Vector4(0.02f, 0.035f, 0.06f, 0.96f); var rimC = new Vector4(0.15f, 0.6f, 0.9f, 1);
                var spot = new Vector4(0.12f, 0.45f, 0.65f, 1);
                foreach (float sd in new[] { 1f, -1f })
                {
                    Vec3 d = M.Normalize(c[3] - c[0]) * sd, outw = M.Normalize(new Vec3(-1, 0, -1)) * sd;
                    Vec3 mid = ((c[0] + c[3]) * 0.5f) * sd + outw * 1.0f;
                    float len = 0.8f * M.Length(c[3] - c[0]), wide = 1.8f;
                    fx.Wall(mid + new Vec3(0, -0.01f, 0), d, outw, len, wide, deck);
                    foreach (float o in new[] { 0.1f, 1.9f })
                    {
                        Vec3 e0 = mid - d * (0.5f * len) + outw * (o - 1.0f), e1 = mid + d * (0.5f * len) + outw * (o - 1.0f);
                        ln[0] = e0 + new Vec3(0, 0.005f, 0); ln[1] = e1 + new Vec3(0, 0.005f, 0);
                        fx.Ribbon(ln, 2, 0.02f, rimC, eye, false);
                        if (o > 1f) { ln[0] = e0 + new Vec3(0, 1.0f, 0); ln[1] = e1 + new Vec3(0, 1.0f, 0); fx.Ribbon(ln, 2, 0.015f, rimC, eye, false); }
                    }
                }
                for (int k = 0; k < 8; ++k) fx.SpriteFlat(HoldingPose(g, k).p + new Vec3(0, 0.008f, 0), 0.9f, spot, new Vec3(0, 1, 0), 1);
            }
        }

        // FIREBALL BLITZ (native drawOctArena): the octagon (it closes in as players are knocked out: its walls follow the
        // game's, sliding in), its floor level with yours, the glass behind each player still in in their colour (pulsing
        // as the room is about to close), a neon grid (its corners turning a warning orange then), the waiting spots
        // outside the glass and each player's spot on the floor
        float octWallR = 7.5f;
        public float OctWallR => octWallR;   // (THE CUTAWAY: the walls as drawn)
        public void DrawOctagon(FxRenderer fx, Game g, float[][] sideRGB, Vec3 eye, float now, float glassMul = 1f, float floorMul = 1f)
        {
            Hide();   // (no platforms: you stand on the floor)
            octWallR += (g.ringApothem - octWallR) * 0.08f;
            float A = octWallR, H = ExpArena.kOctH, R = A / MathF.Cos(M.kPi / 8f), side = 2f * A * MathF.Tan(M.kPi / 8f);
            var glass = new Vector4(0.02f, 0.06f, 0.11f, 0.18f * glassMul);
            var floorC = new Vector4(0.006f, 0.014f, 0.03f, 0.94f * MathF.Min(1f, floorMul));
            var ceil = new Vector4(0.012f, 0.03f, 0.06f, 0.30f * glassMul);
            float pulse = 0.5f + 0.5f * MathF.Sin(now * 3.0f);
            Vec3 Vert(int i) { float t = M.kPi / 8f + i * M.kPi / 4f; return new Vec3(R * MathF.Cos(t), 0, R * MathF.Sin(t)); }
            var up = new Vec3(0, 1, 0);
            // the floor and the ceiling (a square as wide as the room: its corners under the stands, outside the glass)
            fx.Wall(new Vec3(0, 0.002f, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 1), 2f * A, 2f * A, floorC);
            fx.Wall(new Vec3(0, H, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 1), 2f * A, 2f * A, ceil);
            bool closing = g.ringCloseAt >= 0;
            for (int i = 0; i < 8; ++i)
            {   // the eight glass walls; the one behind a player still in, in their colour
                float t = i * M.kPi / 4f;
                var n = new Vec3(MathF.Cos(t), 0, MathF.Sin(t));
                int owner = -1;
                for (int p = 0; p < g.np; ++p)
                    if (g.Alive(p) && M.Dot(M.Normalize(new Vec3(g.tun.start[p].x, 0, g.tun.start[p].z)), n) > 0.97f) owner = p;
                Vector4 c = glass;
                if (owner >= 0) c = C(PlayerRGB(g, sideRGB, owner), 0.35f, 0.24f + (closing ? 0.12f * pulse : 0f));
                fx.Wall(n * (A - 0.01f) + new Vec3(0, 0.5f * H, 0), new Vec3(-n.z, 0, n.x), up, side, H, c);
            }
            // the neon: the corners up the walls, rings round the room every metre, lines across the floor
            var grid = new Vector4(0.06f, 0.32f, 0.55f, 1); var bright = new Vector4(0.25f, 0.8f, 1.0f, 1);
            if (closing)
            {
                var warn = new Vector3(1.0f, 0.45f, 0.1f);
                for (int k = 0; k < 3; ++k) bright[k] += (warn[k] - bright[k]) * pulse;
            }
            var ln = new Vec3[2];
            for (int i = 0; i < 8; ++i)
            {
                Vec3 v = Vert(i);
                ln[0] = v; ln[1] = v + new Vec3(0, H, 0);
                fx.Ribbon(ln, 2, 0.025f, bright, eye, false);
            }
            var ring = new Vec3[9];
            for (float y = 0.01f; y <= H + 0.01f; y += 1f)
            {
                bool rim = y < 0.02f;
                for (int i = 0; i <= 8; ++i) ring[i] = Vert(i % 8) + new Vec3(0, MathF.Min(y, H), 0);
                fx.Ribbon(ring, 9, rim ? 0.03f : 0.010f, rim ? bright : grid, eye, false);
            }
            for (int i = -6; i <= 6; ++i)   // the floor grid, a line a metre, inside the walls
                for (int k = 0; k < 2; ++k)
                {
                    float u = i;
                    float reach = A;   // (as far as the octagon reaches along this line)
                    for (int w = 0; w < 8; ++w)
                    {
                        float t = w * M.kPi / 4f;
                        var n = new Vec3(MathF.Cos(t), 0, MathF.Sin(t));
                        float across = k != 0 ? n.x : n.z, along = k != 0 ? n.z : n.x;
                        if (MathF.Abs(along) > 1e-3f) reach = MathF.Min(reach, (A - u * across) / MathF.Abs(along));
                    }
                    if (reach <= 0.1f) continue;
                    ln[0] = k != 0 ? new Vec3(u, 0.006f, -reach) : new Vec3(-reach, 0.006f, u);
                    ln[1] = k != 0 ? new Vec3(u, 0.006f, reach) : new Vec3(reach, 0.006f, u);
                    fx.Ribbon(ln, 2, 0.008f, grid, eye, false);
                }
            var spot = new Vector4(0.10f, 0.35f, 0.5f, 1);   // the waiting spots outside its glass (the row across: the dugout)
            for (int k = 0; k < 8; ++k) fx.SpriteFlat(HoldingPose(g, k).p + new Vec3(0, 0.008f, 0), 0.9f, spot, up, 1);
            for (int p = 0; p < g.np; ++p)
            {   // each player's spot on the floor, in their colour
                if (!g.Alive(p)) continue;
                fx.SpriteFlat(g.tun.start[p] + new Vec3(0, 0.008f, 0), 1.6f, C(PlayerRGB(g, sideRGB, p), 0.5f), up, 1);
            }
        }
    }
}
