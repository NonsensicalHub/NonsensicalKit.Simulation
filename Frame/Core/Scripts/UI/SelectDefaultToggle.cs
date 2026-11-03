using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.UI;


namespace Frame.Core.UI
{
public class SelectDefaultToggle : NonsensicalMono
{
    [SerializeField] private int m_defaultToggleIndex = 1;
     private Toggle _defaultToggle;

    private void Awake()
    {
        Subscribe("WillBeUnloadScene", SelectToDefaultToggle);
    }

    public void SelectToDefaultToggle()
    {
        _defaultToggle ??= transform.GetChild(m_defaultToggleIndex).GetComponent<Toggle>();
        _defaultToggle.isOn = true;
    }
}
}
