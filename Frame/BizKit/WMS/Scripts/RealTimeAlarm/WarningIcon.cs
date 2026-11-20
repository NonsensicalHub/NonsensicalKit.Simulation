using NonsensicalKit.Core;
using NonsensicalKit.UGUI;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS.RealTimeAlarm
{
public class WarningIcon : MonoBehaviour
{
    [SerializeField] private Button m_btn_icon;
    [SerializeField] private FollowGameObject m_follow;

    private string _deviceType;

    private string _errorCode;

    private Image _image;

    private Color _color;

    private float _process;

    private void Awake()
    {
        _color = Color.white;
        _image = GetComponent<Image>();
        if (_image == null || m_btn_icon == null || m_follow == null)
        {
            Debug.LogError("WarningIcon: 关键引用未绑定，脚本已禁用。", this);
            enabled = false;
            return;
        }

        m_btn_icon.onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        IOCC.Publish("ShowWarningDetail", _deviceType, _errorCode);
    }

    private void OnDestroy()
    {
        m_btn_icon?.onClick.RemoveListener(OnClick);
    }


    public void SetState(Transform target,string deviceType,string errorCode)
    {
        if (m_follow == null)
        {
            return;
        }
        m_follow.SetTarget(target);
        _deviceType = deviceType;
        _errorCode = errorCode;
    }
    private void Update()
    {
        _process += Time.deltaTime;
        var value = Mathf.Abs(_process % 2 - 1) * 0.6f + 0.4f;
        _color.a = value;
        _image.color = _color;
    }
}
}
