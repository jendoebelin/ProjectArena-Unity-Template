using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The lobby balcony (main.cpp drawLobby, updateLobby, lobbyRayHit, lobbyAction, startLobbyMatch): you stand above
    // the court beside it, the two bots playing below; the court is a button (point at it and pull the trigger: practice
    // against the bot). The bots' matches are started by ArenaSession. (The six posters, PLAY THE BOT, MULTIPLAYER, HOW
    // TO PLAY, CUSTOMIZE, SPECTATE and SETTINGS, taken out in native 1159a46, 0.3.24: the owner, 2026-11-21, "i haven't
    // heard anyone say they like them"; the main menu, open on arrival, has every one of them)
    public class Lobby
    {
        public const float Floor = 2.5f;
        public static readonly Vec3 Spot = new Vec3(0, Floor, 5.2f);
        public const int Court = 0;       // hover: the court (-1: nothing)

        public int hover = -1;            // the court under the pointer (Court; -1 nothing)
        bool prevTrig = true;

        readonly AppState a;
        readonly Menu menu;
        public Lobby(AppState a, Menu menu) { this.a = a; this.menu = menu; }

        // pointer ray against the court (the glass tank: a slab test); -1 nothing, Court the court
        public static int RayHit(Vec3 o, Vec3 d, out float dist)
        {
            dist = 1e9f;
            var lo = new Vec3(-9.45f, 0f, -2.45f);
            var hi = new Vec3(9.45f, 3.6f, 2.45f);
            float t0 = 0f, t1 = 60f;
            bool inside = true;
            for (int k = 0; k < 3 && inside; ++k)
            {
                if (Mathf.Abs(d[k]) < 1e-6f) { if (o[k] < lo[k] || o[k] > hi[k]) inside = false; continue; }
                float ta = (lo[k] - o[k]) / d[k], tb = (hi[k] - o[k]) / d[k];
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = M.Max(t0, ta); t1 = M.Min(t1, tb);
                if (t0 > t1) inside = false;
            }
            if (!inside) return -1;
            dist = t0;
            return Court;
        }

        void Action(int which, Pose head)   // the court: practice against the bot
        {
            if (which != Court) return;
            Debug.Log("[Lobby] the court");
            menu.Open(head, Spot);
            menu.titleOpen = menu.settingsOpen = menu.helpOpen = menu.rulesOpen = false;
            menu.rulesOpen = true;
            menu.Activate(new Menu.Item("", 0, 0, 0, 0, Menu.Act.Play, 0, true));
        }

        // rawTrigger: the pointer hand's trigger before the menu clears it. Returns true when something was pressed.
        public bool Update(PlayerInput @in, float rawTrigger, Pose aim, bool aimValid)
        {
            hover = -1;
            if (!a.InLobby || a.builderOpen || (menu.open && menu.hitValid) || !aimValid)
            {
                prevTrig = true;
                return false;
            }
            hover = RayHit(aim.p, aim.q.Rotate(new Vec3(0, 0, -1)), out _);
            if (menu.open && hover == Court) hover = -1;   // the open menu sits in front of the court
            bool trig = rawTrigger > 0.7f;
            bool pressed = false;
            if (trig && !prevTrig && hover >= 0) { Action(hover, @in.head); pressed = true; }
            prevTrig = trig;
            return pressed;
        }

        static Vector4 V(float r, float g, float b, float al = 1) => new Vector4(r, g, b, al);

        public const string SignLine2 = "POINT AT THE COURT AND PULL THE TRIGGER TO PLAY THE BOT";

        // native drawLobby (the balcony, the rail sign, the court as a button, the pointer); your hands are drawn by the
        // caller. The deck and the sign's backing solid, drawn with the opaque things (native 1159a46: 92% opaque, they
        // were blended over the court below, which the graphics chip drew in full underneath them)
        public void Draw(FxRenderer fx, Pose aim, bool aimValid)
        {
            if (!a.InLobby) return;
            Vector4 white = V(1, 1, 1), grey = V(0.62f, 0.68f, 0.76f);
            Vector4 deckSolid = V(0.02f, 0.03f, 0.05f), glow = V(0.10f, 0.55f, 0.85f);
            // the balcony: a dark deck with glowing edges and a front rail
            var c = new Vec3(0, Floor, Spot.z - 0.4f);
            var sign = new Pose(Quat.Identity, c + new Vec3(0, 0.82f, -1.31f));
            fx.PanelSolid(new Pose(Quat.AxisAngle(new Vec3(1, 0, 0), -M.kPi * 0.5f), c + new Vec3(0, -0.005f, 0)), 5.4f, 2.6f, deckSolid);
            fx.PanelSolid(sign, 2.3f, 0.2f, deckSolid);
            // (the unit box spans -1..1: its scale is a half size)
            fx.Box(new Pose(Quat.Identity, c + new Vec3(0, 0.0f, -1.3f)), new Vec3(2.7f, 0.004f, 0.004f), glow);
            fx.Box(new Pose(Quat.Identity, c + new Vec3(0, 1.0f, -1.3f)), new Vec3(2.7f, 0.006f, 0.006f), glow);   // the rail
            foreach (float x in new[] { -2.7f, 2.7f }) fx.Box(new Pose(Quat.Identity, c + new Vec3(x, 0.0f, 0)), new Vec3(0.004f, 0.004f, 1.3f), glow);
            foreach (float x in new[] { -2.6f, -1.3f, 0f, 1.3f, 2.6f }) fx.Box(new Pose(Quat.Identity, c + new Vec3(x, 0.5f, -1.3f)), new Vec3(0.004f, 0.5f, 0.004f), glow);
            fx.SpriteFlat(new Vec3(Spot.x, Floor + 0.006f, Spot.z), 1.0f, V(0.15f, 0.6f, 0.9f), new Vec3(0, 1, 0), 1);
            // the rail sign: the two things everyone needs (its backing drawn above, solid)
            fx.Text("LEFT MENU BUTTON: MAIN MENU    -    HOLD THE META BUTTON: RECENTER", sign * new Pose(Quat.Identity, new Vec3(0, 0.03f, 0.004f)), 0.035f, white);
            fx.Text(SignLine2, sign * new Pose(Quat.Identity, new Vec3(0, -0.04f, 0.004f)), 0.026f, grey);
            // the court as a button
            if (hover == Court)
                fx.Text("PULL THE TRIGGER TO PLAY THE BOT", new Pose(Quat.Identity, new Vec3(0, 3.95f, 2.6f)), 0.22f, V(1.0f, 0.85f, 0.3f));
            // the pointer
            if (!menu.open && !a.builderOpen && aimValid)
            {
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                int hit = RayHit(o, d, out float dist);
                float len = hit >= 0 ? Mathf.Min(dist, 25f) : 2.0f;
                Vector4 laser = V(0.3f, 0.8f, 1.0f);
                fx.Box(new Pose(aim.q, o + d * (len * 0.5f)), new Vec3(0.003f, 0.003f, len * 0.5f), laser);
                if (hit >= 0) fx.Glow(o + d * len, 0.05f, laser);
            }
        }
    }
}
