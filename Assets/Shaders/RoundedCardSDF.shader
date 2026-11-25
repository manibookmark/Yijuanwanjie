Shader "UI/RoundedCardSDF"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CornerRadius ("Corner Radius (px)", Float) = 5
        _BorderWidth ("Border Width (px)", Float) = 1.5
        _BorderColor ("Border Color", Color) = (0,0,0,1)
        _CardSize ("Card Size (px)", Vector) = (200,300,0,0)

        // Paper fiber Multiply
        _PaperTex ("Paper Fiber Texture", 2D) = "white" {}
        _PaperColor ("Paper Tint", Color) = (1,1,1,0.12)
        _PaperTiling ("Paper Tiling", Float) = 1.0

        _Stencil ("Stencil ID", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
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
            Name "RoundedCardSDF"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1; // UnityUV1 for paper
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 paperUV : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
                float4 localPos : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _CornerRadius;
            float _BorderWidth;
            fixed4 _BorderColor;
            float4 _CardSize;

            sampler2D _PaperTex;
            float4 _PaperTex_ST;
            fixed4 _PaperColor;
            float _PaperTiling;

            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.color = v.color * _Color;
                OUT.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                // Paper UV: tile based on pixel position within the card
                OUT.paperUV = v.texcoord * _PaperTiling;
                OUT.localPos = v.vertex; // local position for SDF
                return OUT;
            }

            // Center-symmetric SDF for rounded rectangle
            // Returns negative inside, positive outside
            float sdRoundedRect(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Calculate SDF in local pixel space
                float2 halfSize = _CardSize.xy * 0.5;
                float radius = max(_CornerRadius, 0.5);
                float dist = sdRoundedRect(IN.localPos.xy, halfSize, radius);

                // Anti-alias using screen-space derivatives
                float aa = fwidth(dist);
                float alpha = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist);

                // Base color: use IN.color directly (Image vertex color = cardBaseColor)
                // Don't multiply by _MainTex since backgroundImage has no sprite
                half4 mainColor = IN.color;
                mainColor.a *= alpha;

                // Paper fiber Multiply
                half4 paperTex = tex2D(_PaperTex, IN.paperUV);
                float3 multiplied = mainColor.rgb * paperTex.rgb;
                float paperAlpha = _PaperColor.a * alpha;
                float3 finalRGB = lerp(mainColor.rgb, multiplied, paperAlpha);

                // Border: draw ring at edge
                float borderDist = dist + _BorderWidth;
                float borderAlpha = (1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist))
                                  * smoothstep(-aa * 0.5, aa * 0.5, borderDist);
                finalRGB = lerp(finalRGB, _BorderColor.rgb, borderAlpha * _BorderColor.a);

                float finalAlpha = max(mainColor.a, borderAlpha * _BorderColor.a);

                clip(finalAlpha - 0.001);

                return half4(finalRGB, finalAlpha);
            }
            ENDCG
        }
    }
}
