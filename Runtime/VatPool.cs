using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VatBaker
{
    /// <summary>Receives pool callbacks. Implement it on any component of a pooled prefab.</summary>
    public interface IVatPoolListener
    {
        /// <summary>After the instance is activated and placed.</summary>
        void OnSpawned();

        /// <summary>Before the instance is deactivated and returned to the pool.</summary>
        void OnDespawned();
    }

    /// <summary>
    /// A pool of instances of one prefab. Inactive instances wait under a hidden root; spawning activates one
    /// (its <see cref="VatAnimator"/> then plays its default clip again), despawning deactivates it.
    /// Most code uses the shared pools in <see cref="VatPools"/> instead of creating these directly.
    /// </summary>
    public sealed class VatPool
    {
        private readonly Stack<VatPooledObject> _inactive = new();
        private readonly HashSet<VatPooledObject> _active = new();
        private readonly Transform _root;
        private readonly bool _ownsRoot;
        private int _maxSize;

        public GameObject Prefab { get; }
        public int CountActive => _active.Count;
        public int CountInactive => _inactive.Count;
        public int CountAll => CountActive + CountInactive;

        /// <summary>Instances despawned beyond this many inactive ones are destroyed. 0 means no limit.</summary>
        public int MaxSize
        {
            get => _maxSize;
            set
            {
                _maxSize = Mathf.Max(0, value);
                TrimExcess();
            }
        }

        public VatPool(GameObject prefab, int maxSize = 0, Transform root = null)
        {
            Prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _maxSize = Mathf.Max(0, maxSize);

            if (root == null)
            {
                // Inactive, so instances created by Prewarm don't run Awake / OnEnable until they are spawned.
                var go = new GameObject($"[VatPool] {prefab.name}");
                go.SetActive(false);
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                root = go.transform;
                _ownsRoot = true;
            }

            _root = root;
        }

        /// <summary>Creates inactive instances until <see cref="CountAll"/> reaches <paramref name="count"/>.</summary>
        public void Prewarm(int count)
        {
            while (CountAll < count)
            {
                _inactive.Push(Create());
            }
        }

        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            VatPooledObject obj = null;
            while (obj == null && _inactive.Count > 0)
            {
                obj = _inactive.Pop(); // null when something destroyed an inactive instance
            }

            if (obj == null) obj = Create();

            var go = obj.gameObject;
            go.SetActive(false);
            var t = obj.transform;
            t.SetParent(parent, false);
            if (parent == null && Application.isPlaying)
            {
                // Leave the pool's DontDestroyOnLoad scene, so the instance belongs to the scene it was spawned in.
                SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            }
            t.SetPositionAndRotation(position, rotation);

            obj.isSpawned = true;
            _active.Add(obj);
            go.SetActive(true);
            obj.NotifySpawned();
            return go;
        }

        public GameObject Spawn(Transform parent = null) => Spawn(Vector3.zero, Quaternion.identity, parent);

        public T Spawn<T>(Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
            => Spawn(position, rotation, parent).GetComponent<T>();

        /// <summary>Returns an instance to the pool. Despawning an instance twice or from another pool is ignored with a warning.</summary>
        public void Despawn(GameObject instance)
        {
            if (instance == null) return;

            if (!instance.TryGetComponent<VatPooledObject>(out var obj) || obj.Pool != this)
            {
                Debug.LogWarning($"[VatPool] {instance.name} was not spawned by the pool of {Prefab.name}.", instance);
                return;
            }

            if (!obj.isSpawned)
            {
                Debug.LogWarning($"[VatPool] {instance.name} is already despawned.", instance);
                return;
            }

            obj.NotifyDespawned();
            obj.isSpawned = false;
            _active.Remove(obj);

            if (_maxSize > 0 && _inactive.Count >= _maxSize)
            {
                obj.destroyedByPool = true;
                Object.Destroy(obj.gameObject);
                return;
            }

            obj.gameObject.SetActive(false);
            obj.transform.SetParent(_root, false);
            _inactive.Push(obj);
        }

        /// <summary>Despawns every active instance.</summary>
        public void DespawnAll()
        {
            foreach (var obj in new List<VatPooledObject>(_active))
            {
                if (obj != null) Despawn(obj.gameObject);
            }
        }

        /// <summary>Destroys every instance, active ones included, and the pool root.</summary>
        public void Dispose()
        {
            foreach (var obj in _active) DestroyInstance(obj);
            foreach (var obj in _inactive) DestroyInstance(obj);
            _active.Clear();
            _inactive.Clear();

            if (_ownsRoot && _root != null) DestroyObject(_root.gameObject);
        }

        internal void OnInstanceDestroyed(VatPooledObject obj)
        {
            // Destroyed from outside, for example with the scene it was spawned into.
            _active.Remove(obj);
        }

        private VatPooledObject Create()
        {
            var go = Object.Instantiate(Prefab, _root, false);
            go.name = Prefab.name;
            go.SetActive(false);
            var obj = go.TryGetComponent<VatPooledObject>(out var existing) ? existing : go.AddComponent<VatPooledObject>();
            obj.Pool = this;
            return obj;
        }

        private void TrimExcess()
        {
            while (_maxSize > 0 && _inactive.Count > _maxSize)
            {
                DestroyInstance(_inactive.Pop());
            }
        }

        private static void DestroyInstance(VatPooledObject obj)
        {
            if (obj == null) return;
            obj.destroyedByPool = true;
            DestroyObject(obj.gameObject);
        }

        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
