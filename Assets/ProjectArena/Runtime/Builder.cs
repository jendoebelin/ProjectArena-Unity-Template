using System;
using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The avatar builder (native main.cpp openBuilder / closeBuilder / builderActivate / updateBuilder / drawBuilder /
    // drawMirror) and the title showcase (drawTitleShowcase): a mirror 0.85 m ahead whose reflection copies you, and two
    // pointer-driven panels beside it (TASKS.md E36, native 37c09d3): right of the mirror the parts from head to toe, each
    // colour and the pattern beside the part it goes with, and RANDOM / CANCEL / SAVE AND APPLY; left of it PRESET AND
    // BODY (PRESET, with a country its COUNTRY and COUNTRY KIT, BODY TYPE, BUILD). Each row its name over < VALUE >. The
    // look shows live and is saved to avatar.cfg when the builder closes.
    public class Builder
    {
        // panel layout (panel space: metres, +Z toward the player; native kBRow0 ...). Two panels (the owner, 2026-11-19: "the
        // ordering would be more intuitive if [it] followed where they physically are like head at the top, bottoms at the
        // bottom ... color and patterns might be best on the right ... the others should be off to the left")
        public const float Row0 = 0.325f, RowH = 0.076f, LabelUp = 0.032f, Arrow = 0.165f, ValueW = 0.27f;
        public static readonly float[] ColX = { -0.205f, 0.205f };   // (the right panel's two columns)
        public const float HalfW = 0.435f, HalfWL = 0.23f, Top = 0.52f;
        // the right panel's rows: the parts from head to toe (column 0), each colour and the pattern beside the part it
        // goes with (column 1)
        public static readonly (Avatar.Field f, int col, int row)[] Rows =
        {
            (Avatar.Field.Headgear, 0, 0), (Avatar.Field.Hair, 0, 1), (Avatar.Field.Face, 0, 2), (Avatar.Field.Visor, 0, 3),
            (Avatar.Field.Mask, 0, 4), (Avatar.Field.Suit, 0, 5), (Avatar.Field.Gauntlet, 0, 6), (Avatar.Field.Lower, 0, 7),
            (Avatar.Field.HairColor, 1, 1), (Avatar.Field.Skin, 1, 2), (Avatar.Field.VisorTint, 1, 3), (Avatar.Field.Pattern, 1, 4),
            (Avatar.Field.Primary, 1, 5), (Avatar.Field.Secondary, 1, 6), (Avatar.Field.Glow, 1, 7),
        };
        public static float RowY(int row) => Row0 - row * RowH;
        // the left panel's items, top to bottom: PRESET, with a country its COUNTRY and COUNTRY KIT, then BODY TYPE and BUILD
        public const int LeftPreset = -1, LeftCountry = -2, LeftKit = -3;
        // hit codes: field * 2 (+1 = next), 100 / 101 preset prev / next, 102 random, 103 save and apply, 104 / 105 country
        // prev / next, 106 cancel, 107 / 108 COUNTRY KIT prev / next, 110 / 111 / 112 + 3k MY LOOKS slot k's WEAR / SAVE / CLEAR
        public const int HitPresetPrev = 100, HitPresetNext = 101, HitRandom = 102, HitSave = 103, HitCountryPrev = 104,
            HitCountryNext = 105, HitCancel = 106, HitKitPrev = 107, HitKitNext = 108, HitLook = 110;
        // MY LOOKS (TASKS.md E45, native 10668f0 / ddd18a1; the owner, 2026-11-20: "we also need to save costume slots up to 3";
        // "how do you clear a slot?"): under the left panel's rows, three saved looks, each with WEAR (it goes on as a preset
        // does: SAVE AND APPLY keeps it, CANCEL goes back), SAVE (the look you have on into that slot; a filled slot asks
        // SURE? first) and CLEAR (the slot EMPTY again, after SURE?). Kept in looks.cfg at once, whatever the builder closes with
        public const float LookRowH = 0.085f, LookLabelUp = 0.037f, LookBtnX = 0.145f, LookBtnW = 0.13f, LookBtnH = 0.04f;
        public float LooksY => RowY(LeftRows().Length) + 0.02f;   // (its heading)
        public float LookRowY(int k) => LooksY - 0.08f - k * LookRowH;
        public int lookConfirm = -1;   // the SAVE or CLEAR (its hit code) asking SURE? (a second press does it)
        public double lookConfirmUntil;
        public List<int> nationOrder;  // the COUNTRY order (TASKS.md E44): worked out when the builder opens

        public bool open;
        public Pose pose = Pose.Identity, poseL = Pose.Identity;   // its panels: the parts and colours right of the mirror, the whole look left of it
        public Vec3 mirrorPoint, mirrorNormal = new Vec3(0, 0, 1);
        float mirrorArmsLogged;   // (the mirror's ARM SIZING as last written to the log)
        public int hover = -1, presetIdx, lastNation;   // (lastNation: COUNTRY FLAG's first, the last one chosen; 0: the top of the COUNTRY order)
        public bool hitValid;
        public Vec3 hit;
        public string notice = "";   // (native noticeLocal: LOOK SAVED / CHANGES CANCELLED)
        public float noticeUntil;
        AvatarDesc start;
        bool prevTrig = true;

        readonly AppState a;
        public Builder(AppState a) => this.a = a;

        AvatarDesc Look { get => a.settings.myAvatar; set => a.settings.myAvatar = value; }

        public static float ButtonsY => RowY(7) - 0.095f;
        public int[] LeftRows()
        {
            var o = new System.Collections.Generic.List<int> { LeftPreset };
            if (Avatar.NationOf(Look) != 0) { o.Add(LeftCountry); o.Add(LeftKit); }
            o.Add((int)Avatar.Field.Body);
            o.Add((int)Avatar.Field.Build);
            return o.ToArray();
        }
        public float BottomL => LookRowY(2) - 0.055f;
        static int LeftCode(int item) => item == LeftPreset ? HitPresetPrev : item == LeftCountry ? HitCountryPrev : item == LeftKit ? HitKitPrev : item * 2;

        // the PRESET row: the presets, then COUNTRY FLAG (the team kit of a country, chosen in the row below)
        static int PresetSlots => Avatar.PresetCount() + 1;
        public int PresetSlot => ((presetIdx % PresetSlots) + PresetSlots) % PresetSlots;
        public string PresetLabel => PresetSlot == Avatar.PresetCount() ? "COUNTRY FLAG" : Avatar.PresetName(PresetSlot);

        static int ArrowHit(float lx, float ly, float cx, float y, int code)
        {
            if (Mathf.Abs(ly - y) > 0.026f) return -1;
            if (Mathf.Abs(lx - (cx - Arrow)) < 0.03f) return code;
            if (Mathf.Abs(lx - (cx + Arrow)) < 0.03f) return code + 1;
            return -1;
        }

        // panel 0: right of the mirror (the parts and colours, the buttons); 1: left of it (the whole look)
        public int HitTest(int panel, float lx, float ly)
        {
            if (panel == 1)
            {
                int[] items = LeftRows();
                for (int i = 0; i < items.Length; ++i) { int h = ArrowHit(lx, ly, 0, RowY(i), LeftCode(items[i])); if (h >= 0) return h; }
                for (int k = 0; k < 3; ++k)
                {   // MY LOOKS (an empty slot has nothing to wear or clear)
                    if (Mathf.Abs(ly - LookRowY(k)) > LookBtnH * 0.5f + 0.004f) continue;
                    bool saved = a.settings.myLookSaved[k];
                    if (Mathf.Abs(lx + LookBtnX) < LookBtnW * 0.5f) return saved ? HitLook + 3 * k : -1;
                    if (Mathf.Abs(lx) < LookBtnW * 0.5f) return HitLook + 1 + 3 * k;
                    if (Mathf.Abs(lx - LookBtnX) < LookBtnW * 0.5f) return saved ? HitLook + 2 + 3 * k : -1;
                }
                return -1;
            }
            foreach (var r in Rows) { int h = ArrowHit(lx, ly, ColX[r.col], RowY(r.row), (int)r.f * 2); if (h >= 0) return h; }
            float yb = ButtonsY;
            if (Mathf.Abs(ly - yb) < 0.032f)
            {
                if (Mathf.Abs(lx + 0.24f) < 0.105f) return HitRandom;
                if (Mathf.Abs(lx) < 0.105f) return HitCancel;
                if (Mathf.Abs(lx - 0.24f) < 0.12f) return HitSave;
            }
            return -1;
        }

        // the panel coordinates of a hit code's control, and its panel (tests and editor pictures point at them)
        public Vector2 ControlAt(int hitCode, out int panel)
        {
            panel = 0;
            switch (hitCode)
            {
                case HitRandom: return new Vector2(-0.24f, ButtonsY);
                case HitCancel: return new Vector2(0, ButtonsY);
                case HitSave: return new Vector2(0.24f, ButtonsY);
            }
            float side = (hitCode & 1) != 0 && hitCode < 100 || hitCode == HitPresetNext || hitCode == HitCountryNext || hitCode == HitKitNext ? Arrow : -Arrow;
            int code = hitCode < 100 ? hitCode & ~1 : hitCode == HitPresetNext ? HitPresetPrev : hitCode == HitCountryNext ? HitCountryPrev : hitCode == HitKitNext ? HitKitPrev : hitCode;
            foreach (var r in Rows) if ((int)r.f * 2 == code) return new Vector2(ColX[r.col] + side, RowY(r.row));
            int[] items = LeftRows();
            for (int i = 0; i < items.Length; ++i) if (LeftCode(items[i]) == code) { panel = 1; return new Vector2(side, RowY(i)); }
            if (hitCode >= HitLook && hitCode < HitLook + 9)
            {   // MY LOOKS: WEAR, SAVE, CLEAR across each slot's row
                panel = 1;
                int k = (hitCode - HitLook) / 3, b = (hitCode - HitLook) % 3;
                return new Vector2((b - 1) * LookBtnX, LookRowY(k));
            }
            return new Vector2(9, 9);   // (not shown)
        }
        public Vector2 ControlAt(int hitCode) => ControlAt(hitCode, out _);
        // ... where it is in the world
        public Vec3 ControlPoint(int hitCode)
        {
            Vector2 c = ControlAt(hitCode, out int panel);
            return (panel == 1 ? poseL : pose).Transform(new Vec3(c.x, c.y, 0));
        }

        // the builder's mirror (0.85 m ahead, on the floor you stand on) and its panel, in front of the head; again when the
        // view recenters while it's open (native 7ed11aa; the owner, 2026-11-17: opened at load-in, before the first
        // recenter, the mirror faced another way and stayed so)
        public void Place(Pose head)
        {
            Vec3 f = head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(1, 0, 0);
            Vec3 right = M.Cross(f, new Vec3(0, 1, 0));
            mirrorNormal = new Vec3(0, 0, 0) - f;
            // (on the floor you stand on: on the balcony its glass and ring were drawn on the court, 2.5 m below; a Quest
            // 3, 2026-11-14)
            mirrorPoint = new Vec3(head.p.x, a.InLobby ? Lobby.Floor : 0f, head.p.z) + f * 0.85f;
            Pose Facing(float side)
            {   // a panel beside the mirror, turned to the head
                Vec3 pp = head.p + f * 0.95f + right * side + new Vec3(0, -0.30f, 0);
                Vec3 to = head.p - pp;
                to.y = 0;
                to = M.Normalize(to);
                return new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(to.x, to.z)), pp);
            }
            pose = Facing(0.66f);
            poseL = Facing(-0.64f);
        }

        public void Open(PlayerInput @in)
        {
            Place(@in.head);
            open = a.builderOpen = true;
            start = Look;
            prevTrig = true;
            presetIdx = Avatar.NationOf(Look) != 0 ? Avatar.PresetCount() : 0;   // (wearing a country: COUNTRY FLAG)
            nationOrder = Avatar.NationOrder(a.settings.nationPicks);   // (fixed while it's open: the picks change when it closes)
            lookConfirm = -1;
            Debug.Log("[Builder] open");
        }

        public void Close()
        {
            if (!open) return;
            open = a.builderOpen = false;
            a.settings.SaveAvatar();
            // a country you leave with, other than the one you came in with, counts as chosen once more (TASKS.md E44)
            int n = Avatar.NationOf(Look);
            if (n >= 1 && n <= Avatar.NationCount() && n != Avatar.NationOf(start))
            {
                ++a.settings.nationPicks[n];
                a.settings.SaveLooks();
                Debug.Log($"[Builder] {Avatar.NationAt(n).code} chosen {a.settings.nationPicks[n]} times");
            }
            Debug.Log("[Builder] closed");
        }

        void Notice(string s) { notice = s; noticeUntil = Time.time + 3f; Debug.Log("[Builder] " + s); }

        public void Activate(int code)
        {
            AvatarDesc d = Look;
            nationOrder ??= Avatar.NationOrder(a.settings.nationPicks);
            List<int> order = nationOrder;
            int confirm = lookConfirm != -1 && Time.realtimeSinceStartupAsDouble < lookConfirmUntil ? lookConfirm : -1;
            lookConfirm = -1;   // (any other press: SURE? goes back to SAVE or CLEAR)
            if (code >= 0 && code < 100)
            {
                var f = (Avatar.Field)(code / 2);
                Avatar.Set(ref d, f, Avatar.Get(d, f) + ((code & 1) != 0 ? 1 : -1));
                Debug.Log($"[Builder] {Avatar.FieldName(f)} -> {Avatar.OptionName(f, Avatar.Get(d, f))}");
            }
            else if (code == HitPresetPrev || code == HitPresetNext)
            {
                presetIdx = PresetSlot + (code == HitPresetNext ? 1 : -1);
                if (PresetSlot == Avatar.PresetCount())   // COUNTRY FLAG: the look stays, the country's kit goes on
                {   // (the last country chosen this time, else the top of the COUNTRY order: your most chosen, or A-Z)
                    bool last = lastNation >= 1 && lastNation <= Avatar.NationCount();
                    Avatar.Set(ref d, Avatar.Field.Nation, last ? lastNation : order[0]);
                }
                else d = Avatar.PresetLook(PresetSlot);
                Debug.Log("[Builder] preset " + PresetLabel);
            }
            else if (code == HitCountryPrev || code == HitCountryNext)
            {   // the country: a step along the COUNTRY order (wrapping; TASKS.md E44)
                int m = order.Count;
                int i = order.IndexOf(Avatar.NationOf(d));
                i = i < 0 ? 0 : (i + (code == HitCountryNext ? 1 : m - 1)) % m;
                int v = order[i];
                Avatar.SetNation(ref d, v);
                lastNation = v;
                Debug.Log("[Builder] country " + Avatar.NationAt(v).name);
            }
            else if (code == HitKitPrev || code == HitKitNext)
            {   // COUNTRY KIT (two choices: either arrow swaps them)
                Avatar.SetOwnKit(ref d, !Avatar.OwnKit(d));
                Debug.Log("[Builder] country kit " + (Avatar.OwnKit(d) ? "MY OUTFIT" : "TEAM JERSEY"));
            }
            else if (code >= HitLook && code < HitLook + 9)
            {   // MY LOOKS (TASKS.md E45)
                int k = (code - HitLook) / 3, act = (code - HitLook) % 3;
                var s = a.settings;
                if (act == 0)
                {   // WEAR: the saved look goes on (SAVE AND APPLY keeps it, CANCEL goes back)
                    if (!s.myLookSaved[k]) return;
                    d = s.myLooks[k];
                    int nn = Avatar.NationOf(d);
                    if (nn != 0) { presetIdx = Avatar.PresetCount(); lastNation = nn; }
                    else if (PresetSlot == Avatar.PresetCount()) presetIdx = 0;
                    Debug.Log($"[Builder] wearing LOOK {k + 1}");
                }
                else if (s.myLookSaved[k] && confirm != code)
                {   // SAVE over a filled slot, or CLEAR: SURE? for 3 s first
                    lookConfirm = code;
                    lookConfirmUntil = Time.realtimeSinceStartupAsDouble + 3.0;
                }
                else if (act == 2)
                {   // CLEAR, sure: the slot EMPTY again (the look you have on stays on)
                    s.myLookSaved[k] = false;
                    s.myLooks[k] = AvatarDesc.Default;
                    s.SaveLooks();
                    Notice($"LOOK {k + 1} CLEARED");
                }
                else
                {
                    s.myLooks[k] = d;
                    s.myLookSaved[k] = true;
                    s.SaveLooks();
                    Notice($"SAVED AS LOOK {k + 1}");
                }
            }
            else if (code == HitRandom) d = Avatar.RandomLook((uint)(long)(Time.realtimeSinceStartupAsDouble * 1000.0 + DateTime.Now.Ticks / 10000));
            Look = d;
            if (code == HitSave) { Close(); Notice("LOOK SAVED"); }
            else if (code == HitCancel) { Look = start; Close(); Notice("CHANGES CANCELLED"); }
        }

        // pointer: the pointer hand's aim ray against the panel; a trigger click activates. Returns true on a click (the
        // caller buzzes the hand). No grabbing while customizing.
        public bool Update(PlayerInput @in, Pose aim, bool aimValid)
        {
            hover = -1;
            hitValid = false;
            if (!open) return false;
            bool clicked = false;
            bool trig = @in.hand[a.settings.pointerHand].trigger > 0.7f;
            if (aimValid)
            {
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                float best = 4;
                for (int panel = 0; panel < 2; ++panel)
                {   // (the nearer panel the ray meets inside its edges)
                    Pose P = panel != 0 ? poseL : pose;
                    Vec3 nz = P.q.Rotate(new Vec3(0, 0, 1));
                    float den = M.Dot(d, nz);
                    if (den > -1e-3f) continue;
                    float t = M.Dot(P.p - o, nz) / den;
                    if (t <= 0 || t >= best) continue;
                    Vec3 p = o + d * t;
                    Vec3 lp = P.q.Conj().Rotate(p - P.p);
                    float halfW = panel != 0 ? HalfWL : HalfW, bottom = panel != 0 ? BottomL : ButtonsY - 0.06f;
                    if (Mathf.Abs(lp.x) > halfW + 0.01f || lp.y > Top || lp.y < bottom) continue;
                    best = t;
                    hit = p;
                    hitValid = true;
                    hover = HitTest(panel, lp.x, lp.y);
                }
                if (trig && !prevTrig && hover >= 0) { Activate(hover); clicked = true; }
            }
            prevTrig = trig;
            for (int h = 0; h < 2; ++h) { @in.hand[h].trigger = 0; @in.hand[h].squeeze = 0; }
            return clicked;
        }

        static Vector4 V(float r, float g, float b, float al = 1) => new Vector4(r, g, b, al);

        // reflection through the plane at P with unit normal n (native reflectMat; native space, column vectors)
        public static Matrix4x4 Reflect(Vec3 P, Vec3 n)
        {
            var r = Matrix4x4.identity;
            float[] nv = { n.x, n.y, n.z };
            for (int c = 0; c < 3; ++c)
                for (int rr = 0; rr < 3; ++rr) r[rr, c] = (c == rr ? 1f : 0f) - 2f * nv[rr] * nv[c];
            float d = 2f * M.Dot(P, n);
            r[0, 3] = d * n.x; r[1, 3] = d * n.y; r[2, 3] = d * n.z;
            return r;
        }

        static Mat4 ToMat4(Matrix4x4 n)
        {
            var r = new Mat4();
            for (int c = 0; c < 4; ++c)
                for (int rr = 0; rr < 4; ++rr) r.m[c * 4 + rr] = n[rr, c];
            return r;
        }

        // a player's body and gauntlets placed by `pre` (native space)
        static void DrawPlaced(AvatarDraw av, PlayerInput @in, AvatarDraw.Look look, int team, Matrix4x4 pre, int swingKey)
        {
            @in = av.Apart(@in, @in.avatar);   // (HANDS APART: the two gauntlets drawn just apart)
            av.DrawBody(@in, look, team, ToMat4(pre), swingKey: swingKey);
            for (int h = 0; h < 2; ++h)
            {
                if (!@in.hand[h].tracked) continue;
                Matrix4x4 grip = HandPlacement.FromPose(@in.hand[h].grip);
                av.DrawGauntlet(@in.avatar, pre * (h == 0 ? grip * HandPlacement.Mirror : grip), look, @in.hand[h].trigger, h);
            }
        }

        // the mirror: your reflection, moving with you, and the glass (native drawMirror)
        AvatarDraw.Look DrawMirror(FxRenderer fx, AvatarDraw av, PlayerInput mine, float[][] sideRGB)
        {
            var @in = mine.Clone();
            @in.avatar = Look;
            AvatarDraw.Look look = av.LookOf(@in.avatar, 0, sideRGB);
            DrawPlaced(av, @in, look, 0, Reflect(mirrorPoint, mirrorNormal), 200);   // (its own rig, key 200: native 8a68443's fix)
            {   // (the arms' sizing in the log as it changes: what a headset test did, not a guess from its video)
                float s = av.RigStateOf(200).armScale;
                if (MathF.Abs(s - mirrorArmsLogged) >= 0.04f) { Debug.Log($"[Builder] arms x{s:F2}"); mirrorArmsLogged = s; }
            }
            Vec3 n = mirrorNormal;
            var mq = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(n.x, n.z));   // quad +Z = n (toward the player)
            Vec3 mc = mirrorPoint + new Vec3(0, 1.05f, 0);
            fx.Panel(new Pose(mq, mc), 1.2f, 2.1f, V(0.02f, 0.05f, 0.08f, 0.10f));
            Vector4 edge = V(look.rim.x * 0.8f, look.rim.y * 0.8f, look.rim.z * 0.8f);
            Vec3 side = mq.Rotate(new Vec3(1, 0, 0));
            fx.Box(new Pose(mq, mc + side * 0.6f), new Vec3(0.004f, 1.05f, 0.004f), edge);
            fx.Box(new Pose(mq, mc - side * 0.6f), new Vec3(0.004f, 1.05f, 0.004f), edge);
            fx.Box(new Pose(mq, mc + new Vec3(0, 1.05f, 0)), new Vec3(0.6f, 0.004f, 0.004f), edge);
            var foot = new Vec3(@in.head.p.x, mirrorPoint.y + 0.004f, @in.head.p.z);
            Vec3 rf = foot - n * (2f * M.Dot(foot - mirrorPoint, n));
            fx.SpriteFlat(new Vec3(rf.x, mirrorPoint.y + 0.006f, rf.z), 1.1f, V(look.rim.x * 0.5f, look.rim.y * 0.5f, look.rim.z * 0.5f), new Vec3(0, 1, 0), 1);
            return look;
        }

        static readonly float[] kTeam0 = { 0.1f, 0.7f, 1.0f };   // native kTeamRGB[0]: the swatches' TEAM colour

        public void Draw(FxRenderer fx, AvatarDraw av, PlayerInput mine, float[][] sideRGB, Pose aim, bool aimValid)
        {
            if (!open) return;
            AvatarDraw.Look look = DrawMirror(fx, av, mine, sideRGB);
            Vector4 edge = V(look.rim.x * 0.8f, look.rim.y * 0.8f, look.rim.z * 0.8f);
            Pose P = pose;   // (the panel drawn on: the right one's parts and colours, the left one's whole look)
            Vec3 At(float x, float y, float z) => P.Transform(new Vec3(x, y, z));
            void Quad(float x, float y, float w, float h, float z, Vector4 c) => fx.Panel(new Pose(P.q, At(x, y, z)), w, h, c);
            void Text(string t, float x, float y, float h, Vector4 c) => fx.Text(t, new Pose(P.q, At(x, y, 0.006f)), h, c);
            Vector4 bg = V(0.004f, 0.007f, 0.016f, 0.90f), frame = V(edge.x * 0.6f, edge.y * 0.6f, edge.z * 0.6f, 0.9f);
            Vector4 btn = V(0.03f, 0.05f, 0.08f, 0.95f), hov = V(0.05f, 0.40f, 0.75f, 0.98f);
            Vector4 white = V(1, 1, 1), grey = V(0.45f, 0.5f, 0.56f), cyan = V(0.3f, 0.85f, 1.0f);
            void Board(float halfW, float bottom)
            {
                Quad(0, (Top + bottom) * 0.5f, 2 * halfW + 0.03f, Top - bottom + 0.03f, -0.002f, frame);
                Quad(0, (Top + bottom) * 0.5f, 2 * halfW, Top - bottom, 0, bg);
            }
            // one row: its name over < VALUE > (a long value made smaller to fit between the arrows)
            void Row(float cx, float y, string label, string value, Vector4 vc, int code)
            {
                Text(label, cx, y + LabelUp, 0.02f, grey);
                Quad(cx - Arrow, y, 0.05f, 0.044f, 0.003f, hover == code ? hov : btn);
                Quad(cx + Arrow, y, 0.05f, 0.044f, 0.003f, hover == code + 1 ? hov : btn);
                Text("<", cx - Arrow, y, 0.028f, white);
                Text(">", cx + Arrow, y, 0.028f, white);
                Text(value, cx, y, Mathf.Min(0.024f, ValueW / (0.75f * Mathf.Max(1, value.Length))), vc);
            }
            AvatarDesc d = Look;
            int nat = Avatar.NationOf(d);
            bool own = Avatar.OwnKit(d);
            var c3 = new float[3];
            Board(HalfW, ButtonsY - 0.06f);
            Text("CUSTOMIZE AVATAR", 0, 0.455f, 0.042f, cyan);
            Text("HEAD TO TOE", ColX[0], 0.395f, 0.02f, cyan);
            Text("COLORS AND PATTERN", ColX[1], 0.395f, 0.02f, cyan);
            foreach (var r in Rows)
            {
                Avatar.Field fld = r.f;
                float cx = ColX[r.col], y = RowY(r.row);
                // (the country decides these: its jersey, or with MY OUTFIT only the colours)
                bool colours = fld == Avatar.Field.Primary || fld == Avatar.Field.Secondary || fld == Avatar.Field.Glow;
                bool kit = nat != 0 && (colours || (!own && (fld == Avatar.Field.Suit || fld == Avatar.Field.Pattern || fld == Avatar.Field.Lower)));
                Row(cx, y, Avatar.FieldName(fld), kit ? (own ? "FLAG COLORS" : "TEAM KIT") : Avatar.OptionName(fld, Avatar.Get(d, fld)), kit ? grey : white, (int)fld * 2);
                if (!kit && Avatar.IsColor(fld))
                {   // (its swatch beside the name)
                    Avatar.OptionColor(fld, Avatar.Get(d, fld), kTeam0, c3);
                    Quad(cx + 0.15f, y + LabelUp, 0.026f, 0.026f, 0.003f, V(c3[0], c3[1], c3[2]));
                }
            }
            {   // left of the mirror: the whole look
                P = poseL;
                Board(HalfWL, BottomL);
                {   // MY LOOKS (TASKS.md E45): each slot LOOK N and what it is (its country, else its suit), WEAR, SAVE and CLEAR
                    var st = a.settings;
                    Text("MY LOOKS", 0, LooksY, 0.024f, cyan);
                    bool sureLive = lookConfirm != -1 && Time.realtimeSinceStartupAsDouble < lookConfirmUntil;
                    for (int k = 0; k < 3; ++k)
                    {
                        float y = LookRowY(k);
                        string label = $"LOOK {k + 1}: ";
                        Avatar.Nation N = st.myLookSaved[k] ? Avatar.NationAt(Avatar.NationOf(st.myLooks[k])) : null;
                        label += !st.myLookSaved[k] ? "EMPTY" : N != null ? N.name : Avatar.OptionName(Avatar.Field.Suit, st.myLooks[k].suit);
                        Text(label, 0, y + LookLabelUp, Mathf.Min(0.02f, 0.42f / (0.75f * label.Length)), grey);
                        bool sureSave = sureLive && lookConfirm == HitLook + 1 + 3 * k, sureClear = sureLive && lookConfirm == HitLook + 2 + 3 * k;
                        for (int b = 0; b < 3; ++b) Quad((b - 1) * LookBtnX, y, LookBtnW, LookBtnH, 0.003f, hover == HitLook + 3 * k + b ? hov : btn);
                        Text("WEAR", -LookBtnX, y, 0.021f, st.myLookSaved[k] ? white : grey);
                        Text(sureSave ? "SURE?" : "SAVE", 0, y, 0.021f, sureSave ? cyan : white);
                        Text(sureClear ? "SURE?" : "CLEAR", LookBtnX, y, 0.021f, sureClear ? cyan : st.myLookSaved[k] ? white : grey);
                    }
                }
                Text("PRESET AND BODY", 0, 0.455f, 0.03f, cyan);
                int[] items = LeftRows();
                for (int i = 0; i < items.Length; ++i)
                {
                    float y = RowY(i);
                    int code = LeftCode(items[i]);
                    if (items[i] == LeftPreset) Row(0, y, "PRESET", PresetLabel, white, code);
                    else if (items[i] == LeftCountry)
                    {   // PRESET > COUNTRY FLAG: the country, with its flag
                        Row(0, y, "COUNTRY", Avatar.NationAt(nat).name, white, code);
                        av.DrawFlag(nat, new Pose(P.q, At(0.15f, y + LabelUp, 0.004f)), 0.04f);
                    }
                    else if (items[i] == LeftKit) Row(0, y, "COUNTRY KIT", own ? "MY OUTFIT" : "TEAM JERSEY", white, code);   // (the team jersey, or your own outfit in its colours)
                    else
                    {
                        var fld = (Avatar.Field)items[i];
                        Row(0, y, Avatar.FieldName(fld), Avatar.OptionName(fld, Avatar.Get(d, fld)), white, code);
                    }
                }
                P = pose;
            }
            float yb = ButtonsY;
            Quad(-0.24f, yb, 0.20f, 0.06f, 0.003f, hover == HitRandom ? hov : btn);
            Quad(0.0f, yb, 0.20f, 0.06f, 0.003f, hover == HitCancel ? hov : btn);
            Quad(0.24f, yb, 0.24f, 0.06f, 0.003f, hover == HitSave ? hov : btn);
            Text("RANDOM", -0.24f, yb, 0.026f, white);
            Text("CANCEL", 0.0f, yb, 0.026f, white);
            Text("SAVE AND APPLY", 0.24f, yb, 0.019f, white);
            if (aimValid)
            {   // laser + cursor
                Vec3 o = aim.p, dd = aim.q.Rotate(new Vec3(0, 0, -1));
                float len = hitValid ? M.Length(hit - o) : 1.5f;
                fx.Box(new Pose(aim.q, o + dd * (len * 0.5f)), new Vec3(0.003f, 0.003f, len * 0.5f), V(0.15f, 0.6f, 1.0f));
                if (hitValid) fx.Glow(hit, 0.008f, cyan);
            }
        }

        // after SAVE AND APPLY / CANCEL: the notice a moment in front of you (native noticeLocal)
        public void DrawNotice(FxRenderer fx, Pose head)
        {
            if (open || notice.Length == 0 || Time.time > noticeUntil) return;
            Vec3 f = head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(1, 0, 0);
            var q = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z));
            Vec3 p = head.p + f * 1.2f + new Vec3(0, -0.25f, 0);
            fx.Panel(new Pose(q, p), 0.06f + 0.035f * 0.75f * notice.Length, 0.07f, V(0.004f, 0.007f, 0.016f, 0.9f));
            fx.Text(notice, new Pose(q, p + q.Rotate(new Vec3(0, 0, 0.006f))), 0.035f, V(0.3f, 0.85f, 1.0f));
        }

        // the title page: your avatar, a 60% figure on a slowly turning hologram disc to the right of the menu, your name
        // above it (native drawTitleShowcase; 1159a46: in the menu's frame, Menu.Showcase*, scaled about its disc, which
        // the body stands on, 0.62 m up at full size; no ring on the floor)
        public static void DrawShowcase(FxRenderer fx, AvatarDraw av, AppState a, Menu menu, float[][] sideRGB, double now)
        {
            if (!menu.open || !menu.titleOpen || a.builderOpen || menu.helpOpen || menu.nameEdit || menu.settingsOpen || av.PartCount == 0) return;
            Vec3 disc = menu.pose.Transform(new Vec3(Menu.ShowcaseX, Menu.ShowcaseDiscY, Menu.ShowcaseZ));
            Vec3 bas = disc - new Vec3(0, 0.62f, 0);   // (its feet, at full size)
            float sc = Menu.ShowcaseScale;
            var dv = new Vector3(disc.x, disc.y, disc.z);
            Matrix4x4 pre = Matrix4x4.TRS(dv, Quaternion.identity, new Vector3(sc, sc, sc)) * Matrix4x4.Translate(-dv);
            Vec3 toUser = a.viewHead.p - disc;
            toUser.y = 0;
            toUser = M.Length(toUser) > 1e-3f ? M.Normalize(toUser) : new Vec3(0, 0, 1);
            float face = MathF.Atan2(-toUser.x, -toUser.z);   // the avatar looks at the player
            float yaw = face + 0.55f * MathF.Sin((float)now * 0.35f);   // turns side to side
            var q = Quat.AxisAngle(new Vec3(0, 1, 0), yaw);
            var @in = new PlayerInput { headTracked = true, head = new Pose(q, bas + new Vec3(0, 1.62f, 0)), avatar = a.settings.myAvatar };
            for (int h = 0; h < 2; ++h)
            {   // arms relaxed, fists a little forward
                @in.hand[h].tracked = true;
                float s = h != 0 ? 1f : -1f;
                @in.hand[h].grip = new Pose(q * Quat.AxisAngle(new Vec3(1, 0, 0), -1.05f) * Quat.AxisAngle(new Vec3(0, 0, 1), s * 0.25f),
                                            @in.head.p + q.Rotate(new Vec3(s * 0.24f, -0.80f, -0.10f)));
            }
            AvatarDraw.Look look = av.LookOf(@in.avatar, 0, sideRGB);
            DrawPlaced(av, @in, look, 0, pre, 100);   // (its own ponytail chain)
            // its hologram disc (the bodies end above the knee) and a beam down to the floor (no ring on the floor: the
            // owner, 2026-11-21, "the lowest ring projection under the avatar seems unnecessary")
            var ground = new Vec3(disc.x, a.lobby ? Lobby.Floor : 0f, disc.z);
            fx.SpriteFlat(disc, 0.75f * sc, V(0.10f, 0.55f, 0.90f), new Vec3(0, 1, 0), 1);
            Vec3[] pts = { ground + new Vec3(0, 0.02f, 0), disc - new Vec3(0, 0.02f, 0) };
            fx.Ribbon(pts, 2, 0.32f * sc, V(0.06f, 0.30f, 0.55f, 0.5f), a.viewHead.p);
            var lq = Quat.AxisAngle(new Vec3(0, 1, 0), face + M.kPi);   // text quads face +Z
            fx.Text(a.settings.name, new Pose(lq, disc + new Vec3(0, (1.90f - 0.62f) * sc, 0)), 0.055f, V(1, 1, 1));
        }
    }
}
