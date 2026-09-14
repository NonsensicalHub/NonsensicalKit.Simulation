using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 直线移动 Clip：从起点直移到终点，不寻路、不改朝向。
    /// 端点可为 <see cref="ScriptAnimPoint"/>，或 Data 中的世界坐标。
    /// </summary>
    [System.Serializable]
    public class DirectMoveClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        // HideInInspector：避入 Timeline 右键生成「Add Direct Move Clip From …」等选对象菜单。        [HideInInspector]
        [Tooltip("起点（ScriptAnimPoint；也可在 Data 中改用世界坐标）")]
        public ExposedReference<ScriptAnimPoint> StartNode;

        [HideInInspector]
        [Tooltip("终点（ScriptAnimPoint；也可在 Data 中改用世界坐标）")]
        public ExposedReference<ScriptAnimPoint> EndNode;

        public DirectMoveClipData Data = new DirectMoveClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<DirectMoveBehaviour>.Create(graph);
            DirectMoveBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.StartTarget = ScriptAnimPointUtility.AsTransform(
                ScriptAnimPointUtility.Resolve(StartNode, resolver));
            behaviour.EndTarget = ScriptAnimPointUtility.AsTransform(
                ScriptAnimPointUtility.Resolve(EndNode, resolver));

            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var actor = context.TrackBinding as PathMoveActor;
            if (actor == null || Data == null)
                return -1f;

            Transform start = null;
            Transform end = null;
            if (context.Resolver != null)
            {
                start = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(StartNode, context.Resolver));
                end = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(EndNode, context.Resolver));
            }

            if (!DirectMoveSampler.TryResolveEndpoints(
                    start, end, Data, out Vector3 startPos, out Vector3 endPos, actor.PathOffset))
                return -1f;

            return DirectMoveSampler.EstimateDuration(Data, actor, startPos, endPos);
        }
    }
}
