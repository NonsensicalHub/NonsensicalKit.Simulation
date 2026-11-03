using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ToggleValueChange : MonoBehaviour
{
    [SerializeField] private bool reName;
    [SerializeField] private TMP_Text formT;
    [SerializeField] private TMP_Text ToT;
    [SerializeField] private GameObject _isON;
    [SerializeField] private GameObject _isOFF;

    [SerializeField] private Toggle _toggle;


    private void Reset()
    {
        _toggle = GetComponent<Toggle>();
    }
    [SerializeField] private UnityEvent IsOnEvent;
    [SerializeField] private UnityEvent IsOFFEvent;

    private void Awake()
    {
        _toggle ??= GetComponent<Toggle>();
        if (_toggle == null)
        {
            Debug.LogError("ToggleValueChange: 未找到 Toggle 组件，脚本已禁用。", this);
            enabled = false;
            return;
        }
        _toggle.onValueChanged.AddListener(OnValueChange);
    }
    private void OnDestroy()
    {
        _toggle?.onValueChanged.RemoveListener(OnValueChange);
    }
    private void Update()
    {
        if (reName)
        {
            if (ToT == null || formT == null) return;
            ToT.text = formT.text;
        }
    }
    private void OnValueChange(bool arg0)
    {
        if (arg0)
        {
            IsOnEvent.Invoke();
        }
        else
        {
            IsOFFEvent.Invoke();
        }
    }
}
