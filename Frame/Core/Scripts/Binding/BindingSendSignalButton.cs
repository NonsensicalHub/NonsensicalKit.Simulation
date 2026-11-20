using Newtonsoft.Json.Linq;
using NonsensicalKit.UGUI.SimpleSignalControl;
using UnityEngine;

/// <summary>
/// 将 JToken 的字符串值绑定到 SendSignalButton.Signal（通过 SetSignal）。
/// JSON 格式：普通字符串 或 { "value": "..." }
/// </summary>

namespace Frame.Core.Binding
{
public class BindingSendSignalButton : BindingToken
{
    [SerializeField] private SendSignalButton m_sendSignalButton;

    private void Reset()
    {
        m_sendSignalButton = GetComponent<SendSignalButton>();
    }

    public override void BindToken(JToken token)
    {
        if (m_sendSignalButton == null)
        {
            LogWarning($"{name} 缺少 SendSignalButton，无法绑定 Key=\"{Key}\"");
            return;
        }

        string signal = token?.Type == JTokenType.Object
            ? token["value"]?.ToString()
            : token?.ToString();

        m_sendSignalButton.SetSignal(signal ?? string.Empty);
    }

    public override JToken CollectJson()
    {
        if (m_sendSignalButton == null)
        {
            LogWarning($"{name} 缺少 SendSignalButton");
            return new JValue("未配置组件");
        }

        return new JValue(m_sendSignalButton.GetSignal());
    }
}
}