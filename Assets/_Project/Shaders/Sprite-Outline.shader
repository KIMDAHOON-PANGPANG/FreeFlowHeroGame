// URP 2D 스프라이트 아웃라인 + 플래시 통합 셰이더
// _OutlineEnabled(0/1)로 아웃라인 활성/비활성
// _FlashAmount(0~1)로 원본 텍스처 ↔ _FlashColor 보간 (HitFlash 호환)
// _OutlineColor + _OutlineWidth로 아웃라인 색상/두께 제어
Shader "REPLACED/Sprite-Outline"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        _OutlineColor ("Outline Color", Color) = (1, 0.9, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 5)) = 2
        _OutlineEnabled ("Outline Enabled", Float) = 0
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
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize; // x=1/width, y=1/height

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _FlashColor;
                half _FlashAmount;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineEnabled;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;

                #ifdef PIXELSNAP_ON
                o.pos = UnityPixelSnap(o.pos);
                #endif

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // SpriteRenderer.color 적용 (vertex color)
                texColor *= i.color;

                // ★ 아웃라인: 현재 텍셀이 투명이고 인접에 불투명이 있으면 아웃라인 색상 출력
                if (_OutlineEnabled > 0.5 && texColor.a < 0.1)
                {
                    float2 texelSize = _MainTex_TexelSize.xy * _OutlineWidth;

                    // 8방향 샘플링
                    half a = 0;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2( texelSize.x, 0)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(-texelSize.x, 0)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0,  texelSize.y)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0, -texelSize.y)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2( texelSize.x,  texelSize.y)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(-texelSize.x,  texelSize.y)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2( texelSize.x, -texelSize.y)).a;
                    a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(-texelSize.x, -texelSize.y)).a;

                    if (a > 0)
                    {
                        // 아웃라인 픽셀: premultiplied alpha
                        half outlineAlpha = saturate(a) * _OutlineColor.a;
                        return half4(_OutlineColor.rgb * outlineAlpha, outlineAlpha);
                    }
                }

                // ★ 플래시: RGB만 _FlashColor로 보간, alpha 유지
                texColor.rgb = lerp(texColor.rgb, _FlashColor.rgb * texColor.a, _FlashAmount);

                // Premultiplied alpha (Unity 스프라이트 기본)
                return texColor;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
