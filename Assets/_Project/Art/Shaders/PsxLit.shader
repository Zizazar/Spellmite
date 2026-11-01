// PlayStation 1 style lit surface:
// - vertices snap to a low-resolution screen grid (jitter), grid comes from PsxCamera (_PsxSnapResolution)
// - affine (non perspective-correct) texture mapping, strength from PsxCamera (_PsxAffine)
// - Gouraud lighting: main light, additional lights and ambient are computed per vertex
// - optional point-filtered textures, per-vertex scene fog (Lighting window)
// Main light shadows are sampled per pixel so the level stays readable.
// Optional (characters): dissolves in a dither pattern where the camera comes close or where it stands between
// the camera and the player's body, shadows stay.
Shader "Spellmite/PSX Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap("Emission Map (x Emission, uses Base Map UVs)", 2D) = "white" {}
        [HDR] _RimColor("Rim Color (per vertex, black = off)", Color) = (0, 0, 0, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        [Toggle(_POINT_FILTER)] _PointFilter("Point Filter (no texture smoothing)", Float) = 1
        [Toggle(_VERTEX_COLOR)] _VertexColor("Vertex Color (multiply)", Float) = 0
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_RECEIVE_SHADOWS_OFF)] _ReceiveShadowsOff("Ignore Shadows", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Toggle(_CAMERA_FADE)] _CameraFade("Camera Fade: near it and in front of the player (characters)", Float) = 0
        _CameraFadeRange("Camera Fade: gone at X m, solid at Y m", Vector) = (1, 4, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap); // sampler_PointRepeat comes from the core GlobalSamplers
        TEXTURE2D(_EmissionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _RimColor;
            half _RimPower;
            half _Cutoff;
            float4 _CameraFadeRange;
        CBUFFER_END

        // Set by PsxCamera right before each camera renders; zero for other cameras.
        float2 _PsxSnapResolution;
        float _PsxAffine;
        // Set by BodyCamera: xyz = the player's body, w = radius of the tube from the camera to it (0 = off).
        // The player's own renderers carry this rendering layer bit and never fade in the tube
        float4 _PsxOccluderFade;
        #define PSX_PLAYER_RENDERING_LAYER (1u << 8)

        float4 PsxSnap(float4 positionCS)
        {
            if (_PsxSnapResolution.y > 0 && positionCS.w > 0)
            {
                float2 halfGrid = _PsxSnapResolution * 0.5;
                positionCS.xy = round(positionCS.xy / positionCS.w * halfGrid) / halfGrid * positionCS.w;
            }
            return positionCS;
        }

        // uv * w interpolated perspective-correctly and divided by interpolated w gives the screen-linear (affine) uv,
        // like noperspective but also works on WebGL (GLSL ES 3.0 has no noperspective).
        float3 PsxAffineUV(float2 uv, float4 positionCS)
        {
            return float3(uv * positionCS.w, positionCS.w);
        }

        float2 PsxResolveUV(float2 uv, float3 affineUV)
        {
            return lerp(uv, affineUV.xy / affineUV.z, _PsxAffine);
        }

        half4 PsxSampleBase(float2 uv)
        {
            #if defined(_POINT_FILTER)
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_PointRepeat, uv) * _BaseColor;
            #else
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            #endif
        }

        half3 PsxSampleEmission(float2 uv)
        {
            #if defined(_POINT_FILTER)
                return SAMPLE_TEXTURE2D(_EmissionMap, sampler_PointRepeat, uv).rgb * _EmissionColor.rgb;
            #else
                return SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, uv).rgb * _EmissionColor.rgb;
            #endif
        }

        void PsxAlphaClip(half alpha)
        {
            #if defined(_ALPHATEST_ON)
                clip(alpha - _Cutoff);
            #endif
        }

        // Screen-door fade by distance to the camera (Risk of Rain fades characters the camera comes close to),
        // and inside a tube around the line from the camera to the player's body, so nobody hides the player.
        // pixel = SV_POSITION in the fragment. The depth passes clip the same pixels, or depth priming would leave holes
        void PsxCameraFade(float3 positionWS, float2 pixel)
        {
            #if defined(_CAMERA_FADE)
                const float4x4 bayer = float4x4(0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
                float3 toPixel = positionWS - _WorldSpaceCameraPos;
                float visible = saturate((length(toPixel) - _CameraFadeRange.x) / max(_CameraFadeRange.y - _CameraFadeRange.x, 0.001));

                float radius = _PsxOccluderFade.w;
                if (radius > 0 && (GetMeshRenderingLayer() & PSX_PLAYER_RENDERING_LAYER) == 0)
                {
                    float3 toTarget = _PsxOccluderFade.xyz - _WorldSpaceCameraPos;
                    float targetDistance = length(toTarget);
                    float3 dir = toTarget / max(targetDistance, 0.001);
                    float along = dot(toPixel, dir);
                    // Gone inside half the radius, solid at the edge. Along the line: gone up to 1 m before the body's
                    // center, solid from 0.5 m before it (an enemy right behind the player still fades, one beside it doesn't)
                    float outside = saturate((length(toPixel - dir * along) - radius * 0.5) / (radius * 0.5));
                    float behind = saturate((along - targetDistance + 1) / 0.5);
                    visible = min(visible, max(outside, behind));
                }

                int2 cell = int2(fmod(floor(pixel), 4));
                clip(visible - (bayer[cell.y][cell.x] + 0.5) / 16);
            #endif
        }

        struct DepthAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct DepthVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 affineUV : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            float3 positionWS : TEXCOORD3;
        };

        DepthVaryings PsxDepthVertex(DepthAttributes input)
        {
            DepthVaryings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = PsxSnap(TransformWorldToHClip(output.positionWS));
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.affineUV = PsxAffineUV(output.uv, output.positionCS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _POINT_FILTER
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _CAMERA_FADE
            #pragma shader_feature_local_vertex _VERTEX_COLOR

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 affineUV : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half3 mainLight : TEXCOORD3;   // shadowed per pixel
                half3 otherLight : TEXCOORD4;  // ambient + additional lights
                half fogFactor : TEXCOORD5;
                half3 color : TEXCOORD6;
                half3 rim : TEXCOORD7;         // Gouraud fresnel glow, keeps the silhouette readable
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = PsxSnap(position.positionCS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.affineUV = PsxAffineUV(output.uv, output.positionCS);
                output.positionWS = position.positionWS;

                Light mainLight = GetMainLight();
                output.mainLight = mainLight.color * (mainLight.distanceAttenuation * saturate(dot(normalWS, mainLight.direction)));

                half3 otherLight = SampleSH(normalWS);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = position.positionWS;
                    inputData.normalizedScreenSpaceUV = position.positionNDC.xy / position.positionNDC.w;

                    #if USE_CLUSTER_LIGHT_LOOP
                        for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                        {
                            Light light = GetAdditionalLight(dirIndex, position.positionWS);
                            otherLight += light.color * (light.distanceAttenuation * saturate(dot(normalWS, light.direction)));
                        }
                    #endif

                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, position.positionWS);
                        otherLight += light.color * (light.distanceAttenuation * saturate(dot(normalWS, light.direction)));
                    LIGHT_LOOP_END
                #endif
                output.otherLight = otherLight;

                half facing = saturate(dot(normalWS, GetWorldSpaceNormalizeViewDir(position.positionWS)));
                output.rim = _RimColor.rgb * pow(1 - facing, _RimPower);

                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                #if defined(_VERTEX_COLOR)
                    output.color = input.color.rgb;
                #else
                    output.color = 1;
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = PsxResolveUV(input.uv, input.affineUV);
                half4 albedo = PsxSampleBase(uv);
                PsxAlphaClip(albedo.a);
                PsxCameraFade(input.positionWS, input.positionCS.xy);

                half shadow = 1;
                #if !defined(_RECEIVE_SHADOWS_OFF)
                    shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(input.positionWS));
                #endif

                half3 color = albedo.rgb * input.color * (input.mainLight * shadow + input.otherLight) + PsxSampleEmission(uv) + input.rim;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _POINT_FILTER
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // No snapping here: the shadow map is rendered from the light, not from the PSX camera.
            Varyings Vert(DepthAttributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                PsxAlphaClip(PsxSampleBase(input.uv).a);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PsxDepthVertex
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _POINT_FILTER
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _CAMERA_FADE

            half4 Frag(DepthVaryings input) : SV_Target
            {
                PsxAlphaClip(PsxSampleBase(PsxResolveUV(input.uv, input.affineUV)).a);
                PsxCameraFade(input.positionWS, input.positionCS.xy);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PsxDepthVertex
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _POINT_FILTER
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _CAMERA_FADE

            half4 Frag(DepthVaryings input) : SV_Target
            {
                PsxAlphaClip(PsxSampleBase(PsxResolveUV(input.uv, input.affineUV)).a);
                PsxCameraFade(input.positionWS, input.positionCS.xy);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
