using System;
using NonsensicalKit.ScriptAnimation;

namespace HikvisionAgv
{
    /// <summary>海康地图点位。挂在 PathNode 模块袋里，路网核心不引用本类型。</summary>
    [Serializable]
    [PathNodeFeatureMenu("海康/点位映射")]
    public sealed class HikvisionPathPointFeature : IPathNodeFeature
    {
        public string MapId;
        public int PointId;
        public float MapX;
        public float MapY;
        public int AllDir;
        public float Width;
        public float Height;
        public int RoadProperty;
    }
}
