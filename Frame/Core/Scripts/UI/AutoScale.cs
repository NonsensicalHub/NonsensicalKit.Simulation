using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class AutoScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private RectTransform _rectTransform;
    [SerializeField] private float _scale = 0.5f;
    [SerializeField] private float _alpha = 0.5f;

    private bool targ;
    private WaitUntil waitUntil;

   // Coroutine coroutine;

    private Tweener a;

    private void Start()
    {
        if (_canvasGroup == null || _rectTransform == null)
        {
            Debug.LogError("AutoScale: 关键引用未绑定，脚本已禁用。", this);
            enabled = false;
            return;
        }
        waitUntil = new WaitUntil(() => { return targ; });
    }

    private void OnEnable()
    {
        if (!enabled) return;
        OnRest();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_canvasGroup == null || _rectTransform == null) return;
        targ = false;
        _canvasGroup.DoFade(1f, 0.25f);

        a = _rectTransform.DoLocalScale(Vector3.one, 0.45f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        /*if (targ == false)
        {
            coroutine ??= StartCoroutine(WaitComplete());
        }
        else
        {
            Hide();
        }*/
        a?.Abort();
        Hide();
    }

    IEnumerator WaitComplete()
    {
        yield return waitUntil;
        Hide();
        //coroutine = null;
    }

    private void Hide()
    {
        if (_canvasGroup == null || _rectTransform == null) return;
        _canvasGroup.DoFade(_alpha, 0.25f);
        _rectTransform.DoLocalScale(Vector3.one * _scale, 0.25f);
    }

    private void OnRest()
    {
        if (_canvasGroup == null || _rectTransform == null) return;
        _rectTransform.localScale = Vector3.one * _scale;
        _canvasGroup.alpha = _alpha;
    }
}