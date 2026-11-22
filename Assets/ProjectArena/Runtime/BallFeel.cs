// BALL FEEL (TEST) (TASKS.md E18, native a2e53ce; two players' report, 2026-11-17: the ball "doesn't feel like a ball ...
// more like old school pong"; "the proportion/ratio of the plasma halo vs the core of the ball or maybe the heaviness").
// Found: the ball is all glow (an additive plasma sphere 10 cm, its shell fragments to 12.5 cm, a faint glow inside: no
// solid centre), drawn 25% wider than the 10 cm that hits. Four ways to try, each on its own (SETTINGS > GRAPHICS AND
// VIEW > BALL FEEL (TEST), this headset only): CORE SOLID, GLOW TRUE SIZE, SPIN VISIBLE, WEIGHT HEAVY. Drawing and
// haptics only, nothing sent: play and every version unchanged.
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectArena
{
    public class BallFeel
    {
        // the switches this frame (AppSettings' ballCore, ballTrue, ballSpin, ballWeight)
        public bool core, trueSize, spin, weight;

        public const float TrueSize = 0.8f;      // GLOW TRUE SIZE: the plasma, shell and arcs drawn 0.8 (the outside where it hits)
        public const float CoreShare = 0.62f;    // CORE SOLID: a lit core, 62% of what hits
        public const float Lag = 0.012f, LagMax = 0.03f;   // WEIGHT HEAVY: a held ball drawn 12 ms behind the hand, at most 3 cm

        readonly Quat[] turn = { Quat.Identity, Quat.Identity, Quat.Identity, Quat.Identity };   // each ball's drawn turn
        readonly Vec3[] heldVis = new Vec3[4];   // a held ball where it is drawn
        readonly bool[] heldOn = new bool[4];

        public float Vis => trueSize ? TrueSize : 1f;

        // each frame, whatever the switches (native updateFx): a ball's turn by its spin, and flying a tumble at a quarter of
        // a rolling ball's rate; a held ball following the hand Lag behind, at most LagMax
        public void Step(Game g, float dt)
        {
            for (int p = 0; p < g.np && p < 4; ++p)
            {
                Ball b = g.pl[p].ball;
                Vec3 w = b.spin;
                float sp = M.Length(b.vel);
                if (b.state == BallState.Flying && sp > 0.5f)
                {
                    Vec3 roll = M.Cross(new Vec3(0, 1, 0), b.vel / sp);
                    if (M.Length(roll) > 1e-3f) w += M.Normalize(roll) * (sp / M.Max(0.05f, g.BallRadius(p)) * 0.25f);
                }
                float wl = M.Length(w);
                if (wl > 1e-3f && b.state != BallState.Held) turn[p] = M.Nlerp(Quat.Identity, Quat.AxisAngle(w / wl, wl * dt) * turn[p], 1f);   // (renormalized)
                if (b.state == BallState.Held)
                {
                    if (!heldOn[p]) heldVis[p] = b.pos;
                    heldVis[p] += (b.pos - heldVis[p]) * (1f - Mathf.Exp(-dt / Lag));
                    Vec3 off = heldVis[p] - b.pos;
                    if (M.Length(off) > LagMax) heldVis[p] = b.pos + M.Normalize(off) * LagMax;
                    heldOn[p] = true;
                }
                else heldOn[p] = false;
            }
        }

        // where ball p is drawn (WEIGHT HEAVY: a held one a little behind the hand), and how it is turned (SPIN VISIBLE)
        public Vec3 DrawnAt(int p, Ball b) => weight && p < 4 && b.state == BallState.Held && heldOn[p] ? heldVis[p] : b.pos;
        public bool Lagging(int p) => p < 4 && heldOn[p];   // (WEIGHT HEAVY: a held ball drawn where it lags)
        public Quat Turn(int p) => spin && p < 4 ? turn[p] : Quat.Identity;
        public Quat RawTurn(int p) => turn[p];
        public void SetTurn(int p, Quat q) => turn[p] = q;   // (pictures)

        // WEIGHT HEAVY: holding your ball, the controller hums as you swing it (from 1 m/s, stronger faster; under 0.04 none)
        public static float HumAmp(float handSpeed) => M.Clampf((handSpeed - 1f) / 6f, 0f, 0.6f);
        public const float HumSeconds = 0.025f;

        // CORE SOLID's look (native forceShader SH_LIT): the ball model's materials made opaque and lit
        public static Material[] LitCopy(Material[] src)
        {
            var m = new Material[src.Length];
            for (int i = 0; i < src.Length; ++i)
            {
                var c = new Material(src[i]) { name = src[i].name + " (core)" };
                foreach (string k in new[] { "_PA_ADD", "_PA_UNLIT", "_PA_LIGHTMAP" }) c.DisableKeyword(k);
                c.EnableKeyword("_PA_LIT");
                c.SetFloat("_LitOnly", 1f);
                c.SetFloat("_SrcBlend", (float)BlendMode.One); c.SetFloat("_DstBlend", (float)BlendMode.Zero);
                c.SetFloat("_SrcBlendA", (float)BlendMode.One); c.SetFloat("_DstBlendA", (float)BlendMode.Zero);
                c.SetFloat("_ZWrite", 1f);
                c.SetFloat("_Cull", (float)CullMode.Back);
                c.SetVector("_UVScroll", Vector4.zero);
                c.SetOverrideTag("RenderType", "Opaque");
                c.renderQueue = (int)RenderQueue.Geometry;
                m[i] = c;
            }
            return m;
        }

        // CORE SOLID's tint: the team colour, lit, a little glow of its own (dimmer uncharged)
        public static void CoreTint(Vector3 team, bool charged, out Vector3 mul, out Vector3 emissive)
        {
            mul = team * (charged ? 0.8f : 0.35f);
            emissive = team * (charged ? 0.38f : 0.12f);
        }
    }
}
