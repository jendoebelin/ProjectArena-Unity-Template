// Every tunable of the native simulation (app/src/game.h struct Tunables), ported 1:1 with its provenance tag:
// value/rule read from the original files (cooked assets, exe, or the game's own
//                        English text)
//           behaviour/value chosen from indirect evidence; not yet confirmed
//               not present in the original; added for the native port (e.g. the practice bot)
namespace ProjectArena.Sim
{
    public class Tunables
    {
        // ---- rule set. Basic and Advanced, the original's two.
        //   Basic:    3 minute match, sudden death overtime on a tie, permanent knuckle guard deflectors,
        //             shield breaks on hit, recharge by throwing, aim assist medium
        //   Advanced: best of 3 rounds, each round first to 4 points, no time limit, shield breaks on hit,
        //             recharge by throwing, aim assist low
        public bool advanced = false;
        public bool experimental = false;   // the rules ask for an EXPERIMENTAL arena (Basic's rules there); Game.UseArena loads it
        public int expRoom = 0;             // which: Room.* (0 DIAMOND, 1 CORNERS, 2 FIREBALL BLITZ)
        public bool solidPlatforms = false; // DIAMOND: the ball bounces off the platforms (default: through them, owner 2026-10-29)
        public float basicMatchTime = 180.0f;
        public int advancedPointsToWin = 4, advancedRoundsToWin = 2;
        // ---- MATCH OPTIONS (the host's, 0.3.8; owner's design 2026-10-21). shields false = HOTDOG MODE (the community's
        // name for playing without shields). knuckleMode -1 = as the rules say (Basic on, Advanced off), 0 off, 1 on.
        // basicMatchTime 0 = FREE PLAY: no clock and no winner, play on. advancedRoundTime 0 = no limit; otherwise each
        // round ends at time: more points wins the round, a tie goes to sudden death (the next point wins it).
        public bool shields = true;
        public int knuckleMode = -1;
        public float advancedRoundTime = 0.0f;
        public bool KnuckleGuards() => knuckleMode < 0 ? !advanced : knuckleMode != 0;
        public bool FreePlay() => !advanced && basicMatchTime <= 0f;
        public bool Timed() => advanced ? advancedRoundTime > 0f : basicMatchTime > 0f;
        // statistics are kept by mode: 0 standard (the rules as they come), 1 HOTDOG (no shields, any other options),
        // 2 custom (other options)
        public int StatsMode()
        {
            if (!shields) return 1;
            // (knuckle guards count by what is in play: "off" chosen in Basic and then Advanced picked is Advanced's own rule)
            bool std_ = KnuckleGuards() == !advanced && (advanced ? advancedRoundTime <= 0f : basicMatchTime == 180.0f) &&
                        PerStrike() == speedIncreasePerStrike &&   // (STRIKE SPEED other than REGULATION: custom)
                        FoulMove() == 0f &&                        // (and the FOUL LINE moved)
                        !MaidenNow() && AimAssistNow() == (advanced ? 1 : 2) &&   // (ROOMS AND THEIR OPTIONS)
                        StrikeZoneNow() == 1 && BallGrowthNow() == 3 && (!advanced || RoundsToWinNow() == 2) &&
                        ShieldSizeNow() == 0 && BouncesStandard();   // (SHIELD SIZE; BOUNCES, 0.3.22)
            return std_ ? 0 : 2;
        }

        // ---- arena / layout
        public Vec3[] start = { new Vec3(-7.0f, 0, 0), new Vec3(7.0f, 0, 0),             // the two players' starts (-7 / +7 m);
                                new Vec3(0, 0, -7.0f), new Vec3(0, 0, 7.0f) };           // 2, 3: FOUR CORNERS' other two corners
        public Vec3[] spawner = { new Vec3(-6.48f, 0, 0), new Vec3(6.48f, 0.02f, 0),     // the two players' ball spawners
                                  new Vec3(0, 0, -6.48f), new Vec3(0, 0, 6.48f) };
        // FOUR CORNERS (native 0.3.12): four players (2 otherwise), their teams (Teams.*), and 3 v 1's lone player: their
        // hits count onePoints and their shield never breaks. oneTurns: 3 v 1 online, THE ONE takes turns each match (true)
        // or is the host (owner 2026-10-30); practice: you
        public int players = 2;
        public int teamMode = Teams.FFA;
        public int theOne = 0;
        public bool oneTurns = false;
        public int onePoints = 2;
        // FIREBALL BLITZ (native 0.3.13, owner 2026-10-30): every throw a fireball, hopping along the floor (fireGravity
        // down; each floor bounce back up to fireHop, its speed along the floor kept, at least fireMinSpeed); it fizzles out
        // at its fireBounces-th bounce (floor, walls, ceiling) or after fireLife s. A hit takes 1 of your `health` (1, 3, 5
        // or 10); at 0 you are out, and ringWarn s later the room closes in on those left; the last one standing wins (at
        // time: the most health, then sudden death at 1 each). fireBalls: how many of your fireballs can be in the room at
        // once (1, 2, 3; 4 = RAPID FIRE: 3, and the tee refills at once). octPlayers: practice, how many play (2 to 4: you
        // and bots)
        public int health = 5, fireBalls = 3, octPlayers = 4;   // (BALLS: THREE by default: owner, 2026-11-02)
        public float fireGravity = 12f, fireHop = 1.0f, fireMinSpeed = 5f, fireLife = 5f, ringWarn = 2f;   // (hop: to the waist)
        public int fireBounces = 4, rapidLimit = 3;
        // (only hits from the hips up count: owner, 2026-11-02, "I don't want feet getting hit to count", "include the
        // butt": FIREBALL BLITZ's body goes on below the torso, round the hips and seat, as far as hipBottom below the head)
        public float hipBottom = 1.0f, hipRadius = 0.17f;
        public float fireGuardPad = 0.05f;  // ... and a knuckle guard or shield meets a fireball this much sooner (they come in low)
        public float foulLineX = 4.73f;                                   // FoulLine_Player0/1 at x = -/+473 cm
        // a ball outside this box has left the arena (respawns): below outLow, above outHigh, |x| > outX, |z| > outZ
        // (Game.UseArena sets them for each arena)
        public float outLow = -1f, outHigh = 6f, outX = 11f, outZ = 4f;
        public float spawnerIdleHeight = 1.15f;                           // where the ball waits on its spawner: 115 cm up
        public float springK = 25.0f, springDamping = 16.0f;              // the spring that holds it there: stiffness / damping
        public float respawnDelay = 0.1f, postSpawnDelay = 0.1f;          // the spawner's delays before and after a respawn

        // ---- throw
        public float throwMinHandSpeed = 0.50f;          // the slowest hand that throws: 50 cm/s
        public float throwVelocityWindow = 0.15f;        // the hand's speed is averaged over this window
        // Launch speed
        //   t = clamp((hand - minInputSpeed) / (maxInputSpeed - minInputSpeed), 0, 1)
        //   speed = lerp(minProjectileThrownSpeed, maxProjectileThrownSpeed + strikes * SpeedIncreasePerStrike, t)
        public float minInputSpeed = 0.0f, maxInputSpeed = 6.50f;        // 0 / 650 cm/s (without the original's tuning: 150 / 800)
        public float projectileMinSpeed = 0.0f, projectileMaxSpeed = 14.0f;
        public float speedIncreasePerStrike = 0.5f;      // 50 cm/s faster a strike
        // STRIKE SPEED (native 0.3.19, a host's MATCH OPTION; regulation stays the default): 0 REGULATION (the
        // original's 0.5 m/s a strike), 1 FAST (1.0), 2 FASTER (1.5). Two-player rooms (classic, DIAMOND) only
        public int strikeSpeed = 0;
        // FAIR MATCHES (native 0.3.20, owner 2026-11-15: "if your opponent is playing with a smaller foul line than you
        // can have or see then we have broken a fairness rule"): a rule-changing option plays only when every player's
        // game has it. Each records the first version with it (0.3.19 = 3019); the host sets matchVersion to the oldest
        // game among the match's players (games before 0.3.20 don't say theirs: 0) and an option newer than that plays
        // at regulation for the match. The host's choice is kept for the next match. A new option that changes play gets
        // its version here and goes through a *Now() (and Online.Net.GameVersion rises only once all of a version's are in)
        public const int kVerStrikeSpeed = 3019, kVerFoulLine = 3020, kVerReach = 3021, kVerKnuckle = 3021, kVerRoomOptions = 3021,
                         kVerAll = 1 << 30;   // (native cb0970f: the reach, the knuckles and the room options from 0.3.21)
        public int matchVersion = kVerAll;   // (offline, and every guest: all; guests get the host's rules as they play)
        public int StrikeSpeedNow() => matchVersion >= kVerStrikeSpeed ? System.Math.Max(0, System.Math.Min(2, strikeSpeed)) : 0;
        public int FoulShiftNow() => matchVersion >= kVerFoulLine ? System.Math.Max(-2, System.Math.Min(2, foulShift)) : 0;
        public bool FistReachNow() => matchVersion >= kVerReach;   // (THE REACH ROUND THE FIST, below)
        // THE REACH ROUND THE FIST (native 2ee3617; two players' report, 2026-11-17: picking a ball up felt too small, "hard
        // to tell where the hitbox is"): a hand takes a ball within catchHalfHeight + the ball's radius of its catcher, 22 cm
        // in front of the controller, so a ball beside or just behind the fist was out of reach; a second reach as big round
        // the fist (fistReachOffset, grip space) takes those too. Catches are judged by the host, so it plays when every
        // player's game has it (FAIR MATCHES: kVerReach)
        public Vec3 fistReachOffset = new Vec3(0.0f, 0.0f, -0.04f);
        // FOUL LINE (native 0.3.20, a host's MATCH OPTION; owner 2026-11-15): the line moved in steps of 0.5 m, +1 / +2
        // LONGER (toward the centre: a longer way to throw to clear it), -1 / -2 SHORTER; the foul zone moves with it. The
        // classic room only (Basic, Advanced): not the four-player rooms, nor DIAMOND (its foul zone is the platform)
        public int foulShift = 0;
        public float FoulMove() => experimental ? 0f : 0.5f * FoulShiftNow();
        public float PerStrike()
        {
            bool twoPlayer = !experimental || expRoom == Room.Diamond;
            return speedIncreasePerStrike * (1 + (twoPlayer ? StrikeSpeedNow() : 0));
        }
        // ROOMS AND THEIR OPTIONS (TASKS.md E22, native d4cf079 and cb0970f, 0.3.21; the owner's design, 2026-11-17, and
        // players' reports that day: the ball grows too big too fast, maybe more aim assist): more of the host's options for
        // a room, in the two-player rooms (the SQUARE, DIAMOND) for now; each through FAIR MATCHES (an older player in the
        // match: as the rules say). DIAMOND plays either match too (the owner, 11-17: the SQUARE's options): a timed match or
        // rounds, as any version can.
        // SHIELDS: ALWAYS ON (MAIDEN, the community's name; the owner: it never breaks): with shields on, your shield is up
        // whenever you hold your ball, charged or not, and a block never breaks it
        public bool maiden = false;
        // AIM ASSIST (the owner: four steps): -1 as the rules say (Basic MEDIUM, Advanced LOW), 0 OFF, 1 LOW
        // (aimCorrectAdvancedDeg, 1.5 deg), 2 MEDIUM (aimCorrectBasicDeg, 3), 3 HIGH (half as much again, 4.5)
        public int aimAssist = -1;
        // STRIKE ZONE: 0 SMALL (a ring of 50 cm radius), 1 REGULATION (strikeZoneRadius, 70 cm), 2 LARGE (90 cm)
        public int strikeZone = 1;
        // BALL GROWTH with strikes (the owner: none, quarter, half, regulation): that share of the regulation growth
        public int ballGrowth = 3;
        public bool RoomOptionsNow() => matchVersion >= kVerRoomOptions && (!experimental || expRoom == Room.Diamond);
        public bool MaidenNow() => shields && maiden && RoomOptionsNow();
        public int AimAssistNow() => RoomOptionsNow() && aimAssist >= 0 ? System.Math.Min(3, aimAssist) : (advanced ? 1 : 2);
        public int StrikeZoneNow() => RoomOptionsNow() ? System.Math.Max(0, System.Math.Min(2, strikeZone)) : 1;
        public int BallGrowthNow() => RoomOptionsNow() ? System.Math.Max(0, System.Math.Min(3, ballGrowth)) : 3;
        public float AimAssistDeg()   // how far a throw may be turned toward its target
        {
            int s = AimAssistNow();
            return s == 0 ? 0f : s == 1 ? aimCorrectAdvancedDeg : s == 2 ? aimCorrectBasicDeg : 1.5f * aimCorrectBasicDeg;
        }
        public float StrikeRadius() => strikeZoneRadius + 0.2f * (float)(StrikeZoneNow() - 1);
        static readonly float[] kGrowthShare = { 0f, 0.25f, 0.5f, 1f };
        public float GrowthShare() => kGrowthShare[BallGrowthNow()];
        // ROUNDS (the owner, 2026-11-17: how many rounds; timed rounds too, advancedRoundTime): a match of rounds is BEST
        // OF 1, 3, 5, 7 or 9 (advancedRoundsToWin 1 to 5 rounds won; regulation BEST OF 3; the owner: a most we allow)
        public int RoundsToWinNow() => RoomOptionsNow() ? System.Math.Max(1, System.Math.Min(5, advancedRoundsToWin)) : 2;
        // SHIELD SIZE (TASKS.md E25, native 7ed11aa; the owner, 2026-11-17: the shield felt small in the hand): 0 REGULATION,
        // 1 +25%, 2 +50%, 3 +75%, 4 DOUBLE, its width and height (the plate drawn with it; SHIELDS: OFF is the way to none)
        public int shieldSize = 0;
        // BALL FEEL (TASKS.md E29, native 3129dc2; the owner, 2026-11-17, from the BALL FEEL (TEST) switches: a room option,
        // the host's, and its pick the default): how the ball looks and feels, BALL_* bits: CORE SOLID (a lit core inside the
        // plasma), GLOW TRUE SIZE (its outside where it hits; otherwise drawn 25% wider), SPIN VISIBLE (it turns with its
        // real spin as it flies), WEIGHT HEAVY (a held ball trails the hand a little; the controller hums as you swing it).
        // Drawing and haptics only: no rule (nothing in play, no FAIR MATCHES version, not custom in the stats); a guest of
        // an older host keeps its own
        public const int BALL_CORE = 1, BALL_TRUE = 2, BALL_SPIN = 4, BALL_WEIGHT = 8, BALL_FEEL_ALL = 15,
                         BALL_FEEL_DEFAULT = BALL_TRUE | BALL_SPIN | BALL_WEIGHT;   // (the owner's pick: glow core, the rest on)
        public int ballFeel = BALL_FEEL_DEFAULT;
        // BOUNCES (TASKS.md E32, native 5d3bbec, 0.3.22; the ideas of ProjectArena-public pull request #26, as the host's
        // options: the owner, 2026-11-18): how the ball comes off the court, in the two-player rooms (the SQUARE,
        // DIAMOND), each through FAIR MATCHES. WALLS: LIVELY: a side wall cuts only the ball's speed into it, to
        // sideWallElasticity (STANDARD: the whole speed to elasticity, 90%). WALL GRIP: the floor, ceiling and side walls
        // grip the ball as a solid sphere (friction bankFriction) and the knuckle guards where they meet it
        // (knuckleFriction): a bounce trades its sideways motion and spin, and the spin curves it after. BANK ASSIST (OFF,
        // LOW, MEDIUM, HIGH): after a bounce, a leg about to just miss the opponent is turned toward them, BankAssistDeg()
        // at most, bankTurnRate degrees a second, once (the players' own throws; AIM ASSIST still turns the throw itself)
        public bool wallsLively = false, wallGrip = false;
        public int bankAssist = 0;
        public const int kVerBounces = 3022;
        public bool BouncesNow() => RoomOptionsNow() && matchVersion >= kVerBounces;
        // THE BALL IN THE LEFT PALM (TASKS.md E43, native c2e64a0; DOSperado, 2026-11-19: in the right hand the ball sits
        // "in my palm" under the shield, in the left "more like" a ring): holdOffset is the right hand's point, 8 cm out of
        // the palm along grip -X; the left palm faces +X, so the left hand held it 8 cm off the back of the hand. Mirrored
        // for the left hand: where a held ball sits and where its throw starts, so it plays when everyone's game has it
        // (FAIR MATCHES); a guest plays it as its host says (palmHold, in the snapshots; older hosts: not)
        public const int kVerPalm = 3022;
        public bool palmHold = true;
        public bool PalmHoldNow() => palmHold && matchVersion >= kVerPalm;
        // FIREBALL BLITZ in teams (TASKS.md E51, native 61589a0, 0.3.24; the owner, 2026-11-20: "I wanted to select 3
        // against 1 where I was the 1"): the room's TEAMS, as FOUR CORNERS has them (the same setting). A teammate's
        // fireball passes through you as your own does (no friendly fire), 3 v 1's THE ONE starts with three times the
        // HEALTH, and a team wins when the other team is all out (at time: the most health left between them). FAIR
        // MATCHES: with a player before 0.3.24 in it, the room is everyone for themselves. A guest learns it from the host's
        // snapshot (blitzTeams; an older host sends none)
        public const int kVerBlitzTeams = 3024;
        public bool blitzTeams = true;
        public bool BlitzTeamsNow() => blitzTeams && matchVersion >= kVerBlitzTeams;
        public bool WallsLivelyNow() => BouncesNow() && wallsLively;
        public bool WallGripNow() => BouncesNow() && wallGrip;
        public int BankAssistNow() => BouncesNow() ? System.Math.Max(0, System.Math.Min(3, bankAssist)) : 0;
        static readonly float[] kBankDeg = { 0f, 0.8f, 1.6f, 2.4f };
        public float BankAssistDeg() => kBankDeg[BankAssistNow()];
        public bool BouncesStandard() => !WallsLivelyNow() && !WallGripNow() && BankAssistNow() == 0;
        public float sideWallElasticity = 0.97f, bankFriction = 0.10f, knuckleFriction = 0.14f, bankTurnRate = 24f;
        public int ShieldSizeNow() => RoomOptionsNow() ? System.Math.Max(0, System.Math.Min(4, shieldSize)) : 0;
        public float ShieldScale() => 1f + 0.25f * (float)ShieldSizeNow();
        public Vec3 ShieldHalfNow() { float s = ShieldScale(); return new Vec3(shieldHalfExtents.x * s, shieldHalfExtents.y, shieldHalfExtents.z * s); }
        public float scaleIncreasePerStrike = 0.25f;     // the ball grows with each strike
        public int maxStrikes = 42;                      // the most strikes
        // Ball size per strike: the recovered +0.25 per strike for the first 4, then +0.05 per strike up to
        // maxBallScale (PORT: 0.25 x 42 would be an 11x ball). The ball grows smoothly into each new step.
        public int fullScaleSteps = 4;
        public float lateScaleIncreasePerStrike = 0.05f, maxBallScale = 3.0f, scaleGrowRate = 6.0f;
        // Your own ball can hit you once it has travelled away and is coming back (PORT, owner request); the point goes
        // to the opponent. selfHitGrace: seconds after a throw / return start / block before it can hit its owner.
        public bool selfHitScores = true;
        public float selfHitGrace = 0.35f;
        public int maxBounces = 16;                      // the most bounces
        // aim assist: Basic's tuning ("medium"), Advanced's ("low")
        public float aimCorrectBasicDeg = 3.0f, aimCorrectAdvancedDeg = 1.5f;   // the most a throw's aim is corrected, in its arc's plane

        // ---- projectile flight
        public float elasticity = 0.9f;                  // how much speed a bounce keeps
        public bool bouncesChangeSpeed = true;           // bounces can change the speed limit
        public float minSpeed = 1.0f;                    // the slowest a flying ball goes: 100 cm/s
        public float projectileRadius = 0.10f;           // the ball's collision sphere: 10 cm
        // Spin / Magnus. The effect, its settings (the Magnus factor, spin on a bounce, the spin's decay a second) and the
        // top spin (30) are known.
        // The integrator's exact formula/default coefficients are not yet recovered -> coefficients.
        public float maxSpin = 30.0f;                    // the top spin, taken as rad/s
        public float magnusFactor = 0.016f;              // a = magnusFactor * (spin x v)
        public float spinWindow = 0.12f;                 // peak controller angular velocity over the last 0.12 s feeds spin
        public float spinDecayPerSecond = 0.35f;         // RotationalDecayPerSecond
        public float spinBounceFactor = 0.25f;           // SpinBounceFactor
        // Return
        // hitting the OPPONENT's back wall sends the ball back on a straight line aimed at the owner's position at
        // that moment (no further seeking) at ReturnSpeed. It can be caught, can hit the owner (point to the opponent, selfHitScores), or fly past into the owner's back
        // wall, where it dissolves and respawns rising from the floor spawner.
        public float returnSpeed = 10.0f;
        public float foulReturnSpeed = 16.0f;   // a fouled ball: to the wall and home again (owner, 2026-10-21: faster)
        // Short throws (owner request): a throw that has not crossed the owner's foul line and is slow
        // (below recallSpeed), heading back toward the owner, or still short after recallTime goes back to the spawner
        // (the idle spring pulls it home) instead of rattling round the owner's end.
        public float recallSpeed = 2.0f, recallTime = 2.5f;
        // Stuck balls (native ac8dfff, 2026-10-28; kept narrow, so normal play doesn't change): a flying ball that goes
        // less than stuckDistance along the court in stuckTime (looping in a corner, between floor and ceiling), or that
        // its bounces have slowed under recallSpeed anywhere (after slowGrace, so a soft block still drops nearby), goes
        // home, harmless, as a short throw does. A fouled ball keeps its own speed (at least foulMinSpeed) to its wall.
        public float stuckDistance = 1.0f, stuckTime = 2.0f, slowGrace = 1.0f, foulMinSpeed = 5.0f;
        // Respawn the spawner's rings play (~1.5 s), then the
        // ball appears at the launcher point (15 cm) and the spring lifts it to its waiting height (115 cm).
        // PORT tuning (owner): shorter hidden phase, slower and smoother rise with its own tracers + air-pressure sound.
        public float spawnPreTime = 0.7f, spawnRiseTime = 0.9f, spawnLauncherHeight = 0.15f;
        // While hidden, the ball sits under the floor at y = -0.6 .. -0.3 (pre-spawn progress), so clients can
        // animate the spawner from the replicated position alone.
        public float spawnDepth = 0.30f;

        // ---- hands
        // Offsets in OpenXR grip-local space (+X right, +Y up, -Z forward); UE (x,y,z) cm -> grip (y, z, -x)/100.
        public float catchRadius = 0.125f, catchHalfHeight = 0.25f;   // the catching capsule: 12.5 / 25 cm
        public Vec3 catcherOffset = new Vec3(-0.01f, 0.0f, -0.22f);           // the catching point (22, -1, 0) cm
        public Vec3 launcherOffset = new Vec3(-0.02f, 0.02f, -0.25f);         // the launcher point (25, -2, 2) cm (throw physics)
        public Vec3 holdOffset = new Vec3(-0.079f, 0.011f, -0.010f);          // where a held ball sits: in the fist (owner's TUNING export,
                                                                              // 2026-10-19; was 15 cm out, before that the launcher point)
        public Vec3 shieldOffset = new Vec3(0.0f, 0.10f, -0.20f);             // the shield (20, 0, 10) cm, roll 180
        public Vec3 shieldHalfExtents = new Vec3(0.20f, 0.02f, 0.32f);        // the shield's collision box (32, 20, 2) cm
        // Knuckle bumper = box fitted to the knuckle guard's glass as placed on the hand, (10.5, 0, 0) + (-9, 0, 0) cm, its
        // right-hand grip-space centre / half extents below
        // (x is mirrored for the left hand). The face normal is grip -Z (the front of the fist). The first centre was
        // (-0.022, 0.010, -0.137); the owner moved the glass onto the fist with the TUNING tool (2026-10-18), and the
        // bumper goes with it (what you see is what hits). KNUCKLES ON THE FIST (native 6def918; two players' report,
        // 2026-11-17: punches felt like "the slider in pong, not like I physically punched the ball"): the gauntlets' fists,
        // drawn in line with the cuff since 11-16, have their front 3.4 cm out from the grip, and the bumper sat in front of
        // them (2.8 to 8.6 cm out: a ball turned some 5 cm before it reached the knuckles); now its front face is at the
        // knuckles, the same box, for every gauntlet look (the knuckle glass with it). Older games keep the old place (FAIR
        // MATCHES: kVerKnuckle; the host judges blocks)
        public Vec3 knuckleOffset = new Vec3(0.0f, -0.010f, -0.005f);
        public Vec3 knuckleOffsetOld = new Vec3(0.0f, -0.026f, -0.057f);
        public Vec3 KnuckleOffsetNow() => matchVersion >= kVerKnuckle ? knuckleOffset : knuckleOffsetOld;
        public Vec3 knuckleHalf = new Vec3(0.064f, 0.048f, 0.029f);
        public float knuckleRadius = 0.07f;                           // used for glow rendering only
        // Deflections treat the guard / shield as a moving surface: v_out = reflect(v_in - v_hand) * restitution + v_hand,
        // so a still fist returns the ball and a punch adds the fist's speed
        public float deflectRestitution = 0.9f;
        public float handVelWindow = 0.045f;                          // short window for punch velocity
        public float shieldAimAssistBasicDeg = 8.0f, shieldAimAssistAdvancedDeg = 2.0f;   // the shield's aim assist (degrees)
        public float knuckleAimAssistDeg = 8.0f;                      // the knuckle guards' aim assist (degrees, Basic's)
        // strike zone a 70 cm ring 0.6 m behind the player, 1.28 m up, facing the court.
        public float strikeZoneRadius = 0.70f, strikeZoneBack = 0.60f, strikeZoneHeight = 1.28f;
        // shot clock runs while your ball is in your end, resets when you throw past your foul line;
        // once expired the opponent gets an automatic strike each time they throw successfully. Duration.
        public bool useShotClock = true;                 // the shot clock: on by default
        // READY UP (native 0.3.14, owner 2026-11-03: the bots are always ready, so a room for four started on its own around
        // people still finding their place): every person in the tank presses A first. The host sets it in rooms for four.
        public bool readyUp = false;
        public float shotClockSeconds = 10.0f;
        // shield placement on the hand (cycled live for on-device tuning; 0 = the original's shield placement)
        public int shieldPlacement = 1;   // user report on Quest 3: placement 0 (above) is on the wrong side of the gauntlet
        // placement 1 (the default): on the back of the hand, right-hand grip space (left mirrored); pitch / yaw / roll in
        // degrees (gripEuler). The owner's TUNING export (2026-10-18; was (0.07, 0, 0.01), roll -90 only). Live in
        // developer mode: SETTINGS > TUNING.
        public Vec3 shieldBackPos = new Vec3(0.076f, -0.024f, -0.122f), shieldBackRot = new Vec3(-20.0f, 19.1f, -90.6f);   // owner's export 2026-10-19

        // ---- match flow
        public float countdown = 3.0f;
        // Match start between two people (owner, 2026-10-16): both join in the centre of the court; a fist
        // bump with the triggers pulled sends them to their ends at once, or the countdown does it after meetTime.
        // 0 = no meeting (the practice bot never meets).
        public float meetTime = 10.0f;
        public float meetSpot = 0.45f;          // each player stands this far from the centre line (heads ~0.9 m apart)
        // a fist = the segment from the back of the hand to the knuckles you see (grip frame; the visible fist front is 6-7
        // cm in front of the grip). Two closed fists whose segments come closer than fistBumpGap = a bump. (Measuring the
        // grips, then the knuckle glass's point 13.7 cm out, both missed real bumps on 2026-10-17.)
        public Vec3 fistBack = new Vec3(0.0f, 0.0f, 0.03f), fistFront = new Vec3(0.0f, 0.0f, -0.065f);
        public float fistBumpGap = 0.11f;
        // The meeting point (native e0cc7f7, owner 2026-10-28): a ring for each player over the centre line, at chest
        // height between the two. A fist closed inside its ring lights it (offered). A bump counts only between two fists
        // in their rings, one of them offered for meetOfferTime first: one person offers, the other meets it.
        public float meetRingRadius = 0.22f, meetRingDepth = 0.16f, meetRingX = 0.07f, meetOfferTime = 0.2f;
        public float bumpPause = 0.8f;          // after the bump: the confirmation (buzz, sound, FIST BUMP!) before the move
        public float roundSummaryTime = 4.0f;

        // ---- body hit volumes; torso offset below the head
        public float headRadius = 0.12f, torsoRadius = 0.165f, torsoTop = 0.25f, torsoBottom = 0.85f;

        // ---- practice bot its level (Bot.Skill; practice MATCH OPTIONS), and its body. A person's reach and
        // shoulders, as the avatar draws them: each wrist (grip-local botWrist) stays within botArm of its shoulder (head +
        // botShoulder in the head's frame; x mirrored for the left), so the drawn arm never stretches; its head moves within
        // botBodyRange (sideways, down, toward the court) by stepping, leaning and ducking.
        public int botLevel = 2;
        // MATCH MY LEVEL (owner, 2026-10-26): instead of botLevel, a level between 0 (ROOKIE) and 5 (UNBEATABLE) that
        // follows the player: up botMatchStep each time the person scores against the bot, down each time the bot scores
        public bool botAdapt = false;
        // BOT STYLE (TASKS.md E23, native 50b11ae; the owner, 2026-11-17: a bot option with its level, any match, shields or
        // not; Game.BasicBotStyle): 0 BRAWLER, 1 DUELLIST. In the two-player rooms (the SQUARE, DIAMOND); the four-player rooms'
        // bots play as they did
        public int botPlay = 0;
        public float botMatch = 2f, botMatchStep = 0.15f;
        public bool botMeet = false;   // practice: meet the bot in the centre for a fist bump first (owner, 2026-10-27)
        public Vec3 botShoulder = new Vec3(0.18f, -0.295f, 0.04f), botWrist = new Vec3(0.04f, 0.047f, 0.122f);
        public float botArm = 0.52f, botHandAccel = 40f;   // (0.52: room for the shoulder lifting with a raised arm)
        public Vec3 botBodyRange = new Vec3(1.0f, 0.50f, 0.35f);

        // native: the tunables are copied with the game (the bot's tryouts)
        public Tunables Clone()
        {
            var c = (Tunables)MemberwiseClone();
            c.start = (Vec3[])start.Clone();
            c.spawner = (Vec3[])spawner.Clone();
            return c;
        }

        // native assignment (t = other): every value copied into this one (others keep their reference to it)
        public void CopyFrom(Tunables o)
        {
            foreach (var f in typeof(Tunables).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                f.SetValue(this, f.GetValue(o));
            start = (Vec3[])o.start.Clone();
            spawner = (Vec3[])o.spawner.Clone();
        }
    }
}
