using UnityEngine;

namespace VatBaker
{
    // Plays one clip of a VatClipAtlas on this renderer, for checking an atlas bake in Play Mode.
    [RequireComponent(typeof(MeshRenderer))]
    public class VatClipPreview : MonoBehaviour
    {
        [SerializeField] private VatClipAtlas _atlas;
        [SerializeField] private int _clipIndex;
        [SerializeField] private float _speed = 1f;

        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _block;
        private float _time;

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
        }

        // Restart when the clip changes in the Inspector, so one-shot clips such as death play again.
        private void OnValidate() => _time = 0f;

        private void Update()
        {
            if (_atlas == null || (uint)_clipIndex >= (uint)_atlas.Clips.Length) return;

            _time += Time.deltaTime * _speed;
            _block.SetVector(VatShaderProperty.VatClip, _atlas.Clips[_clipIndex].ToShaderVector());
            _block.SetFloat(VatShaderProperty.VatClipTime, _time);
            _meshRenderer.SetPropertyBlock(_block);
        }
    }
}
