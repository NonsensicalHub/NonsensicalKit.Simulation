using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NonsensicalKit.Simulation.DragSystem
{
    /// <summary>
    /// 可以修改DragItems使用，也可以自行实现拖拽这一套逻辑
    /// </summary>
    public class DragTarget : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
    {
        public object[] DragItems { get; set; }

        protected DragDropSystem DDS;

        protected virtual void Awake()
        {
            DDS = ServiceCore.Get<DragDropSystem>();
        }

        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (DDS == null) return;
            DDS.RaiseBeginDrag(this, DragItems, eventData);
        }

        public virtual void OnBeginDrag(PointerEventData eventData)
        {
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (DDS == null) return;
            DDS.RaiseDrag(eventData);
        }

        public virtual void OnPointerUp(PointerEventData eventData)
        {
            if (DDS == null) return;
            DDS.RaiseDrop(eventData);
        }
    }
}
