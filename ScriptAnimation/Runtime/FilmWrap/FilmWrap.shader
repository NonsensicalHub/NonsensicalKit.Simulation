Shader "NonsensicalKit/ScriptAnimation/FilmWrap"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (0.816, 0.938, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 0.3
        _Smoothness("Smoothness", Range(0, 1)) = 0.6
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [HDR] _EmissionColor("Emission Color", Color) = (0.05, 0.05, 0.05, 1)
        _EmissionMap("Emission Map", 2D) = "white" {}
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1
        [HideInInspector] _WrapParams("Wrap Params", Vector) = (1, 8, 0, 0.04)
        [HideInInspector] _WrapHeight("Wrap Height", Vector) = (0, 1, 1, 1)
        [HideInInspector] _WrapCenter("Wrap Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _WrapAxis("Wrap Axis", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FilmWrapForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float4 _EmissionMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _Metallic;
                half _Smoothness;
                half _BumpScale;
                half _SpecularHighlights;
                half _EnvironmentReflections;
                float4 _WrapParams;
                float4 _WrapHeight;
                float4 _WrapCenter;
                float4 _WrapAxis;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                half4 tangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            float WrapMask(float3 positionOS)
            {
                float progress = saturate(_WrapParams.x);
                float turns = max(_WrapParams.y, 0.001);
                float startAngle = _WrapParams.z;
                float feather = max(_WrapParams.w, 0.0);

                float yMin = _WrapHeight.x;
                float yMax = _WrapHeight.y;
                float clockwise = _WrapHeight.z;
                float riseUp = _WrapHeight.w;

                float3 axis = _WrapAxis.xyz;
                float axisLenSq = max(dot(axis, axis), 1e-8);
                axis *= rsqrt(axisLenSq);

                float3 helper = abs(axis.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 tangent = normalize(cross(helper, axis));
                float3 bitangent = cross(axis, tangent);

                float3 rel = positionOS - _WrapCenter.xyz;
                float height = dot(rel, axis);
                float heightRange = max(abs(yMax - yMin), 1e-5);
                float height01 = saturate((height - min(yMin, yMax)) / heightRange);
                if (riseUp < 0.5)
                    height01 = 1.0 - height01;

                float3 radial = rel - axis * height;
                float angle01 = 0.0;
                float radialLenSq = dot(radial, radial);
                if (radialLenSq > 1e-12)
                {
                    float ang = atan2(dot(radial, bitangent), dot(radial, tangent));
                    ang -= startAngle;
                    if (clockwise > 0.5)
                        ang = -ang;
                    angle01 = frac(ang * (1.0 / (2.0 * PI)) + 1.0);
                }

                float layer = height01 * turns;
                float layerIndex = min(floor(layer), max(turns - 1e-4, 0.0));
                float vertexT = layerIndex + angle01;
                float shown = progress * turns;
                float delta = shown - vertexT;
                if (feather <= 1e-5)
                    return delta >= 0.0 ? 1.0 : 0.0;
                return saturate(delta / feather);
            }

            half3 SampleNormalWS(Varyings input, half3 normalWS, half3 tangentWS, half3 bitangentWS)
            {
                half4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                half3 normalTS;
                normalTS.xy = packedNormal.ag * 2.0h - 1.0h;
                normalTS.xy *= _BumpScale;
                normalTS.z = sqrt(1.0h - saturate(dot(normalTS.xy, normalTS.xy)));
                half3x3 tbn = half3x3(tangentWS, bitangentWS, normalWS);
                return normalize(mul(normalTS, tbn));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = posInputs.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float wrap = WrapMask(input.positionOS);
                clip(wrap - 0.001);

                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseMap.rgb * _BaseColor.rgb;
                half alpha = baseMap.a * _BaseColor.a * wrap;

                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = cross(input.normalWS, tangentWS) * input.tangentWS.w;
                half3 normalWS = SampleNormalWS(input, normalize(input.normalWS), tangentWS, bitangentWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
                #endif
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0.0h, 0.0h, 0.0h);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.emission = _EmissionColor.rgb *
                    SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb;
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = alpha;
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.a = alpha;
                color.rgb *= color.a;
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
