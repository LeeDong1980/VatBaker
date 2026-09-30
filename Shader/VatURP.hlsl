#ifndef GA_FUQUNA_VATBAKER_VAT_URP_INCLUDED
#define GA_FUQUNA_VATBAKER_VAT_URP_INCLUDED

// URP counterpart of Vat.hlsl.
//
// All VAT properties live in the UnityPerMaterial CBUFFER so the shaders are SRP Batcher compatible.
// Define VAT_PER_MATERIAL_PROPERTIES before including this file to add shader specific properties to that CBUFFER.
// Every pass of a SubShader must use the same definition (define it in HLSLINCLUDE, include this file after the
// pass pragmas) so the CBUFFER layout stays identical.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#ifndef VAT_PER_MATERIAL_PROPERTIES
#define VAT_PER_MATERIAL_PROPERTIES
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _VatPositionTex_TexelSize; // (1.0/width, 1.0/height, width, height)
    float _VatAnimFps;
    float _VatAnimLength;
#if defined(UNITY_INSTANCING_ENABLED)
    float _VatAnimationTimeOffsetPadding; // keeps the layout identical to the non-instanced variant
#else
    float _AnimationTimeOffset;
#endif
    VAT_PER_MATERIAL_PROPERTIES
CBUFFER_END

// With GPU instancing, _AnimationTimeOffset is read per instance (e.g. from a MaterialPropertyBlock).
#if defined(UNITY_INSTANCING_ENABLED)
UNITY_INSTANCING_BUFFER_START(VatProps)
    UNITY_DEFINE_INSTANCED_PROP(float, _AnimationTimeOffset)
UNITY_INSTANCING_BUFFER_END(VatProps)
#define VAT_ANIMATION_TIME_OFFSET UNITY_ACCESS_INSTANCED_PROP(VatProps, _AnimationTimeOffset)
#else
#define VAT_ANIMATION_TIME_OFFSET _AnimationTimeOffset
#endif

TEXTURE2D(_VatPositionTex);
SAMPLER(sampler_VatPositionTex);

TEXTURE2D(_VatNormalTex);
SAMPLER(sampler_VatNormalTex);


float CalcVatAnimationTime(float time)
{
    return (time % _VatAnimLength) * _VatAnimFps;
}

// Animation time of the current instance. Call UNITY_SETUP_INSTANCE_ID() first.
float GetVatAnimationTime(float time)
{
    return CalcVatAnimationTime(time + VAT_ANIMATION_TIME_OFFSET);
}

float2 CalcVatTexCoord(uint vertexId, float animationTime)
{
    float x = vertexId + 0.5;
    float y = animationTime + 0.5;

    return float2(x, y) * _VatPositionTex_TexelSize.xy;
}

float3 GetVatPosition(uint vertexId, float animationTime)
{
    return SAMPLE_TEXTURE2D_LOD(_VatPositionTex, sampler_VatPositionTex, CalcVatTexCoord(vertexId, animationTime), 0).xyz;
}

float3 GetVatNormal(uint vertexId, float animationTime)
{
    return SafeNormalize(SAMPLE_TEXTURE2D_LOD(_VatNormalTex, sampler_VatNormalTex, CalcVatTexCoord(vertexId, animationTime), 0).xyz);
}

// VAT has no tangents, so re-orthogonalize the mesh tangent against the animated normal.
float4 GetVatTangent(float3 vatNormalOS, float4 tangentOS)
{
    float3 tangent = tangentOS.xyz - vatNormalOS * dot(vatNormalOS, tangentOS.xyz);
    return float4(SafeNormalize(tangent), tangentOS.w);
}

#endif
