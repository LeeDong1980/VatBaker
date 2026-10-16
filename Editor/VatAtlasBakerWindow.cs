using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VatBaker.Editor
{
    /// <summary>
    /// Bakes models into multi-clip VAT atlases with a ready to use prefab (see <see cref="VatAtlasBaker"/>).
    /// Bakes one model with per-clip settings, or every model selected in the Project window with the defaults.
    /// </summary>
    public class VatAtlasBakerWindow : EditorWindow
    {
        private const string PrefsKey = "VatBaker.AtlasBakerWindow";

        [SerializeField] private GameObject source;
        [SerializeField] private VatAtlasBakeSettings settings = new();

        private Vector2 _scroll;
        private AnimationClip _clipToAdd;

        [MenuItem("Window/VatBaker/Atlas Baker")]
        public static void ShowWindow() => GetWindow<VatAtlasBakerWindow>("VAT Atlas Baker");

        [MenuItem("Assets/VatBaker/Bake VAT Atlas (Selected Models)", false, 2000)]
        private static void BakeSelectedMenu() => BakeMany(SelectedModels(), LoadDefaults());

        [MenuItem("Assets/VatBaker/Bake VAT Atlas (Selected Models)", true)]
        private static bool BakeSelectedMenuValidate() => SelectedModels().Any();

        [MenuItem("GameObject/VatBaker/Bake VAT Atlas", false, 49)]
        private static void BakeSceneObjectMenu()
        {
            var window = GetWindow<VatAtlasBakerWindow>("VAT Atlas Baker");
            window.SetSource(Selection.activeGameObject);
        }

        [MenuItem("GameObject/VatBaker/Bake VAT Atlas", true)]
        private static bool BakeSceneObjectMenuValidate()
            => Selection.activeGameObject != null && Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        private void OnEnable()
        {
            var defaults = LoadDefaults();
            if (settings.shader == null)
            {
                settings.fps = defaults.fps;
                settings.space = defaults.space;
                settings.shader = defaults.shader;
                settings.outputFolder = defaults.outputFolder;
            }

            if (source == null && Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                SetSource(Selection.activeGameObject);
            }
        }

        public void SetSource(GameObject go)
        {
            source = go;
            var clips = settings.clips;
            settings = VatAtlasBaker.CreateDefaultSettings(go, settings);
            if (go == null) settings.clips = clips;
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                var go = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Model", "A scene object, prefab or model asset with a SkinnedMeshRenderer."), source, typeof(GameObject), true);
                if (check.changed) SetSource(go);
            }

            settings.name = EditorGUILayout.TextField(new GUIContent("Name", "Asset names and output sub folder."), settings.name);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            DrawCommonSettings(settings);

            EditorGUILayout.Space();
            DrawClips();

            EditorGUILayout.Space();
            DrawSummaryAndBake();

            EditorGUILayout.Space(16);
            DrawBatch();

            EditorGUILayout.EndScrollView();
        }

        private static void DrawCommonSettings(VatAtlasBakeSettings s)
        {
            s.fps = Mathf.Max(1f, EditorGUILayout.FloatField(new GUIContent("Fps", "Frames baked per second. Higher is smoother and uses taller textures."), s.fps));
            s.space = (Space)EditorGUILayout.EnumPopup(new GUIContent("Space", "Self bakes relative to the model root (root motion on the root is removed)."), s.space);
            s.shader = (Shader)EditorGUILayout.ObjectField("Shader", s.shader, typeof(Shader), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                s.outputFolder = EditorGUILayout.TextField("Output Folder", s.outputFolder);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    var abs = EditorUtility.OpenFolderPanel("VAT output folder", s.outputFolder, "");
                    var dataPath = Application.dataPath.Replace('\\', '/');
                    abs = abs.Replace('\\', '/');
                    if (abs.StartsWith(dataPath)) s.outputFolder = "Assets" + abs.Substring(dataPath.Length);
                }
            }
        }

        private void DrawClips()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Clips ({settings.clips.Count(c => c.include && c.clip != null)} / {settings.clips.Count})", EditorStyles.boldLabel);
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40))) settings.clips.ForEach(c => c.include = true);
                if (GUILayout.Button("None", EditorStyles.miniButtonMid, GUILayout.Width(40))) settings.clips.ForEach(c => c.include = false);
                if (GUILayout.Button("Refresh", EditorStyles.miniButtonRight, GUILayout.Width(60))) SetSource(source);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(20);
                GUILayout.Label("Clip", EditorStyles.miniLabel, GUILayout.MinWidth(80));
                GUILayout.Label("Name (for Play)", EditorStyles.miniLabel, GUILayout.MinWidth(80));
                GUILayout.Label("Loop", EditorStyles.miniLabel, GUILayout.Width(34));
                GUILayout.Label("Fps", EditorStyles.miniLabel, GUILayout.Width(40));
                GUILayout.Space(22);
            }

            VatClipBakeSettings remove = null;
            foreach (var clip in settings.clips)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    clip.include = EditorGUILayout.Toggle(clip.include, GUILayout.Width(16));
                    using (new EditorGUI.DisabledScope(!clip.include))
                    {
                        clip.clip = (AnimationClip)EditorGUILayout.ObjectField(clip.clip, typeof(AnimationClip), false, GUILayout.MinWidth(80));
                        clip.name = EditorGUILayout.TextField(clip.name, GUILayout.MinWidth(80));
                        clip.loop = EditorGUILayout.Toggle(clip.loop, GUILayout.Width(34));
                        clip.fps = Mathf.Max(0f, EditorGUILayout.FloatField(clip.fps, GUILayout.Width(40)));
                    }

                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20))) remove = clip;
                }
            }

            if (remove != null) settings.clips.Remove(remove);

            using (new EditorGUILayout.HorizontalScope())
            {
                _clipToAdd = (AnimationClip)EditorGUILayout.ObjectField("Add Clip", _clipToAdd, typeof(AnimationClip), false);
                using (new EditorGUI.DisabledScope(_clipToAdd == null))
                {
                    if (GUILayout.Button("Add", GUILayout.Width(50)))
                    {
                        settings.clips.Add(new VatClipBakeSettings(_clipToAdd));
                        _clipToAdd = null;
                    }
                }
            }

            EditorGUILayout.HelpBox("Fps 0 uses the bake fps. Clips from other files (for example separate animation FBX files) can be added above, " +
                                    "they must share the model's rig.", MessageType.None);
        }

        private void DrawSummaryAndBake()
        {
            var layout = VatAtlasBaker.CalcLayout(settings);
            if (source != null)
            {
                var skins = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToArray();
                long bytes = 0;
                foreach (var skin in skins)
                {
                    bytes += (long)skin.sharedMesh.vertexCount * layout.totalRows * 8 * 2;
                }

                var lines = skins.Select(s => $"{s.name}: {s.sharedMesh.vertexCount} x {layout.totalRows} px").ToList();
                lines.Add($"{layout.clips.Length} clips, {layout.totalRows} frames, {bytes / (1024f * 1024f):0.0} MB of VAT textures");
                EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.Info);
            }

            var errors = VatAtlasBaker.Validate(source, settings);
            foreach (var error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);

            using (new EditorGUI.DisabledScope(errors.Count > 0))
            {
                if (GUILayout.Button("Bake", GUILayout.Height(28)))
                {
                    SaveDefaults(settings);
                    BakeOne(source, settings);
                }
            }
        }

        private void DrawBatch()
        {
            EditorGUILayout.LabelField("Batch", EditorStyles.boldLabel);
            var models = SelectedModels().ToArray();
            EditorGUILayout.HelpBox(
                "Bakes every model selected in the Project window with the settings above and all of its clips " +
                "(loop from each clip's Loop Time). Also in the Project window context menu: VatBaker > Bake VAT Atlas.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(models.Length == 0))
            {
                if (GUILayout.Button($"Bake {models.Length} Selected Model(s)"))
                {
                    SaveDefaults(settings);
                    BakeMany(models, settings);
                }
            }
        }

        private void OnSelectionChange() => Repaint();

        #region Bake

        private static void BakeOne(GameObject go, VatAtlasBakeSettings s)
        {
            try
            {
                var prefab = VatAtlasBaker.Bake(go, s);
                EditorGUIUtility.PingObject(prefab);
                Selection.activeObject = prefab;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("VAT Atlas Baker", e.Message, "Close");
            }
        }

        public static List<GameObject> BakeMany(IEnumerable<GameObject> models, VatAtlasBakeSettings template)
        {
            var prefabs = new List<GameObject>();
            var failures = new List<string>();

            foreach (var model in models)
            {
                var s = VatAtlasBaker.CreateDefaultSettings(model, template);
                var errors = VatAtlasBaker.Validate(model, s);
                if (errors.Count > 0)
                {
                    failures.Add($"{model.name}: {string.Join(" ", errors)}");
                    continue;
                }

                try
                {
                    prefabs.Add(VatAtlasBaker.Bake(model, s));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    failures.Add($"{model.name}: {e.Message}");
                }
            }

            foreach (var failure in failures) Debug.LogWarning($"[VatBaker] Skipped {failure}");
            Debug.Log($"[VatBaker] Baked {prefabs.Count} VAT prefab(s){(failures.Count > 0 ? $", skipped {failures.Count} (see warnings)" : "")}.");

            if (prefabs.Count > 0) Selection.objects = prefabs.Cast<UnityEngine.Object>().ToArray();
            return prefabs;
        }

        private static IEnumerable<GameObject> SelectedModels()
        {
            return Selection.GetFiltered<GameObject>(SelectionMode.Assets)
                .Where(go => go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null);
        }

        #endregion

        #region Defaults

        private static VatAtlasBakeSettings LoadDefaults()
        {
            var s = new VatAtlasBakeSettings();
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var saved = JsonUtility.FromJson<SavedDefaults>(json);
                    s.fps = saved.fps > 0f ? saved.fps : s.fps;
                    s.space = saved.space;
                    s.outputFolder = string.IsNullOrEmpty(saved.outputFolder) ? s.outputFolder : saved.outputFolder;
                    s.shader = Shader.Find(saved.shaderName ?? "");
                }
                catch (ArgumentException)
                {
                }
            }

            if (s.shader == null) s.shader = VatAtlasBaker.DefaultShader;
            return s;
        }

        private static void SaveDefaults(VatAtlasBakeSettings s)
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(new SavedDefaults
            {
                fps = s.fps,
                space = s.space,
                outputFolder = s.outputFolder,
                shaderName = s.shader != null ? s.shader.name : null,
            }));
        }

        [Serializable]
        private struct SavedDefaults
        {
            public float fps;
            public Space space;
            public string outputFolder;
            public string shaderName;
        }

        #endregion
    }
}
