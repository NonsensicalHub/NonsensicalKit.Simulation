using System;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core;
#if !UNITY_WEBGL||UNITY_EDITOR
using NonsensicalKit.DigitalTwin;
#endif
using UnityEngine;
using UnityEngine.InputSystem;

public enum ReceiveType
{
    WebSocket,
    WebMessage,
    All
}

/// <summary>
/// web Socket在Web不可用
/// </summary>
public class RemoteInputReceive : MonoSingleton<RemoteInputReceive>
{
    [SerializeField] private bool m_log;

    [SerializeField,InfoBox("web Socket在Web不可用")] private ReceiveType m_receiveType = ReceiveType.WebSocket;

    [SerializeField, Label("输入注入InputHub控制器"), Tooltip("将远程设备的输入数据注入到InputHub控制其中")]
    private bool m_enableInputHubControl;

    [SerializeField, Label("失去焦点时保持输入")] private bool m_lossFocusKeepInput = true;
    [SerializeField, Label("完全禁用本地设备输入")] private bool m_disableLocalInput = false;

#if !UNITY_WEBGL||UNITY_EDITOR
    [SerializeField] private SocketClient m_socketClient;
#endif
    private Mouse _remoteMouse;
    private Keyboard _remoteKeyboard;
    private InputSimulator _inputSimulator;

    private const string RemoteMouse = "RemoteVirtualMouse";
    private const string RemoteKeyBoard = "RemoteVirtualKeyboard";

    public Mouse RemoteMouseInstance;
    public Keyboard RemoteKeyboardInstance;


    protected override void Awake()
    {
        base.Awake();
        switch (m_receiveType)
        {
            case ReceiveType.WebSocket:
#if !UNITY_WEBGL||UNITY_EDITOR
                Subscribe<string>("GetSocketMessage", GetSocketMessage);
                m_socketClient.StartServer();
#endif
                break;
            case ReceiveType.WebMessage:
                Subscribe<string>("GetWebRemoteMessage", GetSocketMessage);
                break;
            case ReceiveType.All:
                Subscribe<string>("GetWebRemoteMessage", GetSocketMessage);
#if !UNITY_WEBGL||UNITY_EDITOR
                Subscribe<string>("GetSocketMessage", GetSocketMessage);
                m_socketClient.StartServer();
#endif
                break;
            default: throw new ArgumentOutOfRangeException();
        }
    }

    private void Start()
    {
        _inputSimulator = new InputSimulator();
        _remoteMouse = (InputSystem.GetDevice(RemoteMouse) ?? InputSystem.AddDevice<Mouse>(RemoteMouse)) as Mouse;
        _remoteKeyboard = (InputSystem.GetDevice(RemoteKeyBoard) ?? InputSystem.AddDevice<Keyboard>(RemoteKeyBoard)) as Keyboard;

        RemoteMouseInstance = _remoteMouse;
        RemoteKeyboardInstance = _remoteKeyboard;

        _inputSimulator.Init(_remoteMouse, _remoteKeyboard, m_enableInputHubControl);

        //忽略只有当窗体聚焦是生效
        InputSystem.settings.backgroundBehavior = m_lossFocusKeepInput ? InputSettings.BackgroundBehavior.IgnoreFocus : InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices;
#if UNITY_EDITOR
        //所有设备输入都指向GameView
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (m_disableLocalInput == false)
        {
            RemoteMouseInstance = hasFocus ? Mouse.current : _remoteMouse;
            RemoteKeyboardInstance = hasFocus ? Keyboard.current : _remoteKeyboard;
        }
        else
        {
            InputSystem.DisableDevice(Mouse.current);
            InputSystem.DisableDevice(Keyboard.current);
        }
    }

    private void OnApplicationQuit()
    {
        InputSystem.EnableDevice(Mouse.current);
        InputSystem.EnableDevice(Keyboard.current);
    }

    private void GetSocketMessage(string msg)
    {
        SerializedInputEvent jsonMsg = JsonConvert.DeserializeObject<SerializedInputEvent>(msg);

        if (jsonMsg != null)
        {
            _inputSimulator.SimulateInput(jsonMsg);

            if (m_log)
            {
                Debug.Log($"✅ 主线程收到完整消息：" +
                          $"类型={jsonMsg.type} | " +
                          $"客户端坐标=({jsonMsg.clientX},{jsonMsg.clientY}) | " +
                          $"屏幕坐标=({jsonMsg.screenX},{jsonMsg.screenY}) | " +
                          $"时间戳={jsonMsg.timestamp}");
            }
        }
        else
        {
            Debug.LogWarning($"⚠️ 消息解析为空: {msg}");
        }
    }
}

[Serializable]
public class SerializedInputEvent
{
    public string type;
    public long timestamp;

    public float clientX, clientY, screenX, screenY, viewportWidth, viewportHeight;
    public int button, buttons;
    public float deltaX, deltaY;
    public string key, code;
    public bool ctrlKey, shiftKey, altKey, metaKey;
}
