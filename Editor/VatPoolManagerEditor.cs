using UnityEditor;
using UnityEngine;

namespace VatBaker.Editor
{
    /// <summary>VatPoolManager inspector with live pool counts in Play mode.</summary>
    [CustomEditor(typeof(VatPoolManager))]
    public class VatPoolManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (VatPoolManager)target;
            foreach (var entry in manager.Entries)
            {
                if (entry.prefab != null && entry.prefab.GetComponentInChildren<VatAnimator>(true) == null)
                {
                    EditorGUILayout.HelpBox($"{entry.prefab.name} has no VatAnimator. It still pools, but its animation can't be controlled.", MessageType.Info);
                }
            }

            if (!Application.isPlaying) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pools (all, Play mode)", EditorStyles.boldLabel);
            foreach (var pool in VatPools.All)
            {
                EditorGUILayout.LabelField(pool.Prefab != null ? pool.Prefab.name : "(missing prefab)",
                    $"active {pool.CountActive}  inactive {pool.CountInactive}{(pool.MaxSize > 0 ? $"  max {pool.MaxSize}" : "")}");
            }

            Repaint();
        }
    }
}
