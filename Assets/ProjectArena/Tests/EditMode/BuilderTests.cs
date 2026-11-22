// The avatar builder (Runtime/Builder.cs), as the native openBuilder / builderActivate / updateBuilder behave: pointing
// at a row's < > changes it, PRESET and COUNTRY FLAG, RANDOM, CANCEL puts the look back, SAVE AND APPLY keeps it in
// avatar.cfg; the menu opens it; the mirror reflects.
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEngine;
using static ProjectArena.Sim.M;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena.Tests
{
    public class BuilderTests
    {
        string folder;
        AppState app;
        Builder b;
        PlayerInput me;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "pa_builder_test_" + System.Guid.NewGuid().ToString("N"));
            var g = new Game();
            g.Reset(true);
            app = new AppState { game = g, settings = new AppSettings(folder), lobby = false };
            app.settings.Load(g.tun);
            app.settings.myAvatar = Avatar.PresetLook(0);
            b = new Builder(app);
            me = new PlayerInput { headTracked = true, head = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(-90)), g.tun.start[0] + new Vec3(0, 1.65f, 0)) };
            b.Open(me);
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        // point the right hand's aim at a control and pull the trigger (one frame released, one pulled)
        bool Click(int code)
        {
            Vec3 target = b.ControlPoint(code);   // (on its panel: TASKS.md E36)
            Vec3 from = me.head.p + new Vec3(0, -0.3f, 0);
            var aim = new Pose(FromTo(new Vec3(0, 0, -1), Normalize(target - from)), from);
            me.hand[1].trigger = 0;
            b.Update(me, aim, true);
            me.hand[1].trigger = 1;
            return b.Update(me, aim, true);
        }

        [Test]
        public void OpensWithTheMirrorAheadAndThePanelBeside()
        {
            Assert.That(b.open && app.builderOpen);
            Assert.That(Length(b.mirrorPoint - new Vec3(me.head.p.x + 0.85f, 0, me.head.p.z)), Is.LessThan(1e-4f), "the mirror 0.85 m ahead (+X at your end)");
            Assert.That(b.mirrorNormal.x, Is.EqualTo(-1f).Within(1e-5f), "facing you");
            Vec3 toMe = me.head.p - b.pose.p;
            Assert.That(Dot(b.pose.q.Rotate(new Vec3(0, 0, 1)), Normalize(new Vec3(toMe.x, 0, toMe.z))), Is.GreaterThan(0.999f), "the panel faces you");
            Matrix4x4 r = Builder.Reflect(b.mirrorPoint, b.mirrorNormal);
            Vector3 p = r.MultiplyPoint(new Vector3(me.head.p.x, 1.6f, me.head.p.z));
            Assert.That(p.x, Is.EqualTo(me.head.p.x + 1.7f).Within(1e-4f), "your reflection stands as far behind the glass");
            Assert.That(r.determinant, Is.LessThan(0f), "a mirror image (drawn with the other faces)");
        }

        [Test]
        public void RowsPresetsAndCountry()
        {
            int suit = (int)Avatar.Field.Suit;   // (hit codes: field * 2, + 1 for >: TASKS.md E36)
            Assert.That(Click(suit * 2 + 1), "a click");
            Assert.That(app.settings.myAvatar.suit, Is.EqualTo(1), "SUIT > : GRID RUNNER -> ARENA JERSEY");
            Click(suit * 2); Click(suit * 2);
            Assert.That(app.settings.myAvatar.suit, Is.EqualTo(Avatar.FieldCount(Avatar.Field.Suit) - 1), "< wraps round");
            Click(Builder.HitPresetNext);
            Assert.That(app.settings.myAvatar, Is.EqualTo(Avatar.PresetLook(1)), "PRESET > : ROOKIE");
            for (int i = 0; i < Avatar.PresetCount() - 1; ++i) Click(Builder.HitPresetNext);
            Assert.That(b.PresetLabel, Is.EqualTo("COUNTRY FLAG"));
            var order = Avatar.NationOrder(null);   // (COUNTRY in your order, TASKS.md E44: nothing chosen yet, A-Z)
            Assert.That(Avatar.NationOf(app.settings.myAvatar), Is.EqualTo(order[0]), "COUNTRY FLAG puts on the top of the COUNTRY order's kit");
            Click(Builder.HitCountryNext); Click(Builder.HitCountryNext);
            Assert.That(Avatar.NationOf(app.settings.myAvatar), Is.EqualTo(order[2]), "COUNTRY > > (A-Z)");
            Click(Builder.HitCountryPrev); Click(Builder.HitCountryPrev); Click(Builder.HitCountryPrev);
            Assert.That(Avatar.NationOf(app.settings.myAvatar), Is.EqualTo(order[order.Count - 1]), "< wraps to the last country");
            // COUNTRY KIT (TASKS.md E34, native 485beac): under COUNTRY, TEAM JERSEY or MY OUTFIT (either arrow swaps them)
            Assert.That(b.HitTest(1, b.ControlAt(Builder.HitKitNext).x, b.ControlAt(Builder.HitKitNext).y), Is.EqualTo(Builder.HitKitNext), "the COUNTRY KIT row");
            Assert.That(!Avatar.OwnKit(app.settings.myAvatar), "TEAM JERSEY first");
            Click(Builder.HitKitNext);
            Assert.That(Avatar.OwnKit(app.settings.myAvatar), "MY OUTFIT");
            Click(Builder.HitKitPrev);
            Assert.That(!Avatar.OwnKit(app.settings.myAvatar), "TEAM JERSEY again");
            var before = app.settings.myAvatar;
            Click(Builder.HitRandom);
            AvatarDesc r = app.settings.myAvatar, c = r;
            Avatar.Sanitize(ref c);
            Assert.That(c, Is.EqualTo(r), "RANDOM: a valid look");
            Assert.That(r, Is.Not.EqualTo(before));
        }

        // CUSTOMIZE AVATAR in the order of the body (TASKS.md E36, native 37c09d3): right of the mirror the parts from head
        // to toe, each colour beside the part it goes with; left of it PRESET AND BODY; the pointer meets the nearer panel
        [Test]
        public void InTheOrderOfTheBody()
        {
            float y(Avatar.Field f) => b.ControlAt((int)f * 2, out _).y;
            Assert.That(y(Avatar.Field.Headgear) > y(Avatar.Field.Hair) && y(Avatar.Field.Hair) > y(Avatar.Field.Face) && y(Avatar.Field.Face) > y(Avatar.Field.Visor) &&
                        y(Avatar.Field.Visor) > y(Avatar.Field.Mask) && y(Avatar.Field.Mask) > y(Avatar.Field.Suit) && y(Avatar.Field.Suit) > y(Avatar.Field.Gauntlet) &&
                        y(Avatar.Field.Gauntlet) > y(Avatar.Field.Lower), "head to toe: HEADGEAR, HAIR, FACE, FACE SHIELD, MASK, SUIT, GAUNTLET, LOWER BODY");
            Assert.That(y(Avatar.Field.HairColor) == y(Avatar.Field.Hair) && y(Avatar.Field.Skin) == y(Avatar.Field.Face) && y(Avatar.Field.VisorTint) == y(Avatar.Field.Visor) &&
                        y(Avatar.Field.Primary) == y(Avatar.Field.Suit) && y(Avatar.Field.Glow) == y(Avatar.Field.Lower), "each colour beside the part it goes with");
            b.ControlAt((int)Avatar.Field.Body * 2, out int pb);
            b.ControlAt(Builder.HitPresetNext, out int pp);
            b.ControlAt((int)Avatar.Field.Hair * 2, out int ph);
            Assert.That(pb == 1 && pp == 1 && ph == 0, "PRESET and BODY TYPE on the left panel, the parts on the right");
            Click((int)Avatar.Field.Body * 2 + 1);
            Assert.That(Avatar.BodyType(app.settings.myAvatar), Is.EqualTo(1), "BODY TYPE > on the left panel");
            Click((int)Avatar.Field.Headgear * 2 + 1);
            Assert.That(app.settings.myAvatar.headgear, Is.EqualTo(Avatar.PresetLook(0).headgear + 1), "HEADGEAR > on the right");
            Assert.That(b.ControlAt(Builder.HitCountryNext).x, Is.EqualTo(9f), "no country: no COUNTRY row");
        }

        // COUNTRIES IN YOUR ORDER and MY LOOKS (TASKS.md E44 / E45, native 10668f0 / ddd18a1): a country chosen counts when the
        // builder closes wearing it; three looks saved in looks.cfg, WEAR, SAVE (SURE? over a filled one), CLEAR (SURE?)
        [Test]
        public void CountriesInYourOrderAndMyLooks()
        {
            Click(Builder.HitLook + 1);   // SAVE into LOOK 1 (empty: at once)
            Assert.That(app.settings.myLookSaved[0] && app.settings.myLooks[0].Equals(Avatar.PresetLook(0)), "LOOK 1 saved");
            Assert.That(b.HitTest(1, b.ControlAt(Builder.HitLook + 3).x, b.ControlAt(Builder.HitLook + 3).y), Is.EqualTo(-1), "an empty slot has nothing to wear");
            Click(Builder.HitPresetNext);   // ROOKIE on
            Click(Builder.HitLook + 1);
            Assert.That(app.settings.myLooks[0].Equals(Avatar.PresetLook(0)) && b.lookConfirm == Builder.HitLook + 1, "over a filled slot: SURE? first");
            Click(Builder.HitLook + 1);
            Assert.That(app.settings.myLooks[0].Equals(Avatar.PresetLook(1)), "a second press saves over it");
            Click(Builder.HitPresetPrev);
            Click(Builder.HitLook);   // WEAR LOOK 1
            Assert.That(app.settings.myAvatar.Equals(Avatar.PresetLook(1)), "WEAR: the saved look goes on");
            var back = new AppSettings(folder);
            back.Load(new Tunables());
            back.LoadLooks();
            Assert.That(back.myLookSaved[0] && back.myLooks[0].Equals(Avatar.PresetLook(1)) && !back.myLookSaved[1], "kept in looks.cfg at once");
            Click(Builder.HitLook + 2); Click(Builder.HitLook + 2);   // CLEAR, sure
            Assert.That(!app.settings.myLookSaved[0] && app.settings.myAvatar.Equals(Avatar.PresetLook(1)), "CLEAR: EMPTY again, the look you have on stays on");
            // a country chosen: counted when the builder closes wearing it
            for (int i = 0; i < Avatar.PresetCount(); ++i) Click(Builder.HitPresetNext);   // (from the first preset, where WEAR left it, to COUNTRY FLAG)
            int n = Avatar.NationOf(app.settings.myAvatar);
            Assert.That(n, Is.EqualTo(Avatar.NationOrder(null)[0]), "COUNTRY FLAG");
            Click(Builder.HitCountryNext);
            int chosen = Avatar.NationOf(app.settings.myAvatar);
            Click(Builder.HitSave);
            Assert.That(app.settings.nationPicks[chosen], Is.EqualTo(1), "a country left with counts as chosen once");
            b.Open(me);
            Assert.That(b.nationOrder[0], Is.EqualTo(chosen), "next time it heads the COUNTRY order");
            b.Close();
            Assert.That(app.settings.nationPicks[chosen], Is.EqualTo(1), "closed with the country it opened with: not counted again");
        }

        [Test]
        public void CancelPutsTheLookBackAndSaveKeepsIt()
        {
            Click(Builder.HitPresetNext);
            Click(Builder.HitCancel);
            Assert.That(!b.open && !app.builderOpen, "CANCEL closes");
            Assert.That(app.settings.myAvatar, Is.EqualTo(Avatar.PresetLook(0)), "the look from before");
            b.Open(me);
            Click(Builder.HitPresetNext); Click(Builder.HitPresetNext);
            Click(Builder.HitSave);
            Assert.That(!b.open && b.notice == "LOOK SAVED");
            var s = new AppSettings(folder);
            s.Load(new Tunables());
            s.LoadAvatar(5);
            Assert.That(s.myAvatar, Is.EqualTo(Avatar.PresetLook(2)), "VANGUARD kept in avatar.cfg");
        }

        [Test]
        public void NoGrabbingWhileCustomizing()
        {
            me.hand[0].trigger = 1; me.hand[0].squeeze = 1;
            b.Update(me, Pose.Identity, false);
            Assert.That(me.hand[0].trigger == 0 && me.hand[0].squeeze == 0);
        }

        [Test]
        public void TheMenuOpensIt()
        {
            var menu = new Menu(app);
            menu.Open(me.head, Lobby.Spot);
            menu.titleOpen = true;
            var it = menu.Items().First(i => i.label == "CUSTOMIZE AVATAR");
            Assert.That(it.enabled, "CUSTOMIZE AVATAR is on");
            menu.Activate(it);
            Assert.That(app.openBuilder && !menu.open, "the menu closes and asks for the builder");
            Assert.That(menu.Items().First(i => i.act == Menu.Act.Nav && i.ip == (uint)Menu.Nav.Avatar).enabled, "AVATAR in the side menu");
        }
    }
}
