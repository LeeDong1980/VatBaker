using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VatBaker.Editor
{
    [Serializable]
    public class VatClipBakeSettings
    {
        public bool include = true;
        public AnimationClip clip;
        [Tooltip("Name used by VatAnimator.Play. Defaults to the clip name.")]
        public string name;
        public bool loop = true;
        [Tooltip("0 uses the bake fps.")]
        public float fps;

        public VatClipBakeSettings() { }

        public VatClipBakeSettings(AnimationClip clip)
        {
            this.clip = clip;
            name = clip != null ? clip.name : "";
            loop = clip != null && AnimationUtility.GetAnimationClipSettings(clip).loopTime;
        }
    }

    [Serializable]
    public class VatAtlasBakeSettings
    {
        [Tooltip("Asset names and the output sub folder. Defaults to the source object name.")]
        public string name;
        public float fps = 30f;
        public Space space = Space.Self;
        public Shader shader;
        public string outputFolder = "Assets/VatBakerOutput";
        public List<VatClipBakeSettings> clips = new();
    }

    /// <summary>
    /// Bakes every clip of a model into one position / normal texture pair per SkinnedMeshRenderer (clips stacked
    /// vertically), and writes a ready to use prefab: MeshRenderers with VAT materials, a <see cref="VatAnimator"/>
    /// and the <see cref="VatAnimationData"/> listing the clips. Baking again overwrites the assets in place,
    /// so references to the prefab, materials and data keep working.
    /// </summary>
    public static class VatAtlasBaker
    {
        private const string DefaultShaderName = "VatBaker/VatSurfaceStandard";

        public static Shader DefaultShader => Shader.Find(DefaultShaderName);

        #region Clip discovery

        /// <summary>
        /// Clips of a model: its Animator controller or Animation component, and the clips imported in its model file.
        /// </summary>
        public static List<AnimationClip> FindClips(GameObject source)
        {
            var clips = new List<AnimationClip>();
            if (source == null) return clips;

            void Add(AnimationClip clip)
            {
                if (clip != null && !clip.name.StartsWith("__preview__") && !clips.Contains(clip)) clips.Add(clip);
            }

            foreach (var animator in source.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController == null) continue;
                foreach (var clip in animator.runtimeAnimatorController.animationClips) Add(clip);
            }

            foreach (var clip in AnimationUtility.GetAnimationClips(source)) Add(clip);

            var path = FindSourceAssetPath(source);
            if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
            {
                foreach (var clip in AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>()) Add(clip);
            }

            return clips;
        }

        public static VatAtlasBakeSettings CreateDefaultSettings(GameObject source, VatAtlasBakeSettings template = null)
        {
            return new VatAtlasBakeSettings
            {
                name = source != null ? source.name : "",
                fps = template?.fps ?? 30f,
                space = template?.space ?? Space.Self,
                shader = template?.shader != null ? template.shader : DefaultShader,
                outputFolder = template?.outputFolder ?? "Assets/VatBakerOutput",
                clips = FindClips(source).Select(c => new VatClipBakeSettings(c)).ToList(),
            };
        }

        private static string FindSourceAssetPath(GameObject source)
        {
            var path = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(path)) return path;

            var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(source);
            return original != null ? AssetDatabase.GetAssetPath(original) : null;
        }

        #endregion

        #region Validation

        public readonly struct Layout
        {
            public readonly VatClip[] clips;
            public readonly int totalRows;

            public Layout(VatClip[] clips, int totalRows)
            {
                this.clips = clips;
                this.totalRows = totalRows;
            }
        }

        /// <summary>Row layout of the clips that will be baked.</summary>
        public static Layout CalcLayout(VatAtlasBakeSettings settings)
        {
            var clips = new List<VatClip>();
            var names = new HashSet<string>();
            var row = 0;

            foreach (var c in settings.clips)
            {
                if (!c.include || c.clip == null) continue;

                var fps = c.fps > 0f ? c.fps : settings.fps;
                var frameCount = Mathf.FloorToInt(c.clip.length * fps) + 1;

                var clipName = string.IsNullOrEmpty(c.name) ? c.clip.name : c.name;
                var uniqueName = clipName;
                for (var i = 1; !names.Add(uniqueName); i++) uniqueName = $"{clipName}_{i}";

                clips.Add(new VatClip(uniqueName, row, frameCount, fps, c.loop));
                row += frameCount;
            }

            return new Layout(clips.ToArray(), row);
        }

        /// <summary>Problems that stop a bake. Empty when the bake can run.</summary>
        public static List<string> Validate(GameObject source, VatAtlasBakeSettings settings)
        {
            var errors = new List<string>();
            if (source == null)
            {
                errors.Add("No source object.");
                return errors;
            }

            var skins = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToArray();
            if (skins.Length == 0) errors.Add($"{source.name} has no SkinnedMeshRenderer with a mesh.");
            if (settings.shader == null) errors.Add("No shader.");
            if (settings.fps <= 0f) errors.Add("Fps must be greater than 0.");
            if (string.IsNullOrEmpty(settings.outputFolder) || !settings.outputFolder.Replace('\\', '/').StartsWith("Assets"))
            {
                errors.Add("The output folder must be inside Assets.");
            }

            var layout = CalcLayout(settings);
            if (layout.clips.Length == 0) errors.Add("No clip selected.");

            var maxSize = SystemInfo.maxTextureSize;
            if (layout.totalRows > maxSize)
            {
                errors.Add($"{layout.totalRows} frames in total is more than the max texture height {maxSize}. Lower the fps or bake fewer clips.");
            }

            foreach (var skin in skins)
            {
                if (skin.sharedMesh.vertexCount > maxSize)
                {
                    errors.Add($"{skin.name} has {skin.sharedMesh.vertexCount} vertices, more than the max texture width {maxSize}.");
                }
            }

            var humanoid = settings.clips.Any(c => c.include && c.clip != null && c.clip.humanMotion);
            if (humanoid)
            {
                var animator = source.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    errors.Add("Humanoid clips need an Animator with a humanoid Avatar on the source object.");
                }
            }

            return errors;
        }

        #endregion

        #region Bake

        private class SkinBake
        {
            public SkinnedMeshRenderer skin;
            public string label;
            public int width;
            public ushort[] position; // RGBAHalf
            public ushort[] normal;   // RGBAHalf
        }

        /// <summary>Bakes <paramref name="source"/> (a scene object, prefab or model asset) and returns the prefab asset.</summary>
        public static GameObject Bake(GameObject source, VatAtlasBakeSettings settings)
        {
            var errors = Validate(source, settings);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));

            var baseName = SanitizeFileName(string.IsNullOrEmpty(settings.name) ? source.name : settings.name);
            var layout = CalcLayout(settings);

            // Sample a copy, so the source object, prefab or model is never modified.
            var instance = Object.Instantiate(source);
            instance.name = source.name;
            instance.hideFlags = HideFlags.DontSave;
            if (source.scene.IsValid())
            {
                instance.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                instance.transform.localScale = source.transform.lossyScale;
            }

            var animationModeStarted = false;
            try
            {
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToArray();
                var bakes = skins.Select((skin, i) => new SkinBake
                {
                    skin = skin,
                    label = skins.Length == 1 ? baseName : $"{baseName}_{SanitizeFileName(skin.name)}",
                    width = skin.sharedMesh.vertexCount,
                    position = new ushort[skin.sharedMesh.vertexCount * layout.totalRows * 4],
                    normal = new ushort[skin.sharedMesh.vertexCount * layout.totalRows * 4],
                }).ToArray();

                var root = instance.transform;
                var bounds = new Bounds();
                var boundsInitialized = false;
                var mesh = new Mesh();
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();

                var clipSettings = settings.clips.Where(c => c.include && c.clip != null).ToArray();
                for (var c = 0; c < layout.clips.Length; c++)
                {
                    var clip = clipSettings[c].clip;
                    var vatClip = layout.clips[c];

                    if (EditorUtility.DisplayCancelableProgressBar("VatBaker", $"Baking {baseName}: {clip.name}", (float)c / layout.clips.Length))
                    {
                        throw new OperationCanceledException("VAT bake canceled.");
                    }

                    if (clip.humanMotion && !animationModeStarted)
                    {
                        AnimationMode.StartAnimationMode();
                        animationModeStarted = true;
                    }

                    for (var f = 0; f < vatClip.frameCount; f++)
                    {
                        var time = Mathf.Min(f / vatClip.fps, clip.length);
                        if (clip.humanMotion)
                        {
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(instance, clip, time);
                            AnimationMode.EndSampling();
                        }
                        else
                        {
                            clip.SampleAnimation(instance, time);
                        }

                        var row = vatClip.startRow + f;
                        foreach (var bake in bakes)
                        {
                            // BakeMesh() is relative to the renderer's transform, so bake at the origin; the
                            // vertices are then in world space and converted to the root's space below.
                            // BakeMesh(mesh) ignores the renderer's scale, like the original baker.
                            bake.skin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                            bake.skin.BakeMesh(mesh);
                            mesh.GetVertices(vertices);
                            mesh.GetNormals(normals);

                            var offset = row * bake.width * 4;
                            for (var v = 0; v < bake.width; v++)
                            {
                                var p = vertices[v];
                                var n = v < normals.Count ? normals[v] : Vector3.up;
                                if (settings.space == Space.Self)
                                {
                                    p = root.InverseTransformPoint(p);
                                    n = root.InverseTransformDirection(n);
                                }

                                if (!boundsInitialized)
                                {
                                    bounds = new Bounds(p, Vector3.zero);
                                    boundsInitialized = true;
                                }
                                else
                                {
                                    bounds.Encapsulate(p);
                                }

                                WriteHalf(bake.position, offset + v * 4, p);
                                WriteHalf(bake.normal, offset + v * 4, n);
                            }
                        }
                    }
                }

                Object.DestroyImmediate(mesh);

                return WriteAssets(baseName, settings, layout, bakes, bounds);
            }
            finally
            {
                if (animationModeStarted) AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(instance);
                EditorUtility.ClearProgressBar();
            }
        }

        private static void WriteHalf(ushort[] data, int index, Vector3 v)
        {
            data[index + 0] = Mathf.FloatToHalf(v.x);
            data[index + 1] = Mathf.FloatToHalf(v.y);
            data[index + 2] = Mathf.FloatToHalf(v.z);
            data[index + 3] = Mathf.FloatToHalf(1f);
        }

        #endregion

        #region Assets

        private static GameObject WriteAssets(string baseName, VatAtlasBakeSettings settings, Layout layout, SkinBake[] bakes, Bounds bounds)
        {
            var folder = EnsureFolder($"{settings.outputFolder.TrimEnd('/', '\\')}/{baseName}");

            var data = SaveAsset(ScriptableObject.CreateInstance<VatAnimationData>(), $"{folder}/{baseName}.vat.asset");
            data.SetClips(layout.clips, bounds);
            EditorUtility.SetDirty(data);

            var firstClip = layout.clips[0];
            var parts = new List<(SkinBake bake, Mesh mesh, Material[] materials)>();
            foreach (var bake in bakes)
            {
                var posTex = SaveAsset(CreateTexture($"{bake.label}.posTex", bake.width, layout.totalRows, bake.position), $"{folder}/{bake.label}.posTex.asset");
                var normTex = SaveAsset(CreateTexture($"{bake.label}.normTex", bake.width, layout.totalRows, bake.normal), $"{folder}/{bake.label}.normTex.asset");
                var mesh = SaveAsset(CreateStaticMesh(bake.skin.sharedMesh, bake.label, bounds), $"{folder}/{bake.label}.mesh.asset");

                var sourceMaterials = bake.skin.sharedMaterials;
                var subMeshCount = mesh.subMeshCount;
                var materials = new Material[Mathf.Max(subMeshCount, 1)];
                for (var i = 0; i < materials.Length; i++)
                {
                    var src = i < sourceMaterials.Length ? sourceMaterials[i] : null;
                    var matName = materials.Length == 1 ? bake.label : $"{bake.label}_{i}_{SanitizeFileName(src != null ? src.name : "Material")}";
                    var path = $"{folder}/{matName}.mat";

                    // Keep an existing material, so tweaks made after the last bake survive.
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                    {
                        mat = new Material(settings.shader) { name = matName, enableInstancing = true };
                        CopySurfaceProperties(src, mat);
                        AssetDatabase.CreateAsset(mat, path);
                    }

                    mat.SetTexture(VatShaderProperty.VatPositionTex, posTex);
                    mat.SetTexture(VatShaderProperty.VatNormalTex, normTex);
                    // A renderer without VatAnimator plays the first clip.
                    mat.SetFloat(VatShaderProperty.VatAnimFps, firstClip.fps);
                    mat.SetFloat(VatShaderProperty.VatAnimLength, firstClip.Length);
                    EditorUtility.SetDirty(mat);
                    materials[i] = mat;
                }

                parts.Add((bake, mesh, materials));
            }

            var prefabPath = $"{folder}/{baseName}.prefab";
            var exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = exists ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(baseName);
            try
            {
                var renderers = new List<Renderer>();
                foreach (var (bake, mesh, materials) in parts)
                {
                    var go = parts.Count == 1 ? root : FindOrCreateChild(root.transform, bake.skin.name);
                    var filter = go.TryGetComponent<MeshFilter>(out var f) ? f : go.AddComponent<MeshFilter>();
                    var renderer = go.TryGetComponent<MeshRenderer>(out var r) ? r : go.AddComponent<MeshRenderer>();
                    filter.sharedMesh = mesh;
                    renderer.sharedMaterials = materials;
                    if (!exists)
                    {
                        renderer.shadowCastingMode = bake.skin.shadowCastingMode;
                        renderer.receiveShadows = bake.skin.receiveShadows;
                        renderer.lightProbeUsage = bake.skin.lightProbeUsage;
                        renderer.reflectionProbeUsage = bake.skin.reflectionProbeUsage;
                        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    }

                    renderers.Add(renderer);
                }

                var animator = root.TryGetComponent<VatAnimator>(out var a) ? a : root.AddComponent<VatAnimator>();
                animator.AnimationData = data;
                animator.Renderers = renderers.ToArray();
                if (string.IsNullOrEmpty(animator.DefaultClip) || data.IndexOf(animator.DefaultClip) < 0)
                {
                    animator.DefaultClip = (layout.clips.FirstOrDefault(c => c.loop).name) ?? firstClip.name;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static Texture2D CreateTexture(string name, int width, int height, ushort[] data)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixelData(data, 0);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// A copy of the skinned mesh without bone weights and blend shapes (VAT does not use them), with bounds
        /// that cover every baked frame so the renderer is never culled while the animation is visible.
        /// </summary>
        private static Mesh CreateStaticMesh(Mesh source, string name, Bounds bounds)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = source.indexFormat,
            };

            mesh.SetVertices(source.vertices);
            if (source.HasVertexAttribute(VertexAttribute.Normal)) mesh.SetNormals(source.normals);
            if (source.HasVertexAttribute(VertexAttribute.Tangent)) mesh.SetTangents(source.tangents);
            if (source.HasVertexAttribute(VertexAttribute.Color)) mesh.SetColors(source.colors32);

            var uvs = new List<Vector4>();
            for (var channel = 0; channel < 8; channel++)
            {
                if (!source.HasVertexAttribute(VertexAttribute.TexCoord0 + channel)) continue;
                source.GetUVs(channel, uvs);
                mesh.SetUVs(channel, uvs);
            }

            mesh.subMeshCount = source.subMeshCount;
            for (var i = 0; i < source.subMeshCount; i++)
            {
                mesh.SetIndices(source.GetIndices(i), source.GetTopology(i), i, false);
            }

            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>Copies the textures and surface values the VAT shaders understand from a Standard / URP Lit material.</summary>
        public static void CopySurfaceProperties(Material src, Material dst)
        {
            if (src == null) return;

            if (src.mainTexture != null)
            {
                dst.mainTexture = src.mainTexture;
                dst.mainTextureScale = src.mainTextureScale;
                dst.mainTextureOffset = src.mainTextureOffset;
            }

            CopyColor(src, dst, "_BaseColor", "_BaseColor");
            CopyColor(src, dst, "_Color", "_BaseColor");
            CopyTexture(src, dst, "_BumpMap", "_NormalTex");
            CopyTexture(src, dst, "_MetallicGlossMap", "_MetallicGlossMap");
            CopyFloat(src, dst, "_Metallic", "_Metallic");
            if (src.HasProperty("_Smoothness"))
            {
                CopyFloat(src, dst, "_Smoothness", "_Smoothness"); // URP Lit
            }
            else
            {
                // Standard: the smoothness scale is _GlossMapScale with a metallic map, _Glossiness without.
                var hasMap = src.HasProperty("_MetallicGlossMap") && src.GetTexture("_MetallicGlossMap") != null;
                CopyFloat(src, dst, hasMap ? "_GlossMapScale" : "_Glossiness", "_Smoothness");
            }
            CopyTexture(src, dst, "_OcclusionMap", "_OcclusionMap");
            CopyFloat(src, dst, "_OcclusionStrength", "_OcclusionStrength");

            static void CopyTexture(Material s, Material d, string from, string to)
            {
                if (s.HasProperty(from) && d.HasProperty(to) && s.GetTexture(from) != null) d.SetTexture(to, s.GetTexture(from));
            }

            static void CopyFloat(Material s, Material d, string from, string to)
            {
                if (s.HasProperty(from) && d.HasProperty(to)) d.SetFloat(to, s.GetFloat(from));
            }

            static void CopyColor(Material s, Material d, string from, string to)
            {
                if (s.HasProperty(from) && d.HasProperty(to)) d.SetColor(to, s.GetColor(from));
            }
        }

        /// <summary>Saves a new asset, or copies it into the existing asset at the path so its GUID and references stay.</summary>
        private static T SaveAsset<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }

            var assetName = existing.name;
            EditorUtility.CopySerialized(asset, existing);
            existing.name = assetName;
            Object.DestroyImmediate(asset);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static GameObject FindOrCreateChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null) return child.gameObject;

            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static string EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }

            return current;
        }

        public static string SanitizeFileName(string fileName)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
            return fileName;
        }

        #endregion
    }
}
