using NonsensicalKit.ScriptAnimation;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Demo
{
    /// <summary>
    /// 示例喷嘴运动：螺旋升降。可替换为 Timeline / 自定义动画。
    /// 只负责驱动 ArmPivot / Carriage / 喷嘴位姿，不直接生成膜。
    /// </summary>
    public class FilmWrappingNozzleMotion : MonoBehaviour
    {
        [Header("驱动对象")]
        [InspectorLabel("货物中心")]
        [Tooltip("运动绕此中心旋转；为空则用世界原点")]
        [SerializeField] private Transform m_cargo;
        [InspectorLabel("转臂枢轴")]
        [Tooltip("绕 Y 轴旋转的转臂根节点")]
        [SerializeField] private Transform m_armPivot;
        [InspectorLabel("升降滑架")]
        [Tooltip("沿转臂上下移动的滑架（可挂喷嘴）")]
        [SerializeField] private Transform m_carriage;
        [InspectorLabel("膜卷")]
        [Tooltip("可选：随进度自转的膜卷模型")]
        [SerializeField] private Transform m_filmRoll;

        [Header("螺旋路径")]
        [InspectorLabel("绕行半径")]
        [Tooltip("喷嘴水平绕行半径（米）")]
        [SerializeField] private float m_radius = 1.45f;
        [InspectorLabel("起始高度")]
        [Tooltip("相对货物中心的最低高度（米）")]
        [SerializeField] private float m_bottomY = 0.2f;
        [InspectorLabel("结束高度")]
        [Tooltip("相对货物中心的最高高度（米）")]
        [SerializeField] private float m_topY = 1.55f;
        [InspectorLabel("圈数")]
        [Tooltip("一轮运动绕行圈数")]
        [SerializeField] [Range(1f, 12f)] private float m_revolutions = 6f;
        [InspectorLabel("先升后略降")]
        [Tooltip("末段轻微回降，模拟加固")]
        [SerializeField] private bool m_upThenSlightDown = true;

        [Header("播放")]
        [InspectorLabel("开始时自动播放")]
        [SerializeField] private bool m_playOnStart = true;
        [InspectorLabel("一轮耗时(秒")]
        [SerializeField] private float m_duration = 14f;
        [InspectorLabel("循环")]
        [SerializeField] private bool m_loop = true;
        [InspectorLabel("循环时清空膜带")]
        [Tooltip("循环回到起点时是否通知 FilmRibbonBuilder 重置（避免瞬移拉丝）")]
        [SerializeField] private bool m_resetRibbonOnLoop = true;
        [InspectorLabel("膜带生成器")]
        [Tooltip("可选：循环/重置时调用 ResetWrap")]
        [SerializeField] private FilmRibbonBuilder m_ribbon;

        private float _progress;
        private bool _playing;

        public float Progress => _progress;
        public bool IsPlaying => _playing;
        public float Radius => m_radius;

        private void Start()
        {
            _playing = m_playOnStart;
            ApplyPose(0f);
        }

        private void Update()
        {
            if (!_playing)
                return;

            float speed = m_duration > 0.01f ? 1f / m_duration : 1f;
            _progress += Time.deltaTime * speed;
            if (_progress >= 1f)
            {
                if (m_loop)
                {
                    _progress = 0f;
                    if (m_resetRibbonOnLoop && m_ribbon != null)
                        m_ribbon.ResetWrap();
                }
                else
                {
                    _progress = 1f;
                    _playing = false;
                }
            }

            ApplyPose(_progress);
        }

        public void SetPlaying(bool playing) => _playing = playing;

        public void TogglePlay() => _playing = !_playing;

        public void SetProgress(float progress)
        {
            float p = Mathf.Clamp01(progress);
            if (p + 1e-6f < _progress && m_ribbon != null)
                m_ribbon.ResetWrap();
            _progress = p;
            ApplyPose(_progress);
        }

        public void ResetMotion()
        {
            _progress = 0f;
            ApplyPose(0f);
            if (m_ribbon != null)
                m_ribbon.ResetWrap();
            _playing = m_playOnStart;
        }

        public Vector3 EvaluateNozzlePosition(float progress)
        {
            progress = Mathf.Clamp01(progress);
            Vector3 center = m_cargo != null ? m_cargo.position : Vector3.zero;
            float angle = progress * m_revolutions * Mathf.PI * 2f;
            float y = EvaluateHeight(progress);
            return new Vector3(
                center.x + Mathf.Cos(angle) * m_radius,
                center.y + y,
                center.z + Mathf.Sin(angle) * m_radius);
        }

        private float EvaluateHeight(float progress)
        {
            if (!m_upThenSlightDown)
                return Mathf.Lerp(m_bottomY, m_topY, progress);

            if (progress <= 0.8f)
                return Mathf.Lerp(m_bottomY, m_topY, progress / 0.8f);

            float tDown = (progress - 0.8f) / 0.2f;
            return Mathf.Lerp(m_topY, Mathf.Lerp(m_topY, m_bottomY, 0.25f), tDown);
        }

        private void ApplyPose(float progress)
        {
            Vector3 nozzlePos = EvaluateNozzlePosition(Mathf.Max(progress, 0.0001f));
            Vector3 center = m_cargo != null ? m_cargo.position : Vector3.zero;

            if (m_armPivot != null)
            {
                Vector3 flat = nozzlePos - center;
                flat.y = 0f;
                if (flat.sqrMagnitude > 1e-6f)
                {
                    float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    m_armPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }

            if (m_carriage != null)
            {
                if (m_armPivot != null && m_carriage.IsChildOf(m_armPivot))
                {
                    var lp = m_carriage.localPosition;
                    lp.y = nozzlePos.y - m_armPivot.position.y;
                    m_carriage.localPosition = lp;
                }
                else
                {
                    m_carriage.position = nozzlePos;
                }
            }

            if (m_filmRoll != null)
                m_filmRoll.localRotation = Quaternion.Euler(progress * 360f * m_radius * 3f, 0f, 0f);
        }
    }
}
