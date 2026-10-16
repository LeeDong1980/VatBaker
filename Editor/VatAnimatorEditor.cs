using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VatBaker.Editor
{
    /// <summary>VatAnimator inspector: default clip popup, clip list, and clip preview in Edit and Play mode.</summary>
    [CustomEditor(typeof(VatAnimator))]
    [CanEditMultipleObjects]
    public class VatAnimatorEditor : UnityEditor.Editor
    {
        private SerializedProperty _animationData;
        private SerializedProperty _renderers;
        private SerializedProperty _defaultClip;
        private SerializedProperty _playOnEnable;
        private SerializedProperty _randomStartTime;
        private SerializedProperty _speed;
        private SerializedProperty _timeSource;
        private SerializedProperty _useUnscaledTime;

        // Edit mode preview
        private int _previewClip;
        private float _previewTime;
        private bool _previewPlaying;
        private bool _previewActive;
        private double _lastUpdateTime;

        private VatAnimator Animator => (VatAnimator)target;

        private void OnEnable()
        {
            _animationData = serializedObject.FindProperty("animationData");
            _renderers = serializedObject.FindProperty("renderers");
            _defaultClip = serializedObject.FindProperty("defaultClip");
            _playOnEnable = serializedObject.FindProperty("playOnEnable");
            _randomStartTime = serializedObject.FindProperty("randomStartTime");
            _speed = serializedObject.FindProperty("speed");
            _timeSource = serializedObject.FindProperty("timeSource");
            _useUnscaledTime = serializedObject.FindProperty("useUnscaledTime");

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_animationData);
            EditorGUILayout.PropertyField(_renderers);
            if (!serializedObject.isEditingMultipleObjects && Animator.Renderers.Length == 0)
            {
                EditorGUILayout.HelpBox("No renderers. They are collected from the children on Awake.", MessageType.Warning);
                if (GUILayout.Button("Collect Renderers"))
                {
                    Undo.RecordObject(Animator, "Collect Renderers");
                    Animator.CollectRenderers();
                    EditorUtility.SetDirty(Animator);
                }
            }

            DrawDefaultClip();
            EditorGUILayout.PropertyField(_playOnEnable);
            EditorGUILayout.PropertyField(_randomStartTime);
            EditorGUILayout.PropertyField(_speed);
            EditorGUILayout.PropertyField(_timeSource);
            if (_timeSource.enumValueIndex == (int)VatTimeSource.Script || _timeSource.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(_useUnscaledTime);
            }

            serializedObject.ApplyModifiedProperties();

            if (serializedObject.isEditingMultipleObjects) return;

            EditorGUILayout.Space();
            if (Application.isPlaying) DrawRuntime();
            else DrawPreview();

            DrawClipList();
        }

        private string[] ClipNames()
        {
            var a = Animator;
            return Enumerable.Range(0, a.ClipCount).Select(i => a.GetClip(i).name ?? $"Clip {i}").ToArray();
        }

        private void DrawDefaultClip()
        {
            var names = serializedObject.isEditingMultipleObjects ? null : ClipNames();
            if (names == null || names.Length == 0)
            {
                EditorGUILayout.PropertyField(_defaultClip);
                return;
            }

            var index = System.Array.IndexOf(names, _defaultClip.stringValue);
            var options = names.Select(n => new GUIContent(n)).ToArray();
            var newIndex = EditorGUILayout.Popup(new GUIContent("Default Clip", "Played on enable (and every time a pooled instance is spawned)."),
                Mathf.Max(index, 0), options);
            if (newIndex != index) _defaultClip.stringValue = names[newIndex];
        }

        private void DrawRuntime()
        {
            var a = Animator;
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Clip", a.CurrentClipName ?? "(material clip)");

            if (a.CurrentClipIndex >= 0)
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    var time = EditorGUILayout.Slider("Time", a.ClipTime, 0f, a.Length);
                    if (check.changed) a.ClipTime = time;
                }

                EditorGUILayout.LabelField("State", a.IsFinished ? "Finished" : a.IsPlaying ? (a.IsLooping ? "Playing (loop)" : "Playing (once)") : "Paused");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(a.IsPlaying ? "Pause" : "Resume")) { if (a.IsPlaying) a.Pause(); else a.Resume(); }
                if (GUILayout.Button("Play Default")) a.PlayDefault();
            }

            Repaint();
        }

        private void DrawPreview()
        {
            var a = Animator;
            if (a.ClipCount == 0) return;

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _previewClip = Mathf.Clamp(EditorGUILayout.Popup("Clip", _previewClip, ClipNames()), 0, a.ClipCount - 1);
                var length = a.GetClip(_previewClip).Length;
                _previewTime = EditorGUILayout.Slider("Time", Mathf.Min(_previewTime, length), 0f, length);
                if (check.changed) ApplyPreview();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_previewPlaying ? "Pause" : "Play"))
                {
                    _previewPlaying = !_previewPlaying;
                    _lastUpdateTime = EditorApplication.timeSinceStartup;
                    ApplyPreview();
                }

                using (new EditorGUI.DisabledScope(!_previewActive))
                {
                    if (GUILayout.Button("Stop")) StopPreview();
                }
            }
        }

        private void DrawClipList()
        {
            var data = Animator.AnimationData;
            if (data == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Clips ({data.ClipCount})", EditorStyles.boldLabel);
            for (var i = 0; i < data.ClipCount; i++)
            {
                var clip = data[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(clip.name, $"{clip.Length:0.00}s  {clip.fps:0.#}fps  {(clip.loop ? "loop" : "once")}");
                    if (Application.isPlaying && GUILayout.Button("Play", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        Animator.Play(i);
                    }
                }
            }
        }

        private void ApplyPreview()
        {
            var a = Animator;
            if (a == null || Application.isPlaying) return;

            _previewActive = true;
            if (a.CurrentClipIndex != _previewClip || !a.IsPlaying) a.Play(_previewClip, _previewTime, VatLoopMode.Loop);
            a.ClipTime = _previewTime;
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            _previewPlaying = false;
            if (!_previewActive) return;
            _previewActive = false;

            var a = Animator;
            if (a != null && !Application.isPlaying) a.ClearClip();
            SceneView.RepaintAll();
        }

        private void OnEditorUpdate()
        {
            if (!_previewPlaying || Application.isPlaying || Animator == null) return;

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastUpdateTime);
            _lastUpdateTime = now;

            var length = Animator.GetClip(_previewClip).Length;
            _previewTime = length > 0f ? Mathf.Repeat(_previewTime + dt * Animator.Speed, length) : 0f;
            ApplyPreview();
            Repaint();
        }
    }
}
