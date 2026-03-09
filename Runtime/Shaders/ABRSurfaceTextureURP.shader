// ABR/SurfaceTexture
// URP port of your Built-in Surface shader (albedo-only) that:
// - Uses vertex color.x as the “colormap variable” (vColor)
// - Uses vertex color.y as the “pattern variable” (blend map lookup)
// - Samples a 1D colormap from a 2D texture at (v, 0.5)
// - Does the same triplanar + seam/corner blending of a stacked pattern atlas
// - Multiplies finalColor * textureColor when _NumTex > 0
//
// Notes vs your Built-in version:
// - Your original shader never actually wrote normals/metal/smoothness into the SurfaceOutput,
//   it only set o.Albedo. This URP version uses URP’s PBR lighting, feeding:
//     Albedo = computed color
//     Metallic = _Metallic
//     Smoothness = _Glossiness
//   and leaves Normal at the default (mesh normal).
// - Your original code computes “norm” from _PatternNormal but never uses it.
//   This URP port keeps _PatternNormal sampling code commented where it was, but does not apply it.

Shader "ABR/SurfaceTexture"
{
    Properties
    {
        [PerRendererData]_ColorMap("ColorMap", 2D) = "white" {}
        [PerRendererData]_ColorDataMin("ColorDataMin", Float) = 0.0
        [PerRendererData]_ColorDataMax("ColorDataMax", Float) = 1.0

        _PatternNormal("Normal (RGB)", 2D) = "bump" {}
        _Pattern("Stacked Textures", 2D) = "white" {}
        _BlendMaps("Stacked Blend Maps", 2D) = "white" {}

        _Color("Color", Color) = (1,1,1,1)
        _Glossiness("Smoothness", Range(0,1)) = 0.5
        _Metallic("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardLitCustom"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag

            // URP lighting variants
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // -------- Textures / samplers --------
            TEXTURE2D(_ColorMap);      SAMPLER(sampler_ColorMap);
            TEXTURE2D(_Pattern);       SAMPLER(sampler_Pattern);
            TEXTURE2D(_BlendMaps);     SAMPLER(sampler_BlendMaps);
            TEXTURE2D(_NaNPattern);    SAMPLER(sampler_NaNPattern);
            TEXTURE2D(_PatternNormal); SAMPLER(sampler_PatternNormal);

            // -------- Params --------
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                half   _Glossiness;
                half   _Metallic;

                float4 _NaNColor;
                float4 _HiliteColor;

                int    _UseColorMap;
                int    _UsePatternVariable;

                float  _ColorDataMin;
                float  _ColorDataMax;

                int    _HasNaNPattern;
                uint   _NumTex;

                float  _PatternDataMin;
                float  _PatternDataMax;

                float  _PatternDirectionBlend;
                float  _PatternScale;
                float  _PatternSaturation;
                float  _PatternIntensity;
                float  _PatternBlendWidth;

                int    _Hilite;
            CBUFFER_END

            // ----------------- Utility funcs (ported from your cginc) -----------------
            float XRemap(float dataValue, float from0, float to0, float from1, float to1)
            {
                return from1 + (dataValue - from0) * (to1 - from1) / (to0 - from0);
            }

            float IsNaN_float(float In)
            {
                return (In < 0.0 || In > 0.0 || In == 0.0) ? 0 : 1;
            }

            float2 ActualTexCoord(float2 uv, int texIndex)
            {
                return float2(uv.x, (texIndex + uv.y) / max(1u, _NumTex));
            }

            float3 CornerBlend(float3 color, float2 xyOffset, float2 horizontalTexCoord, float2 verticalTexCoord, float2 sharedTexCoord)
            {
                float2 a = float2(0.5,0.5);
                float2 b = normalize(float2(1,1));
                float2 x = xyOffset / _PatternBlendWidth;
                float d = length(x - (a + dot(x - a, b) * b));
                float sideBlend = d / sqrt(2);

                float cornerBlend = clamp(1 - length(1 - xyOffset / _PatternBlendWidth), 0, 1);

                float3 xColor = SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, horizontalTexCoord).rgb;
                float3 yColor = SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, verticalTexCoord).rgb;
                float3 cornerColor = SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, sharedTexCoord).rgb;

                if (xyOffset.x > xyOffset.y)
                    color = lerp(lerp(color, cornerColor, x.y), xColor, sideBlend);
                else
                    color = lerp(lerp(color, cornerColor, x.x), yColor, sideBlend);

                return lerp(color, cornerColor, cornerBlend);
            }

            float3 SeamBlend(float3 color, float2 uv, float2 buv, int texIndex)
            {
                if (uv.x < _PatternBlendWidth && uv.y < _PatternBlendWidth)
                {
                    float2 xyOffset = float2(_PatternBlendWidth - uv.x, _PatternBlendWidth - uv.y);
                    float2 horizontalTexCoord = ActualTexCoord(float2(1 - xyOffset.x, buv.y), texIndex);
                    float2 verticalTexCoord   = ActualTexCoord(float2(buv.x, 1 - xyOffset.y), texIndex);
                    float2 sharedTexCoord     = ActualTexCoord((uv + _PatternBlendWidth) * (1 - 2 * _PatternBlendWidth), texIndex);
                    color = CornerBlend(color, xyOffset, horizontalTexCoord, verticalTexCoord, sharedTexCoord);
                }
                else if (uv.x > 1 - _PatternBlendWidth && uv.y < _PatternBlendWidth)
                {
                    float2 xyOffset = float2(uv.x - (1 - _PatternBlendWidth), _PatternBlendWidth - uv.y);
                    float2 horizontalTexCoord = ActualTexCoord(float2(xyOffset.x, buv.y), texIndex);
                    float2 verticalTexCoord   = ActualTexCoord(float2(buv.x, 1 - xyOffset.y), texIndex);
                    float2 sharedTexCoord     = ActualTexCoord(float2(xyOffset.x, _PatternBlendWidth + uv.y) * (1 - 2 * _PatternBlendWidth), texIndex);
                    color = CornerBlend(color, xyOffset, horizontalTexCoord, verticalTexCoord, sharedTexCoord);
                }
                else if (uv.x < _PatternBlendWidth && uv.y > 1 - _PatternBlendWidth)
                {
                    float2 xyOffset = float2(_PatternBlendWidth - uv.x, uv.y - (1 - _PatternBlendWidth));
                    float2 horizontalTexCoord = ActualTexCoord(float2(1 - xyOffset.x, buv.y), texIndex);
                    float2 verticalTexCoord   = ActualTexCoord(float2(buv.x, xyOffset.y), texIndex);
                    float2 sharedTexCoord     = ActualTexCoord(float2(_PatternBlendWidth + uv.x, xyOffset.y) * (1 - 2 * _PatternBlendWidth), texIndex);
                    color = CornerBlend(color, xyOffset, horizontalTexCoord, verticalTexCoord, sharedTexCoord);
                }
                else if (uv.x > 1 - _PatternBlendWidth && uv.y > 1 - _PatternBlendWidth)
                {
                    float2 xyOffset = float2(uv.x - (1 - _PatternBlendWidth), uv.y - (1 - _PatternBlendWidth));
                    float2 horizontalTexCoord = ActualTexCoord(float2(xyOffset.x, buv.y), texIndex);
                    float2 verticalTexCoord   = ActualTexCoord(float2(buv.x, xyOffset.y), texIndex);
                    float2 sharedTexCoord     = ActualTexCoord(xyOffset * (1 - 2 * _PatternBlendWidth), texIndex);
                    color = CornerBlend(color, xyOffset, horizontalTexCoord, verticalTexCoord, sharedTexCoord);
                }
                else if (uv.x <= _PatternBlendWidth)
                {
                    float xOffset = _PatternBlendWidth - uv.x;
                    float2 texCoord = ActualTexCoord(float2(1 - xOffset, buv.y), texIndex);
                    float blend = 0.5 * xOffset / _PatternBlendWidth;
                    color = lerp(color, SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, texCoord).rgb, blend);
                }
                else if (uv.y <= _PatternBlendWidth)
                {
                    float yOffset = _PatternBlendWidth - uv.y;
                    float2 texCoord = ActualTexCoord(float2(buv.x, 1 - yOffset), texIndex);
                    float blend = 0.5 * yOffset / _PatternBlendWidth;
                    color = lerp(color, SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, texCoord).rgb, blend);
                }
                else if (uv.x >= 1 - _PatternBlendWidth)
                {
                    float xOffset = uv.x - (1 - _PatternBlendWidth);
                    float2 texCoord = ActualTexCoord(float2(xOffset, buv.y), texIndex);
                    float blend = 0.5 * xOffset / _PatternBlendWidth;
                    color = lerp(color, SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, texCoord).rgb, blend);
                }
                else if (uv.y >= 1 - _PatternBlendWidth)
                {
                    float yOffset = uv.y - (1 - _PatternBlendWidth);
                    float2 texCoord = ActualTexCoord(float2(buv.x, yOffset), texIndex);
                    float blend = 0.5 * yOffset / _PatternBlendWidth;
                    color = lerp(color, SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, texCoord).rgb, blend);
                }

                return color;
            }

            // ----------------- Vertex/fragment IO -----------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 positionOS   : TEXCOORD2;
                float3 absNormalOS  : TEXCOORD3;
                half4  color        : COLOR;
                float  fogFactor    : TEXCOORD4;
                float4 shadowCoord  : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(v.normalOS);

                o.positionCS  = pos.positionCS;
                o.positionWS  = pos.positionWS;
                o.normalWS    = nrm.normalWS;

                o.positionOS  = v.positionOS.xyz;
                o.absNormalOS = abs(v.normalOS);

                o.color = v.color;

                o.fogFactor   = ComputeFogFactor(o.positionCS.z);
                o.shadowCoord = TransformWorldToShadowCoord(o.positionWS);
                return o;
            }

            float4 CalculateABRTexturedSurfaceColor(float3 positionOS, float3 absNormalOS, half4 vertexColor)
            {
                const uint SupportedChannels = 4u;

                float3 poscoodinates = positionOS.xyz;
                float4 variables = vertexColor;

                uint numGroups = _NumTex / SupportedChannels + 1u;
                float groupSize = 1.0 / max(1.0, (float)numGroups);
                float groupOffset = 0.5 * groupSize;

                float3 normal = absNormalOS.xyz;

                float patternScale = (_PatternScale == 0.0) ? 1e-6 : _PatternScale;

                float2 uv0  = frac(poscoodinates.yz / patternScale);
                float2 buv0 = _PatternBlendWidth + uv0 * (1 - 2 * _PatternBlendWidth);

                float2 uv1  = frac(poscoodinates.xz / patternScale);
                float2 buv1 = _PatternBlendWidth + uv1 * (1 - 2 * _PatternBlendWidth);

                float2 uv2  = frac(poscoodinates.xy / patternScale);
                float2 buv2 = _PatternBlendWidth + uv2 * (1 - 2 * _PatternBlendWidth);

                float a = normal.x, b = normal.y, c = normal.z;
                float sum = a + b + c;
                sum = (sum == 0.0) ? 1.0 : sum;
                a /= sum; b /= sum; c /= sum;

                float mx = max(a, max(b, c));

                float degree = _PatternDirectionBlend;
                if (degree > 0.04)
                {
                    a = pow(-2 * a * a * a + 3 * a * a, 1 / degree);
                    b = pow(-2 * b * b * b + 3 * b * b, 1 / degree);
                    c = pow(-2 * c * c * c + 3 * c * c, 1 / degree);
                }
                else
                {
                    a = (a < mx) ? 0 : 1;
                    b = (b < mx) ? 0 : 1;
                    c = (c < mx) ? 0 : 1;
                }

                sum = a + b + c;
                sum = (sum == 0.0) ? 1.0 : sum;
                a /= sum; b /= sum; c /= sum;

                float blendPercentages[16];
                [unroll] for (uint i = 0u; i < 16u; i++) blendPercentages[i] = 0.0;

                float blendMapX = clamp(XRemap(variables.y, _PatternDataMin, _PatternDataMax, 0, 1), 0.001, 0.999);

                for (int group = 0; group < (int)numGroups; group++)
                {
                    float blendMapY = group * groupSize + groupOffset;
                    float4 g = SAMPLE_TEXTURE2D(_BlendMaps, sampler_BlendMaps, float2(blendMapX, blendMapY));
                    int index = group * (int)SupportedChannels;
                    if (index + 0 < 16) blendPercentages[index + 0] += g.r;
                    if (index + 1 < 16) blendPercentages[index + 1] += g.g;
                    if (index + 2 < 16) blendPercentages[index + 2] += g.b;
                    if (index + 3 < 16) blendPercentages[index + 3] += g.a;
                }

                float3 textureColor = 0;

                uint safeNumTex = min(_NumTex, 16u);
                for (uint texIndex = 0u; texIndex < safeNumTex; texIndex++)
                {
                    if (blendPercentages[texIndex] > 0)
                    {
                        float3 colorA = SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, ActualTexCoord(buv0, (int)texIndex)).rgb;
                        colorA = SeamBlend(colorA, uv0, buv0, (int)texIndex);

                        float3 colorB = SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, ActualTexCoord(buv1, (int)texIndex)).rgb;
                        colorB = SeamBlend(colorB, uv1, buv1, (int)texIndex);

                        float3 colorC = SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, ActualTexCoord(buv2, (int)texIndex)).rgb;
                        colorC = SeamBlend(colorC, uv2, buv2, (int)texIndex);

                        float3 currentColor = colorA * a + colorB * b + colorC * c;
                        currentColor *= blendPercentages[texIndex];
                        textureColor += currentColor;
                    }
                }

                if (IsNaN_float(variables.y))
                {
                    if (_HasNaNPattern != 0)
                    {
                        float3 colorA = SAMPLE_TEXTURE2D(_NaNPattern, sampler_NaNPattern, buv0).rgb;
                        float3 colorB = SAMPLE_TEXTURE2D(_NaNPattern, sampler_NaNPattern, buv1).rgb;
                        float3 colorC = SAMPLE_TEXTURE2D(_NaNPattern, sampler_NaNPattern, buv2).rgb;
                        textureColor = colorA * a + colorB * b + colorC * c;
                    }
                    else
                    {
                        textureColor = _NaNColor.rgb;
                    }
                }

                float gray = dot(textureColor, float3(0.3, 0.59, 0.11));
                textureColor = lerp(gray.xxx, textureColor, _PatternSaturation);
                textureColor = lerp(1.0.xxx, textureColor, _PatternIntensity);

                float vColor = variables.x;
                float vColorNorm = clamp(XRemap(vColor, _ColorDataMin, _ColorDataMax, 0, 1), 0.01, 0.99);

                float3 finalColor = 0;

                if (_UseColorMap == 1)
                {
                    if (!IsNaN_float(vColor))
                        finalColor = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, float2(vColorNorm, 0.5)).rgb;
                    else
                        finalColor = _NaNColor.rgb;
                }
                else
                {
                    finalColor = _Color.rgb;
                }

                if (_Hilite != 0)
                    finalColor = _HiliteColor.rgb;

                if (_NumTex > 0u)
                    finalColor *= textureColor;

                return float4(finalColor, _Color.a);
            }

#if 1 == 2
            // ----------------- Custom URP forward lighting (no UniversalFragmentPBR) -----------------
            float3 ApplyMainAndAdditionalLights(float3 albedo, float3 positionWS, float3 normalWS, float3 viewDirWS, float4 shadowCoord)
            {
                normalWS = normalize(normalWS);
                viewDirWS = normalize(viewDirWS);

                // Simple ambient (you can remove or replace with SH/Probe sampling if you want)
                float3 color = 0.0;

                // Convert smoothness -> spec exponent (rough approximation)
                float smooth = saturate(_Glossiness);
                float specPow = lerp(8.0, 256.0, smooth);

                // Metallic as a simple spec strength mix (not physically correct, but controllable)
                float specStrength = lerp(0.08, 1.0, saturate(_Metallic));

                // Main light
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = normalize(mainLight.direction);
                float NdotL = saturate(dot(normalWS, L));

                float3 H = normalize(L + viewDirWS);
                float NdotH = saturate(dot(normalWS, H));
                float spec = pow(NdotH, specPow) * specStrength;

                float atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                color += (albedo * NdotL + spec) * mainLight.color * atten;

                // Additional lights (pixel)
                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light light = GetAdditionalLight(li, positionWS);

                    float3 L2 = normalize(light.direction);
                    float NdotL2 = saturate(dot(normalWS, L2));

                    float3 H2 = normalize(L2 + viewDirWS);
                    float NdotH2 = saturate(dot(normalWS, H2));
                    float spec2 = pow(NdotH2, specPow) * specStrength;

                    float atten2 = light.distanceAttenuation * light.shadowAttenuation;
                    color += (albedo * NdotL2 + spec2) * light.color * atten2;
                }
                #endif

                return color;
            }
#else
float3 ApplyMainAndAdditionalLights(float3 albedo, float3 positionWS, float3 normalWS, float3 viewDirWS, float4 shadowCoord)
{
    normalWS = normalize(normalWS);
    viewDirWS = normalize(viewDirWS);

    // --- Ambient (SH) so objects aren’t black without direct lights ---
    float3 color = SampleSH(normalWS) * albedo;

    // Smoothness -> spec exponent (simple approximation)
    float smooth = saturate(_Glossiness);
    float specPow = lerp(8.0, 256.0, smooth);

    // Simple “metallic as spec strength” (non-physical but controllable)
    float specStrength = lerp(0.08, 1.0, saturate(_Metallic));

    // --- Main light (directional) ---
    Light mainLight = GetMainLight(shadowCoord);
    float3 L = normalize(mainLight.direction);
    float NdotL = saturate(dot(normalWS, L));

    float3 H = normalize(L + viewDirWS);
    float spec = pow(saturate(dot(normalWS, H)), specPow) * specStrength;

    float atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
    color += (albedo * NdotL + spec) * mainLight.color * atten;

    // --- Additional lights (point/spot) ---
    #if defined(_ADDITIONAL_LIGHTS)
    uint lightCount = GetAdditionalLightsCount();
    for (uint li = 0u; li < lightCount; li++)
    {
        Light light = GetAdditionalLight(li, positionWS);
        float3 L2 = normalize(light.direction);
        float NdotL2 = saturate(dot(normalWS, L2));

        float3 H2 = normalize(L2 + viewDirWS);
        float spec2 = pow(saturate(dot(normalWS, H2)), specPow) * specStrength;

        float atten2 = light.distanceAttenuation * light.shadowAttenuation;
        color += (albedo * NdotL2 + spec2) * light.color * atten2;
    }
    #endif

    return color;
}
#endif
#if 1 == 1
half4 frag(Varyings i) : SV_Target
{
    float4 baseColor = CalculateABRTexturedSurfaceColor(i.positionOS, i.absNormalOS, i.color);

    float3 N = normalize(i.normalWS);

    // Ambient lighting (SH)
    float3 ambient = SampleSH(N);

    // Main light WITH shadows
    Light mainLight = GetMainLight(i.shadowCoord);
    float3 L = normalize(mainLight.direction);
    float NdotL = saturate(dot(N, L));

    float atten = mainLight.shadowAttenuation;

    float3 lit = baseColor.rgb * (ambient + mainLight.color * NdotL * atten);
float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
float3 H = normalize(L + V);

float smooth = saturate(_Glossiness);
float specPow = lerp(8.0, 128.0, smooth);  // lower max
float spec = pow(saturate(dot(N, H)), specPow);

// Keep spec subtle
lit += spec * mainLight.color * 0.25;

#if defined(_ADDITIONAL_LIGHTS)
uint lightCount = GetAdditionalLightsCount();
for (uint li = 0u; li < lightCount; li++)
{
    Light light = GetAdditionalLight(li, i.positionWS);
    float3 L2 = normalize(light.direction);
    float NdotL2 = saturate(dot(N, L2));

    lit += baseColor.rgb * light.color * NdotL2 * light.distanceAttenuation;
}
#endif


    return half4(lit, 1);
}
#else
            half4 frag(Varyings i) : SV_Target
            {
                float4 baseColor = CalculateABRTexturedSurfaceColor(i.positionOS, i.absNormalOS, i.color);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
#if 1 == 1

                float3 lit = ApplyMainAndAdditionalLights(
                    baseColor.rgb,
                    i.positionWS,
                    i.normalWS,     // mesh normal only (same behavior as your original Surface shader’s albedo-only usage)
                    viewDirWS,
                    i.shadowCoord
                );

                lit = MixFog(lit, i.fogFactor);
#else
                float3 lit = baseColor;
#endif
                return half4(SampleSH(normalize(i.normalWS)), 1);
                return half4(lit, 1.0);
            }
#endif

            ENDHLSL
        }

        // ShadowCaster so it still casts shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
