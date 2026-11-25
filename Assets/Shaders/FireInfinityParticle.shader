Shader "WanjieScroll/Effects/Infinity Fire"
{
    Properties
    {
        _DeepColor("Deep Fire Color", Color) = (0.22, 0.055, 0.018, 1)
        _FlameColor("Orange Flame Color", Color) = (1, 0.26, 0.025, 1)
        _CoreColor("Yellow Core Color", Color) = (1, 0.93, 0.62, 1)
        _CoreStrength("Core Strength", Range(0, 1)) = 0.65
        _Radiance("Fire Radiance", Range(1, 3)) = 1.5
        [Enum(Flame, 0, Ember, 1, ThinSmoke, 2, GroundHeat, 3)] _Layer("Fire Layer", Float) = 0
        _Opacity("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "InfinityFire"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma never_use_dxc
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Infinity/Particles/ParticlesTransform.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _FlameColor;
                half4 _CoreColor;
                float _Layer;
                half _CoreStrength;
                half _Radiance;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                uint vid : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float seed : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            float FireHash(float2 samplePoint)
            {
                float3 h = frac(float3(samplePoint.xyx) * float3(0.1031, 0.1030, 0.0973));
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }
            float FireNoise(float2 samplePoint)
            {
                float2 cell = floor(samplePoint);
                float2 t = frac(samplePoint);
                t = t * t * (3.0 - 2.0 * t);
                float a = FireHash(cell);
                float b = FireHash(cell + float2(1.0, 0.0));
                float c = FireHash(cell + float2(0.0, 1.0));
                float d = FireHash(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
            }
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                    input.color = GetParticleColor(input.vid);
                #endif
                float4x4 objectToWorld = GetParticleObjectToWorld(input.vid);
                VertexPositionInputs vertexInput = GetParticleVertexPositionInputs(input.positionOS.xyz, objectToWorld);
                output.positionCS = vertexInput.positionCS;
                output.color = input.color;
                output.uv = GetParticleUV(input.uv, input.vid);
                output.seed = FireHash(float2(GetRandomSeed(input.vid) * 127.0 + 0.37, input.color.r * 19.0));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 p = input.uv * 2.0 - 1.0;
                float2 seedOffset = float2(input.seed * 71.0, input.seed * 113.0);
                float alpha;
                half3 color;
                if (_Layer < 0.5)
                {
                    // A rising tapered tongue, with an asymmetric torn tip and
                    // moving folds. The yellow heart stays inside orange fire.
                    float y = saturate(input.uv.y);
                    float coarse = FireNoise(float2(p.x * 3.0, y * 4.5 - _Time.y * 2.8) + seedOffset);
                    float detail = FireNoise(float2(p.x * 8.0, y * 10.0 - _Time.y * 4.0) + seedOffset + 8.1);
                    float bend = sin(y * 7.3 + input.seed * 6.283185 - _Time.y * 5.0) * (0.03 + y * 0.16);
                    bend += (coarse - 0.5) * y * 0.2;
                    float width = max(0.055, 0.84 * pow(1.0 - y, 0.72));
                    width *= 0.86 + coarse * 0.24;
                    float side = abs(p.x - bend) / width;
                    float erosion = (detail - 0.5) * 0.2 * y;
                    float feather = max(fwidth(side) * 1.1, 0.065);
                    float body = 1.0 - smoothstep(0.68 - feather, 1.02 + feather, side + erosion);
                    float foot = smoothstep(0.0, 0.09, y);
                    float tip = 1.0 - smoothstep(0.82 + coarse * 0.08, 0.995, y);
                    float heart = (1.0 - smoothstep(0.16, 0.68, side)) *
                        (1.0 - smoothstep(0.38, 0.88, y)) * _CoreStrength;
                    color = lerp(_DeepColor.rgb, _FlameColor.rgb, 0.73 + coarse * 0.22);
                    color = lerp(color, _CoreColor.rgb, heart);
                    alpha = body * foot * tip * (0.9 + detail * 0.1);
                }
                else if (_Layer < 1.5)
                {
                    // A small hot fragment, tapered on both ends rather than a
                    // soft circular glow or a rectangular billboard.
                    float grain = FireNoise(p * float2(4.0, 5.0) + seedOffset);
                    float center = sin(p.y * 3.0 + input.seed * 6.0) * 0.11;
                    float taper = saturate(1.0 - abs(p.y));
                    float width = 0.23 * taper + 0.035;
                    float side = abs(p.x - center) / width;
                    float feather = max(fwidth(side), 0.10);
                    alpha = (1.0 - smoothstep(0.65 - feather, 1.0 + feather, side)) *
                        (1.0 - smoothstep(0.6, 0.98, abs(p.y))) * (0.7 + grain * 0.25);
                    color = lerp(_FlameColor.rgb, _CoreColor.rgb, 0.35 + grain * 0.4);
                }
                else if (_Layer < 2.5)
                {
                    // Broken, low-opacity wisps only soften the fire's wake.
                    float coarse = FireNoise(p * float2(2.7, 3.1) + seedOffset + float2(0.0, -_Time.y));
                    float detail = FireNoise(p * 8.0 + seedOffset + 4.9);
                    float center = (coarse - 0.5) * 0.4 + sin(p.y * 3.8 + input.seed * 6.0) * 0.13;
                    float side = abs(p.x - center) / (0.42 + coarse * 0.22);
                    alpha = (1.0 - smoothstep(0.25, 1.0, side)) *
                        (1.0 - smoothstep(0.6, 0.98, abs(p.y))) *
                        smoothstep(0.17, 0.75, coarse * 0.65 + detail * 0.35) * 0.14;
                    color = lerp(_DeepColor.rgb, _FlameColor.rgb, 0.08);
                }
                else
                {
                    // Managed four-vertex ground heat, not another native system.
                    float radius = dot(p, p);
                    alpha = (1.0 - smoothstep(0.08, 0.98, radius)) * 0.8;
                    color = lerp(_FlameColor.rgb, _CoreColor.rgb, (1.0 - saturate(radius)) * 0.25);
                }
                alpha *= input.color.a * _Opacity;
                // Keep the hot layers' radiance instead of clipping their RGB
                // to one before transparent blending. Smoke stays subdued.
                half radiance = (_Layer > 1.5 && _Layer < 2.5) ? 1.0h : _Radiance;
                return half4(max(color, 0.0h) * radiance, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
