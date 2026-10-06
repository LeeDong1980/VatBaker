using UnityEngine;

namespace VatBaker
{
    public static class VatShaderProperty
    {
        public static readonly int VatPositionTex = Shader.PropertyToID("_VatPositionTex");
        public static readonly int VatNormalTex = Shader.PropertyToID("_VatNormalTex");
        public static readonly int VatAnimFps = Shader.PropertyToID("_VatAnimFps");
        public static readonly int VatAnimLength = Shader.PropertyToID("_VatAnimLength");
        public static readonly int VatClip = Shader.PropertyToID("_VatClip");
        public static readonly int VatClipTime = Shader.PropertyToID("_VatClipTime");
    }
}
