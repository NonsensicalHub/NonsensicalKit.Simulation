using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Tools.ObjectPool;
using UnityEngine;


namespace Frame.VideoPlayer
{
    /// <summary>
    /// 监控视频播放器对象池管理器。
    /// 参考 <see cref="JKManager"/> 的实现：用对象池复用播放器预制体，避免频繁 Instantiate/Destroy。
    /// </summary>
    public class VideoPlayerManager : NonsensicalMono
    {
        [SerializeField] private bool m_useBuildInVideoPlayer;
        [SerializeField] private GameObject playerPrefab;

        [Tooltip("激活播放器的挂载父节点（一般是 UI 层级下的某个容器）")]
        [SerializeField] private Transform activeParent;

        [Tooltip("回收播放器的挂载父节点（建议是隐藏/禁用的节点）")]
        [SerializeField] private Transform inactiveParent;

        [SerializeField] private bool keepWorldPositionWhenReparent = false;

        private GameObjectPool _pool;
        private readonly HashSet<GameObject> _active = new HashSet<GameObject>();

        private void Awake()
        {
            if (playerPrefab == null)
            {
                Debug.LogError($"{nameof(VideoPlayerManager)}: 未指定 playerPrefab，无法创建播放器对象池。", this);
                return;
            }

            if (m_useBuildInVideoPlayer)
            {
                Destroy(playerPrefab.GetComponent<Frame.VideoPlayer.CustomVLCPlayer>());
            }

            else
            {
                Destroy(playerPrefab.GetComponent<BuildInVideoPlayer>());
            }

            _pool = new GameObjectPool(playerPrefab);
        }

        public IVideoPlayer GetVideoPlayer()
        {
            if (_pool == null) return null;

            var go = _pool.New();
            if (go == null)
            {
                Debug.LogError($"{nameof(VideoPlayerManager)}: 对象池创建失败", this);
                return null;
            }

            go.transform.SetParent(activeParent, keepWorldPositionWhenReparent);

            // 确保组件有效
            var component = go.GetComponent<IVideoPlayer>();
            if (component == null)
            {
                Debug.LogError($"{nameof(VideoPlayerManager)}: 播放器缺少组件 {nameof(IVideoPlayer)}", go);
                StoreObj(go);
                return null;
            }

            _active.Add(go);
            return component;
        }

        /// <summary>
        /// 从对象池获取一个播放器实例，并挂到 activeParent。
        /// </summary>
        public T GetObj<T>() where T : Component
        {
            if (_pool == null) return null;

            var go = _pool.New();
            if (go == null)
            {
                Debug.LogError($"{nameof(VideoPlayerManager)}: 对象池创建失败", this);
                return null;
            }

            go.transform.SetParent(activeParent, keepWorldPositionWhenReparent);

            // 确保组件有效
            var component = go.GetComponent<T>();
            if (component == null)
            {
                Debug.LogError($"{nameof(VideoPlayerManager)}: 播放器缺少组件 {typeof(T).Name}", go);
                StoreObj(go);
                return null;
            }

            _active.Add(go);
            return component;
        }

        /// <summary>
        /// 回收播放器实例，挂到 inactiveParent，并归还对象池。
        /// </summary>
        public void StoreObj(GameObject obj)
        {
            if (obj == null || _pool == null) return;


            if (inactiveParent != null)
            {
                obj.transform.SetParent(inactiveParent, keepWorldPositionWhenReparent);
            }

            _active.Remove(obj);
            _pool.Store(obj);
        }

        /// <summary>
        /// 回收当前所有已借出的播放器实例。
        /// </summary>
        public void StoreAllActive()
        {
            if (_active.Count == 0) return;

            var temp = ListPool<GameObject>.Get();
            temp.AddRange(_active);
            foreach (var go in temp)
            {
                StoreObj(go);
            }

            ListPool<GameObject>.Release(temp);
        }

        private void OnDisable()
        {
            // UI 关闭时直接回收，避免视频继续播放或纹理引用泄漏。
            StoreAllActive();
        }


        /// <summary>
        /// 极简 ListPool，避免每次 StoreAllActive 分配 GC。
        /// </summary>
        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new Stack<List<T>>(4);

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>(8);
            }

            public static void Release(List<T> list)
            {
                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
