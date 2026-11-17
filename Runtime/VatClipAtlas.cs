using System;
using UnityEngine;

namespace VatBaker
{
    // All clips of one mesh stacked in a single position/normal texture pair, one row per frame.
    public class VatClipAtlas : ScriptableObject
    {
        [field: SerializeField] public Texture2D PositionTexture { get; private set; }
        [field: SerializeField] public Texture2D NormalTexture { get; private set; }
        [field: SerializeField] public int VertexCount { get; private set; }
        [SerializeField] private VatClip[] _clips;

        public ReadOnlySpan<VatClip> Clips => _clips;

        public void Initialize(Texture2D positionTexture, Texture2D normalTexture, int vertexCount, VatClip[] clips)
        {
            PositionTexture = positionTexture;
            NormalTexture = normalTexture;
            VertexCount = vertexCount;
            _clips = clips;
        }

        public int IndexOf(string clipName)
        {
            for (var i = 0; i < _clips.Length; i++)
            {
                if (_clips[i].name == clipName) return i;
            }
            return -1;
        }
    }
}
