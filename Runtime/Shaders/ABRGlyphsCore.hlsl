#ifndef ABR_GLYPHS_CORE_HLSL
#define ABR_GLYPHS_CORE_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
TEXTURE2D(_Normal);     SAMPLER(sampler_Normal);
TEXTURE2D(_ColorMap);   SAMPLER(sampler_ColorMap);

// Global scope - NOT in CBUFFER so MaterialPropertyBlock can set these from C#
float4 _MainTex_ST;
float4 _Normal_ST;
float4 _RenderInfo;
float4 _Color;
float4 _NaNColor;
float4 _HiliteColor;
float  _ColorDataMin;
float  _ColorDataMax;
half   _Glossiness;
half   _Metallic;
int    _UseColorMap;
int    _ForceOutlineColor;
half4 _OutlineColor;
half  _OutlineWidth;

// Matrices are always set from C# via SetMatrix - cannot be Properties
float4x4 _ObjectTransform;
float4x4 _ObjectTransformInverse;

#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
StructuredBuffer<float4>    renderInfoBuffer;
StructuredBuffer<float4x4>  transformBuffer;
StructuredBuffer<float4x4>  transformBufferInverse;
int _HasPerInstanceVisibility;
int _HasPerInstanceHilite;
StructuredBuffer<int> perInstanceHiliteBuffer;
#endif

void setup()
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    float4x4 matData        = transformBuffer[unity_InstanceID];
    float4x4 matDataInverse = transformBufferInverse[unity_InstanceID];
    unity_ObjectToWorld = mul(_ObjectTransform, matData);
    unity_WorldToObject = mul(matDataInverse, _ObjectTransformInverse);
#endif
}

float IsNaN_float(float In)
{
    return (In < 0.0 || In > 0.0 || In == 0.0) ? 0 : 1;
}

void rotate2D(inout float2 v, float r)
{
    float s, c;
    sincos(r, s, c);
    v = float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

float ABRRemap(float dataValue, float from0, float to0, float from1, float to1)
{
    return from1 + (dataValue - from0) * (to1 - from1) / (to0 - from0);
}

#endif
