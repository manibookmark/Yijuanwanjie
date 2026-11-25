Shader "WanjieScroll/Effects/Map Aura Particle"
{
    Properties
    {
        [HDR] _DeepColor("Deep Color", Color) = (0.01, 0.005, 0.03, 1)
        [HDR] _CoreColor("Core Color", Color) = (0.12, 0.05, 0.22, 1)
        [HDR] _HighlightColor("Highlight Color", Color) = (0.42, 0.25, 0.62, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.9
        _Softness("Softness", Range(0.05, 0.9)) = 0.48
        _WispShape("Wisp Shape", Range(0, 1)) = 0
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
            // 刻意不声明 UniversalForward：地图使用 Renderer2D，未标记的
            // Pass 会作为 SRPDefaultUnlit 被 Renderer2D 正常绘制。
            Name "MapAura"
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
                half4 _CoreColor;
                half4 _HighlightColor;
                half _Opacity;
                half _Softness;
                half _WispShape;
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half2 centered = input.uv * 2.0h - 1.0h;
                half roundRadius = length(centered);

                // 从圆形柔光连续过渡到微弯、上窄下宽的妖气墨滴。
                // 粒子自身的随机旋转负责打散重复轮廓，不再依赖贴图素材。
                half vertical01 = saturate(centered.y * 0.5h + 0.5h);
                half curl = sin((centered.y + 1.0h) * 3.1415926h) *
                            0.14h * (1.0h - abs(centered.y));
                half taperedX = lerp(1.12h, 2.45h, vertical01);
                half wispRadius = length(half2(
                    (centered.x + curl) * taperedX,
                    centered.y * 0.94h));
                half radius = lerp(
                    roundRadius, wispRadius, saturate(_WispShape));
                half edgeStart = saturate(1.0h - _Softness);
                half alpha = 1.0h - smoothstep(edgeStart, 1.0h, radius);

                // 中心略亮、边缘压暗；只取粒子顶点色的透明度，避免
                // Shuriken 白色色带把怪物重新染成粉色。
                half centerWeight = saturate(1.0h - radius);
                half3 color = lerp(
                    _DeepColor.rgb,
                    _CoreColor.rgb,
                    smoothstep(0.0h, 0.72h, centerWeight));
                color = lerp(
                    color,
                    _HighlightColor.rgb,
                    smoothstep(0.72h, 1.0h, centerWeight));
                alpha *= input.color.a * _Opacity;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
