using System.Collections.Generic;
using UnityEngine;

namespace VatBaker
{
    /// <summary>Added to every pool instance. Links it to its <see cref="VatPool"/>.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VatBaker/Vat Pooled Object")]
    public sealed class VatPooledObject : MonoBehaviour
    {
        [Tooltip("Despawn this instance when its VatAnimator finishes a one-shot clip (effects, death animations).")]
        [SerializeField] private bool despawnWhenAnimationFinished;

        private readonly List<IVatPoolListener> _listeners = new();
        private VatAnimator _animator;
        private bool _initialized;

        internal bool isSpawned;
        internal bool destroyedByPool;

        public VatPool Pool { get; internal set; }
        public bool IsSpawned => isSpawned;

        /// <summary>The VatAnimator on this instance, if any.</summary>
        public VatAnimator Animator
        {
            get
            {
                Initialize();
                return _animator;
            }
        }

        public bool DespawnWhenAnimationFinished
        {
            get => despawnWhenAnimationFinished;
            set => despawnWhenAnimationFinished = value;
        }

        /// <summary>Returns this instance to its pool, or destroys it when it was not spawned by a pool.</summary>
        public void Despawn()
        {
            if (Pool != null) Pool.Despawn(gameObject);
            else Destroy(gameObject);
        }

        internal void NotifySpawned()
        {
            Initialize();
            foreach (var listener in _listeners) listener.OnSpawned();
        }

        internal void NotifyDespawned()
        {
            Initialize();
            foreach (var listener in _listeners) listener.OnDespawned();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            GetComponentsInChildren(true, _listeners);
            _animator = GetComponentInChildren<VatAnimator>(true);
            if (_animator != null) _animator.Finished += OnAnimationFinished;
        }

        private void OnAnimationFinished(VatAnimator animator)
        {
            if (despawnWhenAnimationFinished && isSpawned) Despawn();
        }

        private void OnDestroy()
        {
            if (_animator != null) _animator.Finished -= OnAnimationFinished;
            if (!destroyedByPool) Pool?.OnInstanceDestroyed(this);
        }
    }
}
