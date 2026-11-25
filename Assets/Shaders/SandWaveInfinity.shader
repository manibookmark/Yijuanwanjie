Shader "WanjieScroll/Effects/Infinity Sand Wave"
{
    Properties
    {
        _DeepColor("Deep Sand Color", Color) = (0.27, 0.17, 0.085, 1)
        _SandColor("Sand Color", Color) = (0.68, 0.49, 0.26, 1)
        _LightColor("Light Sand Color", Color) = (0.84, 0.68, 0.43, 1)
        [Enum(ThickCurl, 0, FineSand, 1, ThinDust, 2)] _Layer("Sand Layer", Float) = 0
        _Opacity("Opacity", Range(0, 1)) = 0.9
        _CastFade("Cast Visibility (1 Visible)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SandWave"
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
                half4 _SandColor;
                half4 _LightColor;
                float _Layer;
                half _Opacity;
                half _CastFade;
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

            float SandHash(float2 samplePoint)
            {
                float3 h = frac(float3(samplePoint.xyx) * float3(0.1031, 0.1030, 0.0973));
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }

            float SandNoise(float2 samplePoint)
            {
                float2 cell = floor(samplePoint);
                float2 t = frac(samplePoint);
                t = t * t * (3.0 - 2.0 * t);
                float a = SandHash(cell);
                float b = SandHash(cell + float2(1.0, 0.0));
                float c = SandHash(cell + float2(0.0, 1.0));
                float d = SandHash(cell + float2(1.0, 1.0));
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
                VertexPositionInputs vertexInput =
                    GetParticleVertexPositionInputs(input.positionOS.xyz, objectToWorld);
                output.positionCS = vertexInput.positionCS;
                output.color = input.color;
                output.uv = GetParticleUV(input.uv, input.vid);
                // Native per-particle seed stays constant while the emitter moves.
                output.seed = SandHash(float2(
                    GetRandomSeed(input.vid) * 127.0 + input.color.r * 19.0,
                    input.color.g * 31.0 + input.color.b * 43.0 + 0.37));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 p = input.uv * 2.0 - 1.0;
                float2 seedOffset = float2(input.seed * 71.0, input.seed * 113.0);
                half3 sandColor;
                float alpha;

                if (_Layer < 0.5)
                {
                    // A tapered, curling band carries dense sand instead of a round puff.
                    float coarse = SandNoise(p * float2(2.7, 3.5) + seedOffset);
                    float density = SandNoise(p * float2(11.0, 13.0) + seedOffset + 8.3);
                    float grains = SandNoise(p * float2(43.0, 49.0) + seedOffset + 23.7);
                    float curl = sin(p.x * 3.9 + input.seed * 6.283185);
                    float center = 0.1 + 0.19 * curl + 0.14 * (coarse - 0.5);
                    float taper = saturate(1.0 - p.x * p.x);
                    float height = (0.23 + 0.12 * (p.x + 1.0)) * (0.4 + 0.6 * taper);
                    float band = (p.y - center) / max(height, 0.06);
                    float erosion = (coarse - 0.5) * 0.3 + (density - 0.5) * 0.13;
                    float edge = abs(band) + erosion;
                    float feather = max(fwidth(edge) * 1.2, 0.065);
                    float body = 1.0 - smoothstep(0.82 - feather, 0.98 + feather, edge);
                    float ends = 1.0 - smoothstep(0.75, 0.98, abs(p.x));
                    // Bright upper ridges and the dark underside make the curl feel heavy.
                    float ridge = saturate(0.5 + band * 0.42 + (density - 0.5) * 0.3);
                    sandColor = lerp(_DeepColor.rgb, _SandColor.rgb, 0.28 + ridge * 0.62);
                    sandColor = lerp(sandColor, _LightColor.rgb,
                        ridge * ridge * (0.08 + 0.24 * grains));
                    sandColor *= 0.83 + 0.2 * grains;
                    alpha = body * ends * (0.78 + 0.2 * density);
                }
                else if (_Layer < 1.5)
                {
                    // Each quad is an irregular warm grain, never a square or a light point.
                    float s;
                    float c;
                    sincos(input.seed * 6.283185, s, c);
                    float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y);
                    float contour = SandNoise(q * 3.8 + seedOffset);
                    float grain = SandNoise(q * 15.0 + seedOffset + 4.9);
                    q.x += q.y * (input.seed - 0.5) * 0.45;
                    q.y *= 1.25 + input.seed * 0.65;
                    float radius = length(q);
                    float boundary = 0.71 + (contour - 0.5) * 0.3;
                    float feather = max(fwidth(radius) * 1.15, 0.07);
                    alpha = 1.0 - smoothstep(boundary - feather, boundary + feather, radius);
                    float shade = saturate(input.seed * 0.65 + grain * 0.35);
                    sandColor = lerp(_DeepColor.rgb, _SandColor.rgb, 0.38 + shade * 0.58);
                    sandColor = lerp(sandColor, _LightColor.rgb, shade * 0.12);
                    alpha *= 0.75 + grain * 0.2;
                }
                else
                {
                    // Sparse, broken dust softens the wake without hiding the dense sand.
                    float coarse = SandNoise(p * float2(2.3, 3.2) + seedOffset);
                    float detail = SandNoise(p * float2(8.0, 10.0) + seedOffset + 15.1);
                    float center = (coarse - 0.5) * 0.4;
                    float side = abs(p.y - center) / (0.48 + coarse * 0.25);
                    float body = 1.0 - smoothstep(0.2, 1.0, side);
                    float ends = 1.0 - smoothstep(0.52, 0.97, abs(p.x));
                    float gaps = smoothstep(0.18, 0.7, coarse * 0.7 + detail * 0.3);
                    alpha = body * ends * gaps * 0.19;
                    sandColor = lerp(_DeepColor.rgb, _SandColor.rgb, 0.5 + detail * 0.2);
                }

                // Straight alpha: particle lifetime alpha and the owner tail fade both apply.
                alpha *= input.color.a * _Opacity * _CastFade;
                return half4(saturate(sandColor), saturate(alpha));
            }
            ENDHLSL
        }
    }
}
