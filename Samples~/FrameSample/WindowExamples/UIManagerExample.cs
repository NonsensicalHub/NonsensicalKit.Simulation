using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 在任意脚本中打开窗口的示例。可在 Inspector 用 Button，或绑定 UI Button。
/// </summary>
public class UIManagerExample : MonoBehaviour
{
    [SerializeField] private string m_windowName = "DemoWindow";
    [SerializeField] private GameObject _characterDetailPrefab;
    [SerializeField] private Sprite _characterAvatar;
    [SerializeField] private Button m_openButton;

    private void Awake()
    {
        if (m_openButton != null)
            m_openButton.onClick.AddListener(ShowCharacterDetail);
    }

    private void OnDestroy()
    {
        if (m_openButton != null)
            m_openButton.onClick.RemoveListener(ShowCharacterDetail);
    }

    [Button]
    public void ShowCharacterDetail()
    {
        var config = new WindowConfig
        {
            Title = "角色详情",
            Icon = null,
            Width = 600f,
            Height = 500f,
            AllowFullscreen = true,
            OnCloseCallback = () =>
            {
                Debug.Log("角色详情窗口已关闭，执行后续逻辑。");
            }
        };

        var contentArgs = new CharacterDetailContent.CharacterArgs
        {
            CharacterName = "张三",
            Avatar = _characterAvatar,
            Description = "这是一段角色描述信息……"
        };

        if (IOCC.TryGet<WindowContainerManager>(m_windowName, out var window))
        {
            window.OpenWindow(config, _characterDetailPrefab, contentArgs);
        }
        else
        {
            Debug.LogWarning(
                $"[UIManagerExample] 未找到 WindowContainerManager「{m_windowName}」。请确认场景中已挂载且 m_windowName 一致。");
        }
    }
}
