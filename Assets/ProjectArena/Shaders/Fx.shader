// The native renderer's immediate effects (render.cpp drawSprite / drawSpriteFlat / drawSphere / drawRibbon), batched
// by Runtime/FxRenderer.cs into one mesh per frame. All additive light (blend One One, depth tested, no depth write):
//   sprites and spheres (native additive kind): tint.rgb * texture.rgb * tint.a (spheres: a white texture)
//   ribbons (native unlit kind with the vertex-colour fade): tint.rgb * f, f = vertex alpha (tail -> head, squared) *
//   sin(pi * u) across the width (feathered edges)
// The tint is per vertex (TEXCOORD2, float4: effects go brighter than 1).
Shader "ProjectArena/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Ribbon ("Ribbon (vertex-colour fade)", Float) = 0
        _Cuttable ("THE CUTAWAY (PC presets): 1 an EXPERIMENTAL room's", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+300" "RenderType" = "Transparent" }
        Pass
        {
            Name "Fx"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One, Zero One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Ribbon;
                float _Cuttable;   // THE CUTAWAY (TASKS.md E42, Shaders/Cutaway.hlsl): an EXPERIMENTAL room's walls
            CBUFFER_END
            #include "Cutaway.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD1;
                float4 color : TEXCOORD2;
                float3 wp : TEXCOORD3;   // (world position: THE CUTAWAY)
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
                o.color = v.color;
                return o;
            }

            float _PA_Grade;   // COLOR GRADING (TASKS.md E38): the glows get its saturation only (native kFS main, uShader 2)
            float3 Saturated(float3 c)
            {
                if (_PA_Grade < 0.5) return c;
                c = max(c, float3(0, 0, 0));
                float y = dot(c, float3(0.2126, 0.7152, 0.0722));
                return max(float3(y, y, y) + (c - float3(y, y, y)) * 1.12, float3(0, 0, 0));
            }

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                PA_CutAway(i.wp, _Cuttable);
                if (_Ribbon > 0.5)
                {   // ribbons: feathered edges across the width, fading toward the tail
                    float f = i.color.a * sin(3.14159 * saturate(i.uv.x));
                    return float4(Saturated(i.tint.rgb * f), i.tint.a * f);
                }
                float4 tx = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return float4(Saturated(i.tint.rgb * tx.rgb * i.tint.a), 1.0);
            }
            ENDHLSL
        }
    }
}
