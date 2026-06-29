using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NonsensicalKit.Simulation.DragSystem
{
    public class DragIcon : NonsensicalUI
    {
        [SerializeField] private Image m_img_icon;

        private Camera _eventCamera;
        private RectTransform _rect;
        private DragDropSystem _dds;

        protected override void Awake()
        {
            base.Awake();
            _rect = GetComponent<RectTransform>();
            IOCC.Set(this);
            _dds = ServiceCore.Get<DragDropSystem>();
            _dds.BeginDrag += OnBeginDrag;
            _dds.Drag += OnDrag;
            _dds.Drop += OnDrop;
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
            RectTransformUtility.ScreenPointToWorldPointInRectangle(_rect, eventData.position, eventData.enterEventCamera, out var pos);
            transform.position = pos;
            OpenSelf();
        }

        public void OnDrag(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle(_rect, eventData.position, _eventCamera, out var pos);
            _rect.position = pos;
        }

        public void OnDrop(PointerEventData eventData)
        {
            CloseSelf();
        }
    }
}
