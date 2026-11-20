using NaughtyAttributes;
using TMPro;
using UnityEngine;

namespace Frame.Core.UI
{
public enum ChangeType
{
    SameWithTMP,
    ChangeWithSiblingIndex
}

public class TMPContentToTreeName : MonoBehaviour
{
    [SerializeField] private bool m_enable;
    [SerializeField,Label("同时修改TMPText")] private bool m_changeTMPName;
    [SerializeField] private ChangeType m_expression = ChangeType.SameWithTMP;
    [SerializeField] private bool m_includeUnActive = true;
    [SerializeField] private string m_prefix;

    [SerializeField, ShowIf("m_changeTMPName")]
    private string m_newName;

    [SerializeField] private TMP_Text[] m_text;

    private void Reset()
    {
        m_text = this.GetComponentsInChildren<TMP_Text>(includeInactive: m_includeUnActive);
    }

    [Button("查找TMP_Text")]
    private void FindTMP()
    {
        m_text = this.GetComponentsInChildren<TMP_Text>(includeInactive: m_includeUnActive);
    }

    private void OnValidate()
    {
        Refresh();
    }

    [Button("Refresh")]
    private void Refresh()
    {
        if (m_enable == false) return;
        switch (m_expression)
        {
            case ChangeType.SameWithTMP:
                foreach (var item in m_text)
                {
                    if (m_changeTMPName)
                    {
                        item.text = m_newName;
                    }

                    this.name = $"{m_prefix}_{item.text}";
                }

                break;
            case ChangeType.ChangeWithSiblingIndex:
                this.name = m_prefix.Replace("*", transform.GetSiblingIndex().ToString());
                break;
        }
    }
}
}
