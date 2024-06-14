using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 可以修改DragItems使用，也可以自行实现拖拽这一套逻辑
/// </summary>
public class DragTarget : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
{
    public object[] DragItems { get; set; }

    protected DragDropSystem dds;

    protected virtual void Awake()
    {
        dds = IOCC.Get<DragDropSystem>();
    }

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        dds.RaiseBeginDrag(this, DragItems, eventData);
    }

    public virtual void OnBeginDrag(PointerEventData eventData)
    {
    }

    public virtual void OnDrag(PointerEventData eventData)
    {
        dds.RaiseDrag(eventData);
    }

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        dds.RaiseDrop(eventData);
    }
}
