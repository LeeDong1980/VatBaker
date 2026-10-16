using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace VatBaker
{
    public enum VatLoopMode
    {
        /// <summary>Use <see cref="VatClip.loop"/> from the baked data.</summary>
        ClipDefault,
        Loop,
        Once,
    }

    public enum VatTimeSource
    {
        /// <summary>
        /// The shader advances the clip time from _Time (_VatClipTimeScale), so a playing animator costs nothing per frame.
        /// The MaterialPropertyBlock is only written on Play / speed / time changes. Uses scaled time.
        /// Needs a shader that reads _VatClipTimeScale (VatSurfaceStandard, VatUnlit of this package).
        /// </summary>
        Shader,

        /// <summary>
        /// The script writes _VatClipTime every frame. Works with any shader that reads _VatClip / _VatClipTime
        /// and supports unscaled time, at the cost of one MaterialPropertyBlock write per renderer per frame.
        /// </summary>
        Script,
    }

    /// <summary>
    /// Plays the clips of a VAT atlas on one model instance through a MaterialPropertyBlock, so instances keep
    /// sharing their material and stay GPU instanced. Playback state lives on the CPU: <see cref="ClipTime"/>,
    /// <see cref="Speed"/> and <see cref="Finished"/> work the same for both <see cref="VatTimeSource"/>s.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VatBaker/Vat Animator")]
    public class VatAnimator : MonoBehaviour
    {
        [Tooltip("Clip layout of the atlas. Leave empty for a material baked with the original per-clip baker: its single clip is used.")]
        [SerializeField] private VatAnimationData animationData;
        [Tooltip("Renderers using VAT materials of this model. Filled from the children when empty.")]
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private string defaultClip;
        [SerializeField] private bool playOnEnable = true;
        [Tooltip("Start the default clip at a random time, so crowds don't move in sync.")]
        [SerializeField] private bool randomStartTime;
        [SerializeField] private float speed = 1f;
        [SerializeField] private VatTimeSource timeSource = VatTimeSource.Shader;
        [Tooltip("Script time source only.")]
        [SerializeField] private bool useUnscaledTime;

        /// <summary>Raised once when a non-looping clip reaches its end (or its start, when playing backwards).</summary>
        public event Action<VatAnimator> Finished;

        private static MaterialPropertyBlock _block;

        private VatClip _clip;
        private int _clipIndex = -1;
        private bool _loop;
        private bool _playing;
        private bool _finished;
        private int _nextClipIndex = -1;

        // The clip time is _time at clock _stamp, advancing at speed since then.
        private float _time;
        private double _stamp;

        internal int updaterIndex = -1;

        #region Public API

        public VatAnimationData AnimationData
        {
            get => animationData;
            set
            {
                animationData = value;
                _clipIndex = -1;
                _playing = false;
                UpdateRegistration();
            }
        }

        public Renderer[] Renderers
        {
            get => renderers;
            set
            {
                renderers = value ?? Array.Empty<Renderer>();
                Apply();
            }
        }

        public VatTimeSource TimeSource
        {
            get => timeSource;
            set
            {
                if (timeSource == value) return;
                Rebase();
                timeSource = value;
                _stamp = Clock();
                Apply();
                UpdateRegistration();
            }
        }

        public bool UseUnscaledTime
        {
            get => useUnscaledTime;
            set
            {
                if (useUnscaledTime == value) return;
                var time = ClipTime;
                useUnscaledTime = value;
                ClipTime = time;
            }
        }

        public string DefaultClip
        {
            get => defaultClip;
            set => defaultClip = value;
        }

        public int ClipCount => animationData != null ? animationData.ClipCount : (FallbackClip().IsValid ? 1 : 0);

        public VatClip CurrentClip => _clip;
        public int CurrentClipIndex => _clipIndex;
        public string CurrentClipName => _clipIndex >= 0 ? _clip.name : null;

        /// <summary>True while the clip time advances: not paused and not a finished one-shot.</summary>
        public bool IsPlaying => _playing;

        /// <summary>True when the current clip is a one-shot that reached its end.</summary>
        public bool IsFinished => _finished;

        public bool IsLooping => _loop;

        /// <summary>Clip length in seconds.</summary>
        public float Length => _clip.Length;

        /// <summary>Playback speed multiplier. Negative values play backwards.</summary>
        public float Speed
        {
            get => speed;
            set
            {
                if (Mathf.Approximately(speed, value)) return;
                Rebase();
                speed = value;
                Apply();
            }
        }

        /// <summary>Time in seconds into the current clip, wrapped for loops and clamped for one-shots.</summary>
        public float ClipTime
        {
            get => WrapTime(RawTime());
            set
            {
                _time = WrapTime(value);
                _stamp = Clock();
                if (_finished && !IsAtEnd(_time)) _finished = false;
                Apply();
            }
        }

        /// <summary><see cref="ClipTime"/> divided by the clip length, 0 to 1.</summary>
        public float NormalizedTime
        {
            get => Length > 0f ? ClipTime / Length : 0f;
            set => ClipTime = value * Length;
        }

        public bool HasClip(string clipName) => IndexOf(clipName) >= 0;

        public int IndexOf(string clipName)
        {
            if (animationData != null) return animationData.IndexOf(clipName);
            return FallbackClip().IsValid && (string.IsNullOrEmpty(clipName) || clipName == FallbackClip().name) ? 0 : -1;
        }

        public VatClip GetClip(int index) => animationData != null ? animationData[index] : FallbackClip();

        /// <summary>Plays a clip by name from <paramref name="startTime"/> seconds. Returns false when there is no such clip.</summary>
        public bool Play(string clipName, float startTime = 0f, VatLoopMode loopMode = VatLoopMode.ClipDefault)
        {
            var index = IndexOf(clipName);
            if (index < 0)
            {
                Debug.LogWarning($"[VatAnimator] Clip \"{clipName}\" not found on {name}.", this);
                return false;
            }

            Play(index, startTime, loopMode);
            return true;
        }

        /// <summary>Plays a clip by index from <paramref name="startTime"/> seconds.</summary>
        public void Play(int clipIndex, float startTime = 0f, VatLoopMode loopMode = VatLoopMode.ClipDefault)
        {
            if (clipIndex < 0 || clipIndex >= ClipCount)
            {
                throw new ArgumentOutOfRangeException(nameof(clipIndex), clipIndex, $"{name} has {ClipCount} clips.");
            }

            _clipIndex = clipIndex;
            _clip = GetClip(clipIndex);
            _loop = loopMode switch
            {
                VatLoopMode.Loop => true,
                VatLoopMode.Once => false,
                _ => _clip.loop,
            };
            _nextClipIndex = -1;
            _playing = true;
            _finished = false;
            _time = WrapTime(startTime);
            _stamp = Clock();

            Apply();
            UpdateRegistration();
        }

        /// <summary>Plays a clip at a normalized (0 to 1) start time.</summary>
        public bool PlayNormalized(string clipName, float normalizedStartTime, VatLoopMode loopMode = VatLoopMode.ClipDefault)
        {
            var index = IndexOf(clipName);
            if (index < 0) return Play(clipName);
            Play(index, normalizedStartTime * GetClip(index).Length, loopMode);
            return true;
        }

        /// <summary>Plays a clip once, then plays <paramref name="nextClipName"/> (for example an attack, then back to idle).</summary>
        public bool PlayOnce(string clipName, string nextClipName = null)
        {
            if (!Play(clipName, 0f, VatLoopMode.Once)) return false;
            _nextClipIndex = string.IsNullOrEmpty(nextClipName) ? -1 : IndexOf(nextClipName);
            return true;
        }

        /// <summary>Plays the default clip, or the first one when none is set.</summary>
        public void PlayDefault(bool randomizeStartTime = false)
        {
            if (ClipCount == 0) return;

            var index = Mathf.Max(IndexOf(defaultClip), 0);
            var startTime = randomizeStartTime ? Random.value * GetClip(index).Length : 0f;
            Play(index, startTime);
        }

        /// <summary>Holds the current pose.</summary>
        public void Pause()
        {
            if (!_playing) return;
            Rebase();
            _playing = false;
            Apply();
            UpdateRegistration();
        }

        /// <summary>Continues after <see cref="Pause"/>. A finished one-shot stays finished; call Play to restart it.</summary>
        public void Resume()
        {
            if (_playing || _finished || _clipIndex < 0) return;
            _stamp = Clock();
            _playing = true;
            Apply();
            UpdateRegistration();
        }

        /// <summary>Stops and clears the clip, so the renderers play the material's own clip on _Time again.</summary>
        public void ClearClip()
        {
            _clipIndex = -1;
            _clip = default;
            _playing = false;
            _finished = false;
            _nextClipIndex = -1;
            _time = 0f;
            Apply();
            UpdateRegistration();
        }

        /// <summary>Writes the current state to the renderers' MaterialPropertyBlocks.</summary>
        public void Apply()
        {
            if (renderers == null || renderers.Length == 0) return;

            _block ??= new MaterialPropertyBlock();

            Vector4 clipVector;
            float clipTime;
            float timeScale;

            if (_clipIndex < 0)
            {
                // No clip: the material's own single clip plays on _Time.
                clipVector = Vector4.zero;
                clipTime = 0f;
                timeScale = 0f;
            }
            else
            {
                clipVector = _clip.ToShaderVector(_loop);
                if (_playing && timeSource == VatTimeSource.Shader && Application.isPlaying)
                {
                    // The shader computes clipTime + _Time.y * timeScale, and _Time.y is Time.timeSinceLevelLoad.
                    timeScale = speed;
                    clipTime = WrapTime(RawTime()) - Time.timeSinceLevelLoad * speed;
                }
                else
                {
                    timeScale = 0f;
                    clipTime = WrapTime(RawTime());
                }
            }

            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetVector(VatShaderProperty.VatClip, clipVector);
                _block.SetFloat(VatShaderProperty.VatClipTime, clipTime);
                _block.SetFloat(VatShaderProperty.VatClipTimeScale, timeScale);
                r.SetPropertyBlock(_block);
            }
        }

        /// <summary>Fills <see cref="Renderers"/> with the MeshRenderers under this object.</summary>
        public void CollectRenderers()
        {
            renderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        #endregion

        #region Unity

        private void Awake()
        {
            if (renderers == null || renderers.Length == 0)
            {
                CollectRenderers();
            }
        }

        private void OnEnable()
        {
            VatAnimatorUpdater.AddActive(this);

            if (playOnEnable && Application.isPlaying)
            {
                PlayDefault(randomStartTime);
            }
            else
            {
                _stamp = Clock();
                Apply();
                UpdateRegistration();
            }
        }

        private void OnDisable()
        {
            if (_playing) Rebase();
            VatAnimatorUpdater.RemoveActive(this);
            VatAnimatorUpdater.Unregister(this);
        }

        private void Reset()
        {
            CollectRenderers();
        }

        #endregion

        #region Internal

        /// <summary>Called by <see cref="VatAnimatorUpdater"/> every frame while registered.</summary>
        internal void Tick()
        {
            if (!_playing) return;

            var raw = RawTime();
            if (!_loop && IsAtEnd(raw))
            {
                Finish();
                return;
            }

            if (timeSource == VatTimeSource.Script)
            {
                if (_loop)
                {
                    // Keep the stored time small, so float precision doesn't degrade over long sessions.
                    _time = WrapTime(raw);
                    _stamp = Clock();
                }

                Apply();
            }
        }

        /// <summary>Called when Time.timeSinceLevelLoad jumped (a scene load), so shader driven clips need a new base time.</summary>
        internal void OnShaderClockReset()
        {
            if (_playing && timeSource == VatTimeSource.Shader) Apply();
        }

        private void Finish()
        {
            _time = speed >= 0f ? _clip.Length : 0f;
            _stamp = Clock();
            _playing = false;
            _finished = true;
            Apply();
            UpdateRegistration();

            var next = _nextClipIndex;
            _nextClipIndex = -1;

            Finished?.Invoke(this);

            // Only chain when no Finished handler started another clip.
            if (next >= 0 && _finished && isActiveAndEnabled)
            {
                Play(next);
            }
        }

        private bool IsAtEnd(float rawTime) => speed >= 0f ? rawTime >= _clip.Length : rawTime <= 0f;

        private bool NeedsTick => _playing && (timeSource == VatTimeSource.Script || !_loop);

        private void UpdateRegistration()
        {
            if (isActiveAndEnabled && Application.isPlaying && NeedsTick)
            {
                VatAnimatorUpdater.Register(this);
            }
            else
            {
                VatAnimatorUpdater.Unregister(this);
            }
        }

        private double Clock()
        {
            if (!Application.isPlaying) return 0d;
            return timeSource == VatTimeSource.Script && useUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        }

        private float RawTime()
        {
            return _playing ? _time + (float)(Clock() - _stamp) * speed : _time;
        }

        private void Rebase()
        {
            _time = WrapTime(RawTime());
            _stamp = Clock();
        }

        private float WrapTime(float time)
        {
            var length = _clip.Length;
            if (length <= 0f) return 0f;
            return _loop ? Mathf.Repeat(time, length) : Mathf.Clamp(time, 0f, length);
        }

        private VatClip FallbackClip()
        {
            foreach (var r in renderers ?? Array.Empty<Renderer>())
            {
                if (r != null && r.sharedMaterial != null)
                {
                    return VatClip.FromMaterial(r.sharedMaterial);
                }
            }

            return default;
        }

        #endregion
    }
}
