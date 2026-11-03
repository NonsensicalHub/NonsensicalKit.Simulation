using DG.Tweening;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.UI;


namespace Frame.Core.UI
{
public class ButtonDOMove : MonoBehaviour
{
    [ContextMenuItem("设置当前坐标为值", "SetPos")]
    [SerializeField] private Vector2 startPos;
    [ContextMenuItem("设置当前坐标为值", "SetPos1")]
    [SerializeField] private Vector2 endPos;
    [SerializeField] private Vector2 startScal = Vector3.one;
    [SerializeField] private Vector2 enfScal = Vector3.one;
    [SerializeField] private float time;
    [SerializeField] private bool enableReverse;


    [SerializeField] private RectTransform rectTransform;

    [SerializeField] private HorizontalLayoutGroup horizontalLayoutGroup;
    [SerializeField] private VerticalLayoutGroup verticalLayoutGroup;

    private void Reset()
    {
        rectTransform = GetComponent<RectTransform>();

    }
    private void SetPos()
    {
        startPos = this.rectTransform.anchoredPosition;
    }
    private void SetPos1()
    {
        endPos = this.rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        DO(startPos, endPos);
    }

    private void OnDisable()
    {
        if (!enableReverse)
        {
            rectTransform.anchoredPosition = startPos;
        }
    }


    public void SetHide()
    {
        if (enableReverse)
        {
            DO(endPos, startPos, true);
        }
    }

    private void DO(Vector3 start, Vector3 end, bool inversion = false)
    {
        CheckLayout(false);

        rectTransform.anchoredPosition = start;
        // 使用 DOTween.dll 核心 API，不依赖 DOTween.Modules（DOAnchorPos 在 ModuleUI 中）
        var endPos = (Vector2)end;
        DOTween.To(
                () => rectTransform.anchoredPosition,
                v => rectTransform.anchoredPosition = v,
                endPos,
                time)
            .SetTarget(rectTransform)
            .OnComplete(() => CheckLayout(!inversion));
    }

    private void CheckLayout(bool setactive)
    {
        if (horizontalLayoutGroup != null)
        {
            horizontalLayoutGroup.enabled = setactive;
        }
        if (verticalLayoutGroup != null)
        {
            verticalLayoutGroup.enabled = setactive;
        }
    }
}
}
