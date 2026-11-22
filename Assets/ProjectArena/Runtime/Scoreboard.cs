using System;
using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The holographic scoreboard over centre court and the match banners (main.cpp drawWorld, clockLine, rulesLine),
    // facing the local player (spectators: above the roof, facing the gallery), with the network's notices, the
    // fist-bump prompts of a two-player start and the FREE PLAY queue notice; and the jumbotron over centre court
    // (drawJumbotron, careerLines): each player's name, flag or team colour, the score, their career statistics and The
    // Crowd's fans and hype.
    public static class Scoreboard
    {
        // the clock as the boards show it: 2:41, FREE PLAY, SUDDEN DEATH (empty: an untimed Advanced round)
        public static string ClockLine(Game g)
        {
            if (g.overtime) return "SUDDEN DEATH";
            if (g.tun.FreePlay()) return "FREE PLAY";
            if (!g.tun.Timed()) return "";
            int secs = (int)MathF.Ceiling(MathF.Max(0f, g.roundClock));
            return $"{secs / 60}:{secs % 60:00}";
        }

        public static string RulesLine(Tunables t)
        {
            // (native 0.3.12: an EXPERIMENTAL room says which; FOUR CORNERS its teams; FIREBALL BLITZ its health and balls)
            string[] teams = { "FREE-FOR-ALL", "2 V 2", "3 V 1" };
            // (ROOMS AND THEIR OPTIONS, native d4cf079: the room and its match, SQUARE - BEST OF 3, then what differs)
            string s = !t.experimental ? Menu.RoomName(t)
                     : t.expRoom == Room.Corners ? "FOUR CORNERS - " + teams[Math.Max(0, Math.Min(2, t.teamMode))]
                     : t.expRoom == Room.Blitz ? "FIREBALL BLITZ - " +   // (0.3.24, TASKS.md E51: its teams)
                       (t.teamMode != Teams.FFA && t.BlitzTeamsNow() ? teams[Math.Min(2, t.teamMode)] + " - " : "") + "HEALTH " + t.health +
                       (t.fireBalls >= 4 ? " - RAPID FIRE" : t.fireBalls > 1 ? " - " + Menu.BallsName[t.fireBalls] + " BALLS" : "")
                     : Menu.RoomName(t);
            if (!t.shields) s += " - HOTDOG";
            if (t.advanced && t.advancedRoundTime > 0) s += " - " + ((int)t.advancedRoundTime / 60) + " MIN ROUNDS";
            if (!t.advanced && t.basicMatchTime <= 0) s += " - FREE PLAY";
            else if (!t.advanced && t.basicMatchTime != 180f) s += " - " + ((int)t.basicMatchTime / 60) + " MIN";
            if (t.knuckleMode >= 0 && (t.knuckleMode != 0) != !t.advanced) s += t.knuckleMode != 0 ? " - KNUCKLE GUARDS" : " - NO KNUCKLE GUARDS";
            if (t.PerStrike() != t.speedIncreasePerStrike) s += t.strikeSpeed >= 2 ? " - FASTER STRIKES" : " - FAST STRIKES";
            if (t.FoulMove() != 0f) { string f = Menu.FoulLabel(t); s += " - " + f.Substring(0, 9) + f.Substring(10); }   // (" - FOUL LINE LONGER 2")
            if (t.MaidenNow()) s += " - MAIDEN";   // (ROOMS AND THEIR OPTIONS)
            if (t.AimAssistNow() != (t.advanced ? 1 : 2)) s += " - AIM ASSIST " + Menu.AimName[t.AimAssistNow()];
            if (t.StrikeZoneNow() != 1) s += " - " + Menu.ZoneName[t.StrikeZoneNow()] + " STRIKE ZONE";
            if (t.BallGrowthNow() != 3) s += " - BALL GROWTH " + Menu.GrowthName[t.BallGrowthNow()];
            if (t.ShieldSizeNow() != 0) s += " - SHIELDS " + Menu.ShieldSizeName[t.ShieldSizeNow()];   // (SHIELD SIZE)
            if (t.WallsLivelyNow()) s += " - LIVELY WALLS";   // (BOUNCES)
            if (t.WallGripNow()) s += " - WALL GRIP";
            if (t.BankAssistNow() > 0) s += " - BANK ASSIST " + Menu.AimName[t.BankAssistNow()];
            return s;
        }

        // the big line under the board (null: none)
        public static string Banner(Game g, int L, bool spectating) => Banner(g, L, spectating, p => p == 1 ? "ORANGE" : "BLUE");

        // watching (the lobby, spectating, waiting outside): BLUE WINS, or the country: UNITED STATES WINS
        public static string Banner(Game g, int L, bool watching, Func<int, string> sideName, string ready = "A")
        {
            switch (g.match)
            {
                case MatchState.WaitingForPlayers: return "WAITING FOR PLAYERS";
                case MatchState.Meet: return $"FIST BUMP TO START   {(int)MathF.Ceiling(g.stateTimer)}";
                case MatchState.Countdown: return $"ROUND {g.round}   {(int)MathF.Ceiling(g.stateTimer)}";
                case MatchState.RoundSummary: return g.lastRoundWinner == L ? "ROUND WON" : "ROUND LOST";
                case MatchState.MatchSummary:
                    if (g.np == 4 && watching)
                    {   // FOUR CORNERS: the colour, or the team
                        string[] four = { "BLUE WINS", "ORANGE WINS", "GREEN WINS", "VIOLET WINS" };
                        return g.matchWinner < 0 ? "A DRAW"
                             : g.TeamsNow() == Teams.FFA ? four[g.matchWinner & 3]
                             : g.TeamsNow() == Teams.ThreeVOne ? (g.matchWinner == 1 ? "THE ONE WINS" : "THE THREE WIN")
                             : (g.matchWinner == 0 ? "BLUE TEAM WINS" : "ORANGE TEAM WINS");
                    }
                    if (watching) return sideName(g.matchWinner == 1 ? 1 : 0) + " WINS";
                    // (by team: two players, the same)
                    // (PRESS: READY's button, A as standard: BUTTONS)
                    return (g.matchWinner == g.Team(L) ? "YOU WIN" : "YOU LOSE") + "  -  PRESS " + ready;
            }
            return null;
        }

        static Vector4 V(float r, float g, float b, float a = 1) => new Vector4(r, g, b, a);

        // a player on team t (for its colour: a team's players share it)
        static int TeamPlayer(Game g, int t) { for (int p = 0; p < g.np; ++p) if (g.Team(p) == t) return p; return 0; }

        // what the board shows besides the game: the network status line, a notice, the meeting's prompts
        public class Info
        {
            public string status = "PRACTICE VS BOT";
            public bool spectating;                 // the board above the roof, facing the gallery seat
            public bool watching;                   // the lobby, spectating, waiting outside: "BLUE WINS"
            public bool waiting;                    // waiting outside the tank: the board above the roof, facing the holding area
            public float leadFlash;                 // FOUR CORNERS: seconds left of the lead change's cue (BLUE LEADS)
            // FIREBALL BLITZ: the last knockout or "THE ROOM CLOSES IN", whose (-1: the room's), and how long it shows yet
            public string octMsg = "";
            public int octMsgP = -1;
            public float octMsgT;
            public string notice;                   // the network's short-lived banner (amber)
            public float bumpFlash;                 // FIST BUMP! (players only)
            public bool player;                     // in the tank (the meeting's prompts)
            public int queued;                      // people queued in our room (FREE PLAY: hand over)
            public Func<int, string> sideName = p => p == 1 ? "ORANGE" : "BLUE";
            public string ready = "A";              // READY's button (BUTTONS: A as standard)
        }

        // L: the local player (0 or 1); status: the network status line (offline: PRACTICE VS BOT)
        // A centre-court announcement (native 116331f drawAnnouncement; a player's report, 2026-11-11: "the between-rounds
        // and match-end text gets lost against the see-through arena"): the text on a dark plate with a thin edge in its
        // colour, the plate faded with it (each character 0.75 of the height wide)
        public static void Announcement(FxRenderer fx, string t, Pose at, float h, Vector4 col)
        {
            float w = 0.75f * h * t.Length + h * 0.9f, ph = h * 1.75f;
            Vec3 back = at.q.Rotate(new Vec3(0, 0, -0.012f));
            fx.Panel(new Pose(at.q, at.p + back), w, ph, new Vector4(0.01f, 0.02f, 0.045f, 0.82f * col.w));
            for (int sd = -1; sd <= 1; sd += 2)
                fx.Panel(new Pose(at.q, at.p + back * 0.5f + at.q.Rotate(new Vec3(0, sd * ph * 0.5f, 0))), w, h * 0.06f,
                         new Vector4(col.x * 0.8f, col.y * 0.8f, col.z * 0.8f, 0.9f * col.w));
            fx.Text(t, at, h, col);
        }

        public static void Draw(FxRenderer fx, Game g, int L, Vector3[] teamCol, string status, bool spectating) =>
            Draw(fx, g, L, teamCol, new Info { status = status, spectating = spectating, watching = spectating }, new Vec3(0, 1.6f, 0));

        public static void Draw(FxRenderer fx, Game g, int L, Vector3[] teamCol, Info info, Vec3 viewer)
        {
            bool spectating = info.spectating;
            string status = info.status;
            Vector4 white = V(1, 1, 1), grey = V(0.55f, 0.6f, 0.68f);
            int rival = g.np == 2 ? 1 - L : -1;   // (four players: a rival's colour for the right-hand edge)
            for (int q = 0; q < g.np && rival < 0; ++q) if (g.Rivals(L, q)) rival = q;
            Vector3 myCol = teamCol[L], opCol = teamCol[rival < 0 ? 0 : rival];
            float sx = L == 0 ? 1f : -1f;
            Quat faceP0 = Quat.AxisAngle(new Vec3(0, 1, 0), M.Deg2Rad(L == 0 ? -90f : 90f));   // quad +Z -> toward local player
            var c0 = new Vec3(-0.5f * sx, 2.95f, 0);
            bool corners = ExpArenaView.CornersRoom(g), oct = g.Octagon;
            Quat Yaw(Vec3 f) => Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(-f.x, -f.z));
            if (g.np == 4) { faceP0 = Yaw(g.Facing(L)); c0 = new Vec3(0, 2.95f, 0) - g.Facing(L) * 0.5f; }   // (FOUR CORNERS: toward your corner)
            if (spectating)
            {   // above the roof, facing the gallery seat (CORNERS: the seat outside the room, by a side wall)
                faceP0 = Quat.Identity; c0 = new Vec3(0, 4.75f, 0.2f);
                if (corners) { faceP0 = Yaw(M.Normalize(new Vec3(1, 0, -1))); c0 = new Vec3(0, 4.75f, 0); }
            }
            else if (info.waiting)
            {   // facing the holding area (native 0.3.12; CORNERS: the waiting deck outside the room's glass)
                faceP0 = Quat.AxisAngle(new Vec3(0, 1, 0), M.kPi); c0 = new Vec3(0, 4.75f, -0.2f);
                if (corners) { faceP0 = Yaw(M.Normalize(new Vec3(1, 0, 1))); c0 = new Vec3(0, 4.75f, 0); }
            }
            Vec3 At(float u, float v, float w = 0f) => c0 + faceP0.Rotate(new Vec3(u, v, w));
            void Txt(string t, float u, float v, float h, Vector4 c) => fx.Text(t, new Pose(faceP0, At(u, v, 0.004f)), h, c);

            fx.Panel(new Pose(faceP0, At(0, 0, -0.01f)), 2.4f, 0.66f, V(0.004f, 0.008f, 0.018f, 0.72f));
            Vector4 edgeL = V(myCol.x, myCol.y, myCol.z, 0.95f), edgeR = V(opCol.x, opCol.y, opCol.z, 0.95f);
            fx.Panel(new Pose(faceP0, At(-1.19f, 0, -0.005f)), 0.025f, 0.66f, edgeL);
            fx.Panel(new Pose(faceP0, At(1.19f, 0, -0.005f)), 0.025f, 0.66f, edgeR);
            Vector4 myC = V(myCol.x * 0.6f + 0.4f, myCol.y * 0.6f + 0.4f, myCol.z * 0.6f + 0.4f);
            Vector4 opC = V(opCol.x * 0.6f + 0.4f, opCol.y * 0.6f + 0.4f, opCol.z * 0.6f + 0.4f);
            if (g.np == 2)
            {
                Txt(g.pl[L].score.ToString(), -0.80f, 0.04f, 0.36f, myC);
                Txt(g.pl[1 - L].score.ToString(), 0.80f, 0.04f, 0.36f, opC);
            }
            else if (g.TeamsNow() == Teams.FFA || oct)
            {   // FOUR CORNERS, everyone for themselves: four scores, yours marked (FIREBALL BLITZ: health left, or OUT; always
                // the four, in their team's colours in teams: TASKS.md E51)
                for (int p = 0; p < 4; ++p)
                {
                    float x = (p < 2 ? -1f : 1f) * (p % 2 != 0 ? 0.62f : 0.95f);
                    Vector4 c = V(teamCol[p].x * 0.6f + 0.4f, teamCol[p].y * 0.6f + 0.4f, teamCol[p].z * 0.6f + 0.4f);
                    if (oct && ((g.startMask >> p) & 1) == 0 && g.match != MatchState.WaitingForPlayers) continue;
                    bool outP = oct && !g.Alive(p);
                    Txt(outP ? "OUT" : (oct ? g.pl[p].health : g.pl[p].score).ToString(), x, 0.08f, outP ? 0.10f : 0.20f, c);
                    if (p == L && !info.watching) Txt("YOU", x, -0.08f, 0.045f, c);
                }
            }
            else
            {   // teams: yours and theirs (3 v 1: THE ONE named)
                int mt = g.Team(L), ot = 1 - mt;
                Txt(g.TeamScore(mt).ToString(), -0.80f, 0.04f, 0.36f, myC);
                Txt(g.TeamScore(ot).ToString(), 0.80f, 0.04f, 0.36f, opC);
                if (g.TeamsNow() == Teams.ThreeVOne)
                {
                    bool one = g.tun.theOne == L;
                    Txt(one ? "YOU: THE ONE" : "YOUR THREE", -0.80f, -0.13f, 0.04f, myC);
                    Txt(one ? "THE THREE" : "THE ONE", 0.80f, -0.13f, 0.04f, opC);
                }
                else
                {
                    Txt("YOUR TEAM", -0.80f, -0.13f, 0.04f, myC);
                    Txt("THEIRS", 0.80f, -0.13f, 0.04f, opC);
                }
            }
            string clk = ClockLine(g);
            if (g.tun.advanced)
            {
                Txt($"ROUND {Math.Max(1, g.round)}", 0, 0.14f, 0.11f, white);
                Txt($"{g.pl[L].roundWins} - {g.pl[1 - L].roundWins}", 0, -0.02f, 0.09f, grey);
                if (clk.Length > 0) Txt(clk, 0, -0.13f, clk.Length > 6 ? 0.05f : 0.07f, g.overtime ? edgeR : white);   // a timed round
            }
            else
            {
                Txt(clk, 0, 0.06f, clk.Length > 6 ? 0.10f : 0.20f, g.overtime ? edgeR : white);
                // FREE PLAY never ends a match: say so when people are waiting (owner, 2026-10-21)
                if (g.tun.FreePlay() && info.queued > 0)
                    Txt(info.queued == 1 ? "1 PLAYER WAITING - FORFEIT TO HAND OVER" : "PLAYERS WAITING - FORFEIT TO HAND OVER", 0, -0.11f, 0.045f, edgeR);
                else Txt(g.tun.shields ? Menu.RoomName(g.tun) : Menu.RoomName(g.tun) + " - HOTDOG", 0, -0.11f, 0.055f, grey);
            }
            if (g.np == 2)
            {
                Txt($"STRIKES {g.pl[1 - L].strikesAgainst}", -0.80f, -0.22f, 0.05f, grey);
                Txt($"STRIKES {g.pl[L].strikesAgainst}", 0.80f, -0.22f, 0.05f, grey);
            }
            else if (oct) Txt($"HEALTH  -  {g.AliveCount()} STILL IN", -0.80f, -0.24f, 0.045f, grey);   // (FIREBALL BLITZ: how many are still in)
            else Txt($"YOUR STRIKES {g.pl[L].strikesFor}", -0.80f, -0.24f, 0.045f, grey);   // (four players: the strikes you hold make your ball bigger)
            Txt(g.pl[L].ball.charged ? "BALL CHARGED" : "NO CHARGE", 0, -0.24f, 0.045f, g.pl[L].ball.charged ? myC : grey);
            Txt($"{status}  -  {RulesLine(g.tun)}", 0, -0.42f, 0.045f, grey);
            string banner = Banner(g, L, info.watching, info.sideName, info.ready);
            Vec3 bannerAt = g.np == 4 ? new Vec3(0, 2.2f, 0) - g.Facing(L) * 1.5f : new Vec3(-1.5f * sx, 2.2f, 0);
            if (banner != null) Announcement(fx, banner, new Pose(faceP0, bannerAt), 0.22f, white);
            if (banner == null && g.np == 4 && info.leadFlash > 0 && g.Leader() >= 0)
            {   // FOUR CORNERS: who leads now, in their colour
                string[] leads = { "BLUE LEADS", "ORANGE LEADS", "GREEN LEADS", "VIOLET LEADS" };
                int lt = g.Leader();
                bool ours = !info.watching && lt == g.Team(L);
                bool each = g.TeamsNow() == Teams.FFA;
                string t = ours ? (each ? "YOU LEAD" : "YOUR TEAM LEADS")
                         : each ? leads[lt & 3]
                         : g.TeamsNow() == Teams.ThreeVOne ? (lt == 1 ? "THE ONE LEADS" : "THE THREE LEAD")
                         : (lt == 0 ? "BLUE TEAM LEADS" : "ORANGE TEAM LEADS");
                // (the team's colour: FREE-FOR-ALL the player's, teams their end's)
                Vector3 lc = each ? teamCol[lt & 3] : teamCol[TeamPlayer(g, lt)];
                Announcement(fx, t, new Pose(faceP0, bannerAt), 0.18f, V(lc.x * 0.6f + 0.4f, lc.y * 0.6f + 0.4f, lc.z * 0.6f + 0.4f, Math.Min(1f, info.leadFlash)));
            }
            if (banner == null && oct && info.octMsgT > 0)
            {   // FIREBALL BLITZ: who is out, or the room closing in (with its countdown)
                Vector3 mc = info.octMsgP >= 0 ? teamCol[info.octMsgP & 3] : default;
                Vector4 col = info.octMsgP >= 0 ? V(mc.x * 0.6f + 0.4f, mc.y * 0.6f + 0.4f, mc.z * 0.6f + 0.4f, Math.Min(1f, info.octMsgT))
                                                : V(1f, 0.62f, 0.2f, Math.Min(1f, info.octMsgT));
                string m = info.octMsg;
                if (info.octMsgP < 0 && g.ringCloseAt >= 0) m += "   " + (int)MathF.Ceiling(MathF.Max(0f, g.ringCloseAt - g.now));
                Announcement(fx, m, new Pose(faceP0, bannerAt), 0.19f, col);
            }
            Quat face = Quat.AxisAngle(new Vec3(0, 1, 0), M.Deg2Rad(L == 0 ? -90f : 90f));
            if (info.bumpFlash > 0 && info.player)
                fx.Text("FIST BUMP!", new Pose(face, new Vec3(L == 0 ? 1.6f : -1.6f, 2.30f, 0)), 0.12f, V(1.0f, 0.85f, 0.3f, Math.Min(1f, info.bumpFlash * 2f)));
            if (g.match == MatchState.Meet && info.bumpFlash <= 0 && info.player)
            {   // between the two players
                Vector4 cyanT = V(0.3f, 0.85f, 1.0f);
                float pulse = 0.5f + 0.5f * MathF.Sin(Time.time * 5f);
                fx.Sprite(new Vec3(0, 1.2f, 0), 0.10f + 0.04f * pulse, V(0.3f, 0.85f, 1.0f, 0.55f + 0.35f * pulse), viewer, 0);   // meet here
                fx.Text("BUMP HERE", new Pose(face, new Vec3(L == 0 ? 0.08f : -0.08f, 1.36f, 0)), 0.035f, cyanT);
                fx.Text("HOLD YOUR TRIGGER AND BUMP FISTS", new Pose(face, new Vec3(L == 0 ? 1.6f : -1.6f, 2.15f, 0)), 0.08f, cyanT);   // above the other player
                fx.Text($"OR THE MATCH STARTS IN {(int)MathF.Ceiling(g.stateTimer)}", new Pose(face, new Vec3(L == 0 ? 1.6f : -1.6f, 2.03f, 0)), 0.05f, white);
            }
            if (info.notice != null) fx.Text(info.notice, new Pose(faceP0, new Vec3(-1.5f * sx, 1.9f, 0)), 0.12f, V(1.0f, 0.75f, 0.2f));
        }

        // one player's career as display lines (native careerLines)
        public static string[] CareerLines(bool ok, Online.Net.CareerStats c)
        {
            if (!ok || !c.known) return new[] { "NO STATS YET" };
            if (c.matches == 0) return new[] { "FIRST ONLINE MATCH" };
            return new[]
            {
                $"{c.wins} WINS  /  {c.matches} MATCHES",
                $"THROW ACCURACY {(c.throws != 0 ? (int)Math.Round(100.0 * c.onTarget / c.throws) : 0)}%",
                $"BLOCKS {(c.faced != 0 ? (int)Math.Round(100.0 * c.blocks / c.faced) : 0)}%",
                $"TOP SPEED {(int)Math.Round(c.topSpeed * 3.6f)} KM/H",
                $"{c.hits} HITS  -  {c.catches} CATCHES",
            };
        }

        public const string CrowdSite = "CROWD.AFTERSPARC.COM";   // The Crowd's web app (fans follow online matches on their phones)

        // the screen block over centre court (four faces, 1.712 m out, 8 m up): each player's name, flag or team colour,
        // the score and their career stats; the blue end's player on the side nearer the blue end (native drawJumbotron)
        public static void Jumbotron(FxRenderer fx, Game g, float[][] sideRGB, int[] sideNation, string[] names, string[][] lines,
                                     Online.CrowdInfo crowd, bool online, Action<int, Pose, float> drawFlag)
        {
            Vector4 white = V(1, 1, 1), grey = V(0.70f, 0.74f, 0.80f), board = V(0.004f, 0.008f, 0.02f, 0.92f);
            for (int f = 0; f < 4; ++f)
            {
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), f * M.kPi * 0.5f);
                Vec3 n = q.Rotate(new Vec3(0, 0, 1)), c = new Vec3(0, 8f, 0) + n * 1.712f;
                bool flip = q.Rotate(new Vec3(1, 0, 0)).x < -0.5f;   // (this face's left is the orange end)
                Pose At(float u, float v, float w = 0f) => new Pose(q, c + q.Rotate(new Vec3(u, v, w)));
                fx.Panel(At(0, 0), 3.15f, 1.55f, board);
                fx.Text("-", At(0, 0.42f, 0.004f), 0.22f, grey);
                for (int si = 0; si < 2; ++si)
                {
                    int p = flip ? 1 - si : si;
                    float u = si != 0 ? 0.98f : -0.98f;
                    fx.Text(g.pl[p].score.ToString(), At(si != 0 ? 0.30f : -0.30f, 0.42f, 0.004f), 0.30f, white);
                    Vector4 col = V(0.65f * sideRGB[p][0] + 0.35f, 0.65f * sideRGB[p][1] + 0.35f, 0.65f * sideRGB[p][2] + 0.35f);
                    fx.Text(names[p], At(u, 0.58f, 0.004f), names[p].Length > 10 ? 0.10f : 0.13f, col);
                    if (sideNation[p] != 0) drawFlag(sideNation[p], At(u, 0.30f, 0.003f), 0.42f);
                    else fx.Panel(At(u, 0.33f, 0.002f), 0.62f, 0.05f, V(sideRGB[p][0], sideRGB[p][1], sideRGB[p][2], 0.95f));
                    for (int k = 0; k < lines[p].Length; ++k) fx.Text(lines[p][k], At(u, 0.05f - 0.15f * k, 0.004f), 0.070f, k != 0 ? grey : white);
                }
                // The Crowd, in the middle: how many fans follow on their phones and each end's hype; or where to follow
                if (crowd != null && crowd.fans > 0)
                {
                    fx.Text(crowd.fans.ToString(), At(0, 0.12f, 0.004f), 0.13f, white);
                    fx.Text(crowd.fans == 1 ? "FAN" : "FANS", At(0, -0.02f, 0.004f), 0.06f, grey);
                    for (int si = 0; si < 2; ++si)
                    {
                        int p = flip ? 1 - si : si;
                        float h = 0.40f * Math.Max(0.04f, crowd.hype[p] / 100f);
                        fx.Panel(At(si != 0 ? 0.08f : -0.08f, -0.58f + 0.5f * h, 0.003f), 0.08f, h, V(sideRGB[p][0], sideRGB[p][1], sideRGB[p][2], 0.95f));
                    }
                    fx.Text("HYPE", At(0, -0.66f, 0.004f), 0.05f, grey);
                }
                else if (online)
                {
                    fx.Text("FOLLOW LIVE ON YOUR PHONE", At(0, -0.60f, 0.004f), 0.045f, grey);
                    fx.Text(CrowdSite, At(0, -0.68f, 0.004f), 0.055f, white);
                }
            }
        }
    }
}
