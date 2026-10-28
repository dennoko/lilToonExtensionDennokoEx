# DennokoEx User Manual

DennokoEx is a multi-feature shader variant extending **lilToon 2.x** to enhance the visual expressiveness of avatars, outfits, and props.  
In addition to standard lilToon features, you can configure additional layers (Reflection, Rim Light, Normal Map, Decal, etc.) and various adjustment parameters through a dedicated inspector.

> [!IMPORTANT]
> **Automatic Mask Texture Packing**
> This shader is optimized for VRChat environments.  
> To avoid exceeding the maximum number of textures supported by the shader (64 textures), the **four mask textures** (Reflection 2nd, Rim 2nd, Normal Map 3rd, Main Color 4th) are automatically **packed (combined) into a single RGBA texture** simply by assigning them to their respective slots.  
> The packed texture is saved as a PNG under `Assets/DennokoEx_Generated/PackedMasks/`, and the exact same texture is used for both editor display and avatar upload. Your original mask textures remain untouched.  
> To keep editor performance smooth, automatic packing is not triggered when editing or reimporting mask image files externally. If you edit a source image, click the "Rebuild Packed Mask" button at the top of the inspector (it is also automatically checked and packed during VRChat upload).  
> No additional packages like NDMF are required.

> [!NOTE]
> This extension shader focuses on expanding visual expression and **does not support Lite (lightweight) versions**. No Lite shader variants are provided.

---

## Overview of Additional Features

Open the material inspector and scroll down below the standard lilToon settings to find the **DennokoEx** section. From the menu button (▼) at the top-right of each section, you can copy and paste settings across materials or sections (with or without textures).

---

### 1. Reflection 2nd
Adds an independent "second specular highlight" layer. Choose between a regular (Blinn-Phong) highlight and an anisotropic highlight for hair.
The reflection follows the direction and color of lilToon's main light. In VRC Light Volumes worlds, lilToon itself derives that light from the volumes, so the reflection changes along with it.

*   **Enabled**: Enables the reflection feature.
*   **Mask**: Grayscale mask texture controlling the reflection intensity.
*   **Color**: Color of the reflection highlight.
*   **Strength**: Brightness of the reflection.
*   **Smoothness**: Sharpness of the reflection. Higher values produce smaller and sharper highlights.
*   **Blur**: Blur radius of the reflection contour. `0` creates toon-like hard edges, while `1` produces smooth gradients.
*   **Main Color Strength**: Controls how much the main texture (base color) is multiplied into the reflection highlight.
*   **Shadow Attenuation**: Controls how much the reflection highlight is attenuated (darkened) in shadowed areas.
*   **Normal Map Strength**: Blends the surface normal used for reflection calculations between geometry (mesh) normal and normal-map-perturbed normal. `0` ignores the normal map for smooth reflections, while `1` fully applies normal map bumps (default: `1`).
*   **Anisotropic Mode (Kajiya-Kay)**:
    Anisotropic reflection mode designed for rendering circular hair highlights (angel ring). Enabling this reveals the following parameters:
    *   **Primary - Shift**: Shifts the position of the primary highlight along the normal direction.
    *   **Secondary**: Configures an optional secondary highlight.
        *   **Color**: Color of the secondary highlight.
        *   **Strength**: Brightness of the secondary highlight.
        *   **Shift**: Shifts the position of the secondary highlight along the normal direction.

---

### 2. Rim 2nd
Adds an independent "second rim light" layer separate from the standard rim light.

*   **Enabled**: Enables the rim light feature.
*   **Color**: Emissive color of the rim light.
*   **Mask**: Mask texture defining the area where the rim light is applied.
*   **Power**: Falloff rate of the rim light. Higher values restrict the light to a thinner band along the silhouette.
*   **Blur**: Softness of the rim light boundary. `0` creates sharp edges, while `1` produces a smooth contour.
*   **Strength**: Brightness of the rim light.
*   **Main Color Strength**: Controls how much the main texture (base color) is multiplied into the rim light color.
*   **Shadow Attenuation**: Controls how much the rim light is attenuated (darkened) in shadowed areas.
*   **Normal Map Strength**: Blends the normal used for rim calculations between geometry normal and normal-map normal. `0` ignores normal map bumps for a smooth rim, while `1` reflects normal map details (default: `1`).
*   **Blend Mode**:
    *   **Rim Light (Add)**: Additive blending as a regular glowing rim light.
    *   **Rim Shade (Multiply)**: Multiplicative blending to emphasize shadows on the backlit side.

---

### 3. Normal Map 3rd
Adds a "third normal map layer" that blends on top of standard lilToon 1st and 2nd normal maps. Useful for layering fine details such as fabric weaves or subtle skin micro-textures.

*   **Enabled**: Enables the third normal map (automatically applies when a texture is assigned).
*   **Normal Map**: The normal map texture to apply.
*   **Mask**: Mask texture controlling the application strength.
*   **Strength**: Adjusts bump strength.
> [!NOTE]
> The third normal map is blended in tangent space after lilToon's standard normal blending is complete. This allows fine details to be overlaid without distorting existing 1st and 2nd layer features.

---

### 4. Main Color 4th
Adds an additional "main color layer" on top of lilToon's standard layers (1st–3rd). Switching the UV mode to `MatCap` allows it to act as a lightweight matcap (view-dependent coloring).

*   **Enabled**: Enables the Main Color 4th layer.
*   **Color**: Layer color (HDR supported). The alpha value acts as the overall layer opacity (blend ratio).
*   **Texture**: Main texture used for the layer. Only `RGB` channels are used as color (alpha is not used for masking).
*   **Mask**: Independent **mask texture controlling the draw area** (white applies, black masks out). This is one of the four masks automatically packed into the RGBA texture alongside Reflection 2nd and Rim 2nd.
*   **UV Mode**: UV channel used for texture sampling. Select from `UV0`, `UV1`, `UV2`, `UV3`, or `MatCap`. Choosing `MatCap` uses view-space UVs for matcap-like effects.
*   **Blend Mode**: Determines how the 4th color blends over the underlying base colors (the combined result of Main Color 1st–3rd):
    *   **Normal**: Draws on top of the underlying layers.
    *   **Add**: Additive blending.
    *   **Screen**: Screen blending.
    *   **Multiply**: Multiplicative blending.
*   **Alpha Mode**: Determines how the layer's blend ratio (Color Alpha × Mask) affects the overall material alpha (transparency). Only the masked area is affected:
    *   **Off**: Does not affect material alpha (colors only).
    *   **Replace**: Overwrites material alpha with this value.
    *   **Multiply**: Multiplies material alpha by this value.
    *   **Add**: Adds this value to material alpha.
    *   **Subtract**: Subtracts this value from material alpha.
*   **Shadow Disable**: Controls how much this layer's contribution is cancelled out in shadowed areas (`1` hides the layer inside shadows, `0` keeps it visible). Darkening blend modes like Multiply will properly cancel inside shadows as well.

> [!NOTE]
> Main Color 4th is blended at the pre-lighting stage, meaning it is subject to lighting and shading just like Main Color 2nd / 3rd. Leaving the mask unassigned (white) applies the layer across the entire mesh.

---

### 5. Decal
Adds an independent "decal" that can be freely positioned, rotated, and scaled in the 3D model's UV space.

*   **Enabled**: Enables the decal base map.
*   **Base Map**: Texture of the decal to place.
*   **X Position / Y Position**: Center coordinates in decal UV space (`0.5, 0.5` places it at the center of UV space).
*   **X Size / Y Size**: Scale factor of the decal.
*   **Angle**: Rotation angle (`-180` to `180` degrees) around the decal center.
*   **Mask (Decal UV)**: Mask texture controlling decal shape and edges.
    > [!IMPORTANT]
    > This mask is **sampled in the decal's UV space**, so moving, rotating, or scaling the decal causes the mask to transform together. This makes clipping specific parts of a decal effortless.
*   **Color**: Color tint of the decal.
*   **Alpha**: Opacity of the decal.
*   **Shadow Disable**: Adjusts how much shadow darkening is ignored (`1` displays brightly ignoring shadows).
*   **Blend Mode**:
    *   **Replace**: Overwrites base colors with the decal.
    *   **Add**: Additive blend for glowing effects.
    *   **Screen**: Soft, bright composite based on background color.
    *   **Multiply**: Multiplicative composite suitable for stains, dirt, or shading.

---

### 6. Decal Matcap
Adds a dedicated "matcap" effect that shares identical placement settings (position, rotation, size) and mask (Decal UV) with the decal above.

*   **Enabled**: Enables the decal matcap.
*   **Matcap Texture**: Texture for the matcap.
*   **Color**: Color tint of the matcap.
*   **Alpha**: Intensity and opacity of the matcap.
*   **Shadow Disable**: Controls shadow darkening cancellation for the matcap.
*   **Blend Mode**: Select from **Replace**, **Add**, **Screen**, or **Multiply** (same as Decal blend modes).

> [!TIP]
> Enabling both Decal (Base Map) and Decal Matcap allows advanced graphical effects such as "a metallic, shimmering reflection (Matcap) confined strictly within a specific decal insignia".

---

### 7. Decal Normal Map
Adds a dedicated "normal map" sharing the same placement settings (position, rotation, size) and mask (Decal UV) with the decal base map and matcap. Applies surface relief exclusively to the decal region.

*   **Enabled**: Enables decal normal map.
*   **Normal Map**: Normal map texture to apply. Adjust repetition and offset within the decal area using **Tiling / Offset**.
*   **Strength**: Bump intensity (`-2` to `2`). `0` disables, `1` standard, negative values invert bumps.

> [!NOTE]
> The decal normal map is blended in tangent space **before** reflection and lighting calculations (same blending as Normal Map 3rd). Therefore, applied bumps respond accurately to reflections and lighting. Because it shares the decal transform and mask, it stays perfectly aligned with the base map and matcap.

---

## Utility Features

### Copy & Paste
A dropdown menu (**▼**) is available in the top-right corner of each DennokoEx setting section (Reflection 2nd, Decal, etc.).
*   **Copy**: Copies numerical values (colors, strengths, etc.) to the clipboard.
*   **Copy (with Textures)**: Copies numerical values along with assigned texture references.
*   **Paste**: Applies copied parameters to another material or section instantly.

---

## Update Guide

Import (overwrite) the latest `.unitypackage` directly into your project. When updating from older versions, there is usually no need to delete existing files beforehand.

After import, masks automatically migrate to the new format. To verify all materials at once, run **`Window` > `DennokoEx` > `Packed Masks` > `Update All Materials`**.

> [!IMPORTANT]
> **VRC Light Volumes (VRCLV) integration has been removed.** The regular mode of Reflection 2nd (everything except Anisotropic Mode) no longer derives its highlight from VRCLV or light probes; it now uses a Blinn-Phong highlight from lilToon's main light. Reflections no longer look drastically different from world to world, and what you see in the editor is a reliable guide to the uploaded result.
>
> *   Materials using the regular mode will look different after updating. Readjust **Strength**, **Smoothness** and **Blur**. Anisotropic Mode is unchanged.
> *   The **LV Color Strength** and **Direct Light Blend** settings have been removed.
> *   The menu items **`Window` > `DennokoEx` > `Regenerate VRCLV Bridge`** and **`Prepare for Export`** have been removed.
> *   In VRCLV worlds, lilToon itself feeds the volumes' light into its main light, so the reflection still changes with it (unless VRC Light Volumes is disabled in lilToon's settings).

---

## Troubleshooting

### Errors After Update / DennokoEx Settings Not Appearing
If old files remain after an overwrite import, causing compilation errors (red errors in Console) or hiding the DennokoEx section in the material inspector, reinstall following these steps:

1.  In the Project window, delete the **`Assets/dennokoworks/DennokoEx`** folder.
2.  Import the latest `.unitypackage`.
3.  If needed, run **`Window` > `DennokoEx` > `Packed Masks` > `Update All Materials`**.

> [!NOTE]
> Between steps 1 and 2, materials using DennokoEx may appear pink (missing shader), but they will restore automatically after import. Do not edit or save scenes/materials during this interval.  
> You do **not** need to delete the **`Assets/DennokoEx_Generated`** folder that stores generated packed masks.

### Mask Changes Not Reflected / Showing Outdated Masks
The four masks (Reflection 2nd, Rim 2nd, Normal Map 3rd, Main Color 4th) are **packed (combined) into a single RGBA texture** to avoid exceeding the shader's 64-texture limit. The packed texture is saved as a PNG in `Assets/DennokoEx_Generated/PackedMasks/` and referenced by the material.

To minimize editor overhead (avoiding freezes and long background scans during project imports and editing), automatic processing triggers are strictly limited:

*   **When packed masks are automatically created/updated**:
    *   When assigning, changing, pasting, or undoing mask slots in the material inspector (during inspector repaint)
    *   Initial migration from older versions (checked automatically once per project)
    *   Prior to uploading an avatar to VRChat (verified during avatar build hook; ungenerated or mismatched masks are resolved automatically)

*   **Cases where automatic updates do NOT occur (manual rebuild required)**:
    The following operations do not trigger background asset scans and will not update packed masks automatically:
    *   Editing and overwriting source mask image files in external image editors, or reimporting textures in Unity
    *   Modifying texture import settings (compression format, max size, etc.)
    *   Changing material slot references via scripts without opening the inspector

Once generated, packed textures are stored in the project and will not disconnect during uploads, editor restarts, or `.unitypackage` export/import. When assigning new masks, texture compression may take a few seconds.

*   **Manual Update Instructions**:
    *   **Update Selected Material**: Click the **`Rebuild Packed Mask`** button at the very top of the **DennokoEx** section in the material inspector. This regenerates the packed texture from the current mask images (hover over the button to view a descriptive tooltip).
    *   **Batch Update Project Materials**: From the top menu, execute **`Window` > `DennokoEx` > `Packed Masks` > `Update All Materials`**. This scans all DennokoEx materials and updates packed masks and import settings to their latest state. This is especially useful after migrating from an older version or replacing multiple source images.

> [!NOTE]
> Each time mask textures are replaced, a new PNG is created; unused older files are not deleted automatically. You can safely clean up unreferenced files by running **`Window` > `DennokoEx` > `Packed Masks` > `Delete Unused Generated Masks`** (only deletes files not referenced by any saved material).
>
> When distributing materials via `.unitypackage`, include the generated PNGs inside `Assets/DennokoEx_Generated/PackedMasks/` (enabling "Include dependencies" during export includes them automatically). Even if omitted, masks will be automatically regenerated on the recipient's machine when the material is inspected or uploaded.

### Material Appears Transparent or Broken After VRChat Upload
During upload, lilToon regenerates shaders with all features enabled. Previously, this could exceed the 64-texture limit, making materials appear completely transparent in the editor after upload. Mask packing now keeps texture counts within limits, preventing this issue under normal circumstances.

If a material still appears transparent or missing textures, follow these troubleshooting steps:
*   **Solution**:
    In Unity's top menu, click **`Assets` > `lilToon` > `[Shader] Refresh shaders`**. lilToon will rebuild shaders with current settings, restoring the appearance.
*   If the issue persists, open the affected material's inspector and click the **`Rebuild Packed Mask`** button to verify mask references.

> [!NOTE]
> In older versions, a warning box and "Fix Shader (Refresh Shader)" button appeared in the inspector; this has been removed. The same operation can be performed via the lilToon menu described above.
