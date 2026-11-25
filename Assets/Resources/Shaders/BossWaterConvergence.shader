Shader "WanjieScroll/BossWaterConvergence"
{
    Properties { _Progress ("Convergence", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            float _Progress;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float edgeDistance = abs(i.uv.y * 2 - 1);
                float edge = 1 - smoothstep(0.18, 1, edgeDistance);
                float flow = 0.72 + 0.28 * sin(i.uv.x * 35 - _Progress * 24);
                float ends = smoothstep(0, 0.07, i.uv.x) * (1 - smoothstep(0.80, 1, i.uv.x));
                fixed4 color = i.color;
                color.rgb += pow(1 - edgeDistance, 5) * 0.20 * flow;
                color.a *= edge * flow * ends;
                return color;
            }
            ENDCG
        }
    }
}
