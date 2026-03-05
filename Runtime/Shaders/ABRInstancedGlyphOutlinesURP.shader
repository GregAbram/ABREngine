Shader "ABR/InstancedGlyphsOutline"
{
    Properties
    {
        _RenderInfo         ("Render Info", Vector)         = (0,0,0,0)
        _Color              ("Color", Color)                = (1,1,1,1)
        _NaNColor           ("NaN Color", Color)            = (0,0,0,1)
        _HiliteColor        ("Hilite Color", Color)         = (1,1,0,1)
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

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED

            #include "ABRGlyphsCore.hlsl"

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float3 normalOS    : NORMAL;
                float4 tangentOS   : TANGENT;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                half3  vertexSH    : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs    = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS  = posInputs.positionCS;
                OUT.positionWS   = posInputs.positionWS;
                OUT.uv           = IN.uv;
                OUT.normalWS     = normalInputs.normalWS;
                OUT.tangentWS    = normalInputs.tangentWS;
                OUT.bitangentWS  = normalInputs.bitangentWS;
                OUT.vertexSH     = SampleSH(normalInputs.normalWS);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float4 renderInfo;
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                uint instanceIndex  = unity_InstanceID / 32;
                uint instanceOffset = unity_InstanceID % 32;
                renderInfo = renderInfoBuffer[unity_InstanceID];
#else
                renderInfo = _RenderInfo;
#endif
                clip(renderInfo.a);

                float scalarValue     = renderInfo.r;
                float scalarValueNorm = clamp(ABRRemap(scalarValue, _ColorDataMin, _ColorDataMax, 0, 1), 0.01, 0.99);

                half3 albedo;
                if (_UseColorMap == 1)
                {
                    if (!IsNaN_float(scalarValue))
                        albedo = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, float2(scalarValueNorm, 0.25)).rgb;
                    else
                        albedo = _NaNColor.rgb;
                }
                else
                {
                    albedo = _Color.rgb;
                }

#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                if (perInstanceHiliteBuffer[instanceIndex] & (1 << instanceOffset))
                    albedo = _HiliteColor.rgb;
#endif

                float4 normalSample = SAMPLE_TEXTURE2D(_Normal, sampler_Normal, IN.uv);
                float3 normalTS     = UnpackNormal(normalSample);
                float3x3 TBN = float3x3(
                    normalize(IN.tangentWS),
                    normalize(IN.bitangentWS),
                    normalize(IN.normalWS)
                );
                float3 normalWS = normalize(mul(normalTS, TBN));

                float3 viewDirWS   = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                half3 ambient = IN.vertexSH;

                Light mainLight = GetMainLight(shadowCoord);
                half NdotL    = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = mainLight.color * mainLight.shadowAttenuation * NdotL;

                half3 halfDir  = normalize(mainLight.direction + viewDirWS);
                half NdotH     = saturate(dot(normalWS, halfDir));
                half specPow   = exp2(_Glossiness * 10.0 + 1.0);
                half3 specular = mainLight.color * mainLight.shadowAttenuation * pow(NdotH, specPow) * _Glossiness;

                half3 additionalDiffuse = half3(0, 0, 0);
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < pixelLightCount; i++)
                {
                    Light light = GetAdditionalLight(i, IN.positionWS);
                    half NdotL_add = saturate(dot(normalWS, light.direction));
                    additionalDiffuse += light.color * light.distanceAttenuation * light.shadowAttenuation * NdotL_add;
                }

                half3 color = albedo * (ambient + diffuse + additionalDiffuse) + specular;
                //return half4(color, 1.0);
                return half4(0, 0, 1, 1); // blue = ForwardLit
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

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

                if (_ForceOutlineColor == 1)
                    return _OutlineColor;

                float scalarValue     = renderInfo.r;
                float scalarValueNorm = clamp(ABRRemap(scalarValue, _ColorDataMin, _ColorDataMax, 0, 1), 0.01, 0.99);

                if (_UseColorMap == 1)
                {
                    if (!IsNaN_float(scalarValue))
                        return SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, float2(scalarValueNorm, 0.25));
                    else
                        return _NaNColor;
                }

                //return _OutlineColor;
                return half4(1, 0, 0, 1); // red = Outline
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