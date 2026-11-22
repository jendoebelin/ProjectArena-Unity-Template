// The native renderer's text and panels (render.cpp drawText / drawPanel): the unlit kind, alpha blended, two-sided,
// depth tested without depth writes: colour = tint.rgb * texture.rgb, alpha = tint.a * texture.a; panels (uv < 0) are
// the tint alone. Batched by Runtime/FxRenderer.cs in the order drawn (as the native renderer draws: a later board
// covers what was drawn before it); the tint is per vertex (TEXCOORD2). _SrcBlend, _DstBlend and _ZWrite: the solid
// boards (FxRenderer.PanelSolid, native drawPanelSolid: One Zero with depth writes, drawn with the opaque things).
Shader "ProjectArena/Text"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cuttable ("THE CUTAWAY (PC presets): 1 an EXPERIMENTAL room's", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        _ZWrite ("Depth writes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+310" "RenderType" = "Transparent" }
        Pass
        {
            Name "Text"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cuttable;   // THE CUTAWAY (TASKS.md E42, Shaders/Cutaway.hlsl): an EXPERIMENTAL room's walls
            CBUFFER_END
            #include "Cutaway.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD1;
                float3 wp : TEXCOORD2;   // (world position: THE CUTAWAY)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.wp = TransformObjectToWorld(v.positionOS);
                o.uv = v.uv;
                o.tint = v.tint;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                PA_CutAway(i.wp, _Cuttable);
                if (i.uv.y < -0.5) return i.tint;   // a panel: no texture
                return i.tint * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
            }
            ENDHLSL
        }
    }
}
