namespace CineShot.Core
{
    /// <summary>运镜结果的写入端。CM2/CM3 各有实现，由 Output 程序集注册到 <see cref="CineOutputFactory"/>。</summary>
    public interface ICameraOutput
    {
        void Apply(in CameraPose pose);
        void SetControlling(bool controlling);
    }

    public sealed class NullCameraOutput : ICameraOutput
    {
        public static readonly NullCameraOutput Instance = new NullCameraOutput();
        public void Apply(in CameraPose pose) { }
        public void SetControlling(bool controlling) { }
    }
}
