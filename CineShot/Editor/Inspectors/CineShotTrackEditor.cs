using CineShot.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CineShot.Editor
{
    [CustomEditor(typeof(CineShotTrack))]
    public sealed class CineShotTrackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.LabelField(CineHelp.Line(CineTips.Track), "CineShot 轨道", EditorStyles.boldLabel);
            DrawDefaultInspector();
            var track = (CineShotTrack)target;
            EditorGUILayout.Space(4f);
            if (GUILayout.Button("打开分镜表"))
                StoryboardWindow.Open();
            if (GUILayout.Button("按分镜表铺轨"))
            {
                var director = FindDirector(track);
                var seq = FindSequence();
                if (director != null && seq != null)
                    ShotTrackLayer.Reconcile(director, seq, ConflictPolicy.Ask);
                else
                    EditorUtility.DisplayDialog("CineShot", "需要场景中的 PlayableDirector 与 CineSequenceAsset。", "确定");
            }
            if (GUILayout.Button("校验镜头引用"))
                Validate(track);
        }

        static PlayableDirector FindDirector(TrackAsset track)
        {
            var timeline = track.timelineAsset;
            if (timeline == null)
                return null;
            var directors = Resources.FindObjectsOfTypeAll<PlayableDirector>();
            for (int i = 0; i < directors.Length; i++)
            {
                if (directors[i] != null && directors[i].playableAsset == timeline)
                    return directors[i];
            }
            return null;
        }

        static CineSequenceAsset FindSequence()
        {
            string[] guids = AssetDatabase.FindAssets("t:CineSequenceAsset");
            if (guids.Length == 0)
                return null;
            return AssetDatabase.LoadAssetAtPath<CineSequenceAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static void Validate(CineShotTrack track)
        {
            int missing = 0;
            foreach (var c in track.GetClips())
            {
                var asset = c.asset as CineShotClip;
                if (asset == null || asset.shot == null)
                    missing++;
            }
            EditorUtility.DisplayDialog("CineShot", missing == 0 ? "所有片段都指定了镜头资产。" : "未指定镜头资产的片段：" + missing + " 条（这些片段播放时会被跳过）。", "确定");
        }
    }
}
