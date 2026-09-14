using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ScriptAnimTrackBase), true)]
    public class ScriptAnimTrackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var track = (ScriptAnimTrackBase)target;
            var director = ClipDurationSync.ResolveDirector(track);
            Object binding = director != null ? director.GetGenericBinding(track) : null;

            EditorGUILayout.Space();
            string trackHint = track switch
            {
                ScriptMovementTrack =>
                    "Movement track: PathMove family and equipment clips may mix on one track (pose chain).",
                ScriptDedicatedTrack =>
                    "Dedicated track: Fade / Blink / ObjectSwitch / RobotArm / WorldRotationLock etc. Prefer one Anim and one clip type per track instance. " +
                    "WorldRotationLock needs its own track instance overlapping the movement track.",
                _ => "ScriptAnimation track"
            };

            EditorGUILayout.HelpBox(
                trackHint + "\n" +
                "Bind the matching Actor component before adding clips.\n" +
                "World rotation lock: create another ScriptDedicatedTrack bound to WorldRotationLockAnim.\n" +
                "Post Playback State: Revert = restore start pose; LeaveAsIs = keep end pose.\n" +
                "Min clip duration / instant hold frames affect duration calculation.\n" +
                "Use the buttons below to refresh ScriptAnim clip durations by speed.",
                MessageType.Info);

            if (track is ScriptDedicatedTrack dedicated)
            {
                string mixWarning = dedicated.GetMixedClipTypesWarning();
                if (!string.IsNullOrEmpty(mixWarning))
                    EditorGUILayout.HelpBox(mixWarning, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(director == null || binding == null))
            {
                if (GUILayout.Button("Refresh this track durations"))
                {
                    int updated = ClipDurationSync.TryApplyTrack(
                        track, binding, out int failed);
                    if (updated > 0 || failed == 0)
                        Debug.Log(
                            $"[ScriptAnim] Track duration refresh: ok {updated}, fail {failed}",
                            track);
                    else
                        Debug.LogWarning(
                            $"[ScriptAnim] Track duration refresh partial: ok {updated}, fail {failed} (binding / path / Station)",
                            track);
                }
            }

            TimelineAsset asset = track.timelineAsset;
            using (new EditorGUI.DisabledScope(asset == null || director == null))
            {
                if (GUILayout.Button("Refresh all ScriptAnim tracks on Timeline"))
                {
                    int updated = ClipDurationSync.TryApplyTimeline(
                        asset, out int failed, out int trackCount);
                    if (updated > 0 || failed == 0)
                        Debug.Log(
                            $"[ScriptAnim] Timeline duration refresh: tracks {trackCount}, ok {updated}, fail {failed}",
                            asset);
                    else
                        Debug.LogWarning(
                            $"[ScriptAnim] Timeline duration refresh partial: tracks {trackCount}, ok {updated}, fail {failed}",
                            asset);
                }
            }

            if (binding == null)
                EditorGUILayout.HelpBox(
                    "Bind a ScriptAnimActor subclass.",
                    MessageType.Warning);
            else if (binding is not ScriptAnimActor)
                EditorGUILayout.HelpBox($"Unexpected binding: {binding.GetType().Name}", MessageType.Warning);
        }
    }
}
