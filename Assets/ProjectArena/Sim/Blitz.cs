// FIREBALL BLITZ (native 0.3.13; the owner's design, 2026-10-30 to 11-02), ported 1:1 from app/src/game.cpp
// (task A5): every throw a fireball hopping along the floor of a regular octagon; a hit takes one of your health,
// hits count from the hips up; at 0 you are out (to the dugout) and, after a warning and a ceasefire, the room closes in
// on those left; the last one standing wins (at time: the most health, then sudden death at one each). Its bots aim
// each fireball by trying it out (straight, curved, or off a wall aimed at the target's mirror image) and step out of
// the way of the ones coming at them.
using System;
using System.Collections.Generic;
using static ProjectArena.Sim.M;

namespace ProjectArena.Sim
{
    public partial class Game
    {
        // each player's fireballs in the room (BallState.Dead: none), who started the match (bit p), who stands in the ring
        // now (the room's layout: players knocked out keep their place, outside it), when it closes in next (-1: not), and
        // its walls (their distance from the centre)
        public const int KMaxFire = 3;
        public Ball[][] fire = NewFire();
        public byte startMask, ringMask;
        public float ringCloseAt = -1, ringApothem = 7.5f;
        public int lastKnockBy = -1;
        public int octCap = 4;   // how many places play (the app: practice's PLAYERS; 4 in a room, whatever practice said)

        static Ball[][] NewFire()
        {
            var f = new Ball[Teams.MaxPlayers][];
            for (int p = 0; p < Teams.MaxPlayers; ++p)
            {
                f[p] = new Ball[KMaxFire];
                for (int k = 0; k < KMaxFire; ++k) { f[p][k] = new Ball(); f[p][k].state = BallState.Dead; }
            }
            return f;
        }
        Ball[][] CloneFire()   // (a copy of the game: the bot's tryouts)
        {
            var f = new Ball[Teams.MaxPlayers][];
            for (int p = 0; p < Teams.MaxPlayers; ++p)
            {
                f[p] = new Ball[KMaxFire];
                for (int k = 0; k < KMaxFire; ++k) f[p][k] = fire[p][k].Clone();
            }
            return f;
        }

        public bool Octagon => arenaKind == Arena.Octagon;
        public bool Alive(int p) => !Octagon || (((startMask >> p) & 1) != 0 && pl[p].health > 0);
        public int FireLimit() => tun.fireBalls >= 4 ? tun.rapidLimit : Max(1, Min(KMaxFire, tun.fireBalls));
        public int FireCount(int p)
        {
            int n = 0;
            foreach (Ball f in fire[p]) n += f.state == BallState.Flying ? 1 : 0;
            return n;
        }
        public int AliveCount()
        {
            int n = 0;
            for (int p = 0; p < np; ++p) n += Alive(p) ? 1 : 0;
            return n;
        }

        // out of play, hidden under its spawner, until served or respawned
        public void ParkBall(int p)
        {
            Ball b = pl[p].ball;
            b.state = BallState.Dead;
            b.timer = 1e9f;
            b.vel = b.spin = default;
            b.heldHand = -1;
            b.pos = tun.spawner[p] + new Vec3(0, -2f * tun.spawnDepth, 0);
        }

        // TRAINING: p's ball back on its spawner (native respawnBall)
        public void RespawnBall(int p) => SpawnBall(p);

        // where p's ball goes if served so now, nobody in its way (every body and hand out of it), for up to `horizon`
        // seconds in steps of dt; ends where it stops flying (a returning ball: its first point). TRAINING's planned shots
        public void TrialFlight(int p, Vec3 from, Vec3 dir, float speed, Vec3 spin, bool assist, float horizon, float dt, List<BallPathPt> @out)
        {
            @out.Clear();
            Game g = Clone();
            for (int q = 0; q < np; ++q)
            {   // (everyone out of the way)
                g.pl[q].@in.head.p = new Vec3(0, -50, 0);
                g.pl[q].@in.headTracked = false;
                foreach (HandState hs in g.pl[q].hands) hs.shieldUp = hs.knuckleUp = false;
            }
            g.Serve(p, from, dir, speed, spin, assist);
            Ball b = g.pl[p].ball;
            @out.Add(new BallPathPt(g.now, b.pos, b.vel, false, b.Clone()));
            for (float t = 0; t < horizon; t += dt)
            {
                g.now += dt;
                g.events.Clear();
                g.StepBall(p, dt);
                bool strike = false;
                foreach (GameEvent e in g.events) strike |= e.type == EventType.Strike;
                @out.Add(new BallPathPt(g.now, b.pos, b.vel, strike, b.Clone()));
                if (b.state != BallState.Flying) break;
            }
        }

        static readonly int[] kRingOrder = { 0, 2, 1, 3 };
        // the players in `mask` evenly round the ring for that many (their starts, tees)
        public void LayoutRing(byte mask)
        {
            // round the ring in the order the four always stand (0 at -X, then 2, 1, 3), spread evenly for how many there
            // are; four are exactly FOUR CORNERS' places
            int n = 0;
            foreach (int p in kRingOrder) n += p < np && ((mask >> p) & 1) != 0 ? 1 : 0;
            float R = ExpArena.OctRing(n);
            ringApothem = R + ExpArena.kOctMargin;
            int j = 0;
            foreach (int p in kRingOrder)
            {
                if (p >= np || ((mask >> p) & 1) == 0) continue;
                float a = kPi + 2f * kPi * (float)j++ / (float)Max(1, n);
                tun.start[p] = new Vec3(R * MathF.Cos(a), 0, R * MathF.Sin(a));
                tun.spawner[p] = tun.start[p] + Facing(p) * 0.52f;
            }
        }

        // p's thrown ball becomes a fireball; its tee refills (or waits)
        void ToFire(int p)
        {
            Ball b = pl[p].ball;
            int k = 0;
            for (int i = 0; i < KMaxFire; ++i) if (fire[p][i].state != BallState.Flying) { k = i; break; }
            Ball f = fire[p][k];
            f.CopyFrom(b);
            f.state = BallState.Flying;
            f.speedLimit = Max(tun.fireMinSpeed, Length(new Vec3(b.vel.x, 0, b.vel.z)));   // (its speed along the floor)
            f.bounces = 0;
            f.timer = 0;
            f.instigator = p;
            f.heldHand = -1;
            if (FireCount(p) < FireLimit() && Alive(p))
            {   // another on the tee (RAPID FIRE: at once)
                SpawnBall(p);
                b.timer = tun.fireBalls >= 4 ? 0.2f : 0.7f;   // (FIREBALL BLITZ: back on the tee quickly)
            }
            else
            {
                ParkBall(p);   // (the tee waits for one of them to fizzle)
            }
        }

        void Fizzle(int p, int k, bool show = true)
        {
            Ball f = fire[p][k];
            if (f.state != BallState.Flying) return;
            if (show) Emit(EventType.Fizzle, p, -1, f.pos, 0);
            f.state = BallState.Dead;
            f.vel = f.spin = default;
        }

        // one step of a fireball's flight (the room's walls); the bounces in it
        int FireSub(Ball b, float h, out Vec3 at)
        {
            at = default;
            float r = tun.projectileRadius * b.scale;
            int hits = 0;
            if (Length2(b.spin) > 1e-4f)
            {   // a curve: along the floor only (the hop stays the hop)
                Vec3 a = Cross(b.spin, b.vel) * tun.magnusFactor;
                a.y = 0;
                b.vel += a * h;
                b.spin *= MathF.Exp(-tun.spinDecayPerSecond * h);
            }
            Vec3 flat = new Vec3(b.vel.x, 0, b.vel.z);
            float fl = Length(flat);
            if (fl > 1e-4f) { flat *= b.speedLimit / fl; b.vel.x = flat.x; b.vel.z = flat.z; }
            b.vel.y -= tun.fireGravity * h;
            Vec3 to = b.pos + b.vel * h;
            if (to.y < r && b.vel.y < 0)
            {   // the floor: back up to its hop's height
                to.y = r + (r - to.y);
                b.vel.y = MathF.Sqrt(2f * tun.fireGravity * Max(0.05f, tun.fireHop - r));
                at = new Vec3(to.x, 0, to.z);
                ++hits;
            }
            if (to.y > ExpArena.kOctH - r && b.vel.y > 0)
            {   // the ceiling
                to.y = 2f * (ExpArena.kOctH - r) - to.y;
                b.vel.y = -b.vel.y;
                at = to;
                ++hits;
            }
            for (int i = 0; i < 8; ++i)
            {   // the walls (one behind each of the four places, and four between)
                float a = (float)i * 0.25f * kPi;
                Vec3 n = new Vec3(MathF.Cos(a), 0, MathF.Sin(a));
                float d = Dot(to, n) - (ringApothem - r);
                if (d > 0 && Dot(b.vel, n) > 0)
                {
                    b.vel -= n * (2f * Dot(b.vel, n));
                    to -= n * (2f * d);
                    at = to;
                    ++hits;
                }
            }
            b.pos = to;
            return hits;
        }

        void StepFire(int p, int k, float dt)
        {
            Ball b = fire[p][k];
            if (b.state != BallState.Flying) return;
            // (the room about to close in: a ceasefire, no cheap shots while everyone moves; owner, 2026-11-02)
            if (ringCloseAt >= 0) { Fizzle(p, k); return; }
            b.timer += dt;
            if (b.timer > tun.fireLife) { Fizzle(p, k); return; }
            float r = tun.projectileRadius * b.scale;
            int sub = Max(1, (int)MathF.Ceiling(Length(b.vel) * dt / 0.02f));
            float h = dt / sub;
            for (int i = 0; i < sub; ++i)
            {
                Vec3 from = b.pos;
                int bounced = FireSub(b, h, out Vec3 at);
                Vec3 to = b.pos;
                // shields, then knuckle guards, of everyone but whoever last touched it
                bool handled = false;
                for (int pass = 0; pass < 2 && !handled; ++pass)
                    for (int q = 0; q < np && !handled; ++q)
                    {
                        if (q == b.instigator || !Alive(q) || Friendly(q, b.instigator)) continue;   // (teams: a teammate's passes)
                        for (int hh = 0; hh < 2 && !handled; ++hh)
                        {
                            Vec3 n;
                            float rg = r + tun.fireGuardPad;
                            if (pass == 0 && HitsShield(q, hh, from, to, rg, out n))
                            {
                                DeflectMoving(b, n, RecentHandVel(q, hh, ShieldLocal(hh).p, now), q, tun.shieldAimAssistBasicDeg, from);
                                pl[q].ball.charged = false;
                                pl[q].hands[hh].shieldUp = false;
                                Emit(EventType.ShieldBlock, q, hh, from, 0, p);
                                Emit(EventType.ShieldBreak, q, hh, from);
                                handled = true;
                            }
                            else if (pass == 1 && pl[q].hands[hh].knuckleUp && HitsBox(KnucklePose(q, hh), tun.knuckleHalf, from, to, rg, out n))
                            {
                                Vec3 lo = tun.KnuckleOffsetNow();
                                if (hh == 0) lo.x = -lo.x;
                                Vec3 face = KnucklePose(q, hh).q.Rotate(new Vec3(0, 0, -1));
                                Vec3 vs = RecentHandVel(q, hh, lo, now);
                                if (Dot(face, b.vel - vs) > 0) face = -face;
                                DeflectMoving(b, Normalize(face * 0.75f + n * 0.25f), vs, q, tun.knuckleAimAssistDeg, from, true);
                                Emit(EventType.KnuckleBlock, q, hh, from, Length(b.vel), p);
                                handled = true;
                            }
                        }
                    }
                if (handled)
                {   // (sent on as a fireball: its new speed along the floor)
                    b.speedLimit = Max(tun.fireMinSpeed, Length(new Vec3(b.vel.x, 0, b.vel.z)));
                    b.pos = to = from + b.vel * h;
                    b.state = BallState.Flying;
                }
                if (match == MatchState.InProgress)
                    for (int q = 0; q < np; ++q)
                    {   // a hit: anyone in the room but whoever last touched it (and their team)
                        if (q == b.instigator || !Alive(q) || Friendly(q, b.instigator) || !HitsBody(q, to, r)) continue;
                        int by = b.instigator;
                        pl[q].health = Max(0, pl[q].health - 1);
                        if (by >= 0 && by < np) pl[by].score++;
                        Emit(EventType.HitPlayer, q, -1, to, (float)by, p);
                        b.state = BallState.Dead;
                        b.vel = b.spin = default;
                        if (pl[q].health <= 0) KnockOut(q, by, to);
                        return;
                    }
                if (bounced != 0)
                {
                    b.bounces += bounced;
                    if (b.bounces >= tun.fireBounces) { b.pos = at; Fizzle(p, k); return; }
                    Emit(EventType.Bounce, p, -1, at, b.speedLimit);
                }
            }
        }

        void KnockOut(int q, int by, Vec3 at)
        {
            bool wasIn = ((startMask >> q) & 1) != 0;
            pl[q].health = 0;
            if (!wasIn) return;
            for (int k = 0; k < KMaxFire; ++k) Fizzle(q, k);
            ParkBall(q);
            lastKnockBy = by;
            int left = AliveCount();
            Emit(EventType.Knockout, q, by, at, (float)left);
            if (TeamsIn() >= 2)
            {   // the room closes in on those left, after a warning (a ceasefire till then: every fireball out)
                ringCloseAt = now + tun.ringWarn;
                for (int o = 0; o < np; ++o) for (int k = 0; k < KMaxFire; ++k) Fizzle(o, k);
                Emit(EventType.RingClose, -1, -1, default, tun.ringWarn);
            }
        }

        // a match being played: the ring closes in; the last one standing wins; at time, the most health
        void StepBlitzMatch(float dt)
        {
            if (ringCloseAt >= 0 && now >= ringCloseAt)
            {
                ringCloseAt = -1;
                byte m = 0;
                for (int p = 0; p < np; ++p) if (Alive(p)) m |= (byte)(1u << p);
                for (int p = 0; p < np; ++p) for (int k = 0; k < KMaxFire; ++k) Fizzle(p, k, false);
                ringMask = m;
                LayoutRing(m);
                for (int p = 0; p < np; ++p) if (Alive(p) && pl[p].ball.state != BallState.Held) { SpawnBall(p); pl[p].ball.pos = IdlePoint(p); }
            }
            bool teams = TeamsNow() != Teams.FFA;   // (0.3.24, TASKS.md E51: a team wins when the other team is all out)
            if (teams ? TeamsIn() <= 1 : AliveCount() <= 1)
            {
                int w = lastKnockBy;
                for (int p = 0; p < np; ++p) if (Alive(p)) w = p;
                if (w < 0)   // (nobody left, nobody to credit: whoever landed the most hits; review 2026-11-02)
                    for (int p = 0; p < np; ++p)
                        if (((startMask >> p) & 1) != 0 && (w < 0 || pl[p].score > pl[w].score)) w = p;
                matchWinner = teams && w >= 0 ? Team(w) : w;   // (teams: the team, as FOUR CORNERS' MatchEnd names it)
                match = MatchState.MatchSummary;
                Emit(EventType.MatchEnd, matchWinner, -1, default, 0);
                return;
            }
            if (tun.Timed() && !overtime)
            {
                roundClock -= dt;
                if (roundClock <= 0)
                {
                    roundClock = 0;
                    // the most health left (teams: between the team's players still in); those below it are out
                    var left = new int[4];
                    for (int p = 0; p < np; ++p) if (Alive(p)) left[teams ? Team(p) & 3 : p & 3] += pl[p].health;
                    int top = 0;
                    for (int t = 0; t < 4; ++t) top = Max(top, left[t]);
                    for (int p = 0; p < np; ++p) if (Alive(p) && left[teams ? Team(p) & 3 : p & 3] < top) KnockOut(p, -1, pl[p].@in.head.p);
                    if (teams ? TeamsIn() > 1 : AliveCount() > 1)
                    {   // a tie at the top: sudden death, one hit each
                        overtime = true;
                        for (int p = 0; p < np; ++p) if (Alive(p)) pl[p].health = 1;
                        Emit(EventType.Overtime, -1, -1, default, 0);
                    }
                }
            }
        }

        // a fireball's flight to a body whose head is at `head`, nothing in its way: when it touches it (-1: it doesn't
        // within `horizon` s, or fizzles first)
        public float FireHitTime(Ball b0, Vec3 head, float horizon)
        {
            Ball b = b0.Clone();
            float r = tun.projectileRadius * b.scale, h = 1f / 120;
            int bounces = b.bounces;
            for (float t = 0; t < horizon; t += h)
            {
                bounces += FireSub(b, h, out _);
                if (bounces >= tun.fireBounces) return -1f;
                if (BodyTouches(head, b.pos, r)) return t;
            }
            return -1f;
        }

        struct FireWay { public Vec3 dir; public float spin; public int shape; public FireWay(Vec3 d, float s, int sh) { dir = d; spin = s; shape = sh; } }
        struct FireTry { public float t, pitch; public Vec3 dir; public float spin; public FireTry(float t, float pitch, Vec3 dir, float spin) { this.t = t; this.pitch = pitch; this.dir = dir; this.spin = spin; } }
        static readonly float[] kFirePitches = { -30f, -22f, -15f, -9f, -4f, 0f, 5f, 10f, 15f };

        Vec3 BotFireAim(int p, int h, BotSkill sk, out Vec3 spinOut)
        {
            // at its target's body as the fireball would hop: tried out, straight, curved either way, and off the walls
            // (owner, 2026-11-02: no wall bounces). The best levels (BotSkill.hunts) pick a shape (half the time straight, a
            // quarter curved, a quarter off a wall); the others the one that gets there soonest; then its aim's error
            spinOut = default;
            int q = BotAim(p);
            if (!Rivals(p, q)) q = AimTarget(p, pl[p].@in.head.p, Facing(p));
            Vec3 from = LauncherPoint(p, h);
            Vec3 tgt = pl[q].@in.head.p;
            Vec3 flat = new Vec3(tgt.x - from.x, 0, tgt.z - from.z);
            flat = Length(flat) > 1e-3f ? Normalize(flat) : Facing(p);
            float speed = ThrowSpeedFromHandSpeed(sk.throwHand, p);
            Vec3 side = Normalize(Cross(flat, kUp));
            // the ways along the floor to try: straight at it; curved (aimed out to one side, spun to bend back in); and off
            // each of the eight walls, aimed at the target's mirror image across it (a wall turns the ball along the floor
            // exactly as a mirror would; the hop goes on as it was: owner, 2026-11-02, banks were rare when aimed by guesswork)
            var ways = new List<FireWay> { new FireWay(flat, 0f, 0) };
            if (sk.hunts)
            {
                // (curved: aimed out 9, 14 or 20 degrees, spun to bend back in about as much; one angle found too few at the
                // distances a match brings, native 6def918)
                foreach (float yawOff in new[] { -0.35f, -0.25f, -0.15f, 0.15f, 0.25f, 0.35f })
                {
                    float spin = (yawOff > 0 ? -1f : 1f) * 64f * MathF.Abs(yawOff) * (Dot(Cross(kUp, flat), side) >= 0 ? 1f : -1f);
                    ways.Add(new FireWay(Normalize(flat + side * MathF.Tan(yawOff)), spin, 1));
                }
                Vec3 T = new Vec3(tgt.x, 0, tgt.z), F = new Vec3(from.x, 0, from.z);
                float wall = ringApothem - tun.projectileRadius;
                for (int i = 0; i < 8; ++i)
                {
                    float a = (float)i * 0.25f * kPi;
                    Vec3 n = new Vec3(MathF.Cos(a), 0, MathF.Sin(a));
                    Vec3 mirror = T - n * (2f * (Dot(T, n) - wall));
                    Vec3 d = mirror - F;
                    // (toward that wall, and well off the straight line: the wall behind the target is only a straight throw)
                    if (Length(d) > 1e-3f && Dot(Normalize(d), n) > 0.15f && Dot(Normalize(d), flat) < 0.9f) ways.Add(new FireWay(Normalize(d), 0f, 2));
                }
            }
            // straight, curved, off a wall: the soonest of each
            FireTry[] best = { new FireTry(1e9f, -0.15f, flat, 0), new FireTry(1e9f, 0, flat, 0), new FireTry(1e9f, 0, flat, 0) };
            foreach (FireWay w in ways)
                foreach (float deg in kFirePitches)
                {
                    float a = Deg2Rad(deg);
                    var b = new Ball();
                    b.pos = from;
                    b.vel = (w.dir * MathF.Cos(a) + kUp * MathF.Sin(a)) * speed;
                    b.speedLimit = Max(tun.fireMinSpeed, speed * MathF.Cos(a));
                    b.spin = kUp * w.spin;
                    b.scale = 1f;
                    float t = FireHitTime(b, tgt, 3f);
                    if (t >= 0 && t < best[w.shape].t) best[w.shape] = new FireTry(t, a, w.dir, w.spin);
                }
            int pick = 0;
            if (sk.hunts)
            {   // (a shape: half straight, a quarter curved, a quarter off a wall; one that found nothing: straight)
                float r = Rnd();
                pick = r < 0.5f ? 0 : r < 0.75f ? 1 : 2;
                if (best[pick].t > 1e8f) pick = 0;
            }
            FireTry c = best[pick];
            spinOut = kUp * c.spin;
            float yaw = (Rnd() - 0.5f) * sk.aimError * 0.25f, pitch = c.pitch + (Rnd() - 0.5f) * sk.aimError * 0.15f;
            Vec3 f2 = Quat.AxisAngle(kUp, yaw).Rotate(c.dir);
            return Normalize(f2 * MathF.Cos(pitch) + kUp * MathF.Sin(pitch));
        }

        static readonly float[] kFireDodges = { 0.35f, -0.35f, 0.6f, -0.6f, 0.9f, -0.9f, 0f };
        bool BotFireDodge(int p, BotSkill sk, Vec3 right, ref Vec3 bodyWant)
        {
            // the fireballs coming at it: once it has seen one (its reaction time), it does something about it (its level's
            // chance): a step to the side that clears every one it acts on, in time
            PlayerState s = pl[p];
            Vec3 stand = tun.start[p] + new Vec3(0, 1.62f, 0);
            float soonest = 1e9f;
            var acting = new List<Ball>();
            for (int o = 0; o < np; ++o)
                for (int k = 0; k < KMaxFire; ++k)
                {
                    ushort bit = (ushort)(1u << (o * KMaxFire + k));
                    Ball b = fire[o][k];
                    if (b.state != BallState.Flying || b.instigator == p || Friendly(p, b.instigator) || !Alive(p))
                    {   // (a teammate's passes)
                        s.fireSeen &= (ushort)~bit; s.fireAct &= (ushort)~bit; continue;
                    }
                    if ((s.fireSeen & bit) == 0 && b.timer >= sk.reaction)
                    {
                        s.fireSeen |= bit;
                        if (Rnd() < sk.defend) s.fireAct |= bit;
                    }
                    if ((s.fireAct & bit) == 0) continue;
                    float t = FireHitTime(b, stand + s.botBody, 1.5f);
                    if (t < 0) continue;
                    acting.Add(b);
                    soonest = Min(soonest, t);
                }
            if (acting.Count != 0)
            {
                float bestMove = 1e9f;
                foreach (float x in kFireDodges)
                {
                    Vec3 o = right * x;
                    float move = Length(o - s.botBody);
                    if (Bot.MoveTime(move, sk.bodySpeed, sk.bodyAccel) > soonest) continue;
                    bool clear = true;
                    foreach (Ball b in acting) clear &= FireHitTime(b, stand + o, 1.5f) < 0;
                    if (clear && move < bestMove) { bestMove = move; s.botDodge = o; }
                }
                if (bestMove < 1e8f) s.botDodgeUntil = now + 0.6f;
            }
            if (now < s.botDodgeUntil) { bodyWant = s.botDodge; return true; }
            return false;
        }
    }
}
