using System;
using UnityEngine;

namespace VatBaker
{
    [Serializable]
    public struct VatClip
    {
        public string name;
        public int startRow;
        public int frameCount;
        public float fps;
        public bool loop;

        public float Length => frameCount > 1 && fps > 0f ? (frameCount - 1) / fps : 0f;

        // Layout of the per-instance _VatClip vector read by VatURP.hlsl: (startRow, frameCount, fps, loop).
        public Vector4 ToShaderVector() => new(startRow, frameCount, fps, loop ? 1f : 0f);
    }
}
