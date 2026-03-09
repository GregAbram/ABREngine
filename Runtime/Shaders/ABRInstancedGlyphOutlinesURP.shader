Shader "ABR/InstancedGlyphsOutline"
{
    Properties
    {
        _RenderInfo         ("Render Info", Vector)         = (0,0,0,0)
        _Color              ("Color", Color)                = (1,1,1,1)
        _NaNColor           ("NaN Color", Color)            = (0,0,0,1)
        _ColorDataMin       ("Color Data Min", Float)       = 0.0
        _ColorDataMax       ("Color Data Max", Float)       = 1.0
        _UseColorMap        ("Use Color Map", Int)          = 0
        _ForceOutlineColor  ("Force Outline Color", Int)    = 0
        _OutlineWidth       ("Outline Width", Float)        = 0.02
        _OutlineColor       ("Outline Color", Color)        = (0,0,0,1)
        _MainTex            ("Albedo (RGB)", 2D)            = "white" {}
        _Glossiness         ("Smoothness", Range(0,1))      = 0.5
        _Metallic           ("Metallic", Range(0,1))        = 0.0
        _Normal             ("Normal (RGB)", 2D)            = "bump" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalRenderPipeline"
            "Queue"          = "Geometry"
        }
        LOD 200

        // Single pass: render back faces only, expanded along world-space normals. 
        // Drawn as a second DrawMeshInstancedProcedural call from C#,
        // so no multi-pass conflicts with URP's forward renderer.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "ABRGlyphsCore.hlsl"

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            { 
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            OutlineVaryings OutlineVert(OutlineAttributes IN)
            {
                OutlineVaryings OUT = (OutlineVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Expand vertex along world-space normal by outline width
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                posWS          += normalize(normalWS) * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);

                return OUT;
            }

            half4 OutlineFrag(OutlineVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

 
                float4 renderInfo;
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                renderInfo = renderInfoBuffer[unity_InstanceID];
#else
                renderInfo = _RenderInfo;
#endif
                clip(renderInfo.a);

                // Force solid outline color
                if (_ForceOutlineColor == 1)
                    return _OutlineColor;

                // Otherwise match the glyph's colormap color
                float scalarValue     = renderInfo.r;
                float scalarValueNorm = clamp(ABRRemap(scalarValue, _ColorDataMin, _ColorDataMax, 0, 1), 0.01, 0.99);

                if (_UseColorMap == 1)
                {
                    if (!IsNaN_float(scalarValue))
                        return SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, float2(scalarValueNorm, 0.25));
                    else
                        return _NaNColor;
                }

                return _OutlineColor;

            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "ABRGlyphsCore.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, lightDir));

                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionHCS = posCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "ABRGlyphsCore.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target { return 1; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex   DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "ABRGlyphsCore.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DNVaryings DepthNormalsVert(DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 DepthNormalsFrag(DNVaryings IN) : SV_Target
            {
                return float4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
