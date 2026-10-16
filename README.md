# VatBaker (UPM)

Bakes `AnimationClip`s into Vertex Animation Textures (VAT) and plays them on `MeshRenderer`s, so large crowds of
animated models render with GPU instancing instead of `SkinnedMeshRenderer`s.
Works with URP 17.3 (Unity 6.3) and the Built-in pipeline.

Install: `https://github.com/LeeDong1980/VatBaker.git#upm`

## 1. Bake

**Window > VatBaker > Atlas Baker**

- Drop a model (scene object, prefab or FBX) with a `SkinnedMeshRenderer`. Its clips are listed from its Animator
  controller, Animation component and the FBX itself. Clips from other files with the same rig can be added.
- Per clip: include, name used by `Play`, loop, fps override.
- **Bake** writes to `Assets/VatBakerOutput/<Name>/`:
  - `<Name>.prefab`: `MeshRenderer`(s) with VAT materials and a `VatAnimator`, ready to pool and spawn.
  - `<Name>.vat.asset`: `VatAnimationData`, the clip layout of the atlas.
  - per `SkinnedMeshRenderer`: position / normal textures (all clips stacked vertically), a mesh without bone
    weights whose bounds cover every frame, and materials copied from the source materials (base map, color,
    normal, metallic / smoothness, occlusion).
- Baking again overwrites in place: GUIDs, references and existing materials (with your tweaks) are kept.

**Batch:** select any number of models in the Project window, then **Assets > VatBaker > Bake VAT Atlas
(Selected Models)** (or the Batch button in the window). Every clip is baked with the window's settings, loop
taken from each clip's Loop Time.

The original one-clip-per-prefab baker is still at **Window > VatBaker > Per-Clip Baker (Legacy)**.

## 2. Play: `VatAnimator`

```csharp
var vat = GetComponent<VatAnimator>();
vat.Play("Run");                               // clip's own loop setting
vat.Play("Run", startTime: 0.3f, VatLoopMode.Loop);
vat.PlayNormalized("Idle", Random.value);      // desync a crowd
vat.PlayOnce("Attack", nextClipName: "Idle");  // one-shot, then back to idle
vat.Speed = 1.5f;                              // negative plays backwards
vat.ClipTime = 0.5f;  vat.NormalizedTime = 0.5f;
vat.Pause();  vat.Resume();
vat.Finished += v => Debug.Log($"{v.CurrentClipName} done");
```

- Per instance through a `MaterialPropertyBlock` (`_VatClip`, `_VatClipTime`, `_VatClipTimeScale`), so all
  instances share one material and are drawn with GPU instancing (enabled on baked materials).
- **Time Source = Shader** (default): the shader advances the time from `_Time`, so a playing animator costs nothing
  per frame. The block is only written on `Play`, `Speed`, `ClipTime`, `Pause` / `Resume`. One-shot clips are
  checked once per frame for `Finished` from a single update loop (no per-object `Update`).
- **Time Source = Script**: the time is written every frame (supports unscaled time and any shader that reads
  `_VatClip` / `_VatClipTime`, for example a custom VatURPLit without `_VatClipTimeScale`).
- `Play On Enable` + `Random Start Time` start the default clip whenever the object is enabled or spawned.
- Without a `VatAnimationData`, the animator plays the single clip of a material made by the legacy baker.
- Inspector: default clip popup, clip list, and Edit mode preview (clip, time slider, play).

## 3. Pool: `VatPools`

```csharp
var go = VatPools.Spawn(zombiePrefab, position, rotation);
go.GetComponent<VatAnimator>().Play("Walk");
...
VatPools.Despawn(go);

VatPools.Prewarm(zombiePrefab, 500, maxSize: 1000);
```

- One pool per prefab. Pooled instances wait inactive under a hidden `DontDestroyOnLoad` root; spawned ones move
  to the active scene (or the given parent).
- `VatPooledObject` (added automatically): `Despawn()`, and **Despawn When Animation Finished** for effects and
  death animations.
- `IVatPoolListener.OnSpawned / OnDespawned` on any component of the prefab to reset gameplay state.
- `VatPoolManager` component: prewarm counts and max sizes from the scene, live counts in its inspector.

## Shader properties

| Property | Set by | Meaning |
| --- | --- | --- |
| `_VatPositionTex`, `_VatNormalTex` | material | baked textures |
| `_VatAnimFps`, `_VatAnimLength` | material | clip played when no `_VatClip` is set |
| `_AnimationTimeOffset` | property block | time offset of that material clip |
| `_VatClip` | property block | `(startRow, frameCount, fps, loop)`, see `VatClip.ToShaderVector()` |
| `_VatClipTime` | property block | seconds into the clip |
| `_VatClipTimeScale` | property block | clip time = `_VatClipTime + _Time.y * _VatClipTimeScale` |

Not supported: blending / cross-fading between clips, DOTS instancing (GPU Resident Drawer), meshes with more
vertices than the max texture width (16384).
