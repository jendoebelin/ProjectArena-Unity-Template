// PASSTHROUGH's rules (task D1; native main.cpp: kPtMode*, the PASSTHROUGH page's actions, edgeDistance, the
// frame's ROOM AT EDGES and PEEK, updatePassthrough's want, mrView): the four modes and what each starts with, the
// page's adjustments and presets, how near you are to the edge of your play space, and how far the game fades toward
// your room this frame. PassthroughFeature puts the room in the frame; MatchView draws for it (mrView).
using System;
using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;

namespace ProjectArena
{
    public sealed class RoomPassthrough
    {
        public const int Off = 0, ArenaMode = 1, Window = 2, Outline = 3, Count = 4;
        public static readonly string[] ModeName = { "OFF", "ARENA", "WINDOW", "OUTLINE" };
        public static readonly string[] ModeHelp =
        {
            "THE FULL STADIUM", "THE COURT IN YOUR ROOM: THE STADIUM IS GONE",
            "INSIDE THE COURT IS ALL GAME: YOUR ROOM LOOKS IN THROUGH THE GLASS",
            "ONLY THE COURT'S EDGES AND LINES: SEE YOUR WHOLE ROOM",
        };
        public static readonly float[] ModeFloor = { 0.8f, 0.8f, 1.0f, 0.0f };   // the court floor each mode starts with
        // (native WINDOW also covers real things inside the court with the room's depth, XR_META_environment_depth on
        // Quest 3 / 3S; not in this port yet: WINDOW shows your room through the glass, as on a headset without depth)
        public const string WindowNoDepth = "WINDOW: YOUR ROOM THROUGH THE GLASS (NO ROOM DEPTH IN THIS VERSION)";

        public static string Help(int mode) => mode == Window ? WindowNoDepth : ModeHelp[((mode % Count) + Count) % Count];

        // ---- the page's actions (native MenuAct::PtMode / PtAdjust / PtPreset)
        public static void SetMode(AppSettings s, int m)
        {
            m = ((m % Count) + Count) % Count;
            if (m != s.ptMode) { s.ptMode = m; s.ptFloor = ModeFloor[m]; }
            Debug.Log("[Passthrough] mode " + ModeName[s.ptMode]);
        }
        // 0 / 1 COURT FLOOR - / +, 2 / 3 GLASS WALLS, 4 / 5 ROOM BRIGHTNESS: 10 % steps (floor 0-100 %, glass 0-200 %, room 10-100 %)
        public static void Adjust(AppSettings s, int ip)
        {
            int k = ip / 2;
            float d = (ip & 1) != 0 ? 0.1f : -0.1f;
            float Step(float v, float lo, float hi) => Mathf.Clamp(MathF.Round((v + d) * 10f, MidpointRounding.AwayFromZero) / 10f, lo, hi);   // (as std::round)
            if (k == 0) s.ptFloor = Step(s.ptFloor, 0f, 1f);
            else if (k == 1) s.ptGlass = Step(s.ptGlass, 0f, 2f);
            else if (k == 2) s.ptRoom = Step(s.ptRoom, 0.1f, 1f);
        }
        // INDOOR: all game inside the court; OUTDOOR: see everything, real objects outlined
        public static void Preset(AppSettings s, int ip)
        {
            if (ip == 0) { s.ptMode = Window; s.ptFloor = 1f; s.ptGlass = 1f; s.ptRoom = 1f; s.ptMono = false; s.ptEdges = false; }
            else { s.ptMode = Outline; s.ptFloor = 0f; s.ptGlass = 1f; s.ptRoom = 0.7f; s.ptMono = false; s.ptEdges = true; }
            s.ptEdgeReveal = true;
            Debug.Log("[Passthrough] preset " + (ip == 0 ? "indoor" : "outdoor"));
        }

        // ---- the play space (native edgeDistance): how far a point (tracking space, x / z in metres) is inside the
        // boundary's edge (negative: outside). Quest's boundary comes as the largest rectangle inside it (the STAGE
        // space's); without one, a 1.5 m circle round the tracking origin
        public static float EdgeDistance(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            if (poly == null || poly.Count < 3) return 1.5f - p.magnitude;
            float area = 0;
            for (int i = 0; i < poly.Count; ++i) { Vector2 a = poly[i], b = poly[(i + 1) % poly.Count]; area += a.x * b.y - b.x * a.y; }
            float sign = area >= 0 ? 1f : -1f, d = float.MaxValue;
            for (int i = 0; i < poly.Count; ++i)
            {
                Vector2 a = poly[i], e = poly[(i + 1) % poly.Count] - a;
                float len = e.magnitude;
                if (len < 1e-4f) continue;
                d = Mathf.Min(d, sign * (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / len);   // (inward: left of a counter-clockwise edge)
            }
            return d == float.MaxValue ? 1.5f - p.magnitude : d;
        }
        // the boundary's narrowest width (a rectangle: its shorter side)
        public static float MinWidth(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; ++i)
            {
                Vector2 a = poly[i], e = poly[(i + 1) % poly.Count] - a;
                float len = e.magnitude;
                if (len < 1e-4f) continue;
                float far = 0;
                foreach (Vector2 q in poly) far = Mathf.Max(far, Mathf.Abs(e.x * (q.y - a.y) - e.y * (q.x - a.x)) / len);
                best = Mathf.Min(best, far);
            }
            return best == float.MaxValue ? 0f : best;
        }

        // ---- the frame
        public float reveal;     // how far the game is faded out to show the room now (0..1; native ptReveal)
        public bool mrView;      // this frame drawn for the room (native mrView)
        public float fadeNow;    // this frame's fade, under the menus (native ptFadeNow)
        public bool want;        // the room asked for this frame (native updatePassthrough's want)
        double logAt;

        // ROOM AT EDGES and PEEK (Y), then what the room needs (native: the frame before its layers). available: the
        // passthrough is made and hasn't failed; head / hand: the head's and the nearer hand's distance from the edge;
        // bounds: a boundary rectangle (not the circle); narrow: its shorter side (m)
        public void Step(AppSettings s, bool available, bool spectating, bool peekHeld, float head, float hand, bool bounds,
                         float narrow, float dt, double now)
        {
            float target = 0f;
            // (only with a passthrough mode on: OFF is off, owner 2026-10-18)
            if (available && !spectating && s.ptMode != Off && s.ptEdgeReveal)
            {
                // the head from 35 cm inside the edge (full at 10 cm), a hand only once past the edge (full 15 cm out); a
                // small boundary (under 1.8 m): the hands only; the head outside the boundary: it doesn't describe where
                // you play (owner, 2026-10-18 / 19)
                bool small = bounds && narrow < 1.8f;
                float th = small ? 0f : Mathf.Clamp01((0.35f - head) / 0.25f);
                float tk = Mathf.Clamp01(-hand / 0.15f);
                bool outside = bounds && head < 0f;
                target = outside ? 0f : Mathf.Max(th, tk) * 0.85f;
                if (outside && now > logAt)
                {
                    logAt = now + 30.0;
                    Debug.Log($"[Passthrough] room at edges: the head is {-head:F2} m outside the boundary, ignored");
                }
                else if (target > 0f && now > logAt)
                {
                    logAt = now + 5.0;
                    Debug.Log($"[Passthrough] room at edges: head {head:F2} m, hand {hand:F2} m from the edge ({(bounds ? "boundary" : "circle")})");
                }
            }
            if (available && !spectating && s.ptPeekOn && peekHeld) target = 0.95f;   // PEEK (Y)
            reveal += (target - reveal) * Mathf.Min(1f, dt * (target > reveal ? 10f : 4f));
            if (reveal < 0.003f) reveal = 0f;
            // PEEK in any mode, OFF included: the room runs while Y is held and until the game has faded back in
            bool peek = s.ptPeekOn && !spectating && (peekHeld || reveal > 0f);
            want = available && (s.ptMode != Off || peek);
        }

        // once the room's state is known: draw for it (GRAPHICS AND VIEW shows the arena), and the fade
        public void Frame(AppSettings s, bool running, bool graphicsPage)
        {
            mrView = running && s.ptMode != Off && !graphicsPage;
            fadeNow = running ? reveal : 0f;
        }
        // ---- drawing for the room (native drawWorld with mrView), shared by MatchView and the editor's pictures

        // which of the arena's models show: the stadium never (your room instead), the floor and its reflections not
        // below 1 %, OUTLINE the court's outline in place of the court and its glow layers (court_outline otherwise hidden)
        // (also the PC spectator's VIEW LAYERS, native drawWorld's noWalls / minimal / keyBg: NO WALLS the outline,
        // MINIMAL no stadium, floor reflections or glow layers, KEY nothing of the court at all)
        public static bool Shows(string part, bool noStadium, bool outline, bool noFloor, bool minimal = false, bool noCourt = false)
        {
            if (part == "court_outline") return outline && !noCourt;
            if (noCourt && (part == "arena" || part == "court_fx" || part == "court_pulse" || part == "floor" || part == "floor_reflect")) return false;
            if (noStadium && (part == "environment" || part == "env_fx" || part == "stadium_fx" || part == "floor_reflect_env")) return false;
            if (minimal && (part == "floor_reflect" || part == "floor_reflect_env" || part == "court_fx")) return false;
            if (noFloor && (part == "floor" || part == "floor_reflect")) return false;
            if (outline && (part == "arena" || part == "court_fx")) return false;
            return true;
        }

        static readonly int TintMul = Shader.PropertyToID("_TintMul"), TintId = Shader.PropertyToID("_Tint");

        // the court's glass: each blended (alpha) section of these renderers, and its own opacity
        public struct Glass { public Renderer r; public int index; public float alpha; }
        public static List<Glass> GlassOf(IEnumerable<Renderer> rs)
        {
            var v = new List<Glass>();
            foreach (Renderer r in rs)
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; ++i)
                    if (ms[i] != null && ms[i].HasProperty("_SrcBlend") && (int)ms[i].GetFloat("_SrcBlend") == (int)UnityEngine.Rendering.BlendMode.SrcAlpha)
                        v.Add(new Glass { r = r, index = i, alpha = ms[i].HasProperty(TintId) ? ms[i].GetColor(TintId).a : 1f });
            }
            return v;
        }
        // GLASS WALLS: each section's opacity times this (native glassMul: at most fully opaque)
        public static void SetGlass(List<Glass> glass, MaterialPropertyBlock mpb, float mul)
        {
            foreach (Glass g in glass)
            {
                g.r.GetPropertyBlock(mpb, g.index);
                mpb.SetVector(TintMul, new Vector4(1, 1, 1, Mathf.Min(mul, 1f / Mathf.Max(1e-3f, g.alpha))));
                g.r.SetPropertyBlock(mpb, g.index);
            }
        }
        // COURT FLOOR: the floor's opacity (its own is 80 %: native alphaMul 0.80; for the room min(99.8 %, COURT FLOOR))
        public const float FloorAlpha = 0.80f;
        public static void SetFloor(IEnumerable<Renderer> floor, MaterialPropertyBlock mpb, float alpha)
        {
            foreach (Renderer r in floor)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetVector(TintMul, new Vector4(1, 1, 1, alpha / FloorAlpha));
                r.SetPropertyBlock(mpb);
            }
        }
        public static float FloorFor(bool mr, float ptFloor) => mr ? Mathf.Min(0.998f, ptFloor) : FloorAlpha;

        // WINDOW: a night sky over the court, so only the glass walls look out (native: 19 x 5 m at 4 m)
        public static readonly Vector4 WindowSky = new Vector4(0.006f, 0.010f, 0.022f, 0.998f);

        // ROOM AT EDGES / PEEK: the world (colour and coverage) times 1 - k, under the menus (native fadeToRoom: a
        // full-view quad, blend zero / one minus source alpha for both). Drawn between the effects (Transparent+300)
        // and the text and menus (Transparent+310)
        public static Material FadeMaterial(Shader native)
        {
            if (native == null) return null;
            var m = new Material(native) { name = "room fade", renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 305 };
            m.EnableKeyword("_PA_UNLIT");
            m.SetFloat("_HasDiffuse", 0f);
            m.SetColor(TintId, new Color(0, 0, 0, 0));
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendA", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_DstBlendA", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return m;
        }
        static Mesh quad;
        public static void DrawFade(Material m, Camera cam, float k)
        {
            if (m == null || cam == null || k <= 0f) return;
            if (quad == null)
            {
                quad = new Mesh { name = "room fade" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
                quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                quad.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            }
            m.SetColor(TintId, new Color(0, 0, 0, Mathf.Min(1f, k)));
            Transform t = cam.transform;   // (just past the near plane, far wider than any headset's view, both eyes)
            var at = Matrix4x4.TRS(t.position + t.forward * (cam.nearClipPlane + 0.03f), t.rotation, new Vector3(40f, 40f, 1f));
            Graphics.RenderMesh(new RenderParams(m) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, at);
        }
    }
}

