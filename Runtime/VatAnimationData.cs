using System.Collections.Generic;
using UnityEngine;

namespace VatBaker
{
    /// <summary>
    /// The clips baked into a VAT atlas. Every renderer baked from the same model shares this layout,
    /// so one <see cref="VatAnimator"/> drives all of them with the same per-instance values.
    /// </summary>
    [CreateAssetMenu(menuName = "VatBaker/Vat Animation Data", fileName = "VatAnimationData")]
    public class VatAnimationData : ScriptableObject
    {
        [SerializeField] private VatClip[] clips = System.Array.Empty<VatClip>();
        [Tooltip("Object space bounds over every baked frame. Used for the baked meshes so culling never clips the animation.")]
        [SerializeField] private Bounds bounds;

        private Dictionary<string, int> _indexByName;

        public IReadOnlyList<VatClip> Clips => clips;
        public int ClipCount => clips.Length;
        public Bounds Bounds => bounds;

        public VatClip this[int index] => clips[index];

        /// <summary>Index of the clip with this name, or -1.</summary>
        public int IndexOf(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return -1;

            if (_indexByName == null)
            {
                _indexByName = new Dictionary<string, int>(clips.Length);
                for (var i = 0; i < clips.Length; i++)
                {
                    _indexByName.TryAdd(clips[i].name, i);
                }
            }

            return _indexByName.TryGetValue(clipName, out var index) ? index : -1;
        }

        public bool TryGetClip(string clipName, out VatClip clip)
        {
            var index = IndexOf(clipName);
            clip = index >= 0 ? clips[index] : default;
            return index >= 0;
        }

        public void SetClips(VatClip[] newClips, Bounds newBounds)
        {
            clips = newClips ?? System.Array.Empty<VatClip>();
            bounds = newBounds;
            _indexByName = null;
        }

        private void OnValidate() => _indexByName = null;
    }
}
