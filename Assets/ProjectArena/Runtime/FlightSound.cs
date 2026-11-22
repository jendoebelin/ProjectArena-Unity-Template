// BALL IN FLIGHT (TASKS.md E52; native bee88ee and edd1b41 app/src/flight_sound.h, 0.3.24; the owner, 2026-11-20:
// "it's hard to tell audibly when a ball is coming at you that is going to hurt you ... it's not increasing sound as it
// gets closer"; a player on Discord the same day: "so you can hear it coming ... in 4 square it would help you when your
// looking elsewhere"). Each ball or fireball that can hurt you is heard as it flies: a soft rush of air (noise through a
// band that opens as it nears; native's style B, the owner: "B is good"). Louder the nearer it is and the straighter it
// comes at you; across or away, quieter; on your left or right as it is to your head. Made here, sample by sample: no
// sound file. Params turns a ball (where, how fast) and your head into how it sounds; FlightSynth plays it (NativeMixer:
// four of them, smoothed between the game's frames)
using System;
using ProjectArena.Sim;

namespace ProjectArena
{
    public struct FlightParams
    {
        public float gain;     // 0 silent .. 1 (a ball at you, a metre away)
        public float pan;      // -1 your left .. 1 your right
        public float bright;   // 0 far .. 1 near: the whoosh opens up
        public float pitch;    // 1 .. 1.6: the hum rises as it closes in fast (native: 1 by default)

        public static readonly FlightParams Silent = new FlightParams { pitch = 1f };

        // a ball at `pos` flying at `vel`, heard from `head` (`right`: the head's right, flat); native flightParams
        public static FlightParams Of(Vec3 pos, Vec3 vel, Vec3 head, Vec3 right)
        {
            FlightParams f = Silent;
            Vec3 d = head - pos;
            float dist = M.Length(d);
            if (dist < 1e-3f) return f;
            float closing = M.Dot(vel, d) / dist;   // m/s toward you (less than 0: going away)
            // nearer, louder: all of it within a metre, about a third at 6 m, a sixth at 12 m
            float prox = Math.Min(1f, 2.2f / (dist + 1.2f));
            // at you, all of it; the further off its line passes from your head, the less (a third at 2 m off); going
            // away, it fades out over a few metres a second
            float sp = M.Length(vel);
            float aim;
            if (sp < 0.5f) aim = 0.2f;
            else if (closing <= 0f) aim = 0.2f * M.Clampf(1f + closing / 3f, 0f, 1f);
            else
            {
                float tc = M.Dot(d, vel) / (sp * sp);   // seconds to its nearest
                float miss = M.Length(d - vel * tc);      // how far from your head it passes then
                aim = 0.3f + 0.7f * M.Clampf(1f - (miss - 0.4f) / 1.6f, 0f, 1f);
            }
            f.gain = prox * aim;
            f.bright = M.Clampf(1f - dist / 8f, 0f, 1f);
            f.pitch = 1f + 0.6f * M.Clampf(closing / 15f, 0f, 1f);
            f.pan = M.Clampf(M.Dot(pos - head, right) / dist, -1f, 1f) * 0.85f;
            return f;
        }
    }

    public class FlightSynth
    {
        // how it sounds (the owner, 2026-11-20, of the first: "close but not quite as subtle or blended in as I wanted"):
        // the whoosh's band (lowHz far .. lowHz + spanHz near), its width (damp: higher, broader and softer) and level; a
        // tone's level (hum, pitch rising as it closes in); a low rumble's (noise under 120 Hz, rising as it nears); all of
        // it (level). Native kFlightStyles: 0 the first, A quieter with the hum all but gone, B a soft rush of air (the one
        // played), C B with a low rumble
        public struct Style { public float lowHz, spanHz, damp, whoosh, hum, rumble, level; }
        public static readonly Style[] Styles =
        {
            new Style { lowHz = 500f, spanHz = 2700f, damp = 0.9f, whoosh = 0.70f, hum = 0.28f, rumble = 0f, level = 1f },
            new Style { lowHz = 500f, spanHz = 2700f, damp = 0.9f, whoosh = 0.70f, hum = 0.06f, rumble = 0f, level = 0.55f },
            new Style { lowHz = 250f, spanHz = 1300f, damp = 1.4f, whoosh = 0.80f, hum = 0f, rumble = 0f, level = 0.77f },
            new Style { lowHz = 250f, spanHz = 1300f, damp = 1.4f, whoosh = 0.72f, hum = 0f, rumble = 5f, level = 0.55f },
        };
        public const int KStyle = 2;   // (the one the game plays: B)
        public const float KGlide = 0.0007f;   // per sample at 48 kHz: about 30 ms to follow a change

        public FlightParams want = FlightParams.Silent;   // (set by the game, read by the mixer: under the mixer's lock)
        public int style = KStyle;
        float gL, gR, fc = 0.07f, hz = 120f, rum, lp, bp, lo, lo2, phase;
        uint rng = 0x9e3779b9u;

        // added into `stereo` (interleaved, two channels, `frames` long) at `rate` (native: 48 kHz, which the Quest plays at)
        public void Render(float[] stereo, int frames, float level, int rate = 48000)
        {
            Style st = Styles[style < 0 || style > 3 ? KStyle : style];
            float tl = want.gain * level * st.level * MathF.Sqrt(0.5f * (1f - want.pan));
            float tr = want.gain * level * st.level * MathF.Sqrt(0.5f * (1f + want.pan));
            if (tl < 1e-4f && tr < 1e-4f && gL < 1e-4f && gR < 1e-4f) { gL = gR = 0; lp = bp = lo = lo2 = 0; return; }
            const float kPi2 = 6.2831853f;
            float sr = rate > 0 ? rate : 48000;
            float glide = KGlide * 48000f / sr;
            float fcWant = 2f * MathF.Sin(0.5f * kPi2 * (st.lowHz + st.spanHz * want.bright) / sr);
            float hzWant = 120f * want.pitch;
            float fLow = 2f * MathF.Sin(0.5f * kPi2 * 120f / sr), rumbleWant = st.rumble * want.bright;
            for (int i = 0; i < frames; ++i)
            {
                gL += (tl - gL) * glide; gR += (tr - gR) * glide;   // (smoothed: the game sets it once a frame)
                fc += (fcWant - fc) * glide; hz += (hzWant - hz) * glide; rum += (rumbleWant - rum) * glide;
                rng = rng * 1664525u + 1013904223u;
                float n = (int)rng * (1f / 2147483648f);
                float hp = n - lp - st.damp * bp;   // a band-pass (state-variable filter): the whoosh
                bp += fc * hp;
                lp += fc * bp;
                float s = bp * st.whoosh;
                if (st.hum > 0f)
                {
                    phase += hz * (kPi2 / sr);
                    if (phase > kPi2) phase -= kPi2;
                    s += (MathF.Sin(phase) * 0.75f + MathF.Sin(2f * phase) * 0.2f) * st.hum;
                }
                if (st.rumble > 0f)
                {   // (the same noise, twice through a low-pass under 120 Hz)
                    lo += fLow * (n - lo);
                    lo2 += fLow * (lo - lo2);
                    s += lo2 * rum;
                }
                stereo[2 * i] += s * gL;
                stereo[2 * i + 1] += s * gR;
            }
        }
    }
}
