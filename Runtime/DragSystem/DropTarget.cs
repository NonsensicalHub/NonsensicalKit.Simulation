using System;
using System.Linq;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace NonsensicalKit.Simulation.DragSystem
{
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
        public event DragDropEventHandle BeginDragEvent;
        public event DragDropEventHandle DragEnterEvent;
        public event DragDropEventHandle DragLeaveEvent;
        public event DragDropEventHandle DragEvent;
        public event DragDropEventHandle DropEvent;

        [FormerlySerializedAs("m_dragDropTargetGO")] [SerializeField]
        public GameObject m_DragDropTargetGo;

        private IDropTarget[] _dragDropTargets = Array.Empty<IDropTarget>();
        private DragDropSystem _dragDrop;

        public virtual bool IsPointerOver { get; set; }

        protected virtual void Awake()
        {
            _dragDrop = ServiceCore.Get<DragDropSystem>();
            if (m_DragDropTargetGo == null)
            {
                m_DragDropTargetGo = gameObject;
            }

            _dragDropTargets = m_DragDropTargetGo.GetComponents<Component>().OfType<IDropTarget>().ToArray();
            if (_dragDropTargets.Length == 0)
            {
                Debug.LogWarning("dragDropTargetGO does not contains components with IDragDropTarget interface implemented");
                _dragDropTargets = new[] { this as IDropTarget };
            }
        }

        protected virtual void OnDestroy()
        {
            _dragDropTargets = null;
        }


        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            IsPointerOver = true;
            if (_dragDrop.InProgress && (DropTarget)_dragDrop.Source != this)
            {
                DragEnter(_dragDrop.DragObjects, eventData);

                _dragDrop.BeginDrag += OnBeginDrag;
                _dragDrop.Drag += OnDrag;
                _dragDrop.Drop += OnDrop;
            }
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            IsPointerOver = false;
            _dragDrop.BeginDrag -= OnBeginDrag;
            _dragDrop.Drop -= OnDrop;
            _dragDrop.Drag -= OnDrag;
            if (_dragDrop.InProgress && (DropTarget)_dragDrop.Source != this)
            {
                DragLeave(eventData);
            }
        }

        private void OnBeginDrag(PointerEventData pointerEventData)
        {
            if (_dragDrop.InProgress)
            {
                for (int i = 0; i < _dragDropTargets.Length; ++i)
                {
                    _dragDropTargets[i].BeginDrag(_dragDrop.DragObjects, pointerEventData);
                }
            }
        }

        private void OnDrag(PointerEventData pointerEventData)
        {
            if (_dragDrop.InProgress)
            {
                for (int i = 0; i < _dragDropTargets.Length; ++i)
                {
                    _dragDropTargets[i].Drag(_dragDrop.DragObjects, pointerEventData);
                }
            }
        }

        private void OnDrop(PointerEventData eventData)
        {
            _dragDrop.BeginDrag -= OnBeginDrag;
            _dragDrop.Drop -= OnDrop;
            _dragDrop.Drag -= OnDrag;
            if (_dragDrop.InProgress)
            {
                for (int i = 0; i < _dragDropTargets.Length; ++i)
                {
                    _dragDropTargets[i].Drop(_dragDrop.DragObjects, eventData);
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
}
