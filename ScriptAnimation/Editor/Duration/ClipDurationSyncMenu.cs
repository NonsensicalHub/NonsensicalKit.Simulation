using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>Timeline 窗口当前资源一键刷新所有 ScriptAnim 轨时长。</summary>
    public static class ClipDurationSyncMenu
    {
        [MenuItem("Tools/ScriptAnimation/刷新全部 ScriptAnim 轨时长", false, 100)]
        public static void RefreshInspectedTimeline()
        {
            TimelineAsset asset = TimelineEditor.inspectedAsset;
            if (asset == null)
            {
                Debug.LogWarning("[ScriptAnim] 请先在 Timeline 窗口打开要刷新的 TimelineAsset。");
                return;
            }

            int updated = ClipDurationSync.TryApplyTimeline(
                asset, out int failed, out int trackCount);
            if (trackCount == 0)
            {
                Debug.LogWarning($"[ScriptAnim] 「{asset.name}」中没有 ScriptAnimTrack。", asset);
                return;
            }

            if (updated > 0 || failed == 0)
                Debug.Log(
                    $"[ScriptAnim] Timeline 全量时长已刷新：轨 {trackCount}，成功 {updated}，失败 {failed}",
                    asset);
            else
                Debug.LogWarning(
                    $"[ScriptAnim] Timeline 全量时长刷新失败（轨 {trackCount}，成功 {updated}，失败 {failed}）。检查各轨绑定 / 路网 / Station。",
                    asset);
        }

        [MenuItem("Tools/ScriptAnimation/刷新全部 ScriptAnim 轨时长", true, 100)]
        public static bool RefreshInspectedTimelineValidate()
        {
            return TimelineEditor.inspectedAsset != null;
        }
    }
}
