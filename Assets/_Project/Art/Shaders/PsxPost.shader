// PsxRendererFeature: point-samples the camera image into a low-resolution target, then reduces it to
// _PsxColorBits per channel with the PS1 GPU 4x4 dither matrix (applied in gamma space, like the hardware).
Shader "Hidden/Spellmite/PSX Post"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "PsxColor"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PsxColorBits;
            float _PsxDither;

            // PS1 GPU dither table, in 8-bit color units.
            static const float PsxDitherTable[16] =
            {
                -4,  0, -3,  1,
                 2, -2,  3, -1,
                -3,  1, -4,  0,
                 3, -1,  2, -2
            };

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).rgb;

                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = LinearToSRGB(saturate(color));
                #endif

                // The table is in 8-bit units for a 5-bit step (8); scale it to the current step.
                float levels = exp2(_PsxColorBits) - 1;
                uint2 pixel = uint2(input.positionCS.xy) & 3;
                float offset = PsxDitherTable[pixel.y * 4 + pixel.x] / 8.0 / levels * _PsxDither;

                color = floor(saturate(color + offset) * levels + 0.5) / levels;

                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = SRGBToLinear(color);
                #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
