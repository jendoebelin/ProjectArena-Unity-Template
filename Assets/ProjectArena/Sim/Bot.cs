// The practice bot, ported from the native app/src/game.cpp (Game::stepBot and its helpers, native e5cc0ae,
// 0.3.15): a person's body and limits (reaction time, hand and body speed, arm's reach), its skill set by its level.
// The better levels read each ball's real flight on a copy of the game (PredictBall) and try their answers out on more
// copies (BotTry) before they move; CHAMPION and UNBEATABLE have an attack plan (strikes side to side, at the player at
// 3 strikes, every shot shape) and slap shots; four players' targets; FIREBALL BLITZ's aim and dodges (Sim/Blitz.cs).
// TRAINING's coach comes with task A6.
using System;
using System.Collections.Generic;
using static ProjectArena.Sim.M;

namespace ProjectArena.Sim
{
    // the practice bot's levels (owner, 2026-10-26): it always moves like a person, and the levels differ in skill
    // within those limits. UNBEATABLE reads every ball's real flight (walls, spin) and answers it with what it can reach
    // in time: shield, knuckle guard or dodge; its throws are tried out first.
    public struct BotSkill
    {
        public string name;
        public float reaction;             // seconds from seeing a ball to the first move
        public float defend;               // chance it does something about a ball coming at it
        public bool readsFlight;           // reads curves and bank shots (else it expects a straight line, re-read as it comes)
        public float readError;            // metres its read of the ball's path is off
        public float handSpeed;            // m/s: its fastest hand move (blocking, reaching, catching)
        public float bodySpeed, bodyAccel; // m/s, m/s^2: stepping, leaning and ducking
        public float throwHand;            // m/s: the hand speed of its throws (through the real launch-speed mapping)
        public float aimError;             // metres
        public float holdMin, holdMax;     // seconds it holds the ball before a throw
        public float catchChance;          // its own ball coming back
        public bool planShots;             // tries out throws (direct, curved, banked) on a copy of the game and picks the best
        public bool guardsStrikes;         // also blocks balls heading for its strike zone
        public float slap;                 // chance it sends its returning ball straight back as it catches it (the slap shot)
        public bool hunts;                 // its attack plan (testers 2026-10-30): strikes first, through the ring beside you,
                                           // left and right in turn; one throw in seven at where you usually stand; with 3
                                           // strikes or more, nearly every throw at you. Its throws go exactly where it aims

        public BotSkill(string name, float reaction, float defend, bool readsFlight, float readError, float handSpeed, float bodySpeed,
                        float bodyAccel, float throwHand, float aimError, float holdMin, float holdMax, float catchChance, bool planShots,
                        bool guardsStrikes, float slap, bool hunts)
        {
            this.name = name; this.reaction = reaction; this.defend = defend; this.readsFlight = readsFlight; this.readError = readError;
            this.handSpeed = handSpeed; this.bodySpeed = bodySpeed; this.bodyAccel = bodyAccel; this.throwHand = throwHand;
            this.aimError = aimError; this.holdMin = holdMin; this.holdMax = holdMax; this.catchChance = catchChance;
            this.planShots = planShots; this.guardsStrikes = guardsStrikes; this.slap = slap; this.hunts = hunts;
        }
    }

    public static class Bot
    {
        public const int Levels = 6;
        // its answers to a ball (BotRead.plan)
        public const int NONE = 0, SHIELD = 1, KNUCKLE = 2, DODGE = 3, CATCH = 4;
        // how it holds its hands when it has nothing to do: one picked at random each match (ROOKIE and AMATEUR: only the
        // open ones, KITTEN, RELAXED, MONKEY: a guard blocks by itself). KARATE bows at the fist bump.
        public const int STANCE_KITTEN = 0, STANCE_RELAXED = 1, STANCE_BOXER = 2, STANCE_KARATE = 3, STANCE_MANTIS = 4, STANCE_MONKEY = 5, Stances = 6;
        // the shapes of its throws (testers 2026-10-30: accurate curves, off the walls, every kind of geometry)
        public const int HUNT_STRAIGHT = 0, HUNT_CURVE = 1, HUNT_BANK = 2, HUNT_CURVED_BANK = 3, HUNT_FLOOR = 4, HUNT_SHAPES = 5;
        public const float SideWall = 2.5f;   // the classic arena's side walls (training.h's kSideZ)
        public const int TriesPerStep = 6;    // its tryouts each step (the rest of a plan waits for the next step)

        // throws raised 2026-10-27 from the owner's recorded matches: ROOKIE about 9 m/s, PRO 12, UNBEATABLE 14
        static readonly BotSkill[] kSkills = {
            //            name          react  defend reads  misread hand  body  accel throw aim    hold          catch plan   strikes slap  hunts
            new BotSkill("ROOKIE",     0.45f, 0.30f, false, 0.30f, 2.5f, 1.0f, 4f,  4.2f, 0.60f, 2.0f, 4.0f,  0.2f, false, false, 0.00f, false),
            new BotSkill("AMATEUR",    0.35f, 0.50f, false, 0.18f, 3.0f, 1.4f, 6f,  4.8f, 0.45f, 1.6f, 3.4f,  0.5f, false, false, 0.00f, false),
            new BotSkill("PRO",        0.25f, 0.65f, true,  0.10f, 3.8f, 1.8f, 7f,  5.6f, 0.35f, 1.2f, 3.0f,  0.7f, false, false, 0.30f, false),
            new BotSkill("ALL-STAR",   0.20f, 0.80f, true,  0.05f, 4.5f, 2.2f, 9f,  6.0f, 0.20f, 0.9f, 2.2f,  0.9f, true,  false, 0.50f, false),
            new BotSkill("CHAMPION",   0.17f, 0.92f, true,  0.02f, 5.2f, 2.6f, 11f, 6.3f, 0.10f, 0.45f, 1.0f, 1.0f, true,  true,  0.80f, true),
            new BotSkill("UNBEATABLE", 0.15f, 1.00f, true,  0.00f, 6.0f, 3.0f, 12f, 6.5f, 0.00f, 0.30f, 0.70f, 1.0f, true,  true,  0.90f, true),
        };
        public static BotSkill Skill(int level) => kSkills[level < 0 ? 0 : level >= Levels ? Levels - 1 : level];
        public static BotSkill SkillAt(float level)   // between two levels: their numbers blended (the name and yes-or-no skills: the nearer)
        {
            level = Clampf(level, 0f, Levels - 1f);
            int i = Min((int)level, Levels - 2);
            float f = level - i;
            BotSkill a = kSkills[i], b = kSkills[i + 1];
            BotSkill s = f < 0.5f ? a : b;
            float Mix(float x, float y) => x + (y - x) * f;
            s.reaction = Mix(a.reaction, b.reaction); s.defend = Mix(a.defend, b.defend); s.readError = Mix(a.readError, b.readError);
            s.handSpeed = Mix(a.handSpeed, b.handSpeed); s.bodySpeed = Mix(a.bodySpeed, b.bodySpeed); s.bodyAccel = Mix(a.bodyAccel, b.bodyAccel);
            s.throwHand = Mix(a.throwHand, b.throwHand); s.aimError = Mix(a.aimError, b.aimError); s.holdMin = Mix(a.holdMin, b.holdMin);
            s.holdMax = Mix(a.holdMax, b.holdMax); s.catchChance = Mix(a.catchChance, b.catchChance); s.slap = Mix(a.slap, b.slap);
            return s;
        }

        // the stances: each hand's grip from the head (head frame: x right, y up, z back) and its controller's pitch / yaw /
        // roll (degrees), how low it crouches, and how much it bobs
        public struct Stance
        {
            public string name; public Vec3[] pos, rot; public float crouch, bob;
            public Stance(string name, Vec3 p0, Vec3 p1, Vec3 r0, Vec3 r1, float crouch, float bob)
            { this.name = name; pos = new[] { p0, p1 }; rot = new[] { r0, r1 }; this.crouch = crouch; this.bob = bob; }
        }
        public static readonly Stance[] kStances = {
            new Stance("KITTEN",  new Vec3(-0.25f, -0.40f, -0.25f), new Vec3(0.25f, -0.40f, -0.25f), new Vec3(28, 0, 0), new Vec3(28, 0, 0), 0.00f, 1.0f),     // paws up
            new Stance("RELAXED", new Vec3(-0.19f, -0.86f, -0.02f), new Vec3(0.19f, -0.86f, -0.02f), new Vec3(-88, 0, 0), new Vec3(-88, 0, 0), 0.00f, 0.7f),   // arms down
            new Stance("BOXER",   new Vec3(-0.11f, -0.17f, -0.34f), new Vec3(0.13f, -0.21f, -0.22f), new Vec3(50, -10, 25), new Vec3(50, 10, -25), 0.05f, 1.8f),   // fists up
            new Stance("KARATE",  new Vec3(-0.06f, -0.34f, -0.44f), new Vec3(0.22f, -0.60f, 0.02f), new Vec3(10, 0, 70), new Vec3(0, 0, -90), 0.08f, 0.8f),   // lead hand out, one chambered
            new Stance("MANTIS",  new Vec3(-0.14f, -0.10f, -0.34f), new Vec3(0.14f, -0.10f, -0.34f), new Vec3(40, 0, 0), new Vec3(40, 0, 0), 0.06f, 1.0f),    // raised, forearms up
            new Stance("MONKEY",  new Vec3(-0.30f, -0.60f, -0.30f), new Vec3(0.30f, -0.60f, -0.30f), new Vec3(-20, 0, 0), new Vec3(-20, 0, 0), 0.18f, 2.2f),  // low, loose, bouncing
        };
        public static string StanceName(int stance) => kStances[stance < 0 ? 0 : stance >= Stances ? Stances - 1 : stance].name;

        // a person's movement toward a target: never faster than vmax, speeding up and slowing down at most amax
        public static void Approach(ref Vec3 x, ref Vec3 v, Vec3 target, float vmax, float amax, float dt)
        {
            Vec3 e = target - x;
            float d = Length(e);
            Vec3 want = d > 1e-5f ? e / d * Min(vmax, MathF.Sqrt(2f * amax * d)) : default;
            Vec3 dv = want - v;
            float l = Length(dv), m = amax * dt;
            if (l > m) dv *= m / l;
            v += dv;
            x += v * dt;
            if (d < 0.003f && Length(v) < 0.5f) { x = target; v = default; }   // there: settled (not trembling about it)
        }
        public static float MoveTime(float d, float vmax, float amax)   // from standing still to standing still, d metres away
            => d < vmax * vmax / amax ? 2f * MathF.Sqrt(d / amax) : d / vmax + vmax / amax;
    }

    public partial class Game
    {
        // the bot's tryouts left this step, and whether a plan ran out of them (it goes on the next step)
        int botTriesLeft;
        bool botTriesOut;
        bool botFrozen;                 // a tryout's copy: its bot carries out the plan being tried (no new reads or plans)
        public float lastDt = 1f / 72;  // the last step's length (the bot's reads run in the same steps)

        int BotAim(int p) => np == 2 ? 1 - p : pl[p].botTarget >= 0 ? pl[p].botTarget : AimTarget(p, pl[p].@in.head.p, Facing(p));
        // four players: whose ball read 0 watches (the most urgent attack); two: the other
        int BotThreat(int p)
        {
            if (np == 2) return 1 - p;
            // the attack that reaches it soonest (and nearest): a ball a rival last touched, flying its way. It keeps the one it
            // watches unless another is clearly sooner (a read changing every step would never get anywhere)
            Vec3 head = pl[p].@in.head.p;
            int cur = pl[p].botRead[0].owner;
            int best = cur >= 0 && cur != p ? cur : -1;
            float bu = 1e9f, cu = 1e9f;
            for (int o = 0; o < np; ++o)
            {
                if (o == p) continue;
                Ball B = pl[o].ball;
                if (B.state != BallState.Flying || B.scored || !Rivals(B.instigator, p)) continue;
                Vec3 d = head - B.pos;
                float v2 = Length2(B.vel);
                if (v2 < 1e-4f || Dot(B.vel, d) <= 0) continue;
                float tca = Dot(d, B.vel) / v2, miss = Length(B.pos + B.vel * tca - head);
                float u = tca + 0.25f * miss;
                if (o == cur) cu = u;
                if (u < bu) { bu = u; best = o; }
            }
            if (cur >= 0 && cur != p && cu < 1e8f && cu < bu + 0.15f) return cur;
            return best >= 0 ? best : (p + 1) % np;
        }
        // four players: a rival to throw at (random)
        int BotPickTarget(int p)
        {
            var r = new int[Teams.MaxPlayers];
            int n = 0;
            for (int q = 0; q < np; ++q) if (Rivals(p, q)) r[n++] = q;
            return n != 0 ? r[Min(n - 1, (int)(Rnd() * n))] : -1;
        }
        public Vec3 Facing(int p)   // flat, from the player's start toward the centre
        {
            Vec3 d = new Vec3(-tun.start[p].x, 0, -tun.start[p].z);
            float l = Length(d);
            return l > 1e-4f ? d / l : new Vec3(p == 1 ? -1f : 1f, 0, 0);
        }
        bool BodyTouchesPath(Vec3 head, Vec3 from, Vec3 to, float r)   // anywhere from `from` to `to`
        {
            if (SegSegDist(from, to, head, head) < tun.headRadius + r) return true;
            Vec3 a = head - new Vec3(0, tun.torsoTop, 0), b = head - new Vec3(0, tun.torsoBottom, 0);
            if (b.y < 0.3f) b.y = 0.3f;
            return SegSegDist(from, to, a, b) < tun.torsoRadius + r;
        }

        public BotSkill BotSkillOf(int p)
        {
            if (pl[p].botLevel >= 0) return Bot.Skill(pl[p].botLevel);
            return tun.botAdapt ? Bot.SkillAt(tun.botMatch) : Bot.Skill(tun.botLevel);
        }

        // the bot's body: where its shoulder is and where a grip puts its wrist (within tun.botArm of the shoulder)
        public Vec3 BotShoulder(int p, int h)
        {
            Pose hd = pl[p].@in.head;   // (the bot's head stays level, facing the court)
            Vec3 o = tun.botShoulder;
            return hd.p + hd.q.Rotate(new Vec3(h == 1 ? o.x : -o.x, 0, o.z)) + new Vec3(0, o.y, 0);
        }
        public Vec3 BotWrist(int h, Pose grip)
        {
            Vec3 w = tun.botWrist;
            return grip.Transform(new Vec3(h == 1 ? w.x : -w.x, w.y, w.z));
        }
        Pose BotTuck(int p, int h, Vec3 head)   // hand h pulled in to its side (a shield out of the way)
        {
            Quat q = pl[p].@in.head.q;
            return new Pose(q * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-20)),
                            head + q.Rotate(new Vec3(h == 1 ? 0.20f : -0.20f, 0, 0.12f)) + new Vec3(0, -0.48f, 0));
        }
        Vec3 BotInReach(int p, int h, Pose grip)   // the grip's position, moved in if the arm can't reach it
        {
            Vec3 sh = BotShoulder(p, h), w = BotWrist(h, grip), d = w - sh;
            float l = Length(d);
            return l > tun.botArm ? grip.p + d * (tun.botArm / l - 1f) : grip.p;   // (the hand moves in with its wrist)
        }

        // native: Game g = *this (the bot's tryouts and reads play on copies). The arena is shared (never changed)
        public Game Clone()
        {
            var g = (Game)MemberwiseClone();
            g.tun = tun.Clone();
            g.pl = new PlayerState[Teams.MaxPlayers];
            for (int q = 0; q < Teams.MaxPlayers; ++q) { g.pl[q] = new PlayerState(); g.pl[q].CopyFrom(pl[q]); }
            g.events = new List<GameEvent>(events);
            g.meetTrig = (float[])meetTrig.Clone();
            g.meetLitT = (float[])meetLitT.Clone();
            g.seenLate = (float[])seenLate.Clone();
            g.track = new BallTrack[Teams.MaxPlayers];
            for (int q = 0; q < Teams.MaxPlayers; ++q) { g.track[q] = new BallTrack(); g.track[q].CopyFrom(track[q]); }
            g.fire = CloneFire();
            g.served = new List<GameEvent>(served);
            g.aimAt = new List<Vec3>[Teams.MaxPlayers];
            for (int q = 0; q < Teams.MaxPlayers; ++q) g.aimAt[q] = new List<Vec3>(aimAt[q]);
            return g;
        }

        // `owner`'s ball's expected flight as `reader` would see it (the reader out of the way)
        void PredictBall(int owner, int reader, float horizon, float dt, List<BallPathPt> @out)
        {
            @out.Clear();
            Game g = Clone();
            PlayerState r = g.pl[reader];
            // the reader out of the way: no hit, no block (BANK ASSIST, BOUNCES: a ball that may still be turned toward them at
            // a bounce is read with the reader where they are, so the turn is in the read, and passes through them; PR #26)
            bool keepReader = g.pl[owner].ball.bankAimPending && tun.BankAssistNow() > 0;
            if (!keepReader) r.@in.head.p = new Vec3(0, -50, 0);
            foreach (HandState hs in r.hands) hs.shieldUp = hs.knuckleUp = false;
            Ball b = g.pl[owner].ball;
            int by = b.instigator;
            // (read during a step, before that step moves the ball: the ball is where the step before left it)
            @out.Add(new BallPathPt(g.now - dt, b.pos, b.vel, false, b.Clone()));
            for (float t = 0; t < horizon; t += dt, g.now += dt)
            {
                g.events.Clear();
                g.StepBall(owner, dt, keepReader ? reader : -1);
                // (a returning ball can only reach its owner)
                if ((b.state != BallState.Flying && b.state != BallState.Returning) || b.instigator != by ||
                    (b.state == BallState.Returning && owner != reader))
                    break;
                bool strike = false;
                foreach (GameEvent e in g.events) strike |= e.type == EventType.Strike && e.player == reader;
                @out.Add(new BallPathPt(g.now, b.pos, b.vel, strike, b.Clone()));
            }
        }

        // the grip that puts the shield / guard / catcher on the ball
        Pose BotBlockGrip(int p, int h, int plan, BallPathPt x)
        {
            // the surface faces where the ball comes from; a good reader angles it to send the ball back at the opponent
            Vec3 d = Normalize(x.vel);
            Vec3 n = -d;
            if (plan != Bot.CATCH && BotSkillOf(p).readsFlight)
            {
                Vec3 aim = Normalize(Normalize(ChestPoint(BotAim(p)) - x.pos) - d);
                n = Dot(aim, -d) >= 0.5f ? aim : Normalize(aim - d);
            }
            // turn `g` about n so that its local `upLocal` is as near `upWorld` as it goes
            Quat Roll(Quat g, Vec3 upLocal, Vec3 upWorld)
            {
                Vec3 cur = g.Rotate(upLocal), want = upWorld - n * Dot(upWorld, n);
                cur = cur - n * Dot(cur, n);
                if (Length2(cur) > 1e-6f && Length2(want) > 1e-6f) g = FromTo(Normalize(cur), Normalize(want)) * g;
                return g;
            }
            if (plan == Bot.SHIELD)
            {   // the plate (its normal = local +Y) facing n, its long side down
                Quat plate = Roll(FromTo(new Vec3(0, 1, 0), n), new Vec3(0, 0, 1), new Vec3(0, -1, 0));
                Pose sl = ShieldLocal(h);
                Quat gq = plate * sl.q.Conj();   // grip * shieldLocal = plate
                return new Pose(gq, x.pos - gq.Rotate(sl.p));
            }
            if (plan == Bot.KNUCKLE)
            {   // the front of the fist facing n, knuckles up
                Quat gq = Roll(FromTo(new Vec3(0, 0, -1), n), new Vec3(0, 1, 0), new Vec3(0, 1, 0));
                Vec3 ko = tun.KnuckleOffsetNow();
                if (h == 0) ko.x = -ko.x;
                return new Pose(gq, x.pos - gq.Rotate(ko));
            }
            Quat cq = pl[p].@in.head.q * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-20));   // a catch: the hand as at rest
            return new Pose(cq, x.pos - cq.Rotate(tun.catcherOffset));
        }

        // a tryout of an answer (plan, hand, grip, body offset, hand tucked in) on a copy of the game, while `owner`'s ball
        // flies: the copy's own StepBot carries it out (botFrozen), so it moves exactly as the bot will. 2 nothing comes of
        // it, 1 a strike against it (a block into its own foul zone, or the strike zone), 0 it is hit; -1 no tryouts left
        int BotTry(int p, int k, int owner, int plan, int h, Pose grip, Vec3 body, int tuck)
        {
            if (botTriesLeft <= 0) { botTriesOut = true; return -1; }   // (none left this step: not known yet)
            --botTriesLeft;
            Game g = Clone();
            g.botFrozen = true;
            PlayerState s = g.pl[p];
            BotRead r = s.botRead[k];
            r.plan = plan; r.hand = h; r.grip = grip.p; r.gripQ = grip.q; r.body = body; r.tuck = tuck;
            List<BallPathPt> path = pl[p].botRead[k].path;   // (the read stays here: copies don't carry it)
            Vec3 stand = tun.start[p] + new Vec3(0, 1.62f, 0);
            Ball b = g.pl[owner].ball;
            int by = b.instigator;
            bool touched = false, near = false;
            int result = 2;
            // (while the ball is still far off, nothing of the bot can touch it: it follows the flight already read, so only
            // the part near the bot is played out; the read is from this step, in the same steps)
            bool skip = BotSkillOf(p).readsFlight && path.Count > 0 && MathF.Abs(path[0].t - (now - lastDt)) < 1e-5f;
            int step = 0;
            for (float t = 0; t < 3f; t += lastDt)
            {
                if (step++ > 0) g.now += lastDt;   // (the first: the rest of this step, as the bot's own goes on from here)
                g.events.Clear();
                g.StepBot(p, lastDt);
                g.StepPlayer(p, lastDt);
                if (!near && skip && step < path.Count && Length(path[step].pos - stand) > 3f)
                {
                    b.CopyFrom(path[step].ball);
                    continue;
                }
                near = true;
                // (on its way off and 8 m away: should it come back, there is time to read it again)
                if (Dot(b.vel, b.pos - stand) > 0 && Length(b.pos - stand) > 8f) break;
                g.StepBall(owner, lastDt);
                foreach (GameEvent e in g.events)
                {
                    if (e.type == EventType.HitPlayer && e.player == p) return 0;
                    if (e.type == EventType.Strike && e.player == p) result = 1;
                    touched |= (e.type == EventType.ShieldBlock || e.type == EventType.KnuckleBlock) && e.player == p;
                }
                // done: it stops, the opponent sends it back (a new ball to read), or it goes home to the opponent
                if (b.state != BallState.Flying && b.state != BallState.Returning) break;
                if ((touched || by == p) && Rivals(b.instigator, p)) break;
                if (b.state == BallState.Returning && b.instigator != p) break;
            }
            return result;
        }

        // in reach, and the hand gets there by game time `by`
        bool BotReachable(int p, int h, Pose grip, float by)
        {
            if (Length(BotWrist(h, grip) - BotShoulder(p, h)) > tun.botArm - 0.02f) return false;
            BotSkill sk = BotSkillOf(p);
            // (there a moment early, to settle: a hand still moving changes where the ball goes)
            return now + Bot.MoveTime(Length(grip.p - pl[p].botHand[h]), sk.handSpeed, tun.botHandAccel) + 0.10f <= by;
        }

        // keeps botRead[k] up to date and plans the answer
        void BotWatch(int p, int k, BotSkill sk)
        {
            PlayerState s = pl[p];
            BotRead r = s.botRead[k];
            int owner = k == 0 ? BotThreat(p) : p;
            Ball B = pl[owner].ball;
            int mode = 0;
            if (B.state == BallState.Flying && Rivals(B.instigator, p) && !B.scored) mode = 1;   // coming at it
            else if ((B.state == BallState.Flying || (B.state == BallState.Returning && owner == p)) && B.instigator == p &&
                     !B.homing && !B.scored && Dot(B.vel, s.@in.head.p - B.pos) > 0)
                mode = 2;   // one it threw or deflected, coming back at it (off a wall, or returned)
            if (mode != r.mode || owner != r.owner) { r.Reset(); r.mode = mode; r.owner = owner; }   // (a new ball, or one that changed hands)
            if (mode == 0) return;
            if (r.noticeT < 0)
            {
                r.noticeT = now;
                // it stops where it is (a body still moving comes to rest a little further on) and watches
                r.body = s.botBody + s.botBodyVel * (Length(s.botBodyVel) / (2f * sk.bodyAccel));
                for (int h = 0; h < 2; ++h) { r.rel[h] = s.botHand[h] - s.@in.head.p; r.relQ[h] = s.botPhysQ[h]; }
                r.act = Rnd() < sk.defend;
                // (its own ball coming back, or sent back at it: its own to catch, whoever touched it last, 0.3.19's rule)
                r.catchIt = (mode == 2 || (k == 1 && TwoPlayerRoom)) && Rnd() < sk.catchChance;   // (four players: as they were)
                Vec3 e = new Vec3(Rnd() - 0.5f, Rnd() - 0.5f, Rnd() - 0.5f);
                r.err = Normalize(e) * (sk.readError * Rnd());
            }
            if (now - r.noticeT < sk.reaction) return;   // not seen yet
            bool fresh = r.path.Count == 0;
            if (!fresh && sk.readsFlight)
            {   // still the flight it read? (where the read says the last step left the ball)
                float tb = now - lastDt;
                int i = 0;
                while (i + 1 < r.path.Count && r.path[i].t < tb) ++i;
                Vec3 at = r.path[i].pos;
                if (i > 0 && r.path[i].t > r.path[i - 1].t)
                    at = Lerp(r.path[i - 1].pos, at, Clampf((tb - r.path[i - 1].t) / (r.path[i].t - r.path[i - 1].t), 0f, 1f));
                // (and read on before it runs out: a slow ball can be in the air longer than one read reaches; BANK ASSIST:
                // read again when a bounce planned a turn the read didn't have, or didn't plan the one it had)
                bool turnChanged = tun.BankAssistNow() > 0 && r.path[i].ball.bankAimPending != B.bankAimPending;
                fresh = turnChanged || r.path[r.path.Count - 1].t < now + (r.full ? 1f : 0f) || Length(at - r.err - B.pos) > 0.08f;
            }
            else if (!fresh)
            {
                fresh = now - r.readT > 0.12f;   // a straight-line read, taken again as the ball comes
            }
            // (a shield up or down since it planned: its plan was tried out without that, so a new one)
            int hands = (s.hands[0].shieldUp ? 1 : 0) | (s.hands[1].shieldUp ? 2 : 0);
            if (!fresh && r.hands >= 0 && hands != r.hands && r.path.Count > 0)
            {
                int wPlan = r.plan, wHand = r.hand, wTuck = r.tuck;
                Vec3 wGrip = r.grip, wBody = r.body;
                Quat wGripQ = r.gripQ;
                r.hands = hands;
                r.tried.Clear();
                BotPlan(p, k, sk);
                if (sk.hunts && r.plan == Bot.NONE && wPlan != Bot.NONE)
                {   // (nothing new in time: the plan it had goes on; it caught its ball as a throw came, 2026-10-30)
                    r.plan = wPlan; r.hand = wHand; r.grip = wGrip;
                    r.gripQ = wGripQ; r.body = wBody; r.tuck = wTuck;
                }
                return;
            }
            r.hands = hands;
            if (!fresh && !r.again) return;
            if (!fresh)
            {   // the rest of a plan, the read unchanged
                r.again = false;
                BotPlan(p, k, sk);
                return;
            }
            if (sk.readsFlight)
            {
                PredictBall(owner, p, 2.5f, lastDt, r.path);
                r.full = r.path[r.path.Count - 1].t >= now + 2.5f - 2f * lastDt;
            }
            else
            {
                r.path.Clear();
                Ball held = B.Clone();   // (a straight line: the ball as it is; never played from)
                for (int i = 0; i <= 600; ++i) r.path.Add(new BallPathPt(now + i / 240f, B.pos + B.vel * (i / 240f), B.vel, false, held));
            }
            for (int i = 0; i < r.path.Count; ++i) { BallPathPt x = r.path[i]; x.pos += r.err; r.path[i] = x; }
            r.tried.Clear();
            r.readT = now;
            BotPlan(p, k, sk);
        }

        struct DodgeSpot { public float cost; public Vec3 o; public uint key; public int idx; }

        void BotPlan(int p, int k, BotSkill sk)
        {
            PlayerState s = pl[p];
            BotRead r = s.botRead[k];
            Ball B = pl[k == 0 ? (s.botRead[0].owner >= 0 ? s.botRead[0].owner : BotThreat(p)) : p].ball;
            float br = tun.projectileRadius * B.scale;
            // (it plans from where it stands still: r.body, chosen when it noticed the ball; its hands hold where they come
            // to rest now)
            Vec3 still = r.body;
            for (int h = 0; h < 2; ++h)
            {
                Vec3 v = s.botHand[h] + s.botHandVel[h] * (Length(s.botHandVel[h]) / (2f * tun.botHandAccel)) - s.@in.head.p;
                r.rel[h] = v;
                r.relQ[h] = s.botPhysQ[h];
                if (r.mode == 1)
                {   // a ball coming at it: from its stance into the ready one (hands up in a guard catch balls by themselves)
                    Bot.Stance ks = Bot.kStances[Bot.STANCE_KITTEN];
                    r.rel[h] = s.@in.head.q.Rotate(ks.pos[h]);
                    r.relQ[h] = s.@in.head.q * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(ks.rot[h].x));
                }
            }
            // (and its head steady: it stops bobbing while it watches)
            Vec3 head = (match == MatchState.Meet ? new Vec3(p == 0 ? -tun.meetSpot : tun.meetSpot, 1.62f, 0) : tun.start[p] + new Vec3(0, 1.62f, 0)) + still;
            r.plan = Bot.NONE; r.hand = -1; r.hitT = -1; r.tuck = -1;
            float strikeT = -1;
            // the stretches of the read still to come that pass anywhere near where its body can be
            var near = new List<int>();
            {
                Vec3 mid = tun.start[p] + new Vec3(0, 1.2f, 0);
                float reach = Length(tun.botBodyRange) + 0.5f * tun.torsoBottom + tun.torsoRadius + br + 0.1f;
                for (int i = 1; i < r.path.Count; ++i)
                {
                    if (r.path[i].t < now) continue;
                    if (SegSegDist(r.path[i - 1].pos, r.path[i].pos, mid, mid) < reach) near.Add(i);
                    if (strikeT < 0 && r.path[i].strike) strikeT = r.path[i].t;
                }
            }
            foreach (int i in near)
                if (BodyTouchesPath(head, r.path[i - 1].pos, r.path[i].pos, br + 0.06f)) { r.hitT = r.path[i].t; break; }
            if (!r.act && !r.catchIt) return;
            botTriesOut = false;
            BotPlanAnswer(p, k, sk, r, s, still, head, near, br, strikeT);
            if (botTriesOut && r.plan == Bot.NONE) r.again = true;   // (out of tryouts, nothing found yet: it goes on next step)
        }

        void BotPlanAnswer(int p, int k, BotSkill sk, BotRead r, PlayerState s, Vec3 still, Vec3 head, List<int> near, float br, float strikeT)
        {
            int owner = k == 0 ? (s.botRead[0].owner >= 0 ? s.botRead[0].owner : BotThreat(p)) : p;
            bool tries = sk.guardsStrikes;   // (the best try their answer out first)
            // a tryout, or how it went the last time (a plan carried on over steps goes on from where it was)
            int TryOut(uint key, int plan, int h, Pose g, Vec3 body, int tuck)
            {
                foreach (var c in r.tried) if (c.Key == key) return c.Value;
                int v = BotTry(p, k, owner, plan, h, g, body, tuck);
                if (v >= 0) r.tried.Add(new KeyValuePair<uint, sbyte>(key, (sbyte)v));
                return v;
            }
            Pose none = Pose.Identity;
            // the hand holding its ball, its shield up: unless it blocks with it, the best pull it in to their side as a ball comes
            int sh = tries && r.mode == 1 && s.ball.state == BallState.Held && s.hands[s.ball.heldHand].shieldUp ? s.ball.heldHand : -1;
            // the ball misses its body: would it still get it, off a hand where it is (or tucked in)?
            int passive = 2;
            if (tries && r.mode == 1 && r.hitT < 0)
            {
                if (sh >= 0) passive = TryOut(0xFFFFFFFEu, Bot.NONE, -1, none, still, sh);
                if (passive == 2 && sh >= 0) r.tuck = sh;
                else passive = Max(passive, TryOut(0xFFFFFFFFu, Bot.NONE, -1, none, still, -1));
                if (passive == 0)   // (its answer must come before the ball reaches it)
                    foreach (BallPathPt x in r.path)
                        if (x.t >= now && Dot(x.pos - head, s.@in.head.q.Rotate(new Vec3(0, 0, -1))) < 0.6f) { r.hitT = x.t; break; }
            }
            BotRead other = s.botRead[1 - k];
            // (its defence may take the hand a catch of its own ball has: that catch plans again, the owner 2026-11-17)
            int busy = other.plan == Bot.SHIELD || other.plan == Bot.KNUCKLE ||
                       (other.plan == Bot.CATCH && (k != 0 || !TwoPlayerRoom)) ? other.hand : -1;   // (four players: as they were)
            // the point of the path where hand h meets the ball best (in reach, in time, before `until`): nearest its shoulder
            int Pick(int h, int plan, float until, out float dist, int need)
            {
                dist = 1e9f;
                if (h < 0 || h == busy) return -1;
                var ok = new List<(float d, int i)>();
                for (int i = 0; i < r.path.Count; ++i)
                {
                    BallPathPt x = r.path[i];
                    if (x.t <= now || x.t > until) continue;
                    Pose g = BotBlockGrip(p, h, plan, x);
                    if (BotReachable(p, h, g, x.t)) ok.Add((Length(BotWrist(h, g) - BotShoulder(p, h)), i));
                }
                ok.Sort((a, b) => a.d < b.d ? -1 : b.d < a.d ? 1 : a.i.CompareTo(b.i));   // (native: pairs, sorted)
                for (int j = 0; j < ok.Count && j < 6; ++j)
                {
                    if (plan != Bot.CATCH && tries &&
                        TryOut((uint)plan << 24 | (uint)h << 20 | (uint)ok[j].i, plan, h, BotBlockGrip(p, h, plan, r.path[ok[j].i]), still, -1) < need)
                        continue;
                    dist = ok[j].d;
                    return ok[j].i;
                }
                return -1;
            }
            void Take(int h, int plan, int i)
            {
                Pose g = BotBlockGrip(p, h, plan, r.path[i]);
                r.plan = plan; r.hand = h; r.grip = g.p; r.gripQ = g.q;
                r.body = still;   // (it holds still for it)
                BotRead o = s.botRead[1 - k];
                if (k == 0 && TwoPlayerRoom && o.plan == Bot.CATCH && o.hand == h) { o.plan = Bot.NONE; o.hand = -1; o.again = true; }   // (its own: again)
            }
            bool Either(int plan, float until, int need)   // the better of the two hands
            {
                int i0 = Pick(0, plan, until, out float d0, need), i1 = Pick(1, plan, until, out float d1, need);
                if (i0 < 0 && i1 < 0) return false;
                if (i1 >= 0 && (i0 < 0 || d1 < d0)) Take(1, plan, i1); else Take(0, plan, i0);
                return true;
            }
            // out of the ball's way: the smallest step, lean or duck that clears it, made in time
            // (tried out: its hands come along, and a shield up must not catch the ball on the way)
            bool Dodge(int need)
            {
                Vec3 right = s.@in.head.q.Rotate(new Vec3(1, 0, 0));
                Vec3 stand = tun.start[p] + new Vec3(0, 1.62f, 0);
                var ok = new List<DodgeSpot>();
                Vec3 fwd = s.@in.head.q.Rotate(new Vec3(0, 0, -1));
                float[] steps = { 0f, 0.3f, -0.15f };   // (a step forward or back, for a ball coming in on a slant)
                // (how far the ball's path stays from a body standing at offset o: below zero, it touches)
                float Gap(Vec3 o)
                {
                    Vec3 hd = stand + o, a = hd - new Vec3(0, tun.torsoTop, 0);
                    Vec3 b2 = hd - new Vec3(0, tun.torsoBottom, 0);
                    if (b2.y < 0.3f) b2.y = 0.3f;
                    float g = 9f;
                    foreach (int i in near)
                    {
                        Vec3 x0 = r.path[i - 1].pos, x1 = r.path[i].pos;
                        g = Min(g, Min(SegSegDist(x0, x1, hd, hd) - tun.headRadius, SegSegDist(x0, x1, a, b2) - tun.torsoRadius) - br);
                    }
                    return g;
                }
                float bestGap = -9f;
                Vec3 bestO = still;
                for (int iff = 0; iff < 3; ++iff)
                    for (int iy = 0; iy < 4; ++iy)
                        for (int ix = -10; ix <= 10; ++ix)
                        {
                            Vec3 o = right * (tun.botBodyRange.x * 0.1f * ix) + new Vec3(0, -tun.botBodyRange.y * iy / 3f, 0) + fwd * steps[iff];
                            float move = Length(o - still);
                            if (now + Bot.MoveTime(move, sk.bodySpeed, sk.bodyAccel) + 0.02f > r.hitT) continue;
                            float g = Gap(o);
                            if (g > bestGap) { bestGap = g; bestO = o; }
                            if (g > 0.06f) ok.Add(new DodgeSpot { cost = move - 0.3f * o.y, o = o, key = 0x40000000u | (uint)(iff * 4 + iy) << 8 | (uint)(ix + 10), idx = ok.Count });   // (ducking costs a little more)
                        }
                ok.Sort((a, b) => a.cost < b.cost ? -1 : b.cost < a.cost ? 1 : a.idx.CompareTo(b.idx));   // (native: stable_sort by cost)
                // each as it stands, or with the arm holding its ball (shield up) pulled in, if that hand gets there in time
                int tried = 0;
                for (int j = 0; j < ok.Count && tried < 16; ++j)
                    for (int v = 0; v < (sh >= 0 ? 2 : 1) && tried < 16; ++v)
                    {
                        int tuck = v != 0 ? -1 : sh;   // (tucked in first)
                        Pose g = none;
                        if (tuck >= 0)
                        {
                            g = BotTuck(p, tuck, default);   // (from its head)
                            if (now + Bot.MoveTime(Length(stand + ok[j].o + g.p - s.botHand[tuck]), sk.handSpeed, tun.botHandAccel) + 0.05f > r.hitT)
                                continue;
                        }
                        ++tried;
                        if (tries && TryOut(ok[j].key | (uint)v << 16, Bot.DODGE, -1, none, ok[j].o, tuck) < need) continue;
                        r.plan = Bot.DODGE; r.body = ok[j].o; r.tuck = tuck;
                        return true;
                    }
                if (need == 1 && ok.Count == 0 && bestGap > -8f)
                {   // nowhere clear in time: as far from it as it can get
                    r.plan = Bot.DODGE; r.body = bestO; r.tuck = sh;
                    return true;
                }
                return false;
            }
            if (r.mode == 2)
            {   // a ball of its own coming back: catch it (only its own ball), or step out of its way
                if (k == 1 && r.catchIt && Either(Bot.CATCH, r.hitT >= 0 ? r.hitT - 0.02f : 1e9f, 2)) return;
                // (a ball coming at it decides where its body goes: only then may it step out of its own ball's way)
                if (r.act && r.hitT >= 0 && (k == 0 || s.botRead[0].mode == 0) && !Dodge(2)) Dodge(1);
                return;
            }
            // its own ball sent back at it (punched or blocked: the opponent touched it last): it catches it, as its level
            // catches (the owner, 2026-11-17: not dodge it; and slap it straight back, as its catch does); a block or a
            // dodge only when no hand gets there in time
            if (k == 1 && r.catchIt && Either(Bot.CATCH, r.hitT >= 0 ? r.hitT - 0.02f : 1e9f, 2)) return;
            if (!r.act) return;
            float until = r.hitT >= 0 ? r.hitT - 0.02f : sk.guardsStrikes && strikeT >= 0 ? strikeT - 0.02f : -1f;
            if (until < 0) return;   // it misses (and the strike zone, or it doesn't mind)
            // the shield (on the hand holding a charged ball), then a knuckle guard, then a dodge; a block that fouls only
            // when nothing else will do
            bool shield = s.ball.state == BallState.Held && ShieldCharged(s.ball) && tun.shields;
            for (int need = 2; need >= 1; --need)
            {
                if (shield)
                {
                    int i = Pick(s.ball.heldHand, Bot.SHIELD, until, out float d, need);
                    if (i >= 0) { Take(s.ball.heldHand, Bot.SHIELD, i); return; }
                }
                if (tun.KnuckleGuards() && Either(Bot.KNUCKLE, until, need)) return;
                if (r.hitT >= 0 && Dodge(need)) return;
            }
        }

        // BOT STYLES (TASKS.md E19 / E23, native 7cad5c1 / 50b11ae; a player's report, 2026-11-17: "probably need different
        // logic for basic and advanced bots"; the owner: a bot option with its level, Tunables.botPlay, not tied to the
        // match): a BRAWLER holds the ball 35% shorter, and the better levels punch through a ball they block with a knuckle
        // guard; a DUELLIST keeps its shield while you keep yours (its shield the only guard, as before), and the hunting
        // levels throw at you at once when you have no shield, and aim at you more. In the two-player rooms (the SQUARE,
        // DIAMOND); the four-player rooms' bots as they were
        bool TwoPlayerRoom => np == 2 && !Octagon;   // (the SQUARE, DIAMOND)
        bool BasicBotStyle => tun.botPlay == 0 && TwoPlayerRoom;   // (BOT STYLE: BRAWLER)
        bool Duelist => tun.botPlay == 1 && TwoPlayerRoom;

        // seconds it holds a ball it just took (one draw)
        float BotHoldTime(BotSkill sk) =>
            Lerpf(sk.holdMin, sk.holdMax, Rnd()) * (np == 4 ? 0.5f : 1f) * (Octagon && tun.fireBalls >= 4 ? 0.35f : 1f) *
            (BasicBotStyle ? 0.65f : 1f);   // (BOT STYLES: BASIC's brawler keeps up the pace)

        // BotSkill.hunts: what its next throw is for, and its shape
        void BotChooseIntent(int p, BotSkill sk)
        {
            PlayerState s = pl[p];
            s.botIntent = 0;
            s.botStyle = Bot.HUNT_STRAIGHT;
            if (!sk.hunts) return;
            bool hot = StrikesHeld(p) >= 3;   // (a big, fast ball: go for the player)
            s.botIntent = hot ? (Rnd() < 0.9f ? 1 : 0) : (Rnd() < (Duelist ? 0.35f : 0.15f) ? 1 : 0);   // (the duellist: at you more)
            // and its shape: strikes every way; at the player more straight than off the walls
            float[] w = s.botIntent != 0 ? kHuntAt : kHuntStrike;
            float r = Rnd();
            int k = 0;
            while (k < Bot.HUNT_SHAPES - 1 && r >= w[k]) r -= w[k++];
            s.botStyle = k;
        }
        static readonly float[] kHuntStrike = { 0.30f, 0.25f, 0.20f, 0.15f, 0.10f }, kHuntAt = { 0.55f, 0.12f, 0.20f, 0.08f, 0.05f };

        // where it aims a throw of that shape, and its spin; j (0..7) is which of the shape's ways it tries: bit 0 which way
        // it bends (or how high), bit 1 which wall (near or far) or which side of the player, bit 2 how much it bends
        Vec3 BotHuntAim(int p, int style, int j, out Vec3 spin)
        {
            PlayerState s = pl[p];
            int opp = BotAim(p);
            Vec3 across = np == 2 ? new Vec3(0, 0, 1) : Normalize(Cross(kUp, Facing(opp)));
            spin = default;
            Vec3 at;
            float side;   // the side it goes for: the ring's side for a strike; for the player, either
            if (s.botIntent == 0)
            {   // a strike: through the ring beside the body, this throw's side
                side = (float)s.botStrikeSide;
                at = StrikeZoneCenter(opp) + across * (side * 0.6f * tun.StrikeRadius()) + new Vec3(0, (j & 1) != 0 ? 0.15f : -0.05f, 0);   // (0.42 m in a regulation ring)
            }
            else
            {   // at the player: where they usually stand (one throw in seven), or where they are, at 3 strikes
                side = (j & 2) != 0 ? 1f : -1f;
                at = StrikesHeld(p) >= 3 || !s.botOppSpotSet ? ChestPoint(opp) : new Vec3(s.botOppSpot.x, ChestPoint(opp).y, s.botOppSpot.z);
                if (style == Bot.HUNT_STRAIGHT) at.y += (j % 3 == 1) ? 0.25f : (j % 3 == 2) ? -0.3f : 0f;   // (the head, the hips)
            }
            if (style == Bot.HUNT_BANK || style == Bot.HUNT_CURVED_BANK)
            {   // off a side wall: the near one, or across off the far one (its mirror: the ball's centre turns a radius off the wall)
                float wall = (s.botIntent == 0 && (j & 2) != 0) ? -side : side;
                if (np == 2 && !tun.experimental) at.z = 2f * wall * (Bot.SideWall - 0.1f) - at.z;   // (the classic court's side walls;
                else at += across * (wall * 3f);                                        //  elsewhere well out to the side)
            }
            if (style == Bot.HUNT_FLOOR) at.y = 2f * tun.projectileRadius - at.y;   // off the floor on the way
            if (style == Bot.HUNT_CURVE || style == Bot.HUNT_CURVED_BANK || (style == Bot.HUNT_FLOOR && (j & 4) != 0))
            {   // it bends toward `bend`: a strike round the body into the ring, from an aim out the other side
                float bend = style == Bot.HUNT_CURVE && s.botIntent == 0 ? side : (j & 1) != 0 ? 1f : -1f;
                float amount = (j & 4) != 0 ? 24f : 15f, off = style == Bot.HUNT_CURVE ? ((j & 4) != 0 ? 1.3f : 0.8f) : 0.4f;
                Vec3 v = Normalize(at - s.@in.head.p);
                spin = kUp * (bend * amount * (Dot(Cross(kUp, v), across) >= 0 ? 1f : -1f));   // (the curve: spin x velocity)
                at -= across * (bend * off);
            }
            return at;
        }

        // tries one more throw on a copy of the game (planned shots; hand: from that one)
        void BotPlanShot(int p, BotSkill sk, int hand = -1)
        {
            PlayerState s = pl[p];
            if (sk.hunts)
            {   // the attack plan: this throw's aim, tried as it would fly (strike first, or at the player)
                int opp = BotAim(p), k = s.botShotsTried++, h = hand >= 0 ? hand : s.ball.heldHand;
                Vec3 fwd = s.@in.head.q.Rotate(new Vec3(0, 0, -1));
                Vec3 rel = BotShoulder(p, h) + fwd * 0.45f;
                int style = k < 8 ? s.botStyle : Bot.HUNT_STRAIGHT;   // (its shape, eight ways; then straight, if none works)
                Vec3 aim = BotHuntAim(p, style, k & 7, out Vec3 spin);
                Vec3 dir = Normalize(aim - rel);
                Game g = Clone();
                Ball gb = g.pl[p].ball;
                gb.pos = rel;
                g.Launch(p, h, dir, ThrowSpeedFromHandSpeed(sk.throwHand, p), spin, false);
                float score = -100;
                const float dt = 1f / 120;
                for (float t = 0; t < 2.5f && (gb.state == BallState.Flying); t += dt)
                {
                    g.events.Clear();
                    g.now += dt;
                    g.StepBall(p, dt);
                    bool done = false;
                    foreach (GameEvent e in g.events)
                    {
                        if (e.type == EventType.Strike && e.player == opp)
                        {   // (a strike: best through this throw's side of the ring)
                            Vec3 across = np == 2 ? new Vec3(0, 0, 1) : Normalize(Cross(kUp, Facing(opp)));
                            bool side = Dot(e.pos - StrikeZoneCenter(opp), across) * (float)s.botStrikeSide > 0;
                            score = Max(score, (s.botIntent == 0 ? (side ? 900f : 250f) : 300f) - 100f * t - (k < 8 ? 0f : 150f));
                        }
                        if (e.type == EventType.HitPlayer && e.player == opp)
                        {
                            score = Max(score, (s.botIntent == 1 ? 1000f : 600f) - 120f * t - (k < 8 ? 0f : 150f));
                            done = true;
                        }
                        else if ((e.type == EventType.ShieldBlock || e.type == EventType.KnuckleBlock) && e.player == opp)
                        {
                            score = Min(score, -500f);
                            done = true;
                        }
                    }
                    if (done) break;
                }
                if (score > s.botShotScore) { s.botShotScore = score; s.botShotDir = dir; s.botShotSpin = spin; }
                return;
            }
            {
                int opp = BotAim(p), k = s.botShotsTried++, h = s.ball.heldHand;
                Vec3 fwd = s.@in.head.q.Rotate(new Vec3(0, 0, -1));
                Vec3 rel = BotShoulder(p, h) + fwd * 0.45f;   // about where its hand lets go
                Vec3 aim = ChestPoint(opp) + new Vec3(Rnd() - 0.5f, Rnd() - 0.5f, Rnd() - 0.5f) * 0.15f, spin = default;
                float sgn = Rnd() < 0.5f ? -1f : 1f;
                switch (k % 6)
                {
                    case 0: break;                                          // the chest
                    case 1: aim.y += sgn > 0 ? 0.25f : -0.30f; break;       // the head or the hips
                    case 2: aim.z += sgn * 0.22f; break;                    // a side
                    case 3: aim.z += sgn * 3.0f; break;                     // off the side wall
                    default:
                    {                                                       // a curve (spin about up bends it along cross(up, fwd))
                        spin = kUp * (sgn * Lerpf(14f, 26f, Rnd()));
                        aim -= Cross(kUp, fwd) * (sgn * Lerpf(0.8f, 1.5f, Rnd()));
                        break;
                    }
                }
                Vec3 dir = Normalize(aim - rel);
                Game g = Clone();
                Ball gb = g.pl[p].ball;
                gb.pos = rel;
                g.Launch(p, h, dir, ThrowSpeedFromHandSpeed(sk.throwHand, p), spin);
                float score = -100;
                const float dt = 1f / 120;
                for (float t = 0; t < 2.5f && gb.state == BallState.Flying; t += dt)
                {
                    g.events.Clear();
                    g.now += dt;
                    g.StepBall(p, dt);
                    bool done = false;
                    foreach (GameEvent e in g.events)
                    {
                        if (e.type == EventType.HitPlayer && e.player == opp)
                        {   // it scores: soon, and far from their hands
                            float far = 1f;
                            for (int hh = 0; hh < 2; ++hh) far = Min(far, Length(pl[opp].@in.hand[hh].grip.p - e.pos));
                            score = 1000f - 150f * t + 200f * far + (Length2(spin) > 0 || k % 6 == 3 ? 60f : 0f);
                            done = true;
                        }
                        else if ((e.type == EventType.ShieldBlock || e.type == EventType.KnuckleBlock) && e.player == opp)
                        {
                            score = -500;
                            done = true;
                        }
                        else if (e.type == EventType.Strike && e.player == opp)
                        {
                            score = Max(score, 300f);
                        }
                    }
                    if (done) break;
                }
                if (score > s.botShotScore) { s.botShotScore = score; s.botShotDir = dir; s.botShotSpin = spin; }
            }
        }

        // Practice opponent following the same rules (shield only while holding a charged ball), moving as a person
        // can: a body that steps, leans and ducks, two arms of a person's reach and speed, either hand for everything
        void StepBot(int p, float dt)
        {
            PlayerState s = pl[p];
            BotSkill sk = BotSkillOf(p);
            if (np == 4 && (s.botTarget < 0 || !Rivals(p, s.botTarget))) s.botTarget = BotPickTarget(p);
            int opp = BotAim(p);
            if (sk.hunts && match == MatchState.InProgress && pl[opp].@in.headTracked)
            {   // (where they usually stand)
                Vec3 hp = pl[opp].@in.head.p;
                if (!s.botOppSpotSet) { s.botOppSpot = hp; s.botOppSpotSet = true; }
                s.botOppSpot += (hp - s.botOppSpot) * Min(1f, dt * 0.3f);
            }
            // (facing the centre: two players exactly as they always were)
            Quat q = Quat.AxisAngle(kUp, p < 2 && !Octagon ? Deg2Rad(p == 0 ? -90f : 90f) : MathF.Atan2(-Facing(p).x, -Facing(p).z));
            Vec3 right = q.Rotate(new Vec3(1, 0, 0)), fwd = q.Rotate(new Vec3(0, 0, -1));
            // where it stands: its end, or its side of the centre for a fist bump
            bool meet = match == MatchState.Meet;
            Vec3 stand = meet ? new Vec3(p == 0 ? -tun.meetSpot : tun.meetSpot, 1.62f, 0) : tun.start[p] + new Vec3(0, 1.62f, 0);
            if (s.botStance < 0 && s.coach.on) s.botStance = s.coach.stance;
            if (s.botStance < 0)
            {   // its stance this match. Beginners hold no guard (hands up in front block by themselves)
                float level = s.botLevel >= 0 ? (float)s.botLevel : tun.botAdapt ? tun.botMatch : (float)tun.botLevel;
                s.botStance = botLook != 0 ? BotStanceFor(p, level)   // (NEW BOT: the one it has until a new one; 0: a new one each match)
                                           : level < 1.5f ? kOpenStances[(int)(Rnd() * 3) % 3] : (int)(Rnd() * Bot.Stances) % Bot.Stances;
            }
            Bot.Stance st = Bot.kStances[s.botStance >= Bot.Stances ? 0 : s.botStance];
            // (steady while it watches a ball, and at the fist bump)
            s.botSteady += ((s.botRead[0].mode != 0 || s.botRead[1].mode != 0 || meet ? 1f : 0f) - s.botSteady) * Min(1f, dt * 5f);
            Vec3 Bob() => new Vec3(0, 0.03f * st.bob * (1f - s.botSteady) * MathF.Sin(now * 2.1f * MathF.Sqrt(st.bob)), 0);
            // a bow (KARATE, at the fist bump): the head down and forward
            s.botBow += ((s.botStance == Bot.STANCE_KARATE && meet && meetBumped ? 1f : 0f) - s.botBow) * Min(1f, dt * 6f);
            Pose HeadAt() => new Pose(q * Quat.AxisAngle(new Vec3(1, 0, 0), -0.6f * s.botBow),
                                      stand + s.botBody + Bob() + fwd * (0.10f * s.botBow) - new Vec3(0, 0.12f * s.botBow, 0));
            Ball b = s.ball;
            s.@in.headTracked = true;
            s.@in.head = HeadAt();
            Vec3 Side(int h) => right * (h == 1 ? 1f : -1f);
            Quat StanceQ(int h)
            {
                Vec3 rr = st.rot[h];
                return q * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(rr.x)) * Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(rr.y)) * Quat.AxisAngle(new Vec3(0, 0, 1), Deg2Rad(rr.z));
            }
            Quat restQ = q * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-20));   // (a hand reaching for something: forward, a little down)
            Vec3 Rest(int h) => s.@in.head.p + q.Rotate(st.pos[h]);
            if (!s.botHandSet)
            {
                for (int h = 0; h < 2; ++h) { s.botHand[h] = Rest(h); s.botHandVel[h] = default; s.botHandQ[h] = s.botPhysQ[h] = StanceQ(h); }
                s.botHandSet = true;
            }
            for (int h = 0; h < 2; ++h)
            {
                ref HandInput hi = ref s.@in.hand[h];
                hi.tracked = true;
                hi.trigger = 0;
                hi.angVel = default;
                hi.grip = new Pose(s.botHandQ[h], s.botHand[h]);
            }
            s.botTimer -= dt;
            botTriesLeft = Bot.TriesPerStep;
            if (!botFrozen && !(s.coach.on && !s.coach.watch))
            {   // (in a tryout: the plan being tried, as it is; a coach told not to watch: it stands still, defending nothing)
                BotWatch(p, 0, sk);
                BotWatch(p, 1, sk);
            }
            BotRead[] reads = s.botRead;
            bool HandBusy(int h)
            {
                foreach (BotRead rd in reads)
                    if (rd.hand == h && (rd.plan == Bot.SHIELD || rd.plan == Bot.KNUCKLE || rd.plan == Bot.CATCH)) return true;
                return false;
            }
            // where everything goes: the hands at rest (the ball held ready), the body swaying a little where it stands
            Pose[] want = { new Pose(StanceQ(0), Rest(0)), new Pose(StanceQ(1), Rest(1)) };
            var turnQ = new Quat[2];   // (a throwing hand's drawn turn; turned: it has one)
            var turned = new bool[2];
            float[] speed = { sk.handSpeed, sk.handSpeed };
            if (b.state == BallState.Held) want[b.heldHand].p = s.@in.head.p + new Vec3(0, -0.15f, 0) + Side(b.heldHand) * 0.30f - fwd * 0.10f;
            // (it stops swaying while it watches a ball)
            Vec3 bodyWant = right * (meet ? 0f : 0.15f * MathF.Sin(now * 0.9f)) - new Vec3(0, st.crouch, 0);
            bool threatened = false;
            foreach (BotRead rd in reads)
            {
                // (no throw while a ball comes at it: a wind-up could swing its shield into it. Four players: only an attack that
                // reaches it within a second, or there would always be one on its way and the bot would hardly ever throw)
                threatened |= rd.mode != 0 && (np == 2 || (rd.mode == 1 && rd.hitT >= 0 && rd.hitT - now < 1.0f));
                if (rd.plan == Bot.SHIELD || rd.plan == Bot.KNUCKLE || rd.plan == Bot.CATCH) want[rd.hand] = new Pose(rd.gripQ, rd.grip);
                // BOT STYLES, BASIC's brawler (the better levels): a knuckle block punched through, the fist cocked 15 cm back
                // and driven through the ball's way over its last tenth of a second (the punch sends it back faster)
                if (rd.plan == Bot.KNUCKLE && BasicBotStyle && sk.planShots && rd.hitT >= 0)
                {
                    float to = rd.hitT - now;
                    float off = to > 0.08f ? -0.15f : -0.15f + (0.08f - to) * 2.1f;
                    want[rd.hand].p = rd.grip + rd.gripQ.Rotate(new Vec3(0, 0, -1)) * Clampf(off, -0.15f, 0.06f);
                }
                if (rd.mode != 0 && rd.plan != Bot.CATCH && (rd == reads[0] || reads[0].mode == 0)) bodyWant = rd.body;   // (still while it watches; or its dodge)
            }
            if (Octagon) threatened |= BotFireDodge(p, sk, right, ref bodyWant);   // (FIREBALL BLITZ: out of the fireballs' way)
            // while it watches a ball, the hands it isn't using keep their place (from its head, so a dodge carries them):
            // what its tryouts assumed (a hand still moving could swing a shield into the ball)
            bool watching = false;
            Vec3[] carried = { new Vec3(0, 1e9f, 0), new Vec3(0, 1e9f, 0) };   // (from its head: placed once the body has moved this step)
            foreach (BotRead rd in reads)
            {
                if (rd.mode == 0 || watching) continue;
                watching = true;
                for (int h = 0; h < 2; ++h)
                    if (!HandBusy(h))
                    {
                        want[h] = (rd.plan == Bot.DODGE || rd.plan == Bot.NONE) && rd.tuck == h ? BotTuck(p, h, s.@in.head.p) : new Pose(rd.relQ[h], s.@in.head.p + rd.rel[h]);
                        carried[h] = want[h].p - s.@in.head.p;
                    }
            }
            // its ball off the spawner, with the nearer free hand (four players: unless an attack reaches it within a second;
            // there is nearly always a ball to watch)
            bool soon = false;
            foreach (BotRead rd in reads) soon |= rd.mode == 1 && rd.hitT >= 0 && rd.hitT - now < 1.0f;
            if (match == MatchState.InProgress && b.state == BallState.Idle && (np == 2 ? !watching : !soon))
            {
                int h = -1;
                float bd = 1e9f;
                for (int k = 0; k < 2; ++k)
                {
                    float d = Length(BotShoulder(p, k) - b.pos);
                    if (!HandBusy(k) && d < bd) { bd = d; h = k; }
                }
                if (s.coach.on && !HandBusy(s.coach.hand)) h = s.coach.hand;
                if (h >= 0)
                {
                    want[h] = new Pose(restQ, b.pos - restQ.Rotate(tun.launcherOffset));
                    if (Length(LauncherPoint(p, h) - b.pos) < 0.25f)
                    {
                        b.state = BallState.Held; b.heldHand = h; b.vel = default;
                        Emit(EventType.Grab, p, h, b.pos);
                        s.botTimer = BotHoldTime(sk);   // (four players, BASIC's brawler: quicker)
                        s.botThrow = 0; s.botShotsTried = 0; s.botShotScore = -1e9f;
                        if (sk.hunts) BotChooseIntent(p, sk);
                    }
                }
            }
            // a throw: wind up behind the shoulder, swing through at a throw's hand speed, let go
            bool holding = b.state == BallState.Held;
            if (!holding || match != MatchState.InProgress) s.botSlap = false;
            foreach (BotRead rd in reads)   // (but not with a ball on its way into it: its shield goes with the ball)
                if (rd.mode == 1 && (rd.hitT >= 0 || rd.plan != Bot.NONE)) s.botSlap = false;
            if (!holding || (threatened && !s.botSlap) || match != MatchState.InProgress) s.botThrow = 0;   // (a slap shot goes on)
            if (holding && !ShieldCharged(b) && !s.coach.on) s.botTimer = Min(s.botTimer, 0.35f);   // recharge by throwing
            if (holding && (!threatened || s.botSlap) && match == MatchState.InProgress)
            {
                int h = b.heldHand;
                if (s.botThrow == 0 && s.coach.on)
                {   // a coach (TRAINING): the throw it is told, when it is told
                    if (s.coach.fire)
                    {
                        s.coach.fire = false;
                        s.botShotDir = Normalize(s.coach.dir);
                        s.botShotSpin = s.coach.spin;
                        s.botThrow = 1;
                        s.botThrowT = now;
                    }
                }
                else if (s.botThrow == 0)
                {
                    // one try a step while it waits (not in a step its reads already spent tryouts in)
                    if (sk.planShots && !Octagon && s.botShotsTried < 12 && botTriesLeft == Bot.TriesPerStep) BotPlanShot(p, sk);
                    // the best keep their ball (their shield) while the opponent can throw, unless their shot clock runs low
                    Ball ob = pl[opp].ball;
                    bool patient = sk.guardsStrikes && tun.shields && !tun.KnuckleGuards() && ShieldCharged(b) &&
                                   (ob.state == BallState.Held || ob.state == BallState.Idle) && s.shotClock > 3f &&
                                   s.botTimer > -4f;
                    // BOT STYLES, ADVANCED's duellist (the hunting levels): you without a shield (your ball away from you, or
                    // uncharged): it throws now, at you (its shot tried out afresh for that)
                    bool exposed = Duelist && sk.hunts && tun.shields &&
                                   (ob.state == BallState.Flying || ob.state == BallState.Returning || ob.state == BallState.Spawning ||
                                    ob.state == BallState.Dead || !ShieldCharged(ob));
                    if (exposed && !s.botPounce)
                    {
                        s.botPounce = true;
                        s.botIntent = 1;
                        s.botShotsTried = 0; s.botShotScore = -1e9f;
                        s.botTimer = MathF.Min(s.botTimer, 0.15f);
                    }
                    else if (!exposed) s.botPounce = false;
                    if (s.botTimer <= 0 && (!patient || s.botPounce))   // (the duellist doesn't wait on a player without a shield)
                    {
                        if (Octagon)
                        {   // a fireball: tried out against its target as it would hop
                            s.botShotDir = BotFireAim(p, h, sk, out s.botShotSpin);
                        }
                        else if (sk.hunts && s.botShotScore <= 0)
                        {   // (the plan's own aim, straight)
                            s.botShotDir = Normalize(BotHuntAim(p, Bot.HUNT_STRAIGHT, 0, out Vec3 spin) - (BotShoulder(p, h) + fwd * 0.45f));
                            s.botShotSpin = spin;
                        }
                        else if (!(sk.planShots && s.botShotScore > 0))
                        {   // at the chest, off the side wall, or curved
                            Vec3 rel = BotShoulder(p, h) + fwd * 0.45f;
                            Vec3 target = ChestPoint(opp) + new Vec3(Rnd() - 0.5f, Rnd() - 0.5f, Rnd() - 0.5f) * sk.aimError;
                            s.botShotSpin = default;
                            if (Rnd() < 0.3f)
                            {   // off a side wall
                                float sw = (Rnd() < 0.5f ? -1f : 1f) * 3.0f;
                                if (np == 2) target.z += sw; else target += Normalize(Cross(kUp, target - rel)) * sw;
                            }
                            else if (Rnd() < 0.4f) s.botShotSpin = kUp * ((Rnd() < 0.5f ? -1f : 1f) * Lerpf(12f, 28f, Rnd()));
                            s.botShotDir = Normalize(target - rel);
                        }
                        else
                        {   // the planned throw, made as well as it can
                            s.botShotDir = Normalize(s.botShotDir + new Vec3(Rnd() - 0.5f, Rnd() - 0.5f, Rnd() - 0.5f) * (sk.aimError * 0.1f));
                        }
                        s.botThrow = 1;
                        s.botThrowT = now;
                    }
                }
                // the throwing hand turns as a person's does through a throw (the owner, 2026-11-19: "the way the bots arms look
                // during throws is awful ... very unnatural", worst in FIREBALL BLITZ): it kept its guard's turn all the way, so
                // up behind the shoulder, and swung at someone off to the side (in the octagon it faces the centre), the
                // forearm twisted round. Now it points along the throw (toward who it throws at), tipped up as it is drawn back
                // and level as it comes through (TASKS.md E41, native a812558)
                Quat ThrowQ(float pitchDeg)
                {
                    var d = new Vec3(s.botShotDir.x, 0, s.botShotDir.z);
                    d = Length2(d) > 1e-6f ? Normalize(d) : fwd;
                    return Quat.AxisAngle(kUp, MathF.Atan2(-d.x, -d.z)) * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(pitchDeg));
                }
                if (s.botThrow == 1)
                {   // wind up (a coach: always as long, so its throws leave when they are planned to)
                    want[h].p = BotShoulder(p, h) + new Vec3(0, 0.12f, 0) - fwd * 0.22f + Side(h) * 0.06f;
                    turnQ[h] = ThrowQ(35f);
                    turned[h] = true;
                    if (s.coach.on ? now - s.botThrowT >= 0.45f - 1e-4f : Length(s.botHand[h] - want[h].p) < 0.06f || now - s.botThrowT > 0.6f)
                    { s.botThrow = 2; s.botThrowT = now; }
                }
                else if (s.botThrow == 2)
                {   // the swing, forward along the throw
                    if (s.botSlap && sk.hunts && s.botStyle != Bot.HUNT_STRAIGHT && !threatened && s.botShotsTried < 8 && botTriesLeft == Bot.TriesPerStep)
                        BotPlanShot(p, sk);   // (a slap shot's shape, one try a step)
                    Vec3 flat = s.botShotDir;
                    flat.y *= 0.3f;
                    want[h].p = BotShoulder(p, h) + Normalize(flat) * 0.50f;
                    turnQ[h] = ThrowQ(-5f);
                    turned[h] = true;
                    speed[h] = s.coach.on ? Max(2f, s.coach.speed / 2.2f) : sk.throwHand;
                    if (s.coach.on ? now - s.botThrowT >= 0.25f - 1e-4f : Length(s.botHand[h] - want[h].p) < 0.15f || now - s.botThrowT > 0.4f)
                    {
                        b.pos = LauncherPoint(p, h);
                        if (s.coach.on)
                        {   // (the direction as it is now: a coach throwing at you aims where you are)
                            b.pos = s.coach.from;
                            Launch(p, h, Normalize(s.coach.dir), s.coach.speed, s.coach.spin, false);
                        }
                        else Launch(p, h, s.botShotDir, ThrowSpeedFromHandSpeed(sk.throwHand, p), s.botShotSpin, !sk.hunts);
                        if (sk.hunts && s.botIntent == 0) s.botStrikeSide = -s.botStrikeSide;   // (the next strike: the other side)
                        s.botThrow = 0;
                        s.botSlap = false;
                        s.botPounce = false;
                        s.botTimer = BotHoldTime(sk);   // (four players, BASIC's brawler: quicker)
                        s.botShotsTried = 0;
                        s.botShotScore = -1e9f;
                        if (np == 4) s.botTarget = BotPickTarget(p);   // (four players: someone, maybe someone else, next)
                    }
                }
            }
            // move: the body (within a person's step, lean and duck), then each hand (never beyond its reach)
            Bot.Approach(ref s.botBody, ref s.botBodyVel, bodyWant, sk.bodySpeed, sk.bodyAccel, dt);
            {
                Vec3 R = tun.botBodyRange;
                float lat = Clampf(Dot(s.botBody, right), -R.x, R.x), fw = Clampf(Dot(s.botBody, fwd), -0.15f, R.z);
                Vec3 o = right * lat + fwd * fw + new Vec3(0, Clampf(s.botBody.y, -R.y, 0.05f), 0);
                if (Length2(o - s.botBody) > 1e-10f) s.botBodyVel = default;   // (at the edge of where it can go)
                s.botBody = o;
            }
            s.@in.head = HeadAt();
            int bump = -1;   // the fist bump (owner, 2026-10-28): one offers a fist in its ring, the other meets it there
            bool bumpClosed = false;
            if (meet && !meetBumped)
            {
                int oh = -1;
                float ox = 9f;
                for (int k = 0; k < 2; ++k)
                    if (pl[opp].@in.hand[k].tracked && MathF.Abs(pl[opp].@in.hand[k].grip.p.x) < ox) { ox = MathF.Abs(pl[opp].@in.hand[k].grip.p.x); oh = k; }
                int lit = -1;   // their fist offered in their ring
                for (int k = 0; k < 2; ++k) if (MeetLit(opp, k)) lit = k;
                if (lit >= 0) oh = lit;
                if (oh >= 0)
                {   // the nearer fist; a fist straight in front keeps the hand it has (no switching back and forth)
                    float acrossD = Dot(pl[opp].@in.hand[oh].grip.p - s.@in.head.p, right);
                    if (MathF.Abs(acrossD) > 0.08f) s.botBumpHand = acrossD > 0 ? 1 : 0;
                }
                bump = s.botBumpHand;
                Quat fq = q;   // the fist level, knuckles to the front
                float waited = tun.meetTime - stateTimer;
                Vec3 at;
                if (lit >= 0 && meetLitT[opp] >= 0 && now - meetLitT[opp] > sk.reaction + 0.15f)
                {   // they offer: meet it, there
                    at = pl[opp].@in.hand[lit].grip.Transform(tun.fistFront) - fq.Rotate(tun.fistFront);
                    bumpClosed = true;
                }
                else if (waited > 1.5f + sk.reaction)
                {   // nobody has offered (or not yet seen): it offers, its fist still in its ring
                    at = MeetRing(p) + fwd * 0.03f - fq.Rotate(tun.fistFront);
                    bumpClosed = true;
                }
                else
                {   // ready: the hand low in front, by its body (outside its ring)
                    at = s.@in.head.p + fwd * 0.10f - new Vec3(0, 0.55f, 0) + Side(bump) * 0.15f;
                }
                want[bump] = new Pose(fq, at);
                speed[bump] = Min(speed[bump], 1.6f);   // a calm reach, not a punch
                carried[bump].y = 1e9f;
            }
            for (int h = 0; h < 2; ++h)   // the hands it holds still go with the body (in step with it, as its tryouts have them)
                if (carried[h].y < 1e8f) want[h].p = s.@in.head.p + carried[h];
            for (int h = 0; h < 2; ++h)
            {
                Bot.Approach(ref s.botHand[h], ref s.botHandVel[h], BotInReach(p, h, want[h]), speed[h],
                             tun.botHandAccel * (speed[h] > sk.handSpeed ? 2f : 1f), dt);
                s.botPhysQ[h] = Nlerp(s.botPhysQ[h], want[h].q, Min(1f, dt * 18f));
                s.botHandQ[h] = Nlerp(s.botHandQ[h], turned[h] ? turnQ[h] : want[h].q, Min(1f, dt * 18f));
                s.botHand[h] = BotInReach(p, h, new Pose(s.botPhysQ[h], s.botHand[h]));   // (an arm comes along with the body)
                ref HandInput hi = ref s.@in.hand[h];
                hi.grip = new Pose(s.botHandQ[h], s.botHand[h]);
                hi.trigger = (b.state == BallState.Held && b.heldHand == h) || (h == bump && bumpClosed) ? 1f : 0f;   // (a fist for the bump)
            }
            // the slap shot's shape, planned while its ball comes back to it (owner, 2026-11-02)
            if (sk.hunts && !Octagon && !s.coach.on && match == MatchState.InProgress && !holding &&
                (b.state == BallState.Flying || b.state == BallState.Returning) && s.botRead[1].plan == Bot.CATCH && s.botRead[1].hand >= 0 &&
                !threatened && botTriesLeft == Bot.TriesPerStep)
            {   // (not while a ball comes at it: its defence comes first)
                if (!s.botPre) { BotChooseIntent(p, sk); s.botPre = true; s.botShotsTried = 0; s.botShotScore = 0f; }
                if (s.botStyle != Bot.HUNT_STRAIGHT && s.botShotsTried < 8) BotPlanShot(p, sk, s.botRead[1].hand);
            }
            if (holding) s.botPre = false;
            // a catch: its own ball, in its catcher
            BotRead c = s.botRead[1];
            if (c.plan == Bot.CATCH && (b.state == BallState.Flying || b.state == BallState.Returning))   // (its own, whoever touched it)
            {
                s.@in.hand[c.hand].trigger = 1;
                // (the ball's way this step, not only where it is: a fast ball sent back at it covers 20-30 cm a step and could
                // pass the hand between two looks, as shields and knuckle guards check its whole path; four players: as they were)
                Vec3 cp = CatcherPoint(p, c.hand), seg = TwoPlayerRoom ? b.vel * lastDt : new Vec3();
                float su = Clampf(Dot(cp - b.pos, seg) / MathF.Max(1e-6f, Dot(seg, seg)), 0f, 1f);
                if (Length(b.pos + seg * su - cp) < tun.catchHalfHeight + BallRadius(p))
                {
                    b.state = BallState.Held; b.heldHand = c.hand; b.vel = default; b.spin = default;
                    Emit(EventType.Caught, p, c.hand, b.pos);
                    s.botTimer = BotHoldTime(sk);   // (four players, BASIC's brawler: quicker)
                    bool pre = s.botPre && s.botShotScore > 0;   // (a shape found while it came back)
                    Vec3 preDir = s.botShotDir, preSpin = s.botShotSpin;
                    s.botThrow = 0; s.botShotsTried = 0; s.botShotScore = -1e9f;
                    // the slap shot (owner, 2026-10-29): caught on the swing and sent straight back, no hold, no wind-up
                    if (sk.hunts && !s.botPre) BotChooseIntent(p, sk);
                    s.botPre = false;
                    if (!s.coach.on && match == MatchState.InProgress && sk.slap > 0f && Rnd() < sk.slap && sk.hunts)
                    {   // (by the plan)
                        Vec3 from = LauncherPoint(p, c.hand);
                        // (straight to start with; its shape, curved or off a wall, tried out during the swing)
                        s.botShotDir = Normalize(BotHuntAim(p, Bot.HUNT_STRAIGHT, 0, out Vec3 spin) - from);
                        s.botShotSpin = spin;
                        s.botShotsTried = 0;
                        s.botShotScore = 0f;
                        if (pre) { s.botShotDir = preDir; s.botShotSpin = preSpin; s.botShotsTried = 8; s.botShotScore = 1f; }   // (planned)
                        s.botThrow = 2;
                        s.botThrowT = now;
                        s.botSlap = true;
                    }
                    else if (!s.coach.on && match == MatchState.InProgress && sk.slap > 0f && Rnd() < sk.slap)
                    {   // (its shot chosen as its throws are)
                        Vec3 from = LauncherPoint(p, c.hand);
                        Vec3 to = ChestPoint(opp) + new Vec3(Rnd() - 0.5f, Rnd() - 0.5f, Rnd() - 0.5f) * sk.aimError;
                        s.botShotSpin = default;
                        if (Rnd() < 0.3f)
                        {   // off a side wall
                            float sw = (Rnd() < 0.5f ? -1f : 1f) * 3.0f;
                            if (np == 2) to.z += sw; else to += Normalize(Cross(kUp, to - from)) * sw;
                        }
                        else if (Rnd() < 0.6f)
                        {
                            s.botShotSpin = kUp * ((Rnd() < 0.5f ? -1f : 1f) * Lerpf(12f, 28f, Rnd()));
                        }
                        s.botShotDir = Normalize(to - from);
                        s.botThrow = 2;
                        s.botThrowT = now;
                        s.botSlap = true;
                    }
                }
            }
        }
        static readonly int[] kOpenStances = { Bot.STANCE_KITTEN, Bot.STANCE_RELAXED, Bot.STANCE_MONKEY };
    }
}
