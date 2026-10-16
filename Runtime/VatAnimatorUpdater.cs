using System.Collections.Generic;
using UnityEngine;

namespace VatBaker
{
    /// <summary>
    /// Ticks every <see cref="VatAnimator"/> that needs a per-frame update from one Update loop, instead of one
    /// Update call per animator. Looping clips on the Shader time source are never ticked.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(1000)] // after gameplay scripts that call Play / set Speed in their Update
    internal sealed class VatAnimatorUpdater : MonoBehaviour
    {
        private static VatAnimatorUpdater _instance;
        private static readonly List<VatAnimator> Ticking = new();
        private static readonly HashSet<VatAnimator> Active = new();
        private static readonly List<VatAnimator> ResetBuffer = new();
        private static double _levelLoadClockOffset = double.NaN;

        public static int TickingCount => Ticking.Count;
        public static int ActiveCount => Active.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Needed when domain reload is disabled in Enter Play Mode settings.
            _instance = null;
            Ticking.Clear();
            Active.Clear();
            _levelLoadClockOffset = double.NaN;
        }

        public static void Register(VatAnimator animator)
        {
            if (animator.updaterIndex >= 0) return;
            EnsureInstance();
            animator.updaterIndex = Ticking.Count;
            Ticking.Add(animator);
        }

        public static void Unregister(VatAnimator animator)
        {
            var index = animator.updaterIndex;
            if (index < 0) return;

            var lastIndex = Ticking.Count - 1;
            var last = Ticking[lastIndex];
            Ticking[index] = last;
            last.updaterIndex = index;
            Ticking.RemoveAt(lastIndex);
            animator.updaterIndex = -1;
        }

        public static void AddActive(VatAnimator animator)
        {
            if (!Application.isPlaying) return;
            EnsureInstance();
            Active.Add(animator);
        }

        public static void RemoveActive(VatAnimator animator) => Active.Remove(animator);

        private static void EnsureInstance()
        {
            if (_instance != null || !Application.isPlaying) return;

            var go = new GameObject(nameof(VatAnimatorUpdater)) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<VatAnimatorUpdater>();
        }

        private void Update()
        {
            // Backwards, so animators unregistering themselves (finished one-shots) or others don't skip entries.
            for (var i = Ticking.Count - 1; i >= 0; i--)
            {
                if (i >= Ticking.Count) continue;

                var animator = Ticking[i];
                if (animator == null)
                {
                    // Destroyed without OnDisable (should not happen): swap-remove keeping the indices valid.
                    var last = Ticking[^1];
                    Ticking[i] = last;
                    if (last != null) last.updaterIndex = i;
                    Ticking.RemoveAt(Ticking.Count - 1);
                    continue;
                }

                animator.Tick();
            }
        }

        private void LateUpdate()
        {
            // Shader driven clips are based on _Time.y (Time.timeSinceLevelLoad), which restarts when a scene loads.
            var offset = Time.timeAsDouble - Time.timeSinceLevelLoadAsDouble;
            if (!double.IsNaN(_levelLoadClockOffset) && System.Math.Abs(offset - _levelLoadClockOffset) > 1e-4)
            {
                ResetBuffer.AddRange(Active);
                foreach (var animator in ResetBuffer)
                {
                    if (animator != null) animator.OnShaderClockReset();
                }
                ResetBuffer.Clear();
            }

            _levelLoadClockOffset = offset;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
