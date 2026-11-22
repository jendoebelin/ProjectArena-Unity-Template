// A match's statistics for the community server's careers, ported from the native game.h / game.cpp (MatchTally,
// StatCounter; native 8f35f26 and 6eca485, the fix for the relay refusing matches, report #14): each throw on target is
// counted once, and a point scored with a ball you blocked or punched back counts as a hit and as a point from a return.
using System;

namespace ProjectArena.Sim
{
    public struct MatchTally
    {
        public ushort throws, onTarget, hits, blocks, faced, catches, strikes, returnHits;
        public float topSpeed;
    }

    // Four players (TASKS.md E47, native 530f6fb, 0.3.23; the owner, 2026-11-20: four-player stats didn't appear on The
    // Crowd's page, in FOUR CORNERS or in FIREBALL BLITZ): the same counts for each of the four, for The Crowd's page
    // (careers stay one-on-one). With two players the one attacking is always the other; with four it is
    // whoever threw or returned that ball last
    public class StatCounter
    {
        public readonly MatchTally[] s = new MatchTally[Teams.MaxPlayers];
        public int np = 2;   // (the match's players: set by whoever counts, before its events)

        struct Shot { public int by; public bool thrown, counted; }   // the attack each ball carries
        readonly Shot[] shot = { new Shot { by = -1 }, new Shot { by = -1 }, new Shot { by = -1 }, new Shot { by = -1 } };

        static void Bump(ref ushort v) { if (v < 3000) ++v; }

        // Throw, HitPlayer, Strike, ShieldBlock, KnuckleBlock, Caught (others: ignored)
        public void Add(GameEvent e)
        {
            int n = np == 4 ? 4 : 2;
            int p = e.player, k = e.ball;
            if (p < 0 || p >= n) return;
            bool known = k >= 0 && k < n;
            // who attacks with ball k: two players, the other one (as always); four, whoever threw or returned it last
            // (-1: nobody known, or p themselves)
            int Attacker()
            {
                if (n == 2) return 1 - p;
                int by = known ? shot[k].by : -1;
                return by != p ? by : -1;
            }
            // is ball k an attack by `who`? 0 no, 1 yes (already on target), 2 yes and now on target for the first time
            int Attack(int who)
            {
                if (who < 0 || !known || shot[k].by != who) return 0;
                if (shot[k].counted) return 1;
                shot[k].counted = true;
                if (shot[k].thrown) Bump(ref s[who].onTarget);
                return 2;
            }
            switch (e.type)
            {
                case EventType.Throw:   // a player throws their own ball
                    Bump(ref s[p].throws);
                    s[p].topSpeed = Math.Max(s[p].topSpeed, e.value);
                    shot[p] = new Shot { by = p, thrown = true, counted = false };
                    break;
                case EventType.HitPlayer:
                {   // player = the one hit, value = the scorer
                    int sc = (int)e.value, a = sc >= 0 && sc < n && sc != p ? Attack(sc) : 0;
                    if (a != 0)
                    {
                        Bump(ref s[sc].hits);
                        if (!shot[k].thrown) Bump(ref s[sc].returnHits);
                        if (a == 2) Bump(ref s[p].faced);
                    }
                    break;
                }
                case EventType.Strike:
                {   // player = the one struck against
                    int by = Attacker();
                    if (by < 0) break;
                    Bump(ref s[by].strikes);
                    if (Attack(by) == 2) Bump(ref s[p].faced);
                    break;
                }
                case EventType.ShieldBlock: case EventType.KnuckleBlock:   // player = the blocker: the ball is now their return
                    Bump(ref s[p].blocks);
                    Bump(ref s[p].faced);
                    Attack(Attacker());
                    if (known) shot[k] = new Shot { by = p, thrown = false, counted = false };
                    break;
                case EventType.Caught:
                    Bump(ref s[p].catches);
                    break;
            }
        }
    }
}
