// BUTTONS (TASKS.md E20 / E21; Runtime/Controls.cs, native controls.h and test_game.cpp "[buttons]", eeb17b7): STANDARD
// is the old controls and LEFT-HANDED its mirror; a grip takes only jobs that do no harm; TALK, READY and RECENTER keep a
// button on every kind of controller; settings.cfg's buttons= reads back and refuses what it can't
using NUnit.Framework;
using static ProjectArena.Controls;

namespace ProjectArena.Tests
{
    public class ControlsTests
    {
        [Test]
        public void TheButtonsAsNativeHasThem()
        {
            Map st = Standard(), lh = LeftHanded();
            Assert.That(Save(st), Is.EqualTo("104542350"),
                        "STANDARD is the controls as they were: the left grip talks, A and X ready, B and the right stick recenter, Y peeks, the left stick shows the names");
            Assert.That(Save(lh) == "014245531" && SameMap(Mirrored(lh), st),
                        "LEFT-HANDED is STANDARD mirrored: the right grip talks, Y and the left stick recenter, B peeks, the left stick turns");
            // a grip takes only jobs that do no harm squeezed by accident, and may do nothing
            Map g = st.Clone();
            string gripJobs = "";
            for (int i = 0; i < 4; ++i) { g.job[S_RGRIP] = NextJob(g, S_RGRIP, K_TOUCH); gripJobs += JobName(g.job[S_RGRIP]) + " "; }
            Assert.That(gripJobs, Is.EqualTo("TALK PEEK NAMES NOTHING "), "a grip goes TALK, PEEK, NAMES, NOTHING (never READY or RECENTER)");
            Assert.That(!CanLeave(st, S_LGRIP, K_TOUCH) && NextJob(st, S_LGRIP, K_TOUCH) == J_TALK, "TALK's last button keeps it (give it another one first)");
            // the owner's case: TALK on Y, the left grip left empty
            Map y = st.Clone();
            for (int i = 0; i < J_COUNT && y.job[S_Y] != J_TALK; ++i) y.job[S_Y] = NextJob(y, S_Y, K_TOUCH);
            for (int i = 0; i < J_COUNT && y.job[S_LGRIP] != J_NONE; ++i) y.job[S_LGRIP] = NextJob(y, S_LGRIP, K_TOUCH);
            var down = new bool[S_COUNT];
            down[S_Y] = true;
            bool yTalks = Held(y, J_TALK, K_TOUCH, down);
            down[S_Y] = false; down[S_LGRIP] = true;
            Assert.That(y.job[S_Y] == J_TALK && y.job[S_LGRIP] == J_NONE && yTalks && !Held(y, J_TALK, K_TOUCH, down) && !Held(y, J_PEEK, K_TOUCH, down),
                        "TALK can move to Y and the left grip do nothing (its squeeze does no job)");
            // what each job's buttons do, and the controllers that lack a button (Vive: no Y, no sticks; WMR: no Y)
            bool[] none = new bool[S_COUNT], x = new bool[S_COUNT], rs = new bool[S_COUNT];
            x[S_X] = true; rs[S_RSTICK] = true;
            Assert.That(Held(st, J_READY, K_TOUCH, x) && Held(st, J_RECENTER, K_TOUCH, rs) && !Held(st, J_READY, K_TOUCH, none),
                        "X readies up and the right stick's click recenters, as before");
            Map talkY = st.Clone();
            talkY.job[S_LGRIP] = J_NONE; talkY.job[S_Y] = J_TALK;
            var odd = new Map();   // TALK on the left stick, READY on Y, RECENTER on A and B
            odd.job[S_A] = J_RECENTER; odd.job[S_B] = J_RECENTER; odd.job[S_Y] = J_READY; odd.job[S_LSTICK] = J_TALK;
            Map v1 = Effective(talkY, K_VIVE), v2 = Effective(odd, K_VIVE), w = Effective(talkY, K_WMR);
            Assert.That(v1.job[S_LGRIP] == J_TALK && w.job[S_LGRIP] == J_TALK && Effective(talkY, K_TOUCH).job[S_LGRIP] == J_NONE,
                        "TALK on Y with a Vive or Windows Mixed Reality controller: the left grip talks (with Touch: Y)");
            Assert.That(v2.job[S_LGRIP] == J_TALK && v2.job[S_A] == J_READY && v2.job[S_B] == J_RECENTER,
                        "on a Vive: READY and TALK without a button there take their usual ones, RECENTER keeps one");
            // settings.cfg: read back as saved; a map this game can't read is refused (STANDARD kept)
            Map r = st.Clone(), bad = st.Clone();
            bool round = Load(r, Save(lh)) && SameMap(r, lh);
            bool refused = !Load(bad, "554542350") && !Load(bad, "000000000") && !Load(bad, "10454235") &&
                           !Load(bad, "10454235x") && !Load(bad, "1045423a0") && SameMap(bad, st);
            Assert.That(round && refused, "buttons= reads back as saved; READY on a grip, no TALK / READY / RECENTER, or garbage: refused");
        }

        // pressed at random on every kind of controller: TALK, READY and RECENTER always keep a button there, a grip never
        // readies or recenters, and every map saves and reads back (native's 3000 presses on each of five kinds)
        [Test]
        public void AnyPressesKeepTheJobsThatNeedAButton()
        {
            int bad1 = 0, bad2 = 0, bad3 = 0, presses = 0;
            uint seed = 12345u;
            int Rnd(int n) { seed = seed * 1664525u + 1013904223u; return (int)((seed >> 8) % (uint)n); }
            for (int kind = K_UNKNOWN; kind <= K_WMR; ++kind)
            {
                Map m = Standard();
                for (int i = 0; i < 3000; ++i)
                {
                    m = Effective(m, kind);
                    int what = Rnd(40);
                    if (what == 0) m = Standard();
                    else if (what == 1) m = LeftHanded();
                    else if (what == 2) m.leftSticks = !m.leftSticks;
                    else
                    {
                        int s = Rnd(S_COUNT);
                        if (!OnController(kind, s)) continue;
                        m.job[s] = NextJob(m, s, kind);
                        ++presses;
                    }
                    Map e = Effective(m, kind);
                    for (int j = 0; j < J_COUNT; ++j) bad1 += Required(j) && Count(e, j, kind) == 0 ? 1 : 0;
                    bad2 += e.job[S_LGRIP] == J_READY || e.job[S_LGRIP] == J_RECENTER || e.job[S_RGRIP] == J_READY || e.job[S_RGRIP] == J_RECENTER ? 1 : 0;
                    Map back = Standard();
                    bad3 += !(Load(back, Save(m)) && SameMap(back, m)) ? 1 : 0;
                }
            }
            TestContext.Out.WriteLine($"  ({presses} presses on the page, five kinds of controller)");
            Assert.That(bad1 == 0 && bad2 == 0 && bad3 == 0,
                        "any presses: TALK, READY and RECENTER keep a button on these controllers, no grip readies or recenters, it saves");
        }

        // the words that name a button, as they are now (native jobButtons, readyName, talkHint), and the controllers'
        // kinds from the devices Unity's OpenXR profiles make
        [Test]
        public void EveryButtonNamedAsItIs()
        {
            Map st = Standard();
            Assert.That(JobButtons(st, K_TOUCH, J_RECENTER, true), Is.EqualTo("B / RIGHT STICK CLICK"), "HOW TO PLAY: RECENTER's buttons");
            Assert.That(JobButtons(st, K_TOUCH, J_NAMES, true), Is.EqualTo("LEFT STICK CLICK"));
            Assert.That(ReadyName(st, K_TOUCH) == "A" && ReadyName(st, K_INDEX) == "RIGHT A" && ReadyName(Effective(st, K_VIVE), K_VIVE) == "RIGHT PAD",
                        "PRESS A: READY's button as these controllers name it");
            Assert.That(TalkHint(st, K_TOUCH), Is.EqualTo("HOLD LEFT GRIP TO TALK"));
            Map stick = st.Clone(); stick.job[S_LSTICK] = J_TALK; stick.job[S_LGRIP] = J_NONE;
            Assert.That(TalkHint(stick, K_TOUCH) == "HOLD THE LEFT STICK IN TO TALK" && TalkHand(stick, K_TOUCH, 0) == 0, "a stick: held in");
            Map right = st.Clone(); right.job[S_RGRIP] = J_TALK; right.job[S_LGRIP] = J_NONE;
            Assert.That(TalkHand(right, K_TOUCH, 0) == 1 && TalkHand(right, K_TOUCH, 1) == 0, "the hint by the hand that talks (open mic: the left)");
            var down = new bool[S_COUNT];
            Assert.That(TalkValue(st, K_TOUCH, down, new[] { 0.7f, 0f }), Is.EqualTo(0.7f), "the left grip as push to talk reads it");
            Assert.That(TalkValue(right, K_TOUCH, down, new[] { 0.9f, 0.2f }), Is.EqualTo(0.2f), "the right grip, the left doing nothing");
            down[S_LSTICK] = true;
            Assert.That(TalkValue(stick, K_TOUCH, down, new[] { 0.9f, 0f }), Is.EqualTo(1f), "a button: 1 while held");
            Assert.That(XrInput.KindOf("OculusTouchController") == K_TOUCH && XrInput.KindOf("QuestTouchPlusController") == K_TOUCH &&
                        XrInput.KindOf("ValveIndexController") == K_INDEX && XrInput.KindOf("ViveController") == K_VIVE &&
                        XrInput.KindOf("WMRSpatialController") == K_WMR && XrInput.KindOf(null) == K_UNKNOWN,
                        "the controllers in your hands, from the OpenXR profile's device");
        }
    }
}
