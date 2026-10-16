using System;
using UnityEngine;

namespace VatBaker
{
    /// <summary>
    /// One clip of a VAT texture. A multi-clip atlas stacks clips vertically: each clip owns
    /// <see cref="frameCount"/> rows starting at <see cref="startRow"/>.
    /// </summary>
    [Serializable]
    public struct VatClip
    {
        public string name;
        public int startRow;
        public int frameCount;
        public float fps;
        public bool loop;

        public VatClip(string name, int startRow, int frameCount, float fps, bool loop)
        {
            this.name = name;
            this.startRow = startRow;
            this.frameCount = frameCount;
            this.fps = fps;
            this.loop = loop;
        }

        /// <summary>Length in seconds. The baker writes floor(length * fps) + 1 rows, so the last row is the end pose.</summary>
        public float Length => Mathf.Max(frameCount - 1, 0) / Mathf.Max(fps, 1e-5f);

        public bool IsValid => frameCount > 0 && fps > 0f;

        /// <summary>The _VatClip shader value: (startRow, frameCount, fps, loop).</summary>
        public Vector4 ToShaderVector() => ToShaderVector(loop);

        public Vector4 ToShaderVector(bool loopOverride) => new(startRow, frameCount, fps, loopOverride ? 1f : 0f);

        /// <summary>
        /// The single clip of a material made by the original per-clip baker (_VatAnimFps / _VatAnimLength, starting at row 0).
        /// </summary>
        public static VatClip FromMaterial(Material material, bool loop = true)
        {
            var fps = material.HasProperty(VatShaderProperty.VatAnimFps) ? material.GetFloat(VatShaderProperty.VatAnimFps) : 0f;
            var length = material.HasProperty(VatShaderProperty.VatAnimLength) ? material.GetFloat(VatShaderProperty.VatAnimLength) : 0f;
            var frameCount = fps > 0f ? Mathf.FloorToInt(length * fps + 1e-3f) + 1 : 0;
            return new VatClip(material.name, 0, frameCount, fps, loop);
        }

        public override string ToString() => $"{name} (rows {startRow}-{startRow + frameCount - 1}, {fps}fps, {Length:0.###}s, {(loop ? "loop" : "once")})";
    }
}
