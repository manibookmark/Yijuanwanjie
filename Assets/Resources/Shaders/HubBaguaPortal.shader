Shader "WanjieScroll/UI/HubBaguaPortal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _JadeColor ("Dormant Black Inlay", Color) = (0.015,0.022,0.024,0.98)
        _EnergyColor ("Jade Energy", Color) = (0.16,0.92,0.86,1)
        _Intensity ("State Intensity", Range(0,1.5)) = 0.48
        _Opening ("Opening Progress", Range(0,1)) = 0
        _HoverAngle ("Hover Angle", Range(-3.14159,3.14159)) = 0
        _IdleFlow ("Idle Energy Flow", Range(0,1)) = 0.18
        _IdleFlowSpeed ("Idle Energy Flow Speed", Range(0.1,2)) = 0.66
        _SpinAngle ("Opening Spin Angle", Float) = 0
        _SpinSink ("Opening Sink", Range(0,0.2)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "ProceduralBaguaPortal"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _JadeColor;
            fixed4 _EnergyColor;
            float _Intensity;
            float _Opening;
            float _HoverAngle;
            float _IdleFlow;
            float _IdleFlowSpeed;
            float _SpinAngle;
            float _SpinSink;
            float4 _ClipRect;

            static const float PI = 3.14159265359;
            static const float TAU = 6.28318530718;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float aaFill(float distanceValue)
            {
                float width = max(fwidth(distanceValue), 0.00045);
                return 1.0 - smoothstep(-width, width, distanceValue);
            }

            float sdBox(float2 samplePosition, float2 halfSize)
            {
                float2 delta = abs(samplePosition) - halfSize;
                return length(max(delta, 0.0)) + min(max(delta.x, delta.y), 0.0);
            }

            float ring(float radius, float center, float width)
            {
                return aaFill(abs(radius - center) - width);
            }

            float lineSegment(float2 samplePosition, float2 start, float2 end, float width)
            {
                float2 direction = end - start;
                float denominator = max(dot(direction, direction), 0.00001);
                float projection = saturate(dot(samplePosition - start, direction) / denominator);
                return aaFill(length(samplePosition - (start + projection * direction)) - width);
            }

            float trigramLine(float2 samplePosition, float row, float broken)
            {
                float2 local = samplePosition - float2(0.0, row);
                float solid = aaFill(sdBox(local, float2(0.092, 0.012)));
                float left = aaFill(sdBox(local - float2(-0.055, 0.0), float2(0.037, 0.012)));
                float right = aaFill(sdBox(local - float2(0.055, 0.0), float2(0.037, 0.012)));
                return lerp(solid, max(left, right), broken);
            }

            // Traditional south-up Earlier-Heaven bagua. Sector 0 is top/Qian,
            // then clockwise on screen: Xun, Kan, Gen, Kun, Zhen, Li, Dui.
            float trigram(float2 samplePosition, out float sequence)
            {
                float angle = atan2(samplePosition.x, samplePosition.y);
                float sectorFloat = floor((angle + PI / 8.0) / (PI / 4.0));
                sectorFloat = fmod(sectorFloat + 8.0, 8.0);
                float sector = fmod(sectorFloat, 8.0);
                float sectorAngle = sector * PI / 4.0;
                float cosineValue = cos(sectorAngle);
                float sineValue = sin(sectorAngle);
                float2 center = float2(sineValue, cosineValue) * 0.705;
                float2 delta = samplePosition - center;
                float2 local = float2(
                    delta.x * cosineValue - delta.y * sineValue,
                    delta.x * sineValue + delta.y * cosineValue
                );

                // Bit 0/1/2 encode top/middle/bottom broken lines.
                float pattern = 0.0;
                if (sector < 0.5) pattern = 0.0;       // Qian: solid solid solid
                else if (sector < 1.5) pattern = 4.0;  // Xun: solid solid broken
                else if (sector < 2.5) pattern = 5.0;  // Kan: broken solid broken
                else if (sector < 3.5) pattern = 6.0;  // Gen: solid broken broken
                else if (sector < 4.5) pattern = 7.0;  // Kun: broken broken broken
                else if (sector < 5.5) pattern = 3.0;  // Zhen: broken broken solid
                else if (sector < 6.5) pattern = 2.0;  // Li: solid broken solid
                else pattern = 1.0;                    // Dui: broken solid solid

                float topBroken = fmod(pattern, 2.0);
                float middleBroken = fmod(floor(pattern / 2.0), 2.0);
                float bottomBroken = fmod(floor(pattern / 4.0), 2.0);
                float lines = trigramLine(local, 0.033, topBroken);
                lines = max(lines, trigramLine(local, 0.0, middleBroken));
                lines = max(lines, trigramLine(local, -0.033, bottomBroken));
                sequence = sector / 8.0;
                return lines;
            }

            float taiji(float2 samplePosition, out float darkHalf, out float lightHalf)
            {
                float radius = length(samplePosition);
                float disc = aaFill(radius - 0.275);
                float upperCircle = aaFill(length(samplePosition - float2(0.0, 0.1375)) - 0.1375);
                float lowerCircle = aaFill(length(samplePosition + float2(0.0, 0.1375)) - 0.1375);
                darkHalf = disc * saturate(step(0.0, samplePosition.x) + upperCircle - lowerCircle);
                lightHalf = disc * (1.0 - darkHalf);
                float upperEye = aaFill(length(samplePosition - float2(0.0, 0.1375)) - 0.031);
                float lowerEye = aaFill(length(samplePosition + float2(0.0, 0.1375)) - 0.031);
                darkHalf = saturate(darkHalf - upperEye + lowerEye);
                lightHalf = saturate(disc - darkHalf);
                return disc;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // The RectTransform supplies perspective by scaling Y and may
                // rotate as one unit. Geometry is always authored as one circle.
                float2 planePosition = (input.uv - 0.5) * 2.0;
                float2 samplePosition = planePosition;
                float spinCosine = cos(_SpinAngle);
                float spinSine = sin(_SpinAngle);
                samplePosition = float2(
                    samplePosition.x * spinCosine - samplePosition.y * spinSine,
                    samplePosition.x * spinSine + samplePosition.y * spinCosine);
                samplePosition /= max(0.72, 1.0 - _SpinSink);
                float radius = length(samplePosition);
                float time = _Time.y;
                float pulse = 0.84 + 0.16 * sin(time * 1.35);
                float active = saturate(_Intensity);
                float overcharge = saturate((_Intensity - 0.82) / 0.68);

                float outerBoundary = ring(radius, 0.955, 0.012);
                float jadeRing = ring(radius, 0.585, 0.009);

                float spokes = 0.0;
                [unroll]
                for (int index = 0; index < 8; index++)
                {
                    float angle = index * PI / 4.0;
                    float2 direction = float2(sin(angle), cos(angle));
                    spokes = max(spokes, lineSegment(samplePosition, direction * 0.32, direction * 0.57, 0.0045));
                }

                float trigramSequence = 0.0;
                float trigramShape = trigram(samplePosition, trigramSequence);
                float chase = 0.5 + 0.5 * sin(time * 1.15 - trigramSequence * TAU);
                float trigramEnergy = trigramShape * saturate(active * 0.72 + chase * active * 0.28);

                // The whole formation wakes on hover, while a broad arc nearest
                // the pointer receives a travelling emphasis. Hover therefore
                // feels spatial instead of becoming a uniform brightness swap.
                float pointerAngle = atan2(planePosition.y, planePosition.x);
                float pointerDelta = atan2(
                    sin(pointerAngle - _HoverAngle),
                    cos(pointerAngle - _HoverAngle));
                float pointerArc = exp(-pointerDelta * pointerDelta * 3.2);
                pointerArc *= smoothstep(0.34, 0.62, radius) *
                              smoothstep(1.03, 0.72, radius);
                pointerArc *= active * (1.0 - _Opening) *
                              (0.72 + 0.28 * sin(time * 3.4));

                // Idle remains dark but never dead: a restrained current keeps
                // travelling around the complete formation, with a stronger
                // surge at the configured interval.
                float idleAngle = atan2(samplePosition.y, samplePosition.x);
                float idleHead = frac(time * _IdleFlowSpeed) * TAU - PI;
                float idleDelta = atan2(
                    sin(idleAngle - idleHead),
                    cos(idleAngle - idleHead));
                float idleArc = exp(-idleDelta * idleDelta * 5.8);
                float tailDelta = atan2(
                    sin(idleAngle - idleHead + 0.72),
                    cos(idleAngle - idleHead + 0.72));
                float idleTail = exp(-tailDelta * tailDelta * 9.2) * 0.34;
                float idleCurrent = (idleArc + idleTail) * _IdleFlow * saturate(
                    outerBoundary * 0.92
                    + jadeRing * 0.76
                    + spokes * 0.34
                    + trigramShape * 0.34);

                float darkTaiji = 0.0;
                float lightTaiji = 0.0;
                float taijiDisc = taiji(samplePosition, darkTaiji, lightTaiji);

                float angle = atan2(samplePosition.y, samplePosition.x);
                float spiral = 0.5 + 0.5 * sin(angle * 6.0 - radius * 29.0 + time * (0.55 + _Opening * 3.8));
                float openingCore = smoothstep(0.42, 0.02, radius) * _Opening;
                float openingRim = ring(radius, lerp(0.16, 0.42, _Opening), 0.018 + _Opening * 0.018) * _Opening;
                float portal = saturate(openingCore * (0.38 + spiral * 0.62) + openingRim);
                // The Taiji is the formation's visual subject and must never
                // be covered by the opening vortex or another decorative disc.
                portal *= smoothstep(0.285, 0.34, radius);

                float radialFlow = 0.0;
                [unroll]
                for (int flowIndex = 0; flowIndex < 8; flowIndex++)
                {
                    float flowAngle = flowIndex * PI / 4.0;
                    float2 direction = float2(sin(flowAngle), cos(flowAngle));
                    float phase = frac(time * 0.62 + flowIndex * 0.137);
                    float2 bead = direction * lerp(0.78, 0.31, phase);
                    radialFlow = max(radialFlow, aaFill(length(samplePosition - bead) - lerp(0.014, 0.007, phase)));
                }
                radialFlow *= smoothstep(0.08, 0.42, _Opening);

                float aura = smoothstep(1.03, 0.72, radius) * smoothstep(0.42, 0.78, radius);
                aura *= active * (0.045 + active * 0.080 + overcharge * 0.055) * pulse;

                float3 colour = 0.0;
                float alpha = 0.0;

                float baseInlay = saturate(outerBoundary + jadeRing * 0.65 + spokes * 0.55 + trigramShape * 0.92);
                colour += _JadeColor.rgb * baseInlay;
                alpha = max(alpha, baseInlay * _JadeColor.a);

                float chargedLines = saturate(
                    outerBoundary * 0.58
                    + jadeRing * 0.88
                    + spokes * 0.72
                    + trigramShape * 0.76);
                chargedLines *= active * (0.72 + overcharge * 0.48);

                float energy = saturate(
                     jadeRing * active * pulse
                    + spokes * active * 0.62
                    + trigramEnergy * active * 0.72
                    + chargedLines
                    + pointerArc * 0.82
                    + idleCurrent * 0.92
                    + portal * 1.42
                    + radialFlow * 1.25
                );
                colour += _EnergyColor.rgb * energy *
                          (0.72 + active * 0.86 + overcharge * 0.52 + _Opening * 1.55);
                alpha = max(alpha, saturate(energy * (0.42 + active * 0.48 + _Opening * 0.38)));

                float taijiAlpha = taijiDisc * (0.78 + active * 0.14);
                float3 dormantDark = float3(0.004, 0.007, 0.009);
                float3 dormantLight = float3(0.085, 0.105, 0.102);
                float3 awakenedDark = float3(0.025, 0.055, 0.064);
                float3 awakenedLight = float3(0.61, 0.74, 0.70);
                colour += darkTaiji * lerp(dormantDark, awakenedDark, active) * taijiAlpha;
                colour += lightTaiji * lerp(dormantLight, awakenedLight, active) * taijiAlpha;
                alpha = max(alpha, taijiAlpha);

                colour += _EnergyColor.rgb * aura;
                alpha = max(alpha, aura * 2.8);
                colour *= input.color.rgb;
                alpha *= input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(colour, saturate(alpha));
            }
            ENDCG
        }
    }
}
