// Project Arena avatars (native app/src/avatar.h, avatar.cpp, native commit 0eea0e7), ported 1:1: option tables and
// colour palettes, countries, presets and random looks, avatar.cfg text, and the body rig (torso, neck and two-bone IK
// arms solved from the tracked head and hands) with the skin matrices of the skinned parts. Platform independent;
// tested by Tests/EditMode/AvatarTests.cs (the native avatar checks). Drawing comes with phase 4 step C.
// Avatar parts are separate models (the native tools/blender/make_avatar.py):
//   head space   origin = between the eyes (the HMD pose), +X right, +Y up, -Z forward: av_head, av_hair_N,
//                av_gear_N, av_visor_N, av_mask_N
//   torso space  origin = neck base, yaw-only (upright), -Z forward: av_torso_N (scaled by the build)
//   bone space   joint at the origin, the bone runs down -Y for its natural length, front = -Z, outside = +X
//                (right side; the left side is drawn mirrored): av_upper_N, av_fore_N, av_neck
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena
{
    public static partial class Avatar
    {
        public enum Channel { None = 0, Primary, Secondary, Glow, Skin, Visor, Hair, Trim, Count }

        // builder rows, in display order
        public enum Field { Suit, Build, Pattern, Primary, Secondary, Glow, Hair, HairColor, Headgear, Visor, VisorTint, Mask, Skin, Lower, Body,
                            Gauntlet, Face, Nation, Count }

        // FACE shares AvatarDesc.body (sent raw, no spare byte): bit 0 = the body type, bits 1-2 = the face. Older builds
        // wrap body % 2, so they still see the right body type (with its first face).
        public const int kFaces = 4;
        public static int BodyType(AvatarDesc d) => d.body & 1;
        public static int FaceOf(AvatarDesc d) => ((d.body >> 1) & 3) % kFaces;

        // NATION (country presets: the team jersey in the flag's colours, that end of the court, flags on the banners).
        // The country number (1.., 0 = none; native nations.inc, append-only) rides in spare bits sent with the avatar:
        // its low 5 bits in body bits 3-7, its high 3 bits in gauntlet bits 5-7 (GAUNTLET itself uses bits 0-4). Older
        // builds (0.3.5 and before) don't know it: they show such a player with FACE 1 and, for countries above 31, the
        // first gauntlet.
        public sealed class Nation
        {
            public readonly string code, name, flag;   // "USA", "UNITED STATES", texture "flag_usa" (192 x 128)
            public readonly byte[][] kit;              // jersey: main, trim, accent (sRGB)
            public readonly float[][] team;            // the glow colour for that end of the court, and a second choice (linear, brightest 1)

            public Nation(string code, string name, string flag, byte[] kit9, float[] team6)
            {
                this.code = code; this.name = name; this.flag = flag;
                kit = new[] { new[] { kit9[0], kit9[1], kit9[2] }, new[] { kit9[3], kit9[4], kit9[5] }, new[] { kit9[6], kit9[7], kit9[8] } };
                team = new[] { new[] { team6[0], team6[1], team6[2] }, new[] { team6[3], team6[4], team6[5] } };
            }
        }

        public static int NationOf(AvatarDesc d) => ((d.body >> 3) & 31) | ((d.gauntlet >> 5) << 5);
        public static int GauntletOf(AvatarDesc d) => d.gauntlet & 31;
        // GAUNTLET: 0-2 the plate gauntlets (STRIKER, TITAN, PHANTOM), 3 the CONTROLLER (the lancer in a glove), 4-6 the
        // grapple gauntlets (GRAPPLER, ARC CLAW, REGALIA: plate gauntlets whose fingers open on the trigger; TASKS.md E35,
        // native 27cad43, PR #29)
        public const int kGauntletStyles = 7;
        public static bool PlateGauntlet(int k) => k != 3 && k >= 0 && k < kGauntletStyles;
        public static bool GrappleGauntlet(int k) => k >= 4 && k < kGauntletStyles;
        // SUIT 18 PELURU LAYERED DRESS (PR #30; TASKS.md E35, native 7e4ddee): its skirt takes the LOWER BODY's place, and it
        // has hoop earrings (av_top_18e)
        public const int kSuitDress = 18;

        struct Swatch { public string name; public byte r, g, b; public Swatch(string n, byte r, byte g, byte b) { name = n; this.r = r; this.g = g; this.b = b; } }   // sRGB

        static readonly string[] kSuit = { "GRID RUNNER", "ARENA JERSEY", "VANGUARD", "STREET JACKET", "HOODIE", "ECHO SUIT", "TANK TOP", "PILOT",
                                           "CIRCUIT VEST", "TRAINING TEE", "CIRCUIT RUNNER",   // 10: modelled traces with moving pulses
                                           "GRID RUNNER LIVE", "ECHO SUIT LIVE", "CIRCUIT VEST LIVE",   // 11-13: their glow lines raised, pulsing
                                           "VECTOR", "NIGHTFORGE",    // 14, 15: PR #25 (their own LOWER BODY 1-4; older versions: GRID RUNNER)
                                           "RONIN WRAP",              // 16: PR #27, 0.3.22 (TASKS.md E35; older versions: GRID RUNNER)
                                           "PELURU TUXEDO", "PELURU LAYERED DRESS" };   // 17, 18: PR #30, 0.3.22 (native build_peluru.py;
                                                                                        // the dress its own skirt; older: GRID RUNNER)
        static readonly string[] kLower = { "NONE", "ATHLETIC SHORTS", "CIRCUIT SHORTS", "BIKE SHORTS",
                                            "LONG SHORTS", "TENNIS SKIRT", "KILT" };   // 4-6: PR #25 (the skirts sway; older versions: NONE)
        static readonly string[] kBody = { "MASCULINE", "FEMININE" };
        static readonly string[] kGauntlet = { "STRIKER", "TITAN", "PHANTOM", "CONTROLLER",
                                               "GRAPPLER", "ARC CLAW", "REGALIA" };   // 4-6: the trigger opens them (older versions: STRIKER)
        static readonly string[] kFace = { "FACE 1", "FACE 2", "FACE 3", "FACE 4" };   // native make_body.py FACES (FACE 1 = the base)
        static readonly string[] kBuild = { "SLIM", "STANDARD", "BROAD", "ATHLETIC" };
        static readonly string[] kPattern = { "NONE", "CIRCUIT", "STRIPES", "CHEVRON", "HEX", "SPLIT", "PIXEL", "BANDS" };
        static readonly string[] kPatternTex = { null, "open_pat_circuit", "open_pat_stripes", "open_pat_chevron", "open_pat_hex",
                                                 "open_pat_split", "open_pat_pixel", "open_pat_bands" };
        static readonly string[] kHair = { "NONE", "BUZZ CUT", "SWEPT", "PONYTAIL", "MOHAWK", "SPIKES", "BOB", "TOP KNOT", "TOUSLED",
                                           "HIGH PIGTAILS", "DREADLOCKS" };   // 9, 10: their tails and locs swing (PR #29; older versions: NONE)
        static readonly string[] kHeadgear = { "NONE", "GRID HELMET", "RACER HELMET", "HOOD", "CREST HELMET", "HEADPHONES", "CAP",
                                               "HORNED HELMET", "VECTOR HELMET",   // 7, 8: PR #25 (native e21f83e; older versions: NONE)
                                               "RONIN KABUTO",                     // 9: PR #27, 0.3.22 (TASKS.md E35; older: NONE)
                                               "SOMBRERO" };                       // 10: PR #30, 0.3.22 (native build_peluru.py; older: NONE)
        static readonly string[] kVisor = { "NONE", "BAND VISOR", "FULL SHIELD", "SHADES", "GOGGLES", "HEADSET", "HALO", "SLIT VISOR" };
        static readonly string[] kMask = { "NONE", "MOUTH GUARD", "CYBER JAW", "BREATHER", "BANDANA", "FACEPLATE",
                                           "SUGAR SKULL" };   // 6: PR #30, 0.3.22 (a mask on each face; older versions: NONE)

        static readonly Swatch[] kSuitCol =
        {
            new Swatch("GRAPHITE", 52, 56, 64), new Swatch("SILVER", 168, 174, 184), new Swatch("WHITE", 226, 230, 236),
            new Swatch("MIDNIGHT", 22, 30, 62), new Swatch("COBALT", 30, 82, 196), new Swatch("TEAL", 18, 150, 156),
            new Swatch("EMERALD", 22, 150, 84), new Swatch("LIME", 150, 210, 40), new Swatch("GOLD", 214, 168, 52),
            new Swatch("ORANGE", 232, 112, 24), new Swatch("CRIMSON", 176, 22, 44), new Swatch("MAGENTA", 196, 30, 150),
            new Swatch("VIOLET", 104, 52, 190), new Swatch("BLACK", 16, 16, 19), new Swatch("BROWN", 92, 56, 36),
            new Swatch("GREY", 92, 95, 104),
        };
        static readonly Swatch[] kGlowCol =
        {
            new Swatch("TEAM", 0, 0, 0), new Swatch("WHITE", 235, 245, 255), new Swatch("CYAN", 40, 230, 255),
            new Swatch("BLUE", 50, 110, 255), new Swatch("VIOLET", 150, 70, 255), new Swatch("MAGENTA", 255, 40, 200),
            new Swatch("RED", 255, 40, 50), new Swatch("ORANGE", 255, 120, 20), new Swatch("GOLD", 255, 200, 40),
            new Swatch("LIME", 170, 255, 40), new Swatch("GREEN", 40, 255, 120), new Swatch("PINK", 255, 120, 190),
        };
        static readonly Swatch[] kHairCol =
        {
            new Swatch("BLACK", 22, 20, 20), new Swatch("DARK BROWN", 54, 36, 26), new Swatch("BROWN", 98, 64, 40),
            new Swatch("AUBURN", 128, 52, 30), new Swatch("COPPER", 184, 94, 44), new Swatch("BLONDE", 214, 180, 110),
            new Swatch("PLATINUM", 232, 224, 200), new Swatch("SILVER", 170, 176, 184), new Swatch("CRIMSON", 170, 20, 36),
            new Swatch("COBALT", 34, 74, 200), new Swatch("VIOLET", 110, 50, 190), new Swatch("PINK", 240, 110, 170),
            new Swatch("NEON", 0, 0, 0),
        };
        static readonly Swatch[] kTint =
        {
            new Swatch("SMOKE", 30, 34, 42), new Swatch("MIRROR", 150, 160, 175), new Swatch("GOLD", 200, 150, 50),
            new Swatch("ICE", 90, 170, 230), new Swatch("MAGENTA", 190, 40, 150), new Swatch("AMBER", 230, 130, 30),
            new Swatch("EMERALD", 40, 170, 100), new Swatch("TEAM", 0, 0, 0),
        };
        static readonly Swatch[] kSkin =
        {
            new Swatch("PORCELAIN", 246, 222, 206), new Swatch("FAIR", 238, 200, 176), new Swatch("LIGHT", 226, 180, 150),
            new Swatch("TAN", 204, 150, 112), new Swatch("OLIVE", 176, 134, 92), new Swatch("BROWN", 140, 94, 62),
            new Swatch("DEEP", 100, 64, 42), new Swatch("EBONY", 62, 40, 28), new Swatch("CHROME", 180, 186, 196),
            new Swatch("ICE", 150, 200, 240), new Swatch("GHOST", 230, 236, 245), new Swatch("OBSIDIAN", 26, 26, 32),
        };

        static readonly string[] kFieldName = { "SUIT", "BUILD", "PATTERN", "MAIN COLOR", "TRIM COLOR", "GLOW", "HAIR", "HAIR COLOR",
                                                "HEADGEAR", "FACE SHIELD", "SHIELD TINT", "MASK", "SKIN", "LOWER BODY", "BODY TYPE", "GAUNTLET", "FACE",
                                                "COUNTRY" };
        static readonly string[] kKey = { "suit", "build", "pattern", "primary", "secondary", "glow", "hair", "haircolor",
                                          "headgear", "visor", "visortint", "mask", "skin", "lower", "body", "gauntlet", "face", "nation" };

        static float Lin(byte c) => MathF.Pow(c / 255f, 2.2f);
        static void SwatchLin(Swatch s, float[] o) { o[0] = Lin(s.r); o[1] = Lin(s.g); o[2] = Lin(s.b); }

        // the byte a field lives in (F_FACE and F_NATION live in spare bits: Get / Set handle them)
        static byte Raw(AvatarDesc d, Field f)
        {
            switch (f)
            {
                case Field.Suit: return d.suit;           case Field.Build: return d.build;          case Field.Pattern: return d.pattern;
                case Field.Primary: return d.primary;     case Field.Secondary: return d.secondary;  case Field.Glow: return d.glow;
                case Field.Hair: return d.hair;           case Field.HairColor: return d.hairColor;  case Field.Headgear: return d.headgear;
                case Field.Visor: return d.visor;         case Field.VisorTint: return d.visorTint;  case Field.Mask: return d.mask;
                case Field.Skin: return d.skin;           case Field.Lower: return d.lower;          case Field.Body: return d.body;
                default: return d.gauntlet;
            }
        }

        static void SetRaw(ref AvatarDesc d, Field f, byte v)
        {
            switch (f)
            {
                case Field.Suit: d.suit = v; break;           case Field.Build: d.build = v; break;          case Field.Pattern: d.pattern = v; break;
                case Field.Primary: d.primary = v; break;     case Field.Secondary: d.secondary = v; break;  case Field.Glow: d.glow = v; break;
                case Field.Hair: d.hair = v; break;           case Field.HairColor: d.hairColor = v; break;  case Field.Headgear: d.headgear = v; break;
                case Field.Visor: d.visor = v; break;         case Field.VisorTint: d.visorTint = v; break;  case Field.Mask: d.mask = v; break;
                case Field.Skin: d.skin = v; break;           case Field.Lower: d.lower = v; break;          case Field.Body: d.body = v; break;
                default: d.gauntlet = v; break;
            }
        }

        static uint Rng(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        static int Pick(ref uint s, int n) => (int)(Rng(ref s) % (uint)n);

        static AvatarDesc Make(int suit, int build, int pattern, int primary, int secondary, int glow, int hair, int hairColor,
                               int headgear, int visor, int visorTint, int mask, int skin)
        {
            var d = AvatarDesc.Default;
            int[] v = { suit, build, pattern, primary, secondary, glow, hair, hairColor, headgear, visor, visorTint, mask, skin };
            // (native: the first 13 fields; the rest of its 18-entry array is zero: lower, body and gauntlet become 0)
            for (int f = 0; f < (int)Field.Count; ++f) SetRaw(ref d, (Field)f, (byte)(f < v.Length ? v[f] : 0));
            return d;
        }

        static AvatarDesc WithLower(AvatarDesc d, int lower, int body = 0) { d.lower = (byte)lower; d.body = (byte)body; return d; }

        struct Preset { public string name; public AvatarDesc d; public Preset(string n, AvatarDesc d) { name = n; this.d = d; } }
        static readonly Preset[] kPresets =
        {
            new Preset("GRID RUNNER", Make(0, 1, 1, 13, 0, 0, 0, 0, 1, 2, 0, 0, 2)),
            new Preset("ROOKIE", Make(1, 1, 2, 4, 2, 0, 2, 2, 5, 0, 0, 1, 3)),
            new Preset("VANGUARD", Make(2, 2, 4, 1, 3, 2, 1, 0, 4, 7, 0, 2, 5)),
            new Preset("NIGHT RIDER", Make(3, 1, 0, 13, 10, 6, 3, 5, 0, 3, 2, 4, 1)),
            new Preset("ECHO", Make(5, 0, 7, 2, 13, 1, 4, 12, 0, 6, 0, 0, 6)),
            new Preset("SKY PILOT", Make(7, 1, 0, 6, 8, 8, 1, 1, 0, 4, 5, 3, 4)),
            new Preset("GHOST", Make(4, 1, 6, 0, 12, 5, 0, 0, 3, 5, 4, 0, 10)),
            new Preset("STRIKER", Make(6, 2, 3, 9, 13, 7, 5, 11, 0, 1, 3, 0, 7)),
            new Preset("CHROME", Make(0, 1, 1, 1, 13, 2, 0, 0, 0, 1, 1, 5, 8)),
            new Preset("RACER", Make(5, 1, 5, 10, 2, 0, 0, 0, 2, 2, 2, 0, 2)),
            new Preset("CIRCUIT", WithLower(Make(8, 2, 0, 13, 13, 2, 8, 2, 0, 0, 0, 0, 2), 2)),
            new Preset("TRAINER", WithLower(Make(9, 3, 0, 14, 4, 3, 3, 2, 0, 0, 0, 0, 3), 1, 1)),
            new Preset("TANK AND SHORTS", WithLower(Make(6, 1, 0, 14, 4, 0, 0, 0, 0, 0, 0, 0, 3), 1)),
            new Preset("TRAINING KIT", WithLower(Make(9, 1, 0, 13, 15, 0, 0, 0, 0, 0, 0, 0, 3), 3, 1)),
            // the full sets (the owner, 2026-11-19: "make the new outfits that have full sets into the presets menu ... and
            // don't forget to add night forge outfit and vector outfit"), in their makers' own colours where they gave them
            // (TASKS.md E35, native 7ee73bf):
            new Preset("RONIN", WithLower(Make(16, 1, 0, 4, 9, 2, 7, 0, 9, 0, 0, 4, 4), 4)),               // PR #27: wrap, kabuto, bandana
            new Preset("PELURU TUXEDO", WithLower(Make(17, 1, 0, 13, 8, 11, 2, 0, 10, 0, 0, 6, 3), 4)),    // PR #30: sombrero, sugar skull
            new Preset("PELURU DRESS", WithLower(Make(18, 1, 0, 13, 5, 11, 9, 0, 10, 0, 0, 6, 5), 0, 1)),
            new Preset("VECTOR", WithLower(Make(14, 1, 0, 13, 1, 2, 6, 0, 8, 0, 0, 0, 1), 4, 1)),          // PR #25: its helmet, its shorts
            new Preset("NIGHTFORGE", WithLower(Make(15, 3, 0, 13, 8, 2, 1, 0, 7, 0, 0, 0, 6), 4)),         // PR #25: the HORNED HELMET
        };

        public static int FieldCount(Field f)
        {
            switch (f)
            {
                case Field.Suit: return kSuit.Length;           case Field.Build: return kBuild.Length;     case Field.Pattern: return kPattern.Length;
                case Field.Primary: case Field.Secondary: return kSuitCol.Length;
                case Field.Glow: return kGlowCol.Length;        case Field.Hair: return kHair.Length;       case Field.HairColor: return kHairCol.Length;
                case Field.Headgear: return kHeadgear.Length;   case Field.Visor: return kVisor.Length;     case Field.VisorTint: return kTint.Length;
                case Field.Mask: return kMask.Length;           case Field.Skin: return kSkin.Length;       case Field.Lower: return kLower.Length;
                case Field.Body: return kBody.Length;           case Field.Gauntlet: return kGauntlet.Length;
                case Field.Face: return kFaces;
                case Field.Nation: return NationCount() + 1;
                default: return 1;
            }
        }

        public static string FieldName(Field f) => f >= 0 && f < Field.Count ? kFieldName[(int)f] : "";

        public static string OptionName(Field f, int i)
        {
            if (i < 0 || i >= FieldCount(f)) return "?";
            switch (f)
            {
                case Field.Suit: return kSuit[i];            case Field.Build: return kBuild[i];        case Field.Pattern: return kPattern[i];
                case Field.Primary: case Field.Secondary: return kSuitCol[i].name;
                case Field.Glow: return kGlowCol[i].name;    case Field.Hair: return kHair[i];          case Field.HairColor: return kHairCol[i].name;
                case Field.Headgear: return kHeadgear[i];    case Field.Visor: return kVisor[i];        case Field.VisorTint: return kTint[i].name;
                case Field.Mask: return kMask[i];            case Field.Skin: return kSkin[i].name;     case Field.Lower: return kLower[i];
                case Field.Body: return kBody[i];            case Field.Gauntlet: return kGauntlet[i];
                case Field.Face: return kFace[i];
                case Field.Nation: return i == 0 ? "NONE" : kNations[i - 1].name;
                default: return "?";
            }
        }

        public static byte Get(AvatarDesc d, Field f)
        {
            if (f == Field.Body) return (byte)BodyType(d);
            if (f == Field.Face) return (byte)FaceOf(d);
            if (f == Field.Nation) return (byte)NationOf(d);
            if (f == Field.Gauntlet) return (byte)GauntletOf(d);
            if (f == Field.Pattern) return (byte)(d.pattern & ~kOwnKitBit);   // (its top bit: COUNTRY KIT)
            return Raw(d, f);
        }

        // wraps into range
        public static void Set(ref AvatarDesc d, Field f, int v)
        {
            int n = FieldCount(f);
            v = ((v % n) + n) % n;
            if (f == Field.Body) d.body = (byte)((d.body & ~1) | v);
            else if (f == Field.Face) d.body = (byte)((d.body & ~6) | (v << 1));
            else if (f == Field.Nation) SetNation(ref d, v);
            else if (f == Field.Gauntlet) d.gauntlet = (byte)((d.gauntlet & ~31) | v);
            else if (f == Field.Pattern) d.pattern = (byte)((d.pattern & kOwnKitBit) | v);
            else SetRaw(ref d, f, (byte)v);
        }

        public static bool IsColor(Field f) =>
            f == Field.Primary || f == Field.Secondary || f == Field.Glow || f == Field.HairColor || f == Field.VisorTint || f == Field.Skin;

        // display colour of option i of a colour field (linear rgb); team = the wearer's team colour (for "TEAM")
        public static void OptionColor(Field f, int i, float[] team, float[] o)
        {
            o[0] = o[1] = o[2] = 0.5f;
            if (i < 0 || i >= FieldCount(f)) return;
            Swatch s;
            switch (f)
            {
                case Field.Primary: case Field.Secondary: s = kSuitCol[i]; break;
                case Field.Glow: s = kGlowCol[i]; break;
                case Field.HairColor: s = kHairCol[i]; break;
                case Field.VisorTint: s = kTint[i]; break;
                case Field.Skin: s = kSkin[i]; break;
                default: return;
            }
            if ((f == Field.Glow && i == 0) || (f == Field.VisorTint && i == kTint.Length - 1)) { o[0] = team[0]; o[1] = team[1]; o[2] = team[2]; return; }
            if (f == Field.HairColor && i == kHairCol.Length - 1) { o[0] = team[0]; o[1] = team[1]; o[2] = team[2]; return; }   // NEON: shown in team colour
            SwatchLin(s, o);
        }

        public static void Sanitize(ref AvatarDesc d)
        {
            bool own = (d.pattern & kOwnKitBit) != 0;   // (COUNTRY KIT: kept with a country)
            d.pattern = (byte)(d.pattern & ~kOwnKitBit);
            for (int f = 0; f < (int)Field.Count; ++f)
            {
                var fl = (Field)f;
                if (fl == Field.Body || fl == Field.Face || fl == Field.Nation || fl == Field.Gauntlet) continue;
                if (Raw(d, fl) >= FieldCount(fl)) SetRaw(ref d, fl, 0);
            }
            int n = NationOf(d), face = (d.body >> 1) & 3;
            if (n > NationCount()) n = 0;
            if (face >= kFaces) face = 0;   // a face out of range: FACE 1
            if (GauntletOf(d) >= FieldCount(Field.Gauntlet)) d.gauntlet = 0;
            d.body = (byte)(BodyType(d) | (face << 1));
            d.gauntlet = (byte)GauntletOf(d);
            SetNation(ref d, n);
            if (own && n != 0) d.pattern = (byte)(d.pattern | kOwnKitBit);
        }

        public static int NationCount() => kNations.Length;

        // the builder's COUNTRY order (TASKS.md E44, native 10668f0 nationOrder, 0.3.23; the owner, 2026-11-20: "reordered by
        // frequently chosen and then alphabetically ... if it's the first time chosen there isn't a frequent chosen"): the
        // countries this player has chosen, the most often first (equal counts A-Z), then the rest A-Z by name, a leading
        // "THE " left out (THE GALAXY under G). picks[n] is how often country n was chosen (n = 1..NationCount(); picks[0]
        // unused); every country number comes back once. Nothing chosen yet: all of them A-Z. The numbers stay as they are
        public static List<int> NationOrder(int[] picks)
        {
            string Key(int n) { string s = kNations[n - 1].name; return s.StartsWith("THE ", StringComparison.Ordinal) ? s.Substring(4) : s; }
            int Pick(int n) => picks != null && n < picks.Length ? Math.Max(0, picks[n]) : 0;
            var v = new List<int>();
            for (int n = 1; n <= NationCount(); ++n) v.Add(n);
            return v.OrderByDescending(Pick).ThenBy(Key, StringComparer.Ordinal).ToList();   // (stable, as native's stable_sort)
        }
        public static Nation NationAt(int n) => n >= 1 && n <= kNations.Length ? kNations[n - 1] : null;   // null for 0 (none) or out of range

        public static void SetNation(ref AvatarDesc d, int n)
        {
            if (n < 0 || n > 255) n = 0;
            d.body = (byte)((d.body & 7) | ((n & 31) << 3));
            d.gauntlet = (byte)((d.gauntlet & 31) | ((n >> 5) << 5));
        }

        // COUNTRY KIT (TASKS.md E34, native 485beac, 0.3.22; the owner, 2026-11-18): with a country, TEAM JERSEY (as before,
        // and the default) wears the team kit (Uniform); MY OUTFIT keeps your own suit, pattern and lower body and puts the
        // country on them: its colours and its two prints. It rides in PATTERN's top bit, only with a country (Sanitize
        // clears it without one). Builds before 0.3.22 read that as an unknown pattern, clear it and put the team jersey on,
        // so they show such a player as before
        public const byte kOwnKitBit = 0x80;
        public static bool OwnKit(AvatarDesc d) => (d.pattern & kOwnKitBit) != 0 && NationOf(d) != 0;
        public static void SetOwnKit(ref AvatarDesc d, bool own) =>   // (only with a country)
            d.pattern = (byte)((d.pattern & ~kOwnKitBit) | (own && NationOf(d) != 0 ? kOwnKitBit : 0));

        static int Nearest(Swatch[] s, int n, int from, byte[] c)
        {
            int best = from;
            float bd = 1e9f;
            for (int i = from; i < n; ++i)
            {
                float dr = Lin(s[i].r) - Lin(c[0]), dg = Lin(s[i].g) - Lin(c[1]), db = Lin(s[i].b) - Lin(c[2]);
                float dd = dr * dr * 0.3f + dg * dg * 0.59f + db * db * 0.11f;
                if (dd < bd) { bd = dd; best = i; }
            }
            return best;
        }

        // the kit's trim (ribbed collar, sleeve bands, side stripes, number panel, gloves): the flag's accent colour, or
        // its second colour when the accent is the jersey's own (CANADA: red, white, red)
        static int KitTrim(Nation N)
        {
            int d = 0;
            for (int k = 0; k < 3; ++k) d += Math.Abs(N.kit[2][k] - N.kit[0][k]);
            return d > 90 ? 2 : 1;
        }

        static void GlowOf(byte[] c, float[] fallback, float[] o)
        {   // a kit colour as a glow (brightest 1)
            float[] v = { Lin(c[0]), Lin(c[1]), Lin(c[2]) };
            float m = Max(v[0], Max(v[1], v[2]));
            for (int k = 0; k < 3; ++k) o[k] = m < 0.02f ? fallback[k] : v[k] / m;
        }

        // the look as drawn. With a country: the team kit (ARENA JERSEY, no pattern, athletic shorts, the nearest colour
        // swatches, so builds that don't know countries still show a jersey in about the right colours), or with MY OUTFIT
        // your own outfit and the nearest swatches; the COUNTRY KIT bit cleared. Without: unchanged
        public static AvatarDesc Uniform(AvatarDesc @in)
        {
            AvatarDesc d = @in;
            Sanitize(ref d);
            Nation N = NationAt(NationOf(d));
            if (N == null) return d;
            if ((d.pattern & kOwnKitBit) != 0) d.pattern = (byte)(d.pattern & ~kOwnKitBit);   // COUNTRY KIT = MY OUTFIT: your suit, pattern and lower body stay
            else
            {
                d.suit = 1;      // ARENA JERSEY
                d.pattern = 0;
                // ATHLETIC SHORTS (BIKE, LONG SHORTS and the skirts stay: native 82dd0c2, 0.3.21)
                if (d.lower == 0 || d.lower == 2) d.lower = 1;
            }
            d.primary = (byte)Nearest(kSuitCol, kSuitCol.Length, 0, N.kit[0]);
            d.secondary = (byte)Nearest(kSuitCol, kSuitCol.Length, 0, N.kit[KitTrim(N)]);
            d.glow = (byte)Nearest(kGlowCol, kGlowCol.Length, 1, N.kit[2]);
            return d;
        }

        // the look as sent to the others: Uniform(), with MY OUTFIT's bit (so 0.3.22 shows your outfit; older builds the jersey)
        public static AvatarDesc SentLook(AvatarDesc @in)
        {
            AvatarDesc s = @in;
            Sanitize(ref s);
            AvatarDesc d = Uniform(s);
            if (OwnKit(s)) d.pattern = (byte)(d.pattern | kOwnKitBit);
            return d;
        }

        // the colours of the two ends of the court (linear, brightest channel 1): blue / orange, or the players' countries;
        // two close colours (FRANCE against the blue end, or the same country twice) move the second end to its other colour
        public static void SideColors(int n0, int n1, float[][] o)
        {
            float[][] def = { new[] { 0.10f, 0.70f, 1.00f }, new[] { 1.00f, 0.40f, 0.10f } };
            int[] ns = { n0, n1 };
            var cand = new float[2][][];   // each end's first and second choice
            for (int p = 0; p < 2; ++p)
            {
                Nation N = NationAt(ns[p]);
                cand[p] = new[] { new float[3], new float[3] };
                for (int k = 0; k < 3; ++k)
                {
                    cand[p][0][k] = N != null ? N.team[0][k] : def[p][k];
                    cand[p][1][k] = N != null ? N.team[1][k] : def[1 - p][k];
                }
            }
            float Dist(float[] a, float[] b)
            {
                float s = 0;
                for (int k = 0; k < 3; ++k) s += (a[k] - b[k]) * (a[k] - b[k]);
                return MathF.Sqrt(s);
            }
            for (int k = 0; k < 3; ++k) o[0][k] = cand[0][0][k];
            int pick = Dist(cand[1][0], o[0]) >= 0.55f ? 0 : Dist(cand[1][1], o[0]) > Dist(cand[1][0], o[0]) ? 1 : 0;
            for (int k = 0; k < 3; ++k) o[1][k] = cand[1][pick][k];
            if (Dist(o[0], o[1]) < 0.55f && Dist(cand[0][1], o[1]) > Dist(o[0], o[1]))   // still close: the first end moves
                for (int k = 0; k < 3; ++k) o[0][k] = cand[0][1][k];
        }

        public static int PresetCount() => kPresets.Length;
        public static string PresetName(int i) => kPresets[((i % PresetCount()) + PresetCount()) % PresetCount()].name;
        public static AvatarDesc PresetLook(int i) => kPresets[((i % PresetCount()) + PresetCount()) % PresetCount()].d;

        // a random but coherent look (bots, the RANDOM button)
        public static AvatarDesc RandomLook(uint seed)
        {
            uint s = seed * 2654435761u + 0x9E3779B9u;
            if (s == 0) s = 1;
            for (int i = 0; i < 4; ++i) Rng(ref s);
            var d = AvatarDesc.Default;
            d.suit = (byte)Pick(ref s, kSuit.Length);
            d.build = (byte)(Pick(ref s, 5) == 0 ? 0 : Pick(ref s, 4) == 0 ? 2 : 1);
            d.pattern = (byte)(Pick(ref s, 3) == 0 ? 0 : Pick(ref s, kPattern.Length));
            d.primary = (byte)Pick(ref s, kSuitCol.Length);
            // a trim colour that reads against the main colour (luminance apart)
            float[] p3 = new float[3], s3 = new float[3];
            SwatchLin(kSuitCol[d.primary], p3);
            for (int tries = 0; tries < 12; ++tries)
            {
                d.secondary = (byte)Pick(ref s, kSuitCol.Length);
                SwatchLin(kSuitCol[d.secondary], s3);
                float lp = 0.2126f * p3[0] + 0.7152f * p3[1] + 0.0722f * p3[2], ls = 0.2126f * s3[0] + 0.7152f * s3[1] + 0.0722f * s3[2];
                if (d.secondary != d.primary && MathF.Abs(MathF.Sqrt(lp) - MathF.Sqrt(ls)) > 0.18f) break;
            }
            d.glow = (byte)(Pick(ref s, 5) < 2 ? 0 : Pick(ref s, kGlowCol.Length));
            d.headgear = (byte)(Pick(ref s, 2) == 0 ? 0 : Pick(ref s, kHeadgear.Length));
            d.hair = (byte)Pick(ref s, kHair.Length);
            d.hairColor = (byte)(Pick(ref s, 5) == 0 ? 7 + Pick(ref s, kHairCol.Length - 7) : Pick(ref s, 7));
            d.visor = (byte)(Pick(ref s, 10) < 3 ? 0 : Pick(ref s, kVisor.Length));
            d.visorTint = (byte)Pick(ref s, kTint.Length);
            d.mask = (byte)(Pick(ref s, 10) < 6 ? 0 : Pick(ref s, kMask.Length));
            d.skin = (byte)(Pick(ref s, 10) == 0 ? 8 + Pick(ref s, 4) : Pick(ref s, 8));
            d.lower = (byte)(Pick(ref s, 2) == 0 ? 0 : 1 + Pick(ref s, kLower.Length - 1));
            d.body = (byte)(Pick(ref s, 2) | (Pick(ref s, kFaces) << 1));
            d.gauntlet = (byte)Pick(ref s, 3);
            return d;
        }

        // colours for every channel, linear rgb + 1 (o[Channel.None] = white)
        public static void ChannelColors(AvatarDesc @in, float[] team, float[][] o)
        {
            AvatarDesc d = @in;
            Sanitize(ref d);
            for (int c = 0; c < (int)Channel.Count; ++c) { o[c][0] = o[c][1] = o[c][2] = o[c][3] = 1; }
            OptionColor(Field.Primary, d.primary, team, o[(int)Channel.Primary]);
            OptionColor(Field.Secondary, d.secondary, team, o[(int)Channel.Secondary]);
            OptionColor(Field.Glow, d.glow, team, o[(int)Channel.Glow]);
            Nation N = NationAt(NationOf(d));
            if (N != null)
            {   // the team kit in the flag's own colours
                int t = KitTrim(N);
                for (int k = 0; k < 3; ++k) { o[(int)Channel.Primary][k] = Lin(N.kit[0][k]); o[(int)Channel.Secondary][k] = Lin(N.kit[t][k]); }
                GlowOf(N.kit[2], N.team[0], o[(int)Channel.Glow]);
            }
            OptionColor(Field.Skin, d.skin, team, o[(int)Channel.Skin]);
            OptionColor(Field.VisorTint, d.visorTint, team, o[(int)Channel.Visor]);
            if (NeonHair(d)) for (int k = 0; k < 3; ++k) o[(int)Channel.Hair][k] = o[(int)Channel.Glow][k];
            else OptionColor(Field.HairColor, d.hairColor, team, o[(int)Channel.Hair]);
            if (d.visorTint == kTint.Length - 1) for (int k = 0; k < 3; ++k) o[(int)Channel.Visor][k] *= 0.45f;   // TEAM tint: dark glass
            // trim: brushed metal leaning toward the trim colour
            float[] metal = { 0.50f, 0.52f, 0.56f };
            for (int k = 0; k < 3; ++k) o[(int)Channel.Trim][k] = metal[k] * 0.7f + o[(int)Channel.Secondary][k] * 0.3f;
            for (int k = 0; k < 3; ++k) o[(int)Channel.Glow][k] *= 1.15f;   // glow lines are unlit: a little over white for punch
        }

        public static float[][] NewChannels()
        {
            var o = new float[(int)Channel.Count][];
            for (int c = 0; c < o.Length; ++c) o[c] = new float[4];
            return o;
        }

        public static bool NeonHair(AvatarDesc d) => d.hairColor == kHairCol.Length - 1;   // hair glows (HAIR COLOUR = NEON)
        public static string PatternTexture(int pattern) => pattern > 0 && pattern < kPattern.Length ? kPatternTex[pattern] : null;

        // "suit=0\nbuild=1\n..." (avatar.cfg)
        public static string ToText(AvatarDesc d)
        {
            var s = new StringBuilder();
            for (int f = 0; f < (int)Field.Count; ++f) s.Append(kKey[f]).Append('=').Append((int)Get(d, (Field)f)).Append('\n');
            if (OwnKit(d)) s.Append("kit=1\n");   // COUNTRY KIT = MY OUTFIT (builds before 0.3.22 skip the key: the team jersey)
            return s.ToString();
        }

        public static bool FromText(string s, ref AvatarDesc d)
        {
            bool any = false, own = false;
            foreach (var line in s.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq);
                int v = AtoI(line.Substring(eq + 1));
                for (int f = 0; f < (int)Field.Count; ++f)
                    if (k == kKey[f] && v >= 0 && v < FieldCount((Field)f)) { Set(ref d, (Field)f, v); any = true; }
                if (k == "kit") own = v == 1;
            }
            Sanitize(ref d);
            SetOwnKit(ref d, own);   // (no kit line: TEAM JERSEY)
            return any;
        }

        // C atoi: leading spaces, a sign, digits; anything else ends it (0 when none)
        static int AtoI(string t)
        {
            int i = 0, sign = 1, v = 0;
            while (i < t.Length && char.IsWhiteSpace(t[i])) ++i;
            if (i < t.Length && (t[i] == '+' || t[i] == '-')) { if (t[i] == '-') sign = -1; ++i; }
            while (i < t.Length && t[i] >= '0' && t[i] <= '9') { v = v * 10 + (t[i] - '0'); ++i; }
            return sign * v;
        }

        // ---------------------------------------------------------------- rig
        // The skinned bodies (the native tools/build_body_parts.py, MPFB2): bind-pose joints in app space, origin = the
        // neck base.
        public struct BodyBind
        {
            public Vec3 eye, neckTop;
            public Vec3[] shoulder, elbow, wrist, thumb;
        }

        // the skinned bodies' bones (native avatar.h BONE_*): since native c758fd3 nine, the two collarbones last
        public enum BoneId { Torso, Neck, Head, UpperL, UpperR, ForeL, ForeR, ClavL, ClavR, Count }

        public struct Build { public float width, depth, arm; public bool curved; public Build(float w, float d, float a, bool c) { width = w; depth = d; arm = a; curved = c; } }   // torso x / z scale, arm thickness

        public const float kUpperArm = 0.285f, kForeArm = 0.265f, kNeck = 0.115f;   // natural bone lengths (m)
        public const float kHeadScale = 1.0f;    // the MPFB heads are drawn at their true size

        public struct Bone
        {
            public Vec3 a, b;        // joint -> joint
            public Vec3 front;       // reference for the mesh's front (-Z); need not be perpendicular to the bone
            public float natural;    // the mesh's authored length
            public Bone(Vec3 a, Vec3 b, Vec3 front, float natural) { this.a = a; this.b = b; this.front = front; this.natural = natural; }
        }

        public class Rig
        {
            public Pose torso = Pose.Identity;       // neck base, upright, -Z = facing
            public Bone neck;
            public bool[] arm = { false, false };    // hand tracked
            public Bone[] upper = new Bone[2], fore = new Bone[2];   // 0 = left, 1 = right
            public Mat4[] skin = new Mat4[(int)BoneId.Count];         // bind -> world for the skinned parts
            public float[] reachGap = { 0, 0 };   // THE REACH: how far each forearm ends short of its gauntlet (m; the arm never stretches)
            public float armScale = 1f;           // ARM SIZING: the arms' length against the avatar's own
            // THE REACH: the hips, the torso upright (its heading, no lean) hinged to it at the waist; bent: the torso leans,
            // so the skinning shader blends bone 0 from the pelvis at the hips to the torso at the chest
            public Mat4 pelvis = Mat4.Identity();
            public bool bent;
            // FOREARM TWIST (PR #25, TASKS.md E11): the forearm bone turns with the hand (its front is the controller's top),
            // so a turned hand twisted the skin all at the elbow; the shader turns it back there, less and less toward the
            // wrist. 8 vec4 (native axes), per forearm k (0 left, 1 right): [k] the bind elbow, 1 / (0.96 |d|^2); [2+k] d =
            // bind elbow -> wrist, the roll (rad); [4+k] the elbow (world), 1 / (0.96 length); [6+k] the forearm's axis
            // (world). twisted: a roll to undo
            public float[] twist = new float[32];
            public float[] roll = { 0, 0 };   // each forearm's turn against the upper arm's, untwisted (rad, -pi .. pi)
            public bool twisted;
        }

        // Skeleton and arm-attachment values the rig uses, adjustable live in developer mode (native SETTINGS > TUNING;
        // later in this port). The defaults are the shipped rig. Right-hand / right-side values; the left side is mirrored.
        public class RigTuning
        {
            public Vec3 shoulder = new Vec3(0, 0, 0);                  // added to both shoulders (x = outward, y = up, z = back)
            public float upperLen = 1f, foreLen = 1f;                  // bone length scales
            public float armThick = 1f;                                // arm cross-section scale (with the build's)
            public Vec3 elbowPole = new Vec3(0.35f, -1.0f, 0.25f);     // the elbow's pull: out, down, back (torso frame)
            public float elbowPull = 0.12f;                            // how far toward the pole (m)
            public float reach = 0.09f;                                // the shoulder follows a hand out of reach by up to this (m; 7 cm
                                                                       // until the cuff moved under the fist, native 2026-11-16, its wrist 4.5 cm lower)
            public float reachStart = 1.0f;                            // ... once the hand is beyond this share of the arm's length
                                                                       // (0.97 until 2026-11-17, native 8a68443: the shoulder came
                                                                       // out to meet a hand the arm still reached, so every full
                                                                       // reach held the elbow bent; a player's video)
            public float shrug = 0.06f;                                // how far a shoulder lifts with its hand straight overhead (m)
            public float torsoFollow = 0.6f;                           // how much the chest turns toward the hands
            public float torsoLimitDeg = 50f;                          // never more than this from the head's heading
            public Vec3 neck = new Vec3(0, 0, 0);                      // the neck top against the head (head frame: up, back = +y, +z)
            // where the forearm ends inside a plate gauntlet's cuff (grip space), and the cuff's own pose against the hand:
            // a rotation (pitch / yaw / roll degrees, gripEuler) about the wrist pivot, then an offset
            public Vec3 forearmEnd = new Vec3(0.0247f, 0.0382f, 0.0876f);
            // the cuff against the hand: the owner's TUNING export (2026-10-19: 1.6 right, 2.2 up, 2.6 back (cm), pitched 17 deg,
            // yaw and roll -1), then lined up behind the fist when it was turned in line with the forearm (native c68f23e,
            // owner 2026-11-16; TASKS.md E7): swung 12 degrees about its own wrist point and 3.5 cm toward the little
            // finger's side
            public Vec3 cuffPos = new Vec3(0.016f, -0.013f, 0.026f), cuffRot = new Vec3(28.99f, -2.19f, -1.78f);
            // ELBOWS OUT OF THE BODY (native f362017, owner 2026-11-16; TASKS.md E9): the elbow swings out once the arm is
            // deeper in the torso than elbowAllow (m); the arm kept clear by this share of its thickness (at rest it lies
            // against the body by design)
            public float elbowAllow = 0.010f, armClearShare = 0.6f;

            public RigTuning Clone() => (RigTuning)MemberwiseClone();
        }

        public static RigTuning Tuning = new RigTuning();
        public static readonly Vec3 kCuffPivot = new Vec3(0.004f, 0.005f, 0.050f);   // the wrist joint of the plate gauntlets (make_props.py, grip space)

        // native avatar_bodies.inc (generated by tools/build_body_parts.py from the PR #25 bodies: Quaternius, CC0; native
        // 5992aae, TASKS.md E14)
        static readonly BodyBind[] kBodyBind =
        {
            new BodyBind {   // MASCULINE (native 5992aae: the PR #25 bodies, Quaternius CC0, posed by tools/repose_bodies.py)
                eye = new Vec3(-0.00000f, 0.17797f, -0.11099f), neckTop = new Vec3(-0.00000f, 0.07930f, -0.02400f),
                shoulder = new[] { new Vec3(-0.21201f, -0.06500f, 0.02400f), new Vec3(0.21201f, -0.06500f, 0.02400f) },
                elbow = new[] { new Vec3(-0.37867f, -0.25284f, 0.02437f), new Vec3(0.37867f, -0.25284f, 0.02437f) },
                wrist = new[] { new Vec3(-0.50099f, -0.37012f, -0.15062f), new Vec3(0.50099f, -0.37012f, -0.15062f) },
                thumb = new[] { new Vec3(-0.47486f, -0.40591f, -0.23766f), new Vec3(0.47486f, -0.40591f, -0.23766f) },
            },
            new BodyBind {   // FEMININE (native 5992aae: the PR #25 bodies, Quaternius CC0, posed by tools/repose_bodies.py)
                eye = new Vec3(-0.00000f, 0.17103f, -0.10375f), neckTop = new Vec3(-0.00000f, 0.06470f, -0.02970f),
                shoulder = new[] { new Vec3(-0.15160f, -0.06680f, 0.01370f), new Vec3(0.15160f, -0.06680f, 0.01370f) },
                elbow = new[] { new Vec3(-0.31442f, -0.24354f, 0.00988f), new Vec3(0.31442f, -0.24354f, 0.00988f) },
                wrist = new[] { new Vec3(-0.44302f, -0.38528f, -0.14988f), new Vec3(0.44302f, -0.38528f, -0.14988f) },
                thumb = new[] { new Vec3(-0.41864f, -0.39915f, -0.19985f), new Vec3(0.41864f, -0.39915f, -0.19985f) },
            },
        };

        public static BodyBind BodyBindOf(int body) => kBodyBind[body < 0 ? 0 : body > 1 ? 1 : body];

        public static Build BuildOf(int b)
        {
            // CURVED uses the curved torso meshes (bust, waist) at a slightly slimmer scale
            Build[] k = { new Build(0.94f, 0.96f, 0.92f, false), new Build(1f, 1f, 1f, false), new Build(1.07f, 1.05f, 1.10f, false),
                          new Build(1.03f, 1.02f, 1.05f, false) };
            return k[b < 0 ? 0 : b > 3 ? 3 : b];
        }

        static Vec3 Flat(Vec3 v)
        {
            v.y = 0;
            return Length(v) > 1e-4f ? Normalize(v) : new Vec3(0, 0, -1);
        }

        // The two-bone IK elbow for shoulder s, wrist w and bone lengths l1, l2: on the circle of valid elbows, the point
        // toward `pole`. Out of reach the arm points straight at the wrist.
        public static Vec3 ElbowIK(Vec3 s, Vec3 w, float l1, float l2, Vec3 pole)
        {
            Vec3 d = w - s;
            float dist = Length(d);
            if (dist < 1e-4f) return s + Normalize(pole - s) * l1;
            Vec3 u = d / dist;
            if (dist >= l1 + l2 - 1e-4f) return s + u * (dist * l1 / (l1 + l2));   // out of reach: straight, both bones stretch
            dist = Max(dist, MathF.Abs(l1 - l2) + 1e-3f);
            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist);
            float h = MathF.Sqrt(Max(0f, l1 * l1 - a * a));
            Vec3 c = s + u * a;
            Vec3 p = pole - c;
            p = p - u * Dot(p, u);
            if (Length2(p) < 1e-10f) p = MathF.Abs(u.y) < 0.9f ? Cross(u, new Vec3(0, 1, 0)) : Cross(u, new Vec3(1, 0, 0));
            return c + Normalize(p) * h;
        }

        static Mat4 AffineInverse(Mat4 mm)
        {
            float[] a = mm.m;   // column-major 3x3 + translation
            float c00 = a[5] * a[10] - a[9] * a[6], c01 = a[8] * a[6] - a[4] * a[10], c02 = a[4] * a[9] - a[8] * a[5];
            float det = a[0] * c00 + a[1] * c01 + a[2] * c02;
            float id = MathF.Abs(det) > 1e-12f ? 1f / det : 0f;
            Mat4 r = Mat4.Identity();
            r.m[0] = c00 * id;                               r.m[4] = c01 * id;                               r.m[8] = c02 * id;
            r.m[1] = (a[9] * a[2] - a[1] * a[10]) * id;      r.m[5] = (a[0] * a[10] - a[8] * a[2]) * id;      r.m[9] = (a[8] * a[1] - a[0] * a[9]) * id;
            r.m[2] = (a[1] * a[6] - a[5] * a[2]) * id;       r.m[6] = (a[4] * a[2] - a[0] * a[6]) * id;       r.m[10] = (a[0] * a[5] - a[4] * a[1]) * id;
            for (int i = 0; i < 3; ++i) r.m[12 + i] = -(r.m[i] * a[12] + r.m[4 + i] * a[13] + r.m[8 + i] * a[14]);
            return r;
        }

        // the cuff's pose against the hand (grip space; left = mirrored), from Tuning
        // the plate gauntlets' fist against the hand (grip space, the right hand; the left is drawn mirrored): turned 30
        // degrees about the knuckle bumper (as it stood by default), in line with the cuff and the forearm (native
        // avatar::fistPose, owner 2026-11-16); then 2 cm toward the thumb and 2.5 cm back into the cuff's opening
        public static Pose FistPose()
        {
            var kn = new Vec3(0, -0.026f, -0.057f);
            Quat q = Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(30f));
            return new Pose(q, kn + new Vec3(0, 0.020f, 0.025f) - q.Rotate(kn));
        }

        public static Pose CuffPose(bool left)
        {
            RigTuning T = Tuning;
            float s = left ? -1f : 1f;
            Quat q = GripEuler(T.cuffRot, left);
            var piv = new Vec3(s * kCuffPivot.x, kCuffPivot.y, kCuffPivot.z);
            return new Pose(q, piv + new Vec3(s * T.cuffPos.x, T.cuffPos.y, T.cuffPos.z) - q.Rotate(piv));
        }

        // ---------------------------------------------------------------- ELBOWS OUT OF THE BODY
        // (native f362017; the owner, 2026-11-16, her video in the builder's mirror: hands together at the chest, the elbows
        // sank into the torso and the forearms came out of the chest: the elbow was placed without the body in mind)

        // how deep a point (torso space with the build's scale taken out) is inside the torso grown by `grow` (0: outside,
        // or above the chest / below the hips, where the shoulders and legs are)
        static float TorsoDepth(TorsoShape T, Vec3 p, float grow)
        {
            float fi = (p.y - TorsoY0) / TorsoDY;   // (the slices go down)
            if (fi < -0.5f || fi > TorsoSlices - 0.5f) return 0f;
            int i0 = Math.Max(0, Math.Min(TorsoSlices - 1, (int)MathF.Floor(fi)));
            int i1 = Math.Min(TorsoSlices - 1, i0 + 1);
            float fy = Clampf(fi - i0, 0f, 1f);
            float cz = T.cz[i0] + (T.cz[i1] - T.cz[i0]) * fy;
            float fs = (MathF.Atan2(p.z - cz, p.x) + kPi) / (2f * kPi) * TorsoSectors - 0.5f;
            float fl = MathF.Floor(fs);
            int k0 = (((int)fl % TorsoSectors) + TorsoSectors) % TorsoSectors, k1 = (k0 + 1) % TorsoSectors;
            float fk = fs - fl;
            float At(int i) => T.r[i, k0] + (T.r[i, k1] - T.r[i, k0]) * fk;
            float R = At(i0) + (At(i1) - At(i0)) * fy;
            return Math.Max(0f, R + grow - MathF.Sqrt(p.x * p.x + (p.z - cz) * (p.z - cz)));
        }

        static float ArmInTorso(TorsoShape T, Pose torso, Vec3 scale, Vec3 S, Vec3 E, Vec3 W, float upperR, float foreR)
        {
            Vec3 Local(Vec3 p)
            {
                Vec3 q = torso.q.Conj().Rotate(p - torso.p);
                return new Vec3(q.x / scale.x, q.y, q.z / scale.z);
            }
            float worst = 0f;
            for (int i = 0; i <= 4; ++i)   // the upper arm from halfway (by the shoulder it meets the body by design) to the elbow
                worst = Math.Max(worst, TorsoDepth(T, Local(Lerp(S, E, 0.5f + 0.125f * i)), upperR));
            for (int i = 1; i <= 4; ++i)   // the forearm from the elbow to the cuff's rim (its last half is inside the gauntlet's
                worst = Math.Max(worst, TorsoDepth(T, Local(Lerp(E, W, 0.1375f * i)), foreR));   // cuff, on the controller)
            return worst;
        }

        // the elbow E0 (on the circle the two bones allow round the shoulder-to-wrist line) swung the least way round it
        // until the arm is no deeper in the torso than `allow`: out to the side rather than in (going in counts half as far
        // again), and only as far as it must, so it starts from where it was as soon as the arm meets the body (no jumps)
        static Vec3 ElbowClear(TorsoShape T, Pose torso, Vec3 scale, float side, Vec3 S, Vec3 W, Vec3 E0, float upperR, float foreR, float allow)
        {
            if (ArmInTorso(T, torso, scale, S, E0, W, upperR, foreR) <= allow) return E0;
            Vec3 sw = W - S;
            float d = Length(sw);
            if (d < 1e-4f) return E0;
            Vec3 n = sw / d;
            Vec3 C = S + n * Dot(E0 - S, n);
            Vec3 u = E0 - C;
            float rho = Length(u);
            if (rho < 0.01f) return E0;   // (the arm straight: nothing to swing)
            u = u / rho;
            Vec3 v = Cross(n, u);
            Vec3 AtPhi(float phi) => C + (u * MathF.Cos(phi) + v * MathF.Sin(phi)) * rho;
            float Depth(float phi) => ArmInTorso(T, torso, scale, S, AtPhi(phi), W, upperR, foreR);
            // which way round is out: the way the elbow first moves away from the torso's middle line
            Vec3 outward = torso.q.Rotate(new Vec3(side, 0, 0));
            float outSign = Dot(v, outward) >= 0 ? 1f : -1f;
            float step = Deg2Rad(4f), most = Deg2Rad(140f);
            float[] found = { -1f, -1f };   // [0] outward, [1] inward: the first clear angle
            float bestPhi = 0f, bestDepth = 1e9f;
            for (int w = 0; w < 2; ++w)
            {
                float sgn = w == 0 ? outSign : -outSign;
                float prev = 0f;
                for (float a = step; a <= most + 1e-4f; a += step)
                {
                    float dd = Depth(sgn * a);
                    if (dd < bestDepth - 1e-4f) { bestDepth = dd; bestPhi = sgn * a; }
                    if (dd <= allow)
                    {   // clear: halve back toward the last step that wasn't
                        float lo = prev, hi = a;
                        for (int i = 0; i < 4; ++i)
                        {
                            float mid = (lo + hi) * 0.5f;
                            if (Depth(sgn * mid) <= allow) hi = mid; else lo = mid;
                        }
                        found[w] = hi;
                        break;
                    }
                    prev = a;
                }
            }
            float ph;
            if (found[0] >= 0 && (found[1] < 0 || found[0] <= found[1] * 1.5f)) ph = outSign * found[0];
            else if (found[1] >= 0) ph = -outSign * found[1];
            else ph = bestPhi;   // (it can't clear: the hand is inside the body; as clear as it gets)
            return AtPhi(ph);
        }

        // ELBOWS OUT OF THE BODY: how deep arm k of a solved rig is inside its torso (m, beyond RigTuning armClearShare of
        // the arm's thickness; 0: clear). The torso's shape: AvatarTorso.cs (native avatar_torso.inc)
        public static float ArmInBody(Rig r, AvatarDesc d, int k)
        {
            TorsoShape T = kTorsoShape[BodyType(d)];
            Build B = BuildOf(d.build);
            float thick = B.arm * Tuning.armThick * Tuning.armClearShare;
            return ArmInTorso(T, r.torso, new Vec3(B.width, 1f, B.depth), r.upper[k].a, r.upper[k].b, r.fore[k].b, T.upperArm * thick, T.foreArm * thick);
        }

        // ---------------------------------------------------------------- THE REACH
        // (TASKS.md E10, native 74860ad and 79e7b28: astrobleem's ProjectArena-public PR #25 and issue #24, taken into the
        // game 2026-11-17 with the owner's go: his reaching, plus ARM SIZING): a hand out of reach swings its shoulder
        // forward about the collarbone's root, the collarbone's skin with it (in #24 the shoulder slid toward the hand while
        // its skin stayed), turns and leans the torso toward it about the neck (the head and the neck's length stay),
        // bending at the waist (the hips upright: Rig.pelvis), and the arm never stretches: what is left is a gap between
        // the forearm and the gauntlet, which ARM SIZING closes by lengthening a player's arms to the reach their hands
        // show. With a RigState (one per avatar, stepped once a frame) the torso and shoulders move smoothly; without one,
        // the rig is the posture the moment asks for.
        const float kMaxTilt = 0.4363323f;          // the torso leans at most 25 degrees (pitch and roll together) about the neck
        const float kWaistY = -0.38f;               // the bend's middle (bind space; the shader blends from -0.50 to -0.26)
        const float kMaxProtract = 0.5235988f;      // a collarbone swings forward at most 30 degrees about its root
        const float kRigTau = 0.12f;                // the torso's and shoulders' smoothing (s)
        const float kTorsoSpeed = 2.0943951f;       // ... and their limits: 120 degrees a second
        const float kSlideSpeed = 0.4f;             // ... 40 cm a second
        const float kArmSizing = 1.5f;              // ARM SIZING: the arms lengthen toward a longer reach over this (s)
        public const float kMaxArmScale = 1.35f;    // ... at most 35% longer (the feminine avatar's arms are 47 cm: a tall
                                                    // player's reach is 1.3 times that)
        const float kTouch = 0.001f;                // an arm that comes within this of its hand reaches it (m)
        // ... and they shorten toward the longest reach the hands have shown lately (TASKS.md E25, native 7ed11aa; the owner,
        // 2026-11-17, her mirror videos: arms straight in life, the avatar's elbows bent, and nothing ever shortened them):
        // that reach remembered, fading kPeakFall a second once kPeakHold has passed without a reach that long, while a hand
        // is out kPeakOut of the arm's length or more (a full reach in play renews it; hands by the chest in a menu don't
        // shorten them), the arms toward it over kArmShrink, never under kMinArmScale. (Held 20 s, then 0.5% a second until
        // native 8a68443, 2026-11-17: a player's and the owner's videos in the mirror, arms fully extended in life and the
        // avatar's elbows bent, long forearms on the masculine body in the short-sleeved team kit; one long reach kept the
        // arms long for minutes)
        public const float kMinArmScale = 0.8f;
        const float kPeakHold = 5f;
        const float kPeakFall = 0.03f;
        const float kPeakOut = 0.85f;
        const float kArmShrink = 6f;
        const float kStraightFrom = 0.96f, kStraightAt = 0.985f;   // FULL REACH, STRAIGHT (SolveWith)
        const float kUpperShare = 1.3f;   // the upper arm takes this much of ARM SIZING's change (its share of the length), the forearm the rest

        // THE REACH: one avatar's rig across frames, its own (never shared): the torso's smoothed turn and lean toward a
        // hand out of reach, each shoulder's swing, and how long its arms have grown to the reach its hands show. Stepped
        // once a frame (StepRig), read by every draw of that frame (SolveRig).
        public sealed class RigState
        {
            public bool started;
            public double time;                    // (the caller's clock, s) when last stepped
            public Vec3 torso;                     // heading (world), pitch, roll
            public readonly Vec3[] slide = new Vec3[2];   // each shoulder's swing toward its hand (m)
            public float armScale = 1f;            // 0.8 .. 1.35
            public float reachPeak = 1f;           // ARM SIZING: the longest reach the hands have shown lately (against the arm's own)
            public double peakAt;                  // ... when a reach last came up to it
            public readonly Vec3[] lastW = new Vec3[2];   // (each wrist last frame: a jump faster than a hand is tracking, not a reach)
            public readonly bool[] handSeen = new bool[2];
            public readonly Swish swish = new Swish();   // TENNIS SKIRT / KILT
        }

        // ---------------------------------------------------------------- TENNIS SKIRT and KILT
        // (TASKS.md E13, native 481a290; PR #25, his swish.mjs) a skirt's swing about the hips, one avatar's own: the hem's
        // target is where drag and inertia carry it, against the hips' filtered speed and its change, in the garment's
        // frame, at most maxOffset; the twist lags the hips' turn. A damped spring follows them (240 steps a second), never
        // past the bounds. A jump (30 cm or 1 rad in a step), a stall (0.12 s), a step back in time or another skirt:
        // afresh, at rest. Stepped with the rig (StepRig); the shader moves each vertex by it, by its weight, and keeps it
        // out of the body (Native.shader _Swish)
        public sealed class Swish
        {
            public int kind = -1;                  // 0 the tennis skirt, 1 the kilt; -1 none
            public bool ready;
            public double time;
            public float px, pz, yaw;              // the hips last step (world)
            public float fvx, fvz, fturn;          // their speed (m/s) and turn (rad/s), filtered
            public float x, z, twist;              // the swing, in the garment's frame (m, rad)
            public float vx, vz, vt;
            public void Reset() { kind = -1; ready = false; time = 0; px = pz = yaw = 0; fvx = fvz = fturn = 0; x = z = twist = 0; vx = vz = vt = 0; }
        }
        struct SwishStyle
        {
            public float omega, damping, maxOffset, maxTwist, drag, inertia, turnLag;
            public SwishStyle(float o, float d, float mo, float mt, float dr, float i, float tl) { omega = o; damping = d; maxOffset = mo; maxTwist = mt; drag = dr; inertia = i; turnLag = tl; }
        }
        static readonly SwishStyle[] kSwish = { new SwishStyle(11f, 0.61f, 0.065f, 0.23f, 0.025f, 0.007f, 0.10f),    // the tennis skirt
                                                new SwishStyle(8f, 0.72f, 0.05f, 0.17f, 0.020f, 0.006f, 0.12f) };     // the kilt

        // the skirt worn: 0 tennis, 1 kilt, -1 none (PELURU LAYERED DRESS: its skirt sways as the tennis skirt does)
        public static int SwishKind(AvatarDesc d) => d.suit == kSuitDress ? 0 : d.lower == 5 ? 0 : d.lower == 6 ? 1 : -1;

        public static void StepSwish(Swish s, int kind, float px, float pz, float yaw, double now)
        {
            void Afresh()
            {
                s.Reset();
                s.kind = kind;
                s.ready = kind >= 0;
                s.time = now; s.px = px; s.pz = pz; s.yaw = yaw;
            }
            if (kind < 0) { if (s.kind >= 0) s.Reset(); return; }
            if (!s.ready || s.kind != kind) { Afresh(); return; }
            double dtd = now - s.time;
            if (dtd == 0) return;   // (another draw in the same frame)
            if (dtd < 0 || dtd > 0.12) { Afresh(); return; }
            float dt = (float)dtd;
            float dx = px - s.px, dz = pz - s.pz, dyaw = WrapAngle(yaw - s.yaw);
            if (MathF.Sqrt(dx * dx + dz * dz) > 0.3f || MathF.Abs(dyaw) > 1f) { Afresh(); return; }
            SwishStyle c = kSwish[kind];
            float alpha = 1f - MathF.Exp(-dt / 0.045f), ovx = s.fvx, ovz = s.fvz;
            s.fvx += (Clampf(dx / dt, -4f, 4f) - s.fvx) * alpha;
            s.fvz += (Clampf(dz / dt, -4f, 4f) - s.fvz) * alpha;
            s.fturn += (Clampf(dyaw / dt, -5f, 5f) - s.fturn) * alpha;
            float ax = Clampf((s.fvx - ovx) / dt, -12f, 12f), az = Clampf((s.fvz - ovz) / dt, -12f, 12f);
            float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
            float tx = -(cy * s.fvx - sy * s.fvz) * c.drag - (cy * ax - sy * az) * c.inertia;
            float tz = -(sy * s.fvx + cy * s.fvz) * c.drag - (sy * ax + cy * az) * c.inertia;
            float tr = MathF.Sqrt(tx * tx + tz * tz);
            if (tr > c.maxOffset) { tx *= c.maxOffset / tr; tz *= c.maxOffset / tr; }
            float tt = Clampf(-s.fturn * c.turnLag, -c.maxTwist, c.maxTwist);
            int n = Math.Max(1, (int)MathF.Ceiling(dt * 240f));
            float h = dt / n, k = c.omega * c.omega, dd = 2f * c.damping * c.omega;
            for (int i = 0; i < n; ++i)
            {
                s.vx += (k * (tx - s.x) - dd * s.vx) * h;
                s.vz += (k * (tz - s.z) - dd * s.vz) * h;
                s.vt += (k * (tt - s.twist) - dd * s.vt) * h;
                s.x += s.vx * h; s.z += s.vz * h; s.twist += s.vt * h;
                float rr = MathF.Sqrt(s.x * s.x + s.z * s.z);
                if (rr > c.maxOffset)
                {
                    s.x *= c.maxOffset / rr; s.z *= c.maxOffset / rr;
                    float o = (s.vx * s.x + s.vz * s.z) / MathF.Max(rr, 1e-9f);
                    if (o > 0) { s.vx -= o * s.x / c.maxOffset; s.vz -= o * s.z / c.maxOffset; }
                }
                if (MathF.Abs(s.twist) > c.maxTwist)
                {
                    s.twist = Clampf(s.twist, -c.maxTwist, c.maxTwist);
                    if (s.twist * s.vt > 0) s.vt = 0;
                }
            }
            s.time = now; s.px = px; s.pz = pz; s.yaw = yaw;
        }

        static float Smooth01(float x) { x = Clampf(x, 0f, 1f); return x * x * (3f - 2f * x); }
        static Vec3 XfDir(Mat4 m, Vec3 v) => new Vec3(m.m[0] * v.x + m.m[4] * v.y + m.m[8] * v.z, m.m[1] * v.x + m.m[5] * v.y + m.m[9] * v.z,
                                                      m.m[2] * v.x + m.m[6] * v.y + m.m[10] * v.z);

        static float WrapAngle(float a)
        {
            while (a > kPi) a -= 2f * kPi;
            while (a < -kPi) a += 2f * kPi;
            return a;
        }

        sealed class RigFrame
        {
            public RigTuning T;
            public BodyBind K;
            public Build B;
            public Vec3 scale, neckTop;
            public float headHeading, restOffset;   // the head's heading; the torso's at rest, against it (toward the hands)
            public readonly bool[] tracked = new bool[2];
            public readonly Vec3[] W = new Vec3[2];   // where each forearm reaches for: inside the gauntlet's cuff
            public readonly float[] natural = new float[2];   // each arm's length (tuned, not sized)
        }

        static RigFrame FrameOf(PlayerInput @in, AvatarDesc d)
        {
            var F = new RigFrame { T = Tuning, K = BodyBindOf(BodyType(d)), B = BuildOf(d.build) };
            RigTuning T = F.T;
            BodyBind K = F.K;
            Pose h = @in.head;
            F.scale = new Vec3(F.B.width, 1f, F.B.depth);
            // the head carries the neck top as in the bind pose; the torso hangs below it, so nodding or tilting the head
            // never swings the body
            F.neckTop = h.Transform(K.neckTop - K.eye + T.neck);
            // at rest: the head's heading pulled toward the hands (looking aside while throwing keeps the chest on the
            // throw), never more than torsoLimitDeg from where the head looks
            Vec3 hf = Flat(h.q.Rotate(new Vec3(0, 0, -1)));
            Vec3 base0 = F.neckTop + new Vec3(0, -K.neckTop.y, 0);
            Vec3 f = hf;
            var mid = new Vec3(0, 0, 0);
            int nt = 0;
            for (int k = 0; k < 2; ++k)
                if (@in.hand[k].tracked) { mid = mid + @in.hand[k].grip.p; ++nt; }
            if (nt != 0)
            {
                Vec3 th = mid / (float)nt - base0;
                th.y = 0;
                if (Length(th) > 0.12f) f = Normalize(hf + Normalize(th) * T.torsoFollow);
                float c50 = MathF.Cos(Deg2Rad(T.torsoLimitDeg)), s50 = MathF.Sin(Deg2Rad(T.torsoLimitDeg));
                if (Dot(f, hf) < c50)
                {
                    Vec3 perp = f - hf * Dot(f, hf);
                    perp = Length(perp) > 1e-5f ? Normalize(perp) : new Vec3(-hf.z, 0, hf.x);
                    f = Normalize(hf * c50 + perp * s50);
                }
            }
            F.headHeading = MathF.Atan2(-hf.x, -hf.z);
            F.restOffset = WrapAngle(MathF.Atan2(-f.x, -f.z) - F.headHeading);
            for (int k = 0; k < 2; ++k)
            {
                float side = k == 1 ? 1f : -1f;
                F.tracked[k] = @in.hand[k].tracked;
                // the forearm ends inside the gauntlet's bracer or cuff (RigTuning forearmEnd, carried with the cuff when the
                // tuning tool moves it; the classic lancer's wrist ring, native make_props.py)
                var fe = new Vec3(side * T.forearmEnd.x, T.forearmEnd.y, T.forearmEnd.z);
                F.W[k] = @in.hand[k].grip.Transform(PlateGauntlet(GauntletOf(@in.avatar)) ? CuffPose(k == 0).Transform(fe) : new Vec3(side * 0.004f, -0.042f, 0.105f));
                F.natural[k] = Length(K.elbow[k] - K.shoulder[k]) * T.upperLen + Length(K.wrist[k] - K.elbow[k]) * T.foreLen;
            }
            return F;
        }

        // the torso turned by a (x: heading against the head's, y: pitch, z: roll), about the neck
        static Quat TorsoRot(RigFrame F, Vec3 a) =>
            Quat.AxisAngle(new Vec3(0, 1, 0), F.headHeading + a.x) * Quat.AxisAngle(new Vec3(0, 0, 1), a.z) * Quat.AxisAngle(new Vec3(1, 0, 0), a.y);
        static Pose TorsoAt(RigFrame F, Vec3 a)
        {
            Quat q = TorsoRot(F, a);
            return new Pose(q, F.neckTop - q.Rotate(F.K.neckTop));
        }
        static Vec3 Constrain(RigFrame F, Vec3 a)
        {
            float lim = Deg2Rad(F.T.torsoLimitDeg);
            a.x = Clampf(a.x, -lim, lim);
            float tilt = MathF.Sqrt(a.y * a.y + a.z * a.z);
            if (tilt > kMaxTilt) { a.y *= kMaxTilt / tilt; a.z *= kMaxTilt / tilt; }
            return a;
        }

        // the shortest swing of the shoulder about the collarbone's root toward `wanted`, at most kMaxProtract (a swing,
        // never the collarbone sliding)
        static Quat Protraction(Vec3 root, Vec3 shoulder, Vec3 wanted)
        {
            Vec3 a = shoulder - root, b = wanted - root;
            if (Length2(a) < 1e-8f || Length2(b) < 1e-8f) return Quat.Identity;
            Vec3 na = Normalize(a), nb = Normalize(b);
            float angle = MathF.Acos(Clampf(Dot(na, nb), -1f, 1f));
            if (angle < 1e-6f) return Quat.Identity;
            Vec3 axis = Cross(na, nb);
            if (Length2(axis) < 1e-10f) axis = Cross(na, new Vec3(0, 1, 0));
            return Quat.AxisAngle(Normalize(axis), MathF.Min(angle, kMaxProtract));
        }

        struct Shoulder
        {
            public Vec3 S;        // where the arm hangs from
            public Vec3 S0;       // ... before the collarbone's forward swing
            public Vec3 wanted;   // the swing the hand asks for (m, toward it)
            public Mat4 clav;     // the collarbone's skin (bind -> world)
        }

        // arm k's shoulder on torso `torso`: lifted with a raised hand (a real one shrugs as the arm goes overhead: the
        // collarbone turned up about a point near the neck, its skin with it), then swung forward about the collarbone's
        // root by `slide` (or as the hand asks, when null); `total`: the arm's length
        static Shoulder ShoulderOf(RigFrame F, int k, Pose torso, Mat4 torsoSkin, float total, Vec3? slide)
        {
            RigTuning T = F.T;
            BodyBind K = F.K;
            float side = k == 1 ? 1f : -1f;
            var o = new Shoulder();
            Vec3 rest = torso.Transform(new Vec3(K.shoulder[k].x * F.scale.x + side * T.shoulder.x, K.shoulder[k].y + T.shoulder.y,
                                                 K.shoulder[k].z * F.scale.z + T.shoulder.z));
            // e: 0 with the hand level or below, 1 straight up (the torso's up)
            float e = F.tracked[k] ? Clampf(Dot(Normalize(F.W[k] - rest), torso.q.Rotate(new Vec3(0, 1, 0))) * 1.4f, 0f, 1f) : 0f;
            var piv = new Vec3(side * 0.05f, K.shoulder[k].y, K.shoulder[k].z);   // (the collarbone's root: near the neck)
            float ang = side * MathF.Asin(Clampf(T.shrug * e / (MathF.Abs(K.shoulder[k].x) - 0.05f), 0f, 0.8f));
            Quat cq = Quat.AxisAngle(new Vec3(0, 0, 1), ang);
            Vec3 Kr = piv + cq.Rotate(K.shoulder[k] - piv);
            o.S0 = torso.Transform(new Vec3(Kr.x * F.scale.x + side * T.shoulder.x, Kr.y + T.shoulder.y, Kr.z * F.scale.z + T.shoulder.z));
            // a hand beyond reachStart of the arm's length draws the shoulder after it (a real shoulder blade slides), up
            // to T.reach: a swing about the collarbone's root. As far as brings the hand in (native 8a68443): a swing about
            // the root doesn't carry the shoulder straight at the hand, so it swings on by what is still left, within
            // T.reach (started early, at 97% of the arm, it covered that, but held every full reach's elbow bent: a player's
            // video, 2026-11-17)
            Vec3 root = torso.Transform(new Vec3(piv.x * F.scale.x, piv.y, piv.z * F.scale.z));
            o.wanted = new Vec3(0, 0, 0);
            if (F.tracked[k])
            {
                Vec3 SW = F.W[k] - o.S0;
                float over = Length(SW) - total * T.reachStart;
                if (over > 0)
                {   // (half a millimetre inside: the forearm then ends on the gauntlet exactly)
                    Vec3 dir = Normalize(SW);
                    float inside = total * T.reachStart - 5e-4f;
                    float amt = MathF.Min(over + 5e-4f, T.reach);
                    for (int it = 0; it < 4 && amt < T.reach; ++it)
                    {
                        Vec3 Sx = root + Protraction(root, o.S0, o.S0 + dir * amt).Rotate(o.S0 - root);
                        float left = Length(F.W[k] - Sx) - inside;
                        if (left <= 0f) break;
                        amt = MathF.Min(T.reach, amt + left);
                    }
                    o.wanted = dir * amt;
                }
            }
            Quat pr = Protraction(root, o.S0, o.S0 + (slide ?? o.wanted));
            o.S = root + pr.Rotate(o.S0 - root);
            o.clav = Mat4.FromPose(new Pose(pr, root - pr.Rotate(root))) * torsoSkin * Mat4.FromPose(new Pose(cq, piv - cq.Rotate(piv)));
            return o;
        }

        // how far arm k falls short of its hand with the torso at a (0: it reaches)
        static float Shortfall(RigFrame F, int k, Vec3 a, float armScale, Vec3? slide)
        {
            if (!F.tracked[k]) return 0f;
            Pose torso = TorsoAt(F, a);
            float total = F.natural[k] * armScale;
            Shoulder s = ShoulderOf(F, k, torso, Mat4.FromPose(torso, F.scale), total, slide);
            return MathF.Max(0f, Length(F.W[k] - s.S) - total + 2e-4f);
        }

        // the torso the moment asks for: at rest, unless a hand is out of reach; then turned and leant the least that
        // brings the hands in (both hands take part; an arm that reaches at rest is kept reaching), by a deterministic
        // descent
        static Vec3 TargetPosture(RigFrame F, float armScale)
        {
            Vec3 a = Constrain(F, new Vec3(F.restOffset, 0, 0));
            float[] bas = { Shortfall(F, 0, a, armScale, null), Shortfall(F, 1, a, armScale, null) };
            if (bas[0] <= kTouch && bas[1] <= kTouch) return a;
            Vec3 rest = a;
            float Cost(Vec3 p)
            {
                float c = 0f;
                for (int k = 0; k < 2; ++k)
                {
                    float r = Shortfall(F, k, p, armScale, null);
                    if (bas[k] <= kTouch && r > kTouch) return 1000f + r * r;   // (don't pull a reaching arm away)
                    c += r * r;
                }
                return c + 0.00001f * Length2(p - rest);
            }
            float best = Cost(a);
            for (int it = 0; it < 100; ++it)
            {
                var g = new Vec3();
                const float eps = 0.0002f;
                for (int ax = 0; ax < 3; ++ax)
                {
                    Vec3 plus = a, minus = a;
                    if (ax == 0) { plus.x += eps; minus.x -= eps; } else if (ax == 1) { plus.y += eps; minus.y -= eps; } else { plus.z += eps; minus.z -= eps; }
                    float gv = (Cost(Constrain(F, plus)) - Cost(Constrain(F, minus))) / (2f * eps);
                    if (ax == 0) g.x = gv; else if (ax == 1) g.y = gv; else g.z = gv;
                }
                float g2 = Length2(g);
                if (g2 < 1e-14f) break;
                Vec3 step = g * -MathF.Min(best / g2, 0.05f / MathF.Sqrt(g2));
                bool better = false;
                for (int b = 0; b < 14; ++b)
                {
                    Vec3 trial = Constrain(F, a + step);
                    float v = Cost(trial);
                    if (v < best - 1e-12f) { a = trial; best = v; better = true; break; }
                    step = step * 0.5f;
                }
                if (!better) break;
            }
            return a;
        }

        // the rig for torso a, the shoulders' swings (null: as the hands ask) and the arms' length
        static Rig SolveWith(RigFrame F, PlayerInput @in, AvatarDesc d, Vec3 a, Vec3[] slides, float armScale)
        {
            var r = new Rig();
            RigTuning T = F.T;
            BodyBind K = F.K;
            Build B = F.B;
            Quat q = TorsoRot(F, a);
            r.torso = new Pose(q, F.neckTop - q.Rotate(K.neckTop));
            Vec3 fwd = q.Rotate(new Vec3(0, 0, -1));
            float neckLen = Length(K.neckTop);
            r.neck = new Bone(F.neckTop, r.torso.p, fwd, neckLen);
            var bindNeck = new Bone(K.neckTop, new Vec3(0, 0, 0), new Vec3(0, 0, -1), neckLen);
            r.skin[(int)BoneId.Torso] = Mat4.FromPose(r.torso, F.scale);
            r.skin[(int)BoneId.Head] = Mat4.FromPose(@in.head) * Mat4.FromPose(new Pose(Quat.Identity, new Vec3(0, 0, 0) - K.eye));
            r.skin[(int)BoneId.Neck] = BoneMatrix(r.neck, false, 1f) * AffineInverse(BoneMatrix(bindNeck, false, 1f));
            r.armScale = armScale;
            {   // the hips: the torso upright (its heading, no lean), hinged to it at the waist
                TorsoShape TSw = kTorsoShape[BodyType(d)];
                float wi = Clampf((kWaistY - TorsoY0) / TorsoDY, 0f, TorsoSlices - 1);
                int i0 = (int)wi, i1 = Math.Min(TorsoSlices - 1, i0 + 1);
                var waist = new Vec3(0, kWaistY, TSw.cz[i0] + (TSw.cz[i1] - TSw.cz[i0]) * (wi - i0));
                var waistScaled = new Vec3(0, waist.y, waist.z * F.scale.z);
                Quat up = Quat.AxisAngle(new Vec3(0, 1, 0), F.headHeading + a.x);
                r.pelvis = Mat4.FromPose(new Pose(up, r.torso.Transform(waistScaled) - up.Rotate(waistScaled)), F.scale);
                r.bent = MathF.Abs(a.y) + MathF.Abs(a.z) > 1e-4f;
            }
            for (int k = 0; k < 2; ++k)
            {
                float side = k == 1 ? 1f : -1f;
                // natural (bind) lengths for the meshes; the IK uses the tuned and sized ones (the meshes stretch to match)
                float L1 = Length(K.elbow[k] - K.shoulder[k]), L2 = Length(K.wrist[k] - K.elbow[k]);
                // (ARM SIZING's change mostly above the elbow, the reach the same, native 8a68443: these avatars' forearms are
                // already as long as their upper arms, a real one about 4/5; lengthened evenly a long arm's forearm read as
                // Popeye's, the owner's video of the masculine body in the short-sleeved team kit, 2026-11-17)
                float L1u = L1 * T.upperLen, L2f = L2 * T.foreLen, g = armScale - 1f;
                float L1t = L1u * (1f + g * kUpperShare), L2t = L2f * (1f + g * (1f - (kUpperShare - 1f) * L1u / L2f));
                var bu = new Bone(K.shoulder[k], K.elbow[k], (K.wrist[k] - K.elbow[k]) + new Vec3(0, 0, -0.3f), L1);
                var bf = new Bone(K.elbow[k], K.wrist[k], K.thumb[k] - K.wrist[k], L2);
                HandInput hi = @in.hand[k];
                r.skin[(int)BoneId.ClavL + k] = r.skin[(int)BoneId.Torso];   // (the collarbone as bound, unless an arm moves it, below)
                if (!hi.tracked)
                {   // arms at rest: the bind pose, turned with the torso
                    Vec3 S = r.torso.Transform(new Vec3(K.shoulder[k].x * F.scale.x + side * T.shoulder.x, K.shoulder[k].y + T.shoulder.y,
                                                        K.shoulder[k].z * F.scale.z + T.shoulder.z));
                    r.upper[k] = new Bone(S, r.torso.Transform(K.elbow[k]), q.Rotate(bu.front), L1);
                    r.fore[k] = new Bone(r.upper[k].b, r.torso.Transform(K.wrist[k]), q.Rotate(bf.front), L2);
                }
                else
                {
                    r.arm[k] = true;
                    Shoulder sh = ShoulderOf(F, k, r.torso, r.skin[(int)BoneId.Torso], L1t + L2t, slides != null ? slides[k] : (Vec3?)null);
                    r.skin[(int)BoneId.ClavL + k] = sh.clav;
                    Vec3 S = sh.S, W = F.W[k];
                    Vec3 back = hi.grip.q.Rotate(Normalize(new Vec3(0, -0.047f, 1f)));
                    // elbow: where a straight wrist would put it, nudged down / out / back so the IK stays stable; a hand
                    // raised above the shoulder turns it out to the side instead of hanging down (native c758fd3, owner's
                    // report 2026-10-27: arms raised to the ceiling folded the shoulders in)
                    float e = Clampf(Dot(Normalize(W - sh.S0), q.Rotate(new Vec3(0, 1, 0))) * 1.4f, 0f, 1f);
                    Vec3 hang = q.Rotate(new Vec3(side * T.elbowPole.x, T.elbowPole.y, T.elbowPole.z));
                    Vec3 outw = q.Rotate(new Vec3(side * 1.0f, 0.15f, 0.35f));
                    Vec3 pole = W + back * (L2t * (1f - 0.6f * e)) + Normalize(Lerp(hang, outw, e)) * (T.elbowPull + 0.25f * e);
                    // never stretched: the forearm ends on the arm's reach; what is left is a gap to the gauntlet (ARM SIZING)
                    Vec3 SW = W - S;
                    float dist = Length(SW);
                    Vec3 dir = dist > 1e-5f ? SW / dist : q.Rotate(new Vec3(side, 0, 0));
                    // FULL REACH, STRAIGHT (native 8a68443; a player's video and the owner's, 2026-11-17: arms fully extended
                    // in life, the avatar's elbows bent): a hand within kStraightAt of the arm's reach is a full reach, the arm
                    // drawn straight, its bones that much shorter (from kStraightFrom, more and more so). An arm sized even 1%
                    // long was bent 11 degrees there, and 3% short of its reach a real arm is bent 28: tracking can't tell
                    float Lu = L1t, Lf = L2t;
                    {
                        float ext = dist / (L1t + L2t);
                        if (ext > kStraightFrom && ext < 1f)
                        {
                            float f1 = MathF.Min(1f, (dist + 2.5e-4f) / (L1t + L2t));
                            float f = 1f + (f1 - 1f) * Smooth01((ext - kStraightFrom) / (kStraightAt - kStraightFrom));
                            Lu *= f;
                            Lf *= f;
                        }
                    }
                    Vec3 Wd = S + dir * Clampf(dist, MathF.Abs(Lu - Lf) + 1e-3f, Lu + Lf - 2e-4f);
                    r.reachGap[k] = Length(W - Wd);
                    Vec3 E = ElbowIK(S, Wd, Lu, Lf, pole);
                    // ELBOWS OUT OF THE BODY (owner, 2026-11-16): swung out round the shoulder-to-wrist line, only as far as
                    // keeps the upper arm and forearm out of the torso (hands together at the chest put them inside it)
                    TorsoShape TS = kTorsoShape[BodyType(d)];
                    float thick = B.arm * T.armThick * T.armClearShare;
                    E = ElbowClear(TS, r.torso, F.scale, side, S, Wd, E, TS.upperArm * thick, TS.foreArm * thick, T.elbowAllow);
                    r.upper[k] = new Bone(S, E, (Wd - E) + fwd * 0.3f, L1);   // the inner elbow faces the forearm
                    r.fore[k] = new Bone(E, Wd, hi.grip.q.Rotate(new Vec3(0, 1, 0)), L2);   // thumb side (controller top) is the front
                }
                bool mir = k == 0;
                r.skin[(int)BoneId.UpperL + k] = BoneMatrix(r.upper[k], mir, B.arm * T.armThick) * AffineInverse(BoneMatrix(bu, mir, 1f));
                r.skin[(int)BoneId.ForeL + k] = BoneMatrix(r.fore[k], mir, B.arm * T.armThick) * AffineInverse(BoneMatrix(bf, mir, 1f));
                // FOREARM TWIST (native 0a41fe7): the forearm as the upper arm carries it (its bind direction and front,
                // turned the least way onto where the forearm points) is the untwisted one; the roll is how far the hand's
                // turns it from that, about the forearm. More than 135 degrees from the hand held thumbs up it fades out (a
                // wrist can't turn that far; at 180 the turn's side is a guess: no snap from one side to the other)
                Mat4 Mu = r.skin[(int)BoneId.UpperL + k], Mf = r.skin[(int)BoneId.ForeL + k];
                Vec3 dv = K.wrist[k] - K.elbow[k];
                Vec3 bfront = bf.front - dv * (Dot(bf.front, dv) / MathF.Max(1e-8f, Dot(dv, dv)));
                Vec3 Ew = Mf.TransformPoint(K.elbow[k]), dw = XfDir(Mf, dv);
                float Lw = MathF.Max(1e-4f, Length(dw));
                Vec3 ax = dw / Lw;
                Vec3 a1 = XfDir(Mu, dv);
                Vec3 f0 = FromTo(Normalize(a1), ax).Rotate(XfDir(Mu, bfront)), fa = XfDir(Mf, bfront);
                f0 = f0 - ax * Dot(f0, ax);
                fa = fa - ax * Dot(fa, ax);
                float roll = 0f;
                if (Length2(f0) > 1e-10f && Length2(fa) > 1e-10f) roll = MathF.Atan2(Dot(Cross(f0, fa), ax), Dot(f0, fa));
                // (about the hand held thumbs up, where the forearm is turned some 60 degrees out from the bind's (the PR #25
                // bodies' thumbs, native 5992aae): the turns a wrist makes, about 90 degrees either way, stay clear of the fade)
                float neutral = side * 1.0471976f, rel = WrapAngle(roll - neutral);
                roll = neutral + rel;
                r.roll[k] = roll;
                float fade = 1f - Smooth01((MathF.Abs(rel) - 2.356194f) / (kPi - 2.356194f));
                float[][] v = {
                    new[] { K.elbow[k].x, K.elbow[k].y, K.elbow[k].z, 1f / (0.96f * MathF.Max(1e-8f, Dot(dv, dv))) },
                    new[] { dv.x, dv.y, dv.z, roll * fade },
                    new[] { Ew.x, Ew.y, Ew.z, 1f / (0.96f * Lw) },
                    new[] { ax.x, ax.y, ax.z, 0f } };
                for (int j = 0; j < 4; ++j) System.Array.Copy(v[j], 0, r.twist, (2 * j + k) * 4, 4);
                if (MathF.Abs(roll * fade) > 1e-4f) r.twisted = true;
            }
            return r;
        }

        // the rig: with st, as it stands this frame (StepRig); without, the posture the moment asks for, arms unsized
        public static Rig SolveRig(PlayerInput @in, AvatarDesc d, RigState st = null)
        {
            RigFrame F = FrameOf(@in, d);
            if (st != null && st.started)
            {
                Vec3 a = Constrain(F, new Vec3(WrapAngle(st.torso.x - F.headHeading), st.torso.y, st.torso.z));
                return SolveWith(F, @in, d, a, st.slide, st.armScale);
            }
            float scale = st != null ? st.armScale : 1f;
            return SolveWith(F, @in, d, TargetPosture(F, scale), null, scale);
        }

        // THE REACH: one step of an avatar's rig state, to `now` (s, the caller's clock; again within the same frame:
        // nothing)
        public static void StepRig(RigState st, PlayerInput @in, AvatarDesc d, double now)
        {
            if (st.started && now - st.time < 0.0005) return;   // (stepped already this frame: the other draws use it)
            RigFrame F = FrameOf(@in, d);
            float dt = st.started ? Clampf((float)(now - st.time), 0f, 0.1f) : 0f;
            Vec3 target = TargetPosture(F, st.armScale);
            Vec3 posture = target;
            if (st.started)
            {
                // toward what the moment asks, smoothly (frame-rate independent), never faster than kTorsoSpeed
                Vec3 prev = Constrain(F, new Vec3(WrapAngle(st.torso.x - F.headHeading), st.torso.y, st.torso.z));
                Vec3 delta = (target - prev) * (1f - MathF.Exp(-dt / kRigTau));
                float dl = Length(delta), maxStep = kTorsoSpeed * dt;
                if (dl > maxStep && dl > 0) delta = delta * (maxStep / dl);
                posture = Constrain(F, prev + delta);
                // a hand that the target reaches but the smoothed torso leaves short: as much further toward the target as
                // it takes (contact first), then the whole step bounded again (never a snap)
                float Worst(Vec3 p) => MathF.Max(Shortfall(F, 0, p, st.armScale, null), Shortfall(F, 1, p, st.armScale, null));
                if (Worst(posture) > kTouch && Worst(target) <= kTouch)
                {
                    float lo = 0, hi = 1;
                    for (int i = 0; i < 16; ++i) { float m = (lo + hi) * 0.5f; if (Worst(Lerp(posture, target, m)) <= kTouch) hi = m; else lo = m; }
                    posture = Lerp(posture, target, hi);
                }
                Quat oldQ = TorsoRot(F, prev);
                float Turned(Vec3 p)
                {
                    Quat qq = TorsoRot(F, p);
                    float c = MathF.Abs(oldQ.x * qq.x + oldQ.y * qq.y + oldQ.z * qq.z + oldQ.w * qq.w);
                    return 2f * MathF.Acos(Clampf(c, 0f, 1f));
                }
                if (Turned(posture) > maxStep)
                {
                    Vec3 want = posture;
                    float lo = 0, hi = 1;
                    for (int i = 0; i < 18; ++i) { float m = (lo + hi) * 0.5f; if (Turned(Lerp(prev, want, m)) <= maxStep) lo = m; else hi = m; }
                    posture = Lerp(prev, want, lo);
                }
            }
            st.torso = new Vec3(F.headHeading + posture.x, posture.y, posture.z);
            // each shoulder's swing, the same way (40 cm a second at most, contact first)
            Pose torso = TorsoAt(F, posture);
            StepSwish(st.swish, SwishKind(d), torso.p.x, torso.p.z, st.torso.x, now);   // (TENNIS SKIRT / KILT)
            Mat4 torsoSkin = Mat4.FromPose(torso, F.scale);
            for (int k = 0; k < 2; ++k)
            {
                float total = F.natural[k] * st.armScale;
                Shoulder want = ShoulderOf(F, k, torso, torsoSkin, total, null);
                if (!st.started || !F.tracked[k]) { st.slide[k] = want.wanted; continue; }
                int kk = k;
                float Gap(Vec3 s)
                {
                    Shoulder o = ShoulderOf(F, kk, torso, torsoSkin, total, s);
                    return MathF.Max(0f, Length(F.W[kk] - o.S) - total + 2e-4f);
                }
                float maxStep = kSlideSpeed * dt;
                Vec3 sl = st.slide[k] + (want.wanted - st.slide[k]) * (1f - MathF.Exp(-dt / kRigTau));
                if (Length(sl - st.slide[k]) > maxStep) sl = st.slide[k] + Normalize(sl - st.slide[k]) * maxStep;
                if (Gap(sl) > kTouch && Gap(want.wanted) <= kTouch)
                {
                    float lo = 0, hi = 1;
                    for (int i = 0; i < 16; ++i) { float m = (lo + hi) * 0.5f; if (Gap(Lerp(sl, want.wanted, m)) <= kTouch) hi = m; else lo = m; }
                    sl = Lerp(sl, want.wanted, hi);
                }
                if (Length(sl - st.slide[k]) > maxStep) sl = st.slide[k] + Normalize(sl - st.slide[k]) * maxStep;
                if (Length(sl) > F.T.reach) sl = Normalize(sl) * F.T.reach;
                st.slide[k] = sl;
            }
            // ARM SIZING: how far each hand gets from its shoulder on the torso at rest (no turn or lean toward the hand),
            // with the shoulder's own forward swing as the hand asks (a real shoulder blade's, up to T.reach): the player's
            // arm, not what the body does to reach. A reach longer than the arms lengthens them toward it over kArmSizing
            // (a jump faster than a hand moves is tracking, not a reach). So the arms grow only past what the shoulder
            // covers, and the lean is for reaches past those
            Pose restTorso = TorsoAt(F, Constrain(F, new Vec3(F.restOffset, 0, 0)));
            Mat4 restSkin = Mat4.FromPose(restTorso, F.scale);
            float peakNow = 0f;   // (this frame's longest reach, either hand)
            for (int k = 0; k < 2; ++k)
            {
                if (!F.tracked[k]) { st.handSeen[k] = false; continue; }
                Shoulder sh = ShoulderOf(F, k, restTorso, restSkin, F.natural[k] * st.armScale, null);
                float ratio = Length(F.W[k] - sh.S) / MathF.Max(0.1f, F.natural[k]);
                bool jumped = st.handSeen[k] && Length(F.W[k] - st.lastW[k]) > 8f * MathF.Max(dt, 1f / 90f);
                // (a hand across the body is no measure of the arm: the real shoulder turns and reaches with it, so from the
                // shoulder at rest it looks further than the arm is long: pointing at a menu across the body grew the arms)
                Vec3 o = sh.S - restTorso.p;
                o.y = 0f;
                bool ownSide = Length(o) < 1e-4f || Dot(F.W[k] - restTorso.p, Normalize(o)) > -0.05f;
                if (st.started && dt > 0 && !jumped && ownSide && ratio > st.armScale && ratio < 1.6f)
                    st.armScale = MathF.Min(kMaxArmScale, st.armScale + (ratio - st.armScale) * (1f - MathF.Exp(-dt / kArmSizing)));
                if (!jumped && ownSide && ratio < 1.6f) peakNow = MathF.Max(peakNow, ratio);
                st.lastW[k] = F.W[k];
                st.handSeen[k] = true;
            }
            if (!st.started)
            {
                st.reachPeak = st.armScale;   // (the arms' own length: nothing shortens them before reaches are seen)
                st.peakAt = now;
            }
            else if (dt > 0)
            {
                // (a reach as long as the one remembered: held again. Within 2% until native 8a68443: the fade stopped 2%
                // above the reach the hands showed, and an arm 2% too long is bent 24 degrees at its full reach)
                if (peakNow >= st.reachPeak - 0.001f) st.peakAt = now;
                // (fading only while a hand is out at arm's length, reaching shorter than remembered: hands by the chest in a
                // menu don't shorten them)
                float fall = now - st.peakAt > kPeakHold && peakNow >= kPeakOut ? kPeakFall * dt : 0f;
                st.reachPeak = MathF.Max(peakNow, st.reachPeak - fall);
                float toward = MathF.Max(kMinArmScale, st.reachPeak);
                if (st.armScale > toward) st.armScale += (toward - st.armScale) * (1f - MathF.Exp(-dt / kArmShrink));
            }
            st.started = true;
            st.time = now;
        }

        // Model matrix for a bone mesh: joint a at the origin, -Y along the bone (scaled to its length), -Z toward front;
        // mirrorX for the left side (negative determinant: flip the winding when drawing); thick scales the cross-section.
        public static Mat4 BoneMatrix(Bone b, bool mirrorX, float thick = 1f)
        {
            Vec3 axis = b.a - b.b;
            float len = Length(axis);
            Vec3 y = len > 1e-5f ? axis / len : new Vec3(0, 1, 0);
            Vec3 fr = b.front - y * Dot(b.front, y);
            if (Length2(fr) < 1e-8f)
            {
                fr = new Vec3(0, 0, -1) - y * (-y.z);
                if (Length2(fr) < 1e-8f) fr = new Vec3(1, 0, 0) - y * y.x;
            }
            Vec3 z = -Normalize(fr);
            Vec3 x = Cross(y, z);
            if (mirrorX) x = -x;
            float s = len / b.natural;
            return Mat4.FromBasis(x * thick, y * s, z * thick, b.a);
        }

        // What to draw for one avatar (hands / gauntlets are drawn by the caller): model name, world matrix, winding.
        // part glows (neon hair); skinned body part (uses the rig's skin); its texture scrolls along v (CIRCUIT RUNNER
        // pulses); PART_HAIR: hair (head space). The game swings the styles that move (their spring chains:
        // av_hair_*.swing) and tucks hair inside the headgear and face shield worn (av_gear_* / av_visor_*.tuck)
        public const byte PartNeon = 1, PartSkinned = 2, PartPulse = 4, PartHair = 8;

        public struct PartDraw { public string model; public Mat4 m; public bool mirrored; public byte flags; }

        public static void DrawList(Rig r, PlayerInput @in, AvatarDesc din, List<PartDraw> o, bool lod = false)
        {
            AvatarDesc d = Uniform(din);   // (sanitized; with a country, the team kit)
            char bk = BodyType(d) != 0 ? 'f' : 'm';
            int face = FaceOf(d);
            string fk = face != 0 ? $"{bk}{face}" : $"{bk}";   // the face's own parts (head, face shields, masks): m / m1 / m2 / m3
            void Add(string model, Mat4 m, bool mirrored, byte flags = 0) =>
                o.Add(new PartDraw { model = model.Length > 23 ? model.Substring(0, 23) : model, m = m, mirrored = mirrored, flags = flags });
            Mat4 head = Mat4.FromPose(@in.head, new Vec3(kHeadScale, kHeadScale, kHeadScale));
            Mat4 I = Mat4.Identity();
            Add($"av_top_{d.suit}_{bk}", I, false, PartSkinned);
            Add($"av_bhead_{fk}", I, false, PartSkinned);
            if (d.suit == kSuitDress) { }   // PELURU LAYERED DRESS: its skirt in the LOWER BODY's place (nothing under it)
            else if (d.lower >= 1 && d.lower <= 4 && (d.suit == 14 || d.suit == 15)) Add($"av_sbottom_{d.suit}_{d.lower}_{bk}", I, false, PartSkinned);   // (VECTOR and NIGHTFORGE: their own shorts)
            else if (d.lower != 0) Add($"av_bottom_{d.lower}_{bk}", I, false, PartSkinned);
            else Add($"av_wfade_{bk}", I, false, PartSkinned);
            if (d.headgear != 0) Add($"av_gear_{d.headgear}_{bk}", head, false);
            else if (lod && d.hair != 0) Add($"av_hair_{d.hair}_{bk}", head, false, NeonHair(d) ? PartNeon : (byte)0);
            if (lod) return;   // miniatures: the silhouette is enough
            if (d.suit >= 10 && d.suit <= 13) Add($"av_tpulse_{d.suit}_{bk}", I, false, PartSkinned | PartPulse);   // CIRCUIT RUNNER and the LIVE suits' pulses
            // hair under every headgear: the game keeps it inside the helmet, hood or cap, so what shows is what would (a
            // ponytail from under a helmet's rim, a fringe in a hood)
            if (d.hair != 0)
            {
                byte hfl = (byte)(PartHair | (NeonHair(d) ? PartNeon : 0));
                Add($"av_hair_{d.hair}_{bk}", head, false, hfl);
                if (d.hair == 3 || d.hair == 7 || d.hair == 9) Add($"av_hair_{d.hair}t_{bk}", head, false, hfl);   // PONYTAIL's tail, TOP KNOT's ends, HIGH PIGTAILS (they swing)
            }
            // face shields and masks are fitted to each face (the game falls back to the base head's if one is missing)
            if (d.visor != 0) Add($"av_visor_{d.visor}_{fk}", head, false);
            // (the BANDANA's point below the chin swings, its cloth drawn as hair is: av_mask_4_*.swing; native 27cad43)
            if (d.mask != 0) Add($"av_mask_{d.mask}_{fk}", head, false, d.mask == 4 ? PartHair : (byte)0);
            // BANDANA: its two tails, a part of their own (one per body, every face): they swing as hair does (TASKS.md E35,
            // native 988824c, PR #28)
            if (d.mask == 4) Add($"av_mask_4t_{bk}", head, false, PartHair);
            if (d.suit == kSuitDress) Add($"av_top_{kSuitDress}e_{bk}", head, false, PartHair);   // PELURU LAYERED DRESS: its hoop earrings, which swing as hair does
        }
    }
}
