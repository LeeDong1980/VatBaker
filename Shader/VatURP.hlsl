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

// Per-instance playback, set from a MaterialPropertyBlock (not material properties):
//   _VatClip = (startRow, frameCount, fps, loop) selects a clip in a multi-clip atlas,
//   _VatClipTime is the time in seconds into that clip.
// A zero frameCount means no clip was set, so the material's single clip plays on _Time + _AnimationTimeOffset.
#if defined(UNITY_INSTANCING_ENABLED)
UNITY_INSTANCING_BUFFER_START(VatInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _VatClip)
    UNITY_DEFINE_INSTANCED_PROP(float, _VatClipTime)
    UNITY_DEFINE_INSTANCED_PROP(float, _AnimationTimeOffset)
UNITY_INSTANCING_BUFFER_END(VatInstance)
#define VAT_INSTANCE(name) UNITY_ACCESS_INSTANCED_PROP(VatInstance, name)
#else
float4 _VatClip;
float _VatClipTime;
#define VAT_INSTANCE(name) name
#endif

TEXTURE2D(_VatPositionTex);
TEXTURE2D(_VatNormalTex);
SAMPLER(vat_linear_clamp_sampler); // Inline sampler: ignores the VAT textures' import wrap/filter settings.


// Texture coordinate of vertexId at timeOffset seconds from now (0 for this frame, -unity_DeltaTime.x for the previous one).
// Call UNITY_SETUP_INSTANCE_ID() first.
float2 CalcVatTexCoord(uint vertexId, float timeOffset)
{
    float4 clip = VAT_INSTANCE(_VatClip);
    float time = VAT_INSTANCE(_VatClipTime);
    if (clip.y < 1.0)
    {
        // The baker writes floor(length * fps) + 1 rows; the last row repeats the first for looping.
        clip = float4(0.0, floor(_VatAnimLength * _VatAnimFps + 1e-3) + 1.0, _VatAnimFps, 1.0);
        time = _Time.y + VAT_INSTANCE(_AnimationTimeOffset);
    }
    time += timeOffset;

    float clipLength = max((clip.y - 1.0) / max(clip.z, 1e-5), 1e-5);
    // Positive modulo, so negative offsets still wrap; one-shot clips hold their last frame.
    time = clip.w > 0.5 ? time - floor(time / clipLength) * clipLength : clamp(time, 0.0, clipLength);
    float frame = time * clip.z;

    uint width = (uint)_VatPositionTex_TexelSize.z;
    uint block = vertexId / width; // Non-zero only for legacy multi-block bakes; an atlas is one block wide.
    return float2(float(vertexId - block * width) + 0.5, block * clip.y + clip.x + frame + 0.5) * _VatPositionTex_TexelSize.xy;
}

float3 GetVatPosition(float2 vatUV)
{
    return SAMPLE_TEXTURE2D_LOD(_VatPositionTex, vat_linear_clamp_sampler, vatUV, 0).xyz;
}

float3 GetVatNormal(float2 vatUV)
{
    return SafeNormalize(SAMPLE_TEXTURE2D_LOD(_VatNormalTex, vat_linear_clamp_sampler, vatUV, 0).xyz);
}

// VAT has no tangents, so re-orthogonalize the mesh tangent against the animated normal.
float4 GetVatTangent(float3 vatNormalOS, float4 tangentOS)
{
    float3 tangent = tangentOS.xyz - vatNormalOS * dot(vatNormalOS, tangentOS.xyz);
    return float4(SafeNormalize(tangent), tangentOS.w);
}

#endif
