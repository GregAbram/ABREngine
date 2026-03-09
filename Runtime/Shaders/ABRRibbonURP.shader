Shader "ABR/Ribbon"
{
    Properties
    {
        _ColorMap           ("ColorMap", 2D)                = "white" {}
        _ColorDataMin       ("ColorDataMin", Float)         = 0.0
        _ColorDataMax       ("ColorDataMax", Float)         = 1.0
        _RibbonBrightness   ("RibbonBrightness", Float)     = 1.0
        _Cutoff             ("Alpha cutoff", Range(0,1))    = 0.5
        _Color              ("Color", Color)                = (1,1,1,1)
        _Glossiness         ("Smoothness", Range(0,1))      = 0.5
        _Metallic           ("Metallic", Range(0,1))        = 0.0
        _BlendMaps          ("Blend Maps", 2D)              = "white" {}
        _Texture            ("Textures", 2D)                = "white" {}
        _NaNColor           ("NaN Color", Color)            = (0,0,0,1)
        _HiliteColor        ("Hilite Color", Color)         = (1,1,0,1)
        _Hilite             ("Hilite", Int)                 = 0
        _UseColorMap        ("Use Color Map", Int)          = 0
        _UseLineTexture     ("Use Line Texture", Int)       = 0
        _NumTex             ("Num Textures", Int)           = 0
        _Blend              ("Blend", Float)                = 1.0
        _TextureCutoff      ("Texture Cutoff", Float)       = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "TransparentCutout"
            "RenderPipeline" = "UniversalRenderPipeline"
            "Queue"          = "AlphaTest"
        }
        LOD 200
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "ABRGlyphsCore.hlsl"

            // -------------------------------------------------------
            // Ribbon-specific properties (global scope, no CBUFFER)
            // -------------------------------------------------------
            TEXTURE2D(_Texture);    SAMPLER(sampler_Texture);
            TEXTURE2D(_NaNTexture); SAMPLER(sampler_NaNTexture);
            TEXTURE2D(_BlendMaps);  SAMPLER(sampler_BlendMaps);

            float  _RibbonBrightness;
            float  _Cutoff;
            float  _Blend;
            float  _TextureCutoff;
            int    _UseLineTexture;
            int    _Hilite;
            int    _NumTex;
            float4 _ScalarMin;
            float4 _ScalarMax;

            // Per-texture aspect ratios (max 16 textures)
            float _TextureAspect[16];
            float _NaNTextureAspect;
            float _TextureHeightWidthAspect[16];

            // -------------------------------------------------------
            // Vertex / Fragment structs
            // -------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
            };

            // -------------------------------------------------------
            // Vertex shader
            // -------------------------------------------------------
            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;

                VertexPositionInputs posInputs    = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normalInputs.normalWS;
                OUT.uv          = abs(IN.uv);
                OUT.color       = IN.color;

                return OUT;
            }

            // -------------------------------------------------------
            // Fragment shader
            // -------------------------------------------------------
            half4 frag(Varyings IN) : SV_Target
            {
                uint SupportedChannels = 4u;

                // Variables packed into vertex color: r=color scalar, g=ribbon scalar
                float4 variables = IN.color;

                // ---- Colormap ----
                float vColor     = variables.r;
                float vColorNorm = clamp(ABRRemap(vColor, _ScalarMin[0], _ScalarMax[0], 0, 1), 0.01, 0.99);

                half3 albedo;
                if (_UseColorMap == 1)
                {
                    if (!IsNaN_float(vColor))
                        albedo = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, float2(vColorNorm, 0.5)).rgb;
                    else
                        albedo = _NaNColor.rgb;
                }
                else
                {
                    albedo = _Color.rgb;
                }

                // ---- Line texture blending ----
                if (_UseLineTexture)
                {
                    // Calculate texture height percentages
                    float suma = 0;
                    for (uint i = 0u; i < (uint)_NumTex; i++)
                        suma += _TextureHeightWidthAspect[i];

                    float hwAspectPercent[16];
                    for (uint j = 0u; j < (uint)_NumTex; j++)
                        hwAspectPercent[j] = _TextureHeightWidthAspect[j] / suma;

                    uint  numGroups   = (uint)_NumTex / SupportedChannels + 1u;
                    float groupSize   = 1.0 / (float)numGroups;
                    float groupOffset = 0.5 * groupSize;

                    // Aggregate blend percentages
                    float blendPercentages[16];
                    for (uint k = 0u; k < 16u; k++)
                        blendPercentages[k] = 0.0;

                    float blendMapX = clamp(ABRRemap(variables.y, _ScalarMin.y, _ScalarMax.y, 0, 1), 0.001, 0.999);
                    for (int group = 0; group < (int)numGroups; group++)
                    {
                        float blendMapY = clamp(group * groupSize + groupOffset, 0.001, 0.999);
                        float4 blendPercentageGroup = SAMPLE_TEXTURE2D(_BlendMaps, sampler_BlendMaps, float2(blendMapX, blendMapY));

                        int index = group * (int)SupportedChannels;
                        blendPercentages[index + 0] += blendPercentageGroup.r;
                        blendPercentages[index + 1] += blendPercentageGroup.g;
                        blendPercentages[index + 2] += blendPercentageGroup.b;
                        blendPercentages[index + 3] += blendPercentageGroup.a;
                    }

                    // Blend textures
                    float3 textureColor  = 0;
                    float  vOffsetTotal  = 0;
                    for (uint texIndex = 0u; texIndex < (uint)_NumTex; texIndex++)
                    {
                        float u = fmod(IN.uv.x / _TextureAspect[texIndex], 1.0);
                        float vOffset = hwAspectPercent[texIndex];
                        float v = IN.uv.y * vOffset + vOffsetTotal;
                        vOffsetTotal += vOffset;

                        float3 currentColor = SAMPLE_TEXTURE2D(_Texture, sampler_Texture, float2(u, v)).rgb;
                        currentColor *= sqrt(blendPercentages[texIndex]);
                        textureColor += currentColor;
                    }

                    if (IsNaN_float(variables.y))
                        textureColor = SAMPLE_TEXTURE2D(_NaNTexture, sampler_NaNTexture, float2(IN.uv.x / _NaNTextureAspect, IN.uv.y)).rgb;

                    // Alpha cutout based on texture
                    if (textureColor.g > _TextureCutoff)
                        clip(-1);
                }

                // ---- Hilite override ----
                if (_Hilite)
                    albedo = _HiliteColor.rgb;

                // ---- Flat lighting (RibbonBrightness replaces NdotL) ----
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half3 lighting  = mainLight.color * mainLight.shadowAttenuation * _RibbonBrightness;

                // Ambient
                float3 normalWS = normalize(IN.normalWS);
                half3 ambient   = SampleSH(normalWS);

                half3 color = albedo * (ambient + lighting);
                return half4(color, 1.0);
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

            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "ABRGlyphsCore.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;

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

            #include "ABRGlyphsCore.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
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

            #include "ABRGlyphsCore.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DNVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            DNVaryings DepthNormalsVert(DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
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
