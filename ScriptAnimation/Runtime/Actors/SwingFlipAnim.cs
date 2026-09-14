using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.ScriptAnimation
{
    public enum SwingFlipAxis
    {
        [InspectorName("X 轴")]
        X = 0,
        [InspectorName("Y 轴")]
        Y = 1,
        [InspectorName("Z 轴")]
        Z = 2,
    }

    /// <summary>
    /// 钟摆摇摆 + 翻转序列：摇摆 → 翻转 → 再摇摆 → 翻回。
    /// 可 <see cref="Trigger"/> 播放，也可绑定到 <see cref="ScriptAnimTrackBase"/> 用 <see cref="SwingFlipClip"/> 驱动。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/摇摆翻转 (SwingFlipAnim)")]
    public class SwingFlipAnim : ScriptAnimActor
    {
        [Header("目标")]
        [Tooltip("为空则使用自身 Transform")]
        [InspectorLabel("目标")]
        [SerializeField] private Transform m_target;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [Tooltip("触发后等待多少秒再开始动画，0 表示立即开始")]
        [InspectorLabel("启动延时")]
        [SerializeField] private float m_startDelay;

        [Header("摇摆")]
        [InspectorLabel("摇摆轴")]
        [SerializeField] private SwingFlipAxis m_swayAxis = SwingFlipAxis.X;
        [InspectorLabel("摇摆角度")]
        [SerializeField] private float m_swayAngle = 25f;
        [InspectorLabel("摇摆时长")]
        [SerializeField] private float m_swayDuration = 0.35f;

        [Header("翻转")]
        [InspectorLabel("翻转轴")]
        [SerializeField] private SwingFlipAxis m_flipAxis = SwingFlipAxis.Y;
        [InspectorLabel("翻转角度")]
        [SerializeField] private float m_flipAngle = 180f;
        [InspectorLabel("翻转时长")]
        [SerializeField] private float m_flipDuration = 0.5f;

        [Header("曲线")]
        [InspectorLabel("缓动曲线")]
        [SerializeField] private AnimationCurve m_ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("事件（摇摆到最大角）")]
        [Tooltip("第一次钟摆摇摆到一侧最大角时触发")]
        [InspectorLabel("首次摇摆到位")]
        [SerializeField] private UnityEvent m_onFirstSwayDone = new UnityEvent();
        [Tooltip("第二次钟摆摇摆到一侧最大角时触发")]
        [InspectorLabel("二次摇摆到位")]
        [SerializeField] private UnityEvent m_onSecondSwayDone = new UnityEvent();

        private Coroutine m_routine;
        private Quaternion m_baseLocalRotation;
        private float m_flipOffset;
        private bool m_baseCaptured;

        public bool IsPlaying => m_routine != null;

        public Transform Target => m_target != null ? m_target : transform;
        public float StartDelay => m_startDelay;
        public SwingFlipAxis SwayAxis => m_swayAxis;
        public float SwayAngle => m_swayAngle;
        public float SwayDuration => m_swayDuration;
        public SwingFlipAxis FlipAxis => m_flipAxis;
        public float FlipAngle => m_flipAngle;
        public float FlipDuration => m_flipDuration;
        public AnimationCurve Ease => m_ease;
        public float FlipOffset => m_flipOffset;
        public Quaternion BaseLocalRotation => m_baseLocalRotation;

        public UnityEvent OnFirstSwayDone => m_onFirstSwayDone;
        public UnityEvent OnSecondSwayDone => m_onSecondSwayDone;

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(SwingFlipClipData data)
        {
            if (data == null)
                return;

            data.StartDelay = m_startDelay;
            data.SwayAxis = m_swayAxis;
            data.SwayAngle = m_swayAngle;
            data.SwayDuration = m_swayDuration;
            data.FlipAxis = m_flipAxis;
            data.FlipAngle = m_flipAngle;
            data.FlipDuration = m_flipDuration;
            data.Ease = PathMoveActor.CopyCurve(m_ease);
        }

        private void Awake()
        {
            CaptureBaseIfNeeded();
        }

        /// <summary>记录当前本地旋转为基准姿态（Timeline / Trigger 共用）。</summary>
        public void CaptureBase()
        {
            m_baseLocalRotation = Target.localRotation;
            m_baseCaptured = true;
        }

        /// <summary>播放完整序列。播放中再次调用会被忽略。</summary>
        public void Trigger()
        {
            if (m_routine != null)
                return;

            CaptureBaseIfNeeded();
            m_routine = StartCoroutine(PlaySequence());
        }

        /// <summary>强制重播（打断当前动画并从基准姿态重新开始）。</summary>
        public void TriggerRestart()
        {
            Stop();
            CaptureBaseIfNeeded();
            m_flipOffset = 0f;
            ApplyPose(0f, 0f);
            m_routine = StartCoroutine(PlaySequence());
        }

        public void Stop()
        {
            if (m_routine == null)
                return;

            StopCoroutine(m_routine);
            m_routine = null;
        }

        /// <summary>按摇摆角与翻转角写入本地旋转（相对基准姿态，使用组件轴）。</summary>
        public void ApplyPose(float swayAngle, float flipAngle)
        {
            ApplyPose(swayAngle, flipAngle, m_swayAxis, m_flipAxis);
        }

        /// <summary>按摇摆角与翻转角写入本地旋转（相对基准姿态）。</summary>
        public void ApplyPose(
            float swayAngle,
            float flipAngle,
            SwingFlipAxis swayAxis,
            SwingFlipAxis flipAxis)
        {
            CaptureBaseIfNeeded();
            var euler = Vector3.zero;
            SetAxis(ref euler, swayAxis, swayAngle);
            SetAxis(ref euler, flipAxis, flipAngle);
            Target.localRotation = m_baseLocalRotation * Quaternion.Euler(euler);
        }

        /// <summary>供 Timeline 采样：写入翻转偏移并应用姿态。</summary>
        public void ApplySampledPose(
            float swayAngle,
            float flipAngle,
            SwingFlipAxis swayAxis,
            SwingFlipAxis flipAxis)
        {
            m_flipOffset = flipAngle;
            ApplyPose(swayAngle, flipAngle, swayAxis, flipAxis);
        }

        private void CaptureBaseIfNeeded()
        {
            if (m_baseCaptured)
                return;
            CaptureBase();
        }

        private IEnumerator PlaySequence()
        {
            if (m_startDelay > 0f)
                yield return new WaitForSeconds(m_startDelay);

            yield return Sway(m_onFirstSwayDone, forward: true);
            yield return FlipTo(m_flipOffset + m_flipAngle);
            yield return Sway(m_onSecondSwayDone, forward: false);
            yield return FlipTo(m_flipOffset - m_flipAngle);

            m_routine = null;
        }

        /// <summary>
        /// 一次钟摆摇摆。forward： →+angle →-angle →0；否则反方向。
    /// 到第一侧最大角时触发事件。时长按角位移比例分配（1 : 2 : 1）。
    /// </summary>
        private IEnumerator Sway(UnityEvent onExtreme, bool forward)
        {
            float t1 = m_swayDuration * 0.25f;
            float t2 = m_swayDuration * 0.5f;
            float t3 = m_swayDuration * 0.25f;
            float first = forward ? m_swayAngle : -m_swayAngle;
            float second = -first;

            yield return AnimateSway(0f, first, t1);
            onExtreme?.Invoke();
            yield return AnimateSway(first, second, t2);
            yield return AnimateSway(second, 0f, t3);
        }

        private IEnumerator FlipTo(float targetFlip)
        {
            float from = m_flipOffset;
            yield return Animate(m_flipDuration, t =>
            {
                m_flipOffset = Mathf.LerpUnclamped(from, targetFlip, t);
                ApplyPose(0f, m_flipOffset);
            });
            m_flipOffset = targetFlip;
            ApplyPose(0f, m_flipOffset);
        }

        private IEnumerator AnimateSway(float from, float to, float duration)
        {
            yield return Animate(duration, t =>
            {
                float v = Mathf.LerpUnclamped(from, to, t);
                ApplyPose(v, m_flipOffset);
            });
        }

        private IEnumerator Animate(float duration, System.Action<float> onStep)
        {
            if (duration <= 0f)
            {
                onStep?.Invoke(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float raw = Mathf.Clamp01(elapsed / duration);
                float t = m_ease != null ? m_ease.Evaluate(raw) : raw;
                onStep?.Invoke(t);
                yield return null;
            }

            onStep?.Invoke(1f);
        }

        private static void SetAxis(ref Vector3 euler, SwingFlipAxis axis, float angle)
        {
            switch (axis)
            {
                case SwingFlipAxis.X:
                    euler.x = angle;
                    break;
                case SwingFlipAxis.Y:
                    euler.y = angle;
                    break;
                case SwingFlipAxis.Z:
                    euler.z = angle;
                    break;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_startDelay = Mathf.Max(0f, m_startDelay);
            m_swayDuration = Mathf.Max(0.01f, m_swayDuration);
            m_flipDuration = Mathf.Max(0.01f, m_flipDuration);
        }
#endif
    }
}
