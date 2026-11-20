using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Frame.Core.Window;

/// <summary>
/// 示例：角色详情内容，可被加载到 WindowContainer 中。
/// 若未在 Inspector 绑定，会按子节点名 Name / Avatar / Description 自动查找。
/// </summary>

namespace Frame.Sample.WindowExamples
{
public class CharacterDetailContent : MonoBehaviour, IWindowContent
{
    [SerializeField] private TextMeshProUGUI _nameLabel;
    [SerializeField] private Image _avatarImage;
    [SerializeField] private TextMeshProUGUI _descLabel;

    public class CharacterArgs
    {
        public string CharacterName;
        public Sprite Avatar;
        public string Description;
    }

    private void Awake()
    {
        AutoBind();
    }

    private void AutoBind()
    {
        if (_nameLabel == null)
        {
            var t = transform.Find("Name");
            if (t != null) _nameLabel = t.GetComponent<TextMeshProUGUI>();
        }

        if (_avatarImage == null)
        {
            var t = transform.Find("Avatar");
            if (t != null) _avatarImage = t.GetComponent<Image>();
        }

        if (_descLabel == null)
        {
            var t = transform.Find("Description");
            if (t != null) _descLabel = t.GetComponent<TextMeshProUGUI>();
        }
    }

    public void OnContentInit(object args)
    {
        AutoBind();
        if (args is CharacterArgs data)
        {
            if (_nameLabel != null) _nameLabel.text = data.CharacterName;
            if (_avatarImage != null) _avatarImage.sprite = data.Avatar;
            if (_descLabel != null) _descLabel.text = data.Description;
        }
    }

    public bool OnContentClose()
    {
        Debug.Log("[CharacterDetailContent] 正在关闭，执行清理逻辑...");
        return true;
    }
}
}
