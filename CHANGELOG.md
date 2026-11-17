## [1.1.0](https://github.com/LeeDong1980/VatBaker/compare/a716408...upm) (2026-11-17)

Fork release on the `upm` branch of LeeDong1980/VatBaker (Unity 6 / URP 17.3).

### Features

* URP (Unity 6.3 / URP 17.3) SubShaders for VatUnlit and VatSurfaceStandard; SRP Batcher compatible, GPU instancing, shadow / depth / depth-normals / motion vector passes ([#1](https://github.com/LeeDong1980/VatBaker/pull/1))
* VatSurfaceStandard: metallic/smoothness, occlusion and normal maps ([#1](https://github.com/LeeDong1980/VatBaker/pull/1))
* Per-instance clip playback through `_VatClip` / `_VatClipTime` ([#1](https://github.com/LeeDong1980/VatBaker/pull/1))
* Multi-clip VAT atlas baker: `BakeAtlas`, `VatClip`, `VatClipAtlas`, `VatClipPreview` ([#2](https://github.com/LeeDong1980/VatBaker/pull/2))
* VatSurfaceStandard: URP Lit style emission (`_EmissionMap`, `_EmissionColor`, `_EMISSION`) and a keyword-backed Inspector toggle ([#4](https://github.com/LeeDong1980/VatBaker/pull/4))

## [1.0.5](https://github.com/fuqunaga/VatBaker/compare/v1.0.4...v1.0.5) (2025-03-03)


### Bug Fixes

* Update package.json ([8497729](https://github.com/fuqunaga/VatBaker/commit/84977295ab52afcd5491314283bc3900bb9adace))

## [1.0.4](https://github.com/fuqunaga/VatBaker/compare/v1.0.3...v1.0.4) (2024-12-04)


### Bug Fixes

* Vat.hlsl warnings ([66002ff](https://github.com/fuqunaga/VatBaker/commit/66002fff450077cedeaa7e7ec7ca1b8e46af4401))

## [1.0.3](https://github.com/fuqunaga/VatBaker/compare/v1.0.2...v1.0.3) (2022-08-25)


### Bug Fixes

* warning  "'VatBaker/VatUnlit': Output value 'vert' is not completely initialized at line 34 (on d3d11)" when build ([b99fc68](https://github.com/fuqunaga/VatBaker/commit/b99fc684e78a45a7010184ee48030a0ee005f081))

## [1.0.2](https://github.com/fuqunaga/VatBaker/compare/v1.0.1...v1.0.2) (2022-08-01)


### Bug Fixes

* add LICENSE.md ([97356ac](https://github.com/fuqunaga/VatBaker/commit/97356ac29f98ccf98ea0eb0294da606791ef65a9))

## [1.0.1](https://github.com/fuqunaga/VatBaker/compare/v1.0.0...v1.0.1) (2022-08-01)


### Bug Fixes

* pakcage.json. add .releaserc.yml, CHANGELOG.md ([d3a463f](https://github.com/fuqunaga/VatBaker/commit/d3a463f0157231426c1538496179155e07bf598e))
