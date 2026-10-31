// Void fog filling a spawn pocket (EnemyPocket). Goes on a unit cube that matches the pocket box.
// - the view ray is clipped to the box and to the scene depth, so whatever stands inside sinks into the fog
// - density grows toward the back (object -Z); the opening (+Z) is clear, so the box has no visible edge there
// - noise drifts out of the opening; its peaks glow faintly
// Needs the camera depth texture (Depth Texture on the URP asset). Drawn from back faces, so it also works from inside.
Shader "Spellmite/Void Fog"
{
    Properties
    {
        _Color("Void Color", Color) = (0.006, 0.004, 0.012, 1)
        _GlowColor("Glow Color", Color) = (0.25, 0.08, 0.4, 1)
        _Glow("Glow Amount", Range(0, 1)) = 0.6
        _Density("Density (per meter at the back)", Float) = 1.2
        _DepthCurve("Depth Curve (higher = clearer near the opening)", Float) = 1.5
        _NoiseScale("Noise Scale (per meter)", Float) = 0.7
        _NoiseSpeed("Noise Speed (meters per second)", Float) = 0.35
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VoidFog"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define STEPS 8

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _GlowColor;
                half _Glow;
                float _Density;
                float _DepthCurve;
                float _NoiseScale;
                float _NoiseSpeed;
                half _NoiseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1) * 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Value noise, 0..1
            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 cameraWS = GetCameraPositionWS();
                float3 rayWS = normalize(input.positionWS - cameraWS);

                // Ray against the unit box in object space. The direction stays unnormalized there, so t is in world meters.
                float3 originOS = TransformWorldToObject(cameraWS);
                float3 dirOS = TransformWorldToObjectDir(rayWS, false);
                dirOS = (abs(dirOS) < 1e-6) ? 1e-6 : dirOS;
                float3 t0 = (-0.5 - originOS) / dirOS;
                float3 t1 = (0.5 - originOS) / dirOS;
                float3 tNear = min(t0, t1);
                float3 tFar = max(t0, t1);
                float enter = max(max(max(tNear.x, tNear.y), tNear.z), 0.0);
                float exit = min(min(tFar.x, tFar.y), tFar.z);

                // Stop at whatever is in front of the back face
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                exit = min(exit, sceneDepth / dot(rayWS, GetViewForwardDir()));
                clip(exit - enter - 1e-4);

                float3 outWS = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
                float3 drift = (outWS + float3(0, 0.3, 0)) * (_Time.y * _NoiseSpeed);
                float stepLength = (exit - enter) / STEPS;
                // Per-pixel offset (interleaved gradient noise) turns step banding into dither
                float jitter = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));

                float opticalDepth = 0.0;
                float glow = 0.0;
                for (int s = 0; s < STEPS; s++)
                {
                    float t = enter + (s + jitter) * stepLength;
                    float3 p = cameraWS + rayWS * t;
                    float depth01 = saturate(0.5 - (originOS.z + dirOS.z * t));

                    float3 q = (p - drift) * _NoiseScale;
                    float n = Noise(q) * 0.65 + Noise(q * 2.3 + 7.1) * 0.35;

                    float density = _Density * pow(depth01, _DepthCurve) * lerp(1.0, n * 2.0, _NoiseStrength);
                    opticalDepth += density * stepLength;
                    glow += saturate(n * 2.5 - 1.4) * density * stepLength;
                }

                half alpha = 1.0 - exp(-opticalDepth);
                half3 color = lerp(_Color.rgb, _GlowColor.rgb, saturate(glow / max(opticalDepth, 1e-4)) * _Glow);
                color = MixFog(color, ComputeFogFactor(TransformWorldToHClip(cameraWS + rayWS * enter).z));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
