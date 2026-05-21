using System;
using System.Collections.Generic;
using NonsensicalKit.Tools.InputTool;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

public class InputSimulator
{
    private Mouse _mouse;
    private Keyboard _keyboard;

    private Vector2 _lastMousePos;

    // ═══════════════════════════════════════════════════════════
    //  JS MouseEvent.button → 单次触发时哪个按钮引起了事件
    //    0=左, 1=中, 2=右, 3=后退, 4=前进
    //  JS MouseEvent.buttons → 当前按下的按钮位掩码（持续状态）
    //    bit0(1)=左, bit1(2)=右, bit2(4)=中, bit3(8)=后退, bit4(16)=前进
    // ═══════════════════════════════════════════════════════════
    private static readonly int[] ButtonBitMasks = { 1, 4, 2, 8, 16 };
    //                         对应 button index:  0  1  2  3   4
    //                         即: 左 中 右 后退 前进

    public void Init(Mouse mouse, Keyboard keyboard, bool enableInputHubControl)
    {
        _mouse = mouse;
        _keyboard = keyboard;
        if (enableInputHubControl)
        {
            InitInputHub();
        }
    }

    private void InitInputHub()
    {
        OnMousePosChanged = InputHub.Instance.DataInjection.MousePosChanged;
        OnMouseMoveChanged = InputHub.Instance.DataInjection.MouseMoveChanged;
        OnZoomChanged = InputHub.Instance.DataInjection.ZoomChanged;

        OnMouseLeftButtonDown = InputHub.Instance.DataInjection.MouseLeftButtonDown;
        OnMouseLeftButtonUp = InputHub.Instance.DataInjection.MouseLeftButtonUp;
        OnMouseRightButtonDown = InputHub.Instance.DataInjection.MouseRightButtonDown;
        OnMouseRightButtonUp = InputHub.Instance.DataInjection.MouseRightButtonUp;
        OnMouseMiddleButtonDown = InputHub.Instance.DataInjection.MouseMiddleButtonDown;
        OnMouseMiddleButtonUp = InputHub.Instance.DataInjection.MouseMiddleButtonUp;

        OnMoveChanged = InputHub.Instance.DataInjection.MoveChanged;

        OnSpaceKeyEnter = InputHub.Instance.DataInjection.SpaceKeyEnter;
        OnFKeyEnter = InputHub.Instance.DataInjection.FKeyEnter;
        OnLeftShiftKeyChanged = LeftShiftKeyChanged;
        OnLeftAltKeyChanged = LeftAltKeyChanged;
        return;

        void LeftShiftKeyChanged(bool enter)
        {
            if (enter)
            {
                InputHub.Instance.DataInjection.LeftShiftKeyEnter();
            }
            else
            {
                InputHub.Instance.DataInjection.LeftShiftKeyLeave();
            }
        }

        void LeftAltKeyChanged(bool enter)
        {
            if (enter)
            {
                InputHub.Instance.DataInjection.LeftAltKeyEnter();
            }
            else
            {
                InputHub.Instance.DataInjection.LeftAltKeyLeave();
            }
        }
    }

    public void SimulateInput(SerializedInputEvent data)
    {
        switch (data.type)
        {
            case "mousemove": OnMouseMove(data); break;
            case "mousedown": OnMouseDown(data); break;
            case "mouseup": OnMouseUp(data); break;
            case "click": OnClick(data); break;
            case "dblclick": OnDblClick(data); break;
            case "wheel": OnWheel(data); break;
            case "keydown": OnKey(data, true); break;
            case "keyup": OnKey(data, false); break;
        }
        // 注意：不再调用 InputSystem.Update()
        // QueueEvent 的事件会在 PlayerLoop 中自动刷新，
        // 手动 Update 会阻塞主线程并打断批处理窗口。
    }

    //  鼠标移动
    private void OnMouseMove(SerializedInputEvent data)
    {
        var crtPos = ConvertPos(data);
        var delta = crtPos - _lastMousePos;
        using (StateEvent.From(_mouse, out var e))
        {
            _mouse.position.WriteValueIntoEvent(crtPos, e);
            _mouse.delta.WriteValueIntoEvent(delta, e);
            InputSystem.QueueEvent(e);
        }

        OnMousePosChanged?.Invoke(crtPos);
        OnMouseMoveChanged?.Invoke(_mouse.delta.ReadValue());

        _lastMousePos = crtPos;
    }

    //  鼠标按下
    private void OnMouseDown(SerializedInputEvent data)
    {
        var lastMousePos = ConvertPos(data);
        var btn = ResolveButton(data.button, data.buttons, pressed: true);

        using (StateEvent.From(_mouse, out var e))
        {
            _mouse.position.WriteValueIntoEvent(lastMousePos, e);
            btn.WriteValueIntoEvent(1f, e);
            InputSystem.QueueEvent(e);
        }

        switch (btn.name)
        {
            case "leftButton":
                OnMouseLeftButtonDown?.Invoke();
                break;
            case "rightButton":
                OnMouseRightButtonDown?.Invoke();
                break;
            case "middleButton":
                OnMouseMiddleButtonDown?.Invoke();
                break;
            default:
                break;
        }
    }

    //  鼠标松开
    private void OnMouseUp(SerializedInputEvent data)
    {
        var lastMousePos = ConvertPos(data);
        var btn = ResolveButton(data.button, data.buttons, pressed: false);

        using (StateEvent.From(_mouse, out var e))
        {
            _mouse.position.WriteValueIntoEvent(lastMousePos, e);
            btn.WriteValueIntoEvent(0f, e);
            InputSystem.QueueEvent(e);
        }

        switch (btn.name)
        {
            case "leftButton":
                OnMouseLeftButtonUp?.Invoke();
                break;
            case "rightButton":
                OnMouseRightButtonUp?.Invoke();
                break;
            case "middleButton":
                OnMouseMiddleButtonUp?.Invoke();
                break;
            default:
                break;
        }
    }

    //  单击 / 双击 — 共享逻辑，只差异步帧数
    private void OnClick(SerializedInputEvent data)
    {
        var btn = ResolveButton(data.button, data.buttons, pressed: true);
        QueueButtonCycle(btn);
    }

    private void OnDblClick(SerializedInputEvent data)
    {
        var btn = ResolveButton(data.button, data.buttons, pressed: true);
        QueueButtonCycle(btn);
        QueueButtonCycle(btn);
    }

    /// <summary>
    /// 一个完整的 按下→松开 周期，位置只写一次
    /// </summary>
    private void QueueButtonCycle(ButtonControl btn)
    {
        // 按下
        using (var down = StateEvent.From(_mouse, out var e1))
        {
            _mouse.position.WriteValueIntoEvent(_lastMousePos, e1);
            btn.WriteValueIntoEvent(1f, e1);
            InputSystem.QueueEvent(e1);
        }

        // 松开
        using (var up = StateEvent.From(_mouse, out var e2))
        {
            _mouse.position.WriteValueIntoEvent(_lastMousePos, e2);
            btn.WriteValueIntoEvent(0f, e2);
            InputSystem.QueueEvent(e2);
        }
    }

    //  滚轮
    private void OnWheel(SerializedInputEvent data)
    {
        // 浏览器 delta 通常 ~100/格，Unity 期望 ~0.1~1.0
        // Y 轴方向相反（浏览器向下为正，Unity 向下为负）
        var scroll = new Vector2(
            data.deltaX,
            -data.deltaY
        );

        using (StateEvent.From(_mouse, out var e))
        {
            _mouse.scroll.WriteValueIntoEvent(scroll, e);
            InputSystem.QueueEvent(e);
        }

        OnZoomChanged?.Invoke(scroll.y);
    }

    //  键盘 — 所有字段写入同一 StateEvent
    private void OnKey(SerializedInputEvent data, bool pressed)
    {
        if (!KeyMap.TryGetValue(data.code, out var key)) return;

        using (StateEvent.From(_keyboard, out var e))
        {
            _keyboard.ctrlKey.WriteValueIntoEvent(data.ctrlKey ? 1f : 0f, e);
            _keyboard.shiftKey.WriteValueIntoEvent(data.shiftKey ? 1f : 0f, e);
            _keyboard.altKey.WriteValueIntoEvent(data.altKey ? 1f : 0f, e);
            _keyboard[key].WriteValueIntoEvent(pressed ? 1f : 0f, e);
            InputSystem.QueueEvent(e);
        }

        switch (key, pressed)
        {
            case (Key.Space, true):
                OnSpaceKeyEnter?.Invoke();
                break;
            case (Key.F, true):
                OnFKeyEnter?.Invoke();
                break;
            case (Key.LeftShift, _):
                OnLeftShiftKeyChanged?.Invoke(pressed);
                break;
            case (Key.LeftAlt, _):
                OnLeftAltKeyChanged?.Invoke(pressed);
                break;
            case (Key.W, _):
            case (Key.UpArrow, _):
                OnMoveChanged?.Invoke(new Vector2(0f, pressed ? 1f : 0f));
                break;
            case (Key.S, _):
            case (Key.DownArrow, _):
                OnMoveChanged?.Invoke(new Vector2(0f, pressed ? -1f : 0f));
                break;
            case (Key.A, _):
            case (Key.LeftArrow, _):
                OnMoveChanged?.Invoke(new Vector2(pressed ? -1f : 0f, 0f));
                break;
            case (Key.D, _):
            case (Key.RightArrow, _):
                OnMoveChanged?.Invoke(new Vector2(pressed ? 1f : 0f, 0f));
                break;


            default: break;
        }
    }

    //  按钮解析
    /// <summary>
    /// 综合 button 和 buttons 确定目标按钮。
    ///
    /// 策略：
    ///   pressed=true  (mousedown/click) → 优先用 button（刚按下的那个）
    ///   pressed=false (mouseup)         → 优先用 button（刚松开的那个）
    ///   任何情况下 button 无效时 → 回退到 buttons 位掩码取最低位
    /// </summary>
    private ButtonControl ResolveButton(int button, int buttons, bool pressed)
    {
        // 1. button 有效时直接映射（mousedown/mouseup 的标准用法）
        if (button is >= 0 and <= 4)
            return IndexToButton(button);

        // 2. 回退：从 buttons 位掩码中提取最低位的按下按钮
        if (buttons > 0)
        {
            for (int i = 0; i < ButtonBitMasks.Length; i++)
            {
                if ((buttons & ButtonBitMasks[i]) != 0)
                    return IndexToButton(i);
            }
        }

        // 3. 兜底
        return _mouse.leftButton;
    }

    private ButtonControl IndexToButton(int index) => index switch
    {
        0 => _mouse.leftButton,
        1 => _mouse.middleButton,
        2 => _mouse.rightButton,
        3 => _mouse.backButton,
        4 => _mouse.forwardButton,
        _ => _mouse.leftButton,
    };


    // 坐标转换
    private Vector2 ConvertPos(SerializedInputEvent data)
    {
        float x = data.clientX / data.viewportWidth * Screen.width;
        float y = data.clientY / data.viewportHeight * Screen.height;
        return new Vector2(x, Screen.height - y);
    }


    /// 按键映射表（不变）
    private static readonly Dictionary<string, Key> KeyMap = new()
    {
        // 字母
        { "KeyA", Key.A }, { "KeyB", Key.B }, { "KeyC", Key.C }, { "KeyD", Key.D },
        { "KeyE", Key.E }, { "KeyF", Key.F }, { "KeyG", Key.G }, { "KeyH", Key.H },
        { "KeyI", Key.I }, { "KeyJ", Key.J }, { "KeyK", Key.K }, { "KeyL", Key.L },
        { "KeyM", Key.M }, { "KeyN", Key.N }, { "KeyO", Key.O }, { "KeyP", Key.P },
        { "KeyQ", Key.Q }, { "KeyR", Key.R }, { "KeyS", Key.S }, { "KeyT", Key.T },
        { "KeyU", Key.U }, { "KeyV", Key.V }, { "KeyW", Key.W }, { "KeyX", Key.X },
        { "KeyY", Key.Y }, { "KeyZ", Key.Z },
        // 数字
        { "Digit0", Key.Digit0 }, { "Digit1", Key.Digit1 }, { "Digit2", Key.Digit2 },
        { "Digit3", Key.Digit3 }, { "Digit4", Key.Digit4 }, { "Digit5", Key.Digit5 },
        { "Digit6", Key.Digit6 }, { "Digit7", Key.Digit7 }, { "Digit8", Key.Digit8 },
        { "Digit9", Key.Digit9 },
        // F键
        { "F1", Key.F1 }, { "F2", Key.F2 }, { "F3", Key.F3 }, { "F4", Key.F4 },
        { "F5", Key.F5 }, { "F6", Key.F6 }, { "F7", Key.F7 }, { "F8", Key.F8 },
        { "F9", Key.F9 }, { "F10", Key.F10 }, { "F11", Key.F11 }, { "F12", Key.F12 },
        // 修饰键
        { "ShiftLeft", Key.LeftShift }, { "ShiftRight", Key.RightShift },
        { "ControlLeft", Key.LeftCtrl }, { "ControlRight", Key.RightCtrl },
        { "AltLeft", Key.LeftAlt }, { "AltRight", Key.RightAlt },
        { "MetaLeft", Key.LeftMeta }, { "MetaRight", Key.RightMeta },
        // 功能键
        { "Enter", Key.Enter }, { "NumpadEnter", Key.NumpadEnter },
        { "Escape", Key.Escape }, { "Space", Key.Space },
        { "Backspace", Key.Backspace }, { "Delete", Key.Delete },
        { "Tab", Key.Tab },
        { "ArrowUp", Key.UpArrow }, { "ArrowDown", Key.DownArrow },
        { "ArrowLeft", Key.LeftArrow }, { "ArrowRight", Key.RightArrow },
        { "Home", Key.Home }, { "End", Key.End },
        { "PageUp", Key.PageUp }, { "PageDown", Key.PageDown },
        { "CapsLock", Key.CapsLock }, { "Insert", Key.Insert },
        // 符号
        { "Minus", Key.Minus }, { "Equal", Key.Equals },
        { "BracketLeft", Key.LeftBracket }, { "BracketRight", Key.RightBracket },
        { "Backslash", Key.Backslash }, { "Semicolon", Key.Semicolon },
        { "Quote", Key.Quote }, { "Backquote", Key.Backquote },
        { "Comma", Key.Comma }, { "Period", Key.Period },
        { "Slash", Key.Slash },
        // 小键盘
        { "Numpad0", Key.Numpad0 }, { "Numpad1", Key.Numpad1 }, { "Numpad2", Key.Numpad2 },
        { "Numpad3", Key.Numpad3 }, { "Numpad4", Key.Numpad4 }, { "Numpad5", Key.Numpad5 },
        { "Numpad6", Key.Numpad6 }, { "Numpad7", Key.Numpad7 }, { "Numpad8", Key.Numpad8 },
        { "Numpad9", Key.Numpad9 },
        { "NumpadAdd", Key.NumpadPlus }, { "NumpadSubtract", Key.NumpadMinus },
        { "NumpadMultiply", Key.NumpadMultiply }, { "NumpadDivide", Key.NumpadDivide },
        { "NumpadDecimal", Key.NumpadPeriod },
    };

    #region InputHub 控制器

    public Action<Vector2> OnMousePosChanged { get; set; }
    public Action<Vector2> OnMouseMoveChanged { get; set; }
    public Action<float> OnZoomChanged { get; set; }

    public Action OnMouseLeftButtonDown { get; set; }
    public Action OnMouseLeftButtonUp { get; set; }
    public Action OnMouseRightButtonDown { get; set; }
    public Action OnMouseRightButtonUp { get; set; }
    public Action OnMouseMiddleButtonDown { get; set; }
    public Action OnMouseMiddleButtonUp { get; set; }

    public Action<Vector2> OnMoveChanged { get; set; }

    public Action OnSpaceKeyEnter { get; set; }
    public Action OnFKeyEnter { get; set; }
    public Action<bool> OnLeftShiftKeyChanged { get; set; }
    public Action<bool> OnLeftAltKeyChanged { get; set; }

    #endregion
}
