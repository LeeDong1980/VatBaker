using System.Collections.Generic;
using UnityEngine;

namespace VatBaker
{
    /// <summary>
    /// Shared pools, one per prefab, created on first use.
    /// <code>
    /// var go = VatPools.Spawn(zombiePrefab, position, rotation);
    /// go.GetComponent&lt;VatAnimator&gt;().Play("Walk");
    /// ...
    /// VatPools.Despawn(go);
    /// </code>
    /// </summary>
    public static class VatPools
    {
        private static readonly Dictionary<GameObject, VatPool> Pools = new();

        public static IReadOnlyCollection<VatPool> All => Pools.Values;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Pools.Clear();

        /// <summary>The pool of <paramref name="prefab"/>, created when it doesn't exist yet.</summary>
        public static VatPool Get(GameObject prefab)
        {
            if (!Pools.TryGetValue(prefab, out var pool))
            {
                pool = new VatPool(prefab);
                Pools.Add(prefab, pool);
            }

            return pool;
        }

        public static bool TryGet(GameObject prefab, out VatPool pool) => Pools.TryGetValue(prefab, out pool);

        public static void Prewarm(GameObject prefab, int count, int maxSize = 0)
        {
            var pool = Get(prefab);
            if (maxSize > 0) pool.MaxSize = maxSize;
            pool.Prewarm(count);
        }

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
            => Get(prefab).Spawn(position, rotation, parent);

        public static GameObject Spawn(GameObject prefab, Transform parent = null)
            => Get(prefab).Spawn(parent);

        /// <summary>Spawns and returns the instance's VatAnimator (null when the prefab has none).</summary>
        public static VatAnimator SpawnAnimator(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
            => Spawn(prefab, position, rotation, parent).GetComponentInChildren<VatAnimator>();

        /// <summary>Returns an instance to its pool. Objects that didn't come from a pool are destroyed.</summary>
        public static void Despawn(GameObject instance)
        {
            if (instance == null) return;

            if (instance.TryGetComponent<VatPooledObject>(out var obj))
            {
                obj.Despawn();
            }
            else
            {
                Object.Destroy(instance);
            }
        }

        /// <summary>Destroys the pool of <paramref name="prefab"/> and all its instances.</summary>
        public static void Dispose(GameObject prefab)
        {
            if (Pools.Remove(prefab, out var pool)) pool.Dispose();
        }

        public static void DisposeAll()
        {
            foreach (var pool in Pools.Values) pool.Dispose();
            Pools.Clear();
        }
    }
}
