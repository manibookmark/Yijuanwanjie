Shader "WanjieScroll/Effects/Stage Boss Hit Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        _HitLocal ("Local Contact", Vector) = (0,0,0,0)
        _WorldScale ("World Scale", Vector) = (1,1,1,1)
        _Radius ("Contact Radius", Float) = 0.7
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex StageHitVert
            #pragma fragment StageHitFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float4 _HitLocal;
            float4 _WorldScale;
            float _Radius;

            struct StageHitVaryings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localPoint : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            StageHitVaryings StageHitVert(appdata_t input)
            {
                StageHitVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 flippedVertex = UnityFlipSprite(input.vertex, _Flip);
                output.vertex = UnityObjectToClipPos(flippedVertex);
                output.localPoint = flippedVertex.xy;
                output.uv = input.texcoord;
                output.color = input.color * _Color * _RendererColor;
                return output;
            }

            fixed4 StageHitFrag(StageHitVaryings input) : SV_Target
            {
                float2 offset = (input.localPoint - _HitLocal.xy) *
                    _WorldScale.xy;
                float distanceToContact = length(offset) /
                    max(0.001, _Radius);
                float softMask = 1.0 - smoothstep(
                    0.25, 1.0, distanceToContact);
                fixed sourceAlpha = SampleSpriteTexture(input.uv).a;
                return fixed4(input.color.rgb,
                    sourceAlpha * input.color.a * softMask);
            }
            ENDCG
        }
    }
}
