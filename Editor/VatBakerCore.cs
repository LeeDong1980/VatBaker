using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

namespace VatBaker.Editor
{
    public static class VatBakerCore
    {
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int NormalTex = Shader.PropertyToID("_NormalTex");
        
        private static readonly int BaseShaderBumpMap = Shader.PropertyToID("_BumpMap");


        public static (Texture2D, Texture2D) BakeClip(string name, GameObject gameObject, SkinnedMeshRenderer skin, AnimationClip clip, float fps, Space space)
        {
            var vertexCount = skin.sharedMesh.vertexCount;
            var frameCount = Mathf.FloorToInt(clip.length * fps) + 1; // for loop

            var posTex = new Texture2D(vertexCount, frameCount, TextureFormat.RGBAHalf, false, true)
            {
                name = $"{name}.posTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            
            var normTex = new Texture2D(vertexCount, frameCount, TextureFormat.RGBAHalf, false, true)
            {
                name = $"{name}.normTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
   
            using var poolVtx0 = ListPool<Vector3>.Get(out var tmpVertexList);
            using var poolVtx1 = ListPool<Vector3>.Get(out var localVertices);
            
            using var poolNorm0 = ListPool<Vector3>.Get(out var tmpNormalList);
            using var poolNorm1 = ListPool<Vector3>.Get(out var localNormals);

            // SkinnedMeshRenderer.BakeMesh() uses the transform
            // but is not used in the actual display, so it is reset during Bake
            using var tranScope = TransformCacheScope.ResetScope(skin.transform);

            var mesh = new Mesh();
            var dt = 1f / fps;
            for (var i = 0; i < frameCount; i++)
            {
                clip.SampleAnimation(gameObject, dt * i);
                skin.BakeMesh(mesh);

                mesh.GetVertices(tmpVertexList);
                mesh.GetNormals(tmpNormalList);
                
                localVertices.AddRange(tmpVertexList);
                localNormals.AddRange(tmpNormalList);
            }

            var trans = gameObject.transform;
            var (vertices, normals) = space switch
            {
                Space.Self => (
                    localVertices.Select(vtx => trans.InverseTransformPoint(vtx)),
                    localNormals.Select(norm => trans.InverseTransformDirection(norm))
                ),
                Space.World => (localVertices, localNormals),

                _ => throw new ArgumentOutOfRangeException(nameof(space), space, null)
            };
            

            posTex.SetPixels(ListToColorArray(vertices));
            normTex.SetPixels(ListToColorArray(normals));

            return (posTex, normTex);

            static Color[] ListToColorArray(IEnumerable<Vector3> list) =>
                list.Select(v3 => new Color(v3.x, v3.y, v3.z)).ToArray();
        }
        
        public const int MaxAtlasSize = 4096;

        public static (Texture2D, Texture2D, VatClip[]) BakeAtlas(string name, GameObject gameObject,
            SkinnedMeshRenderer skin, AnimationClip[] clips, float fps, Space space)
        {
            var vertexCount = skin.sharedMesh.vertexCount;
            if (vertexCount > MaxAtlasSize)
            {
                throw new ArgumentException(
                    $"[{skin.sharedMesh.name}] has {vertexCount} vertices; an atlas row holds at most {MaxAtlasSize}.");
            }

            var vatClips = new VatClip[clips.Length];
            var height = 0;
            for (var i = 0; i < clips.Length; i++)
            {
                vatClips[i] = LayoutClip(clips[i], fps, height);
                height += vatClips[i].frameCount;
            }

            if (height > MaxAtlasSize)
            {
                throw new ArgumentException(
                    $"{clips.Length} clips need {height} rows at {fps} fps; an atlas holds at most {MaxAtlasSize}.");
            }

            var posTex = CreateAtlasTexture($"{name}.posTex", vertexCount, height);
            var normTex = CreateAtlasTexture($"{name}.normTex", vertexCount, height);
            var positionRow = new Color[vertexCount];
            var normalRow = new Color[vertexCount];
            using var poolVertices = ListPool<Vector3>.Get(out var vertices);
            using var poolNormals = ListPool<Vector3>.Get(out var normals);
            var mesh = new Mesh();
            var trans = gameObject.transform;

            // BakeMesh output depends on the skin's transform, so bake with it reset (restored on dispose).
            // Clips that move the root GameObject itself are unsupported here, as in BakeClip.
            using (TransformCacheScope.ResetScope(skin.transform))
            {
                for (var i = 0; i < clips.Length; i++)
                {
                    var vatClip = vatClips[i];
                    var dt = vatClip.frameCount > 1 ? clips[i].length / (vatClip.frameCount - 1) : 0f;
                    for (var frame = 0; frame < vatClip.frameCount; frame++)
                    {
                        clips[i].SampleAnimation(gameObject, dt * frame);
                        skin.BakeMesh(mesh);
                        mesh.GetVertices(vertices);
                        mesh.GetNormals(normals);

                        for (var v = 0; v < vertexCount; v++)
                        {
                            var position = vertices[v];
                            var normal = normals[v];
                            if (space == Space.Self)
                            {
                                position = trans.InverseTransformPoint(position);
                                normal = trans.InverseTransformDirection(normal);
                            }

                            positionRow[v] = new Color(position.x, position.y, position.z);
                            normalRow[v] = new Color(normal.x, normal.y, normal.z);
                        }

                        posTex.SetPixels(0, vatClip.startRow + frame, vertexCount, 1, positionRow);
                        normTex.SetPixels(0, vatClip.startRow + frame, vertexCount, 1, normalRow);
                    }
                }
            }

            UnityEngine.Object.DestroyImmediate(mesh);
            posTex.Apply();
            normTex.Apply();
            return (posTex, normTex, vatClips);
        }

        // Rounds the frame count and samples the clip's exact end, so a looping clip closes without a seam;
        // the clip's own fps absorbs the rounding.
        private static VatClip LayoutClip(AnimationClip clip, float fps, int startRow)
        {
            var intervals = clip.length > 0f ? Mathf.Max(1, Mathf.RoundToInt(clip.length * fps)) : 0;
            return new VatClip
            {
                name = clip.name,
                startRow = startRow,
                frameCount = intervals + 1,
                fps = intervals > 0 ? intervals / clip.length : 0f,
                loop = clip.isLooping,
            };
        }

        private static Texture2D CreateAtlasTexture(string name, int width, int height) =>
            new(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

        public static void GenerateAssets(string name, SkinnedMeshRenderer skin, float fps, float animLength, Shader shader, Texture posTex, Texture normTex)
        {
            var mat = CreateMaterial(shader, skin, posTex, normTex, fps, animLength);
            SaveOutput(name, skin, mat, posTex, normTex);
        }

        public static void GenerateAtlasAssets(string name, SkinnedMeshRenderer skin, Shader shader,
            Texture2D posTex, Texture2D normTex, VatClip[] clips)
        {
            // The single-clip material values play clip 0 (it starts at row 0), so the material previews on its own.
            var mat = CreateMaterial(shader, skin, posTex, normTex, clips[0].fps, clips[0].Length);

            var atlas = ScriptableObject.CreateInstance<VatClipAtlas>();
            atlas.name = $"{name}.atlas";
            atlas.Initialize(posTex, normTex, skin.sharedMesh.vertexCount, clips);

            SaveOutput(name, skin, mat, posTex, normTex, atlas);
        }

        private static Material CreateMaterial(Shader shader, SkinnedMeshRenderer skin, Texture posTex, Texture normTex,
            float fps, float animLength)
        {
            var mat = new Material(shader)
            {
                enableInstancing = true
            };

            mat.SetTexture(MainTex, skin.sharedMaterial.mainTexture);
            var normalTex = skin.sharedMaterial.GetTexture(BaseShaderBumpMap);
            if (normalTex != null)
            {
                mat.SetTexture(NormalTex, normalTex);
            }
            mat.SetTexture(VatShaderProperty.VatPositionTex, posTex);
            mat.SetTexture(VatShaderProperty.VatNormalTex, normTex);
            mat.SetFloat(VatShaderProperty.VatAnimFps, fps);
            mat.SetFloat(VatShaderProperty.VatAnimLength, animLength);
            return mat;
        }

        private static void SaveOutput(string name, SkinnedMeshRenderer skin, Material mat, params UnityEngine.Object[] assets)
        {
            const string folderName = "VatBakerOutput";

            var folderPath = CombinePathAndCreateFolderIfNotExist("Assets", folderName, false);
            var subFolderPath = CombinePathAndCreateFolderIfNotExist(folderPath, name);

            var go = new GameObject(name);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshFilter>().sharedMesh = skin.sharedMesh;

            foreach (var asset in assets)
            {
                AssetDatabase.CreateAsset(asset, CreatePath(subFolderPath, asset.name, "asset"));
            }
            AssetDatabase.CreateAsset(mat, CreatePath(subFolderPath, name, "mat"));
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go,
                CreatePath(subFolderPath, go.name, "prefab"),
                InteractionMode.AutomatedAction);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(prefab);

            static string CreatePath(string folder, string file, string extension)
                => Path.Combine(folder, $"{ReplaceInvalidPathChar(file)}.{extension}");
        }


        static string CombinePathAndCreateFolderIfNotExist(string parent, string folderName, bool unique = true)
        {
            parent = ReplaceInvalidPathChar(parent);
            folderName = ReplaceInvalidPathChar(folderName);
            
            var path = Path.Combine(parent, folderName);
            
            if (unique)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            }

            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }

            return path;

        }
        
        static readonly string InvalidChars = new string(Path.GetInvalidPathChars());
        
        static string ReplaceInvalidPathChar(string path)
        {
            return Regex.Replace(path, $"[{InvalidChars}]", "_");
        }
    }
}