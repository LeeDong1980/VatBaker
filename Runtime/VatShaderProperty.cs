using UnityEngine;

namespace VatBaker
{
    public static class VatShaderProperty
    {
        public static readonly int VatPositionTex = Shader.PropertyToID("_VatPositionTex");
        public static readonly int VatNormalTex = Shader.PropertyToID("_VatNormalTex");
        public static readonly int VatAnimFps = Shader.PropertyToID("_VatAnimFps");
        public static readonly int VatAnimLength = Shader.PropertyToID("_VatAnimLength");

        // Per-instance properties, set from a MaterialPropertyBlock (see VatAnimator).
        public static readonly int AnimationTimeOffset = Shader.PropertyToID("_AnimationTimeOffset");
        public static readonly int VatClip = Shader.PropertyToID("_VatClip");
        public static readonly int VatClipTime = Shader.PropertyToID("_VatClipTime");
        public static readonly int VatClipTimeScale = Shader.PropertyToID("_VatClipTimeScale");
    }
}
