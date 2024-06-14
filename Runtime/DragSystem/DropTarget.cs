using NonsensicalKit.Core.Service;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public interface IDropTarget
{
    void BeginDrag(object[] dragObjects, PointerEventData eventData);
    void DragEnter(object[] dragObjects, PointerEventData eventData);
    void DragLeave(PointerEventData eventData);
    void Drag(object[] dragObjects, PointerEventData eventData);
    void Drop(object[] dragObjects, PointerEventData eventData);
}

[DefaultExecutionOrder(-50)]
public class DropTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IDropTarget
{
    public event DragDropEventHander BeginDragEvent;
    public event DragDropEventHander DragEnterEvent;
    public event DragDropEventHander DragLeaveEvent;
    public event DragDropEventHander DragEvent;
    public event DragDropEventHander DropEvent;

    [SerializeField]
    public GameObject m_dragDropTargetGO;

    private IDropTarget[] m_dragDropTargets = new IDropTarget[0];
    private DragDropSystem dragDrop;

    public virtual bool IsPointerOver
    {
        get;
        set;
    }

    protected virtual void Awake()
    {
        dragDrop = ServiceCore.Get<DragDropSystem>();
        if (m_dragDropTargetGO == null)
        {
            m_dragDropTargetGO = gameObject;
        }
        m_dragDropTargets = m_dragDropTargetGO.GetComponents<Component>().OfType<IDropTarget>().ToArray();
        if (m_dragDropTargets.Length == 0)
        {
            Debug.LogWarning("dragDropTargetGO does not contains components with IDragDropTarget interface implemented");
            m_dragDropTargets = new[] { this };
        }
    }

    protected virtual void OnDestroy()
    {
        m_dragDropTargets = null;
    }


    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
    {
        IsPointerOver = true;
        if (dragDrop.InProgress && dragDrop.Source != (object)this)
        {
            DragEnter(dragDrop.DragObjects, eventData);

            dragDrop.BeginDrag += OnBeginDrag;
            dragDrop.Drag += OnDrag;
            dragDrop.Drop += OnDrop;
        }
    }

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
    {
        IsPointerOver = false;
        dragDrop.BeginDrag -= OnBeginDrag;
        dragDrop.Drop -= OnDrop;
        dragDrop.Drag -= OnDrag;
        if (dragDrop.InProgress && dragDrop.Source != (object)this)
        {
            DragLeave(eventData);
        }
    }

    private void OnBeginDrag(PointerEventData pointerEventData)
    {
        if (dragDrop.InProgress)
        {
            for (int i = 0; i < m_dragDropTargets.Length; ++i)
            {
                m_dragDropTargets[i].BeginDrag(dragDrop.DragObjects, pointerEventData);
            }
        }
    }

    private void OnDrag(PointerEventData pointerEventData)
    {
        if (dragDrop.InProgress)
        {
            for (int i = 0; i < m_dragDropTargets.Length; ++i)
            {
                m_dragDropTargets[i].Drag(dragDrop.DragObjects, pointerEventData);
            }
        }
    }

    private void OnDrop(PointerEventData eventData)
    {
        dragDrop.BeginDrag -= OnBeginDrag;
        dragDrop.Drop -= OnDrop;
        dragDrop.Drag -= OnDrag;
        if (dragDrop.InProgress)
        {
            for (int i = 0; i < m_dragDropTargets.Length; ++i)
            {
                m_dragDropTargets[i].Drop(dragDrop.DragObjects, eventData);
            }
        }
    }

    public virtual void BeginDrag(object[] dragObjects, PointerEventData eventData)
    {
        if (BeginDragEvent != null)
        {
            BeginDragEvent(eventData);
        }
    }

    public virtual void DragEnter(object[] dragObjects, PointerEventData eventData)
    {
        if (DragEnterEvent != null)
        {
            DragEnterEvent(eventData);
        }
    }

    public virtual void Drag(object[] dragObjects, PointerEventData eventData)
    {
        if (DragEvent != null)
        {
            DragEvent(eventData);
        }
    }

    public virtual void DragLeave(PointerEventData eventData)
    {
        if (DragLeaveEvent != null)
        {
            DragLeaveEvent(eventData);
        }
    }

    public virtual void Drop(object[] dragObjects, PointerEventData eventData)
    {
        if (DropEvent != null)
        {
            DropEvent(eventData);
        }
    }
}
