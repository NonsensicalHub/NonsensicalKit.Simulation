namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 路网节点扩展模块。实现类用 [SerializeReference] 挂在 PathNode 上，
    /// 不要做成额外 MonoBehaviour。热路径请走 <see cref="PathFeatureStore"/>，不要每帧 GetFeature。
    /// </summary>
    public interface IPathNodeFeature
    {
    }
}
