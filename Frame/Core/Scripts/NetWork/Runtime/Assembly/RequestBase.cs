using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Timer;
using UnityEngine;

public abstract class RequestBase : MonoBehaviour, IRequestTool
{
    [SerializeField] protected bool m_log;
    [SerializeField] protected bool m_enable;
    [SerializeField] private bool m_autoListener;

    [SerializeField, Label("后台 Server Key")]
    protected string m_serverKey;
    [SerializeField, ShowIf("m_enable"), Label("计时循环调用")]
    private bool m_enableLoopCallOnAwake;

    [SerializeField, ShowIf("m_enableLoopCallOnAwake"), Label("请求间隔")]
    private float m_interval;

    [SerializeField, ShowIf("m_enableLoopCallOnAwake"), Label("请求次数")]
    private int m_requestCount;

    [SerializeField, ShowIf("m_enableLoopCallOnAwake"), Label("开始计时调用")]
    private bool m_initialcall;

    protected readonly JsonSerializerSettings JsonSetting = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore };
    private IDPack _tiemrID;
    private bool _hasTimerTask;

    protected virtual void Awake()
    {
    }

    protected virtual void Start()
    {
        if (m_enable == false) return;
        if (IOCC.TryGet<TimerSystem>("Timer", out var timer) == false)
        {
            Debug.LogError("未配置定时器！", this.gameObject);
            if (m_autoListener)
            {
                IOCC.AddListener<TimerSystem>("Timer", Init);
            }
        }
        else
        {
            if (m_enableLoopCallOnAwake)
            {
                _tiemrID = timer.AddTimerTask(GetData, m_interval, m_requestCount, TimeUnit.Second, m_initialcall);
                _hasTimerTask = true;
            }
        }
    }

    private void Init(TimerSystem timer)
    {
        if (m_enableLoopCallOnAwake)
        {
            _tiemrID = timer.AddTimerTask(GetData, m_interval, m_requestCount, TimeUnit.Second, m_initialcall);
            _hasTimerTask = true;
        }
    }

    protected virtual void OnDestroy()
    {
        if (m_autoListener)
        {
            IOCC.RemoveListener<TimerSystem>("Timer", Init);
        }

        if (_hasTimerTask && IOCC.TryGet<TimerSystem>("Timer", out var timer))
        {
            timer.DeleteTimeTask(_tiemrID.id);
            _hasTimerTask = false;
        }
    }

    public virtual void SetEnable(bool enable)
    {
        if (enable)
        {
            if (!IOCC.TryGet<TimerSystem>("Timer", out var timer))
            {
                Debug.LogError("未配置定时器，无法启用请求任务。", this.gameObject);
                return;
            }

            _tiemrID = timer.AddTimerTask(GetData, m_interval, m_requestCount, TimeUnit.Second, m_initialcall);
            _hasTimerTask = true;
        }
        else
        {
            if (_hasTimerTask && IOCC.TryGet<TimerSystem>("Timer", out var timer))
            {
                timer.DeleteTimeTask(_tiemrID.id);
                _hasTimerTask = false;
            }
        }
    }

    protected abstract void GetData(int id);

    public virtual void SendRequestTest()
    {
        Debug.Log("(若需要测试请重写此方法) 测试接口： " + this.name);
    }
}
