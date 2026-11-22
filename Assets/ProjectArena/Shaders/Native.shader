// The native game's shader (app/src/render.cpp kVS / kFS), ported to URP line by line. The native game does its own
// lighting (one fixed key light, a hemisphere term and a painted stand-in for the arena's reflections), so this is an
// unlit URP pass that does the same maths; colours work the same way (linear maths, sRGB textures and output).
// Kinds (keywords, set by MdlImporter from the .mdl shader number):
//   _PA_LIT        0 lit / 4 translucent (blended)   _PA_ADD       2 additive (scrolling UVs)
//   _PA_UNLIT      3 unlit                           _PA_LIGHTMAP  5 lightmapped (baked light on UV set 1)
// Per draw (MaterialPropertyBlock, the native DrawParams): _TintMul (tintMul, w = alphaMul), _UVOff, _EmissiveAdd,
// _Rim (the team-colour rim light; a > 0 also turns on the character lighting of avatar parts and gauntlets),
// _UseChannels + _ChanCols[8] (the look's colour channels: primary, secondary, glow, skin, visor, hair, trim).
// Avatar features (per draw, Runtime/AvatarDraw.cs): _Skinned + _Bones[9] (linear blend skinning, bones in UV 2 / 3),
// _TuckOn + _TuckC + _TuckTex (hair kept inside headgear), _PatternOn + _PatternTex (the suit pattern on materials
// flagged _PatternMat), _Decals + _DecalM0 / _DecalM1 + _DecalTex (prints on a garment: native DrawParams.decal, the
// country's flag on the jersey's back and chest, 0.3.17); per material: _DesignMat (garment design mask: R trim, G glow, B white), _ClothMat + _FabricTex
// (knit and sheen), _LitOnly (native SH_LIT, not translucent: the only kind those features and character lighting use).
Shader "ProjectArena/Native"
{
    Properties
    {
        _Tint ("Tint", Vector) = (1, 1, 1, 1)
        _Emissive ("Emissive (rgb), gloss / metallic (a)", Vector) = (0, 0, 0, 0)
        _MainTex ("Diffuse", 2D) = "white" {}
        _HasDiffuse ("Has diffuse", Float) = 0
        _MaskTex ("Mask", 2D) = "black" {}
        _HasMask ("Has mask", Float) = 0
        _UVSet ("UV set (0 / 1)", Float) = 0
        _UVScroll ("UV scroll per second (additive)", Vector) = (0, 0, 0, 0)
        _Channel ("Colour channel (8 / 9: team ends)", Float) = 0
        _TintMul ("Tint multiplier (per draw: team colours)", Vector) = (1, 1, 1, 1)
        _UVOff ("UV offset (per draw, additive)", Vector) = (0, 0, 0, 0)
        _EmissiveAdd ("Emissive added (per draw)", Vector) = (0, 0, 0, 0)
        _Rim ("Rim light (per draw; a > 0: character lighting)", Vector) = (0, 0, 0, 0)
        _UseChannels ("Use the colour channels (per draw)", Float) = 0
        _LitOnly ("Native lit kind (0), not translucent", Float) = 0
        _PatternMat ("Takes the suit pattern", Float) = 0
        _DesignMat ("Garment design (mask)", Float) = 0
        _ClothMat ("Garment fabric", Float) = 0
        _FabricTex ("Fabric knit", 2D) = "white" {}
        _FlipFace ("Mirrored draw (front faces flipped)", Float) = 0
        _Cuttable ("THE CUTAWAY (PC presets): 1 the room, -1 its mirror image", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Blend source", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Blend destination", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendA ("Blend source alpha", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendA ("Blend destination alpha", Float) = 0
        _ZWrite ("Depth write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "Native"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _PA_LIT _PA_ADD _PA_UNLIT _PA_LIGHTMAP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Cutaway.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            TEXTURE2D(_FabricTex); SAMPLER(sampler_FabricTex);
            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);
            TEXTURE2D(_TuckTex); SAMPLER(sampler_TuckTex);
            TEXTURE2D(_DecalTex); SAMPLER(sampler_DecalTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _Emissive;
                float4 _MainTex_ST;
                float4 _MaskTex_ST;
                float4 _UVScroll;
                float4 _TintMul;      // native DrawParams.tintMul (set per renderer with a MaterialPropertyBlock)
                float4 _UVOff;        // native DrawParams.uvOff
                float4 _EmissiveAdd;  // native DrawParams.emissiveAdd
                float4 _Rim;          // native DrawParams.rim
                float _UseChannels;   // native DrawParams.channels != null
                float _HasDiffuse;
                float _HasMask;
                float _UVSet;
                float _Channel;
                float _LitOnly;
                float _PatternMat;
                float _DesignMat;
                float _ClothMat;
                float4 _FabricTex_ST;
                float _FlipFace;      // a mirrored copy of the material (AvatarDraw: native DrawParams.mirrored)
                float _Cuttable;      // THE CUTAWAY (TASKS.md E42, Shaders/Cutaway.hlsl): 1 the SQUARE's walls, -1 their mirror image
            CBUFFER_END
            // set by ArenaLook (the native Renderer's uniforms)
            float3 _PA_LightDir;   // the key light's direction of travel, Unity space
            float4 _PA_Side0;      // end colours (rgb) used when _PA_SidesOn: a country's colours, crowd flare
            float4 _PA_Side1;
            float _PA_SidesOn;
            float _PA_Grade;       // COLOR GRADING (TASKS.md E38, ArenaGraphics): 1 on
            float _PA_FoulMove;    // the FOUL LINE moved toward the centre, metres (0: where the arena has it; CourtStretch)
            float4 _ChanCols[8];   // per draw: the look's channel colours (native DrawParams.channels)
            float4x4 _Bones[9];    // per draw: bind -> world of the skinned parts (Unity space), native DrawParams.bones (Avatar.BoneId: nine since native c758fd3)
            float _Skinned;
            float _Bend;           // THE REACH: bending at the waist: bone 0 is _Pelvis below it, _Bones[0] above (bind-space height)
            float4x4 _Pelvis;
            float _Twist;          // FOREARM TWIST: the forearm's skin turned back toward the elbow (Avatar.Rig.twist, AvatarDraw.TwistToUnity)
            float4 _Tw[8];
            float4 _Squash;        // THE PUNCH LANDS: a ball just punched, squashed along xyz (object space) by w, bulging half that
            float _Swish;          // TENNIS SKIRT / KILT (PR #25): swung about the hips, kept out of the body (Avatar.Swish)
            float4 _Sw;            // the swing: x, z (m, the garment's frame, native axes), twist (rad), lift (m)
            float4 _SwC;           // the hips' centre x, z (native axes), the most a vertex moves (m)
            float4 _SwP;           // the body's radii (_SwTex): the first row's height, 1 / row step, rows, columns
            TEXTURE2D(_SwTex);
            float _TuckOn;         // per draw: hair under headgear (native DrawParams.tuck / tuckC, model space)
            float4 _TuckC;
            float _PatternOn;      // per draw: the look's suit pattern (native DrawParams.pattern)
            float _Decals;         // per draw: prints on the garment (native DrawParams.decals, 0-2): each box maps the
            float4x4 _DecalM0;     // garment's bind-space position (Unity axes in, native axes out: z through the cloth)
            float4x4 _DecalM1;     // to a unit box

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 color : COLOR;
                float4 bone : TEXCOORD2;     // skinned models: bone indices
                float4 weight : TEXCOORD3;   // and weights
                float2 court : TEXCOORD4;    // the court's models: each section's part when the FOUL LINE moves (CourtStretch)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : TEXCOORD3;
                float3 dec0 : TEXCOORD4;     // (native vDec0 / vDec1 / vDecN: where this point lies in each print's box,
                float3 dec1 : TEXCOORD5;     // and whether its cloth faces out of the box)
                float2 decN : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // TENNIS SKIRT / KILT: the body's radius at this height and angle (native render.cpp swishRadius, his radialRadius)
            float SwishRadius(float y, float th)
            {
                int rows = (int)_SwP.z, cols = (int)_SwP.w;
                float fy = clamp((y - _SwP.x) * _SwP.y, 0.0, (float)(rows - 1));
                int lo = (int)floor(fy), hi = min(lo + 1, rows - 1);
                float u = frac(th * 0.1591549 + 1.0) * cols;
                int i = ((int)floor(u)) % cols, j = (i + 1) % cols;
                float f = u - floor(u);
                float ra = lerp(LOAD_TEXTURE2D_LOD(_SwTex, int2(i, lo), 0).r, LOAD_TEXTURE2D_LOD(_SwTex, int2(j, lo), 0).r, f);
                float rb = lerp(LOAD_TEXTURE2D_LOD(_SwTex, int2(i, hi), 0).r, LOAD_TEXTURE2D_LOD(_SwTex, int2(j, hi), 0).r, f);
                return lerp(ra, rb, fy - lo);
            }

            // FOREARM TWIST (native 0a41fe7, render.cpp): a turn by th about the line through E along ax
            float4x4 TwistTurn(float3 E, float3 ax, float th)
            {
                float c = cos(th), sn = sin(th), t = 1.0 - c;
                float3x3 R = float3x3(c + t * ax.x * ax.x, t * ax.x * ax.y - sn * ax.z, t * ax.x * ax.z + sn * ax.y,
                                      t * ax.x * ax.y + sn * ax.z, c + t * ax.y * ax.y, t * ax.y * ax.z - sn * ax.x,
                                      t * ax.x * ax.z - sn * ax.y, t * ax.y * ax.z + sn * ax.x, c + t * ax.z * ax.z);
                float3 o = E - mul(R, E);
                return float4x4(R[0].x, R[0].y, R[0].z, o.x, R[1].x, R[1].y, R[1].z, o.y, R[2].x, R[2].y, R[2].z, o.z, 0, 0, 0, 1);
            }
            // one influence of a forearm (bones 5, 6): its bone turned back about the forearm by -roll at the elbow, none
            // from 96% of the way to the wrist (smoothly); g: the turn's change along the arm, for the normal
            void TwistBone(float bone, float3 bindPos, inout float4x4 Bm, out float g)
            {
                g = 0.0;
                int b = (int)bone;
                if (b != 5 && b != 6) return;
                int k = b - 5;
                float s = saturate(dot(bindPos - _Tw[k].xyz, _Tw[2 + k].xyz) * _Tw[k].w);
                float th = -_Tw[2 + k].w * (1.0 - s * s * (3.0 - 2.0 * s));
                Bm = mul(TwistTurn(_Tw[4 + k].xyz, _Tw[6 + k].xyz, th), Bm);
                g = _Tw[2 + k].w * 6.0 * s * (1.0 - s) * _Tw[4 + k].w;
            }
            float3 TwistNormal(float bone, float w, float g, float3 p, float3 nrm)
            {
                if (g == 0.0) return nrm;
                int k = (int)bone - 5;
                float3 ax = _Tw[6 + k].xyz;
                return nrm - w * g * ax * dot(cross(ax, p - _Tw[4 + k].xyz), nrm);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS;
                float3 nrm = v.normalOS;
                float pin = 0.0;
                if (_TuckOn > 0.5)
                {   // pulled in (smoothly, from 6 mm short of the radius) and, where the gear covers it, held still. The
                    // map is in native axes: z negated here
                    float3 d = p - _TuckC.xyz;
                    float r = max(length(d), 1e-4);
                    float2 tc = float2(atan2(d.x, -d.z) * 0.1591549 + 0.5, asin(clamp(d.y / r, -1.0, 1.0)) * 0.3183099 + 0.5);
                    float lim = 0.05 + 0.5 * SAMPLE_TEXTURE2D_LOD(_TuckTex, sampler_TuckTex, tc, 0).r;
                    float x = r - (lim - 0.006);
                    if (x > 0.0) p = _TuckC.xyz + d * ((lim - 0.006 + 0.006 * (1.0 - exp(-x / 0.006))) / r);
                    pin = clamp((0.45 - lim) * 4.0, 0.0, 1.0);
                }
                o.dec0 = o.dec1 = float3(9, 9, 9);
                o.decN = float2(0, 0);
                if (_Decals > 0.5)
                {   // (before skinning: the print stays on the same stitches however the body bends)
                    o.dec0 = mul(_DecalM0, float4(p, 1.0)).xyz;
                    o.dec1 = mul(_DecalM1, float4(p, 1.0)).xyz;
                    o.decN = float2(mul((float3x3)_DecalM0, nrm).z, mul((float3x3)_DecalM1, nrm).z);
                }
                if (_Swish > 0.5 && v.uv1.x > 0.0)
                {   // TENNIS SKIRT / KILT (native 481a290): its weight and the room it keeps from the body in the second UV (its
                    // V flipped by MdlImporter: 1 - V); native's sum, worked in native axes (the meshes' z negated)
                    float3 swA = float3(p.x, p.y, -p.z), swN = float3(nrm.x, nrm.y, -nrm.z);
                    float swW = v.uv1.x, swRoom = 1.0 - v.uv1.y, swAng = _Sw.z * swW, swC = cos(swAng), swS = sin(swAng);
                    float2 swD = swA.xz - _SwC.xy;
                    float3 swQ = float3(_SwC.x + swC * swD.x + swS * swD.y + _Sw.x * swW, swA.y + _Sw.w * swW, _SwC.y - swS * swD.x + swC * swD.y + _Sw.y * swW);
                    float3 swM = swQ - swA;
                    float swL = length(swM), swBound = _SwC.z * swW;
                    if (swL > swBound) swQ = swA + swM * (swBound / swL);
                    float2 swE = swQ.xz - _SwC.xy;
                    float swR = length(swE), swTarget = SwishRadius(swQ.y, atan2(swE.x, -swE.y)) + swRoom;
                    if (swR < swTarget) swQ.xz = _SwC.xy + (swR > 1e-6 ? swE * (swTarget / swR) : float2(swTarget, 0.0));
                    p = float3(swQ.x, swQ.y, -swQ.z);
                    float3 swNq = float3(swC * swN.x + swS * swN.z, swN.y, -swS * swN.x + swC * swN.z);
                    nrm = float3(swNq.x, swNq.y, -swNq.z);
                }
                if (_Skinned > 0.5)
                {   // linear blend skinning over the avatar's 7 bones
                    float4x4 B0 = _Bones[(int)v.bone.x], B1 = _Bones[(int)v.bone.y], B2 = _Bones[(int)v.bone.z], B3 = _Bones[(int)v.bone.w];
                    if (_Bend > 0.5)
                    {   // (THE REACH, native 79e7b28: the torso bending at the waist: from the hips, the pelvis, to the chest, the torso)
                        float t = smoothstep(-0.50, -0.26, v.positionOS.y);
                        float4x4 Tm = _Pelvis * (1.0 - t) + _Bones[0] * t;
                        if ((int)v.bone.x == 0) B0 = Tm;
                        if ((int)v.bone.y == 0) B1 = Tm;
                        if ((int)v.bone.z == 0) B2 = Tm;
                        if ((int)v.bone.w == 0) B3 = Tm;
                    }
                    float g0 = 0, g1 = 0, g2 = 0, g3 = 0;
                    if (_Twist > 0.5)
                    {   // (FOREARM TWIST: the forearm influences turned back toward the elbow)
                        TwistBone(v.bone.x, v.positionOS, B0, g0); TwistBone(v.bone.y, v.positionOS, B1, g1);
                        TwistBone(v.bone.z, v.positionOS, B2, g2); TwistBone(v.bone.w, v.positionOS, B3, g3);
                    }
                    float4x4 S = B0 * v.weight.x + B1 * v.weight.y + B2 * v.weight.z + B3 * v.weight.w;
                    if (_TuckOn > 0.5)
                    {   // (hair rigs: bone 0 is the head)
                        float4x4 I = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                        S = S * (1.0 - pin) + I * pin;
                    }
                    p = mul(S, float4(p, 1.0)).xyz;
                    nrm = mul((float3x3)S, nrm);
                    if (_Twist > 0.5)
                    {   // (FOREARM TWIST: the normal follows the turn and its change along the arm)
                        nrm = TwistNormal(v.bone.x, v.weight.x, g0, p, nrm); nrm = TwistNormal(v.bone.y, v.weight.y, g1, p, nrm);
                        nrm = TwistNormal(v.bone.z, v.weight.z, g2, p, nrm); nrm = TwistNormal(v.bone.w, v.weight.w, g3, p, nrm);
                    }
                }
                if (_PA_FoulMove != 0.0 && v.court.x != 0.0)
                {   // FOUL LINE moved (native 0.3.20 drawCourtModel, Runtime/CourtStretch.cs): the line moved, each end
                    // stretched to start at it (its back wall where it is), the middle with them
                    float sg = sign(v.court.x), part = abs(v.court.x), L = 4.75 - _PA_FoulMove;
                    if (part < 1.5) p.x -= sg * _PA_FoulMove;
                    else if (part < 2.5) { float kEnd = (9.5 - L) / (9.5 - 4.75); p.x = kEnd * p.x + sg * 9.5 * (1.0 - kEnd); }
                    else p.x *= L / 4.75;
                }
                if (_Squash.w > 0.0)
                {   // (THE PUNCH LANDS, native f045e04: squashed along its new way, round again in 0.14 s: AvatarDraw / MatchView)
                    float sqAlong = 1.0 - _Squash.w, sqRound = 1.0 + 0.5 * _Squash.w;
                    float3 sqAx = _Squash.xyz;
                    p = p * sqRound + sqAx * ((sqAlong - sqRound) * dot(sqAx, p));
                    nrm = nrm / sqRound + sqAx * ((1.0 / sqAlong - 1.0 / sqRound) * dot(sqAx, nrm));
                }
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.uv = _UVSet > 0.5 ? v.uv1 : v.uv0;
                o.color = v.color;
                return o;
            }

            // Stand-in for reflections of the arena (native arenaEnv): dark below, the bright band of stadium light slats
            // around the horizon, cool ceiling glow above. r is converted back to native axes (z negated) first.
            float3 ArenaEnv(float3 rUnity)
            {
                float3 r = float3(rUnity.x, rUnity.y, -rUnity.z);
                float t = (r.y - 0.10) / 0.09;
                float band = exp(-(t * t));
                float cols = pow(0.5 + 0.5 * sin(atan2(r.z, r.x) * 26.0), 6.0);
                float3 c = lerp(float3(0.004, 0.006, 0.012), float3(0.04, 0.055, 0.08), smoothstep(-0.2, 1.0, r.y));
                float sides = smoothstep(0.35, 0.85, abs(r.z));   // the galleries run along the long sides (native z)
                return c + float3(0.42, 0.47, 0.55) * band * sides * (0.15 + 0.85 * cols);
            }

            // the material tint times the draw's tint; a team end (channels 8 / 9) in its side's colour, as bright as it
            // was built (Renderer::draw)
            float4 Tint()
            {
                float4 tint = _Tint * _TintMul;
                int ch = (int)round(_Channel);
                if (_UseChannels > 0.5 && ch >= 1 && ch < 8) tint.rgb *= _ChanCols[ch].rgb;
                if (_PA_SidesOn > 0.5 && (abs(_Channel - 8.0) < 0.5 || abs(_Channel - 9.0) < 0.5))
                {
                    float k0 = max(_Tint.r, max(_Tint.g, _Tint.b));
                    tint.rgb = k0 * (_Channel < 8.5 ? _PA_Side0.rgb : _PA_Side1.rgb) * _TintMul.rgb;
                }
                return tint;
            }

            // COLOR GRADING (TASKS.md E38, native kFS main): the post-processing review's filmic grade (Narkowicz's fit of
            // ACES at 1.35 exposure, then 12% more saturation) at the end of each surface's shading instead of a pass over
            // the frame. Glows (additive) get the saturation only: the curve is for a whole surface, not light added on top
            float3 Graded(float3 c, bool glow)
            {
                if (_PA_Grade < 0.5) return c;
                c = max(c, float3(0, 0, 0));
                if (!glow)
                {
                    float3 x = c * 1.35;
                    c = saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
                }
                float y = dot(c, float3(0.2126, 0.7152, 0.0722));
                return max(float3(y, y, y) + (c - float3(y, y, y)) * 1.12, float3(0, 0, 0));
            }

            // a print (_DecalTex) where this point lies in its box, on cloth facing out of it (native printed(); the
            // texture's v runs up in Unity, down from the image's top natively)
            float3 Printed(float3 alb, float3 d, float facing, inout float cover)
            {
                if (facing <= 0.0 || abs(d.x) > 0.5 || abs(d.y) > 0.5 || abs(d.z) > 0.5) return alb;
                float4 t = SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, float2(d.x + 0.5, 0.5 + d.y));
                cover = max(cover, t.a);
                return lerp(alb, t.rgb, t.a);
            }

            // COUNTRY KIT (MY OUTFIT; native 485beac printCover): how much of a print lies over this point. Under one a
            // suit's own glow gives way to it (its glowing lines and its pulses), as the jersey has none there
            float PrintCover(Varyings i)
            {
                float a = 0.0;
                float3 x = float3(0, 0, 0);
                if (_Decals > 0.5) x = Printed(x, i.dec0, i.decN.x, a);
                if (_Decals > 1.5) x = Printed(x, i.dec1, i.decN.y, a);
                return a;
            }

            float4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                PA_CutAway(i.positionWS, _Cuttable);   // (THE CUTAWAY: a PC preset camera's near walls)
                float3 n = normalize(i.normalWS);
                if (_FlipFace > 0.5) front = !front;
                if (!front) n = -n;
                float4 tint = Tint();
                float4 base = tint;
                if (_HasDiffuse > 0.5) base *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                float3 em = _Emissive.rgb + _EmissiveAdd.rgb;   // (native: the material's emissive + the draw's emissiveAdd)

            #if defined(_PA_LIGHTMAP)
                float3 r = reflect(-v, n);
                float fres = pow(1.0 - max(dot(n, v), 0.0), 5.0);
                float3 F = em + (1.0 - em) * fres * 0.6;
                return float4(Graded(base.rgb + ArenaEnv(r) * F * _Emissive.a, false), base.a);
            #elif defined(_PA_ADD)
                // (the native erode / dissolve is a per-draw effect for balls and bursts: not used by the arena)
                if (_Decals > 0.5 && PrintCover(i) > 0.5) discard;   // (a suit's pulses under a print)
                float4 tx = _HasDiffuse > 0.5
                    ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + frac(_UVScroll.xy * _Time.y) + _UVOff.xy) : float4(1, 1, 1, 1);
                return float4(Graded(tint.rgb * tx.rgb * tint.a + em, true), 1.0);
            #elif defined(_PA_UNLIT)
                // (the native vertex-colour fade is for ribbons, a per-draw effect: not used by the arena)
                return float4(base.rgb + em, base.a);
            #else
                float3 L = _PA_LightDir;
                bool lit0 = _LitOnly > 0.5;
                float3 alb = base.rgb, glowAdd = float3(0, 0, 0);
                if (lit0 && _UseChannels > 0.5 && _DesignMat > 0.5 && _HasMask > 0.5)
                {   // garment design (mask texture): R trim colour, G glowing lines, B white accents
                    float4 dm = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
                    alb = lerp(alb, _ChanCols[2].rgb, dm.r);
                    alb = lerp(alb, float3(0.80, 0.80, 0.80), dm.b);
                    glowAdd += _ChanCols[3].rgb * dm.g;
                }
                alb *= i.color.r;   // baked ambient occlusion (1 on models without it)
                if (lit0 && _UseChannels > 0.5 && _PatternOn > 0.5 && _PatternMat > 0.5)
                {   // avatar suit pattern: R = secondary colour regions, G = glowing lines
                    float4 pt = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, i.uv);
                    alb = lerp(alb, (_Tint * _TintMul).rgb * _ChanCols[2].rgb, pt.r);
                    glowAdd += _ChanCols[3].rgb * pt.g;
                }
                if (lit0 && _Decals > 0.5)
                {   // NATIONS: the flags printed on the garment (lit and knit like the rest of it), over its glow
                    float cover = 0.0;
                    alb = Printed(alb, i.dec0, i.decN.x, cover);
                    if (_Decals > 1.5) alb = Printed(alb, i.dec1, i.decN.y, cover);
                    glowAdd *= 1.0 - cover;
                }
                float hemi = 0.5 + 0.5 * n.y;
                float ndl = max(dot(n, -L), 0.0);
                float3 h = normalize(v - L);
                float nv = max(dot(n, v), 0.0);
                float matte = _Emissive.a < 0.0 ? -_Emissive.a : 0.0;   // negative gloss: matte (hair, cloth)
                float spec = pow(max(dot(n, h), 0.0), 32.0) * 0.25 * (1.0 - matte);
                float3 c = alb * (0.10 + 0.18 * hemi + 0.80 * ndl) + spec + em + glowAdd;
                bool cloth = false;
                if (_Rim.a > 0.0 && lit0)
                {   // character lighting (avatar parts, gauntlets): the arena's key light is nearly overhead, which leaves
                    // the front of a player flat: wrap it round the body and add a soft fill from the viewer
                    float wrap = clamp((dot(n, -L) + 0.5) / 1.5, 0.0, 1.0);
                    float fill = max(dot(n, v), 0.0);
                    float diff = 0.10 + 0.12 * hemi + 0.42 * wrap + 0.36 * fill;
                    cloth = _ClothMat > 0.5;
                    if (cloth)
                    {   // garment fabric: knit texture and sheen instead of a glossy highlight
                        float fab = SAMPLE_TEXTURE2D(_FabricTex, sampler_FabricTex, i.uv * 28.0).r;
                        alb *= 0.82 + 0.36 * fab;
                        float sheen = pow(1.0 - nv, 3.0);
                        c = alb * (diff + 0.45 * sheen) + 0.025 * sheen + em + glowAdd;
                    }
                    else c = alb * diff + spec + em + glowAdd;
                }
                if (_Emissive.a > 0.0 && !cloth)
                {   // gloss: the arena's lights reflected in visors / polished armour
                    float fres = 0.04 + 0.96 * pow(1.0 - nv, 5.0);
                    c += ArenaEnv(reflect(-v, n)) * (0.25 + fres) * (_Emissive.a * 1.5)
                         + pow(max(dot(n, h), 0.0), 96.0) * _Emissive.a * 0.8;
                }
                c += _Rim.rgb * (_Rim.a * pow(1.0 - nv, 2.5) * (1.0 - 0.75 * matte));   // team-colour rim light
                return float4(Graded(c, false), base.a);
            #endif
            }
            ENDHLSL
        }
    }
}
