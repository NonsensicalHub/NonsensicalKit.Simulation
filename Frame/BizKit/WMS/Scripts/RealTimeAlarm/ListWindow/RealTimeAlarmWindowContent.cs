using System;
using System.Collections.Generic;
using Frame.RealTimeAlarm;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RealTimeAlarmWindowContent : NonsensicalMono, IWindowContent, IWindowHandleReceiver
{
    [SerializeField] private string m_tableID = "实时报警详情";
    [SerializeField] private TMP_InputField m_inputIndex;
    [SerializeField] private TMP_Text m_totalCount;
    [SerializeField] private Button m_lastPage;
    [SerializeField] private Button m_nextPage;
    [SerializeField] private int m_pageSize = 20; // 每页显示条数，可根据需要调整
    [SerializeField] private TableControl m_table;

    private int _currentPage = 1; // 当前页码
    private int _totalPages = 1; // 总页数
    public string WindowHandle { get; set; }

    private void Awake()
    {
        m_lastPage.onClick.AddListener(OnLastButton);
        m_nextPage.onClick.AddListener(OnNextButton);
    }


    public void SetWindowHandle(string windowHandle)
    {
        WindowHandle = windowHandle;
        if (string.IsNullOrEmpty(WindowHandle))
        {
            return;
        }

        Unsubscribe("onLastButton", m_tableID, GoLastPage);
        Unsubscribe("onNextButton", m_tableID, GoNextPage);
        //窗口的按钮订阅
        m_tableID = $"{m_tableID}_{WindowHandle}";
        if (m_table != null)
        {
            //更新窗口订阅ID
            m_table.ChangeSubscribeTableID(m_tableID);  
        }


        Subscribe("onLastButton", m_tableID, GoLastPage);
        Subscribe("onNextButton", m_tableID, GoNextPage);
    }

    public void OnContentInit(object _)
    {
        m_inputIndex.onEndEdit.AddListener(GetData);
        // 获取第一页数据
        GetData("1");
    }

    public bool OnContentClose()
    {
        Exit();
        return true;
    }

    private void Exit()
    {
        m_inputIndex.onEndEdit.RemoveListener(GetData);
        Destroy(m_table);
    }

    private void GetData(string pageIndex)
    {
        // 解析页码
        if (int.TryParse(pageIndex, out int page))
        {
            // 确保页码在有效范围内
            page = Mathf.Clamp(page, 1, _totalPages);
            _currentPage = page;
        }
        else
        {
            Debug.LogWarning("无效的页码输入");
            return;
        }

        var alarmData = Execute<int, int, (List<List<string>>, int)>(RealTimeAlarmInfo.HistoryListHandler, _currentPage, m_pageSize);
        if (alarmData.Item1 == null)
        {
            Debug.LogWarning("[RealTimeAlarmWindowContent] getAlarmList 无数据（在线模式未注册或返回空），跳过表格刷新。", this);
            m_totalCount.text = "0";
            m_inputIndex.text = "1";
            _totalPages = 1;
            _currentPage = 1;
            return;
        }

        // 更新总页数
        _totalPages = Mathf.Max(1, Mathf.CeilToInt((float)alarmData.Item2 / m_pageSize));
        m_totalCount.text = _totalPages.ToString();

        // 发布事件更新表格
        PublishWithID<List<List<string>>, bool>("showTable", m_tableID, alarmData.Item1, true);

        // 更新输入框显示当前页码
        m_inputIndex.text = _currentPage.ToString();
    }

    private void GoLastPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            GetData(_currentPage.ToString());
        }
    }

    private void GoNextPage()
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            GetData(_currentPage.ToString());
        }
    }

    private void OnLastButton()
    {
        PublishWithID("onLastButton", m_tableID);
    }

    private void OnNextButton()
    {
        PublishWithID("onNextButton", m_tableID);
    }
}
