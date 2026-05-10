#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using lilToon;

namespace Dennokoworks
{
    internal static class DennokoExLanguage
    {
        static Dictionary<string, string> _table;
        static string _loadedLang;

        static void Refresh()
        {
            string lang = lilLanguageManager.langSet.languageName;
            if (_table != null && _loadedLang == lang) return;

            _loadedLang = lang;
            var asset = Resources.Load<TextAsset>("Language/" + lang);
            if (!asset) asset = Resources.Load<TextAsset>("Language/en-US");

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
