// BALL LOOK-AHEAD (task B5): native 0.3.15's Game::lookAhead (fc80084) and recordHand, ported line by line.
// Display only: the host still decides every throw, block and point.
using System;
using static ProjectArena.Sim.M;

namespace ProjectArena.Sim
{
    public partial class Game
    {
        // owner, 2026-11-04 (a match against a player in Malaysia, a 245 ms round trip; as the one not hosting, "I saw the
        // ball leave my hand late and his ball arrive later than it really did"). A player who isn't hosting calls
        // LookAhead after each snapshot: the balls are drawn `lead` seconds (the round trip) ahead of it, where the host
        // will have them when this player's hands get there, so a block made against what you see lands; and your own
        // throw leaves your hand the moment you let go (launched here from your hand as the host will launch it). A guess
        // the host disagrees with (it blocked or caught the ball) glides over to the real flight (a big one snaps). Two
        // players; and from hosts on 0.3.24, four in FOUR CORNERS (the host's clock and the balls' spin: ahead.snapNow;
        // TASKS.md E48, native 8ea32fc) and FIREBALL BLITZ's fireballs (their spin and bounces and the clock:
        // ahead.fireKnown; my throw a fireball at once, in the slot the host will put it in; TASKS.md E49, native b289ab9).
        public sealed class AheadState
        {
            public bool mine;                 // my throw, flying here since I let go, until the snapshots show it
            public float mineAt;
            public readonly Ball mineBall = new Ball();   // ... as launched
            public bool mineHost;             // ... the host's Throw for it has come
            // what was drawn; where the guess had the ball last frame and how fast it was going there; the correction
            // still gliding (drawn = guess + err)
            public readonly Vec3[] shown = new Vec3[Teams.MaxPlayers], target = new Vec3[Teams.MaxPlayers], tvel = new Vec3[Teams.MaxPlayers], err = new Vec3[Teams.MaxPlayers];
            public readonly bool[] valid = new bool[Teams.MaxPlayers];
            public float snapNow = -1;        // the host's game clock when it sent the latest snapshot (-1: a host before 0.3.15)
            public bool held;                 // my grab or catch, in my hand here until the snapshots show it
            public int heldHand = -1;
            public float heldAt;
            public readonly float[] prevTrig = { 0, 0 };
            public readonly bool[] blk = new bool[Teams.MaxPlayers];     // my block of that ball, flying on here until the snapshots show the host's
            public readonly Ball[] blkBall = { new Ball(), new Ball(), new Ball(), new Ball() };   // ... just after it
            public readonly float[] blkAt = new float[Teams.MaxPlayers], blkReal = new float[Teams.MaxPlayers];   // ... its time as drawn (my clock less the lead's part), and when seen
            // FIREBALL BLITZ (0.3.24, TASKS.md E49): the same for each fireball, by its owner and slot. My throw's fireball
            // (mineBall) is drawn in the slot the host will put it in (the first free one: mineSlot)
            public readonly Vec3[,] fireShown = new Vec3[Teams.MaxPlayers, KMaxFire], fireTarget = new Vec3[Teams.MaxPlayers, KMaxFire],
                                    fireTvel = new Vec3[Teams.MaxPlayers, KMaxFire], fireErr = new Vec3[Teams.MaxPlayers, KMaxFire];
            public readonly bool[,] fireValid = new bool[Teams.MaxPlayers, KMaxFire];
            public readonly bool[,] fireBlk = new bool[Teams.MaxPlayers, KMaxFire];
            public readonly Ball[,] fireBlkBall = NewBalls();
            public readonly float[,] fireBlkAt = new float[Teams.MaxPlayers, KMaxFire], fireBlkReal = new float[Teams.MaxPlayers, KMaxFire];
            public int mineSlot = -1;
            public bool fireKnown;   // the latest snapshot had the fireballs' spin and bounces and the host's clock (0.3.24)

            static Ball[,] NewBalls()
            {
                var b = new Ball[Teams.MaxPlayers, KMaxFire];
                for (int p = 0; p < Teams.MaxPlayers; ++p)
                    for (int k = 0; k < KMaxFire; ++k) b[p, k] = new Ball();
                return b;
            }
        }
        public AheadState ahead = new AheadState();   // (copies share it: only LookAhead changes it, never on a copy)

        // the hand's pose into its history (throw speed, spin, a shield's swing)
        public void RecordHand(int p, int h)
        {
            HandState hs = pl[p].hands[h];
            HandInput hi = pl[p].@in.hand[h];
            float t = now;
            if (pl[p].@in.sampleT >= 0f)
            {   // HAND TIMES (native 0.3.19): the headset's own time for it; a sample already recorded isn't again
                t = pl[p].@in.sampleT;
                if (hs.histN > 0 && t <= hs.histT[(hs.histHead + 63) & 63] + 1e-5f) return;
            }
            hs.hist[hs.histHead] = LauncherPoint(p, h);
            hs.angHist[hs.histHead] = hi.angVel;
            hs.gripHist[hs.histHead] = hi.grip;
            hs.histT[hs.histHead] = t;
            hs.histHead = (hs.histHead + 1) & 63;
            hs.histN = Min(64, hs.histN + 1);
        }

        static bool Flying(Ball b) => b.state == BallState.Flying || b.state == BallState.Returning;

        public void LookAhead(int L, float lead, float dt)
        {
            // (two players; four in FOUR CORNERS from hosts that send what it takes, 0.3.24: their clock with the balls' spin
            // and bounces, ahead.snapNow; FIREBALL BLITZ's fireballs from hosts that send their spin and bounces and the clock,
            // ahead.fireKnown, 0.3.24. An older host's four-player room or FIREBALL BLITZ: the snapshots as they come)
            bool oct = Octagon;
            if (L < 0 || L >= np || dt <= 0) return;
            if (oct ? !ahead.fireKnown : (np != 2 && !(np == 4 && ahead.snapNow >= 0f))) return;
            dt = Min(dt, 0.05f);
            lead = Clampf(lead, 0f, 0.4f);
            now += dt;   // (a client's game isn't stepped: its clock runs here, for the hands' history)
            PlayerState me = pl[L];
            for (int h = 0; h < 2; ++h) RecordHand(L, h);
            bool canThrow = match == MatchState.InProgress || match == MatchState.WaitingForPlayers;
            Ball mb = me.ball;
            // my grab (off the tee) or catch: in my hand at once, by the game's own rules (the host takes it when my
            // trigger reaches it). A catch is judged against the ball as drawn: where the host will have it when my hand
            // gets there
            if (ahead.held)
            {
                if (mb.state == BallState.Held || ahead.mine || !canThrow || now - ahead.heldAt > lead + 0.5f) ahead.held = false;
            }
            else if (!ahead.mine && canThrow && mb.state != BallState.Held && lead > 0.02f)
            {
                Vec3 drawn = ahead.valid[L] ? ahead.shown[L] : mb.pos;
                for (int h = 0; h < 2 && !ahead.held; ++h)
                {
                    HandInput hi = me.@in.hand[h];
                    if (!hi.tracked) continue;
                    bool pressed = hi.trigger > 0.55f && ahead.prevTrig[h] <= 0.55f;
                    float reach = tun.catchHalfHeight + BallRadius(L);
                    bool grab = Grabbable(mb) &&
                                ((pressed && Length(LauncherPoint(L, h) - mb.pos) < 0.25f) || (hi.trigger > 0.5f && InReach(L, h, mb.pos, reach)));
                    bool caught = hi.trigger > 0.5f && Flying(mb) && InReach(L, h, drawn, reach);   // (your own ball, always)
                    if (grab || caught) { ahead.held = true; ahead.heldHand = h; ahead.heldAt = now; }
                }
            }
            for (int h = 0; h < 2; ++h) ahead.prevTrig[h] = me.@in.hand[h].trigger;
            if (ahead.held) { mb.state = BallState.Held; mb.heldHand = ahead.heldHand; mb.vel = mb.spin = default; ahead.valid[L] = false; }
            // a ball in my hand is drawn in my hand as it is now (the snapshot has my hand as the host last heard of it),
            // and its shield is up at once
            if (mb.state == BallState.Held && mb.heldHand >= 0 && mb.heldHand < 2) mb.pos = me.@in.hand[mb.heldHand].grip.Transform(HoldLocal(mb.heldHand));
            for (int h = 0; h < 2; ++h)
                me.hands[h].shieldUp = me.@in.hand[h].tracked && mb.state == BallState.Held && mb.heldHand == h && ShieldCharged(mb) && tun.shields &&
                                       !ahead.mine;   // (thrown here: the snapshots still have it in my hand for a round trip)
            // my throw: the host launches it when my let-go reaches it, from the same hand motion; here it leaves at once
            if (ahead.mine)
            {
                // the host has it once its own Throw (or ThrowFail) for me has come (owner's test, 2026-11-05: a ball caught
                // and sent straight back was taken as the host's at once, its snapshot still had my ball flying home, mine,
                // and the throw drawn here was dropped: the ball flew on past me and snapped back 3.5 m)
                foreach (GameEvent e in events)
                    if ((e.type == EventType.Throw || e.type == EventType.ThrowFail) && e.player == L) ahead.mineHost = true;
                bool hostHas = ahead.mineHost && (mb.state != BallState.Held || mb.heldHand < 0);
                if (hostHas || !canThrow || now - ahead.mineAt > lead + 0.5f)
                {
                    ahead.mine = false;
                    if (!hostHas) ahead.valid[L] = false;   // (the host didn't throw it: back to what it says)
                    int k = ahead.mineSlot;
                    if (oct && k >= 0 && k < KMaxFire)
                    {   // (FIREBALL BLITZ: my fireball, drawn in the slot the host would pick)
                        if (!hostHas) ahead.fireValid[L, k] = false;
                        else if (fire[L][k].state != BallState.Flying)   // (the host put it in another: its glide goes with it)
                            for (int j = 0; j < KMaxFire; ++j)
                                if (j != k && fire[L][j].state == BallState.Flying && !ahead.fireValid[L, j])
                                {
                                    ahead.fireShown[L, j] = ahead.fireShown[L, k]; ahead.fireTarget[L, j] = ahead.fireTarget[L, k];
                                    ahead.fireTvel[L, j] = ahead.fireTvel[L, k]; ahead.fireErr[L, j] = ahead.fireErr[L, k];
                                    ahead.fireValid[L, j] = ahead.fireValid[L, k];
                                    ahead.fireValid[L, k] = false;
                                    break;
                                }
                        ahead.mineSlot = -1;
                    }
                }
            }
            else if (mb.state == BallState.Held && mb.heldHand >= 0 && mb.heldHand < 2 && canThrow && lead > 0.02f)
            {
                int h = mb.heldHand;
                HandInput hi = me.@in.hand[h];
                Vec3 v = HandVelocity(L, h, now);
                float sp = Length(v);
                if (hi.tracked && hi.trigger < 0.35f && sp >= tun.throwMinHandSpeed)
                {
                    Ball keep = mb.Clone();
                    int ne = events.Count;
                    // (from where the host launches it: the ball in the fist, where the last step left it; native 0.3.19)
                    HandState hs = me.hands[h];
                    Pose was = hs.histN >= 2 ? hs.gripHist[(hs.histHead + 62) & 63] : hi.grip;
                    mb.pos = was.Transform(HoldLocal(h));
                    var fireKeep = new Ball[KMaxFire];
                    for (int k = 0; k < KMaxFire; ++k) fireKeep[k] = fire[L][k].Clone();
                    Launch(L, h, Normalize(v), ThrowSpeedFromHandSpeed(sp, L), PeakAngVel(L, h, now));
                    events.RemoveRange(ne, events.Count - ne);   // (the host's Throw comes with a snapshot: its sound plays once)
                    ahead.mineBall.CopyFrom(mb);                 // (as launched: each frame flies it on from here)
                    if (oct)
                    {   // (FIREBALL BLITZ: the launch made it a fireball, in the first free slot, as the host's will)
                        ahead.mineSlot = 0;
                        for (int k = 0; k < KMaxFire; ++k) if (fireKeep[k].state != BallState.Flying) { ahead.mineSlot = k; break; }
                        ahead.mineBall.CopyFrom(fire[L][ahead.mineSlot]);
                        for (int k = 0; k < KMaxFire; ++k) fire[L][k].CopyFrom(fireKeep[k]);   // (the snapshot's, till the host has it)
                        ahead.fireValid[L, ahead.mineSlot] = false;
                    }
                    mb.CopyFrom(keep);
                    ahead.mine = true;
                    ahead.mineHost = false;
                    ahead.mineAt = now;
                    ahead.valid[L] = false;
                }
            }
            // the balls as the host will have them: a copy of the game flies them on from the snapshot (my hands where
            // they are now: where the host will have them then; the other's as last sent). How far: all the lead at my
            // end of the court, where my hands meet a ball (what I do there lands); none at the other's end, where what
            // they do with it (a block, a catch) is drawn where it happens instead of flying on past it and snapping
            // back. A ball coming at me looks a little quicker, one going away a little slower (owner's test, 2026-11-05:
            // snaps of up to 3.5 m when the whole lead flew on past the other's blocks)
            Game c = Clone();
            c.events.Clear();
            // (FIREBALL BLITZ: nobody is knocked out in the copy. A knockout puts out every fireball in the room, and the
            // fireballs flown on after it in this copy would be drawn gone with it)
            if (oct) for (int q = 0; q < np; ++q) if (c.Alive(q)) c.pl[q].health = 1000;
            const float step = 1f / 90f;
            bool two = np == 2 && !oct;
            float xMe = tun.start[L].x, xThem = two ? tun.start[1 - L].x : 0f;
            // (four players, and FIREBALL BLITZ's ring: by the nearest other player still in instead of the other end; all
            // the lead at my place, none at theirs, where what they do with it is drawn where it happens. Across the floor:
            // everyone stands at the same height)
            float Flat(Vec3 a, Vec3 b) { float x = a.x - b.x, z = a.z - b.z; return MathF.Sqrt(x * x + z * z); }
            float Weight(Vec3 at)
            {
                if (two) return Math.Abs(xMe - xThem) < 1f ? 1f : Clampf((at.x - xThem) / (xMe - xThem), 0f, 1f);
                float mine = Flat(at, tun.start[L]);
                float theirs = 1e9f;
                for (int q = 0; q < np; ++q) if (q != L && Alive(q)) theirs = Min(theirs, Flat(at, tun.start[q]));
                return mine + theirs < 1e-3f || theirs > 1e8f ? 1f : Clampf(theirs / (mine + theirs), 0f, 1f);
            }
            bool Away(Ball sb)
            {   // (going away from me: off my block, as the host has it once it has the block)
                if (two) return sb.vel.x * (xThem - xMe) > 0f;
                return sb.vel.x * (sb.pos.x - tun.start[L].x) + sb.vel.z * (sb.pos.z - tun.start[L].z) > 0f;
            }
            // a guess the snapshots correct glides over (about a tenth of a second); a big one snaps. Only a jump in the
            // guess is a correction: where last frame's guess would have the ball now, at its own speed, against where this
            // one has it. A bounce or a block in the guess is drawn as it is (native f11a7e1, 2026-11-10: the glide had kept
            // each ball going the way it was drawn going, so at every bounce and block it flew on and swung back across its
            // path, up to 1.15 m, with no lag at all). Returns the ball as drawn (for balls and fireballs alike, native b289ab9)
            Ball Glide(Ball b, ref Vec3 shown, ref Vec3 target, ref Vec3 tvel, ref Vec3 err, ref bool valid, bool mineToo)
            {
                if (valid)
                {
                    Vec3 jump = default;
                    float sv = Length(b.vel), st = Length(tvel);
                    if (sv < 0.5f || st < 0.5f || Dot(b.vel, tvel) > 0.9f * sv * st)   // (turned: a bounce, not a correction)
                        jump = target + tvel * dt - b.pos;
                    Vec3 corr = err + jump;
                    // (my own throw: a softer glide, over about a fifth of a second)
                    err = Length(corr) < 3f ? corr * MathF.Exp(-dt * (mineToo ? 5f : 10f)) : default;
                }
                else err = default;
                target = b.pos;
                tvel = b.vel;
                Ball o = b.Clone();
                o.pos = b.pos + err;
                shown = o.pos;
                valid = true;
                return o;
            }
            if (oct)
            {   // FIREBALL BLITZ (0.3.24, TASKS.md E49): the fireballs, as the balls are below (a player's own ball never flies
                // there): each one flown on in the copy; my throw's from when I let go, in the slot the host will put it in;
                // my block of one kept until the host has it
                for (int p = 0; p < np; ++p)
                    for (int k = 0; k < KMaxFire; ++k)
                    {
                        if (ahead.fireBlk[p, k])
                        {   // (my block, kept until the snapshots show the host's)
                            Ball sf = fire[p][k];
                            bool hostHas = sf.state == BallState.Flying && sf.instigator == L && Away(sf);
                            if (hostHas || sf.state != BallState.Flying || now - ahead.fireBlkReal[p, k] > lead + 0.5f) ahead.fireBlk[p, k] = false;
                        }
                        Ball b = c.fire[p][k];
                        bool mineHere = p == L && ahead.mine && k == ahead.mineSlot;
                        if (mineHere || ahead.fireBlk[p, k])
                        {   // (a flight launched here: flown from then, as a ball's is)
                            Ball from = mineHere ? ahead.mineBall : ahead.fireBlkBall[p, k];
                            float at = mineHere ? ahead.mineAt : ahead.fireBlkAt[p, k];
                            float w = Weight(ahead.fireValid[p, k] ? ahead.fireShown[p, k] : from.pos);
                            float fly = Max(0f, (now - (1f - w) * lead) - at);
                            b.CopyFrom(from);
                            for (float t = 0; t < fly - 1e-4f && b.state == BallState.Flying; t += step) c.StepFire(p, k, Min(step, fly - t));
                        }
                        else if (b.state == BallState.Flying)
                        {
                            float l = lead * Weight(b.pos);
                            for (float t = 0; t < l - 1e-4f && b.state == BallState.Flying; t += step)
                            {
                                float hh = Min(step, l - t);
                                int ne = c.events.Count;
                                c.StepFire(p, k, hh);
                                bool myBlock = false;
                                for (int i = ne; i < c.events.Count; ++i)
                                    myBlock |= (c.events[i].type == EventType.ShieldBlock || c.events[i].type == EventType.KnuckleBlock) && c.events[i].player == L;
                                if (myBlock && b.state == BallState.Flying && !ahead.fireBlk[p, k])
                                {
                                    ahead.fireBlk[p, k] = true;
                                    ahead.fireBlkBall[p, k].CopyFrom(b);
                                    ahead.fireBlkAt[p, k] = now - lead + t + hh;
                                    ahead.fireBlkReal[p, k] = now;
                                }
                            }
                        }
                        else
                        {
                            ahead.fireValid[p, k] = false;
                            ahead.fireErr[p, k] = default;
                            continue;
                        }
                        if (b.state != BallState.Flying)
                        {   // (it fizzles or hits someone within the lead: as the copy has it)
                            ahead.fireValid[p, k] = false;
                            ahead.fireErr[p, k] = default;
                            fire[p][k].CopyFrom(b);
                            continue;
                        }
                        fire[p][k].CopyFrom(Glide(b, ref ahead.fireShown[p, k], ref ahead.fireTarget[p, k], ref ahead.fireTvel[p, k],
                                                  ref ahead.fireErr[p, k], ref ahead.fireValid[p, k], p == L));
                    }
                if (ahead.mine) { mb.state = BallState.Dead; mb.heldHand = -1; }   // (out of my hand: the host's tee ball comes)
                return;
            }
            // my block, once seen, is kept until the snapshots show the host's (owner's test, 2026-11-05: flown again from
            // each snapshot against my hand as it is now, gone on past the block, the ball flipped between two paths every
            // other frame: a zigzagging tail)
            for (int p = 0; p < np; ++p)
            {
                if (!ahead.blk[p]) continue;
                Ball sb = pl[p].ball;
                bool hostHas = Flying(sb) && sb.instigator == L && Away(sb);
                if (hostHas || !Flying(sb) || now - ahead.blkReal[p] > lead + 0.5f) ahead.blk[p] = false;
            }
            for (int p = 0; p < np; ++p)
            {
                Ball b = c.pl[p].ball;
                // a flight launched here (my throw, my block): flown from then on by the time the ball is drawn at. That
                // time runs `lead` ahead at my end and with the snapshots at the other's: here (now - (1 - w) * lead) on
                // my clock
                bool local = (p == L && ahead.mine) || ahead.blk[p];
                if (local)
                {
                    Ball from = p == L && ahead.mine ? ahead.mineBall : ahead.blkBall[p];
                    float at = p == L && ahead.mine ? ahead.mineAt : ahead.blkAt[p];
                    float w = Weight(ahead.valid[p] ? ahead.shown[p] : from.pos);
                    float fly = Max(0f, (now - (1f - w) * lead) - at);
                    b.CopyFrom(from);
                    for (float t = 0; t < fly - 1e-4f && Flying(b); t += step) c.StepBall(p, Min(step, fly - t));
                }
                else if (Flying(b))
                {
                    float l = lead * Weight(b.pos);
                    for (float t = 0; t < l - 1e-4f && Flying(b); t += step)
                    {
                        float hh = Min(step, l - t);
                        int ne = c.events.Count;
                        c.StepBall(p, hh);
                        bool myBlock = false;
                        for (int i = ne; i < c.events.Count; ++i)
                            myBlock |= (c.events[i].type == EventType.ShieldBlock || c.events[i].type == EventType.KnuckleBlock) && c.events[i].player == L;
                        if (myBlock && Flying(b) && !ahead.blk[p])
                        {   // (blocked at the time it is drawn at, t + h after the snapshot)
                            ahead.blk[p] = true;
                            ahead.blkBall[p].CopyFrom(b);
                            ahead.blkAt[p] = now - lead + t + hh;
                            ahead.blkReal[p] = now;
                        }
                    }
                }
                else
                {
                    ahead.valid[p] = false;
                    ahead.err[p] = default;
                    continue;
                }
                if (!Flying(b))
                {   // (it lands, is caught or goes home within the lead: drawn as the copy has it)
                    ahead.valid[p] = false;
                    ahead.err[p] = default;
                    pl[p].ball.CopyFrom(b);
                    continue;
                }
                pl[p].ball.CopyFrom(Glide(b, ref ahead.shown[p], ref ahead.target[p], ref ahead.tvel[p], ref ahead.err[p], ref ahead.valid[p], p == L));
            }
        }
    }
}
