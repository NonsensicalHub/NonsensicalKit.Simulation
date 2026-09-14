using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(WorldRotationLockClip))]
    public class WorldRotationLockClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (WorldRotationLockClip)target;
            WorldRotationLockAnim anim = ResolveBinding(clipAsset);

            using (new EditorGUI.DisabledScope(anim == null || anim.Target == null))
            {
                if (GUILayout.Button("捕获当前世界旋转"))
                {
                    if (anim != null && anim.TryGetCurrentWorldEuler(out Vector3 euler))
                    {
                        Undo.RecordObject(clipAsset, "Capture Lock Euler");
                        if (clipAsset.Data == null)
                            clipAsset.Data = new WorldRotationLockClipData();
                        clipAsset.Data.LockEulerAngles = euler;
                        EditorUtility.SetDirty(clipAsset);
                        serializedObject.Update();
                        TimelineEditor.Refresh(RefreshReason.ContentsModified);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "放在 ScriptDedicatedTrack 上，可与移动轨的移动/转向 Clip 时间重叠。\n" +
                "WorldRotationLockAnim 可挂任意物体（不必挂车体）；Target 指向要锁的节点。\n" +
                "开启时：将 Target 世界旋转维持为 Clip 上配置的欧拉角。\n" +
                "若 Target 在某移动 Actor 层级下，该移动轨位移后会再写一次（防父节点带偏）。用按钮捕获当前世界欧拉角到 Clip。",
                MessageType.Info);

            if (anim == null)
                EditorGUILayout.HelpBox("Track 未绑定 WorldRotationLockAnim。", MessageType.Warning);
            else if (anim.Target == null)
                EditorGUILayout.HelpBox("WorldRotationLockAnim.Target 未指定。", MessageType.Warning);
        }

        static WorldRotationLockAnim ResolveBinding(WorldRotationLockClip asset)
        {
            if (asset == null || TimelineEditor.inspectedDirector == null)
                return null;

            PlayableDirector director = TimelineEditor.inspectedDirector;
            foreach (PlayableBinding binding in director.playableAsset.outputs)
            {
                if (binding.sourceObject is not ScriptDedicatedTrack track)
                    continue;
                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip != null && clip.asset == asset)
                        return director.GetGenericBinding(track) as WorldRotationLockAnim;
                }
            }

            return null;
        }
    }

    [CustomTimelineEditor(typeof(WorldRotationLockClip))]
    public class WorldRotationLockClipTimelineEditor : ClipEditor
    {
        static readonly Color s_color = new Color(0.72f, 0.45f, 0.82f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_color;
            UpdateDisplayName(clip);
            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            UpdateDisplayName(clip);
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip?.asset is not WorldRotationLockClip lockClip)
                return;

            if (lockClip.Data == null)
                lockClip.Data = new WorldRotationLockClipData();

            if (clonedFrom == null &&
                TimelineEditor.inspectedDirector != null &&
                track != null)
            {
                var anim = TimelineEditor.inspectedDirector.GetGenericBinding(track) as WorldRotationLockAnim;
                if (anim != null && anim.TryGetCurrentWorldEuler(out Vector3 euler))
                    lockClip.Data.LockEulerAngles = euler;
            }

            UpdateDisplayName(clip);
        }

        static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not WorldRotationLockClip lockClip || lockClip.Data == null)
                return;

            if (!lockClip.Data.Enabled)
            {
                clip.displayName = "世界旋转锁定（关）";
                return;
            }

            Vector3 e = lockClip.Data.LockEulerAngles;
            clip.displayName = $"锁定世界旋转 ({e.x:0.#},{e.y:0.#},{e.z:0.#})";
        }
    }
}
