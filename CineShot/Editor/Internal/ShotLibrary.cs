using System;
using System.Collections.Generic;
using CineShot.Core;
using UnityEditor;
using UnityEngine;

namespace CineShot.Editor
{
    public struct ShotQuery
    {
        public string text;
        public RigKind? rig;
        /// <summary>仅匹配标签；空则不限。可用搜索框 #标签 写入。</summary>
        public string tag;
    }

    public static class ShotLibrary
    {
        /// <summary>用户可写的镜头模板目录。包内 Templates 只读，新建与变体落到工程 Resources。</summary>
        public const string UserShotFolder = "Assets/Resources/CineShot/Templates/Shots";
        public const string UserSequenceFolder = "Assets/Resources/CineShot/Templates/Sequences";

        static readonly List<ShotAsset> s_Cache = new List<ShotAsset>();
        static double s_LastRefresh;

        public static IReadOnlyList<ShotAsset> All
        {
            get
            {
                EnsureCache();
                return s_Cache;
            }
        }

        public static void Invalidate()
        {
            s_Cache.Clear();
            s_LastRefresh = 0;
        }

        public static void EnsureCache()
        {
            if (s_Cache.Count > 0 && EditorApplication.timeSinceStartup - s_LastRefresh < 2.0)
                return;
            s_Cache.Clear();
            string[] guids = AssetDatabase.FindAssets("t:ShotAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<ShotAsset>(path);
                if (asset != null)
                    s_Cache.Add(asset);
            }
            s_LastRefresh = EditorApplication.timeSinceStartup;
        }

        public static ShotAsset FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            EnsureCache();
            for (int i = 0; i < s_Cache.Count; i++)
            {
                if (s_Cache[i] != null && s_Cache[i].Id == id)
                    return s_Cache[i];
            }
            return null;
        }

        /// <summary>分镜行按镜头编号存引用；界面上用资产选择框。</summary>
        public static void ShotField(Rect rect, GUIContent label, SerializedProperty idProp)
        {
            var current = FindById(idProp.stringValue);
            var picked = (ShotAsset)EditorGUI.ObjectField(rect, label, current, typeof(ShotAsset), false);
            if (picked != current)
                idProp.stringValue = picked != null ? picked.Id : string.Empty;
        }

        public static List<ShotAsset> Query(ShotQuery q)
        {
            EnsureCache();
            var hits = new List<ShotAsset>();
            string text = q.text != null ? q.text.Trim() : string.Empty;
            string tagFilter = q.tag != null ? q.tag.Trim() : string.Empty;
            if (string.IsNullOrEmpty(tagFilter) && text.StartsWith("#", StringComparison.Ordinal))
            {
                tagFilter = text.Substring(1).Trim();
                text = string.Empty;
            }
            for (int i = 0; i < s_Cache.Count; i++)
            {
                var s = s_Cache[i];
                if (q.rig.HasValue && s.rigKind != q.rig.Value)
                    continue;
                if (!string.IsNullOrEmpty(tagFilter) && !HasTag(s.tags, tagFilter))
                    continue;
                if (!string.IsNullOrEmpty(text) && !MatchesText(s, text))
                    continue;
                hits.Add(s);
            }
            return hits;
        }

        public static bool MatchesText(ShotAsset s, string text)
        {
            if (s == null || string.IsNullOrEmpty(text))
                return true;
            string name = string.IsNullOrEmpty(s.displayName) ? s.name : s.displayName;
            if (name != null && name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (s.Id != null && s.Id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return HasTag(s.tags, text);
        }

        public static bool HasTag(string[] tags, string needle)
        {
            if (tags == null || string.IsNullOrEmpty(needle))
                return false;
            for (int i = 0; i < tags.Length; i++)
            {
                string t = tags[i];
                if (!string.IsNullOrEmpty(t) && t.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static string FormatTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return string.Empty;
            var parts = new List<string>(tags.Length);
            for (int i = 0; i < tags.Length; i++)
            {
                if (!string.IsNullOrEmpty(tags[i]))
                    parts.Add(tags[i]);
            }
            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts.ToArray());
        }

        public static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        public static ShotAsset Create(string folder, string fileName, RigKind kind)
        {
            EnsureAssetFolder(folder);
            var asset = ScriptableObject.CreateInstance<ShotAsset>();
            asset.displayName = fileName;
            asset.rigKind = kind;
            asset.rigParams = RigParams.DefaultFor(kind);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName + ".asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Invalidate();
            return asset;
        }

        public static ShotAsset DuplicateAsVariant(ShotAsset source)
        {
            if (source == null)
                return null;
            string path = AssetDatabase.GetAssetPath(source);
            string dir = WritableDirectory(System.IO.Path.GetDirectoryName(path));
            var clone = UnityEngine.Object.Instantiate(source);
            clone.parentGuid = source.Id;
            clone.displayName = source.displayName + "_v";
            EnsureAssetFolder(dir);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + clone.displayName + ".asset");
            AssetDatabase.CreateAsset(clone, newPath);
            AssetDatabase.SaveAssets();
            Invalidate();
            return clone;
        }

        static string WritableDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return UserShotFolder;
            string dir = path.Replace('\\', '/');
            if (dir.StartsWith("Packages/", StringComparison.Ordinal))
                return UserShotFolder;
            return dir;
        }

        public static int CountReferences(ShotAsset shot)
        {
            if (shot == null || string.IsNullOrEmpty(shot.Id))
                return 0;
            int count = 0;
            string[] guids = AssetDatabase.FindAssets("t:CineSequenceAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                var seq = AssetDatabase.LoadAssetAtPath<CineSequenceAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (seq == null || seq.rows == null)
                    continue;
                for (int r = 0; r < seq.rows.Count; r++)
                {
                    if (seq.rows[r] != null && seq.rows[r].shotGuid == shot.Id)
                        count++;
                }
            }
            return count;
        }
    }

    class ShotLibraryPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
        {
            ShotLibrary.Invalidate();
        }
    }
}
