using TMPro;
using UnityEngine;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 库龄分布右侧单行视图：仅持有 TMP 引用，供运行时灌数使用（不继承 BindingToken）。
/// </summary>
public class StockAgeRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text m_title;
    [SerializeField] private TMP_Text m_count;
    [SerializeField] private TMP_Text m_percent;

    public void Set(string title, string count, string percent)
    {
        SetOne(m_title, title);
        SetOne(m_count, count);
        SetOne(m_percent, percent);
    }

    private static void SetOne(TMP_Text text, string value)
    {
        if (text == null)
            return;
        text.SetText(value ?? "");
        text.ForceMeshUpdate(true);
    }
}
}
