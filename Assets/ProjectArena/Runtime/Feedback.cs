using ProjectArena.Sim;

namespace ProjectArena
{
    // What the player hears and feels for a game event, as the native game decides it (audio.cpp Audio::onEvent,
    // main.cpp handleEvents). Pure logic: Runtime/NativeMixer.cs plays the sound, MatchView sends the haptics.
    public static class Feedback
    {
        // the sound's key in audio/events.txt, from the listener's side (local = the listener's player index)
        public static string SoundKey(GameEvent e, int local)
        {
            string key = Game.EventName(e.type);
            // from the listener's side (it used to be player 0's: the orange player heard "you were hit" when they scored)
            if (e.type == EventType.HitPlayer) key = (e.player == local) ? "HitPlayerSelf" : "HitPlayerOpponent";
            if (e.type == EventType.RoundEnd || e.type == EventType.MatchEnd) key += (e.player == local) ? "Win" : "Lose";
            if (e.type == EventType.Throw) key = e.value < 6 ? "ThrowSlow" : e.value < 9 ? "ThrowMed" : e.value < 12 ? "ThrowFast" : "ThrowFull";
            if (e.type == EventType.KnuckleBlock && e.value >= 12f) key = "KnucklePunch";   // (THE PUNCH LANDS: a hard one, its crack)
            return key;
        }

        // THE PUNCH LANDS: how hard a knuckle hit sent the ball, 0.2 .. 1 (a still fist's 7 m/s to 16)
        public static float PunchPower(GameEvent e) => M.Clampf((e.value - 6f) / 10f, 0.2f, 1f);

        // nearer is louder (events without a place play at full volume)
        public static float Gain(GameEvent e, Vec3 listener)
        {
            float d = M.Length(e.pos - listener);
            return (e.pos.x == 0 && e.pos.y == 0 && e.pos.z == 0) ? 1f : M.Clampf(2.0f / (1.0f + d), 0.15f, 1f);
        }

        // court-relative pan, the player faces +X (+Z = right)
        public static float Pan(GameEvent e, Vec3 listener) => M.Clampf((e.pos.z - listener.z) * 0.4f, -0.8f, 0.8f);

        // haptics: strong and long enough to feel through the Touch controllers' motors. hand -1 = both hands.
        // mine: the event is the local player's. Returns false when the event has none.
        public static bool Haptic(GameEvent e, bool mine, out int hand, out float amplitude, out float seconds)
        {
            hand = e.hand; amplitude = 0; seconds = 0;
            switch (e.type)
            {
                case EventType.Grab: if (mine && e.hand >= 0) { amplitude = 0.85f; seconds = 0.12f; } break;
                case EventType.Caught: if (mine && e.hand >= 0) { amplitude = 1.0f; seconds = 0.18f; } break;
                case EventType.ShieldBlock: if (mine && e.hand >= 0) { amplitude = 1.0f; seconds = 0.28f; } break;
                case EventType.ShieldBreak: if (mine && e.hand >= 0) { amplitude = 1.0f; seconds = 0.35f; } break;
                case EventType.KnuckleBlock:   // THE PUNCH LANDS (native f045e04): a short hard knock in that hand (was 0.2 s)
                    if (mine && e.hand >= 0) { amplitude = 1.0f; seconds = 0.05f + 0.05f * PunchPower(e); }
                    break;
                case EventType.Throw: if (mine && e.hand >= 0) { amplitude = 0.6f; seconds = 0.08f; } break;
                case EventType.ThrowFail: if (mine && e.hand >= 0) { amplitude = 0.4f; seconds = 0.06f; } break;
                case EventType.FistBump: hand = -1; amplitude = 1f; seconds = 0.35f; break;   // (not while waiting / spectating)
                case EventType.HitPlayer: if (mine) { hand = -1; amplitude = 1f; seconds = 0.5f; } break;   // you were hit
            }
            return seconds > 0;
        }
    }
}
