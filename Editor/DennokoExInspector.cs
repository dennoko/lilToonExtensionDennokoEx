using UnityEditor;
using UnityEngine;

namespace Dennokoworks
{
    public class DennokoExInspector : ShaderGUI
    {
        // ── Reflection 2nd ──────────────────────────────────────────────────
        MaterialProperty _CustomRefl2ndEnabled;
        MaterialProperty _CustomRefl2ndTex;
        MaterialProperty _CustomRefl2ndMaskTex;
        MaterialProperty _CustomRefl2ndColor;
        MaterialProperty _CustomRefl2ndStrength;
        MaterialProperty _CustomRefl2ndAnisotropy;
        MaterialProperty _CustomRefl2ndAnisotropyAngle;

        // ── Rim 2nd ─────────────────────────────────────────────────────────
        MaterialProperty _CustomRim2ndEnabled;
        MaterialProperty _CustomRim2ndColor;
        MaterialProperty _CustomRim2ndMaskTex;
        MaterialProperty _CustomRim2ndPower;
        MaterialProperty _CustomRim2ndStrength;
        MaterialProperty _CustomRim2ndBlendMode;

        // ── Matcap 3rd ──────────────────────────────────────────────────────
        MaterialProperty _CustomMatcap3rdEnabled;
        MaterialProperty _CustomMatcap3rdTex;
        MaterialProperty _CustomMatcap3rdMaskTex;
        MaterialProperty _CustomMatcap3rdColor;
        MaterialProperty _CustomMatcap3rdStrength;
        MaterialProperty _CustomMatcap3rdBlendMode;

        // ── Extra Decal ─────────────────────────────────────────────────────
        MaterialProperty _CustomDecalEnabled;
        MaterialProperty _CustomDecalSharedMaskTex;
        MaterialProperty _CustomDecalTex;
        MaterialProperty _CustomDecalNormalTex;
        MaterialProperty _CustomDecalNormalStrength;
        MaterialProperty _CustomDecalMatcapTex;
        MaterialProperty _CustomDecalMatcapStrength;
        MaterialProperty _CustomDecalColor;
        MaterialProperty _CustomDecalTiling;
        MaterialProperty _CustomDecalBlendMode;

        static bool _foldRefl2nd    = true;
        static bool _foldRim2nd     = true;
        static bool _foldMatcap3rd  = true;
        static bool _foldDecal      = true;

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
        {
            LoadProperties(props);

            base.OnGUI(editor, props);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("─── DennokoEx ───────────────────────────────────", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(4);

            DrawRefl2nd(editor);
            DrawRim2nd(editor);
            DrawMatcap3rd(editor);
            DrawDecal(editor);

            EditorGUILayout.Space(4);
            editor.RenderQueueField();
            editor.EnableInstancingField();
        }

        void LoadProperties(MaterialProperty[] props)
        {
            _CustomRefl2ndEnabled        = Find("_CustomRefl2ndEnabled",        props);
            _CustomRefl2ndTex            = Find("_CustomRefl2ndTex",            props);
            _CustomRefl2ndMaskTex        = Find("_CustomRefl2ndMaskTex",        props);
            _CustomRefl2ndColor          = Find("_CustomRefl2ndColor",          props);
            _CustomRefl2ndStrength       = Find("_CustomRefl2ndStrength",       props);
            _CustomRefl2ndAnisotropy     = Find("_CustomRefl2ndAnisotropy",     props);
            _CustomRefl2ndAnisotropyAngle= Find("_CustomRefl2ndAnisotropyAngle",props);

            _CustomRim2ndEnabled         = Find("_CustomRim2ndEnabled",         props);
            _CustomRim2ndColor           = Find("_CustomRim2ndColor",           props);
            _CustomRim2ndMaskTex         = Find("_CustomRim2ndMaskTex",         props);
            _CustomRim2ndPower           = Find("_CustomRim2ndPower",           props);
            _CustomRim2ndStrength        = Find("_CustomRim2ndStrength",        props);
            _CustomRim2ndBlendMode       = Find("_CustomRim2ndBlendMode",       props);

            _CustomMatcap3rdEnabled      = Find("_CustomMatcap3rdEnabled",      props);
            _CustomMatcap3rdTex          = Find("_CustomMatcap3rdTex",          props);
            _CustomMatcap3rdMaskTex      = Find("_CustomMatcap3rdMaskTex",      props);
            _CustomMatcap3rdColor        = Find("_CustomMatcap3rdColor",        props);
            _CustomMatcap3rdStrength     = Find("_CustomMatcap3rdStrength",     props);
            _CustomMatcap3rdBlendMode    = Find("_CustomMatcap3rdBlendMode",    props);

            _CustomDecalEnabled          = Find("_CustomDecalEnabled",          props);
            _CustomDecalSharedMaskTex    = Find("_CustomDecalSharedMaskTex",    props);
            _CustomDecalTex              = Find("_CustomDecalTex",              props);
            _CustomDecalNormalTex        = Find("_CustomDecalNormalTex",        props);
            _CustomDecalNormalStrength   = Find("_CustomDecalNormalStrength",   props);
            _CustomDecalMatcapTex        = Find("_CustomDecalMatcapTex",        props);
            _CustomDecalMatcapStrength   = Find("_CustomDecalMatcapStrength",   props);
            _CustomDecalColor            = Find("_CustomDecalColor",            props);
            _CustomDecalTiling           = Find("_CustomDecalTiling",           props);
            _CustomDecalBlendMode        = Find("_CustomDecalBlendMode",        props);
        }

        MaterialProperty Find(string name, MaterialProperty[] props) =>
            FindProperty(name, props, false);

        // ── セクション描画ヘルパー ─────────────────────────────────────────

        static bool DrawSectionHeader(string label, ref bool fold, MaterialProperty enabledProp, MaterialEditor editor)
        {
            EditorGUILayout.BeginHorizontal();
            fold = EditorGUILayout.Foldout(fold, label, true, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (enabledProp != null)
            {
                EditorGUI.BeginChangeCheck();
                bool on = EditorGUILayout.Toggle(enabledProp.floatValue > 0.5f, GUILayout.Width(24));
                if (EditorGUI.EndChangeCheck())
                    enabledProp.floatValue = on ? 1f : 0f;
            }
            EditorGUILayout.EndHorizontal();
            return enabledProp != null && enabledProp.floatValue > 0.5f;
        }

        // ── 反射2nd ────────────────────────────────────────────────────────

        void DrawRefl2nd(MaterialEditor editor)
        {
            bool on = DrawSectionHeader("反射 2nd (異方性)", ref _foldRefl2nd, _CustomRefl2ndEnabled, editor);
            if (!_foldRefl2nd) return;
            EditorGUI.BeginDisabledGroup(!on);
            EditorGUI.indentLevel++;
            Prop(editor, _CustomRefl2ndTex,             "Texture");
            Prop(editor, _CustomRefl2ndMaskTex,         "Mask");
            Prop(editor, _CustomRefl2ndColor,            "Color");
            Prop(editor, _CustomRefl2ndStrength,         "Strength");
            Prop(editor, _CustomRefl2ndAnisotropy,       "Anisotropy");
            Prop(editor, _CustomRefl2ndAnisotropyAngle,  "Anisotropy Angle (rad)");
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space(4);
        }

        // ── リム2nd ────────────────────────────────────────────────────────

        void DrawRim2nd(MaterialEditor editor)
        {
            bool on = DrawSectionHeader("リム 2nd", ref _foldRim2nd, _CustomRim2ndEnabled, editor);
            if (!_foldRim2nd) return;
            EditorGUI.BeginDisabledGroup(!on);
            EditorGUI.indentLevel++;
            Prop(editor, _CustomRim2ndColor,     "Color");
            Prop(editor, _CustomRim2ndMaskTex,   "Mask");
            Prop(editor, _CustomRim2ndPower,     "Power");
            Prop(editor, _CustomRim2ndStrength,  "Strength");
            if (_CustomRim2ndBlendMode != null)
            {
                EditorGUI.BeginChangeCheck();
                int mode = EditorGUILayout.Popup("Blend Mode",
                    (int)_CustomRim2ndBlendMode.floatValue,
                    new[] { "Rim Light (Add)", "Rim Shade (Multiply)" });
                if (EditorGUI.EndChangeCheck())
                    _CustomRim2ndBlendMode.floatValue = mode;
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space(4);
        }

        // ── Matcap 3rd ────────────────────────────────────────────────────

        void DrawMatcap3rd(MaterialEditor editor)
        {
            bool on = DrawSectionHeader("Matcap 3rd", ref _foldMatcap3rd, _CustomMatcap3rdEnabled, editor);
            if (!_foldMatcap3rd) return;
            EditorGUI.BeginDisabledGroup(!on);
            EditorGUI.indentLevel++;
            Prop(editor, _CustomMatcap3rdTex,     "Texture");
            Prop(editor, _CustomMatcap3rdMaskTex, "Mask");
            Prop(editor, _CustomMatcap3rdColor,   "Color");
            Prop(editor, _CustomMatcap3rdStrength,"Strength");
            if (_CustomMatcap3rdBlendMode != null)
            {
                EditorGUI.BeginChangeCheck();
                int mode = EditorGUILayout.Popup("Blend Mode",
                    (int)_CustomMatcap3rdBlendMode.floatValue,
                    new[] { "Add", "Multiply", "Screen" });
                if (EditorGUI.EndChangeCheck())
                    _CustomMatcap3rdBlendMode.floatValue = mode;
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space(4);
        }

        // ── 追加デカール ───────────────────────────────────────────────────

        void DrawDecal(MaterialEditor editor)
        {
            bool on = DrawSectionHeader("追加デカール", ref _foldDecal, _CustomDecalEnabled, editor);
            if (!_foldDecal) return;
            EditorGUI.BeginDisabledGroup(!on);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("共通マスク", EditorStyles.miniLabel);
            Prop(editor, _CustomDecalSharedMaskTex, "Shared Mask (R)");

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("デカールカラー", EditorStyles.miniLabel);
            Prop(editor, _CustomDecalTex,       "Texture");
            Prop(editor, _CustomDecalColor,     "Color");
            if (_CustomDecalTiling != null)
                editor.ShaderProperty(_CustomDecalTiling, "UV (XY=Tiling ZW=Offset)");
            if (_CustomDecalBlendMode != null)
            {
                EditorGUI.BeginChangeCheck();
                int mode = EditorGUILayout.Popup("Blend Mode",
                    (int)_CustomDecalBlendMode.floatValue,
                    new[] { "Normal (Alpha)", "Add", "Multiply" });
                if (EditorGUI.EndChangeCheck())
                    _CustomDecalBlendMode.floatValue = mode;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("追加ノーマル", EditorStyles.miniLabel);
            Prop(editor, _CustomDecalNormalTex,      "Normal Map");
            Prop(editor, _CustomDecalNormalStrength, "Normal Strength");

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("デカール Matcap", EditorStyles.miniLabel);
            Prop(editor, _CustomDecalMatcapTex,      "Matcap Texture");
            Prop(editor, _CustomDecalMatcapStrength, "Matcap Strength");

            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space(4);
        }

        static void Prop(MaterialEditor editor, MaterialProperty prop, string label)
        {
            if (prop != null) editor.ShaderProperty(prop, label);
        }
    }
}
