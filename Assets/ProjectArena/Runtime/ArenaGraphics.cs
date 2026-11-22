// GRAPHICS (TASKS.md E38; native c97c364, 362d772, 9b00fbc, c184b84, 0.3.22): what this headset's or this PC's picture
// draws, and nothing else: nothing is sent, so no one else's game, frame rate or connection changes.
//   SETTINGS > GRAPHICS AND VIEW: FOVEATION (OFF / LOW / MEDIUM / HIGH: the headset's fixed foveated rendering; HIGH by
//   default, measured natively: 1.2 ms back a frame, no blur seen), SMOOTH EDGES (OFF / 2X / 4X: multisampling; OFF by
//   default, frames dropped with it), CONTACT SHADOWS, COLOR GRADING, BLOOM and FLOOR REFLECTIONS (OFF / SPECTATING /
//   ON, SPECTATING: only while you watch). The PC spectator has its own switches, all on (Spectator Controls > Graphics),
//   with AMBIENT OCCLUSION and VIGNETTE too.
//   The frame-rate guard: in a match, fewer than 92% of the display's frames for 3 s running (the PC spectator: under 55
//   a second) turns BLOOM and SMOOTH EDGES (on a PC, AMBIENT OCCLUSION too) off until the match is over.
// In Unity: FOVEATION through the OpenXR Foveated Rendering feature (XRDisplaySubsystem.foveatedRenderingLevel, fixed);
// SMOOTH EDGES the URP asset's MSAA (in a player only: in the editor the asset would keep it); COLOR GRADING in the
// shaders as native (the global _PA_Grade: Native.shader's surfaces and Fx.shader's glows); CONTACT SHADOWS the soft
// blobs native draws (FxRenderer.ShadowFlat); FLOOR REFLECTIONS the glossy floor's mirrored court, stadium and balls;
// BLOOM, AMBIENT OCCLUSION and VIGNETTE URP's own post-processing (ArenaPost).
using System;
using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace ProjectArena
{
    public static class ArenaGraphics
    {
        public static readonly string[] FovName = { "OFF", "LOW", "MEDIUM", "HIGH" }, MsName = { "OFF", "2X", "4X" },
                                         OnName = { "OFF", "SPECTATING", "ON" };
        public static bool On(int v, bool spectating) => v >= 2 || (v == 1 && spectating);   // (OFF / SPECTATING / ON)

        // the frame-rate guard: BLOOM and SMOOTH EDGES (the costly ones) went off by themselves, frames dropping in a match
        public static bool guard;
        static double since;
        static int frames, slow;

        // in force this frame (Update)
        public static int Samples;
        public static bool Bloom, Contact, Reflect, Grade, Ao, Vignette;

        public static int WantSamples(AppSettings s, bool desk)
        {
            int v = desk ? s.dcMsaa : s.gfxMsaa;
            return v >= 2 ? 4 : v == 1 ? 2 : 0;
        }
        public static bool WantBloom(AppSettings s, bool desk, bool spectating) => desk ? s.dcBloom : On(s.gfxBloom, spectating);

        // GRAPHICS as set, into the log (at start and at each change: bug reports carry it, and a measurement lines up with it)
        public static void Log(AppSettings s)
        {
            Debug.Log($"[Graphics] foveation {FovName[s.gfxFoveation & 3].ToLowerInvariant()}, smooth edges {MsName[s.gfxMsaa % 3].ToLowerInvariant()}, " +
                      $"contact shadows {OnName[s.gfxContact % 3].ToLowerInvariant()}, color grading {OnName[s.gfxGrade % 3].ToLowerInvariant()}, " +
                      $"bloom {OnName[s.gfxBloom % 3].ToLowerInvariant()}, floor reflections {OnName[s.gfxReflect % 3].ToLowerInvariant()}");
        }

        // the frame-rate guard, once a frame (t: seconds; hz: the display's refresh rate, 0 on a PC window)
        public static void Guard(AppSettings s, bool desk, bool spectating, MatchState m, double t, float hz)
        {
            if (guard && (m == MatchState.MatchSummary || m == MatchState.WaitingForPlayers))
            {
                guard = false;
                Debug.Log("[Graphics] the match is over: bloom and smooth edges as set again");
            }
            if (since == 0 || t < since) { since = t; frames = 0; }
            ++frames;
            if (t - since < 1.0) return;
            float fps = (float)(frames / (t - since));
            since = t; frames = 0;
            bool costly = WantSamples(s, desk) > 0 || WantBloom(s, desk, spectating) || (desk && s.dcAo);
            bool low = fps < (hz > 1f ? 0.92f * hz : 55f);
            slow = low && costly && m == MatchState.InProgress && !guard ? slow + 1 : 0;
            if (slow >= 3)
            {
                guard = true;
                slow = 0;
                Debug.Log($"[Graphics] frames dropping ({fps:F0} a second, the display {hz:F0}): bloom and smooth edges off until the match is over");
            }
        }

        // what is in force now (desk: the PC spectator's own switches)
        public static void Decide(AppSettings s, bool desk, bool spectating)
        {
            Samples = guard ? 0 : WantSamples(s, desk);
            Bloom = !guard && WantBloom(s, desk, spectating);
            Contact = desk ? s.dcContact : On(s.gfxContact, spectating);
            Reflect = desk ? s.dcReflect : On(s.gfxReflect, spectating);
            Grade = desk ? s.dcGrade : On(s.gfxGrade, spectating);
            Ao = desk && s.dcAo && !guard;
            Vignette = desk && s.dcVignette;
        }

        static readonly int GradeId = Shader.PropertyToID("_PA_Grade");
        static int foveationSet = -1, samplesSet = -1;
        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        static XRDisplaySubsystem Display()
        {
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return d;
            return null;
        }

        // FOVEATION: a headset that can (a running XR display and a GPU with foveated rendering)
        public static bool HasFoveation => SystemInfo.foveatedRenderingCaps != FoveatedRenderingCaps.None && Display() != null;

        // the display's refresh rate (0: none, the PC window)
        public static float Hz()
        {
            var d = Display();
            return d != null && d.TryGetDisplayRefreshRate(out float hz) ? hz : 0f;
        }

        // onto the picture: foveation and multisampling when they change, the grade every frame
        public static void Apply(AppSettings s)
        {
            Shader.SetGlobalFloat(GradeId, Grade ? 1f : 0f);
            var d = Display();
            if (d != null && foveationSet != s.gfxFoveation && SystemInfo.foveatedRenderingCaps != FoveatedRenderingCaps.None)
            {   // (fixed: the same at every moment, what was measured)
                foveationSet = s.gfxFoveation;
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
                d.foveatedRenderingLevel = Math.Max(0, Math.Min(3, s.gfxFoveation)) / 3f;
                Debug.Log($"[Graphics] foveation: {FovName[s.gfxFoveation & 3].ToLowerInvariant()}");
            }
            int n = Samples >= 4 ? 4 : Samples >= 2 ? 2 : 1;
            if (n != samplesSet && !Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                samplesSet = n;
                urp.msaaSampleCount = n;
                Debug.Log($"[Graphics] smooth edges: {(n == 1 ? "off" : n + "x")}");
            }
        }

        // CONTACT SHADOWS: the height of the arena's floor (any surface facing up; not the foul and return zones' slabs)
        // under `at`, at most 0.3 m above it (native floorUnder; its collision grid's column, here the arena's floor
        // triangles, gathered once). False: none
        static List<CollisionTri> floorOf;
        static readonly List<CollisionTri> floorTris = new List<CollisionTri>();
        public static bool FloorUnder(Game g, Vec3 at, out float y)
        {
            y = 0f;
            if (g.arena == null || g.arena.Count == 0) return false;
            if (!ReferenceEquals(floorOf, g.arena))
            {
                floorOf = g.arena;
                floorTris.Clear();
                foreach (CollisionTri t in g.arena)
                    if (Math.Abs(t.n.y) >= 0.6f && (t.tags & (Tag.Foul | Tag.Return)) == 0) floorTris.Add(t);   // (either winding)
            }
            float top = at.y + 0.3f, best = -1e9f;
            bool found = false;
            float Side(Vec3 p, Vec3 q, Vec3 r) => (q.x - p.x) * (r.z - p.z) - (q.z - p.z) * (r.x - p.x);
            foreach (CollisionTri t in floorTris)
            {
                float h = t.a.y - ((at.x - t.a.x) * t.n.x + (at.z - t.a.z) * t.n.z) / t.n.y;   // (its plane there)
                if (h > top || h <= best) continue;
                float d1 = Side(t.a, t.b, at), d2 = Side(t.b, t.c, at), d3 = Side(t.c, t.a, at);
                if ((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0)) continue;   // (not over it)
                best = h;
                found = true;
            }
            if (found) y = best;
            return found;
        }

        // CONTACT SHADOWS (native drawWorld): a soft shadow on the floor under each player and ball (not over passthrough's
        // room). viewIn / viewInValid: the others as drawn; local: your slot (-1 watching)
        public static void DrawContact(FxRenderer fx, Game g, PlayerInput[] viewIn, bool[] viewInValid, int local, bool exp)
        {
            bool FloorBelow(Vec3 at, out float fy)
            {
                if (FloorUnder(g, at, out fy)) { fy += 0.006f; return true; }   // (just over it)
                if (g.Octagon) { fy = 0.006f; return true; }   // (FIREBALL BLITZ's room has no collision: its floor is at 0)
                return false;
            }
            for (int p = 0; p < g.np; ++p)
            {
                // (each where it is drawn: you as tracked, the others smoothed; no one in an empty slot, or knocked out)
                bool mine = p == local;
                bool there = g.Alive(p) && g.pl[p].@in.headTracked && (mine || (p < viewInValid.Length && viewInValid[p]));
                Vec3 h = mine || p >= viewIn.Length ? g.pl[p].@in.head.p : viewIn[p].head.p;
                bool onFloor = FloorBelow(h, out float y);
                if (!onFloor && !exp) { y = 0.03f; onFloor = true; }   // (the court's players stand beyond its collision: the drawn floor)
                if (there && onFloor) fx.ShadowFlat(new Vec3(h.x, y, h.z), 0.95f, 0.6f, new Vec3(0, 1, 0));
                void BallShadow(Ball b)
                {
                    if (b.state == BallState.Dead || !FloorBelow(b.pos, out float by)) return;
                    float ht = b.pos.y - by;
                    fx.ShadowFlat(new Vec3(b.pos.x, by, b.pos.z), 0.22f + 0.14f * ht, 0.6f * Math.Clamp(1f - ht / 3f, 0.15f, 1f), new Vec3(0, 1, 0));
                }
                BallShadow(g.pl[p].ball);
                for (int k = 0; k < Game.KMaxFire; ++k) if (g.fire[p][k].state == BallState.Flying) BallShadow(g.fire[p][k]);
            }
        }
    }
}
