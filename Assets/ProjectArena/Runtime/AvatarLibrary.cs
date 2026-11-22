using UnityEngine;

namespace ProjectArena
{
    // The avatar art the game draws by name (native initContent: avModels, the .swing and .tuck files, the suit
    // patterns, the countries' flags, the gauntlets). Filled by the editor (ProjectConfigure.AvatarLibraryAsset) from
    // Art/Open; read by AvatarDraw.
    public class AvatarLibrary : ScriptableObject
    {
        public GameObject[] parts;          // av_*.mdl (the skinned bodies and the head parts)
        public TextAsset[] swings;          // av_*.swing.bytes (hair that moves, the BANDANA's point and tails)
        public TextAsset[] tucks;           // av_gear_* / av_visor_*.tuck.bytes (hair under headgear)
        public TextAsset[] swishes;         // av_*.swish.bytes (TENNIS SKIRT / KILT, the PELURU dress: their sway shapes)
        public Texture2D[] patterns;        // open_pat_* (the suit patterns, by name)
        public Texture2D[] flags;           // flag_* (192 x 128, by name)
        public GameObject[] gauntletHands;  // gauntlet_K_hand by style (the plate gauntlets 0-2, 4-6; 3, the CONTROLLER: none)
        public GameObject[] gauntletCuffs;  // gauntlet_K_cuff
        // the grapple gauntlets (4-6; TASKS.md E35, native 27cad43): by style, the fist (the hand with its fingers closed: the
        // HANDS APART shape), the five fingers (style * 5 + finger: four and the thumb) and gauntlet_K.hinge; the tether's
        // beam and flare
        public GameObject[] gauntletFists;
        public GameObject[] gauntletFingers;
        public TextAsset[] gauntletHinges;
        public GameObject grappleBeam, grappleFlare;
        public GameObject lancer;           // CONTROLLER: the plain lancer
        public Texture2D crowdFoam, crowdHeart, crowdStar, crowdHotdog, crowdSmile;   // The Crowd: fx_foam, fx_heart, ...
    }
}
