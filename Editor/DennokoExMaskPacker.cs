#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks
{
    // Packs DennokoEx's four single-channel mask textures into one RGBA texture so the runtime
    // shader declares one texture instead of four (keeping the shader under the 64 texture-parameter
    // limit). Each mask keeps its own UV/tiling because the runtime shader samples the packed texture
    // separately per channel — packing is done 1:1 with no UV transform here.
    //
    // Channel layout (must match custom.hlsl / DennokoEx_MaskPacker.shader):
    //   R = _CustomRefl2ndMaskTex   G = _CustomRim2ndMaskTex
    //   B = _CustomNormal3rdMaskTex  A = _CustomMain4thMaskTex
    public static class DennokoExMaskPacker
    {
        public const string PackedProp = "_CustomMaskPacked";

        // Channel order must match custom.hlsl and DennokoEx_MaskPacker.shader.
        public static readonly string[] SourceProps =
        {
            "_CustomRefl2ndMaskTex",   // R
            "_CustomRim2ndMaskTex",    // G
            "_CustomNormal3rdMaskTex", // B
            "_CustomMain4thMaskTex",   // A
        };

        const string PackerShader = "Hidden/dennokoworks/DennokoEx/MaskPacker";
        const int MaxSize = 2048;
        const int MinSize = 4;

        // Written to the generated texture's importer userData so we can tell our own output apart
        // from a texture the user authored, or from a copy owned by a different material.
        // Layout: "<OwnerMarkerPrefix>|<GlobalObjectId>#<source signature>"
        //   - the part before '#' identifies the owning material (see BuildOwnerMarker)
        //   - the part after '#' records which source masks the file was baked from (see SourceSignature),
        //     so both the inspector and the build pass can tell a stale file from a current one without
        //     re-baking. It has to live on the asset because an in-memory cache does not survive a
        //     domain reload, and the build pass runs in a fresh one.
        const string OwnerMarkerPrefix = "DennokoExPackedMask";
        const char   UserDataSigSeparator = '#';

        const string MigrationPrefPrefix = "DennokoEx_MaskMigration_v2_";

        // Hash128.Compute is deterministic across sessions and runtimes; string.GetHashCode is not
        // guaranteed to be (it is randomized per process on CoreCLR), which would silently re-run the
        // one-shot migration on every launch if the editor ever moves off Mono.
        public static string MigrationKey => MigrationPrefPrefix + Hash128.Compute(Application.dataPath);
        public static bool IsMigrated() => EditorPrefs.GetBool(MigrationKey, false);
        public static void SetMigrated(bool value = true) => EditorPrefs.SetBool(MigrationKey, value);

        // Returns true if the material has at least one non-default mask worth packing.
        public static bool NeedsPacking(Material m)
        {
            if (m == null) return false;
            foreach (var p in SourceProps)
                if (m.HasProperty(p) && m.GetTexture(p) != null) return true;
            return false;
        }

        // Identifies the exact set of source masks a packed file was baked from. Asset GUID rather than
        // GetInstanceID (which is session-local) plus the source's content hash, so the signature stays
        // valid across domain reloads and can be persisted on the generated asset.
        public static string SourceSignature(Material m)
        {
            if (m == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var p in SourceProps)
            {
                var t = m.HasProperty(p) ? m.GetTexture(p) : null;
                if (t == null) { sb.Append("_;"); continue; }
                var path = AssetDatabase.GetAssetPath(t);
                sb.Append(string.IsNullOrEmpty(path) ? t.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(path));
                sb.Append(':');
                try { sb.Append(t.imageContentsHash.ToString()); } catch { }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// True when the material's assigned packed mask is an asset this material generated AND was
        /// baked from the source masks the material currently holds. Callers use this to skip re-baking:
        /// the inspector so a slider drag never rewrites a 2048^2 PNG, the build pass so a stale file is
        /// never shipped.
        /// </summary>
        public static bool IsPackedMaskUpToDate(Material m)
        {
            if (m == null || !m.HasProperty(PackedProp)) return false;
            var packed = m.GetTexture(PackedProp);
            if (packed == null) return !NeedsPacking(m);
            if (!TryReadUserData(packed, out var marker, out var sig)) return false;
            return marker == BuildOwnerMarker(m) && sig == SourceSignature(m);
        }

        /// <summary>
        /// Bakes the four source slots into one packed RGBA texture, writes it as a PNG file,
        /// sets correct import settings (Linear, Streaming Mipmaps), and assigns it to the material.
        /// Returns without touching the asset database when the existing file is already up to date.
        /// </summary>
        /// <param name="force">Re-bake even when the existing file matches the current sources.</param>
        /// <param name="recordUndo">
        /// Register the material writes with Undo. False for batch/automatic passes, which would
        /// otherwise push one undo entry per material onto the user's undo stack.
        /// </param>
        public static Texture2D PackAndSaveMask(
            Material owner, Material[] assignTargets = null, bool force = false, bool recordUndo = true)
        {
            if (owner == null) return null;

            // Cheap guard that makes this safe to call from any change check: no bake, no PNG write and
            // no reimport unless the packed result would actually differ from what is already on disk.
            if (!force && IsPackedMaskUpToDate(owner))
                return owner.GetTexture(PackedProp) as Texture2D;

            if (!NeedsPacking(owner))
            {
                // If there's an existing generated mask owned by this material, clear it
                var existing = owner.GetTexture(PackedProp);
                if (existing != null && IsGeneratedMask(existing, owner))
                {
                    if (recordUndo) Undo.RecordObject(owner, $"Clear {PackedProp}");
                    owner.SetTexture(PackedProp, null);
                    EditorUtility.SetDirty(owner);
                }
                // The generated file itself is deliberately left on disk: deleting it here would leave a
                // dangling reference if the user undoes the mask removal.
                return null;
            }

            var bakedTex = Bake(owner, forBuild: false);
            if (bakedTex == null) return null;

            string ownerMarker = BuildOwnerMarker(owner);
            string userData   = ownerMarker + UserDataSigSeparator + SourceSignature(owner);
            string targetPath = null;

            try
            {
                targetPath = ResolveTargetPath(owner, ownerMarker);
                if (string.IsNullOrEmpty(targetPath)) return null;

                byte[] pngBytes = bakedTex.EncodeToPNG();

                string fullSystemPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), targetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(fullSystemPath));
                File.WriteAllBytes(fullSystemPath, pngBytes);

                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                // Reimport a second time only when the importer actually needs changing, i.e. on first
                // creation or when the recorded source signature moved on.
                var importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
                if (importer != null && ApplyImportSettings(importer, userData))
                {
                    importer.SaveAndReimport();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DennokoEx] Failed to write packed mask '{targetPath ?? "(path unresolved)"}': {e}");
                return null;
            }
            finally
            {
                Object.DestroyImmediate(bakedTex);
            }

            var savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
            if (savedTexture == null) return null;

            foreach (var target in assignTargets ?? new[] { owner })
            {
                if (target == null || !target.HasProperty(PackedProp)) continue;
                if (target.GetTexture(PackedProp) == savedTexture) continue;
                if (recordUndo) Undo.RecordObject(target, $"Set {PackedProp}");
                target.SetTexture(PackedProp, savedTexture);
                EditorUtility.SetDirty(target);
            }

            return savedTexture;
        }

        /// <summary>
        /// True when the texture is a packed mask this material generated, and is therefore safe to overwrite.
        /// </summary>
        public static bool IsGeneratedMask(Texture texture, Material owner)
        {
            if (texture == null || owner == null) return false;
            return TryReadUserData(texture, out var marker, out _) && marker == BuildOwnerMarker(owner);
        }

        // Splits a generated mask's importer userData into its owner marker and source signature.
        // Files written before the signature was introduced carry the marker alone; they are reported
        // with an empty signature, which reads as "stale" and simply triggers one re-bake.
        static bool TryReadUserData(Texture texture, out string marker, out string signature)
        {
            marker = signature = null;
            if (texture == null) return false;
            string path = AssetDatabase.GetAssetPath(texture);
            if (!IsWritableAssetPath(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || string.IsNullOrEmpty(importer.userData)) return false;
            if (!importer.userData.StartsWith(OwnerMarkerPrefix, System.StringComparison.Ordinal)) return false;

            int cut = importer.userData.IndexOf(UserDataSigSeparator);
            marker    = cut < 0 ? importer.userData : importer.userData.Substring(0, cut);
            signature = cut < 0 ? string.Empty      : importer.userData.Substring(cut + 1);
            return true;
        }

        // Below this many materials the migration finishes fast enough that a progress bar would only
        // flash - and the whole point of the migration is that the user never has to think about it.
        const int ProgressBarThreshold = 8;

        /// <summary>
        /// Scans all materials in the project and packs any DennokoEx material that needs it.
        /// Runs once per project as a silent migration from the old in-memory preview, and on demand
        /// from the menu (force: true).
        /// </summary>
        public static int BatchMigrateAll(bool force = false)
        {
            int count = 0;
            bool showedProgress = false;
            try
            {
                var guids = AssetDatabase.FindAssets("t:Material");
                var targets = new List<Material>();
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    if (!IsWritableAssetPath(path)) continue;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null || m.shader == null || !m.shader.name.Contains("dennokoworks/DennokoEx")) continue;
                    if (!NeedsPacking(m)) continue;
                    if (!force && IsPackedMaskUpToDate(m)) continue;
                    targets.Add(m);
                }

                showedProgress = targets.Count >= ProgressBarThreshold;
                for (int i = 0; i < targets.Count; i++)
                {
                    var mat = targets[i];
                    if (showedProgress)
                    {
                        EditorUtility.DisplayProgressBar("DennokoEx",
                            $"Packing masks for {mat.name} ({i + 1}/{targets.Count})...",
                            (float)i / targets.Count);
                    }
                    // recordUndo: false - a project-wide pass must not push one undo entry per material
                    // onto whatever the user was actually doing.
                    if (PackAndSaveMask(mat, null, force, recordUndo: false) != null) count++;
                }

                if (count > 0)
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[DennokoEx] Batch packed masks for {count} material(s).");
                }
            }
            finally
            {
                if (showedProgress) EditorUtility.ClearProgressBar();
                // Marked even if some materials failed: a migration that retried on every launch would
                // be far worse than one that gave up. The build pass re-bakes anything stale anyway.
                SetMigrated(true);
            }
            return count;
        }

        // Bakes a packed RGBA Texture2D from the material's four mask slots. Returns null if there is
        // nothing to pack (all slots empty) - in that case the shader's "white" default is correct.
        //
        // forBuild: the texture is shipped on the avatar rather than written out as a PNG, so it is
        // block-compressed, given mipmaps and marked for mipmap streaming here instead of by a
        // TextureImporter. An uncompressed 2048^2 RGBA32 mask costs ~16.8 MB of VRAM per material and
        // counts fully against VRChat's texture budget; BC7 / ASTC brings that to ~1/4 with mipmaps
        // included, and streaming keeps the top mips out of VRAM at distance. Without mipmaps the mask
        // aliases/shimmers at distance, the GPU always fetches full resolution, and streaming cannot
        // engage at all.
        //
        // forBuild == false is the PNG path: mipmaps and compression are the importer's job there, and
        // EncodeToPNG only ever writes mip 0, so generating them here would be wasted work.
        public static Texture2D Bake(Material m, bool forBuild = false)
        {
            if (!NeedsPacking(m)) return null;

            var texR = Get(m, SourceProps[0]);
            var texG = Get(m, SourceProps[1]);
            var texB = Get(m, SourceProps[2]);
            var texA = Get(m, SourceProps[3]);

            int size = MinSize;
            foreach (var t in new[] { texR, texG, texB, texA })
                if (t != null) size = Mathf.Max(size, Mathf.Max(t.width, t.height));
            size = Mathf.Clamp(Mathf.NextPowerOfTwo(size), MinSize, MaxSize);

            var shader = Shader.Find(PackerShader);
            if (shader == null)
            {
                Debug.LogError($"[DennokoEx] Mask packer shader '{PackerShader}' not found; cannot pack masks.");
                return null;
            }

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // Null -> Unity binds the shader's "white" default, which is the correct neutral mask value.
            if (texR != null) mat.SetTexture("_TexR", texR);
            if (texG != null) mat.SetTexture("_TexG", texG);
            if (texB != null) mat.SetTexture("_TexB", texB);
            if (texA != null) mat.SetTexture("_TexA", texA);

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prevActive = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Graphics.Blit(null, rt, mat);
                RenderTexture.active = rt;
                result = new Texture2D(size, size, TextureFormat.RGBA32, /*mipChain*/ forBuild, /*linear*/ true);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                result.Apply(/*updateMipmaps*/ forBuild, false);
                result.name = m.name + "_DnkwPackedMask";
                if (forBuild) { Compress(result); ConfigureForStreaming(result); }
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(mat);
            }
            return result;
        }

        static string ResolveTargetPath(Material owner, string ownerMarker)
        {
            if (owner.HasProperty(PackedProp))
            {
                var existingTex = owner.GetTexture(PackedProp);
                if (existingTex != null)
                {
                    string existingPath = AssetDatabase.GetAssetPath(existingTex);
                    if (IsWritableAssetPath(existingPath) && existingPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadUserData(existingTex, out var marker, out _) && marker == ownerMarker)
                        {
                            return existingPath;
                        }
                    }
                }
            }

            string baseFolder = GetTargetFolder(owner);
            string maskFolder = Path.Combine(baseFolder, "Mask").Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(maskFolder) && AssetDatabase.IsValidFolder(baseFolder))
            {
                AssetDatabase.CreateFolder(baseFolder, "Mask");
            }

            string baseFileName = $"{SanitizeFileName(owner.name)}_DnkwPackedMask.png";
            string candidate = Path.Combine(maskFolder, baseFileName).Replace("\\", "/");
            if (!IsWritableAssetPath(candidate))
            {
                Debug.LogError($"[DennokoEx] Cannot write a packed mask outside of Assets/ ('{candidate}').");
                return null;
            }

            return AssetDatabase.GenerateUniqueAssetPath(candidate);
        }

        static bool ApplyImportSettings(TextureImporter importer, string userData)
        {
            bool changed = false;

            if (importer.textureType != TextureImporterType.Default)          { importer.textureType = TextureImporterType.Default;          changed = true; }
            if (importer.sRGBTexture)                                         { importer.sRGBTexture = false; /* Linear mask */              changed = true; }
            if (importer.alphaSource != TextureImporterAlphaSource.FromInput) { importer.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (importer.alphaIsTransparency)                                 { importer.alphaIsTransparency = false;                        changed = true; }
            if (!importer.mipmapEnabled)                                      { importer.mipmapEnabled = true;                               changed = true; }
            if (!importer.streamingMipmaps)                                   { importer.streamingMipmaps = true;                            changed = true; }
            if (importer.streamingMipmapsPriority != 0)                       { importer.streamingMipmapsPriority = 0;                       changed = true; }
            if (importer.userData != userData)                                { importer.userData = userData;                                changed = true; }

            // The four channels hold four UNRELATED masks. Unity's default "Compressed" is DXT5/BC3 on
            // PC, which encodes RGB along a single line per 4x4 block, so independent R/G/B masks bleed
            // into each other. CompressedHQ selects BC7, whose partition modes keep them apart. Quest has
            // no BC7, so Android is overridden to ASTC 6x6. The encode costs a second or two, but the
            // signature guard in PackAndSaveMask means it is only paid when a mask slot actually changes.
            if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
            { importer.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; }

            var android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.format != TextureImporterFormat.ASTC_6x6 || android.maxTextureSize != MaxSize)
            {
                android.overridden     = true;
                android.format         = TextureImporterFormat.ASTC_6x6;
                android.maxTextureSize = MaxSize;
                importer.SetPlatformTextureSettings(android);
                changed = true;
            }

            return changed;
        }

        static string BuildOwnerMarker(Material owner)
        {
            return $"{OwnerMarkerPrefix}|{GlobalObjectId.GetGlobalObjectIdSlow(owner)}";
        }

        static bool IsWritableAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", System.StringComparison.Ordinal);
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Material";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return string.IsNullOrEmpty(name) ? "Material" : name;
        }

        static string GetTargetFolder(Material material)
        {
            Texture mainTex = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            if (mainTex != null)
            {
                string mainTexPath = AssetDatabase.GetAssetPath(mainTex);
                if (IsWritableAssetPath(mainTexPath))
                {
                    string dir = Path.GetDirectoryName(mainTexPath);
                    if (!string.IsNullOrEmpty(dir)) return dir.Replace("\\", "/");
                }
            }

            string matPath = AssetDatabase.GetAssetPath(material);
            if (IsWritableAssetPath(matPath))
            {
                string dir = Path.GetDirectoryName(matPath);
                if (!string.IsNullOrEmpty(dir)) return dir.Replace("\\", "/");
            }

            return "Assets";
        }

        // Block-compress in place for the build. BC7 is used on PC rather than DXT5 because the four
        // channels hold four UNRELATED masks: DXT5 encodes RGB along a single line per 4x4 block, so
        // independent R/G/B masks bleed into each other, while BC7's partition modes keep them apart.
        // Mobile (Quest) has no BC7, so ASTC 6x6 is used there. If the platform format is unavailable
        // the texture is simply left uncompressed rather than corrupted.
        static void Compress(Texture2D tex)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            bool mobile = target == BuildTarget.Android || target == BuildTarget.iOS;
            var format = mobile ? TextureFormat.ASTC_6x6 : TextureFormat.BC7;
            // Only the desktop format is checked against the editor's GPU: the mobile format is
            // encoded on the CPU for a device this machine does not have to be able to sample.
            if (!mobile && !SystemInfo.SupportsTextureFormat(format)) return;
            try
            {
                EditorUtility.CompressTexture(tex, format, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DennokoEx] Packed mask compression to {format} failed, shipping it uncompressed. {e.Message}");
            }
        }

        // Marks the baked mask for VRChat's mipmap streaming and drops its CPU-side copy from the
        // build. Both live only in the serialized object: Texture2D.streamingMipmaps is get-only in
        // the scripting API, and a texture baked at build time has no TextureImporter behind it to
        // set them the normal way, so the fields are written the same way NDMF's CheckMipStreamingPass
        // reads them back. Without this the mask is never streamed - it stays resident at full
        // resolution however far away the avatar is - and NDMF reports it as a bug in the tool that
        // generated the texture.
        //
        // m_IsReadable is cleared rather than calling Apply(_, makeNoLongerReadable: true), which
        // would free the system-memory copy before the texture has been serialized into the build.
        static void ConfigureForStreaming(Texture2D tex)
        {
            var so = new SerializedObject(tex);
            var streaming = so.FindProperty("m_StreamingMipmaps");
            // A future Unity could rename these; leave the texture untouched rather than half-set.
            if (streaming == null) return;
            streaming.boolValue = true;

            var priority = so.FindProperty("m_StreamingMipmapsPriority");
            if (priority != null) priority.intValue = 0;

            // The mask is only ever sampled by the GPU, and a Read/Write enabled texture is excluded
            // from streaming besides.
            var readable = so.FindProperty("m_IsReadable");
            if (readable != null) readable.boolValue = false;

            // Without undo: this runs on a throwaway object during the build.
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Texture Get(Material m, string prop)
            => (m.HasProperty(prop)) ? m.GetTexture(prop) : null;
    }
}
#endif
