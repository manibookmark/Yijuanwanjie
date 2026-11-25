Shader "WanjieScroll/UI/SealStampChromaKey"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (1, 0, 1, 1)
        _Similarity ("Key Similarity", Range(0, 1)) = 0.10
        _Smoothness ("Key Edge Softness", Range(0.001, 0.5)) = 0.12
        _Spill ("Magenta Spill Removal", Range(0, 2)) = 1.20
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _KeyColor;
            float _Similarity;
            float _Smoothness;
            float _Spill;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 sampleColor = tex2D(_MainTex, input.uv);
                float chromaDistance = distance(
                    sampleColor.rgb,
                    _KeyColor.rgb);
                float distanceMask = 1.0 - smoothstep(
                    _Similarity,
                    _Similarity + _Smoothness,
                    chromaDistance);

                // H.264 4:2:0 会把紫色键色污染到边缘。除了纯色距离，
                // 再用“红蓝同时高于绿”识别压缩后的紫边；红色印面与
                // 青色粒子不具备这个特征，因此不会被一并抠掉。
                float magentaDominance = saturate(
                    min(sampleColor.r, sampleColor.b) - sampleColor.g);
                float dominanceMask = smoothstep(
                    _Similarity,
                    _Similarity + _Smoothness * 3.0,
                    magentaDominance);
                float keyMask = max(distanceMask, dominanceMask);

                float spill = saturate(magentaDominance * _Spill);
                sampleColor.r = max(0, sampleColor.r - spill * 0.82);
                sampleColor.b = max(0, sampleColor.b - spill * 0.82);
                sampleColor.g = saturate(sampleColor.g + spill * 0.10);
                sampleColor.a *= 1.0 - keyMask;
                return sampleColor * input.color;
            }
            ENDCG
        }
    }
}
