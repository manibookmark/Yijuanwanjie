Shader "WanjieScroll/Effects/Boss Action Particle"
{
    Properties
    {
        [HDR] _DeepColor("Deep Color", Color) = (0.08, 0.11, 0.15, 1)
        [HDR] _CoreColor("Core Color", Color) = (0.72, 0.82, 0.9, 1)
        [HDR] _HighlightColor("Highlight Color", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.92
        _Softness("Softness", Range(0.05, 0.9)) = 0.2
        _WispShape("Shape: Disc / Shard / Streak", Range(0, 1)) = 0.68
        _SmokeBlend("Smoke Silhouette", Range(0, 1)) = 0
        _SmokeStretch("Smoke Stretch", Range(1, 4)) = 1
        _SmokeCurl("Smoke Curl", Range(0, 1)) = 0
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
            Name "BossAction"
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
                half _SmokeBlend;
                half _SmokeStretch;
                half _SmokeCurl;
            CBUFFER_END

            half SmoothMinimum(half a, half b, half radius)
            {
                half h = saturate(
                    0.5h + 0.5h * (b - a) / max(0.001h, radius));
                return lerp(b, a, h) - radius * h * (1.0h - h);
            }

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

                half2 localPoint = input.uv * 2.0h - 1.0h;
                half discRadius = length(localPoint);

                // 中段为解析式玻璃菱片；不使用图片，随机旋转后不会出现方块。
                half2 shardPoint = localPoint;
                shardPoint.x += shardPoint.y * 0.18h;
                half shardRadius =
                    abs(shardPoint.x) * 0.82h +
                    abs(shardPoint.y) * 0.92h;

                // 高段收成横向短尾，粒子旋转由速度方向决定，供横扫气压使用。
                half2 streakPoint = localPoint;
                streakPoint.x *= 0.92h;
                streakPoint.y *= 3.65h;
                half streakRadius = length(streakPoint);

                half shardBlend = smoothstep(0.34h, 0.68h, _WispShape);
                half streakBlend = smoothstep(0.78h, 1.0h, _WispShape);
                half radius = lerp(discRadius, shardRadius, shardBlend);
                radius = lerp(radius, streakRadius, streakBlend);

                // 登场专用的解析式烟缕：三个不等大的柔软气团沿 S 形相接。
                // 粒子本体仍是 Infinity，不采样图片；随机旋转和三层轮廓
                // 会把同一流场拆成疏密各异的妖气，而不是圆点阵列。
                half smokeStretch = max(1.0h, _SmokeStretch);
                half curl = _SmokeCurl;
                // startColor.r 只在烟雾模式下作为三层标记：0 是侧向雾体，
                // 0.5 是承担主轮廓的宽气脉，1 是只补速度的细亮丝。
                half flowVariant = step(0.75h, input.color.r);
                half mainVariant =
                    step(0.25h, input.color.r) * (1.0h - flowVariant);
                half bodyStretch = lerp(1.0h, smokeStretch, 0.42h);
                half currentStretch = lerp(
                    bodyStretch,
                    lerp(1.0h, smokeStretch, 0.72h),
                    mainVariant);
                half2 smokeHead = half2(
                    (localPoint.x - 0.38h) / 0.64h,
                    (localPoint.y - curl * 0.055h) *
                    currentStretch / 0.62h);
                half2 smokeMiddle = half2(
                    (localPoint.x + 0.10h) / 0.58h,
                    (localPoint.y + curl * 0.095h) *
                    currentStretch / 0.48h);
                half2 smokeTail = half2(
                    (localPoint.x + 0.62h) / 0.46h,
                    (localPoint.y - curl * 0.14h) *
                    currentStretch / 0.30h);
                half smokeRadius = SmoothMinimum(
                    length(smokeHead),
                    length(smokeMiddle),
                    0.32h);
                smokeRadius = SmoothMinimum(
                    smokeRadius,
                    length(smokeTail),
                    0.24h);

                half filamentCurve = sin(
                    localPoint.x * 2.7h + curl * 1.3h) *
                    curl * 0.14h;
                half2 filamentPoint = half2(
                    localPoint.x * 0.92h,
                    (localPoint.y - filamentCurve) *
                    smokeStretch / 0.22h);
                half filamentRadius = length(filamentPoint);
                half layeredSmokeRadius = lerp(
                    smokeRadius,
                    filamentRadius,
                    flowVariant);
                radius = lerp(radius, layeredSmokeRadius, _SmokeBlend);

                half edgeStart = saturate(1.0h - _Softness);
                half alpha = 1.0h - smoothstep(edgeStart, 1.0h, radius);
                half bodyBreakup = 0.78h + 0.22h *
                    sin(localPoint.x * 8.3h + localPoint.y * 4.7h) *
                    sin(localPoint.x * 3.1h - localPoint.y * 10.9h);
                half filamentBreakup = 0.90h + 0.10h *
                    sin(localPoint.x * 13.7h - localPoint.y * 4.1h);
                half mainBreakup = 0.86h + 0.14h *
                    sin(localPoint.x * 6.1h - localPoint.y * 8.7h);
                half nonFlowBreakup = lerp(
                    bodyBreakup,
                    mainBreakup,
                    mainVariant);
                half smokeBreakup = lerp(
                    nonFlowBreakup,
                    filamentBreakup,
                    flowVariant);
                alpha *= lerp(
                    1.0h,
                    saturate(smokeBreakup),
                    _SmokeBlend * curl);
                half nonFlowAlpha = lerp(1.12h, 1.28h, mainVariant);
                half smokeAlpha = lerp(
                    nonFlowAlpha,
                    1.34h,
                    flowVariant);
                alpha = saturate(alpha * lerp(
                    1.0h,
                    smokeAlpha,
                    _SmokeBlend));
                half centerWeight = saturate(1.0h - radius);
                // 色值仍与地图怪物灵气完全一致，只让烟体更早进入紫色核心、
                // 细流丝更早进入亮紫，避免大片烟雾最终只剩近黑色。
                half bodyPaletteWeight = lerp(
                    centerWeight,
                    sqrt(centerWeight),
                    0.55h);
                half flowPaletteWeight = sqrt(sqrt(centerWeight));
                half mainPaletteWeight = lerp(
                    bodyPaletteWeight,
                    flowPaletteWeight,
                    0.52h);
                half nonFlowPaletteWeight = lerp(
                    bodyPaletteWeight,
                    mainPaletteWeight,
                    mainVariant);
                half smokePaletteWeight = lerp(
                    nonFlowPaletteWeight,
                    flowPaletteWeight,
                    flowVariant);
                half paletteWeight = lerp(
                    centerWeight,
                    smokePaletteWeight,
                    _SmokeBlend);
                half paletteKnee = lerp(0.68h, 0.72h, _SmokeBlend);
                half3 color = lerp(
                    _DeepColor.rgb,
                    _CoreColor.rgb,
                    smoothstep(0.0h, paletteKnee, paletteWeight));
                color = lerp(
                    color,
                    _HighlightColor.rgb,
                    smoothstep(paletteKnee, 1.0h, paletteWeight));
                alpha *= input.color.a * _Opacity;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
