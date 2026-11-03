using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 通过ID与悬浮标签绑定,响应标签的交互事件
/// </summary>
public class UIPoint : MonoBehaviour
{
    [BindableParam("IconID")] public string m_IconID;
    //[BindableParam("ShowNode")] public string[] m_ShowNodes;

    public UnityEvent m_OnClick;
    public UnityEvent m_OnHoverEnter;
    public UnityEvent m_OnHoverExit;

    private bool _registered;

    private void Start()
    {
        // 等 DigitalTwinUIManager 完成监听注册后再上报，避免 Set 早于 Listener
        ServiceCore.SafeGet<DigitalTwinUIManager>(OnManagerReady);
    }

    private void OnEnable()
    {
        // 对象被重新激活时，若管理器已就绪则补注册
        if (!_registered)
            ServiceCore.SafeGet<DigitalTwinUIManager>(OnManagerReady);
    }

    private void OnManagerReady(DigitalTwinUIManager manager)
    {
        if (_registered || manager == null || !manager.IsReady)
            return;

        manager.RegisterUIPoint(this);
        _registered = true;
    }

    public void OnClick()
    {
        m_OnClick?.Invoke();
    }

    public void OnHoverEnter()
    {
        m_OnHoverEnter?.Invoke();
    }

    public void OnHoverExit()
    {
        m_OnHoverExit?.Invoke();
    }
}
