// The gameplay simulation of the native game (app/src/game.h, game.cpp), ported 1:1 to C#: same rules, same
// numbers, same order of float maths, native coordinates (right-handed, +Y up; +X toward player 1's end). Platform
// independent (no Unity code); tested by Tests/EditMode/GameTests.cs, the port of the native tests/test_game.cpp.
// Ported from native commit 0eea0e7 (release 0.3.8), brought up to e5cc0ae (0.3.15) for up to four players
// (task A1-A5; the practice bot: Sim/Bot.cs; the EXPERIMENTAL arenas: Sim/ExpArena.cs; FIREBALL BLITZ: Sim/Blitz.cs). Quoted rules are
// the original game's own words.
using System;
using System.Collections.Generic;
using static ProjectArena.Sim.M;

namespace ProjectArena.Sim
{
    public partial class Game
    {
        public Tunables tun = new Tunables();
        public PlayerState[] pl = { new PlayerState(), new PlayerState(), new PlayerState(), new PlayerState() };
        public int np = 2;          // the players in this match (tun.players, taken at reset)
        public MatchState match = MatchState.WaitingForPlayers;
        public float stateTimer;
        public bool meetBumped;     // host: the fist bump happened; the short pause before both go to their ends
        public float roundClock;    // Basic: match clock; Advanced: unused (no time limit)
        public bool overtime;
        public int round;
        public int lastRoundWinner = -1, matchWinner = -1;
        public List<CollisionTri> arena = new List<CollisionTri>();
        public List<GameEvent> events = new List<GameEvent>();
        public float now;
        public bool meetLog;        // a meeting diagnostic is ready (main.cpp logs it): closest fist gap, triggers
        public float meetGap;
        public float[] meetTrig = { 0, 0 };

        float lastFistLog;
        uint rng = 0x12345u;

        static readonly Vec3 kUp = new Vec3(0, 1, 0);

        public void SetArena(List<CollisionTri> tris) { arenaKind = -1; arena = new List<CollisionTri>(tris); }
        // each end's foul line, from the centre (the arena's, moved by the FOUL LINE option in the classic room)
        public float FoulLine() => tun.foulLineX - (arenaKind <= Arena.Classic && np == 2 ? tun.FoulMove() : 0f);
        // THE REACH ROUND THE FIST: within hand h's reach to grab or catch (its catcher's, and round the fist when that
        // plays), r the reach (m)
        public Vec3 FistPoint(int p, int h) => pl[p].@in.hand[h].grip.Transform(tun.fistReachOffset);
        public bool InReach(int p, int h, Vec3 pos, float r) => Length(CatcherPoint(p, h) - pos) < r || (tun.FistReachNow() && Length(FistPoint(p, h) - pos) < r);
        // p's own ball could be taken by hand h now, the trigger pulled (the game shows p it is in reach)
        public bool BallInReach(int p, int h)
        {
            Ball b = pl[p].ball;
            if (!pl[p].@in.hand[h].tracked || b.state == BallState.Held || b.scored) return false;
            if (!Grabbable(b) && b.state != BallState.Flying && b.state != BallState.Returning) return false;
            return InReach(p, h, b.pos, tun.catchHalfHeight + BallRadius(p));
        }
        public void RngSeed(uint seed) => rng = seed != 0 ? seed : 1u;   // (the bot's chances; tests vary them)

        float Rnd()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / (float)0x1000000;
        }

        void Emit(EventType t, int p, int h, Vec3 pos, float v = 0, int ball = -1) => events.Add(new GameEvent(t, p, h, pos, v, ball));

        // ---- helpers exposed for rendering / tests
        public Vec3 IdlePoint(int p) => tun.spawner[p] + new Vec3(0, tun.spawnerIdleHeight, 0);
        public float BallRadius(int p) => tun.projectileRadius * pl[p].ball.scale;
        // a ball that raises its shield when held: charged, or any ball with SHIELDS: ALWAYS ON (MAIDEN)
        public bool ShieldCharged(Ball b) => b.charged || tun.MaidenNow();
        // a hand as the rules have it (a bot's through a throw: its arm drawn turned, its rules as before: botPhysQ; TASKS.md
        // E41, native a812558)
        public Pose HandPose(int p, int h)
        {
            PlayerState s = pl[p];
            Pose g = s.@in.hand[h].grip;
            if (s.isBot && s.botHandSet) g.q = s.botPhysQ[h];
            return g;
        }
        public Vec3 LauncherPoint(int p, int h) => HandPose(p, h).Transform(tun.launcherOffset);
        // where a ball held in hand h sits, in its grip space (THE BALL IN THE LEFT PALM: holdOffset, mirrored for the left)
        // NEW BOT (TASKS.md E55, native 75f3f1f, 0.3.23; the owner, 2026-11-20: "a randomize new bot button to give them a
        // random stance ... and a different outfit ... hosts may not want it to change so we need a push to change button"):
        // the bots' stances come from this (each corner its own), the same every match until NEW BOT gives a new one; 0: a
        // new stance each match, at random (as before; the tests). The app keeps it (settings) and makes the outfits from it
        public uint botLook;
        public int BotStanceFor(int p, float level)   // the stance botLook gives corner p's bot at that level
        {
            uint h = botLook ^ (0x9E3779B9u * (uint)(p + 1));
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return level < 1.5f ? kOpenStances[h % 3] : (int)(h % Bot.Stances);
        }

        public Vec3 HoldLocal(int h)
        {
            Vec3 o = tun.holdOffset;
            if (h == 0 && tun.PalmHoldNow()) o.x = -o.x;
            return o;
        }
        public Vec3 CatcherPoint(int p, int h) => HandPose(p, h).Transform(tun.catcherOffset);
        public Vec3 KnucklePoint(int p, int h) => KnucklePose(p, h).p;
        public Pose KnucklePose(int p, int h)
        {
            Vec3 o = tun.KnuckleOffsetNow();
            if (h == 0) o.x = -o.x;   // left hand mirror (HandnessRoot)
            Pose g = HandPose(p, h);
            return new Pose(g.q, g.Transform(o));
        }

        public Vec3 RecentHandVel(int p, int h, Vec3 local, float t)
        {
            // velocity of a grip-local point over a short window, from the recorded grip poses
            HandState hs = pl[p].hands[h];
            if (hs.histN < 2) return default;
            int newest = (hs.histHead + 63) & 63, oldest = newest;
            for (int i = 1; i < hs.histN; ++i)
            {
                int k = (newest - i + 64) & 63;
                oldest = k;
                if (hs.histT[newest] - hs.histT[k] >= tun.handVelWindow) break;   // (from the newest sample: HAND TIMES)
            }
            float dt = hs.histT[newest] - hs.histT[oldest];
            if (dt <= 1e-4f) return default;
            return (hs.gripHist[newest].Transform(local) - hs.gripHist[oldest].Transform(local)) / dt;
        }

        bool HitsBox(Pose box, Vec3 half, Vec3 from, Vec3 to, float r, out Vec3 n) => HitsBox(box, half, from, to, r, out n, out _);
        // (entry: how far along from -> to it met the box)
        bool HitsBox(Pose box, Vec3 half, Vec3 from, Vec3 to, float r, out Vec3 n, out float entry)
        {
            n = default;
            entry = 0f;
            Quat inv = box.q.Conj();
            Vec3 a = inv.Rotate(from - box.p), b = inv.Rotate(to - box.p);
            Vec3 e = half + new Vec3(r, r, r);
            float t0 = 0, t1 = 1;
            Vec3 d = b - a;
            int axis = -1;
            for (int i = 0; i < 3; ++i)
            {
                if (MathF.Abs(d[i]) < 1e-8f) { if (a[i] < -e[i] || a[i] > e[i]) return false; continue; }
                float ta = (-e[i] - a[i]) / d[i], tb = (e[i] - a[i]) / d[i];
                if (ta > tb) (ta, tb) = (tb, ta);
                if (ta > t0) { t0 = ta; axis = i; }
                t1 = Min(t1, tb);
                if (t0 > t1) return false;
            }
            if (axis < 0)
            {   // already overlapping at segment start: push out along the least-penetrated face
                float best = 1e9f;
                for (int i = 0; i < 3; ++i) { float pen = e[i] - MathF.Abs(a[i]); if (pen < best) { best = pen; axis = i; } }
            }
            Vec3 ln = default;
            ln[axis] = (a[axis] + d[axis] * t0) >= 0 ? 1f : -1f;
            n = box.q.Rotate(ln);
            if (Dot(n, to - from) > 0) n = -n;
            entry = t0;
            return true;
        }

        // WALL GRIP (BOUNCES, native 0.3.22; the contact from pull request #26): the ball, a solid sphere of this radius,
        // meets a surface (normal n toward the ball, moving at surfaceVel). Per unit mass: the normal impulse jn = -(1 + e)
        // un; the slip at the contact point (the arm -R n) is the sideways motion plus spin x arm; the impulse that would
        // stop it is -2/7 of it (inertia 2/5 m R^2), at most friction * jn (Coulomb). It changes the spin by 5 / (2 R^2)
        // (arm x jt) (at most maxSpin). Returns the new velocity (the spin changed in b); a ball already leaving keeps its own
        static Vec3 BounceWithFriction(Ball b, Vec3 n, Vec3 surfaceVel, float radius, float restitution, float friction, float maxSpin)
        {
            Vec3 rel = b.vel - surfaceVel;
            float un = Dot(rel, n);
            if (un >= 0f) return b.vel;
            float jn = -(1f + restitution) * un;
            Vec3 arm = -n * radius;
            Vec3 slip = rel - n * un + Cross(b.spin, arm);
            Vec3 jt = slip * (-2f / 7f);
            float m = Length(jt);
            if (m > friction * jn) jt *= friction * jn / m;
            b.spin += Cross(arm, jt) * (2.5f / (radius * radius));
            float s = Length(b.spin);
            if (s > maxSpin) b.spin *= maxSpin / s;
            return b.vel + n * jn + jt;
        }
        bool GripsNow() => np == 2 && tun.WallGripNow();   // (BOUNCES: WALL GRIP, in a two-player room)

        // a flying ball's turn over h seconds: BANK ASSIST's turn still to come (BOUNCES), then the Magnus curve from its
        // spin, at constant speed (SpeedLimit FixedToInitialSpeed)
        static void AdvanceFlight(Ball b, Tunables tun, float h)
        {
            if (Length2(b.bankTurn) > 0f && b.state == BallState.Flying && !b.fouled && !b.homing)
            {
                float left = Length(b.bankTurn);
                float turn = MathF.Min(left, Deg2Rad(tun.bankTurnRate) * h);
                b.vel = Quat.AxisAngle(b.bankTurn / left, turn).Rotate(b.vel);
                b.bankTurn *= (left - turn) / left;
            }
            if (Length2(b.spin) > 1e-4f)
            {
                b.vel += Cross(b.spin, b.vel) * (tun.magnusFactor * h);
                b.vel = Normalize(b.vel) * b.speedLimit;
                b.spin *= MathF.Exp(-tun.spinDecayPerSecond * h);
            }
        }

        // BANK ASSIST (BOUNCES, native 0.3.22; from pull request #26): at a bounce of a player's own throw, the leg after it
        // is flown on (no further bounce: a surface ahead means it is still setting up another bank) to the other player's
        // plane. If it would miss them by at most 0.85 m and the turn it needs, atan2(miss, V (T - 0.08)), is at most 8
        // degrees, it is turned toward their chest by that or BankAssistDeg(), whichever is less (bankTurnRate a second,
        // its speed kept), if that brings it nearer. Planned once; their moves after it don't steer it
        void PlanBankAim(Ball b, Vec3 from, float radius)
        {
            if (!b.bankAimPending || tun.BankAssistNow() == 0 || np != 2 || b.state != BallState.Flying || b.fouled || b.homing ||
                b.instigator < 0 || b.instigator >= 2 || b.speedLimit < tun.recallSpeed) return;
            int opponent = 1 - b.instigator;
            if (!pl[opponent].@in.headTracked) return;
            Vec3 head = pl[opponent].@in.head.p;
            Vec3 target = ChestPoint(opponent);
            float forward = Facing(b.instigator).x;
            if ((target.x - from.x) * forward < 0.1f || b.vel.x * forward <= 0f) return;
            float step = MathF.Min(1f / 120f, 0.05f / b.speedLimit);
            // (the leg flown on to their plane: false if it meets the court first, or, unturned, them)
            bool Arrival(Ball src, bool missOnly, out Vec3 at, out float elapsed)
            {
                at = default; elapsed = 0f;
                Ball flight = src.Clone();
                flight.pos = from;
                for (float t = 0; t < 2.5f; t += step)
                {
                    AdvanceFlight(flight, tun, step);
                    Vec3 next = flight.pos + flight.vel * step;
                    bool crosses = (next.x - target.x) * forward >= 0f;
                    float fraction = crosses ? (target.x - flight.pos.x) / (next.x - flight.pos.x) : 1f;
                    next = Lerp(flight.pos, next, fraction);
                    if (SweepArena(flight.pos, next, radius, out _, out _, out _) ||
                        (missOnly && BodyTouchesPath(head, flight.pos, next, radius))) return false;
                    if (crosses) { at = next; elapsed = t + step * fraction; return true; }
                    flight.pos = next;
                }
                return false;
            }
            if (!Arrival(b, true, out Vec3 at0, out float el0)) return;
            Vec3 miss = target - at0;
            float distance = Length(miss);
            float needed = MathF.Atan2(distance, b.speedLimit * MathF.Max(0.01f, el0 - 0.08f));
            if (distance > 0.85f || needed > Deg2Rad(8f) || Length2(Cross(b.vel, miss)) < 1e-10f) return;
            Ball steered = b.Clone();
            steered.bankTurn = Normalize(Cross(b.vel, miss)) * MathF.Min(needed, Deg2Rad(tun.BankAssistDeg()));
            if (Arrival(steered, false, out Vec3 at1, out _) && Length2(target - at1) < Length2(miss))
            {
                b.bankTurn = steered.bankTurn;
                b.bankAimPending = false;
            }
        }

        // (contactRadius > 0: WALL GRIP, the guard grips the ball where it meets it)
        void DeflectMoving(Ball b, Vec3 n, Vec3 vs, int deflector, float assistDeg, Vec3 from, bool punch = false, float contactRadius = 0f)
        {
            Vec3 @out;
            if (contactRadius > 0f)   // (WALL GRIP: the guard grips it; its spin as the contact leaves it)
                @out = BounceWithFriction(b, n, vs, contactRadius, tun.deflectRestitution, tun.knuckleFriction, tun.maxSpin);
            else
            {
                Vec3 rel = b.vel - vs;
                float vn = Dot(rel, n);
                if (vn < 0) rel = rel - n * ((1f + tun.deflectRestitution) * vn);
                @out = rel + vs;
                b.spin *= 0.5f;
            }
            if (punch)
            {
                // a knuckle-guard hit is a punch: follow the fist's motion and always send the ball toward the opponent's end
                float hs = Length(vs);
                if (hs > 1.5f) @out = Normalize(Lerp(Normalize(@out), vs / hs, 0.5f)) * Length(@out);
                // (toward: the deflector's facing; two players: +X for player 0, -X for player 1, as it always was)
                Vec3 toward = Facing(deflector);
                float sp0 = Length(@out);
                Vec3 dd = sp0 > 1e-4f ? @out / sp0 : toward;
                float along = Dot(dd, toward);
                if (along < 0.35f) { dd = dd - toward * along + toward * 0.35f; dd = Normalize(dd); }
                @out = dd * sp0;
            }
            float cap = tun.projectileMaxSpeed + tun.PerStrike() * StrikesHeld(deflector);
            float sp = Clampf(Length(@out), tun.minSpeed, cap);
            Vec3 dir = Normalize(@out), tgt = AssistDir(deflector, from, Normalize(@out));
            float ang = MathF.Acos(Clampf(Dot(dir, tgt), -1f, 1f));
            if (ang > 1e-4f) dir = Normalize(Lerp(dir, tgt, Min(1f, Deg2Rad(assistDeg) / ang)));
            b.vel = dir * sp;
            b.speedLimit = sp;
            b.instigator = deflector;
            b.bankAimPending = false;   // (BANK ASSIST: a throw's, not a block's)
            b.bankTurn = default;
            b.rechargePending = false;
            b.scored = false;
            b.timer = 0;   // (the self-hit grace, after a block as after a throw: native af99864)
            b.progX = b.pos.x; b.progZ = b.pos.z; b.progT = 0;
        }

        public Pose ShieldLocal(int h)
        {
            // Candidate placements (grip space: +X right, +Y up/thumb, -Z forward). side = +1 right hand, -1 left hand.
            float side = h == 1 ? 1f : -1f;
            switch (tun.shieldPlacement & 3)
            {
                default:
                case 0: return new Pose(Quat.Identity, tun.shieldOffset);                                              // above, ahead (the original's shield placement)
                // on the back of the hand, standing out from the outside of the arm and running back along the forearm (plate
                // normal = grip X, long side along the forearm), as in the owner's reference from the original (2026-10-17)
                case 1: return new Pose(GripEuler(tun.shieldBackRot, h == 0), new Vec3(side * tun.shieldBackPos.x, tun.shieldBackPos.y, tun.shieldBackPos.z));
                case 2: return new Pose(Quat.AxisAngle(new Vec3(0, 0, 1), -side * kPi * 0.5f), new Vec3(side * 0.10f, 0.0f, -0.08f));   // back of the hand
                case 3: return new Pose(Quat.AxisAngle(new Vec3(0, 0, 1), side * kPi * 0.5f), new Vec3(-side * 0.10f, 0.0f, -0.08f));   // palm side
            }
        }

        static float SegSegDist(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
        {
            Vec3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r), c = Dot(d1, r), b = Dot(d1, d2);
            float s = 0, t = 0;
            float den = a * e - b * b;
            if (den > 1e-9f) s = Clampf((b * f - c * e) / den, 0f, 1f);
            t = e > 1e-9f ? (b * s + f) / e : 0f;
            if (t < 0) { t = 0; s = a > 1e-9f ? Clampf(-c / a, 0f, 1f) : 0f; }
            else if (t > 1) { t = 1; s = a > 1e-9f ? Clampf((b - c) / a, 0f, 1f) : 0f; }
            return Length((p1 + d1 * s) - (p2 + d2 * t));
        }

        // closest distance between two fists (segments), metres
        public float FistGap(int p0, int h0, int p1, int h1)
        {
            Pose g0 = pl[p0].@in.hand[h0].grip;
            Pose g1 = pl[p1].@in.hand[h1].grip;
            return SegSegDist(g0.Transform(tun.fistBack), g0.Transform(tun.fistFront), g1.Transform(tun.fistBack), g1.Transform(tun.fistFront));
        }

        // The meeting point (native e0cc7f7, owner 2026-10-28): a ring for each player over the centre line, at chest
        // height between the two. A fist closed inside its ring lights it (offered); a bump counts only between two fists
        // in their rings, one of them offered for meetOfferTime first: nobody can force the start.
        public float[] meetLitT = { -1, -1 };   // host: since when each player has had a fist lit in its ring (-1: not)

        // a ball you can take off the spawner: resting on it, or already up and rising to it (native 0.3.19, owner
        // 2026-11-10: a hand went through the rising ball)
        public bool Grabbable(Ball b) => b.state == BallState.Idle || (b.state == BallState.Spawning && b.timer <= tun.spawnRiseTime);

        // CATCHES ONLINE (native 0.3.19): a player who isn't hosting sees the balls one round trip late; their catch is
        // also judged against the ball where it was then (seenLate[p], set by the host each frame; 0: the host, bots,
        // offline), within LateCatchSlack either way, never more than LateCatchMax back
        public float[] seenLate = new float[Teams.MaxPlayers];
        public bool lateCatch = true;   // (tests: off to compare)
        public const float LateCatchSlack = 0.04f, LateCatchMax = 0.25f;
        public sealed class BallTrack
        {
            public const int N = 40;
            public readonly Vec3[] p = new Vec3[N];
            public readonly float[] t = new float[N];
            public int n, head;
            public void CopyFrom(BallTrack o) { Array.Copy(o.p, p, N); Array.Copy(o.t, t, N); n = o.n; head = o.head; }
        }
        public BallTrack[] track = { new BallTrack(), new BallTrack(), new BallTrack(), new BallTrack() };
        // BLOCKS ONLINE (TASKS.md E61, native e0292de; a player's report, 2026-11-11: "a shield held low didn't deflect;
        // always got hit"). A ball that would hit a player who isn't hosting, while they have a shield or knuckle guard
        // up, waits where it touched them for their round trip (seenLate, at most LateBlockMax): their hands as they
        // arrive meanwhile are judged against the ball where it was when they saw it. A block there deflects it from that
        // point; none, and the hit counts as before. The host, bots and offline play are as they were.
        public bool lateBlock = true;   // (tests: off to compare)
        public const float LateBlockMax = 0.25f;
        // what it decided, for the session log (native 489711e): late blocks counted, waits that ended in a hit, and the
        // last one's player, kind (0 shield, 1 knuckle guard, 2 a hit) and round trip (s)
        public int lateBlocks, lateWaitHits, lateLastPlayer = -1, lateLastKind = -1;
        public float lateLastRtt;
        // player q's hand hh blocks ball `ball` moving from -> to: its shield (pass 0) or its knuckle guard (pass 1); the
        // ball deflected off it, the events emitted (native Game::tryBlock, e0292de: the step's own check and BLOCKS
        // ONLINE's late one)
        public bool TryBlock(Ball b, int ball, int q, int hh, int pass, Vec3 from, Vec3 to, float r)
        {
            Vec3 n;
            if (pass == 0 && HitsShield(q, hh, from, to, r, out n))
            {
                Vec3 vs = RecentHandVel(q, hh, ShieldLocal(hh).p, now);
                DeflectMoving(b, n, vs, q, tun.advanced ? tun.shieldAimAssistAdvancedDeg : tun.shieldAimAssistBasicDeg, from);
                if (tun.MaidenNow())
                {   // SHIELDS: ALWAYS ON (MAIDEN): a block, and the shield stays
                    Emit(EventType.ShieldBlock, q, hh, from, 0, ball);
                    return true;
                }
                if (!(np == 4 && tun.teamMode == Teams.ThreeVOne && q == tun.theOne))
                {   // (THE ONE's shield never breaks)
                    pl[q].ball.charged = false;        // "Shield breaks on hit": the held ball loses its charge
                    pl[q].hands[hh].shieldUp = false;
                }
                Emit(EventType.ShieldBlock, q, hh, from, 0, ball);
                Emit(EventType.ShieldBreak, q, hh, from);
                return true;
            }
            if (pass == 1 && pl[q].hands[hh].knuckleUp && HitsBox(KnucklePose(q, hh), tun.knuckleHalf, from, to, r, out n, out float entry))
            {
                Pose guard = KnucklePose(q, hh);
                Vec3 lo = tun.KnuckleOffsetNow();
                if (hh == 0) lo.x = -lo.x;
                // (WALL GRIP: the guard's speed where the ball meets it, its nearest point (the guard's frame is the grip's),
                // so a guard turning as it punches spins the ball one way or the other; pull request #26)
                bool grip = GripsNow();
                if (grip)
                {
                    Vec3 at = guard.q.Conj().Rotate(Lerp(from, to, entry) - guard.p), hf = tun.knuckleHalf;
                    lo += new Vec3(Clampf(at.x, -hf.x, hf.x), Clampf(at.y, -hf.y, hf.y), Clampf(at.z, -hf.z, hf.z));
                }
                Vec3 vs = RecentHandVel(q, hh, lo, now);
                // bounce off the guard's front face (fist forward), not whichever box face was entered first
                Vec3 face = guard.q.Rotate(new Vec3(0, 0, -1));
                if (Dot(face, b.vel - vs) > 0) face = -face;
                n = Normalize(face * 0.75f + n * 0.25f);
                DeflectMoving(b, n, vs, q, tun.knuckleAimAssistDeg, from, true, grip ? r : 0f);
                Emit(EventType.KnuckleBlock, q, hh, from, Length(b.vel), ball);
                return true;
            }
            return false;
        }

        // p's own ball, as p saw it, came within reach of `at`
        public bool SawBallNear(int p, Vec3 at, float reach)
        {
            float late = Min(seenLate[p], LateCatchMax);
            if (!lateCatch || late <= 0f) return false;
            BallTrack k = track[p];
            float from = now - late - LateCatchSlack, to = now - late + LateCatchSlack;
            for (int i = 0; i + 1 < k.n; ++i)
            {   // newest first: each stretch of the path between two steps
                int i1 = (k.head + BallTrack.N - 1 - i) % BallTrack.N, i0 = (i1 + BallTrack.N - 1) % BallTrack.N;
                if (k.t[i1] < from) break;
                if (k.t[i0] > to) continue;
                Vec3 a = k.p[i0], ab = k.p[i1] - a;
                float u = Clampf(Dot(at - a, ab) / MathF.Max(1e-6f, Dot(ab, ab)), 0f, 1f);
                if (Length(at - (a + ab * u)) < reach) return true;
            }
            return false;
        }
        public Vec3 MeetRing(int p)            // the centre of p's ring at the meeting point
        {
            float y = 0.5f * ((pl[0].@in.headTracked ? pl[0].@in.head.p.y : 1.62f) + (pl[1].@in.headTracked ? pl[1].@in.head.p.y : 1.62f));
            return new Vec3(p == 0 ? -tun.meetRingX : tun.meetRingX, Clampf(y - 0.45f, 0.9f, 1.5f), 0f);   // chest height between them
        }
        public bool InMeetRing(int p, int h)   // that fist's knuckles are inside p's ring
        {
            HandInput hi = pl[p].@in.hand[h];
            if (!hi.tracked) return false;
            Vec3 d = HandPose(p, h).Transform(tun.fistFront) - MeetRing(p);
            return MathF.Abs(d.x) < tun.meetRingDepth && d.y * d.y + d.z * d.z < tun.meetRingRadius * tun.meetRingRadius;
        }
        public bool MeetLit(int p, int h)      // ... and closed: its ring lights up (offered)
        {
            HandInput hi = pl[p].@in.hand[h];
            return (hi.trigger > 0.5f || hi.squeeze > 0.5f) && InMeetRing(p, h);
        }

        public Pose ShieldPose(int p, int h) => HandPose(p, h) * ShieldLocal(h);

        public Vec3 StrikeZoneCenter(int p)
        {
            if (p < 2)
            {   // (as it always was: behind player 0 is -X)
                float back = p == 0 ? -1f : 1f;
                return tun.start[p] + new Vec3(back * tun.strikeZoneBack, tun.strikeZoneHeight, 0);
            }
            return tun.start[p] - Facing(p) * tun.strikeZoneBack + new Vec3(0, tun.strikeZoneHeight, 0);
        }

        public Vec3 PeakAngVel(int p, int h, float t)
        {
            HandState hs = pl[p].hands[h];
            Vec3 best = default;
            for (int i = 0; i < hs.histN; ++i)
            {
                int k = (hs.histHead - 1 - i + 128) & 63;
                if (hs.histT[(hs.histHead + 63) & 63] - hs.histT[k] > tun.spinWindow) break;   // (from the newest sample)
                if (Length2(hs.angHist[k]) > Length2(best)) best = hs.angHist[k];
            }
            return best;
        }

        public Vec3 ChestPoint(int p) => pl[p].@in.head.p - new Vec3(0, 0.35f, 0);

        // teams: two players are each their own; FOUR CORNERS by tun.teamMode, and FIREBALL BLITZ too when the room can
        // play in teams (0.3.24, TASKS.md E51: Tunables.BlitzTeamsNow; else everyone for themselves). Rivals: different
        // teams; friendly: two players on the same team (never in a free-for-all)
        // FIREBALL BLITZ plays in teams only when the players who start (startMask: everyone until the countdown) are on
        // both of them; else everyone for themselves (native 3fccaf9, review 2026-11-21: practice's PLAYERS: 2 in 2 V 2, or
        // a room's THE ONE in an empty corner, put everyone in one team, and the match ended the moment it began)
        public int TeamsNow()
        {
            if (Octagon && !tun.BlitzTeamsNow()) return Teams.FFA;
            int m = Math.Max(0, Math.Min(2, tun.teamMode));
            return Octagon && m != Teams.FFA && !BothTeams(m, startMask) ? Teams.FFA : m;
        }
        // whether the players in `mask` are on both teams of team mode m (2 V 2: corners 0, 1 against 2, 3; 3 V 1: THE ONE
        // against the rest)
        public bool BothTeams(int m, byte mask)
        {
            uint seen = 0;
            for (int p = 0; p < np; ++p)
                if (((mask >> p) & 1) != 0) seen |= 1u << (m == Teams.TwoVTwo ? (p < 2 ? 0 : 1) : (p == tun.theOne ? 1 : 0));
            return seen == 3u;
        }
        public int Team(int p)
        {
            if (np == 2) return p;
            int m = TeamsNow();   // (FIREBALL BLITZ: everyone for themselves, unless the room can play in teams: 0.3.24)
            if (m == Teams.TwoVTwo) return p < 2 ? 0 : 1;   // partners in opposite corners (FIREBALL BLITZ: across the ring)
            if (m == Teams.ThreeVOne) return p == tun.theOne ? 1 : 0;
            return p;
        }
        public int TeamCount() => np == 2 ? 2 : TeamsNow() == Teams.FFA ? 4 : 2;
        public bool Friendly(int a, int b) => a >= 0 && b >= 0 && a < np && b < np && a != b && Team(a) == Team(b);
        public int TeamsIn()   // FIREBALL BLITZ: how many teams (everyone for themselves: players) still have someone in
        {
            uint inMask = 0;
            for (int p = 0; p < np; ++p) if (Alive(p)) inMask |= 1u << (Team(p) & 3);
            int n = 0;
            for (; inMask != 0; inMask &= inMask - 1) ++n;
            return n;
        }
        public int MaxHealth(int p) =>   // FIREBALL BLITZ: the health p starts a match with (3 v 1's THE ONE: three times HEALTH)
            Math.Max(1, tun.health) * (np == 4 && TeamsNow() == Teams.ThreeVOne && p == tun.theOne ? 3 : 1);
        public int Leader()   // the team ahead on its own (-1: level at the top)
        {
            int w = -1, top = -1;
            for (int t = 0; t < TeamCount(); ++t)
            {
                int sc = TeamScore(t);
                if (sc > top) { top = sc; w = t; } else if (sc == top) w = -1;
            }
            return w;
        }
        public int TeamScore(int t)
        {
            int s = 0;
            for (int p = 0; p < np; ++p) if (Team(p) == t) s += pl[p].score;
            return s;
        }
        public bool Rivals(int a, int b) => a >= 0 && b >= 0 && a != b && Team(a) != Team(b) && Alive(a) && Alive(b);
        public int StrikesHeld(int p) => np == 2 ? pl[1 - p].strikesAgainst : pl[p].strikesFor;
        // the rival a ball leaving `from` along `dir` is aimed at most nearly (two players: the other one)
        public int AimTarget(int p, Vec3 from, Vec3 dir)
        {
            if (np == 2) return 1 - p;
            int best = -1;
            float bd = -2f;
            for (int q = 0; q < np; ++q)
            {
                if (!Rivals(p, q)) continue;
                float d = Dot(Normalize(ChestPoint(q) - from), dir);
                if (d > bd) { bd = d; best = q; }
            }
            return best >= 0 ? best : (p + 1) % np;
        }
        // TRAINING: aim assist for p's throws toward the nearest of these points (the targets) instead of a rival
        public List<Vec3>[] aimAt = { new List<Vec3>(), new List<Vec3>(), new List<Vec3>(), new List<Vec3>() };
        // the aim assist's way: toward the rival it is aimed at most nearly (TRAINING: the target nearest the ball's line)
        public Vec3 AssistDir(int p, Vec3 from, Vec3 dir)
        {
            if (aimAt[p].Count == 0) return Normalize(ChestPoint(AimTarget(p, from, dir)) - from);
            Vec3 to = dir;
            float best = -2f;
            foreach (Vec3 t in aimAt[p])
            {
                Vec3 d = Normalize(t - from);
                if (Dot(d, dir) > best) { best = Dot(d, dir); to = d; }
            }
            return to;
        }

        public float ThrowSpeedFromHandSpeed(float s, int thrower)
        {
            // lerp(minThrown, maxThrown + strikes * SpeedIncreasePerStrike, t)
            float t = Clampf((s - tun.minInputSpeed) / Max(1e-3f, tun.maxInputSpeed - tun.minInputSpeed), 0f, 1f);
            int strikes = StrikesHeld(thrower);
            return Lerpf(tun.projectileMinSpeed, tun.projectileMaxSpeed + strikes * tun.PerStrike(), t);
        }

        public Vec3 HandVelocity(int p, int h, float t)
        {
            // Average velocity of the launcher point over the throw's averaging window (throwVelocityWindow).
            HandState hs = pl[p].hands[h];
            if (hs.histN < 2) return default;
            int newest = (hs.histHead + 63) & 63;
            int oldest = newest;
            for (int i = 1; i < hs.histN; ++i)
            {
                int k = (newest - i + 64) & 63;
                oldest = k;
                // (counted back from the newest sample, not from `t`: native 0.3.19, HAND TIMES; the same wherever the
                // newest sample is now)
                if (hs.histT[newest] - hs.histT[k] >= tun.throwVelocityWindow) break;
            }
            float dt = hs.histT[newest] - hs.histT[oldest];
            if (dt <= 1e-4f) return default;
            return (hs.hist[newest] - hs.hist[oldest]) / dt;
        }

        // the event's name (logs, sound mapping). One table for everything: two private copies once fell behind the enum
        // and a new event (FistBump) read past their end and crashed both headsets. Unknown values give "?".
        public static string EventName(EventType t)
        {
            switch (t)
            {
                case EventType.Spawn: return "Spawn";               case EventType.Grab: return "Grab";
                case EventType.Throw: return "Throw";               case EventType.ThrowFail: return "ThrowFail";
                case EventType.Bounce: return "Bounce";             case EventType.ShieldBlock: return "ShieldBlock";
                case EventType.ShieldBreak: return "ShieldBreak";   case EventType.HitPlayer: return "HitPlayer";
                case EventType.Returned: return "Returned";         case EventType.Caught: return "Caught";
                case EventType.Foul: return "Foul";                 case EventType.RoundStart: return "RoundStart";
                case EventType.RoundEnd: return "RoundEnd";         case EventType.MatchEnd: return "MatchEnd";
                case EventType.Countdown: return "Countdown";       case EventType.KnuckleBlock: return "KnuckleBlock";
                case EventType.Recharge: return "Recharge";         case EventType.Strike: return "Strike";
                case EventType.Overtime: return "Overtime";         case EventType.SpawnRise: return "SpawnRise";
                case EventType.FistBump: return "FistBump";         case EventType.LeadChange: return "LeadChange";
                case EventType.Knockout: return "Knockout";         case EventType.Fizzle: return "Fizzle";
                case EventType.RingClose: return "RingClose";
            }
            return "?";
        }

        public void Reset(bool player1IsBot) => ResetWithBots(player1IsBot ? 2u : 0u);
        public void ResetWithBots(uint botMask)   // bit p: player p is a bot (four players: any of them)
        {
            np = tun.players == 4 ? 4 : 2;
            if (np == 4) tun.advanced = false;   // FOUR CORNERS: Basic's rules (and no shot clock: StepPlayer)
            // FIREBALL BLITZ: everyone in the ring until the countdown (who is there plays), no fireballs
            startMask = ringMask = (byte)((1u << np) - 1u);
            ringCloseAt = -1;
            lastKnockBy = -1;
            foreach (Ball[] row in fire) foreach (Ball f in row) { f.Reset(); f.state = BallState.Dead; }
            if (Octagon) LayoutRing(ringMask);
            for (int p = 0; p < Teams.MaxPlayers; ++p)
            {
                PlayerState s = pl[p];
                s.Reset();
                if (p >= np) continue;
                s.isBot = ((botMask >> p) & 1u) != 0;
                s.@in.head.p = tun.start[p] + new Vec3(0, 1.65f, 0);
                SpawnBall(p);
                s.ball.pos = IdlePoint(p);
                s.botTimer = 1.0f;
                s.health = tun.health;
            }
            match = MatchState.WaitingForPlayers;
            stateTimer = 0;
            meetBumped = false;
            meetLitT[0] = meetLitT[1] = -1;
            round = 0;
            overtime = false;
            roundClock = tun.advanced ? tun.advancedRoundTime : tun.basicMatchTime;
            lastRoundWinner = matchWinner = -1;
            events.Clear();
        }

        void SpawnBall(int p)
        {
            Ball b = pl[p].ball;
            b.state = BallState.Spawning;
            b.timer = tun.respawnDelay + tun.postSpawnDelay + tun.spawnPreTime + tun.spawnRiseTime;
            b.vel = default;
            b.spin = default;
            b.bankTurn = default;
            b.bankAimPending = false;
            b.heldHand = -1;
            b.instigator = p;
            b.charged = true;              // a new ball comes charged (it recharges your shield)
            b.scored = false;
            b.rechargePending = false;
            b.bounces = 0;
            b.fouled = b.homing = false;
            b.hitWaitAt = b.blockWaitAt = -1f;
            b.scale = StrikeScale(StrikesHeld(p));
        }

        public float StrikeScale(int n)
        {
            int early = Min(n, tun.fullScaleSteps), late = Max(0, n - tun.fullScaleSteps);
            float full = Min(tun.maxBallScale, 1f + tun.scaleIncreasePerStrike * early + tun.lateScaleIncreasePerStrike * late);
            return 1f + (full - 1f) * tun.GrowthShare();   // (BALL GROWTH: that share of the regulation growth)
        }

        void Launch(int p, int h, Vec3 dir, float speed, Vec3 spin, bool assist = true)
        {
            Ball b = pl[p].ball;
            if (assist && !Octagon)
            {   // (the bot's attack plan aims exactly: no assist; FIREBALL BLITZ: none, a fireball's drop is part of the aim)
                Vec3 to = AssistDir(p, b.pos, dir);
                float deg = tun.AimAssistDeg();   // (AIM ASSIST: the rules' own, Basic MEDIUM and Advanced LOW, or the room's)
                float ang = MathF.Acos(Clampf(Dot(dir, to), -1f, 1f));
                if (ang > 1e-4f) dir = Normalize(Lerp(dir, to, Min(1f, Deg2Rad(deg) / ang)));
            }
            // (BANK ASSIST, BOUNCES: a player's own throw at the other player may have a bank's last leg turned; not the
            // bots', nor TRAINING's throws at its targets)
            b.bankAimPending = assist && np == 2 && tun.BankAssistNow() > 0 && !pl[p].isBot && aimAt[p].Count == 0;
            b.bankTurn = default;
            float sl = Length(spin);
            if (sl > tun.maxSpin) spin *= tun.maxSpin / sl;
            b.state = BallState.Flying;
            b.vel = dir * Max(speed, tun.minSpeed);
            b.speedLimit = Length(b.vel);
            b.spin = spin;
            b.heldHand = -1;
            b.instigator = p;
            b.scored = false;
            b.rechargePending = true;      // "To recharge your ball, you must throw it past your foul line"
            b.bounces = 0;
            b.timer = 0;
            b.fouled = b.homing = false;
            b.hitWaitAt = b.blockWaitAt = -1f;
            b.progX = b.pos.x; b.progZ = b.pos.z; b.progT = 0;
            Emit(EventType.Throw, p, h, b.pos, b.speedLimit);
            if (Octagon) ToFire(p);
        }

        // TRAINING (and tests): p's ball thrown from `from` as a hand would (a Throw; aim assist unless !assist). Its events
        // come with the next step's (served between steps)
        List<GameEvent> served = new List<GameEvent>();
        public void Serve(int p, Vec3 from, Vec3 dir, float speed, Vec3 spin, bool assist = true)
        {
            pl[p].ball.pos = from;
            int n = events.Count;
            Launch(p, -1, Normalize(dir), speed, spin, assist);
            served.Add(events[n]);   // (native: events.back() straight after the Throw; a fireball's tee adds none)
        }

        void Deflect(Ball b, Vec3 n, int deflector, float assistDeg, Vec3 from)
        {
            float sp = Length(b.vel);
            Vec3 r = b.vel - n * (2 * Dot(b.vel, n));
            Vec3 dir = Normalize(r), tgt = AssistDir(deflector, from, dir);
            float ang = MathF.Acos(Clampf(Dot(dir, tgt), -1f, 1f));
            if (ang > 1e-4f) dir = Normalize(Lerp(dir, tgt, Min(1f, Deg2Rad(assistDeg) / ang)));
            b.vel = dir * sp;
            b.instigator = deflector;      // OnRep_Instigator: scoring / zone rules follow the last deflector
            b.bankAimPending = false;      // (BANK ASSIST: a throw's, not a block's)
            b.bankTurn = default;
            b.rechargePending = false;
            b.scored = false;
            b.spin *= 0.5f;
            b.timer = 0;   // (the self-hit grace, after a block as after a throw: native af99864)
            b.progX = b.pos.x; b.progZ = b.pos.z; b.progT = 0;
        }

        public bool SweepArena(Vec3 from, Vec3 to, float r, out Vec3 hitPos, out Vec3 hitN, out uint tags)   // (public: BALL MARKS ask it which surface a bounce was on, as native 0.3.17)
        {
            // Discrete test at the end point (callers sub-step finely). Picks the deepest penetration,
            // preferring tagged zones (foul/return) over plain walls when both overlap.
            hitPos = hitN = default;
            tags = 0;
            float best = 1e9f;
            bool hit = false;
            Vec3 motion = to - from;
            foreach (CollisionTri t in arena)
            {
                Vec3 ab = t.b - t.a, ac = t.c - t.a, ap = to - t.a;
                float d1 = Dot(ab, ap), d2 = Dot(ac, ap);
                Vec3 q;
                if (d1 <= 0 && d2 <= 0) q = t.a;
                else
                {
                    Vec3 bp = to - t.b; float d3 = Dot(ab, bp), d4 = Dot(ac, bp);
                    if (d3 >= 0 && d4 <= d3) q = t.b;
                    else
                    {
                        float vc = d1 * d4 - d3 * d2;
                        if (vc <= 0 && d1 >= 0 && d3 <= 0) q = t.a + ab * (d1 / (d1 - d3));
                        else
                        {
                            Vec3 cp = to - t.c; float d5 = Dot(ab, cp), d6 = Dot(ac, cp);
                            if (d6 >= 0 && d5 <= d6) q = t.c;
                            else
                            {
                                float vb = d5 * d2 - d1 * d6;
                                if (vb <= 0 && d2 >= 0 && d6 <= 0) q = t.a + ac * (d2 / (d2 - d6));
                                else
                                {
                                    float va = d3 * d6 - d5 * d4;
                                    if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) q = t.b + (t.c - t.b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                                    else { float den = 1f / (va + vb + vc); q = t.a + ab * (vb * den) + ac * (vc * den); }
                                }
                            }
                        }
                    }
                }
                Vec3 d = to - q;
                float dist = Length(d);
                if (dist >= r) continue;
                Vec3 n = dist > 1e-6f ? d / dist : t.n;
                // the foul zones are a slab 5 cm over the floor with no face at the line: a ball touching its edge
                // bounced off the corner, nearly straight up (native 8051266). They bounce as floor.
                if ((t.tags & Tag.Foul) != 0) n = t.n;
                if (Dot(motion, n) >= 0) continue;   // already separating
                float score = dist - ((t.tags & (Tag.Foul | Tag.Return)) != 0 ? 0.05f : 0f);
                if (score < best) { best = score; hitPos = q + n * r; hitN = n; tags = t.tags; hit = true; }
            }
            return hit;
        }

        bool HitsBody(int v, Vec3 pos, float r) => BodyTouches(pl[v].@in.head.p, pos, r);
        // whether a ball of radius r at pos touches a body whose head is at `head`
        public bool BodyTouches(Vec3 head, Vec3 pos, float r)
        {
            if (Length(pos - head) < tun.headRadius + r) return true;
            Vec3 a = head - new Vec3(0, tun.torsoTop, 0);
            Vec3 b = head - new Vec3(0, tun.torsoBottom, 0);
            if (b.y < 0.3f) b.y = 0.3f;
            Vec3 ab = b - a;
            float t = Clampf(Dot(pos - a, ab) / Max(1e-6f, Dot(ab, ab)), 0f, 1f);
            if (Length(pos - (a + ab * t)) < tun.torsoRadius + r) return true;
            if (!Octagon) return false;
            Vec3 hip0 = b, hip1 = head - new Vec3(0, tun.hipBottom, 0);   // (FIREBALL BLITZ: the hips and seat too)
            if (hip1.y < 0.3f) return false;
            float u = Clampf((pos.y - hip1.y) / Max(1e-4f, hip0.y - hip1.y), 0f, 1f);
            return Length(pos - Lerp(hip1, hip0, u)) < tun.hipRadius + r;
        }

        bool HitsShield(int p, int h, Vec3 from, Vec3 to, float r, out Vec3 n)
        {
            n = default;
            if (!pl[p].hands[h].shieldUp) return false;
            Pose s = ShieldPose(p, h);
            Quat inv = s.q.Conj();
            Vec3 a = inv.Rotate(from - s.p), b = inv.Rotate(to - s.p);
            Vec3 e = tun.ShieldHalfNow() + new Vec3(r, r, r);   // (SHIELD SIZE)
            float t0 = 0, t1 = 1;
            Vec3 d = b - a;
            for (int i = 0; i < 3; ++i)
            {
                if (MathF.Abs(d[i]) < 1e-8f) { if (a[i] < -e[i] || a[i] > e[i]) return false; continue; }
                float ta = (-e[i] - a[i]) / d[i], tb = (e[i] - a[i]) / d[i];
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Max(t0, ta); t1 = Min(t1, tb);
                if (t0 > t1) return false;
            }
            Vec3 up = s.q.Rotate(kUp);
            n = Dot(to - from, up) > 0 ? -up : up;
            return true;
        }

        void ScorePoint(int scorer, int victim, Vec3 where, int ball = -1)
        {
            if (match != MatchState.InProgress) return;
            int ledBy = np == 4 ? Leader() : -1;
            // (3 v 1: THE ONE's hits count double)
            pl[scorer].score += np == 4 && tun.teamMode == Teams.ThreeVOne && scorer == tun.theOne ? tun.onePoints : 1;
            if (np == 2 && tun.botAdapt && pl[scorer].isBot != pl[victim].isBot)   // MATCH MY LEVEL: toward close matches
                tun.botMatch = Clampf(tun.botMatch + (pl[scorer].isBot ? -tun.botMatchStep : tun.botMatchStep), 0f, Bot.Levels - 1f);
            pl[victim].strikesAgainst = 0;   // "If you get hit, any strikes against you are removed."
            pl[victim].strikesFor = 0;       // (four players: and the strikes they held)
            Emit(EventType.HitPlayer, victim, -1, where, (float)scorer, ball);
            if (np == 4 && !overtime && Leader() >= 0 && Leader() != ledBy) Emit(EventType.LeadChange, -1, -1, default, (float)Leader());
            if (!tun.advanced)
            {
                if (overtime)
                {   // sudden death
                    matchWinner = Team(scorer);
                    match = MatchState.MatchSummary;
                    Emit(EventType.MatchEnd, matchWinner, -1, default, 0);   // (the winning team, as on the clock)
                }
            }
            else if (overtime || pl[scorer].score >= tun.advancedPointsToWin)
            {   // (overtime: a timed round's sudden death)
                EndRound(scorer);
            }
        }

        // Advanced: a round won (by points, at time, or in sudden death)
        void EndRound(int scorer)
        {
            lastRoundWinner = scorer;
            pl[scorer].roundWins++;
            match = MatchState.RoundSummary;
            stateTimer = tun.roundSummaryTime;
            Emit(EventType.RoundEnd, scorer, -1, default, (float)round);
        }

        // ball: whose ball did it; by: who earns it (four players: their ball grows)
        public void AddStrike(int against, Vec3 where, int ball = -1, int by = -1)
        {
            if (match != MatchState.InProgress) return;
            if (pl[against].strikesAgainst >= tun.maxStrikes) return;
            pl[against].strikesAgainst++;
            if (np == 4 && by >= 0 && by < np && pl[by].strikesFor < tun.maxStrikes) pl[by].strikesFor++;
            Emit(EventType.Strike, against, -1, where, (float)pl[against].strikesAgainst, ball);
        }

        void StepBall(int p, float dt, int ignoredPlayer = -1)   // (ignoredPlayer: a bot's read of a ball, it can't be hit)
        {
            PlayerState s = pl[p];
            Ball b = s.ball;
            // stepped growth: ease toward the size for the current strike count
            {
                float tgt = StrikeScale(StrikesHeld(p));
                b.scale += (tgt - b.scale) * Min(1f, dt * tun.scaleGrowRate);
            }
            switch (b.state)
            {
                case BallState.Dead:
                case BallState.Spawning:
                {
                    b.timer -= dt;
                    float pre = tun.respawnDelay + tun.postSpawnDelay + tun.spawnPreTime;
                    float total = pre + tun.spawnRiseTime;
                    float e = b.state == BallState.Spawning ? Clampf(total - b.timer, 0f, total) : 0f;
                    if (e < pre)
                    {   // hidden under the platform while the pre-spawn rings play
                        b.pos = tun.spawner[p] + new Vec3(0, -2f * tun.spawnDepth + tun.spawnDepth * (e / pre), 0);
                    }
                    else
                    {   // appears at the launcher point and springs up to the idle point (slight overshoot)
                        if (e - dt < pre) Emit(EventType.SpawnRise, p, -1, tun.spawner[p] + new Vec3(0, tun.spawnLauncherHeight, 0));
                        float r0 = Clampf((e - pre) / Max(1e-3f, tun.spawnRiseTime), 0f, 1f);
                        r0 = r0 * r0 * (3f - 2f * r0);   // ease in as well as out: a smooth journey up
                        const float c1 = 0.9f, c3 = c1 + 1f;
                        float k = 1f + c3 * MathF.Pow(r0 - 1f, 3f) + c1 * MathF.Pow(r0 - 1f, 2f);   // easeOutBack
                        b.pos = Lerp(tun.spawner[p] + new Vec3(0, tun.spawnLauncherHeight, 0), IdlePoint(p), k);
                    }
                    b.vel = default;
                    if (b.timer <= 0)
                    {
                        b.state = BallState.Idle;
                        b.pos = IdlePoint(p);
                        Emit(EventType.Spawn, p, -1, b.pos);
                    }
                    return;
                }
                case BallState.Idle:
                {
                    Vec3 acc = (IdlePoint(p) - b.pos) * tun.springK - b.vel * tun.springDamping;   // a damped spring
                    b.vel += acc * dt;
                    b.pos += b.vel * dt;
                    return;
                }
                case BallState.Held:
                    b.pos = HandPose(p, b.heldHand).Transform(HoldLocal(b.heldHand));   // in the fist (throws use the launcher)
                    return;
                case BallState.Returning:
                case BallState.Flying: break;
            }

            if (b.blockWaitAt >= 0f)
            {   // BLOCKS ONLINE: it reached a player who isn't hosting; held there until their block could have come
                int v = b.blockWaitBy;
                float late = v >= 0 && v < np ? Min(seenLate[v], LateBlockMax) : 0f;
                Vec3 held = b.pos;
                float rb = BallRadius(p);
                // their hands as they arrive now, against the ball's path as they saw it (about their round trip ago), up
                // to where it reached them
                BallTrack k = track[p];
                float t0 = now - late - LateCatchSlack, t1 = now - late + LateCatchSlack;
                for (int pass = 0; pass < 2; ++pass)
                    for (int hh = 0; hh < 2; ++hh)
                        for (int i = 0; i + 1 < k.n; ++i)
                        {   // newest first: each stretch of the path between two steps
                            int i1 = (k.head + BallTrack.N - 1 - i) % BallTrack.N, i0 = (i1 + BallTrack.N - 1) % BallTrack.N;
                            if (k.t[i1] < t0) break;
                            if (k.t[i0] > t1 || k.t[i1] > b.blockWaitAt + 1e-4f) continue;
                            b.pos = k.p[i0];
                            b.vel = b.blockWaitVel;
                            if (TryBlock(b, p, v, hh, pass, k.p[i0], k.p[i1], rb))
                            {   // blocked in time, as they saw it
                                b.blockWaitAt = -1f;
                                ++lateBlocks; lateLastPlayer = v; lateLastKind = pass; lateLastRtt = seenLate[v];
                                return;
                            }
                        }
                b.pos = held;
                b.vel = default;
                if (now - b.blockWaitAt < late + LateCatchSlack) return;
                b.blockWaitAt = -1f;   // no block came: the hit, as it would have been
                ++lateWaitHits; lateLastPlayer = v; lateLastKind = 2; lateLastRtt = v >= 0 && v < np ? seenLate[v] : 0f;
                b.scored = true;
                ScorePoint(b.instigator, v, b.pos, p);
                b.state = BallState.Dead;
                b.timer = tun.respawnDelay + tun.postSpawnDelay + 0.5f;
                return;
            }
            if (b.hitWaitAt >= 0f)
            {   // CATCHES ONLINE: it reached its owner; held there until their catch could have come
                b.vel = default;
                int self0 = b.instigator;
                float wait = self0 >= 0 && self0 < np ? Min(seenLate[self0], LateCatchMax) + LateCatchSlack : 0f;
                if (now - b.hitWaitAt < wait) return;
                b.hitWaitAt = -1f;   // no catch came: the hit, as it would have been
                b.scored = true;
                if (np == 2 && self0 >= 0 && self0 < 2) ScorePoint(1 - self0, self0, b.pos, p);
                b.state = BallState.Dead;
                b.timer = tun.respawnDelay + tun.postSpawnDelay + 0.5f;
                return;
            }
            b.timer += dt;
            if (b.homing)
            {   // a fouled ball on its way home after its bounce off the wall
                Vec3 d = IdlePoint(p) - b.pos;
                if (Length(d) < 0.35f || b.timer > 6f)
                {
                    b.state = BallState.Idle;
                    b.homing = b.fouled = false;
                    b.vel = b.vel * 0.3f;
                    b.spin = default;
                    return;
                }
                // a magnetic pull: the velocity swings toward home (a curve, not a sharp turn) at the return speed
                Vec3 want = Normalize(d) * tun.foulReturnSpeed;
                b.vel += (want - b.vel) * Min(1f, dt * 7f);
            }
            void GoHome()   // drawn home, harmless, by the same magnetic pull as a foul
            {
                b.state = BallState.Returning;
                b.heldHand = -1;
                b.instigator = p;
                b.spin = default;
                b.scored = true;
                b.fouled = false;
                b.homing = true;
                b.rechargePending = false;
                b.timer = 0;
                b.speedLimit = tun.foulReturnSpeed;
                Emit(EventType.ThrowFail, p, -1, b.pos, Length(b.vel));
            }
            // a short throw goes home: not yet past the owner's foul line, and slow, heading back, or taking too long
            if (b.state == BallState.Flying && b.instigator == p && b.rechargePending)
            {
                // (along the player's facing: two players, +X or -X exactly as it always was)
                bool behind = Dot(b.pos, Facing(p)) < -FoulLine();
                float forward = Dot(b.vel, Facing(p));   // toward the opponent
                if (behind && (b.speedLimit < tun.recallSpeed || forward < 0 || b.timer > tun.recallTime)) { GoHome(); return; }
            }
            // a stuck ball goes home: no headway along the court, or slowed to a crawl by its bounces (see Tunables)
            if (b.state == BallState.Flying)
            {
                // (headway: along the court; four players, across the room's floor plan)
                float moved = np == 2 ? MathF.Abs(b.pos.x - b.progX) : Length(new Vec3(b.pos.x - b.progX, 0, b.pos.z - b.progZ));
                if (moved >= tun.stuckDistance) { b.progX = b.pos.x; b.progZ = b.pos.z; b.progT = b.timer; }
                if (b.timer - b.progT > tun.stuckTime || (b.speedLimit < tun.recallSpeed && b.timer > tun.slowGrace)) { GoHome(); return; }
            }
            // a fouled ball that finds no wall to turn home from (bouncing between floor and ceiling) turns home anyway
            if (b.fouled && b.timer > tun.stuckTime)
            {
                b.fouled = false;
                b.homing = true;
                b.timer = 0;
                b.speedLimit = tun.foulReturnSpeed;
            }
            float r = BallRadius(p);
            int sub = Max(1, (int)MathF.Ceiling(Length(b.vel) * dt / 0.02f));
            float h = dt / sub;
            for (int i = 0; i < sub; ++i)
            {
                AdvanceFlight(b, tun, h);   // (BANK ASSIST's turn, then the Magnus curve from spin)
                Vec3 from = b.pos, to = b.pos + b.vel * h;
                // recharge once the owner's throw crosses the owner's foul line
                if (b.rechargePending && b.instigator == p && Dot(to, Facing(p)) > -FoulLine())
                {
                    b.rechargePending = false;
                    if (!b.charged) Emit(EventType.Recharge, p, -1, to);
                    b.charged = true;
                    pl[p].shotClock = tun.shotClockSeconds;                         // throw past the foul line resets it
                    if (np == 2 && tun.useShotClock && pl[1 - p].shotClock <= 0) AddStrike(1 - p, to);   // automatic strike (no ball's doing)
                }
                bool handled = b.state == BallState.Returning;   // a returning ball can't be blocked / score
                // shields and knuckle guards of the player who did NOT last touch the ball (four players: every rival of the
                // one who did; a teammate's ball passes through you)
                // shields take priority over the knuckle bumpers when both are in the ball's path
                int by0 = b.instigator;
                for (int pass = 0; pass < 2 && !handled; ++pass)
                for (int qi = 0; qi < np && !handled; ++qi)
                {
                    int q = np == 2 ? 1 - by0 : qi;
                    if (np == 2 ? qi > 0 : !Rivals(by0, q)) continue;
                    for (int hh = 0; hh < 2 && !handled; ++hh)
                    {
                        if (TryBlock(b, p, q, hh, pass, from, to, r))
                        {
                            to = from + b.vel * h;
                            handled = true;
                        }
                    }
                }
                // the player who did not last touch the ball gets hit (four players: any rival of the one who did)
                for (int vi = 0; vi < np; ++vi)
                {
                    int victim = np == 2 ? 1 - b.instigator : vi;
                    if (np == 2 ? vi > 0 : !Rivals(b.instigator, victim)) continue;
                    if (b.state == BallState.Flying)
                    {   // strike: the ball crosses the victim's strike-zone ring inside its radius
                        // (a step that ends on the ring's plane counts, and the next, starting there, doesn't: once; native
                        // 5d3bbec, pull request #26)
                        static bool CrossesPlane(float q0, float q1) => (q0 < 0f && q1 >= 0f) || (q0 > 0f && q1 <= 0f);
                        Vec3 c = StrikeZoneCenter(victim);
                        if (np == 2)
                        {
                            if (CrossesPlane(from.x - c.x, to.x - c.x))
                            {
                                float t = (c.x - from.x) / (to.x - from.x);
                                Vec3 x = from + (to - from) * t;
                                if (Length(new Vec3(0, x.y - c.y, x.z - c.z)) < tun.StrikeRadius()) AddStrike(victim, x, p);   // (STRIKE ZONE)
                            }
                        }
                        else
                        {   // (the ring faces its player's facing)
                            Vec3 f = Facing(victim);
                            float a0 = Dot(from - c, f), a1 = Dot(to - c, f);
                            if (CrossesPlane(a0, a1))
                            {
                                Vec3 x = from + (to - from) * (a0 / (a0 - a1));
                                Vec3 off = x - c;
                                if (Length(off - f * Dot(off, f)) < tun.StrikeRadius()) AddStrike(victim, x, p, b.instigator);
                            }
                        }
                    }
                    if (!handled && !b.scored && victim != ignoredPlayer && HitsBody(victim, to, r))
                    {
                        if (lateBlock && seenLate[victim] > 0f &&
                            (pl[victim].hands[0].shieldUp || pl[victim].hands[1].shieldUp || pl[victim].hands[0].knuckleUp ||
                             pl[victim].hands[1].knuckleUp))
                        {   // (BLOCKS ONLINE: their block may still be coming)
                            b.blockWaitVel = b.vel;
                            b.pos = to;
                            b.vel = default;
                            b.blockWaitAt = now;
                            b.blockWaitBy = victim;
                            return;
                        }
                        b.scored = true;
                        ScorePoint(b.instigator, victim, to, p);
                        b.state = BallState.Dead;
                        b.timer = tun.respawnDelay + tun.postSpawnDelay + 0.5f;
                        return;
                    }
                }
                // your own ball can hit you on its way back (rebound or slow return); the opponent gets the point
                {
                    int self = b.instigator;
                    Vec3 toSelf = pl[self].@in.head.p - to;
                    if (tun.selfHitScores && !b.scored && b.timer > tun.selfHitGrace && Dot(b.vel, toSelf) > 0 &&
                        self != ignoredPlayer && pl[self].@in.headTracked && HitsBody(self, to, r))
                    {
                        if (lateCatch && seenLate[self] > 0f)
                        {   // (CATCHES ONLINE: their catch may still be coming)
                            b.pos = to;
                            b.vel = default;
                            b.hitWaitAt = now;
                            return;
                        }
                        b.scored = true;
                        if (np == 2) ScorePoint(1 - self, self, to, p);   // (four players: nobody's point, the ball is spent)
                        b.state = BallState.Dead;
                        b.timer = tun.respawnDelay + tun.postSpawnDelay + 0.5f;
                        return;
                    }
                }
                {
                    if (SweepArena(from, to, r, out Vec3 hp, out Vec3 hn, out uint tags))
                    {
                        if (arenaKind <= Arena.Classic && np == 2 && tun.FoulMove() != 0f && (tags & Tag.Return) == 0)
                        {   // FOUL LINE moved (native 0.3.20): each end's floor, ceiling and side walls past its line are its
                            // player's foul zone (the arena's own zone starts at 4.75 m; the back walls stay as they are)
                            tags &= ~(Tag.Foul | Tag.Team0 | Tag.Team1);
                            int end = hp.x < 0f ? 0 : 1;
                            if (Dot(hp, Facing(end)) < -FoulLine()) tags |= Tag.Foul | (end != 0 ? Tag.Team1 : Tag.Team0);
                        }
                        int zoneTeam = (tags & Tag.Team0) != 0 ? 0 : (tags & Tag.Team1) != 0 ? 1 : (tags & Tag.Team2) != 0 ? 2 : (tags & Tag.Team3) != 0 ? 3 : -1;
                        bool own = (zoneTeam >= 0 && zoneTeam == b.instigator);   // (a zone of the thrower's own team)
                        bool returning = b.state == BallState.Returning;
                        // (the other player's ball you blocked or punched: only your end's floor counts, native c96c2f5,
                        // owner 2026-10-30; its walls and ceiling are walls)
                        bool deflectedIn = b.instigator != p;
                        if ((tags & Tag.Foul) != 0 && own && !returning && (!deflectedIn || hn.y > 0.7f))
                        {
                            // a foul. Deflecting the opponent's ball into your own foul zone gives them a strike.
                            // The original dissolved the ball; here (owner, 2026-10-21) it stays in the arena: a ball that never
                            // crossed its owner's foul line springs straight back to its float position; one that did bounces on,
                            // off the wall it is heading for, and flies home, harmless (it can't hit or score; its owner can catch it)
                            // (their ball off your floor: their strike, but no FOUL shown: it was never your throw)
                            if (!deflectedIn) Emit(EventType.Foul, b.instigator, -1, hp, (float)zoneTeam);
                            if (b.instigator != p)
                            {
                                // blocked or punched into the blocker's own zone: their strike, and the ball comes back to its
                                // owner as from their back wall, live: catch it, move, or be hit (native 26e4f8f, owner 2026-10-29)
                                AddStrike(b.instigator, hp, p);
                                Emit(EventType.Returned, p, -1, hp, 0f);
                                b.state = BallState.Returning;
                                b.instigator = p;
                                b.rechargePending = false;
                                b.scored = false;
                                b.fouled = b.homing = false;
                                b.pos = hp + hn * 0.01f;
                                b.vel = Normalize(ChestPoint(p) - b.pos) * tun.returnSpeed;
                                b.speedLimit = tun.returnSpeed;
                                b.spin *= 0.3f;
                                b.bounces = 0;
                                b.timer = 0;
                                return;
                            }
                            if (b.instigator == p && b.rechargePending)
                            {   // bounce off, then drawn home (owner: faster, magnetic)
                                b.state = BallState.Returning;
                                b.heldHand = -1;
                                b.spin = default;
                                b.rechargePending = false;
                                b.scored = true;
                                b.fouled = false;
                                b.homing = true;
                                b.timer = 0;
                                b.speedLimit = tun.foulReturnSpeed;
                                b.vel = (b.vel - hn * (2 * Dot(b.vel, hn))) * 0.6f;
                                b.pos = hp + hn * 0.01f;
                                return;
                            }
                            b.state = BallState.Returning;
                            b.instigator = p;
                            b.rechargePending = false;
                            b.scored = true;
                            b.fouled = true;
                            b.homing = false;
                            b.timer = 0;
                            b.speedLimit = Max(tun.foulMinSpeed, Min(b.speedLimit, tun.foulReturnSpeed));   // its own speed (native ac8dfff)
                            b.vel = Normalize(b.vel - hn * (2 * Dot(b.vel, hn))) * b.speedLimit;   // bounce off the floor
                            b.pos = hp + hn * 0.01f;
                            return;
                        }
                        if (b.fouled && MathF.Abs(hn.y) < 0.7f)
                        {   // the wall it was heading for: bounce, then home
                            b.fouled = false;
                            b.homing = true;
                            b.timer = 0;
                            b.speedLimit = tun.foulReturnSpeed;
                            b.pos = hp + hn * 0.01f;
                            b.vel = Normalize(IdlePoint(p) - b.pos) * b.speedLimit;
                            Emit(EventType.Bounce, p, -1, hp, 0f);
                            return;
                        }
                        if ((tags & Tag.Return) != 0)
                        {
                            bool ownerWall = (zoneTeam == p);
                            if (ownerWall)
                            {
                                // "Your ball has gone past you and hit your back wall ... your ball will respawn in front of you."
                                Emit(EventType.Returned, p, -1, hp, 1f);
                                SpawnBall(p);
                                return;
                            }
                            if (!returning)
                            {
                                // opponent's back wall: start the slow return toward the owner's end (not homing on the owner)
                                Emit(EventType.Returned, p, -1, hp, 0f);
                                // initial trajectory toward where the owner is now; it does not keep seeking afterwards
                                Vec3 aim = ChestPoint(p);
                                b.state = BallState.Returning;
                                b.instigator = p;
                                b.rechargePending = false;
                                b.vel = Normalize(aim - hp) * tun.returnSpeed;
                                b.speedLimit = tun.returnSpeed;
                                b.spin *= 0.3f;
                                b.bounces = 0;
                                b.timer = 0;
                                b.pos = hp + hn * 0.01f;
                                return;
                            }
                        }
                        // BOUNCES (native 0.3.22, the host's): WALLS: LIVELY, a side wall cuts only the ball's speed into it
                        // (sideWallElasticity); WALL GRIP, the floor, ceiling and side walls grip it as a solid sphere (its
                        // sideways motion and spin traded: it leaves spinning, and curves). STANDARD: the mirror, the
                        // original's spin kick, and the whole speed cut to elasticity. (A side wall: the classic court's by its
                        // facing, the rooms built here by their tag)
                        bool sideWall = arenaKind <= Arena.Classic ? MathF.Abs(hn.z) > 0.9f : (tags & Tag.SideWall) != 0;
                        bool lively = np == 2 && sideWall && tun.WallsLivelyNow();
                        if (!returning && tun.bouncesChangeSpeed && (GripsNow() || lively))
                        {
                            float e = lively ? tun.sideWallElasticity : tun.elasticity;
                            Vec3 v;
                            float sp;
                            if (GripsNow())
                            {
                                v = BounceWithFriction(b, hn, default, r, e, tun.bankFriction, tun.maxSpin);
                                sp = Length(v);
                            }
                            else
                            {   // (LIVELY alone: the speed into the wall cut, then the original's spin kick, which turns it)
                                v = b.vel - hn * ((1f + e) * Dot(b.vel, hn));
                                sp = Length(v);
                                v += Cross(b.spin, hn) * (tun.spinBounceFactor * r);
                                b.spin *= (1f - tun.spinBounceFactor);
                            }
                            b.speedLimit = Max(tun.minSpeed, sp);
                            b.vel = Normalize(v) * b.speedLimit;
                        }
                        else
                        {
                            Vec3 v = b.vel - hn * (2 * Dot(b.vel, hn));
                            // spin grips the wall: tangential kick, then lose some spin
                            v += Cross(b.spin, hn) * (tun.spinBounceFactor * r);
                            b.spin *= (1f - tun.spinBounceFactor);
                            if (tun.bouncesChangeSpeed && !returning) b.speedLimit = Max(tun.minSpeed, b.speedLimit * tun.elasticity);
                            b.vel = Normalize(v) * b.speedLimit;
                        }
                        b.bankTurn = default;      // (BANK ASSIST: a turn is for the leg it was planned for;
                        PlanBankAim(b, hp, r);     //  this leg's, if it may have one)
                        b.bounces++;
                        Emit(EventType.Bounce, p, -1, hp, b.speedLimit);
                        to = hp;
                        if (b.bounces > tun.maxBounces) { SpawnBall(p); return; }
                    }
                }
                b.pos = to;
                if (b.pos.y < tun.outLow || b.pos.y > tun.outHigh || MathF.Abs(b.pos.x) > tun.outX || MathF.Abs(b.pos.z) > tun.outZ) { SpawnBall(p); return; }
            }
            if (b.timer > 10f) SpawnBall(p);
        }

        void StepPlayer(int p, float dt)
        {
            PlayerState s = pl[p];
            Ball b = s.ball;
            // shot clock runs while my ball is in my end of the court
            if (match == MatchState.InProgress && tun.useShotClock && np == 2)
            {
                bool inMyEnd = (b.state != BallState.Flying) || Dot(b.pos, Facing(p)) < -FoulLine();
                if (inMyEnd) s.shotClock = Max(0f, s.shotClock - dt);
            }
            else
            {
                s.shotClock = tun.shotClockSeconds;
            }
            for (int h = 0; h < 2; ++h)
            {
                HandState hs = s.hands[h];
                HandInput hi = s.@in.hand[h];
                RecordHand(p, h);

                bool pressed = hi.trigger > 0.55f && hs.prevTrigger <= 0.55f;
                bool released = hi.trigger < 0.35f;
                bool holding = (b.state == BallState.Held && b.heldHand == h);
                if (!s.isBot && hi.tracked)
                {
                    if (holding && released)
                    {
                        Vec3 v = HandVelocity(p, h, now);
                        float sp = Length(v);
                        // throws count in a match, and as warm-up while waiting for an opponent (scoring needs a match)
                        bool canThrow = match == MatchState.InProgress || match == MatchState.WaitingForPlayers;
                        if (sp < tun.throwMinHandSpeed || !canThrow)
                        {
                            // dropped: the spawner's damped spring pulls the ball back to its idle point
                            b.state = BallState.Idle;
                            b.heldHand = -1;
                            b.vel = v;
                            Emit(EventType.ThrowFail, p, h, b.pos, sp);
                        }
                        else
                        {
                            Launch(p, h, Normalize(v), ThrowSpeedFromHandSpeed(sp, p), PeakAngVel(p, h, now));
                        }
                    }
                    else if (Grabbable(b) &&
                             ((pressed && Length(LauncherPoint(p, h) - b.pos) < 0.25f) ||
                              (hi.trigger > 0.5f && InReach(p, h, b.pos, tun.catchHalfHeight + BallRadius(p)))))
                    {
                        // off the spawner: a trigger press at the ball, or a swing through it with the trigger already held
                        b.state = BallState.Held; b.heldHand = h; b.vel = default;
                        Emit(EventType.Grab, p, h, b.pos);
                    }
                    else if (hi.trigger > 0.5f && (b.state == BallState.Flying || b.state == BallState.Returning) &&
                             // (your own ball, whoever touched it last: native 0.3.19, the owner 2026-11-11: a player found a
                             // ball in their colour that the other player had blocked floating beside them, and it couldn't be caught)
                             (InReach(p, h, b.pos, tun.catchHalfHeight + BallRadius(p)) ||
                              SawBallNear(p, CatcherPoint(p, h), tun.catchHalfHeight + BallRadius(p)) ||
                              (tun.FistReachNow() && SawBallNear(p, FistPoint(p, h), tun.catchHalfHeight + BallRadius(p)))))
                    {
                        // catch your own ball ("You can't catch your opponent's ball!")
                        b.state = BallState.Held; b.heldHand = h; b.vel = default; b.spin = default;
                        b.hitWaitAt = -1f;   // (CATCHES ONLINE: a catch that came while the hit waited wins)
                        Emit(EventType.Caught, p, h, b.pos);
                    }
                }
                holding = (b.state == BallState.Held && b.heldHand == h);
                // "To activate your shield, you must first hold a charged ball."
                hs.shieldUp = hi.tracked && holding && ShieldCharged(b) && tun.shields;   // (shields off = HOTDOG MODE; MAIDEN: any ball)
                // in Basic the gauntlet bumpers are always active, trigger or not. (The tutorial line "Pulling the trigger will
                // deactivate the knuckle guard on that hand" belongs to the tutorial's own setup, not Basic's rules.)
                hs.knuckleUp = tun.KnuckleGuards() && hi.tracked;   // (MATCH OPTIONS can turn them on or off in either rules)
                hs.prevTrigger = hi.trigger;
            }
        }

        // round 1 of a new match: the 3-2-1
        public void StartCountdown()
        {
            if (Octagon)
            {   // FIREBALL BLITZ: the players who are here (practice: as many as asked) start, spread round the ring
                byte m = 0;
                for (int p = 0; p < np; ++p) if (pl[p].@in.headTracked && p < Max(2, Min(np, octCap))) m |= (byte)(1u << p);
                startMask = ringMask = m;
                for (int p = 0; p < np; ++p)
                {
                    pl[p].health = ((m >> p) & 1) != 0 ? MaxHealth(p) : 0;   // (3 v 1: THE ONE three times HEALTH)
                    if (((m >> p) & 1) == 0) ParkBall(p);
                }
                LayoutRing(ringMask);
                for (int p = 0; p < np; ++p) if (((m >> p) & 1) != 0) { SpawnBall(p); pl[p].ball.pos = IdlePoint(p); }
            }
            match = MatchState.Countdown;
            stateTimer = tun.countdown;
            round = 1;
            roundClock = tun.advanced ? tun.advancedRoundTime : tun.basicMatchTime;
            overtime = false;
            Emit(EventType.Countdown, -1, -1, default, tun.countdown);
        }

        void StepMatch(float dt)
        {
            switch (match)
            {
                case MatchState.WaitingForPlayers:
                {
                    bool all = true;
                    for (int p = 0; p < np; ++p) all &= pl[p].@in.headTracked;
                    bool ready = true;   // READY UP: every person in the tank said so (a bot always has)
                    if (tun.readyUp) for (int p = 0; p < np; ++p) ready &= pl[p].isBot || !pl[p].@in.headTracked || pl[p].ready;
                    if (Octagon)
                    {   // (FIREBALL BLITZ: everyone asked for, or two or more for a moment: an empty place sits out)
                        int here = 0;
                        bool asked = true;
                        for (int p = 0; p < np; ++p)
                        {
                            here += pl[p].@in.headTracked ? 1 : 0;
                            if (p < octCap) asked &= pl[p].@in.headTracked;
                        }
                        stateTimer = here >= 2 ? stateTimer + dt : 0f;
                        all = here >= 2 && (asked || stateTimer > 1.5f || tun.readyUp);
                    }
                    if (all && ready)
                    {
                        if (np == 2 && (pl[0].isBot != pl[1].isBot ? tun.botMeet : !pl[0].isBot) && tun.meetTime > 0)
                        {   // two people meet in the centre first; a person and the bot too when asked (practice); two bots never;
                            // four players never (the centre is far from every corner)
                            match = MatchState.Meet;
                            stateTimer = tun.meetTime;
                        }
                        else
                        {
                            StartCountdown();
                        }
                    }
                    break;
                }
                case MatchState.Meet:
                {
                    stateTimer -= dt;
                    bool bump = false;
                    Vec3 at = default;
                    float best = 9f;
                    bool Closed(HandInput x) => x.trigger > 0.5f || x.squeeze > 0.5f;   // a fist
                    bool[] offered = new bool[2];
                    for (int p = 0; p < 2; ++p)
                    {   // a fist lit in its ring: offered once it has been there meetOfferTime
                        bool lit = MeetLit(p, 0) || MeetLit(p, 1);
                        if (!lit) meetLitT[p] = -1;
                        else if (meetLitT[p] < 0) meetLitT[p] = now;
                        offered[p] = meetLitT[p] >= 0 && now - meetLitT[p] >= tun.meetOfferTime;
                    }
                    for (int h0 = 0; h0 < 2; ++h0)
                        for (int h1 = 0; h1 < 2; ++h1)
                        {
                            HandInput a = pl[0].@in.hand[h0];
                            HandInput b = pl[1].@in.hand[h1];
                            if (!a.tracked || !b.tracked) continue;
                            float gap = FistGap(0, h0, 1, h1);
                            best = Min(best, gap);
                            // either fist closed is enough: in the headset the two triggers were rarely held at the same moment
                            // (log 2026-10-17: fists 1-3 cm apart with triggers 1/0, 0/1)
                            // only at the meeting point, and one of them offered first (nobody can force the start)
                            if ((Closed(a) || Closed(b)) && gap < tun.fistBumpGap && InMeetRing(0, h0) && InMeetRing(1, h1) &&
                                (offered[0] || offered[1]))
                            {
                                bump = true;
                                at = (a.grip.Transform(tun.fistFront) + b.grip.Transform(tun.fistFront)) * 0.5f;
                            }
                        }
                    if (now - lastFistLog > 0.5f && best < 0.6f)
                    {   // diagnostics while they reach for each other
                        lastFistLog = now;
                        float t0 = Max(pl[0].@in.hand[0].trigger, pl[0].@in.hand[1].trigger);
                        float t1 = Max(pl[1].@in.hand[0].trigger, pl[1].@in.hand[1].trigger);
                        meetLog = true; meetGap = best; meetTrig[0] = t0; meetTrig[1] = t1;
                    }
                    if (bump && !meetBumped)
                    {   // confirm it (buzz, sound, FIST BUMP!) for a moment, then both go to their ends
                        meetBumped = true;
                        Emit(EventType.FistBump, -1, -1, at, 0);
                        stateTimer = Min(stateTimer, tun.bumpPause);
                    }
                    if (stateTimer <= 0) { meetBumped = false; StartCountdown(); }
                    break;
                }
                case MatchState.Countdown:
                {
                    float before = stateTimer;
                    stateTimer -= dt;
                    if (MathF.Floor(before) != MathF.Floor(stateTimer) && stateTimer > 0) Emit(EventType.Countdown, -1, -1, default, MathF.Ceiling(stateTimer));
                    if (stateTimer <= 0)
                    {
                        match = MatchState.InProgress;
                        Emit(EventType.RoundStart, -1, -1, default, (float)round);
                    }
                    break;
                }
                case MatchState.InProgress:
                    if (Octagon) { StepBlitzMatch(dt); break; }
                    // the clock: Basic's match time (none in FREE PLAY), Advanced's round time when the host set one
                    if (tun.Timed() && !overtime)
                    {
                        roundClock -= dt;
                        if (roundClock <= 0)
                        {
                            roundClock = 0;
                            int w = Leader();   // the team ahead (a tie: sudden death)
                            if (w >= 0)
                            {
                                if (tun.advanced) EndRound(w);   // the round goes to the one ahead
                                else
                                {
                                    matchWinner = w;
                                    match = MatchState.MatchSummary;
                                    Emit(EventType.MatchEnd, matchWinner, -1, default, 0);
                                }
                            }
                            else
                            {
                                overtime = true;   // "Sudden death overtime on a tie" (and a timed round's tie: the next point)
                                Emit(EventType.Overtime, -1, -1, default, 0);
                            }
                        }
                    }
                    break;
                case MatchState.RoundSummary:
                    stateTimer -= dt;
                    if (stateTimer <= 0)
                    {
                        int w = pl[0].roundWins >= tun.RoundsToWinNow() ? 0 : pl[1].roundWins >= tun.RoundsToWinNow() ? 1 : -1;   // (ROUNDS: BEST OF n)
                        for (int p = 0; p < np; ++p) { pl[p].strikesAgainst = 0; pl[p].strikesFor = 0; SpawnBall(p); }
                        if (w >= 0)
                        {
                            matchWinner = w;
                            match = MatchState.MatchSummary;
                            Emit(EventType.MatchEnd, w, -1, default, 0);
                        }
                        else
                        {
                            round++;
                            pl[0].score = pl[1].score = 0;
                            overtime = false;
                            roundClock = tun.advancedRoundTime;   // (a timed round starts its clock again)
                            match = MatchState.Countdown;
                            stateTimer = tun.countdown;
                            Emit(EventType.Countdown, -1, -1, default, tun.countdown);
                        }
                    }
                    break;
                case MatchState.MatchSummary:
                    if (pl[0].@in.buttonA)
                    {
                        uint bots = 0;
                        for (int p = 0; p < np; ++p) bots |= pl[p].isBot ? 1u << p : 0u;
                        ResetWithBots(bots);
                    }
                    break;
            }
        }

        // HOST HANDOFF (native resumeAfterHandoff, 0.3.13): a match taken over from the old host's snapshot goes on from
        // here. Snapshots don't carry a ball's spin or bounces, so balls in the air go back to their throwers, and a match
        // being played counts down again before it picks up; scores, the clock, the round, health and the ring stay as
        // they were (fireballs fizzle)
        public void ResumeAfterHandoff()
        {
            for (int p = 0; p < np; ++p)
            {
                for (int k = 0; k < KMaxFire; ++k) Fizzle(p, k, false);
                if (pl[p].ball.state == BallState.Held) continue;
                if (Octagon && !Alive(p)) { ParkBall(p); continue; }
                SpawnBall(p);
                if (Octagon) pl[p].ball.pos = IdlePoint(p);
            }
            if (match == MatchState.InProgress)
            {
                match = MatchState.Countdown;
                stateTimer = tun.countdown;
            }
        }

        // Advance the simulation. `in` is overwritten for bot players (and gets every player's input back, as natively).
        // `in`: np entries (four players: four)
        public void Step(float dt, PlayerInput[] @in)
        {
            events.Clear();
            events.AddRange(served);   // (native: events.swap(served_))
            served.Clear();
            if (dt <= 0) return;
            dt = Min(dt, 0.05f);
            now += dt;
            lastDt = dt;
            for (int p = 0; p < np; ++p)
            {
                if (@in[p] == null) @in[p] = new PlayerInput();   // (native: a default PlayerInput{})
                if (pl[p].isBot) StepBot(p, dt);
                else pl[p].@in.CopyFrom(@in[p]);
                @in[p].CopyFrom(pl[p].@in);
                if (match == MatchState.WaitingForPlayers && (pl[p].isBot || (tun.readyUp && pl[p].@in.buttonA && pl[p].@in.headTracked)))
                    pl[p].ready = true;
            }
            StepMatch(dt);
            for (int p = 0; p < np; ++p) StepPlayer(p, dt);
            for (int p = 0; p < np; ++p) StepBall(p, dt);
            for (int p = 0; p < np; ++p)
            {   // each flying ball's recent path (CATCHES ONLINE)
                Ball b = pl[p].ball;
                BallTrack k = track[p];
                if (b.state != BallState.Flying && b.state != BallState.Returning) { k.n = 0; continue; }
                k.p[k.head] = b.pos; k.t[k.head] = now;
                k.head = (k.head + 1) % BallTrack.N; k.n = Min(BallTrack.N, k.n + 1);
            }
            if (Octagon)
            {   // FIREBALL BLITZ: the fireballs; a tee waiting for room refills (RAPID FIRE: at once)
                for (int p = 0; p < np; ++p)
                    for (int k = 0; k < KMaxFire; ++k) StepFire(p, k, dt);
                for (int p = 0; p < np; ++p)
                {
                    Ball b = pl[p].ball;
                    if (!Alive(p) || b.state != BallState.Dead || b.timer < 1e8f || FireCount(p) >= FireLimit()) continue;
                    SpawnBall(p);
                    b.timer = tun.fireBalls >= 4 ? 0.2f : 0.7f;   // (FIREBALL BLITZ: back on the tee quickly)
                }
            }
        }
    }
}
