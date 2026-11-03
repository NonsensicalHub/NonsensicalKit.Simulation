using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Frame.Core.SubScene
{
    /// <summary>子场景加载实现方式。UniTask 基于 PlayerLoop，WebGL 可用；勿用 Thread/Task.Run。</summary>
    public enum SubSceneLoadBackend { Coroutine, UniTask }
    // =========================================================================
    //  枚举 & 运行时信息（框架内部使用，业务层无需关心）
    // =========================================================================
    public enum SubSceneState { Unloaded, Loading, Loaded, Unloading }

    public class SubSceneInfo
    {
        public string        SceneName;
        public SubSceneState State        = SubSceneState.Unloaded;
        public float         LoadProgress = 0f;
        public Scene         SceneHandle;
        public List<string>  Dependencies;
        public int           RefCount     = 0;
        public bool          Persistent;
        public bool          MainScene;

        public SubSceneInfo(SubSceneEntry entry)
        {
            SceneName    = entry.SceneName;
            Dependencies = new List<string>(entry.Dependencies);
            Persistent   = entry.Persistent;
            MainScene   = entry.MainScene;
        }
    }
    
    // =========================================================================
    //  SubSceneManager
    //  ● 挂到 Core Scene 的常驻 GameObject
    //  ● Inspector 拖入 SubSceneConfig.asset 即可，业务层零代码
    // =========================================================================
    public class SubSceneManager : MonoBehaviour
    {
        // ── 单例 ──────────────────────────────────────────────────────────────
        public static SubSceneManager Instance { get; private set; }

        // ── Inspector 唯一入口 ────────────────────────────────────────────────
        [Header("拖入 SubSceneConfig.asset，业务层无需其他配置")]
        [SerializeField] private SubSceneConfig _config;

        [Header("加载实现")]
        [Tooltip("UniTask：主线程 PlayerLoop 驱动，WebGL 可用。Coroutine：传统协程。")]
        [SerializeField] private SubSceneLoadBackend _loadBackend = SubSceneLoadBackend.UniTask;
        
        [Header("加载中提示")]
        [Tooltip("可选：加载过程中自动显示，加载完成后自动隐藏。")]
        [SerializeField] private GameObject _loadingHint;

        // ── 全局事件（框架内其他系统监听，业务层可忽略）──────────────────────
        public static event Action<string>        OnSceneLoadStart;
        public static event Action<string, float> OnSceneLoadProgress;
        public static event Action<string>        OnSceneLoadComplete;
        public static event Action<string>        OnSceneUnloadStart;
        public static event Action<string>        OnSceneUnloadComplete;
        public static event Action<string, string>OnSceneError;
        
        /// <summary>
        /// 加载状态变化事件：
        /// true = 进入加载中（至少有一个子场景在加载）
        /// false = 结束加载（当前没有子场景在加载）
        /// </summary>
        public static event Action<bool> OnLoadingStateChanged;

        // ── 私有运行时数据 ────────────────────────────────────────────────────
        private readonly Dictionary<string, SubSceneInfo> _registry   = new();
        private readonly HashSet<string>                  _runningOps = new();
        private readonly Dictionary<string, List<Action>> _waitQueue  = new();
        private int _loadingSceneCount = 0;

        private bool UseUniTask => _loadBackend == SubSceneLoadBackend.UniTask;

        // =========================================================================
        //  生命周期
        // =========================================================================
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SetLoadingHintVisible(false);

            InitFromConfig();
        }

        private void Start()
        {
            AutoLoadOnStart();
        }
        
        private void OnDestroy()
        {
            _loadingSceneCount = 0;
            SetLoadingHintVisible(false);
            if (Instance == this)
                Instance = null;
        }

        // ── 读取 SO，完成注册 ─────────────────────────────────────────────────
        private void InitFromConfig()
        {
            if (_config == null)
            {
                Debug.LogError("[SubSceneManager] 未指定 SubSceneConfig，请在 Inspector 拖入配置文件");
                return;
            }

            foreach (var entry in _config.Scenes)
            {
                if (string.IsNullOrEmpty(entry.SceneName)) continue;
                _registry[entry.SceneName] = new SubSceneInfo(entry);
            }

            Debug.Log($"[SubSceneManager] 已注册 {_registry.Count} 个子场景");
        }

        //
        
        // ── 自动加载标记了 AutoLoadOnStart 的场景 ────────────────────────────
        private void AutoLoadOnStart()
        {
            if (_config == null) return;

            foreach (var entry in _config.Scenes)
            {
                if (entry.AutoLoadOnStart)
                    LoadScene(entry.SceneName);
            }
        }
      
        
        // =========================================================================
        //  公开 API（框架内其他系统调用；业务层通过 SO 配置驱动，不直接调用）
        // =========================================================================

        /// <summary>加载子场景（自动处理依赖 + 引用计数）</summary>
        public void LoadScene(string sceneName,
                              Action onComplete  = null,
                              Action<float> onProgress = null)
        {
            if (!EnsureRegistered(sceneName)) return;
            var info = _registry[sceneName];

            if (info.State == SubSceneState.Loaded)
            {
                info.RefCount++;
                onProgress?.Invoke(1f);
                onComplete?.Invoke();
                return;
            }
            if (info.State == SubSceneState.Loading)   { EnqueueWait(sceneName, onComplete); return; }
            if (info.State == SubSceneState.Unloading) { EnqueueWait(sceneName, () => LoadScene(sceneName, onComplete, onProgress)); return; }

            info.RefCount++;
            if (UseUniTask)
                RunLoadUniTask(sceneName, onComplete, onProgress).Forget();
            else
                StartCoroutine(LoadCoroutine(sceneName, onComplete, onProgress));
        }

        /// <summary>异步加载子场景（UniTask，WebGL 安全）</summary>
        public UniTask LoadSceneAsync(string sceneName,
                                      IProgress<float> onProgress = null,
                                      CancellationToken cancellationToken = default)
        {
            if (!EnsureRegistered(sceneName))
                return UniTask.CompletedTask;

            var info = _registry[sceneName];

            if (info.State == SubSceneState.Loaded)
            {
                info.RefCount++;
                onProgress?.Report(1f);
                return UniTask.CompletedTask;
            }

            if (info.State == SubSceneState.Loading)
                return WaitForSceneOpAsync(sceneName, cancellationToken);

            if (info.State == SubSceneState.Unloading)
                return WaitForSceneOpAsync(sceneName, cancellationToken)
                    .ContinueWith(() => LoadSceneAsync(sceneName, onProgress, cancellationToken));

            info.RefCount++;
            return LoadSceneInternalAsync(sceneName, onProgress, cancellationToken);
        }

        /// <summary>卸载子场景（Persistent 场景忽略普通卸载请求）</summary>
        public void UnloadScene(string sceneName,
                                bool   force     = false,
                                Action onComplete = null)
        {
            if (!EnsureRegistered(sceneName)) return;
            var info = _registry[sceneName];

            // Persistent 场景只允许 force 卸载
            if (info.Persistent && !force)
            {
                Debug.Log($"[SubSceneManager] {sceneName} 是常驻场景，跳过卸载");
                onComplete?.Invoke();
                return;
            }

            if (info.State == SubSceneState.Unloaded)  { onComplete?.Invoke(); return; }
            if (info.State == SubSceneState.Unloading) { EnqueueWait(sceneName, onComplete); return; }
            if (info.State == SubSceneState.Loading)   { EnqueueWait(sceneName, () => UnloadScene(sceneName, force, onComplete)); return; }

            if (!force)
            {
                info.RefCount = Mathf.Max(0, info.RefCount - 1);
                if (info.RefCount > 0) { onComplete?.Invoke(); return; }
            }
            else
            {
                info.RefCount = 0;
            }

            if (UseUniTask)
                RunUnloadUniTask(sceneName, onComplete).Forget();
            else
                StartCoroutine(UnloadCoroutine(sceneName, onComplete));
        }

        /// <summary>异步卸载子场景（UniTask，WebGL 安全）</summary>
        public UniTask UnloadSceneAsync(string sceneName,
                                        bool force = false,
                                        CancellationToken cancellationToken = default)
        {
            if (!EnsureRegistered(sceneName))
                return UniTask.CompletedTask;

            var info = _registry[sceneName];

            if (info.Persistent && !force)
            {
                Debug.Log($"[SubSceneManager] {sceneName} 是常驻场景，跳过卸载");
                return UniTask.CompletedTask;
            }

            if (info.State == SubSceneState.Unloaded)
                return UniTask.CompletedTask;

            if (info.State == SubSceneState.Unloading)
                return WaitForSceneOpAsync(sceneName, cancellationToken);

            if (info.State == SubSceneState.Loading)
                return WaitForSceneOpAsync(sceneName, cancellationToken)
                    .ContinueWith(() => UnloadSceneAsync(sceneName, force, cancellationToken));

            if (!force)
            {
                info.RefCount = Mathf.Max(0, info.RefCount - 1);
                if (info.RefCount > 0)
                    return UniTask.CompletedTask;
            }
            else
            {
                info.RefCount = 0;
            }

            return UnloadSceneInternalAsync(sceneName, cancellationToken);
        }

        /// <summary>切换场景（卸载旧场景后加载新场景）</summary>
        public void SwitchScene(string oldScene, string newScene,
                                Action onComplete = null, Action<float> onProgress = null)
        {
            if (UseUniTask)
                RunSwitchUniTask(oldScene, newScene, onComplete, onProgress).Forget();
            else
                UnloadScene(oldScene, force: true,
                    onComplete: () => LoadScene(newScene, onComplete, onProgress));
        }

        /// <summary>异步切换场景（UniTask，WebGL 安全）</summary>
        public async UniTask SwitchSceneAsync(string oldScene, string newScene,
                                              IProgress<float> onProgress = null,
                                              CancellationToken cancellationToken = default)
        {
            await UnloadSceneAsync(oldScene, force: true, cancellationToken);
            await LoadSceneAsync(newScene, onProgress, cancellationToken);
        }

        // ── 状态查询 ──────────────────────────────────────────────────────────
        public SubSceneState GetState(string sceneName)
            => _registry.TryGetValue(sceneName, out var i) ? i.State : SubSceneState.Unloaded;

        public float GetProgress(string sceneName)
            => _registry.TryGetValue(sceneName, out var i) ? i.LoadProgress : 0f;

        public bool IsLoaded(string sceneName)
            => GetState(sceneName) == SubSceneState.Loaded;
        
        public SubSceneInfo GetInfo(string sceneName)
        => _registry.TryGetValue(sceneName, out var i) ? i : null;
        
        public bool IsAnySceneLoading => _loadingSceneCount > 0;

        // =========================================================================
        //  UniTask（主线程 PlayerLoop，无 Thread）
        // =========================================================================
        private async UniTaskVoid RunLoadUniTask(string sceneName, Action onComplete, Action<float> onProgress)
        {
            try
            {
                await LoadSceneInternalAsync(sceneName, ActionProgress.Create(onProgress),
                    this.GetCancellationTokenOnDestroy());
                onComplete?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async UniTaskVoid RunUnloadUniTask(string sceneName, Action onComplete)
        {
            try
            {
                await UnloadSceneInternalAsync(sceneName, this.GetCancellationTokenOnDestroy());
                onComplete?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async UniTaskVoid RunSwitchUniTask(string oldScene, string newScene,
                                                   Action onComplete, Action<float> onProgress)
        {
            try
            {
                await SwitchSceneAsync(oldScene, newScene, ActionProgress.Create(onProgress),
                    this.GetCancellationTokenOnDestroy());
                onComplete?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async UniTask LoadSceneInternalAsync(string sceneName,
                                                     IProgress<float> onProgress,
                                                     CancellationToken cancellationToken)
        {
            _runningOps.Add(sceneName);
            BeginLoading();
            try
            {
                var info = _registry[sceneName];
                info.State = SubSceneState.Loading;
                info.LoadProgress = 0f;
                OnSceneLoadStart?.Invoke(sceneName);

                foreach (var dep in info.Dependencies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsLoaded(dep))
                        await LoadSceneAsync(dep, cancellationToken: cancellationToken);
                }

                var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (op == null)
                {
                    var err = $"{sceneName} 不在 Build Settings 中";
                    Debug.LogError($"[SubSceneManager] {err}");
                    OnSceneError?.Invoke(sceneName, err);
                    info.State = SubSceneState.Unloaded;
                    info.RefCount = Mathf.Max(0, info.RefCount - 1);
                    throw new InvalidOperationException(err);
                }

                op.allowSceneActivation = false;
                while (op.progress < 0.9f)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float p = op.progress / 0.9f;
                    info.LoadProgress = p;
                    onProgress?.Report(p);
                    OnSceneLoadProgress?.Invoke(sceneName, p);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                op.allowSceneActivation = true;
                await op.ToUniTask(cancellationToken: cancellationToken);

                info.LoadProgress = 1f;
                info.SceneHandle = SceneManager.GetSceneByName(sceneName);
                info.State = SubSceneState.Loaded;

                onProgress?.Report(1f);
                OnSceneLoadProgress?.Invoke(sceneName, 1f);
                OnSceneLoadComplete?.Invoke(sceneName);
                Debug.Log($"[SubSceneManager] 加载完成: {sceneName}");

                if (info.MainScene)
                    SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
            }
            finally
            {
                EndLoading();
                _runningOps.Remove(sceneName);
                FlushWaitQueue(sceneName);
            }
        }

        private async UniTask UnloadSceneInternalAsync(string sceneName, CancellationToken cancellationToken)
        {
            _runningOps.Add(sceneName);
            try
            {
                var info = _registry[sceneName];
                info.State = SubSceneState.Unloading;
                OnSceneUnloadStart?.Invoke(sceneName);

                var op = SceneManager.UnloadSceneAsync(sceneName);
                if (op != null)
                    await op.ToUniTask(cancellationToken: cancellationToken);

                info.State = SubSceneState.Unloaded;
                info.LoadProgress = 0f;
                info.SceneHandle = default;

                OnSceneUnloadComplete?.Invoke(sceneName);
                Debug.Log($"[SubSceneManager] 卸载完成: {sceneName}");
            }
            finally
            {
                _runningOps.Remove(sceneName);
                FlushWaitQueue(sceneName);
            }
        }

        private async UniTask WaitForSceneOpAsync(string sceneName, CancellationToken cancellationToken)
        {
            var tcs = new UniTaskCompletionSource();
            EnqueueWait(sceneName, () => tcs.TrySetResult());
            await tcs.Task.AttachExternalCancellation(cancellationToken);
        }

        // =========================================================================
        //  协程
        // =========================================================================
        private IEnumerator LoadCoroutine(string sceneName,
                                          Action onComplete, Action<float> onProgress)
        {
            _runningOps.Add(sceneName);
            BeginLoading();
            var info = _registry[sceneName];
            info.State = SubSceneState.Loading;
            info.LoadProgress = 0f;
            OnSceneLoadStart?.Invoke(sceneName);

            // 1. 前置加载依赖
            foreach (var dep in info.Dependencies)
            {
                if (!IsLoaded(dep))
                {
                    bool done = false;
                    LoadScene(dep, onComplete: () => done = true);
                    yield return new WaitUntil(() => done);
                }
            }

            // 2. 异步加载本体
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                var err = $"{sceneName} 不在 Build Settings 中";
                Debug.LogError($"[SubSceneManager] {err}");
                OnSceneError?.Invoke(sceneName, err);
                info.State = SubSceneState.Unloaded;
                info.RefCount = Mathf.Max(0, info.RefCount - 1);
                EndLoading();
                _runningOps.Remove(sceneName);
                yield break;
            }

            op.allowSceneActivation = false;
            while (op.progress < 0.9f)
            {
                float p = op.progress / 0.9f;
                info.LoadProgress = p;
                onProgress?.Invoke(p);
                OnSceneLoadProgress?.Invoke(sceneName, p);
                yield return null;
            }
            op.allowSceneActivation = true;
            yield return op;

            info.LoadProgress = 1f;
            info.SceneHandle  = SceneManager.GetSceneByName(sceneName);
            info.State        = SubSceneState.Loaded;

            onProgress?.Invoke(1f);
            OnSceneLoadProgress?.Invoke(sceneName, 1f);
            OnSceneLoadComplete?.Invoke(sceneName);
            Debug.Log($"[SubSceneManager] 加载完成: {sceneName}");

            if (info.MainScene)
            {
                Scene scene = SceneManager.GetSceneByName(sceneName);
                SceneManager.SetActiveScene(scene);
            }
            onComplete?.Invoke();
            FlushWaitQueue(sceneName);
            EndLoading();
            _runningOps.Remove(sceneName);
        }

        private IEnumerator UnloadCoroutine(string sceneName, Action onComplete)
        {
            _runningOps.Add(sceneName);
            var info = _registry[sceneName];
            info.State = SubSceneState.Unloading;
            OnSceneUnloadStart?.Invoke(sceneName);

            var op = SceneManager.UnloadSceneAsync(sceneName);
            if (op != null) yield return op;

            info.State        = SubSceneState.Unloaded;
            info.LoadProgress = 0f;
            info.SceneHandle  = default;

            OnSceneUnloadComplete?.Invoke(sceneName);
            Debug.Log($"[SubSceneManager] 卸载完成: {sceneName}");

            onComplete?.Invoke();
            FlushWaitQueue(sceneName);
            _runningOps.Remove(sceneName);
        }

        // =========================================================================
        //  工具方法
        // =========================================================================
        private bool EnsureRegistered(string sceneName)
        {
            if (_registry.ContainsKey(sceneName)) return true;
            Debug.LogError($"[SubSceneManager] 场景 [{sceneName}] 未在 SubSceneConfig 中声明，请先在 SO 配置文件里添加");
            return false;   // 不再自动注册，强制要求配置驱动
        }

        private void EnqueueWait(string sceneName, Action cb)
        {
            if (cb == null) return;
            if (!_waitQueue.ContainsKey(sceneName)) _waitQueue[sceneName] = new();
            _waitQueue[sceneName].Add(cb);
        }

        private void FlushWaitQueue(string sceneName)
        {
            if (!_waitQueue.TryGetValue(sceneName, out var q)) return;
            var copy = new List<Action>(q);
            q.Clear();
            foreach (var cb in copy) cb?.Invoke();
        }
        
        private void BeginLoading()
        {
            _loadingSceneCount++;
            if (_loadingSceneCount == 1)
            {
                SetLoadingHintVisible(true);
                OnLoadingStateChanged?.Invoke(true);
            }
        }
        
        private void EndLoading()
        {
            _loadingSceneCount = Mathf.Max(0, _loadingSceneCount - 1);
            if (_loadingSceneCount == 0)
            {
                SetLoadingHintVisible(false);
                OnLoadingStateChanged?.Invoke(false);
            }
        }
        
        private void SetLoadingHintVisible(bool visible)
        {
            if (_loadingHint != null)
                _loadingHint.SetActive(visible);
        }

        sealed class ActionProgress : IProgress<float>
        {
            readonly Action<float> _action;
            ActionProgress(Action<float> action) => _action = action;
            public static IProgress<float> Create(Action<float> action)
                => action == null ? null : new ActionProgress(action);
            public void Report(float value) => _action?.Invoke(value);
        }

#if UNITY_EDITOR
        [ContextMenu("打印所有场景状态")]
        public void DebugPrintAllStates()
        {
            Debug.Log("===== SubSceneManager =====");
            foreach (var kv in _registry)
            {
                var i = kv.Value;
                Debug.Log($"[{i.State,-10}] {i.SceneName}  Ref={i.RefCount}  " +
                          $"Persistent={i.Persistent}  Progress={i.LoadProgress:P0}");
            }
        }
#endif
    }
}