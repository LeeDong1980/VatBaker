#ifndef GA_FUQUNA_VATBAKER_VAT_INCLUDED
#define GA_FUQUNA_VATBAKER_VAT_INCLUDED

sampler2D _VatPositionTex;
float4 _VatPositionTex_TexelSize; // (1.0/width, 1.0/height, width, height) // https://docs.unity3d.com/Manual/SL-PropertiesInPrograms.html

sampler2D _VatNormalTex;
float4 _VatNormalTex_TexelSize; // (1.0/width, 1.0/height, width, height) // https://docs.unity3d.com/Manual/SL-PropertiesInPrograms.html

float _VatAnimFps;
float _VatAnimLength;


inline float CalcVatAnimationTime(float time)
{
    return (time  % _VatAnimLength) * _VatAnimFps;
}

// Per-instance clip playback, mirroring CalcVatTexCoord in VatURP.hlsl (see the comment there):
// clip = _VatClip (startRow, frameCount, fps, loop), clipTime = _VatClipTime, timeScale = _VatClipTimeScale.
// legacyTime is the time used when no clip is set (frameCount 0): the material's single clip plays as before.
// Returns the animation time in rows for CalcVatTexCoord / GetVatPosition / GetVatNormal.
inline float CalcVatClipAnimationTime(float4 clip, float clipTime, float timeScale, float legacyTime)
{
    if (clip.y < 1.0)
    {
        return CalcVatAnimationTime(legacyTime);
    }

    float time = clipTime + _Time.y * timeScale;
    float clipLength = max((clip.y - 1.0) / max(clip.z, 1e-5), 1e-5);
    // Positive modulo, so negative offsets still wrap; one-shot clips hold their last frame.
    time = clip.w > 0.5 ? time - floor(time / clipLength) * clipLength : clamp(time, 0.0, clipLength);
    return clip.x + time * clip.z;
}

inline float4 CalcVatTexCoord(uint vertexId, float animationTime)
{
    float x = vertexId + 0.5;
    float y = animationTime + 0.5;
    
    return float4(x, y, 0, 0) * _VatPositionTex_TexelSize;   
}

inline float3 GetVatPosition(uint vertexId, float animationTime)
{
    return (float3)tex2Dlod(_VatPositionTex, CalcVatTexCoord(vertexId, animationTime));
}

inline float3 GetVatNormal(uint vertexId, float animationTime)
{
    return (float3)tex2Dlod(_VatNormalTex, CalcVatTexCoord(vertexId, animationTime));
}

#endif