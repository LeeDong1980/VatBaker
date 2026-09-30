#ifndef GA_FUQUNA_VATBAKER_VAT_URP_PASSES_INCLUDED
#define GA_FUQUNA_VATBAKER_VAT_URP_PASSES_INCLUDED

// Utility passes shared by the URP VAT shaders: ShadowCaster, DepthOnly, DepthNormals and MotionVectors.
// Define VAT_PER_MATERIAL_PROPERTIES (see VatURP.hlsl) before including this file.

#include "VatURP.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct VatPassAttributes
{
    uint vertexId : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VatPassVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void VatPassLODFadeCrossFade(float4 positionCS)
{
    #if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(positionCS);
    #endif
}


///////////////////////////////////////////////////////////////////////////////
// ShadowCaster
///////////////////////////////////////////////////////////////////////////////

// Set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer
float3 _LightDirection;
float3 _LightPosition;

VatPassVaryings VatShadowPassVertex(VatPassAttributes input)
{
    VatPassVaryings output = (VatPassVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float animationTime = GetVatAnimationTime(_Time.y);
    float3 positionWS = TransformObjectToWorld(GetVatPosition(input.vertexId, animationTime));
    float3 normalWS = TransformObjectToWorldNormal(GetVatNormal(input.vertexId, animationTime));

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    output.positionCS = ApplyShadowClamping(positionCS);
    return output;
}

half4 VatShadowPassFragment(VatPassVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    VatPassLODFadeCrossFade(input.positionCS);
    return 0;
}


///////////////////////////////////////////////////////////////////////////////
// DepthOnly
///////////////////////////////////////////////////////////////////////////////

VatPassVaryings VatDepthOnlyVertex(VatPassAttributes input)
{
    VatPassVaryings output = (VatPassVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float animationTime = GetVatAnimationTime(_Time.y);
    output.positionCS = TransformObjectToHClip(GetVatPosition(input.vertexId, animationTime));
    return output;
}

half VatDepthOnlyFragment(VatPassVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    VatPassLODFadeCrossFade(input.positionCS);
    return input.positionCS.z;
}


///////////////////////////////////////////////////////////////////////////////
// DepthNormals
///////////////////////////////////////////////////////////////////////////////

struct VatDepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

VatDepthNormalsVaryings VatDepthNormalsVertex(VatPassAttributes input)
{
    VatDepthNormalsVaryings output = (VatDepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float animationTime = GetVatAnimationTime(_Time.y);
    output.positionCS = TransformObjectToHClip(GetVatPosition(input.vertexId, animationTime));
    output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(GetVatNormal(input.vertexId, animationTime)));
    return output;
}

void VatDepthNormalsFragment(
    VatDepthNormalsVaryings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    VatPassLODFadeCrossFade(input.positionCS);

#if defined(_GBUFFER_NORMALS_OCT)
    float3 normalWS = normalize(input.normalWS);
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms.
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]
    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);      // values between [ 0,  1]
    outNormalWS = half4(packedNormalWS, 0.0);
#else
    float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
    outNormalWS = half4(normalWS, 0.0);
#endif

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}


///////////////////////////////////////////////////////////////////////////////
// MotionVectors
// VAT moves vertices on the GPU, so the object always writes its own motion vectors
// from the VAT position of this frame and the previous frame. Needed for TAA / motion blur.
///////////////////////////////////////////////////////////////////////////////

struct VatMotionVectorsVaryings
{
    float4 positionCS : SV_POSITION;
    float4 positionCSNoJitter : TEXCOORD0;
    float4 previousPositionCSNoJitter : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

VatMotionVectorsVaryings VatMotionVectorsVertex(VatPassAttributes input)
{
    VatMotionVectorsVaryings output = (VatMotionVectorsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 positionOS = float4(GetVatPosition(input.vertexId, GetVatAnimationTime(_Time.y)), 1.0);
    float4 previousPositionOS = float4(GetVatPosition(input.vertexId, GetVatAnimationTime(_Time.y - unity_DeltaTime.x)), 1.0);

    output.positionCS = TransformObjectToHClip(positionOS.xyz);
    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, positionOS));
    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, previousPositionOS));
    return output;
}

float4 VatMotionVectorsFragment(VatMotionVectorsVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    VatPassLODFadeCrossFade(input.positionCS);

    return float4(CalcNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 0, 0);
}

#endif
