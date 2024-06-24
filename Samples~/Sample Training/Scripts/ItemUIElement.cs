using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Temp.InventorySystem;
using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemUIElement : ListTableElement<ItemEntity>, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler, IDropTarget
{
    [SerializeField] private Image m_img_icon;
    [SerializeField] private TextMeshProUGUI m_txt_name;
    [SerializeField] private TextMeshProUGUI m_txt_count;
    [SerializeField] private GameObject m_dragMask;

    public string _inventoryID { get; set; }
    public int _inventoryIndex { get; set; }

    protected DragDropSystem dds;
    protected override void Awake()
    {
        base.Awake();
        dds = ServiceCore.Get<DragDropSystem>();
        m_dragMask.SetActive(false);
    }

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        if (ElementData.Data != null)
        {
            IOCC.Get<DragIcon>().ChangeSprite(ElementData.Data.Sprite);
            dds.RaiseBeginDrag(this, new object[] { ElementData }, eventData);
            m_dragMask.SetActive(true);
        }
    }

    public virtual void OnBeginDrag(PointerEventData eventData)
    {
    }

    public virtual void OnDrag(PointerEventData eventData)
    {
        if (ElementData.Data != null)
        {
            dds.RaiseDrag(eventData);
        }
    }

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        if (ElementData.Data != null)
        {
            m_dragMask.SetActive(false);
            dds.RaiseDrop(eventData);
        }
    }


    public override void SetValue(ItemEntity elementData)
    {
        base.SetValue(elementData);
        m_dragMask.SetActive(false);
        if (elementData.Data != null)
        {
            m_txt_name.text = elementData.Data.ItemName;
            m_txt_count.text = elementData.StackNum.ToString();

            m_img_icon.gameObject.SetActive(true);
            Debug.Log(elementData.Data.Sprite);
            m_img_icon.sprite =Resources.Load<Sprite>(elementData.Data.Sprite);
        }
        else
        {
            m_txt_name.text = string.Empty;
            m_txt_count.text = string.Empty;
            m_img_icon.gameObject.SetActive(false);
        }
    }


    public void BeginDrag(object[] dragObjects, PointerEventData eventData)
    {
    }

    public void DragEnter(object[] dragObjects, PointerEventData eventData)
    {
    }

    public void DragLeave(PointerEventData eventData)
    {
    }

    public void Drag(object[] dragObjects, PointerEventData eventData)
    {
    }

    public void Drop(object[] dragObjects, PointerEventData eventData)
    {
        if (dragObjects[0] is ItemEntity)
        {
            var data = dragObjects[0] as ItemEntity;
            ServiceCore.Get<InventorySystem>().MoveItem(data.InventoryIndex, data.InventoryID, ElementData.InventoryIndex, ElementData.InventoryID);
        }
    }
}
