using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 纸箱折叠：在 <see cref="OpenBoxAnim"/> 五阶段之上，
    /// 保留总控 MasterFold 与底/顶合并滑条。
    /// Timeline 绑定本组件即可用 OpenBoxClip 自由控制五个阶段。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("ScriptAnimation/开箱总控 (FoldableCarton)")]
    public class FoldableCarton : OpenBoxAnim
    {
        [Header("合并滑条（总控或分项）")]
        [Tooltip("1=闭合；2=闭合后再往里折90°；0=竖直；-1=外翻放平；-2=外翻放平后再折90°。0~1 区间仍是短边先合、长边后合。")]
        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [SerializeField, InspectorLabel("底盖")]
        float m_bottomFold = 1f;

        [Tooltip("1=闭合；2=闭合后再往里折90°；0=竖直；-1=外翻放平；-2=外翻放平后再折90°。0~1 区间仍是短边先合、长边后合。")]
        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [SerializeField, InspectorLabel("顶盖")]
        float m_topFold = 0f;

        [Tooltip("0 压扁 → 侧壁 → 底盖(短→长) → 顶盖(短→长) → 1 全闭")]
        [Range(0f, 1f)]
        [SerializeField, InspectorLabel("主折叠")]
        float m_masterFold = 0.55f;

        [SerializeField]
        bool m_useMasterFold = true;

        [SerializeField, HideInInspector]
        bool m_independentStagesReady;

        bool m_syncingMaster;
        bool m_restUseMaster;
        float m_restMaster;
        float m_restBottom;
        float m_restTop;

        public new float WallFold
        {
            get => base.WallFold;
            set
            {
                m_useMasterFold = false;
                base.WallFold = value;
            }
        }

        public float BottomFold
        {
            get => m_bottomFold;
            set
            {
                m_useMasterFold = false;
                ApplyCombinedBottom(value);
            }
        }

        public float TopFold
        {
            get => m_topFold;
            set
            {
                m_useMasterFold = false;
                ApplyCombinedTop(value);
            }
        }

        public float MasterFold
        {
            get => m_masterFold;
            set => SetMasterFold(value);
        }

        public bool UseMasterFold
        {
            get => m_useMasterFold;
            set => m_useMasterFold = value;
        }

        public void SetFolds(float wall, float bottom, float top)
        {
            m_useMasterFold = false;
            m_bottomFold = OpenBoxPose.ClampFlap(bottom);
            m_topFold = OpenBoxPose.ClampFlap(top);
            WriteStages(wall, m_bottomFold, m_topFold);
        }

        public void SetMasterFold(float value)
        {
            m_useMasterFold = true;
            m_masterFold = Mathf.Clamp01(value);
            ResolveMaster();
        }

        public override void ApplyPose(in OpenBoxPose pose)
        {
            if (!m_syncingMaster)
                m_useMasterFold = false;
            base.ApplyPose(pose);
        }

        protected override void OnTimelinePoseApplied()
        {
            if (!m_syncingMaster)
                m_useMasterFold = false;
        }

        protected override void OnRestCaptured()
        {
            m_restUseMaster = m_useMasterFold;
            m_restMaster = m_masterFold;
            m_restBottom = m_bottomFold;
            m_restTop = m_topFold;
        }

        protected override void OnRestReverting()
        {
            m_useMasterFold = m_restUseMaster;
            m_masterFold = m_restMaster;
            m_bottomFold = m_restBottom;
            m_topFold = m_restTop;
        }

        protected override void OnEnable()
        {
            if (TimelineDriven)
            {
                base.OnEnable();
                return;
            }

            if (!m_independentStagesReady)
            {
                SyncFromMasterOrCombined();
                m_independentStagesReady = true;
            }
            else if (m_useMasterFold)
            {
                ResolveMaster();
            }

            base.OnEnable();
        }

#if UNITY_EDITOR
        protected override void OnValidatedApply()
        {
            if (TimelineDriven)
                return;
            if (m_useMasterFold)
                ResolveMaster();
            base.OnValidatedApply();
        }
#endif

        void SyncFromMasterOrCombined()
        {
            if (m_useMasterFold)
                ResolveMaster();
            else
                WriteStages(base.WallFold, m_bottomFold, m_topFold);
        }

        void ResolveMaster()
        {
            float m = m_masterFold;
            float wall = Mathf.Clamp01(m / 0.4f);
            m_bottomFold = Mathf.Clamp01((m - 0.4f) / 0.3f);
            m_topFold = Mathf.Clamp01((m - 0.7f) / 0.3f);
            m_syncingMaster = true;
            WriteStages(wall, m_bottomFold, m_topFold);
            m_syncingMaster = false;
        }

        void ApplyCombinedBottom(float value)
        {
            m_bottomFold = OpenBoxPose.ClampFlap(value);
            WriteStages(base.WallFold, m_bottomFold, m_topFold);
        }

        void ApplyCombinedTop(float value)
        {
            m_topFold = OpenBoxPose.ClampFlap(value);
            WriteStages(base.WallFold, m_bottomFold, m_topFold);
        }

        void WriteStages(float wall, float bottom, float top)
        {
            SplitFlapFold(bottom, out float bottomShort, out float bottomLong);
            SplitFlapFold(top, out float topShort, out float topLong);
            SetPose(new OpenBoxPose
            {
                Wall = Mathf.Clamp01(wall),
                BottomShort = bottomShort,
                BottomLong = bottomLong,
                TopShort = topShort,
                TopLong = topLong
            });
        }

        /// <summary>
        /// fold&lt;-1：长短边一起外翻；0~1：短边先合、长边后合；1~2：闭合后再一起往里折。
    /// </summary>
        static void SplitFlapFold(float fold, out float shortFold, out float longFold)
        {
            fold = OpenBoxPose.ClampFlap(fold);
            if (fold <= 0f)
            {
                shortFold = fold;
                longFold = fold;
                return;
            }

            if (fold <= 1f)
            {
                shortFold = Mathf.Clamp01(fold * 2f);
                longFold = Mathf.Clamp01(fold * 2f - 1f);
                return;
            }

            float extra = fold - 1f;
            shortFold = 1f + extra;
            longFold = 1f + extra;
        }
    }
}
