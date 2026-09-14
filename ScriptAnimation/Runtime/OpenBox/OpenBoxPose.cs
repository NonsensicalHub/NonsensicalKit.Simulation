using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>纸箱开合的五个独立阶段。侧壁0=压扁 1=成型；顶/底盖角 <see cref="OpenBoxPose"/>。</summary>
    public enum OpenBoxStage
    {
        [InspectorName("侧壁")]
        Wall = 0,

        [InspectorName("底盖短边")]
        BottomShort = 1,

        [InspectorName("底盖长边")]
        BottomLong = 2,

        [InspectorName("顶盖短边")]
        TopShort = 3,

        [InspectorName("顶盖长边")]
        TopLong = 4
    }

    /// <summary>五个阶段的折叠姿态。</summary>
    [System.Serializable]
    public struct OpenBoxPose
    {
        /// <summary>
        /// 顶底盖 fold：1=闭合，2=闭合后再往里折90°，0=竖直，-1=外翻放平，-2=外翻放平后再转90°。
        /// 角度 = LerpUnclamped(flat, closed, fold)。
        /// </summary>
        public const float FlapFoldMin = -2f;
        public const float FlapFoldMax = 2f;

        public float Wall;
        public float BottomShort;
        public float BottomLong;
        public float TopShort;
        public float TopLong;

        public static float ClampFlap(float value) => Mathf.Clamp(value, FlapFoldMin, FlapFoldMax);

        /// <summary>侧壁压扁、盖板竖直（fold=0）。</summary>
        public static OpenBoxPose Flat => default;

        public static OpenBoxPose Closed => new OpenBoxPose
        {
            Wall = 1f,
            BottomShort = 1f,
            BottomLong = 1f,
            TopShort = 1f,
            TopLong = 1f
        };

        public OpenBoxPose Clamped()
        {
            return new OpenBoxPose
            {
                Wall = Mathf.Clamp01(Wall),
                BottomShort = ClampFlap(BottomShort),
                BottomLong = ClampFlap(BottomLong),
                TopShort = ClampFlap(TopShort),
                TopLong = ClampFlap(TopLong)
            };
        }

        public float Get(OpenBoxStage stage)
        {
            switch (stage)
            {
                case OpenBoxStage.Wall: return Wall;
                case OpenBoxStage.BottomShort: return BottomShort;
                case OpenBoxStage.BottomLong: return BottomLong;
                case OpenBoxStage.TopShort: return TopShort;
                case OpenBoxStage.TopLong: return TopLong;
                default: return 0f;
            }
        }

        public void Set(OpenBoxStage stage, float value)
        {
            value = stage == OpenBoxStage.Wall ? Mathf.Clamp01(value) : ClampFlap(value);
            switch (stage)
            {
                case OpenBoxStage.Wall: Wall = value; break;
                case OpenBoxStage.BottomShort: BottomShort = value; break;
                case OpenBoxStage.BottomLong: BottomLong = value; break;
                case OpenBoxStage.TopShort: TopShort = value; break;
                case OpenBoxStage.TopLong: TopLong = value; break;
            }
        }

        public static string StageLabel(OpenBoxStage stage)
        {
            switch (stage)
            {
                case OpenBoxStage.Wall: return "侧壁";
                case OpenBoxStage.BottomShort: return "底短";
                case OpenBoxStage.BottomLong: return "底长";
                case OpenBoxStage.TopShort: return "顶短";
                case OpenBoxStage.TopLong: return "顶长";
                default: return stage.ToString();
            }
        }
    }
}
