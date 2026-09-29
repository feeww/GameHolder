Shader "PureDots/InfiniteFloor"
{
    Properties
    {
        _MainTex ("Floor Texture", 2D) = "gray" {}
        _TileScale ("Tile World Scale", Float) = 2.0
        _FloorColor ("Floor Tint", Color) = (0.2, 0.22, 0.25, 1.0)
        _GridColor ("Grid Line Tint", Color) = (0.3, 0.33, 0.38, 1.0)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "Queue" = "Geometry-100" 
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _TileScale;
                float4 _FloorColor;
                float4 _GridColor;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXY : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.worldXY = positionWS.xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float scale = max(0.1, _TileScale);
                // World coordinates modulo tile scale prevents precision jitter
                float2 floorUV = frac(input.worldXY / scale);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, floorUV);

                // Procedural subtle grid line
                float2 grid = abs(floorUV - 0.5);
                float lineMask = step(0.48, max(grid.x, grid.y));

                half4 finalColor = lerp(_FloorColor * texColor, _GridColor, lineMask * 0.4);
                return finalColor;
            }
            ENDHLSL
        }
    }
}
