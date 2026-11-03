using NonsensicalKit.Core;
using UnityEngine;


namespace Frame.Core.FocusCamera
{
public class FocusPoint : MonoBehaviour
{
    [SerializeField] private string m_id;
    [SerializeField] private GameObject m_point;

    [SerializeField] private bool m_autoInit = true;

    [SerializeField, Tooltip("ConfigurableCamera 写入 IOCC 时的名称，需与相机组件一致")]
    private string m_focusCameraIoccName = "FocusCamera";

    private bool _subscribed;

    private void Reset()
    {
        m_point ??= this.gameObject;
    }

    private void Awake()
    {
        if (m_autoInit)
        {
            SubscribeFocus();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFocus();
    }

    public void SetID(string id)
    {
        if (_subscribed && m_id == id)
        {
            return;
        }

        UnsubscribeFocus();
        m_id = id;
        SubscribeFocus();
    }

    private void SubscribeFocus()
    {
        if (_subscribed || string.IsNullOrEmpty(m_id))
        {
            return;
        }

        IOCC.Subscribe("focusEquipment", m_id, FocusEquipment);
        _subscribed = true;
    }

    private void UnsubscribeFocus()
    {
        if (!_subscribed || string.IsNullOrEmpty(m_id))
        {
            _subscribed = false;
            return;
        }

        IOCC.Unsubscribe("focusEquipment", m_id, FocusEquipment);
        _subscribed = false;
    }

    private void FocusEquipment()
    {
        if (!IOCC.TryGet<ConfigurableCamera>(m_focusCameraIoccName, out var cam) || cam == null)
        {
            Debug.LogWarning(
                $"[FocusPoint] 未找到 ConfigurableCamera「{m_focusCameraIoccName}」，跳过注视。id={m_id}",
                this);
            return;
        }

        cam.StartFocus(transform);
    }
}
}
