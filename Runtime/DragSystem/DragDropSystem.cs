using System;
using NonsensicalKit.Core.Service;
using UnityEngine.EventSystems;

namespace NonsensicalKit.Simulation.DragSystem
{
    public delegate void DragDropEventHandle(PointerEventData pointerEventData);

    public class DragDropSystem : IClassService
    {
        public event DragDropEventHandle BeginDrag;
        public event DragDropEventHandle Drag;
        public event DragDropEventHandle Drop;

        public object[] DragObjects
        {
            get;
            private set;
        }

        public bool InProgress => DragObjects is { Length: > 0 };

        private object _source;

        public object Source => _source;

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

            _source = source;
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
                _source = null;
            }
        }
    }
}
