Shader "WanjieScroll/Effects/Reward Reveal Infinity Particle"
{
    Properties
    {
        [HDR] _PrimaryColor("Primary Color", Color) = (0.42, 0.66, 0.62, 1)
        [HDR] _SecondaryColor("Secondary Color", Color) = (0.86, 0.96, 0.84, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.86
        _Softness("Softness", Range(0.01, 0.35)) = 0.10
        _ShapeMode("Shape Mode", Range(0, 2)) = 0
        _FlowBend("Flow Bend", Range(-0.4, 0.4)) = 0
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
            Name "RewardRevealInfinity"
            Blend One OneMinusSrcAlpha
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
                half4 _PrimaryColor;
                half4 _SecondaryColor;
                half _Opacity;
                half _Softness;
                half _ShapeMode;
                half _FlowBend;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                    GetParticleVertexPositionInputs(
                        input.positionOS.xyz, objectToWorld);
                output.positionCS = vertexInput.positionCS;
                output.color = input.color;
                output.uv = GetParticleUV(input.uv, input.vid);
                return output;
            }

            half SpiritAuraMask(half2 p)
            {
                half radius = length(half2(p.x * 1.04h, p.y * 0.96h));
                half edge = 1.0h - smoothstep(
                    saturate(1.0h - _Softness * 2.2h),
                    1.0h,
                    radius);
                half inner = 1.0h - smoothstep(0.08h, 0.78h, radius);
                return edge * lerp(0.72h, 1.0h, inner);
            }

            half SpiritWispMask(half2 p)
            {
                half vertical01 = saturate(p.y * 0.5h + 0.5h);
                half envelope = saturate(1.0h - abs(p.y));
                half bend = sin(vertical01 * 3.14159265h) *
                            _FlowBend * envelope;
                half taperX = lerp(1.18h, 2.55h, vertical01);
                half radius = length(half2(
                    (p.x + bend) * taperX,
                    p.y * 0.92h));
                half edge = 1.0h - smoothstep(
                    saturate(1.0h - _Softness * 2.0h),
                    1.0h,
                    radius);
                half inner = 1.0h - smoothstep(0.12h, 0.74h, radius);
                return edge * lerp(0.76h, 1.0h, inner);
            }

            half RisingWispMask(half2 p)
            {
                half vertical01 = saturate(p.y * 0.5h + 0.5h);
                half bend = sin(vertical01 * 3.14159265h) *
                            _FlowBend * 0.72h;
                half radius = length(half2(
                    (p.x + bend) * lerp(1.65h, 3.0h, vertical01),
                    p.y * 0.88h));
                return 1.0h - smoothstep(
                    saturate(1.0h - _Softness * 1.8h),
                    1.0h,
                    radius);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half2 p = input.uv * 2.0h - 1.0h;
                half mask;
                if (_ShapeMode < 0.5h)
                    mask = SpiritAuraMask(p);
                else if (_ShapeMode < 1.5h)
                    mask = SpiritWispMask(p);
                else
                    mask = RisingWispMask(p);

                clip(mask - 0.002h);
                half center = saturate(1.0h - length(p));
                half3 color = lerp(
                    _PrimaryColor.rgb,
                    _SecondaryColor.rgb,
                    center * center);
                color *= input.color.rgb;
                half alpha = mask * input.color.a * _Opacity;
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
