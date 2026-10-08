Shader "PureDots/SpriteDOTS"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _BaseColor ("Base Color", Vector) = (1, 1, 1, 1)
        _SpriteUV ("Sprite UV (xy: scale, zw: offset)", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "TransparentCutout" 
            "Queue" = "AlphaTest" 
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SpriteUV;
                float4 _BaseColor;
                float _Cutoff;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float4, _SpriteUV)
                    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
                #define _SpriteUV UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpriteUV)
                #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
            #endif

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 spriteUV = _SpriteUV;
                float4 baseColor = _BaseColor;

                // Transform UV with atlas scale and offset
                output.uv = input.uv * spriteUV.xy + spriteUV.zw;
                output.color = baseColor;

                // Bottom-center pivot alignment:
                // Vertices are assumed to be centered or offset such that bottom-center is (0,0)
                float3 positionOS = input.positionOS.xyz;
                output.positionCS = TransformObjectToHClip(positionOS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 col = texColor * input.color;

                // Alpha cutout discarding for GPU Z-buffer write without sorting artifacts
                clip(col.a - _Cutoff);

                return col;
            }
            ENDHLSL
        }
    }
    CustomEditor "GameHolder.PureDots.Editor.PureDotsShaderGUI"
}
