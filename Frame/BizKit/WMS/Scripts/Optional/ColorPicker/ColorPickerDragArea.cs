using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Frame.BizKit.WMS
{
public class ColorPickerDragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public Action<Vector2, RectTransform> OnDragLocal;

    public void OnPointerDown(PointerEventData eventData) => Handle(eventData);
    public void OnDrag(PointerEventData eventData) => Handle(eventData);

    private void Handle(PointerEventData eventData)
    {
        var rt = (RectTransform)transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out var local))
            OnDragLocal?.Invoke(local, rt);
    }
}
}
