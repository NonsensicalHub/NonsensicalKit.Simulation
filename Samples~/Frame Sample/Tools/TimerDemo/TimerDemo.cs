using System.Collections.Generic;
using Frame.Core.Tools;
using UnityEngine;

/// <summary>
/// 定时器系统演示脚本 —— 包含全部使用示例。
/// 在 Inspector 中选择 DemoMode 切换场景。
/// </summary>
public class TimerDemo : MonoBehaviour
{
    // ======================== 演示模式选择 ========================

    public enum DemoMode
    {
        IdleDetector,       // 示例1：空闲检测
        SkillCooldown,      // 示例2：技能冷却 + 进度
        DamageOverTime,     // 示例3：DOT 持续伤害
        StressTest          // 示例4：批量压力测试
    }

    [Header("选择演示模式")]
    [SerializeField] private DemoMode mode = DemoMode.IdleDetector;

    // ======================== IdleDetector 参数 ========================

    [Header("── 空闲检测 ──")]
    [SerializeField] private float idleTimeout = 5f;
    [SerializeField] private float signalInterval = 1.5f;   // 模拟信号发送间隔

    // ======================== SkillCooldown 参数 ========================

    [Header("── 技能冷却 ──")]
    [SerializeField] private float cooldownDuration = 3f;

    // ======================== DOT 参数 ========================

    [Header("── 持续伤害 ──")]
    [SerializeField] private float dotTickInterval = 0.5f;
    [SerializeField] private int   dotDamagePerTick = 10;
    [SerializeField] private int   dotTotalTicks = 8;

    // ======================== StressTest 参数 ========================

    [Header("── 压力测试 ──")]
    [SerializeField] private int stressTimerCount = 1000;

    // ======================== 内部状态 ========================

    private TimerHandle _idleTimer;
    private int         _lastState;
    private float       _nextSignalTime;

    private TimerHandle _cooldown;
    private int         _hp = 100;
    private int         _maxHp = 100;

    private TimerHandle _dotTickTimer;
    private int         _dotRemainingTicks;

    private readonly List<TimerHandle> _stressHandles = new();
    private readonly List<string>      _logLines      = new();
    private Vector2 _logScroll;
    private bool    _showLog = true;

    // ======================== 生命周期 ========================

    void Start()
    {
        switch (mode)
        {
            case DemoMode.IdleDetector:     InitIdleDetector();     break;
            case DemoMode.SkillCooldown:    InitSkillCooldown();    break;
            case DemoMode.DamageOverTime:   InitDamageOverTime();   break;
            case DemoMode.StressTest:       InitStressTest();       break;
        }
    }

    void Update()
    {
        switch (mode)
        {
            case DemoMode.IdleDetector:     TickIdleDetector();     break;
            case DemoMode.SkillCooldown:    TickSkillCooldown();    break;
            case DemoMode.DamageOverTime:   TickDamageOverTime();   break;
            case DemoMode.StressTest:       TickStressTest();       break;
        }

        // L 键：显示 / 隐藏日志
        if (Input.GetKeyDown(KeyCode.L)) _showLog = !_showLog;
    }

    void OnDisable()
    {
        // 场景切换或对象销毁时清理所有定时器
        DelayTimerManager.CancelAll();
    }

    // ====================================================================
    //  示例 1：空闲检测
    //  场景：物体持续接收信号，若超时无信号则触发空闲回调。
    //  键位：按 S 手动发送信号。
    // ====================================================================

    void InitIdleDetector()
    {
        _idleTimer    = DelayTimerManager.Schedule(idleTimeout, OnIdle);
        _nextSignalTime = Time.time + signalInterval;
        _lastState    = 0;
        Log("空闲检测启动，等待信号...");
        Log($"超时时间: {idleTimeout}s，自动信号间隔: {signalInterval}s");
        Log("按 [S] 手动发送信号 | 自动信号也会定时发送");
    }

    void TickIdleDetector()
    {
        // 自动信号模拟
        if (Time.time >= _nextSignalTime)
        {
            _nextSignalTime = Time.time + signalInterval;
            ReceiveSignal(Random.Range(1, 100));
        }

        // 手动信号
        if (Input.GetKeyDown(KeyCode.S))
        {
            ReceiveSignal(Random.Range(1, 100));
        }
    }

    void ReceiveSignal(int newState)
    {
        if (_lastState == newState) return;
        _lastState = newState;
        _idleTimer.Reset();
        Log($"[信号] state={newState}，空闲计时已重置 (剩余: {idleTimeout}s)");
    }

    void OnIdle()
    {
        Log($"[空闲] 超过 {idleTimeout}s 无信号！执行自定义函数");
        // 持续检测模式：自动重建
        _idleTimer.Reset();
    }

    // ====================================================================
    //  示例 2：技能冷却 + UI 进度
    //  键位：按 Space 释放技能。
    // ====================================================================

    void InitSkillCooldown()
    {
        _hp    = _maxHp;
        _cooldown = null;
        Log("技能冷却演示");
        Log($"冷却时间: {cooldownDuration}s");
        Log("按 [Space] 释放技能");
    }

    void TickSkillCooldown()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            CastSkill();
    }

    void CastSkill()
    {
        if (_cooldown != null && _cooldown.IsActive)
        {
            Log($"[冷却中] 剩余 {_cooldown.Remaining:F1}s ({_cooldown.Progress:P0})");
            return;
        }

        Log("[技能] 释放！进入冷却");
        _cooldown = DelayTimerManager.Schedule(cooldownDuration, OnCooldownEnd);
    }

    void OnCooldownEnd()
    {
        Log("[技能] 冷却结束，可再次释放");
    }

    // ====================================================================
    //  示例 3：DOT（持续伤害）+ 手动净化
    //  键位：按 A 施加 DOT，按 D 净化。
    // ====================================================================

    void InitDamageOverTime()
    {
        _hp = _maxHp;
        _dotRemainingTicks = 0;
        _dotTickTimer = null;
        Log("DOT 持续伤害演示");
        Log($"每 {dotTickInterval}s 造成 {dotDamagePerTick} 伤害，共 {dotTotalTicks} 次");
        Log("按 [A] 施加 DOT | 按 [D] 净化");
    }

    void TickDamageOverTime()
    {
        if (Input.GetKeyDown(KeyCode.A))
            ApplyDOT();

        if (Input.GetKeyDown(KeyCode.D))
            PurgeDOT();
    }

    void ApplyDOT()
    {
        // 先清除旧的
        _dotTickTimer?.Cancel();

        _dotRemainingTicks = dotTotalTicks;
        _hp = _maxHp;
        _dotTickTimer = DelayTimerManager.Schedule(dotTickInterval, OnDotTick, loop: true);
        Log($"[DOT] 施加！{dotTotalTicks} 次 Tick，HP: {_hp}/{_maxHp}");
    }

    void PurgeDOT()
    {
        if (_dotTickTimer == null || !_dotTickTimer.IsActive)
        {
            Log("[DOT] 当前无 DOT 效果");
            return;
        }
        _dotTickTimer.Cancel();
        _dotTickTimer = null;
        Log($"[净化] DOT 已清除，HP: {_hp}/{_maxHp}");
    }

    void OnDotTick()
    {
        _hp = Mathf.Max(0, _hp - dotDamagePerTick);
        _dotRemainingTicks--;

        Log($"[DOT] Tick！-{dotDamagePerTick} HP，剩余 {_hp}/{_maxHp}，Tick剩余: {_dotRemainingTicks}");

        if (_hp <= 0)
        {
            Log("[DOT] 目标死亡！");
            _dotTickTimer?.Cancel();
            _dotTickTimer = null;
            return;
        }

        if (_dotRemainingTicks <= 0)
        {
            Log($"[DOT] 结束，HP: {_hp}/{_maxHp}");
            _dotTickTimer?.Cancel();
            _dotTickTimer = null;
        }
    }

    // ====================================================================
    //  示例 4：批量压力测试
    //  键位：R 重置 50 个 | N 新增 200 个 | C 清除全部
    // ====================================================================

    void InitStressTest()
    {
        _stressHandles.Clear();
        for (int i = 0; i < stressTimerCount; i++)
        {
            int   idx = i;
            float dur = Random.Range(1f, 15f);
            _stressHandles.Add(DelayTimerManager.Schedule(dur, () =>
            {
                if (idx % 200 == 0)
                    Log($"[Stress] Timer[{idx}] fired");
            }));
        }
        Log($"压力测试：已创建 {DelayTimerManager.ActiveCount} 个定时器");
        Log("[R] 重置 50 个 | [N] 新增 200 个 | [C] 清除全部");
    }

    void TickStressTest()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            for (int i = 0; i < 50 && _stressHandles.Count > 0; i++)
            {
                var h = _stressHandles[Random.Range(0, _stressHandles.Count)];
                h.Reset();
            }
            Log($"[R] 重置 50 个，当前活跃: {DelayTimerManager.ActiveCount}");
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            for (int i = 0; i < 200; i++)
            {
                int idx = _stressHandles.Count;
                _stressHandles.Add(DelayTimerManager.Schedule(
                    Random.Range(0.5f, 5f),
                    () => { if (idx % 200 == 0) Log($"[Stress] Timer[{idx}] fired"); }));
            }
            Log($"[N] 新增 200，当前活跃: {DelayTimerManager.ActiveCount}");
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            DelayTimerManager.CancelAll();
            _stressHandles.Clear();
            Log("[C] 全部清除");
        }
    }

    // ======================== 日志系统 ========================

    private void Log(string msg)
    {
        string line = $"[{Time.time:F2}] {msg}";
        _logLines.Add(line);
        if (_logLines.Count > 100) _logLines.RemoveAt(0);
        Debug.Log(line);
    }

    // ======================== GUI ========================

    private GUIStyle _headerStyle;
    private GUIStyle _boxStyle;

    void OnGUI()
    {
        InitStyles();

        float x = 10f, y = 10f, w = 420f;

        // ── 顶部状态栏 ──
        GUI.Box(new Rect(x, y, w, 50), "");
        GUI.Label(new Rect(x + 10, y + 4, w, 20),
            $"<b>Timer Demo</b>  |  Mode: <b>{mode}</b>  |  Active: <b>{DelayTimerManager.ActiveCount}</b>",
            _headerStyle);

        string hintText = mode switch
        {
            DemoMode.IdleDetector   => "[S] 手动信号  |  自动信号定时发送  |  [L] 日志",
            DemoMode.SkillCooldown  => "[Space] 释放技能  |  [L] 日志",
            DemoMode.DamageOverTime => "[A] 施加 DOT  |  [D] 净化  |  [L] 日志",
            DemoMode.StressTest     => "[R] 重置50  [N] 新增200  [C] 清除  |  [L] 日志",
            _ => ""
        };
        GUI.Label(new Rect(x + 10, y + 26, w, 20), hintText);

        y += 60;

        // ── 模式特定 HUD ──
        switch (mode)
        {
            case DemoMode.IdleDetector:     DrawIdleHUD(ref y, w);    break;
            case DemoMode.SkillCooldown:    DrawSkillHUD(ref y, w);   break;
            case DemoMode.DamageOverTime:   DrawDOTHUD(ref y, w);     break;
            case DemoMode.StressTest:       DrawStressHUD(ref y, w);  break;
        }

        // ── 日志面板 ──
        if (_showLog && _logLines.Count > 0)
        {
            float logH = 260f;
            GUI.Box(new Rect(x, y, w, logH), "");
            GUI.Label(new Rect(x + 8, y + 2, w, 18), "<b>Log</b>", _headerStyle);
            _logScroll = GUI.BeginScrollView(
                new Rect(x, y + 20, w, logH - 24),
                _logScroll,
                new Rect(0, 0, w - 20, _logLines.Count * 18));

            for (int i = 0; i < _logLines.Count; i++)
                GUI.Label(new Rect(4, i * 18, w - 30, 18), _logLines[i]);

            GUI.EndScrollView();
        }
    }

    void DrawIdleHUD(ref float y, float w)
    {
        GUI.Box(new Rect(10, y, w, 48), "");
        float remaining = _idleTimer?.Remaining ?? 0f;
        float progress  = _idleTimer?.Progress  ?? 0f;
        GUI.Label(new Rect(18, y + 4, w, 20),
            $"空闲计时: <b>{remaining:F1}s</b> / {idleTimeout:F1}s  |  state: <b>{_lastState}</b>");
        DrawBar(18, y + 24, w - 16, 16, progress);
        y += 56;
    }

    void DrawSkillHUD(ref float y, float w)
    {
        GUI.Box(new Rect(10, y, w, 48), "");

        bool onCd = _cooldown != null && _cooldown.IsActive;
        string status = onCd
            ? $"冷却中: <b>{_cooldown.Remaining:F1}s</b> ({_cooldown.Progress:P0})"
            : "<b>技能可用</b>";
        GUI.Label(new Rect(18, y + 4, w, 20), status);

        float progress = onCd ? _cooldown.Progress : 1f;
        DrawBar(18, y + 24, w - 16, 16, progress, onCd ? new Color(0.8f, 0.3f, 0.3f) : new Color(0.3f, 0.8f, 0.3f));
        y += 56;
    }

    void DrawDOTHUD(ref float y, float w)
    {
        GUI.Box(new Rect(10, y, w, 68), "");

        bool active = _dotTickTimer != null && _dotTickTimer.IsActive;
        GUI.Label(new Rect(18, y + 4, w, 20),
            $"HP: <b>{_hp}/{_maxHp}</b>  |  DOT: {(active ? $"活跃 (tick剩余: {_dotRemainingTicks})" : "无")}");

        DrawBar(18, y + 24, w - 16, 16, (float)_hp / _maxHp, new Color(0.3f, 0.8f, 0.3f));

        if (active)
        {
            float dotProgress = _dotTickTimer.Progress;
            GUI.Label(new Rect(18, y + 46, w, 16), $"下次 Tick 进度:");
            DrawBar(120, y + 46, w - 128, 16, dotProgress, new Color(0.8f, 0.5f, 0.2f));
        }

        y += 76;
    }

    void DrawStressHUD(ref float y, float w)
    {
        GUI.Box(new Rect(10, y, w, 36), "");
        GUI.Label(new Rect(18, y + 8, w, 20),
            $"活跃定时器: <b>{DelayTimerManager.ActiveCount}</b>  |  Handle 数量: <b>{_stressHandles.Count}</b>");
        y += 44;
    }

    // ── 进度条绘制 ──

    void DrawBar(float x, float y, float w, float h, float fill, Color? color = null)
    {
        Color c = color ?? new Color(0.2f, 0.6f, 1f);

        // 背景
        GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        GUI.Box(new Rect(x, y, w, h), "");
        GUI.backgroundColor = Color.white;

        // 填充
        if (fill > 0f)
        {
            GUI.backgroundColor = c;
            GUI.Box(new Rect(x, y, w * Mathf.Clamp01(fill), h), "");
            GUI.backgroundColor = Color.white;
        }
    }

    void InitStyles()
    {
        if (_headerStyle != null) return;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = 13
        };
    }
}
