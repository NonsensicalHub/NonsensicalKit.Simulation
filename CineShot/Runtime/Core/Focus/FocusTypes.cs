namespace CineShot.Core
{
    public struct FocusRequest
    {
        public float Distance;
        public float Weight;
        /// <summary>焦点来源；与上一帧不同时触发拉焦过渡。</summary>
        public int SourceId;
    }

    public struct FocusState
    {
        public float Distance;
    }

    /// <summary>景深写入端。URP 实现在 Output.URP 程序集。</summary>
    public interface IFocusOutput
    {
        void Apply(in FocusState state, in FocusSettings settings);
        void Dispose();
    }

    public sealed class NullFocusOutput : IFocusOutput
    {
        public static readonly NullFocusOutput Instance = new NullFocusOutput();
        public void Apply(in FocusState state, in FocusSettings settings) { }
        public void Dispose() { }
    }
}
