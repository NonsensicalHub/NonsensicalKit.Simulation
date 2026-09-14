namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>可按当前配置估算播放秒数，供 Timeline Clip 时长回写。</summary>
    public interface IDurationResolvable
    {
        /// <summary>无法估算时返回负值。</summary>
        float ResolveDuration(in DurationResolveContext context);
    }
}
