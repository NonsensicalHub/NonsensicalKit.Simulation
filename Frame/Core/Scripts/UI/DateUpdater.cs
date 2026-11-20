using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Frame.Core.UI
{
public class DateUpdater : MonoBehaviour
{
    [SerializeField] private TMP_Text m_dateText;

    WaitForSeconds sleep = new WaitForSeconds(10);
    private void Start()
    {
        m_dateText.text = GetTheDayTime();
        StartCoroutine(UpdaterDate());
    }

    IEnumerator UpdaterDate()
    {
        m_dateText.text = GetTheDayTime();
        while (true)
        {
            yield return sleep;
            m_dateText.text = GetTheDayTime();
        }

    }

    private string GetTheDayTime()
    {
        return DateTime.Now.ToString("M月d日 dddd tt h:mm", new CultureInfo("zh-CN"));
    }
}
}
