using NonsensicalKit.Core.Service;
using System;
using UnityEngine.EventSystems;

public delegate void DragDropEventHander(PointerEventData pointerEventData);

public class DragDropSystem : IClassService
{
    public event DragDropEventHander BeginDrag;
    public event DragDropEventHander Drag;
    public event DragDropEventHander Drop;

    public object[] DragObjects
    {
        get;
        private set;
    }

    public bool InProgress
    {
        get { return DragObjects != null && DragObjects.Length > 0; }
    }

    private object m_source;
    public object Source
    {
        get { return m_source; }
    }

    public object DragObject
    {
        get
        {
            if (DragObjects == null || DragObjects.Length == 0)
            {
                return null;
            }

            return DragObjects[0];
        }
    }

    public bool IsReady { get; set; }

    public Action InitCompleted { get; set; }

    public DragDropSystem()
    {
        IsReady = true;
        InitCompleted?.Invoke();
    }


    public void Reset()
    {
        DragObjects = null;
    }

    public void RaiseBeginDrag(object source, object[] dragItems, PointerEventData pointerEventData)
    {
        if (dragItems == null)
        {
            return;
        }
         
        m_source = source;
        DragObjects = dragItems;
        if (BeginDrag != null)
        {
            BeginDrag(pointerEventData);
        }
    }

    public void RaiseDrag(PointerEventData eventData)
    {
        if (InProgress)
        {
            if (Drag != null)
            {
                Drag(eventData);
            }
        }
    }

    public void RaiseDrop(PointerEventData pointerEventData)
    {
        if (InProgress)
        {
            if (Drop != null)
            {
                Drop(pointerEventData);
            }

            DragObjects = null;
            m_source = null;
        }
    }
}
