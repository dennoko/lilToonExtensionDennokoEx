#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using lilToon;

namespace Dennokoworks
{
    public class DennokoExInspector : lilToonInspector
    {
        // -- Reflection 2nd --
        MaterialProperty _CustomRefl2ndEnabled;
        MaterialProperty _CustomRefl2ndMaskTex;
        MaterialProperty _CustomRefl2ndColor;
        MaterialProperty _CustomRefl2ndStrength;
        MaterialProperty _CustomRefl2ndSmoothness;
        MaterialProperty _CustomRefl2ndAnisotropic;
        MaterialProperty _CustomRefl2ndAnisoPrimaryShift;
        MaterialProperty _CustomRefl2ndAnisoSecondaryColor;
        MaterialProperty _CustomRefl2ndAnisoSecondaryStrength;
        MaterialProperty _CustomRefl2ndAnisoSecondaryShift;
        MaterialProperty _CustomRefl2ndShadowAttenuation;
        MaterialProperty _CustomRefl2ndLVColorStrength;
        MaterialProperty _CustomRefl2ndMainColorStrength;
        MaterialProperty _CustomRefl2ndBlur;

        // -- Rim 2nd --
        MaterialProperty _CustomRim2ndEnabled;
        MaterialProperty _CustomRim2ndColor;
        MaterialProperty _CustomRim2ndMaskTex;
        MaterialProperty _CustomRim2ndPower;
        MaterialProperty _CustomRim2ndStrength;
        MaterialProperty _CustomRim2ndBlendMode;
        MaterialProperty _CustomRim2ndShadowAttenuation;
        MaterialProperty _CustomRim2ndMainColorStrength;
        MaterialProperty _CustomRim2ndBlur;

        // -- Normal Map 3rd --
        MaterialProperty _CustomNormal3rdEnabled;
        MaterialProperty _CustomNormal3rdUIEnabled;
        MaterialProperty _CustomNormal3rdTex;
        MaterialProperty _CustomNormal3rdMaskTex;
        MaterialProperty _CustomNormal3rdStrength;

        // -- Normal Map 1st Ext --
        MaterialProperty _CustomBump1stMaskTex;

        // -- Main Color 2nd/3rd Shadow Suppress --
        MaterialProperty _CustomMain2ndShadowDisable;
        MaterialProperty _CustomMain3rdShadowDisable;

        // -- Decal --
        MaterialProperty _CustomDecalEnabled;
        MaterialProperty _CustomDecalTex;
        MaterialProperty _CustomDecalPosX;
        MaterialProperty _CustomDecalPosY;
        MaterialProperty _CustomDecalSizeX;
        MaterialProperty _CustomDecalSizeY;
        MaterialProperty _CustomDecalAngle;
        MaterialProperty _CustomDecalMaskTex;
        MaterialProperty _CustomDecalColor;
        MaterialProperty _CustomDecalAlpha;
        MaterialProperty _CustomDecalBlendMode;
        MaterialProperty _CustomDecalShadowDisable;

        // -- Decal Matcap --
        MaterialProperty _CustomDecalMatcapEnabled;
        MaterialProperty _CustomDecalMatcapTex;
        MaterialProperty _CustomDecalMatcapColor;
        MaterialProperty _CustomDecalMatcapAlpha;
        MaterialProperty _CustomDecalMatcapBlendMode;
        MaterialProperty _CustomDecalMatcapShadowDisable;
        MaterialProperty _CustomDecalMatcapEnableLighting;

        // Foldout states
        static bool _foldRefl2nd      = false;
        static bool _foldRim2nd       = false;
        static bool _foldNormal3rd    = false;
        static bool _foldNormalExt    = false;
        static bool _foldMainShadow   = false;
        static bool _foldDecal        = false;

        // Copy/paste buffer
        static Dictionary<string, float>   _clipFloats   = new Dictionary<string, float>();
        static Dictionary<string, Color>   _clipColors   = new Dictionary<string, Color>();
        static Dictionary<string, Vector4> _clipVectors  = new Dictionary<string, Vector4>();
        static Dictionary<string, Texture> _clipTextures = new Dictionary<string, Texture>();
        static bool _clipHasContent = false;

        private const string shaderName = "dennokoworks/DennokoEx";

        static string Loc(string key) => DennokoExLanguage.Get(key);

        protected override void LoadCustomProperties(MaterialProperty[] props, Material material)
        {
            isCustomShader = true;
            ReplaceToCustomShaders();
            isShowRenderMode = !material.shader.name.Contains("Optional");

            _CustomRefl2ndEnabled              = FindProperty("_CustomRefl2ndEnabled",              props, false);
            _CustomRefl2ndMaskTex              = FindProperty("_CustomRefl2ndMaskTex",              props, false);
            _CustomRefl2ndColor                = FindProperty("_CustomRefl2ndColor",                props, false);
            _CustomRefl2ndStrength             = FindProperty("_CustomRefl2ndStrength",             props, false);
            _CustomRefl2ndSmoothness           = FindProperty("_CustomRefl2ndSmoothness",           props, false);
            _CustomRefl2ndAnisotropic          = FindProperty("_CustomRefl2ndAnisotropic",          props, false);
            _CustomRefl2ndAnisoPrimaryShift    = FindProperty("_CustomRefl2ndAnisoPrimaryShift",    props, false);
            _CustomRefl2ndAnisoSecondaryColor  = FindProperty("_CustomRefl2ndAnisoSecondaryColor",  props, false);
            _CustomRefl2ndAnisoSecondaryStrength = FindProperty("_CustomRefl2ndAnisoSecondaryStrength", props, false);
            _CustomRefl2ndAnisoSecondaryShift  = FindProperty("_CustomRefl2ndAnisoSecondaryShift",  props, false);
            _CustomRefl2ndShadowAttenuation    = FindProperty("_CustomRefl2ndShadowAttenuation",    props, false);
            _CustomRefl2ndLVColorStrength      = FindProperty("_CustomRefl2ndLVColorStrength",      props, false);
            _CustomRefl2ndMainColorStrength    = FindProperty("_CustomRefl2ndMainColorStrength",    props, false);
            _CustomRefl2ndBlur                 = FindProperty("_CustomRefl2ndBlur",                 props, false);

            _CustomRim2ndEnabled          = FindProperty("_CustomRim2ndEnabled",          props, false);
            _CustomRim2ndColor            = FindProperty("_CustomRim2ndColor",            props, false);
            _CustomRim2ndMaskTex          = FindProperty("_CustomRim2ndMaskTex",          props, false);
            _CustomRim2ndPower            = FindProperty("_CustomRim2ndPower",            props, false);
            _CustomRim2ndStrength         = FindProperty("_CustomRim2ndStrength",         props, false);
            _CustomRim2ndBlendMode        = FindProperty("_CustomRim2ndBlendMode",        props, false);
            _CustomRim2ndShadowAttenuation = FindProperty("_CustomRim2ndShadowAttenuation", props, false);
            _CustomRim2ndMainColorStrength = FindProperty("_CustomRim2ndMainColorStrength", props, false);
            _CustomRim2ndBlur              = FindProperty("_CustomRim2ndBlur",              props, false);

            _CustomNormal3rdEnabled       = FindProperty("_CustomNormal3rdEnabled",       props, false);
            _CustomNormal3rdUIEnabled     = FindProperty("_CustomNormal3rdUIEnabled",     props, false);
            _CustomNormal3rdTex           = FindProperty("_CustomNormal3rdTex",           props, false);
            _CustomNormal3rdMaskTex       = FindProperty("_CustomNormal3rdMaskTex",       props, false);
            _CustomNormal3rdStrength      = FindProperty("_CustomNormal3rdStrength",      props, false);

            _CustomBump1stMaskTex         = FindProperty("_CustomBump1stMaskTex",         props, false);

            _CustomMain2ndShadowDisable   = FindProperty("_CustomMain2ndShadowDisable",   props, false);
            _CustomMain3rdShadowDisable   = FindProperty("_CustomMain3rdShadowDisable",   props, false);

            _CustomDecalEnabled           = FindProperty("_CustomDecalEnabled",           props, false);
            _CustomDecalTex               = FindProperty("_CustomDecalTex",               props, false);
            _CustomDecalPosX              = FindProperty("_CustomDecalPosX",              props, false);
            _CustomDecalPosY              = FindProperty("_CustomDecalPosY",              props, false);
            _CustomDecalSizeX             = FindProperty("_CustomDecalSizeX",             props, false);
            _CustomDecalSizeY             = FindProperty("_CustomDecalSizeY",             props, false);
            _CustomDecalAngle             = FindProperty("_CustomDecalAngle",             props, false);
            _CustomDecalMaskTex           = FindProperty("_CustomDecalMaskTex",           props, false);
            _CustomDecalColor             = FindProperty("_CustomDecalColor",             props, false);
            _CustomDecalAlpha             = FindProperty("_CustomDecalAlpha",             props, false);
            _CustomDecalBlendMode         = FindProperty("_CustomDecalBlendMode",         props, false);
            _CustomDecalShadowDisable     = FindProperty("_CustomDecalShadowDisable",     props, false);

            _CustomDecalMatcapEnabled     = FindProperty("_CustomDecalMatcapEnabled",     props, false);
            _CustomDecalMatcapTex         = FindProperty("_CustomDecalMatcapTex",         props, false);
            _CustomDecalMatcapColor       = FindProperty("_CustomDecalMatcapColor",       props, false);
            _CustomDecalMatcapAlpha       = FindProperty("_CustomDecalMatcapAlpha",       props, false);
            _CustomDecalMatcapBlendMode   = FindProperty("_CustomDecalMatcapBlendMode",   props, false);
            _CustomDecalMatcapShadowDisable = FindProperty("_CustomDecalMatcapShadowDisable", props, false);
            _CustomDecalMatcapEnableLighting = FindProperty("_CustomDecalMatcapEnableLighting", props, false);

            // Migrate pre-UIEnabled materials
            if (_CustomNormal3rdEnabled?.floatValue > 0.5f && _CustomNormal3rdUIEnabled?.floatValue < 0.5f)
                _CustomNormal3rdUIEnabled.floatValue = 1f;
        }

        protected override void DrawCustomProperties(Material material)
        {
            // Warn when transparent / cutout mode is active — VRC upload may leave
            // the shader in an optimized state, causing the material to appear transparent.
            string sn = material.shader.name;
            if (sn.Contains("Transparent") || sn.Contains("Cutout"))
            {
                EditorGUILayout.HelpBox(Loc("warn_vrc_upload"), MessageType.Warning);
                if (GUILayout.Button(Loc("btn_refresh_shader")))
                    EditorApplication.ExecuteMenuItem("Assets/lilToon/[Shader] Refresh shaders");
                EditorGUILayout.Space(4f);
            }

            EditorGUILayout.LabelField("DennokoEx", EditorStyles.centeredGreyMiniLabel);

            // Manual mask-preview refresh (re-bakes the in-memory _CustomMaskPacked for these materials).
            if (GUILayout.Button(Loc("btn_refresh_mask_preview")))
                foreach (var t in m_MaterialEditor.targets)
                    if (t is Material mm) DennokoExMaskSync.ForceSync(mm);

            EditorGUI.BeginChangeCheck();

            DrawRefl2nd();
            DrawRim2nd();
            DrawNormal3rd();
            DrawNormalExt();
            DrawMainShadow();
            DrawDecal();

            // Rebuild the in-memory packed-mask preview when any mask slot may have changed.
            if (EditorGUI.EndChangeCheck())
                foreach (var t in m_MaterialEditor.targets)
                    if (t is Material mm) DennokoExMaskSync.Sync(mm);
        }

        // ========================================================================
        //  Helpers
        // ========================================================================
        void SyncEffectiveEnabled(MaterialProperty enabledProp, MaterialProperty uiEnabledProp, MaterialProperty texProp)
        {
            if (enabledProp == null || uiEnabledProp == null || texProp == null) return;
            float target = (uiEnabledProp.floatValue > 0.5f && texProp.textureValue != null) ? 1f : 0f;
            if (enabledProp.floatValue != target)
                enabledProp.floatValue = target;
        }

        void DrawToggle(MaterialProperty enabledProp)
        {
            if (enabledProp == null) return;
            EditorGUI.BeginChangeCheck();
            bool on = EditorGUI.ToggleLeft(
                EditorGUILayout.GetControlRect(),
                enabledProp.displayName,
                enabledProp.floatValue > 0.5f,
                lilEditorGUI.customToggleFont
            );
            if (EditorGUI.EndChangeCheck())
                enabledProp.floatValue = on ? 1f : 0f;
        }

        void Prop(MaterialProperty prop, string label)
        {
            if (prop != null) m_MaterialEditor.ShaderProperty(prop, label);
        }

        // ========================================================================
        //  Copy/Paste section menu
        // ========================================================================
        void DrawSectionMenu(MaterialProperty[] props)
        {
            var rect = GUILayoutUtility.GetLastRect();
            rect.xMin = rect.xMax - 24f;
            rect.width = 24f;

            if (GUI.Button(rect, EditorGUIUtility.IconContent("_Popup"), new GUIStyle("IconButton")))
            {
                var captured = props;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(Loc("menu_copy")),          false, () => CopySection(captured, false));
                menu.AddItem(new GUIContent(Loc("menu_copy_with_tex")), false, () => CopySection(captured, true));
                menu.AddSeparator("");
                if (_clipHasContent)
                    menu.AddItem(    new GUIContent(Loc("menu_paste")), false, () => PasteSection(captured));
                else
                    menu.AddDisabledItem(new GUIContent(Loc("menu_paste")));
                menu.ShowAsContext();
            }
        }

        void CopySection(MaterialProperty[] props, bool includeTextures)
        {
            _clipFloats.Clear();
            _clipColors.Clear();
            _clipVectors.Clear();
            _clipTextures.Clear();
            _clipHasContent = true;

            foreach (var p in props)
            {
                if (p == null) continue;
                switch (p.type)
                {
                    case MaterialProperty.PropType.Float:
                    case MaterialProperty.PropType.Range:
                        _clipFloats[p.name] = p.floatValue;
                        break;
                    case MaterialProperty.PropType.Color:
                        _clipColors[p.name] = p.colorValue;
                        break;
                    case MaterialProperty.PropType.Vector:
                        _clipVectors[p.name] = p.vectorValue;
                        break;
                    case MaterialProperty.PropType.Texture:
                        if (includeTextures)
                            _clipTextures[p.name] = p.textureValue;
                        break;
                }
            }
        }

        void PasteSection(MaterialProperty[] props)
        {
            var targets = props
                .Where(p => p != null)
                .SelectMany(p => p.targets)
                .Distinct()
                .ToArray();

            if (targets.Length > 0)
                Undo.RecordObjects(targets, "Paste DennokoEx Section");

            foreach (var p in props)
            {
                if (p == null) continue;
                switch (p.type)
                {
                    case MaterialProperty.PropType.Float:
                    case MaterialProperty.PropType.Range:
                        if (_clipFloats.TryGetValue(p.name, out float f))  p.floatValue    = f;
                        break;
                    case MaterialProperty.PropType.Color:
                        if (_clipColors.TryGetValue(p.name, out Color c))  p.colorValue    = c;
                        break;
                    case MaterialProperty.PropType.Vector:
                        if (_clipVectors.TryGetValue(p.name, out Vector4 v)) p.vectorValue = v;
                        break;
                    case MaterialProperty.PropType.Texture:
                        if (_clipTextures.TryGetValue(p.name, out Texture t)) p.textureValue = t;
                        break;
                }
            }
        }

        // ========================================================================
        //  Sections
        // ========================================================================

        // -- Reflection 2nd --
        void DrawRefl2nd()
        {
            _foldRefl2nd = Foldout(Loc("foldout_refl2nd"), _foldRefl2nd);
            DrawSectionMenu(new[] {
                _CustomRefl2ndEnabled,
                _CustomRefl2ndMaskTex,          _CustomRefl2ndColor,
                _CustomRefl2ndStrength,         _CustomRefl2ndSmoothness,
                _CustomRefl2ndBlur,             _CustomRefl2ndLVColorStrength,
                _CustomRefl2ndMainColorStrength, _CustomRefl2ndShadowAttenuation,
                _CustomRefl2ndAnisotropic,      _CustomRefl2ndAnisoPrimaryShift,
                _CustomRefl2ndAnisoSecondaryColor, _CustomRefl2ndAnisoSecondaryStrength,
                _CustomRefl2ndAnisoSecondaryShift,
            });
            if (_foldRefl2nd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                DrawToggle(_CustomRefl2ndEnabled);
                if (_CustomRefl2ndEnabled != null && _CustomRefl2ndEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    Prop(_CustomRefl2ndMaskTex,    Loc("label_mask"));
                    Prop(_CustomRefl2ndColor,      Loc("label_color"));
                    lilEditorGUI.DrawLine();
                    Prop(_CustomRefl2ndStrength,          Loc("label_strength"));
                    Prop(_CustomRefl2ndSmoothness,        Loc("label_smoothness"));
                    Prop(_CustomRefl2ndBlur,              Loc("label_blur"));
                    Prop(_CustomRefl2ndLVColorStrength,   Loc("label_lv_color_strength"));
                    Prop(_CustomRefl2ndMainColorStrength, Loc("label_main_color_strength"));
                    Prop(_CustomRefl2ndShadowAttenuation, Loc("label_shadow_attenuation"));
                    lilEditorGUI.DrawLine();

                    bool isAniso = _CustomRefl2ndAnisotropic != null && _CustomRefl2ndAnisotropic.floatValue > 0.5f;
                    if (_CustomRefl2ndAnisotropic != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        isAniso = EditorGUILayout.Toggle(Loc("label_anisotropic"), isAniso);
                        if (EditorGUI.EndChangeCheck())
                            _CustomRefl2ndAnisotropic.floatValue = isAniso ? 1f : 0f;
                    }

                    if (isAniso)
                    {
                        EditorGUILayout.LabelField(Loc("label_primary"), lilEditorGUI.boldLabel);
                        Prop(_CustomRefl2ndAnisoPrimaryShift, Loc("label_shift"));
                        lilEditorGUI.DrawLine();
                        EditorGUILayout.LabelField(Loc("label_secondary"), lilEditorGUI.boldLabel);
                        Prop(_CustomRefl2ndAnisoSecondaryColor,    Loc("label_color"));
                        Prop(_CustomRefl2ndAnisoSecondaryStrength, Loc("label_strength"));
                        Prop(_CustomRefl2ndAnisoSecondaryShift,    Loc("label_shift"));
                    }

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
        }

        // -- Rim 2nd --
        void DrawRim2nd()
        {
            _foldRim2nd = Foldout(Loc("foldout_rim2nd"), _foldRim2nd);
            DrawSectionMenu(new[] {
                _CustomRim2ndEnabled,
                _CustomRim2ndColor,            _CustomRim2ndMaskTex,
                _CustomRim2ndPower,            _CustomRim2ndBlur,
                _CustomRim2ndStrength,         _CustomRim2ndMainColorStrength,
                _CustomRim2ndShadowAttenuation, _CustomRim2ndBlendMode,
            });
            if (_foldRim2nd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                DrawToggle(_CustomRim2ndEnabled);
                if (_CustomRim2ndEnabled != null && _CustomRim2ndEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    Prop(_CustomRim2ndColor,     Loc("label_color"));
                    Prop(_CustomRim2ndMaskTex,   Loc("label_mask"));
                    lilEditorGUI.DrawLine();
                    Prop(_CustomRim2ndPower,            Loc("label_power"));
                    Prop(_CustomRim2ndBlur,             Loc("label_blur"));
                    Prop(_CustomRim2ndStrength,         Loc("label_strength"));
                    Prop(_CustomRim2ndMainColorStrength, Loc("label_main_color_strength"));
                    Prop(_CustomRim2ndShadowAttenuation, Loc("label_shadow_attenuation"));
                    if (_CustomRim2ndBlendMode != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        int mode = EditorGUILayout.Popup(Loc("label_blend_mode"),
                            (int)_CustomRim2ndBlendMode.floatValue,
                            new[] { Loc("blend_rim_add"), Loc("blend_rim_mul") });
                        if (EditorGUI.EndChangeCheck())
                            _CustomRim2ndBlendMode.floatValue = mode;
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
        }

        // -- Normal Map 3rd --
        void DrawNormal3rd()
        {
            SyncEffectiveEnabled(_CustomNormal3rdEnabled, _CustomNormal3rdUIEnabled, _CustomNormal3rdTex);
            _foldNormal3rd = Foldout(Loc("foldout_normal3rd"), _foldNormal3rd);
            DrawSectionMenu(new[] {
                _CustomNormal3rdEnabled,   _CustomNormal3rdUIEnabled,
                _CustomNormal3rdTex,       _CustomNormal3rdMaskTex,
                _CustomNormal3rdStrength,
            });
            if (_foldNormal3rd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                EditorGUI.BeginChangeCheck();
                DrawToggle(_CustomNormal3rdUIEnabled);
                if (EditorGUI.EndChangeCheck())
                    SyncEffectiveEnabled(_CustomNormal3rdEnabled, _CustomNormal3rdUIEnabled, _CustomNormal3rdTex);
                if (_CustomNormal3rdUIEnabled != null && _CustomNormal3rdUIEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    EditorGUI.BeginChangeCheck();
                    Prop(_CustomNormal3rdTex, Loc("label_normal_map"));
                    if (EditorGUI.EndChangeCheck())
                        SyncEffectiveEnabled(_CustomNormal3rdEnabled, _CustomNormal3rdUIEnabled, _CustomNormal3rdTex);
                    Prop(_CustomNormal3rdMaskTex, Loc("label_mask"));
                    lilEditorGUI.DrawLine();
                    Prop(_CustomNormal3rdStrength, Loc("label_strength"));
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
        }

        // -- Normal Map 1st Ext (mask only) --
        void DrawNormalExt()
        {
            _foldNormalExt = Foldout(Loc("foldout_normalext"), _foldNormalExt);
            DrawSectionMenu(new[] { _CustomBump1stMaskTex });
            if (_foldNormalExt)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                EditorGUILayout.LabelField(Loc("label_normal1st"), lilEditorGUI.boldLabel);
                Prop(_CustomBump1stMaskTex, Loc("label_mask"));
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndVertical();
            }
        }

        // -- Main Color 2nd/3rd Shadow Suppress --
        void DrawMainShadow()
        {
            _foldMainShadow = Foldout(Loc("foldout_main_shadow"), _foldMainShadow);
            DrawSectionMenu(new[] { _CustomMain2ndShadowDisable, _CustomMain3rdShadowDisable });
            if (_foldMainShadow)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                Prop(_CustomMain2ndShadowDisable, Loc("label_main2nd_shadow_disable"));
                Prop(_CustomMain3rdShadowDisable, Loc("label_main3rd_shadow_disable"));
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndVertical();
            }
        }

        // -- Decal --
        void DrawDecal()
        {
            _foldDecal = Foldout(Loc("foldout_decal"), _foldDecal);
            DrawSectionMenu(new[] {
                _CustomDecalEnabled,
                _CustomDecalTex,
                _CustomDecalPosX,     _CustomDecalPosY,
                _CustomDecalSizeX,    _CustomDecalSizeY,
                _CustomDecalAngle,    _CustomDecalMaskTex,
                _CustomDecalColor,    _CustomDecalAlpha,
                _CustomDecalBlendMode, _CustomDecalShadowDisable,
                _CustomDecalMatcapEnabled,
                _CustomDecalMatcapTex,    _CustomDecalMatcapColor,
                _CustomDecalMatcapAlpha,  _CustomDecalMatcapBlendMode,
                _CustomDecalMatcapShadowDisable, _CustomDecalMatcapEnableLighting,
            });
            if (!_foldDecal) return;

            // --- Base map ---
            EditorGUILayout.LabelField(Loc("label_decal_base"), lilEditorGUI.boldLabel);
            EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
            DrawToggle(_CustomDecalEnabled);
            if (_CustomDecalEnabled != null && _CustomDecalEnabled.floatValue > 0.5f)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                Prop(_CustomDecalTex, Loc("label_texture"));
                lilEditorGUI.DrawLine();
                // Position/Size/Angle layout matching lilToon's decal UI
                Prop(_CustomDecalPosX,  Loc("label_decal_pos_x"));
                Prop(_CustomDecalPosY,  Loc("label_decal_pos_y"));
                Prop(_CustomDecalSizeX, Loc("label_decal_size_x"));
                Prop(_CustomDecalSizeY, Loc("label_decal_size_y"));
                Prop(_CustomDecalAngle, Loc("label_decal_angle"));
                lilEditorGUI.DrawLine();
                Prop(_CustomDecalMaskTex,       Loc("label_decal_mask"));
                Prop(_CustomDecalColor,              Loc("label_color"));
                Prop(_CustomDecalAlpha,              Loc("label_alpha"));
                Prop(_CustomDecalShadowDisable,      Loc("label_shadow_disable"));
                Prop(_CustomDecalMatcapEnableLighting, Loc("label_enable_lighting"));
                if (_CustomDecalBlendMode != null)
                {
                    EditorGUI.BeginChangeCheck();
                    int bm = EditorGUILayout.Popup(Loc("label_blend_mode"),
                        (int)_CustomDecalBlendMode.floatValue,
                        new[] { Loc("blend_replace"), Loc("blend_add"), Loc("blend_screen"), Loc("blend_mul") });
                    if (EditorGUI.EndChangeCheck())
                        _CustomDecalBlendMode.floatValue = bm;
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            // --- Matcap ---
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(Loc("label_decal_matcap"), lilEditorGUI.boldLabel);
            EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
            DrawToggle(_CustomDecalMatcapEnabled);
            if (_CustomDecalMatcapEnabled != null && _CustomDecalMatcapEnabled.floatValue > 0.5f)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                Prop(_CustomDecalMatcapTex,   Loc("label_texture"));
                Prop(_CustomDecalMatcapColor, Loc("label_color"));
                lilEditorGUI.DrawLine();
                Prop(_CustomDecalMatcapAlpha,         Loc("label_alpha"));
                Prop(_CustomDecalMatcapShadowDisable, Loc("label_shadow_disable"));
                if (_CustomDecalMatcapBlendMode != null)
                {
                    EditorGUI.BeginChangeCheck();
                    int bm = EditorGUILayout.Popup(Loc("label_blend_mode"),
                        (int)_CustomDecalMatcapBlendMode.floatValue,
                        new[] { Loc("blend_replace"), Loc("blend_add"), Loc("blend_screen"), Loc("blend_mul") });
                    if (EditorGUI.EndChangeCheck())
                        _CustomDecalMatcapBlendMode.floatValue = bm;
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
        }

        protected override void ReplaceToCustomShaders()
        {
            lts         = Shader.Find(shaderName + "/lilToon");
            ltsc        = Shader.Find("Hidden/" + shaderName + "/Cutout");
            ltst        = Shader.Find("Hidden/" + shaderName + "/Transparent");
            ltsot       = Shader.Find("Hidden/" + shaderName + "/OnePassTransparent");
            ltstt       = Shader.Find("Hidden/" + shaderName + "/TwoPassTransparent");

            ltso        = Shader.Find("Hidden/" + shaderName + "/OpaqueOutline");
            ltsco       = Shader.Find("Hidden/" + shaderName + "/CutoutOutline");
            ltsto       = Shader.Find("Hidden/" + shaderName + "/TransparentOutline");
            ltsoto      = Shader.Find("Hidden/" + shaderName + "/OnePassTransparentOutline");
            ltstto      = Shader.Find("Hidden/" + shaderName + "/TwoPassTransparentOutline");

            ltsoo       = Shader.Find(shaderName + "/[Optional] OutlineOnly/Opaque");
            ltscoo      = Shader.Find(shaderName + "/[Optional] OutlineOnly/Cutout");
            ltstoo      = Shader.Find(shaderName + "/[Optional] OutlineOnly/Transparent");

            ltstess     = Shader.Find("Hidden/" + shaderName + "/Tessellation/Opaque");
            ltstessc    = Shader.Find("Hidden/" + shaderName + "/Tessellation/Cutout");
            ltstesst    = Shader.Find("Hidden/" + shaderName + "/Tessellation/Transparent");
            ltstessot   = Shader.Find("Hidden/" + shaderName + "/Tessellation/OnePassTransparent");
            ltstesstt   = Shader.Find("Hidden/" + shaderName + "/Tessellation/TwoPassTransparent");

            ltstesso    = Shader.Find("Hidden/" + shaderName + "/Tessellation/OpaqueOutline");
            ltstessco   = Shader.Find("Hidden/" + shaderName + "/Tessellation/CutoutOutline");
            ltstessto   = Shader.Find("Hidden/" + shaderName + "/Tessellation/TransparentOutline");
            ltstessoto  = Shader.Find("Hidden/" + shaderName + "/Tessellation/OnePassTransparentOutline");
            ltstesstto  = Shader.Find("Hidden/" + shaderName + "/Tessellation/TwoPassTransparentOutline");

            ltsl        = Shader.Find(shaderName + "/lilToonLite");
            ltslc       = Shader.Find("Hidden/" + shaderName + "/Lite/Cutout");
            ltslt       = Shader.Find("Hidden/" + shaderName + "/Lite/Transparent");
            ltslot      = Shader.Find("Hidden/" + shaderName + "/Lite/OnePassTransparent");
            ltsltt      = Shader.Find("Hidden/" + shaderName + "/Lite/TwoPassTransparent");

            ltslo       = Shader.Find("Hidden/" + shaderName + "/Lite/OpaqueOutline");
            ltslco      = Shader.Find("Hidden/" + shaderName + "/Lite/CutoutOutline");
            ltslto      = Shader.Find("Hidden/" + shaderName + "/Lite/TransparentOutline");
            ltsloto     = Shader.Find("Hidden/" + shaderName + "/Lite/OnePassTransparentOutline");
            ltsltto     = Shader.Find("Hidden/" + shaderName + "/Lite/TwoPassTransparentOutline");

            ltsref      = Shader.Find("Hidden/" + shaderName + "/Refraction");
            ltsrefb     = Shader.Find("Hidden/" + shaderName + "/RefractionBlur");
            ltsfur      = Shader.Find("Hidden/" + shaderName + "/Fur");
            ltsfurc     = Shader.Find("Hidden/" + shaderName + "/FurCutout");
            ltsfurtwo   = Shader.Find("Hidden/" + shaderName + "/FurTwoPass");
            ltsfuro     = Shader.Find(shaderName + "/[Optional] FurOnly/Transparent");
            ltsfuroc    = Shader.Find(shaderName + "/[Optional] FurOnly/Cutout");
            ltsfurotwo  = Shader.Find(shaderName + "/[Optional] FurOnly/TwoPass");
            ltsgem      = Shader.Find("Hidden/" + shaderName + "/Gem");
            ltsfs       = Shader.Find(shaderName + "/[Optional] FakeShadow");

            ltsover     = Shader.Find(shaderName + "/[Optional] Overlay");
            ltsoover    = Shader.Find(shaderName + "/[Optional] OverlayOnePass");
            ltslover    = Shader.Find(shaderName + "/[Optional] LiteOverlay");
            ltsloover   = Shader.Find(shaderName + "/[Optional] LiteOverlayOnePass");

            ltsm        = Shader.Find(shaderName + "/lilToonMulti");
            ltsmo       = Shader.Find("Hidden/" + shaderName + "/MultiOutline");
            ltsmref     = Shader.Find("Hidden/" + shaderName + "/MultiRefraction");
            ltsmfur     = Shader.Find("Hidden/" + shaderName + "/MultiFur");
            ltsmgem     = Shader.Find("Hidden/" + shaderName + "/MultiGem");
        }
    }
}
#endif
