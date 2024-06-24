using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragIcon : NonsensicalUI
{
    [SerializeField] private Image m_img_icon;

    private Camera _eventCamera;
    private RectTransform _rect;
    private DragDropSystem dds;

    protected override void Awake()
    {
        base.Awake();
        _rect = GetComponent<RectTransform>();
        IOCC.Set<DragIcon>(this);
        dds = ServiceCore.Get<DragDropSystem>();
        dds.BeginDrag += OnBeginDrag;
        dds.Drag += OnDrag;
        dds.Drop += OnDrop;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        IOCC.Set<DragIcon>(null);
    }

    public void ChangeSprite(Sprite sprite)
    {
        m_img_icon.sprite = sprite;
    }
    public void ChangeSprite(string iconPath)
    {
        m_img_icon.sprite = Resources.Load<Sprite>(iconPath);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _eventCamera = eventData.enterEventCamera;
        Vector3 pos;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(_rect, eventData.position, eventData.enterEventCamera, out pos);
        transform.position = pos;
        OpenSelf();
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector3 pos;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(_rect, eventData.position, _eventCamera, out pos);
        _rect.position = pos;
    }

    public void OnDrop(PointerEventData eventData)
    {
        CloseSelf();
    }
}
