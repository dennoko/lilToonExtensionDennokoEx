#if UNITY_EDITOR
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

        // -- Rim 2nd --
        MaterialProperty _CustomRim2ndEnabled;
        MaterialProperty _CustomRim2ndColor;
        MaterialProperty _CustomRim2ndMaskTex;
        MaterialProperty _CustomRim2ndPower;
        MaterialProperty _CustomRim2ndStrength;
        MaterialProperty _CustomRim2ndBlendMode;
        MaterialProperty _CustomRim2ndShadowAttenuation;

        // -- Matcap 3rd --
        MaterialProperty _CustomMatcap3rdEnabled;
        MaterialProperty _CustomMatcap3rdTex;
        MaterialProperty _CustomMatcap3rdMaskTex;
        MaterialProperty _CustomMatcap3rdColor;
        MaterialProperty _CustomMatcap3rdStrength;
        MaterialProperty _CustomMatcap3rdBlendMode;
        MaterialProperty _CustomMatcap3rdShadowAttenuation;

        // -- Normal Map 3rd --
        MaterialProperty _CustomNormal3rdEnabled;
        MaterialProperty _CustomNormal3rdTex;
        MaterialProperty _CustomNormal3rdMaskTex;
        MaterialProperty _CustomNormal3rdStrength;

        // Foldout states
        static bool _foldRefl2nd    = false;
        static bool _foldRim2nd     = false;
        static bool _foldMatcap3rd  = false;
        static bool _foldNormal3rd  = false;

        private const string shaderName = "dennokoworks/DennokoEx";

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

            _CustomRim2ndEnabled          = FindProperty("_CustomRim2ndEnabled",          props, false);
            _CustomRim2ndColor            = FindProperty("_CustomRim2ndColor",            props, false);
            _CustomRim2ndMaskTex          = FindProperty("_CustomRim2ndMaskTex",          props, false);
            _CustomRim2ndPower            = FindProperty("_CustomRim2ndPower",            props, false);
            _CustomRim2ndStrength         = FindProperty("_CustomRim2ndStrength",         props, false);
            _CustomRim2ndBlendMode        = FindProperty("_CustomRim2ndBlendMode",        props, false);
            _CustomRim2ndShadowAttenuation = FindProperty("_CustomRim2ndShadowAttenuation", props, false);

            _CustomMatcap3rdEnabled       = FindProperty("_CustomMatcap3rdEnabled",       props, false);
            _CustomMatcap3rdTex           = FindProperty("_CustomMatcap3rdTex",           props, false);
            _CustomMatcap3rdMaskTex       = FindProperty("_CustomMatcap3rdMaskTex",       props, false);
            _CustomMatcap3rdColor         = FindProperty("_CustomMatcap3rdColor",         props, false);
            _CustomMatcap3rdStrength      = FindProperty("_CustomMatcap3rdStrength",      props, false);
            _CustomMatcap3rdBlendMode     = FindProperty("_CustomMatcap3rdBlendMode",     props, false);
            _CustomMatcap3rdShadowAttenuation = FindProperty("_CustomMatcap3rdShadowAttenuation", props, false);

            _CustomNormal3rdEnabled       = FindProperty("_CustomNormal3rdEnabled",       props, false);
            _CustomNormal3rdTex           = FindProperty("_CustomNormal3rdTex",           props, false);
            _CustomNormal3rdMaskTex       = FindProperty("_CustomNormal3rdMaskTex",       props, false);
            _CustomNormal3rdStrength      = FindProperty("_CustomNormal3rdStrength",      props, false);
        }

        protected override void DrawCustomProperties(Material material)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("DennokoEx", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(4);

            DrawRefl2nd();
            DrawRim2nd();
            DrawMatcap3rd();
            DrawNormal3rd();
        }

        // ========================================================================
        //  Helper: Toggle in boxOuter style (matches lilToon's ToggleLeft pattern)
        // ========================================================================
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

        // -- Reflection 2nd --
        void DrawRefl2nd()
        {
            _foldRefl2nd = Foldout("Reflection 2nd", _foldRefl2nd);
            if (_foldRefl2nd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                DrawToggle(_CustomRefl2ndEnabled);
                if (_CustomRefl2ndEnabled != null && _CustomRefl2ndEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    Prop(_CustomRefl2ndMaskTex,    "Mask");
                    Prop(_CustomRefl2ndColor,      "Color");
                    lilEditorGUI.DrawLine();
                    Prop(_CustomRefl2ndStrength,   "Strength");
                    Prop(_CustomRefl2ndSmoothness, "Smoothness");
                    lilEditorGUI.DrawLine();

                    // Anisotropic mode toggle
                    bool isAniso = _CustomRefl2ndAnisotropic != null && _CustomRefl2ndAnisotropic.floatValue > 0.5f;
                    if (_CustomRefl2ndAnisotropic != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        isAniso = EditorGUILayout.Toggle("Anisotropic Mode (Kajiya-Kay)", isAniso);
                        if (EditorGUI.EndChangeCheck())
                            _CustomRefl2ndAnisotropic.floatValue = isAniso ? 1f : 0f;
                    }

                    if (isAniso)
                    {
                        EditorGUILayout.LabelField("Primary", lilEditorGUI.boldLabel);
                        Prop(_CustomRefl2ndAnisoPrimaryShift, "Shift");
                        lilEditorGUI.DrawLine();
                        EditorGUILayout.LabelField("Secondary", lilEditorGUI.boldLabel);
                        Prop(_CustomRefl2ndAnisoSecondaryColor,    "Color");
                        Prop(_CustomRefl2ndAnisoSecondaryStrength, "Strength");
                        Prop(_CustomRefl2ndAnisoSecondaryShift,    "Shift");
                    }

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
        }

        // -- Rim 2nd --
        void DrawRim2nd()
        {
            _foldRim2nd = Foldout("Rim 2nd", _foldRim2nd);
            if (_foldRim2nd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                DrawToggle(_CustomRim2ndEnabled);
                if (_CustomRim2ndEnabled != null && _CustomRim2ndEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    Prop(_CustomRim2ndColor,     "Color");
                    Prop(_CustomRim2ndMaskTex,   "Mask");
                    lilEditorGUI.DrawLine();
                    Prop(_CustomRim2ndPower,     "Power");
                    Prop(_CustomRim2ndStrength,  "Strength");
                    Prop(_CustomRim2ndShadowAttenuation, "Shadow Attenuation");
                    if (_CustomRim2ndBlendMode != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        int mode = EditorGUILayout.Popup("Blend Mode",
                            (int)_CustomRim2ndBlendMode.floatValue,
                            new[] { "Rim Light (Add)", "Rim Shade (Multiply)" });
                        if (EditorGUI.EndChangeCheck())
                            _CustomRim2ndBlendMode.floatValue = mode;
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
        }

        // -- Matcap 3rd --
        void DrawMatcap3rd()
        {
            _foldMatcap3rd = Foldout("Matcap 3rd", _foldMatcap3rd);
            if (_foldMatcap3rd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                DrawToggle(_CustomMatcap3rdEnabled);
                if (_CustomMatcap3rdEnabled != null && _CustomMatcap3rdEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    Prop(_CustomMatcap3rdTex,      "Texture");
                    Prop(_CustomMatcap3rdMaskTex,  "Mask");
                    Prop(_CustomMatcap3rdColor,    "Color");
                    lilEditorGUI.DrawLine();
                    Prop(_CustomMatcap3rdStrength,           "Strength");
                    Prop(_CustomMatcap3rdShadowAttenuation, "Shadow Attenuation");
                    if (_CustomMatcap3rdBlendMode != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        int mode = EditorGUILayout.Popup("Blend Mode",
                            (int)_CustomMatcap3rdBlendMode.floatValue,
                            new[] { "Add", "Multiply", "Screen" });
                        if (EditorGUI.EndChangeCheck())
                            _CustomMatcap3rdBlendMode.floatValue = mode;
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
        }

        // -- Normal Map 3rd --
        void DrawNormal3rd()
        {
            _foldNormal3rd = Foldout("Normal Map 3rd", _foldNormal3rd);
            if (_foldNormal3rd)
            {
                EditorGUILayout.BeginVertical(lilEditorGUI.boxOuter);
                DrawToggle(_CustomNormal3rdEnabled);
                if (_CustomNormal3rdEnabled != null && _CustomNormal3rdEnabled.floatValue > 0.5f)
                {
                    EditorGUILayout.BeginVertical(lilEditorGUI.boxInnerHalf);
                    Prop(_CustomNormal3rdTex,     "Normal Map");
                    Prop(_CustomNormal3rdMaskTex, "Mask");
                    lilEditorGUI.DrawLine();
                    Prop(_CustomNormal3rdStrength, "Strength");
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndVertical();
            }
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