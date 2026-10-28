# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**DennokoEx** is a lilToon 2.x custom shader extension for VRChat avatars. It adds four extra layers on top of lilToon's standard pipeline: Reflection 2nd, Rim 2nd, Matcap 3rd, and Normal Map 3rd. The shader name is `dennokoworks/DennokoEx`.

## Build / Iteration Workflow

This is a Unity Editor package — there are no CLI build or test commands. To iterate:

1. Open the project in Unity via VRChat Creator Companion (VCC).
2. Shader changes in `Shaders/` are compiled automatically on save.
3. Inspector changes in `Editor/` recompile via Unity's domain reload.

## Architecture

### Shader Pipeline (`Shaders/`)

lilToon's custom shader system compiles `.lilcontainer` files (one per render mode variant) by substituting three `.lilblock` injection points:

| File | Role |
|---|---|
| `lilCustomShaderDatas.lilblock` | Declares the shader name and editor class |
| `lilCustomShaderProperties.lilblock` | Material property declarations (`_CustomXxx`) |
| `lilCustomShaderInsert.lilblock` | `#include`s `custom_insert.hlsl` — forces TBN into v2f |
| `custom.hlsl` | All fragment shader logic via `#define` hooks into lilToon's pipeline |

The `custom.hlsl` hooks execute at these lilToon pipeline points:
- `BEFORE_AUDIOLINK` → Normal Map 3rd (composites on top of lilToon's normal pipeline)
- `BEFORE_REFLECTION` → Reflection 2nd (Blinn-Phong isotropic or Kajiya-Kay anisotropic)
- `BEFORE_RIMLIGHT` → Matcap 3rd
- `BEFORE_EMISSION_1ST` → Rim 2nd

### VRC Light Volumes

DennokoEx does not integrate VRC Light Volumes (removed 2026-10: v3 made the v2 integration obsolete, and world-dependent looks could not be checked in the editor). Reflection 2nd lights from lilToon's main light (`fd.L` / `fd.lightColor`) in both modes — isotropic Blinn-Phong and anisotropic Kajiya-Kay — and lilToon itself derives that light from the volumes in VRCLV worlds. Do not include `LightVolumes.cginc` from DennokoEx: defining `VRC_LIGHT_VOLUMES_INCLUDED` also switches on lilToon's own LV code paths, overriding the user's lilToon setting. `Editor/DennokoExVRCLVDetector.cs` and `Shaders/DennokoEx_VRCLV_Bridge.cginc` are empty GUID-keeping stubs (see Removing Released Files).

### Editor Inspector (`Editor/`)

`DennokoExInspector` extends `lilToonInspector`. Key patterns:

- **Dual-property pattern for texture-gated features** (Matcap 3rd, Normal Map 3rd): `_CustomXxxUIEnabled` (shown in UI) and `_CustomXxxEnabled` (effective, drives shader). `SyncEffectiveEnabled()` keeps them in sync — the feature is only active when UI is on AND a texture is assigned. This avoids GPU branching on empty textures. Call `SyncEffectiveEnabled` whenever either the toggle or the texture changes.
- **Copy/paste buffer**: Per-section gear menu (static dictionaries `_clipFloats`, `_clipColors`, etc.). Undo is recorded via `Undo.RecordObjects` before paste.
- **Localization**: `DennokoExLanguage.Get(key)` loads `Resources/Language/<lang>.json` (keyed by lilToon's `lilLanguageManager.langSet.languageName`), falling back to `en-US`. Add new UI strings to both `en-US.json` and `ja-JP.json`.

### Mask Packing (64 texture-parameter limit)

The shader samples only `_CustomMaskPacked` (R=Refl2nd, G=Rim2nd, B=Normal3rd, A=Main4th masks); the four individual `_Custom*MaskTex` slots are authoring-only. The packed texture is a persistent PNG at `Assets/DennokoEx_Generated/PackedMasks/<input-fingerprint>.png`:

- `DennokoExMaskPacker` bakes pixels; `DennokoExPackedMaskStore.EnsureAll` names files by input fingerprint (slot GUIDs + dependency hashes + `Version`), writes missing ones and assigns the reference. It is idempotent — keep every write gated by that comparison.
- Automatic triggers are deliberately minimal (`DennokoExPackedMaskWatcher`): the inspector when a material's slot references change (checked on Repaint only), plus a one-time loaded-scene check per project (`EditorUserSettings` key `DennokoEx.PackedMaskMigration`). No processing in play mode. **Do not add asset-import (`AssetPostprocessor`), `ObjectChangeEvents`, scene-open or per-domain-reload triggers**: they made package installs, image imports and editing slow. Changes the editor does not see (source image edited/reimported, slots changed outside the inspector) are fixed manually — inspector rebuild button or *Window > DennokoEx > Packed Masks > Update All Materials* — and at the latest by the build hook.
- `EnsureAll` writes the generated PNGs' import settings (linear, BC7/ASTC, mip streaming) onto their own importers, reimporting only on mismatch. The automatic inspector path passes `checkAllImportSettings: false` (only newly written files are checked); manual operations and the build hook check every assigned file. **Never add `OnPreprocessTexture` (or any per-type postprocessor callback / `GetVersion`)**: it becomes an import dependency of every texture, so installing/updating reimports the whole project.
- `Editor/VRCSDK/DennokoExPackedMaskBuildHook` (callbackOrder 0, only with `com.vrchat.avatars`) re-checks renderer and animation-clip materials before lilToon's build step.
- Bump `DennokoExMaskPacker.Version` when baked output changes; import setting changes reach existing files through Update All Materials, the rebuild button or the build hook. Background: `Docs/Impl/mask_packing_alternatives.md` §11.

### Assembly Definition

`Editor/DennokoEx.Editor.asmdef` is Editor-only and references `lilToon.Editor`. All editor code is wrapped in `#if UNITY_EDITOR`. `Editor/VRCSDK/DennokoEx.VRCSDK.Editor.asmdef` compiles only when the VRChat Avatars SDK is installed (`versionDefines`).

### Removing Released Files

Users update by overwriting with a `.unitypackage`, which never deletes files. Do not delete a file that has shipped: replace it with an empty stub that keeps its original `.meta` (GUID), as done for `Editor/DennokoExMaskSync.cs` and `Editor/NDMF/` (the stub asmdef has an undefined `defineConstraints` symbol so no assembly is built). `.meta` files are not tracked in git, so recover GUIDs from a released install.

## Shader Property Naming Conventions

All custom properties use the prefix `_Custom` followed by the feature name and parameter:
- Reflection 2nd → `_CustomRefl2nd*`
- Rim 2nd → `_CustomRim2nd*`
- Matcap 3rd → `_CustomMatcap3rd*`
- Normal Map 3rd → `_CustomNormal3rd*`

Sampler usage: all custom textures share a single `sampler_linear_repeat` to stay within the ps_4_0 16-sampler limit.
