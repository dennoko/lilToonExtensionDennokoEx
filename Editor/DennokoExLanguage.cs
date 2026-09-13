#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using lilToon;

namespace Dennokoworks
{
    internal static class DennokoExLanguage
    {
        static Dictionary<string, string> _table;
        static string _loadedLang;

        static TextAsset LoadLanguageAsset(string lang)
        {
            // DennokoExLanguage.cs の配置場所から DennokoEx の Resources/Language パスを特定
            string[] guids = AssetDatabase.FindAssets("DennokoExLanguage t:MonoScript");
            foreach (var guid in guids)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(scriptPath) != "DennokoExLanguage") continue;
                string editorDir = Path.GetDirectoryName(scriptPath);
                string packageDir = Path.GetDirectoryName(editorDir);
                if (!string.IsNullOrEmpty(packageDir))
                {
                    string assetPath = Path.Combine(packageDir, "Resources", "Language", lang + ".json").Replace('\\', '/');
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                    if (asset) return asset;
                }
            }

            // フォールバック: 標準配置パス
            var fallback = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/dennokoworks/DennokoEx/Resources/Language/{lang}.json");
            if (fallback) return fallback;

            return Resources.Load<TextAsset>("Language/" + lang);
        }

        static void Refresh()
        {
            string lang = lilLanguageManager.langSet != null ? lilLanguageManager.langSet.languageName : "ja-JP";
            if (_table != null && _loadedLang == lang) return;

            _loadedLang = lang;
            var asset = LoadLanguageAsset(lang);
            if (!asset) asset = LoadLanguageAsset("ja-JP");
            if (!asset) asset = LoadLanguageAsset("en-US");

            _table = new Dictionary<string, string>();
            if (!asset) return;

            foreach (Match m in Regex.Matches(asset.text, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\""))
                _table[m.Groups[1].Value] = m.Groups[2].Value;
        }

        public static string Get(string key)
        {
            Refresh();
            return _table != null && _table.TryGetValue(key, out var v) ? v : key;
        }
    }
}
#endif
