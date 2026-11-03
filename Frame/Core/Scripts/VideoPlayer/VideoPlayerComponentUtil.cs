using UnityEngine;
using UnityVideoPlayer = UnityEngine.Video.VideoPlayer;

namespace Frame.Core.VideoPlayer
{
    /// <summary>
    /// Core 侧播放器组件选择：不引用具体 VLC 类型，便于可选 VLC 程序集在缺失时 Core 仍可编译。
    /// </summary>
    public static class VideoPlayerComponentUtil
    {
        /// <summary>去掉非内置的 <see cref="IVideoPlayer"/> 实现（如可选第三方播放器组件）。</summary>
        public static void DestroyNonBuildInPlayers(GameObject root)
        {
            if (root == null) return;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IVideoPlayer && mb is not BuildInVideoPlayer)
                    Object.Destroy(mb);
            }
        }

        /// <summary>去掉内置播放器及相关 Unity VideoPlayer，留给外部/可选程序集实现。</summary>
        public static void DestroyBuildInPlayers(GameObject root)
        {
            if (root == null) return;
            foreach (var b in root.GetComponentsInChildren<BuildInVideoPlayer>(true))
                Object.Destroy(b);
            foreach (var v in root.GetComponentsInChildren<UnityVideoPlayer>(true))
                Object.Destroy(v);
        }

        public static IVideoPlayer GetPlayer(GameObject root)
        {
            return root != null ? root.GetComponentInChildren<IVideoPlayer>(true) : null;
        }

        public static IVideoPlayer GetPlayer(UnityEngine.Component root)
        {
            return root != null ? root.GetComponentInChildren<IVideoPlayer>(true) : null;
        }
    }
}
