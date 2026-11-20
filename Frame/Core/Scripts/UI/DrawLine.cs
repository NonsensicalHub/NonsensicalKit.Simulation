using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Core.UI
{
/// <summary>
/// 挂载在Panel上
/// </summary>
public class DrawLine : NonsensicalMono
{
    public Image line;//画线的Image
    [SerializeField] private RectTransform pointStart;
    [SerializeField] private RectTransform pointEnd;
    [SerializeField] public Transform target;
    [SerializeField] public Transform target2;
    [SerializeField] private float width;
    [SerializeField] private CanvasGroup _group;

    Vector3 temp;

    [SerializeField] bool enable;
    private void Awake()
    {
        Subscribe<bool>("showLine", ShowLine);
        if (target == null || target2 == null || line == null || pointStart == null || pointEnd == null || _group == null)
        {
            Debug.LogError("DrawLine: 关键引用未绑定，组件已禁用。", this);
            enabled = false;
            return;
        }
        temp = target.position;
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    private void OnEnable()
    {
        if (!enabled) return;
        _group.alpha = 0;
        target.position = target2.position;
        NonsensicalInstance.Instance.DelayDoIt(0.3f, () =>
        {
            if (this == null || target == null || _group == null) return;
            _group.alpha = 1;
            target.DoMove(temp, 1f);
        });
    }

    private void Update()
    {
        if (enable)
        {
            if (Camera.main == null || target == null || target2 == null) return;
            Vector3 aa = Camera.main.WorldToScreenPoint(target.position);
            Vector3 bb = Camera.main.WorldToScreenPoint(target2.position);
            pointStart.transform.position = aa;
            pointEnd.transform.position = bb;
            DrawStraightLine(line, pointStart.anchoredPosition, pointEnd.anchoredPosition);
        }
    }

    void DrawStraightLine(Image image, Vector2 a, Vector2 b)
    {
        float distance = Vector2.Distance(a, b);                                    //求距离
        float angle = Vector2.SignedAngle(a - b, Vector2.left);                     //求夹角
        image.GetComponent<RectTransform>().anchoredPosition = (a + b) / 2;
        image.GetComponent<RectTransform>().sizeDelta = new Vector2(distance, width);   //长度，宽度
        image.transform.localRotation = Quaternion.AngleAxis(-angle, Vector3.forward);
    }

    public void ShowLine(bool enable)
    {
        if (line == null) return;
        line.enabled = enable;
        this.enable = enable;
    }

}
}
