Shader "TemperatureVisualization/VolumeRaymarch"
{
    Properties
    {
        _TemperatureTex ("Temperature 3D", 3D) = "" {}
        _TemperatureTexPrev ("Temperature 3D Prev", 3D) = "" {}
        _ColorRamp ("Color Ramp", 2D) = "white" {}
        _TempMin ("Temp Min", Float) = 10
        _TempMax ("Temp Max", Float) = 40
        _Opacity ("Opacity", Range(0, 1)) = 0.65
        _Blend ("Blend", Range(0, 1)) = 1
        _StepCount ("Step Count", Float) = 96
        _DensityScale ("Density Scale", Float) = 1.5
        _EdgeSoftness ("Edge Softness", Range(0, 1)) = 0.35
        _NoiseScale ("Noise Scale", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "TemperatureVolumeRaymarch"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_TemperatureTex);
            SAMPLER(sampler_TemperatureTex);
            TEXTURE3D(_TemperatureTexPrev);
            SAMPLER(sampler_TemperatureTexPrev);
            TEXTURE2D(_ColorRamp);
            SAMPLER(sampler_ColorRamp);

            CBUFFER_START(UnityPerMaterial)
                float _TempMin;
                float _TempMax;
                float _Opacity;
                float _Blend;
                float _StepCount;
                float _DensityScale;
                float _EdgeSoftness;
                float _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash(i + float3(0, 0, 0));
                float n100 = Hash(i + float3(1, 0, 0));
                float n010 = Hash(i + float3(0, 1, 0));
                float n110 = Hash(i + float3(1, 1, 0));
                float n001 = Hash(i + float3(0, 0, 1));
                float n101 = Hash(i + float3(1, 0, 1));
                float n011 = Hash(i + float3(0, 1, 1));
                float n111 = Hash(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            bool IntersectAABB(float3 origin, float3 dir, float3 boundsMin, float3 boundsMax, out float tEnter, out float tExit)
            {
                float3 safeDir = dir;
                safeDir.x = abs(safeDir.x) < 1e-6 ? 1e-6 : safeDir.x;
                safeDir.y = abs(safeDir.y) < 1e-6 ? 1e-6 : safeDir.y;
                safeDir.z = abs(safeDir.z) < 1e-6 ? 1e-6 : safeDir.z;

                float3 t0 = (boundsMin - origin) / safeDir;
                float3 t1 = (boundsMax - origin) / safeDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                tEnter = max(max(tmin.x, tmin.y), tmin.z);
                tExit = min(min(tmax.x, tmax.y), tmax.z);
                return tExit >= max(tEnter, 0.0);
            }

            float SampleTemperature(float3 uvw)
            {
                float current = SAMPLE_TEXTURE3D(_TemperatureTex, sampler_TemperatureTex, uvw).r;
                if (_Blend >= 0.999)
                    return current;
                float previous = SAMPLE_TEXTURE3D(_TemperatureTexPrev, sampler_TemperatureTexPrev, uvw).r;
                return lerp(previous, current, _Blend);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // 在体积物体局部空间求交并采样，与插值网格 / 父物体变换一致
                float3 rayOriginOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 rayDirOS = input.positionOS - rayOriginOS;

                float tEnter;
                float tExit;
                if (!IntersectAABB(rayOriginOS, rayDirOS, float3(-0.5, -0.5, -0.5), float3(0.5, 0.5, 0.5), tEnter, tExit))
                {
                    discard;
                }

                tEnter = max(tEnter, 0.0);
                int steps = clamp((int)_StepCount, 8, 128);
                float stepSize = (tExit - tEnter) / steps;
                float invTempRange = rcp(max(_TempMax - _TempMin, 0.0001));
                float edgeSoft = _EdgeSoftness;
                float edgeThresh = edgeSoft * 0.25 + 0.001;
                float noiseAmp = edgeSoft * 0.35;
                bool useNoise = edgeSoft > 0.001;
                float densityScale = _DensityScale;

                half4 accum = half4(0, 0, 0, 0);
                float t = tEnter;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    if (accum.a >= 0.98h) break;

                    float3 posOS = rayOriginOS + rayDirOS * t;
                    float3 uvw = saturate(posOS + 0.5);
                    float temperature = SampleTemperature(uvw);
                    float normalized = saturate((temperature - _TempMin) * invTempRange);

                    float3 edgeDist = min(uvw, 1.0 - uvw);
                    float edgeFactor = min(min(edgeDist.x, edgeDist.y), edgeDist.z);
                    float edge = smoothstep(0.0, edgeThresh, edgeFactor);

                    float density;
                    if (useNoise)
                    {
                        float3 posWS = TransformObjectToWorld(posOS);
                        float noise = Noise3D(posWS * _NoiseScale) * noiseAmp;
                        density = saturate(normalized * densityScale + noise) * edge;
                    }
                    else
                    {
                        density = saturate(normalized * densityScale) * edge;
                    }

                    half4 sampleColor = SAMPLE_TEXTURE2D(_ColorRamp, sampler_ColorRamp, float2(normalized, 0.5));
                    half sampleAlpha = sampleColor.a * (half)(density * _Opacity);
                    half oneMinusA = 1.0h - accum.a;
                    accum.rgb += oneMinusA * sampleAlpha * sampleColor.rgb;
                    accum.a += oneMinusA * sampleAlpha;

                    t += stepSize;
                }

                return accum;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
