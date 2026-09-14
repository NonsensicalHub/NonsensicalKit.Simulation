using System;
using System.Text;
using NonsensicalKit.ScriptAnimation;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NonsensicalKit.ScriptAnimation.Demo
{
    /// <summary>UI / 键盘控制折叠。</summary>
    public class FoldableCartonController : MonoBehaviour
    {
        [SerializeField] private FoldableCarton m_carton;
        [SerializeField] private Slider m_masterSlider;
        [SerializeField] private Slider m_wallSlider;
        [SerializeField] private Slider m_bottomSlider;
        [SerializeField] private Slider m_topSlider;
        [SerializeField] private Toggle m_masterToggle;
        [SerializeField] private Text m_statusText;
        [SerializeField] private float m_keyStep = 0.35f;

        private bool _syncing;
        private readonly StringBuilder _sb = new StringBuilder(160);

        private void Awake()
        {
            if (m_carton == null)
                m_carton = FindObjectOfType<FoldableCarton>();

            Wire(m_masterSlider, OnMasterChanged);
            Wire(m_wallSlider, v => OnPartChanged(v, Part.Wall));
            Wire(m_bottomSlider, v => OnPartChanged(v, Part.Bottom), OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax);
            Wire(m_topSlider, v => OnPartChanged(v, Part.Top), OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax);
            if (m_masterToggle != null)
                m_masterToggle.onValueChanged.AddListener(OnMasterToggle);
        }

        private void Start()
        {
            PullFromCarton();
            RefreshStatus();
        }

        private void Update()
        {
            if (m_carton == null)
                return;

            if (WasPressed(KeyCode.R))
            {
                ResetFolds();
                return;
            }

            if (WasPressed(KeyCode.M) && m_masterToggle != null)
            {
                m_masterToggle.isOn = !m_masterToggle.isOn;
                return;
            }

            float hold = 0f;
            if (IsHeld(KeyCode.LeftArrow) || IsHeld(KeyCode.A)) hold -= 1f;
            if (IsHeld(KeyCode.RightArrow) || IsHeld(KeyCode.D)) hold += 1f;
            if (hold == 0f)
                return;

            float delta = hold * m_keyStep * Time.deltaTime;
            if (m_carton.UseMasterFold)
            {
                m_carton.SetMasterFold(m_carton.MasterFold + delta);
                SetSlider(m_masterSlider, m_carton.MasterFold);
                SyncDependentSliders();
            }
            else
            {
                m_carton.TopFold = OpenBoxPose.ClampFlap(m_carton.TopFold + delta);
                SetSlider(m_topSlider, m_carton.TopFold);
            }

            RefreshStatus();
        }

        public void ResetFolds()
        {
            if (m_carton == null)
                return;
            m_carton.SetMasterFold(0.55f);
            if (m_masterToggle != null)
                m_masterToggle.isOn = true;
            PullFromCarton();
            RefreshStatus();
        }

        private enum Part { Wall, Bottom, Top }

        private void OnMasterChanged(float v)
        {
            if (_syncing || m_carton == null)
                return;
            m_carton.SetMasterFold(v);
            SyncDependentSliders();
            RefreshStatus();
        }

        private void OnPartChanged(float v, Part part)
        {
            if (_syncing || m_carton == null)
                return;
            m_carton.UseMasterFold = false;
            if (m_masterToggle != null)
                m_masterToggle.isOn = false;

            switch (part)
            {
                case Part.Wall: m_carton.WallFold = v; break;
                case Part.Bottom: m_carton.BottomFold = v; break;
                case Part.Top: m_carton.TopFold = v; break;
            }

            RefreshStatus();
        }

        private void OnMasterToggle(bool on)
        {
            if (m_carton == null)
                return;
            m_carton.UseMasterFold = on;
            if (on)
                m_carton.SetMasterFold(m_masterSlider != null ? m_masterSlider.value : m_carton.MasterFold);
            PullFromCarton();
            RefreshStatus();
        }

        private void PullFromCarton()
        {
            if (m_carton == null)
                return;
            _syncing = true;
            SetSlider(m_masterSlider, m_carton.MasterFold);
            SetSlider(m_wallSlider, m_carton.WallFold);
            SetSlider(m_bottomSlider, m_carton.BottomFold);
            SetSlider(m_topSlider, m_carton.TopFold);
            if (m_masterToggle != null)
                m_masterToggle.isOn = m_carton.UseMasterFold;
            _syncing = false;
        }

        private void SyncDependentSliders()
        {
            if (m_carton == null || !m_carton.UseMasterFold)
                return;
            _syncing = true;
            SetSlider(m_wallSlider, m_carton.WallFold);
            SetSlider(m_bottomSlider, m_carton.BottomFold);
            SetSlider(m_topSlider, m_carton.TopFold);
            _syncing = false;
        }

        private void RefreshStatus()
        {
            if (m_statusText == null || m_carton == null)
                return;

            var c = m_carton;
            _sb.Clear();
            _sb.Append("12面纸箱\n总进度 ").Append((c.MasterFold * 100f).ToString("0")).Append("% ")
                .Append(c.UseMasterFold ? "[总控]" : "[分项]")
                .Append("\n侧壁 ").Append((c.WallFold * 100f).ToString("0")).Append("%  底 短")
                .Append((c.BottomShortFold * 100f).ToString("0")).Append("% 长")
                .Append((c.BottomLongFold * 100f).ToString("0")).Append("%  顶 短")
                .Append((c.TopShortFold * 100f).ToString("0")).Append("% 长")
                .Append((c.TopLongFold * 100f).ToString("0")).Append("%\n[A/D] 调节  [M] 总控  [R] 重置");
            m_statusText.text = _sb.ToString();
        }

        private static void Wire(Slider slider, UnityEngine.Events.UnityAction<float> cb, float min = 0f, float max = 1f)
        {
            if (slider == null)
                return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(cb);
        }

        private static void SetSlider(Slider slider, float v)
        {
            if (slider != null)
                slider.SetValueWithoutNotify(v);
        }

        private static bool WasPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (key)
            {
                case KeyCode.R: return kb.rKey.wasPressedThisFrame;
                case KeyCode.M: return kb.mKey.wasPressedThisFrame;
                default: return false;
            }
#else
            return Input.GetKeyDown(key);
#endif
        }

        private static bool IsHeld(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (key)
            {
                case KeyCode.LeftArrow: return kb.leftArrowKey.isPressed;
                case KeyCode.RightArrow: return kb.rightArrowKey.isPressed;
                case KeyCode.A: return kb.aKey.isPressed;
                case KeyCode.D: return kb.dKey.isPressed;
                default: return false;
            }
#else
            return Input.GetKey(key);
#endif
        }
    }
}
