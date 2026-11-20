using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Frame.Core.Editor
{
    /// <summary>
    /// 宿主存在 LibVLCSharp.dll 时自动加上 FRAME_HAS_LIBVLC，从而编译 Frame.Core.VLC；
    /// 未安装 VLCUnity 时去掉该宏，避免 CustomVLCPlayer 因缺引用报 CS0246。
    /// </summary>
    [InitializeOnLoad]
    internal static class FrameLibVlcDefineSync
    {
        internal const string Define = "FRAME_HAS_LIBVLC";

        static FrameLibVlcDefineSync()
        {
            EditorApplication.delayCall += Sync;
        }

        [MenuItem("Tools/Frame/Sync LibVLC Define", false, 50)]
        private static void SyncMenu()
        {
            Sync();
        }

        private static void Sync()
        {
            var hasDll = HasLibVlcSharp();
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
            {
                return;
            }

            var named = NamedBuildTarget.FromBuildTargetGroup(group);
            var symbols = PlayerSettings.GetScriptingDefineSymbols(named);
            var hasDefine = ContainsDefine(symbols, Define);
            if (hasDll == hasDefine)
            {
                return;
            }

            var next = hasDll ? AddDefine(symbols, Define) : RemoveDefine(symbols, Define);
            PlayerSettings.SetScriptingDefineSymbols(named, next);
            Debug.Log(hasDll
                ? $"Frame: 检测到 LibVLCSharp.dll，已添加 {Define}（编译 Frame.Core.VLC）。"
                : $"Frame: 未找到 LibVLCSharp.dll，已移除 {Define}（跳过 Frame.Core.VLC）。");
        }

        private static bool HasLibVlcSharp()
        {
            var guids = AssetDatabase.FindAssets("LibVLCSharp");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("LibVLCSharp.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return File.Exists(Path.Combine(Application.dataPath, "Plugins/VLCUnity/Plugins/Win64/LibVLCSharp.dll"))
                   || File.Exists(Path.Combine(Application.dataPath, "VLCUnity/Plugins/Win64/LibVLCSharp.dll"));
        }

        private static bool ContainsDefine(string symbols, string define)
        {
            if (string.IsNullOrEmpty(symbols))
            {
                return false;
            }

            foreach (var part in symbols.Split(';'))
            {
                if (part.Trim() == define)
                {
                    return true;
                }
            }

            return false;
        }

        private static string AddDefine(string symbols, string define)
        {
            if (string.IsNullOrEmpty(symbols))
            {
                return define;
            }

            return symbols.TrimEnd(';') + ";" + define;
        }

        private static string RemoveDefine(string symbols, string define)
        {
            var parts = symbols.Split(';');
            var kept = new string[parts.Length];
            var count = 0;
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0 || trimmed == define)
                {
                    continue;
                }

                kept[count++] = trimmed;
            }

            return string.Join(";", kept, 0, count);
        }
    }
}
