using UnityEditor;
using UnityEngine;

namespace VatBaker.Editor
{
    /// <summary>
    /// Material property drawer for a keyword toggle whose state is the keyword itself, not the float property.
    /// Usage: [VatKeywordToggle(_EMISSION)] _EmissionEnabled ("Emission", Float) = 0
    ///
    /// Unlike the built-in [Toggle(KEYWORD)], Apply() never turns the keyword off. It only syncs the float from the
    /// keyword, so a keyword set with Material.EnableKeyword() (or carried over from a URP Lit material when the
    /// shader is switched) survives shader reassignment, Reset and undo.
    /// </summary>
    public class VatKeywordToggleDrawer : MaterialPropertyDrawer
    {
        private readonly string _keyword;

        public VatKeywordToggleDrawer(string keyword)
        {
            _keyword = keyword;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            var targets = prop.targets;
            var isOn = ((Material)targets[0]).IsKeywordEnabled(_keyword);
            var isMixed = false;
            foreach (var target in targets)
            {
                if (((Material)target).IsKeywordEnabled(_keyword) != isOn)
                {
                    isMixed = true;
                    break;
                }
            }

            EditorGUI.showMixedValue = isMixed;
            EditorGUI.BeginChangeCheck();
            isOn = EditorGUI.Toggle(position, label, isOn);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            editor.RegisterPropertyChangeUndo(label.text);
            foreach (var target in targets)
            {
                var material = (Material)target;
                if (isOn)
                {
                    material.EnableKeyword(_keyword);
                }
                else
                {
                    material.DisableKeyword(_keyword);
                }

                material.SetFloat(prop.name, isOn ? 1f : 0f);
                EditorUtility.SetDirty(material);
            }
        }

        public override void Apply(MaterialProperty prop)
        {
            base.Apply(prop);
            foreach (var target in prop.targets)
            {
                var material = (Material)target;
                material.SetFloat(prop.name, material.IsKeywordEnabled(_keyword) ? 1f : 0f);
            }
        }
    }
}
