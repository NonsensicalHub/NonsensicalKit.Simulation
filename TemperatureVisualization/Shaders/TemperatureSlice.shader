Shader "TemperatureVisualization/Slice"
{
    Properties
    {
        _TemperatureTex ("Temperature 3D", 3D) = "" {}
        _TemperatureTexPrev ("Temperature 3D Prev", 3D) = "" {}
        _ColorRamp ("Color Ramp", 2D) = "white" {}
        _TempMin ("Temp Min", Float) = 10
        _TempMax ("Temp Max", Float) = 40
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _Blend ("Blend", Range(0, 1)) = 1
        _SliceAxis ("Slice Axis", Float) = 0
        _SlicePosition ("Slice Position", Range(0, 1)) = 0.5
        _VolumeCenter ("Volume Center", Vector) = (0, 0, 0, 0)
        _VolumeSize ("Volume Size", Vector) = (1, 1, 1, 0)
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
            Name "TemperatureSlice"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Off
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

            // 每切片通过 MaterialPropertyBlock 覆盖，须放在 UnityPerMaterial 外。
            float _SliceAxis;
            float _SlicePosition;
            float4x4 _WorldToVolume;
            float4 _VolumeCenter;
            float4 _VolumeSize;

            CBUFFER_START(UnityPerMaterial)
                float _TempMin;
                float _TempMax;
                float _Opacity;
                float _Blend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.worldPos = positionWS;
                output.uv = input.uv;
                return output;
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
                float3 volumeSize = max(_VolumeSize.xyz, float3(0.0001, 0.0001, 0.0001));
                float invTempRange = rcp(max(_TempMax - _TempMin, 0.0001));
                float3 localPos = mul(_WorldToVolume, float4(input.worldPos, 1.0)).xyz;
                float3 uvw = (localPos - _VolumeCenter.xyz) / volumeSize + 0.5;

                if (_SliceAxis < 0.5)
                    uvw.z = _SlicePosition;
                else if (_SliceAxis < 1.5)
                    uvw.y = _SlicePosition;
                else
                    uvw.x = _SlicePosition;

                uvw = saturate(uvw);
                float temperature = SampleTemperature(uvw);
                float normalized = saturate((temperature - _TempMin) * invTempRange);
                float4 color = SAMPLE_TEXTURE2D(_ColorRamp, sampler_ColorRamp, float2(normalized, 0.5));
                color.a *= _Opacity;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
