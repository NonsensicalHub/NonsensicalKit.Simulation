using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Frame.Core.UI
{
public class UIInteractable : NonsensicalMono, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private string m_subscribeCommand;
    [SerializeField] private bool _onlyRightMouseButton;

    [SerializeField] private UnityEvent _onPointerclick;
    [SerializeField] private UnityEvent _onPointerEnter;
    [SerializeField] private UnityEvent _onPointerExit;

    [SerializeField] private UnityEvent m_cmmandCallEvent;
    
    private void Awake()
    {
        if (m_subscribeCommand != null)
            Subscribe(m_subscribeCommand, CallMethod);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_onlyRightMouseButton)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                _onPointerclick?.Invoke();
            }
        }
        else
        {
            _onPointerclick?.Invoke();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _onPointerEnter?.Invoke();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _onPointerExit?.Invoke();
    }

    private void CallMethod()
    {
        m_cmmandCallEvent?.Invoke();
    }
}
}
