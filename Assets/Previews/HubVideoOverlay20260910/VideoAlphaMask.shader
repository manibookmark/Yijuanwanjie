Shader "WanjieScroll/Preview/VideoAlphaMask" {
 Properties {
  [PerRendererData] _MainTex ("Video", 2D) = "black" {}
  _MaskTex ("Alpha mask", 2D) = "white" {}
  _ReferenceTex ("Stable reference", 2D) = "white" {}
  _StableOutside ("Stable surroundings", Float) = 0
 }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   sampler2D _MainTex; sampler2D _MaskTex; sampler2D _ReferenceTex; float _StableOutside;
   float windowMask(float2 uv,float4 box) {float2 d=min(uv-box.xy,box.zw-uv);return smoothstep(0,0.025,min(d.x,d.y));}
   v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
   fixed4 frag(v2f i):SV_Target { fixed3 rgb=tex2D(_MainTex,i.uv).rgb; float dynamicMask=max(windowMask(i.uv,float4(0.33,0.44,0.445,0.83)),max(windowMask(i.uv,float4(0.60,0.30,0.745,0.75)),windowMask(i.uv,float4(0.45,0.39,0.595,0.67)))); rgb=lerp(rgb,lerp(tex2D(_ReferenceTex,i.uv).rgb,rgb,dynamicMask),_StableOutside); fixed a=tex2D(_MaskTex,i.uv).r; return fixed4(rgb*i.color.rgb,a*i.color.a); }
   ENDCG
  }
 }
}