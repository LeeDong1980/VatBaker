using System;
using System.Collections.Generic;
using UnityEngine;

namespace VatBaker
{
    /// <summary>Prewarms <see cref="VatPools"/> from the scene, so spawning during gameplay doesn't instantiate.</summary>
    [AddComponentMenu("VatBaker/Vat Pool Manager")]
    [DefaultExecutionOrder(-1000)]
    public class VatPoolManager : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public GameObject prefab;
            [Min(0)] public int prewarmCount;
            [Tooltip("Inactive instances kept at most. 0 means no limit.")]
            [Min(0)] public int maxSize;
        }

        [SerializeField] private List<Entry> entries = new();
        [Tooltip("Destroy these pools and their instances when this object is destroyed.")]
        [SerializeField] private bool disposeOnDestroy;

        public List<Entry> Entries => entries;

        private void Awake()
        {
            foreach (var entry in entries)
            {
                if (entry.prefab == null) continue;
                VatPools.Prewarm(entry.prefab, entry.prewarmCount, entry.maxSize);
            }
        }

        private void OnDestroy()
        {
            if (!disposeOnDestroy) return;
            foreach (var entry in entries)
            {
                if (entry.prefab != null) VatPools.Dispose(entry.prefab);
            }
        }
    }
}
