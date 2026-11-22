// The simulation's data types (app/src/game.h), ported 1:1.
// The native structs are values (copied on assignment). Here the ones that hold arrays or are reset in place are
// classes: PlayerInput is copied explicitly (Clone / CopyFrom) wherever the native code copies it, and Ball, HandState
// and PlayerState are reset in place (Reset) where the native code assigns a fresh value, so references to them stay
// valid as native references do.
using System;
using System.Collections.Generic;

namespace ProjectArena.Sim
{
    // collision tag bits (match tools/build_assets.py)
    public static class Tag
    {
        public const uint Arena = 1, Return = 2, Foul = 4, Team0 = 8, Team1 = 16,
                          Team2 = 32, Team3 = 64,   // (2, 3: the FOUR CORNERS players' zones)
                          SideWall = 128;           // (native 0.3.22, BOUNCES: DIAMOND's side walls, ExpArena.DiamondCollision)
    }

    // FOUR CORNERS' teams (Tunables.teamMode): everyone for themselves; partners in opposite corners (0 with 1, 2 with 3);
    // THE ONE alone
    public static class Teams
    {
        public const int FFA = 0, TwoVTwo = 1, ThreeVOne = 2;
        public const int MaxPlayers = 4;   // up to four players (FOUR CORNERS); every other mode is two (slots 0 and 1)
    }

    public struct CollisionTri
    {
        public Vec3 a, b, c, n;
        public uint tags;
    }

    public struct HandInput
    {
        public Pose grip;       // controller grip pose in world (app) space
        public Vec3 angVel;     // controller angular velocity in world space (rad/s), from XrSpaceVelocity
        public bool tracked;
        public float trigger;
        public float squeeze;

        public static HandInput Default => new HandInput { grip = Pose.Identity };
    }

    // Avatar look indices into the option tables in avatar.cpp. Travels with every input so the
    // opponent and spectators see it (16 bytes, sent raw).
    public struct AvatarDesc
    {
        public byte suit, build, hair, hairColor, headgear, visor, visorTint, mask;
        public byte pattern, primary, secondary, glow, skin, lower, body, gauntlet;   // lower: shorts (0 = waist up); body: 0 masc, 1 fem

        public static AvatarDesc Default => new AvatarDesc { build = 1, hair = 2, hairColor = 1, visor = 1, pattern = 1, secondary = 1, skin = 2 };
    }

    public class PlayerInput
    {
        public Pose head = Pose.Identity;
        public bool headTracked;
        public HandInput[] hand = { HandInput.Default, HandInput.Default };   // 0 = left, 1 = right
        public bool buttonA;            // restart / ready
        public AvatarDesc avatar = AvatarDesc.Default;   // cosmetic only; the simulation ignores it
        // HAND TIMES (native 0.3.19): online, on the host, when the player's own headset took this sample, on the host's
        // game clock (-1: now). The hands' history is timed by it. Not sent: the host sets it (Net)
        public float sampleT = -1f;

        public PlayerInput Clone()
        {
            var c = new PlayerInput();
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(PlayerInput o)
        {
            head = o.head;
            headTracked = o.headTracked;
            hand[0] = o.hand[0];
            hand[1] = o.hand[1];
            buttonA = o.buttonA;
            avatar = o.avatar;
            sampleT = o.sampleT;
        }
    }

    public enum BallState : byte { Spawning, Idle, Held, Flying, Dead, Returning }
    // Meet (appended, so older builds keep their numbers): two players start a match face to face in the centre
    public enum MatchState : byte { WaitingForPlayers, Countdown, InProgress, RoundSummary, MatchSummary, Meet }
    public enum EventType : byte
    {
        Spawn, Grab, Throw, ThrowFail, Bounce, ShieldBlock, ShieldBreak, HitPlayer,
        Returned, Caught, Foul, RoundStart, RoundEnd, MatchEnd, Countdown,
        KnuckleBlock, Recharge, Strike, Overtime, SpawnRise, FistBump,
        LeadChange,     // (FOUR CORNERS: a new team leads; value = that team)
        Knockout,       // FIREBALL BLITZ: player out (hand: who did it; value: how many are left)
        Fizzle,         // ... a fireball spent (player: whose)
        RingClose,      // ... the room is about to close in (value: seconds)
    }

    public struct GameEvent
    {
        public EventType type;
        public int player;      // owning / affected player
        public int hand;        // hand index where relevant (-1 otherwise)
        public Vec3 pos;
        public float value;
        public int ball;        // hits, strikes, blocks: the ball (its owner's index) behind it; -1 otherwise. Not sent: host only

        public GameEvent(EventType type, int player, int hand, Vec3 pos, float value, int ball = -1)
        {
            this.type = type; this.player = player; this.hand = hand; this.pos = pos; this.value = value; this.ball = ball;
        }
    }

    public class Ball
    {
        public BallState state;
        public Vec3 pos, vel, spin;     // spin: angular velocity (rad/s, world)
        public float speedLimit;
        public float timer;
        public int heldHand;
        public int instigator;          // who threw / last deflected it (decides scoring and zone rules)
        public bool charged;            // powers the holder's shield; drained by a block, recharged past the foul line
        public bool scored;
        public bool rechargePending;
        public int bounces;
        public float scale;             // grows with strikes against the opponent
        // a foul (owner, 2026-10-21): the ball no longer dissolves. It bounces on, off the wall it is heading for, then
        // flies home to its launch spot (homing), harmless all the way; fouled = still before that wall
        public bool fouled, homing;
        public float progX, progT;      // stuck balls: where along the court (x) it was at progT (its timer), last metre on
        public float progZ;             // (four players: across the room too)
        // CATCHES ONLINE (native 0.3.19): the game time it reached its owner, a player who isn't hosting, on its way
        // back (-1: not); the hit waits there for their round trip, in case their catch is still on its way
        public float hitWaitAt = -1f;
        // BLOCKS ONLINE (TASKS.md E61, native e0292de): the game time it reached a player who isn't hosting and could still
        // block it (a shield or knuckle guard up), blockWaitBy; held there for their round trip in case their block, as they
        // saw it, is on its way. blockWaitVel: how it was flying (a late block deflects it from where it met it)
        public float blockWaitAt = -1f;
        public int blockWaitBy = -1;
        public Vec3 blockWaitVel;
        // BANK ASSIST (BOUNCES, native 0.3.22): its turn still to come (the axis times the radians left), and whether a
        // bounce may still plan one (a player's own throw, until one is planned)
        public Vec3 bankTurn;
        public bool bankAimPending;

        public Ball() => Reset();

        public Ball Clone() { var c = new Ball(); c.CopyFrom(this); return c; }

        public void CopyFrom(Ball o)
        {
            state = o.state; pos = o.pos; vel = o.vel; spin = o.spin; speedLimit = o.speedLimit; timer = o.timer;
            heldHand = o.heldHand; instigator = o.instigator; charged = o.charged; scored = o.scored;
            rechargePending = o.rechargePending; bounces = o.bounces; scale = o.scale; fouled = o.fouled; homing = o.homing;
            progX = o.progX; progT = o.progT; progZ = o.progZ;
            hitWaitAt = o.hitWaitAt;
            blockWaitAt = o.blockWaitAt; blockWaitBy = o.blockWaitBy; blockWaitVel = o.blockWaitVel;
            bankTurn = o.bankTurn; bankAimPending = o.bankAimPending;
        }

        public void Reset()
        {
            state = BallState.Spawning;
            pos = vel = spin = default;
            speedLimit = 0;
            timer = 0;
            heldHand = -1;
            instigator = -1;
            charged = true;
            scored = false;
            rechargePending = false;
            bounces = 0;
            scale = 1f;
            bankTurn = default; bankAimPending = false;
            fouled = homing = false;
            progX = progT = progZ = 0;
            hitWaitAt = -1f;
        }
    }

    public class HandState
    {
        public Vec3[] hist = new Vec3[64];
        public Vec3[] angHist = new Vec3[64];
        public Pose[] gripHist = new Pose[64];
        public float[] histT = new float[64];
        public int histN, histHead;
        public bool shieldUp;
        public bool knuckleUp;
        public float prevTrigger;

        public HandState() => Reset();

        public void CopyFrom(HandState o)
        {
            Array.Copy(o.hist, hist, 64); Array.Copy(o.angHist, angHist, 64); Array.Copy(o.gripHist, gripHist, 64);
            Array.Copy(o.histT, histT, 64);
            histN = o.histN; histHead = o.histHead; shieldUp = o.shieldUp; knuckleUp = o.knuckleUp; prevTrigger = o.prevTrigger;
        }

        public void Reset()
        {
            for (int i = 0; i < 64; ++i) { hist[i] = default; angHist[i] = default; gripHist[i] = Pose.Identity; histT[i] = 0; }
            histN = histHead = 0;
            shieldUp = knuckleUp = false;
            prevTrigger = 0;
        }
    }

    public class PlayerState
    {
        public PlayerInput @in = new PlayerInput();
        public HandState[] hands = { new HandState(), new HandState() };
        public Ball ball = new Ball();
        public int score;           // points (this round in Advanced)
        public int roundWins;
        public int strikesAgainst;  // strikes the opponent holds against this player
        public int strikesFor;      // four players: the strikes this player holds (its ball grows with them); lost when hit
        public float shotClock;     // seconds remaining on this player's shot clock
        public int health;          // FIREBALL BLITZ: hits left (0: out)
        public ushort fireSeen, fireAct;   // FIREBALL BLITZ's bots: the fireballs (bit owner * 3 + k) noticed, and dodged
        public Vec3 botDodge;              // ... where it stepped to, and until when it stays there
        public float botDodgeUntil = -1;
        public bool isBot;
        public bool ready;          // READY pressed (A) before the match (Tunables.readyUp; a bot always is)
        // bot (Game.StepBot, Sim/Bot.cs)
        public int botLevel = -1;          // -1: tun.botLevel (the lobby's bots choose their own)
        public int botStance = -1;         // Bot.STANCE_* (-1: picked as its first step of a match; no guard below PRO)
        public float botBow;               // 0 upright .. 1 bowed (KARATE, at the fist bump)
        public int botBumpHand = 1;        // the hand it bumps with (kept unless their fist is clearly on its other side)
        public float botSteady;            // 0 bobbing as its stance does .. 1 still (it steadies its head while it watches a ball)
        public float botTimer;             // until its next throw
        public Vec3 botBody, botBodyVel;   // its head's offset from where it stands (steps, leans, ducks), and that offset's speed
        public Vec3[] botHand = new Vec3[2], botHandVel = new Vec3[2];   // its grips, moving at a person's speed
        // its hands' turn: as drawn (botHandQ: what the game shows and sends, turned with the arm through a throw, the owner
        // 2026-11-19), and as the rules have it (botPhysQ: catching, launching, holding, the shield and the knuckle guard,
        // its own tryouts: as before, the guard's turn through a throw too; the two are the same but in a throw; TASKS.md
        // E41, native a812558)
        public Quat[] botHandQ = { Quat.Identity, Quat.Identity }, botPhysQ = { Quat.Identity, Quat.Identity };
        public bool botHandSet;
        // the balls it watches: [0] the opponent's, [1] its own
        public BotRead[] botRead = { new BotRead(), new BotRead() };
        public int botThrow;                   // 0 holding, 1 winding up, 2 swinging
        public bool botSlap;                   // the swing is a slap shot (it goes on whatever comes at it)
        public int botIntent;                  // BotSkill.hunts: this throw is for a strike (0) or at the player (1)
        public bool botPounce;                 // BOT STYLES, ADVANCED's duellist: throwing now, at an opponent without a shield
        public int botStrikeSide = 1;          // ... the side of the ring the next strike goes (it alternates)
        public int botStyle;                   // ... this throw's shape (straight, curved, off a wall, curved off a wall, off the floor)
        public bool botPre;                    // ... its slap's shape being planned while its ball comes back to it
        public Vec3 botOppSpot;                // ... where its opponent usually stands (a slow average of their head)
        public bool botOppSpotSet;
        public float botThrowT;
        public Vec3 botShotDir, botShotSpin;   // the throw it will make, and how good it looked (planned shots)
        public float botShotScore = -1e9f;
        public int botShotsTried;
        public int botTarget = -1;             // four players: whom it throws at next (-1: to choose)
        // TRAINING's coach (a bot): it throws only when told (fire), exactly the throw it is given, from `from` with the
        // hand it holds the ball in (it takes it with `hand`); watch false: it stands still, defending nothing
        public class Coach
        {
            public bool on, fire, watch = true;
            public int hand = 1, stance = 1;   // (Bot.STANCE_RELAXED)
            public Vec3 from, dir, spin;
            public float speed = 8f;
            public void CopyFrom(Coach o) { on = o.on; fire = o.fire; watch = o.watch; hand = o.hand; stance = o.stance; from = o.from; dir = o.dir; spin = o.spin; speed = o.speed; }
        }
        public Coach coach = new Coach();

        // native: s = PlayerState{}
        public void Reset()
        {
            @in.CopyFrom(new PlayerInput());
            hands[0].Reset();
            hands[1].Reset();
            ball.Reset();
            score = roundWins = strikesAgainst = strikesFor = 0;
            shotClock = 0;
            health = 0; fireSeen = fireAct = 0; botDodge = default; botDodgeUntil = -1;
            isBot = false;
            ready = false;
            botLevel = -1; botStance = -1; botBow = 0; botBumpHand = 1; botSteady = 0;
            botTimer = 0;
            botBody = botBodyVel = default;
            botHand[0] = botHand[1] = botHandVel[0] = botHandVel[1] = default;
            botHandQ[0] = botHandQ[1] = botPhysQ[0] = botPhysQ[1] = Quat.Identity;
            botHandSet = false;
            botRead[0].Reset(); botRead[1].Reset();
            botThrow = 0; botSlap = false; botIntent = 0; botPounce = false; botStrikeSide = 1; botStyle = 0; botPre = false;
            botOppSpot = default; botOppSpotSet = false; botThrowT = 0;
            botShotDir = botShotSpin = default; botShotScore = -1e9f; botShotsTried = 0;
            botTarget = -1;
            coach.CopyFrom(new Coach());
        }

        // native: a copy of the player (the bot's tryouts copy the whole game). The reads' paths are not copied (native
        // BallPath's copy is empty)
        public void CopyFrom(PlayerState o)
        {
            @in.CopyFrom(o.@in);
            hands[0].CopyFrom(o.hands[0]); hands[1].CopyFrom(o.hands[1]);
            ball.CopyFrom(o.ball);
            score = o.score; roundWins = o.roundWins; strikesAgainst = o.strikesAgainst; strikesFor = o.strikesFor; shotClock = o.shotClock;
            health = o.health; fireSeen = o.fireSeen; fireAct = o.fireAct; botDodge = o.botDodge; botDodgeUntil = o.botDodgeUntil;
            isBot = o.isBot; ready = o.ready;
            botLevel = o.botLevel; botStance = o.botStance; botBow = o.botBow; botBumpHand = o.botBumpHand; botSteady = o.botSteady;
            botTimer = o.botTimer; botBody = o.botBody; botBodyVel = o.botBodyVel;
            for (int h = 0; h < 2; ++h) { botHand[h] = o.botHand[h]; botHandVel[h] = o.botHandVel[h]; botHandQ[h] = o.botHandQ[h]; botPhysQ[h] = o.botPhysQ[h]; }
            botHandSet = o.botHandSet;
            botRead[0].CopyFrom(o.botRead[0]); botRead[1].CopyFrom(o.botRead[1]);
            botThrow = o.botThrow; botSlap = o.botSlap; botIntent = o.botIntent; botPounce = o.botPounce; botStrikeSide = o.botStrikeSide;
            botStyle = o.botStyle; botPre = o.botPre; botOppSpot = o.botOppSpot; botOppSpotSet = o.botOppSpotSet;
            botThrowT = o.botThrowT; botShotDir = o.botShotDir; botShotSpin = o.botShotSpin; botShotScore = o.botShotScore;
            botShotsTried = o.botShotsTried; botTarget = o.botTarget; coach.CopyFrom(o.coach);
        }
    }

    // one point of a ball's expected flight (Game.PredictBall): the step (its game time) whose move brings the ball here
    // (the first point: the ball as it is), where, how fast; strike: it crossed the reader's strike zone ring on the way
    // here; the whole ball then (a tryout starts from it)
    public struct BallPathPt
    {
        public float t;
        public Vec3 pos, vel;
        public bool strike;
        public Ball ball;
        public BallPathPt(float t, Vec3 pos, Vec3 vel, bool strike, Ball ball) { this.t = t; this.pos = pos; this.vel = vel; this.strike = strike; this.ball = ball; }
    }

    // the bot's read of a flight: the path (Game.PredictBall, or a straight line), when it noticed, whether it acts, and
    // its answer (Bot.NONE ...)
    public class BotRead
    {
        public int mode;                    // 0 nothing to watch, 1 a ball coming at it (an attack), 2 one it threw or deflected coming back
        public List<BallPathPt> path = new List<BallPathPt>();   // (not copied with the game: the tryouts don't need it)
        public float noticeT, readT;
        public bool act, catchIt;           // it does something about it (block, punch, dodge); it goes for the catch
        public Vec3 err;                    // its misread (lower levels)
        public bool full;                   // the read reaches its whole horizon (the ball still flying at its end)
        public int tuck;                    // a dodge with this hand (its shield up) pulled in to its side, out of the ball's way
        public bool again;                  // its plan ran out of tryouts this step: it goes on with it the next
        public int hands;                   // its shields up when it planned (bits by hand): a change, a new plan
        public List<KeyValuePair<uint, sbyte>> tried = new List<KeyValuePair<uint, sbyte>>();   // this read's tryouts so far
        public Vec3[] rel = new Vec3[2];    // where its hands hold still (from its head) while it watches the ball (as planned)
        public Quat[] relQ = { Quat.Identity, Quat.Identity };
        public int owner;                   // whose ball it is
        public int plan, hand;
        public float hitT;                  // when the ball would reach its body (or, missing it, its own hands where they are)
        public Vec3 grip, body;             // the grip (block, catch) it moves to; its body offset (where it stands, or its dodge)
        public Quat gripQ;

        public BotRead() => Reset();

        public void Reset()   // native: r = PlayerState::BotRead{}
        {
            mode = 0; path.Clear(); noticeT = readT = -1; act = catchIt = false; err = default; full = false; tuck = -1;
            again = false; hands = -1; tried.Clear(); rel[0] = rel[1] = default; relQ[0] = relQ[1] = Quat.Identity;
            owner = -1; plan = 0; hand = -1; hitT = -1; grip = body = default; gripQ = Quat.Identity;
        }

        public void CopyFrom(BotRead o)   // (native: the path's copy is empty)
        {
            mode = o.mode; path.Clear(); noticeT = o.noticeT; readT = o.readT; act = o.act; catchIt = o.catchIt; err = o.err;
            full = o.full; tuck = o.tuck; again = o.again; hands = o.hands;
            tried.Clear(); tried.AddRange(o.tried);
            rel[0] = o.rel[0]; rel[1] = o.rel[1]; relQ[0] = o.relQ[0]; relQ[1] = o.relQ[1];
            owner = o.owner; plan = o.plan; hand = o.hand; hitT = o.hitT; grip = o.grip; body = o.body; gripQ = o.gripQ;
        }

    }
}
