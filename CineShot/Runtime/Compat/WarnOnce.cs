using UnityEngine;

namespace CineShot.Compat
{
    /// <summary>同一条兼容性警告只打一次（编辑器内按会话记忆）。</summary>
    public static class WarnOnce
    {
#if !UNITY_EDITOR
        static readonly System.Collections.Generic.HashSet<string> s_Runtime = new System.Collections.Generic.HashSet<string>();
#endif

        public static void Log(string id, string message)
        {
            if (string.IsNullOrEmpty(id))
                id = message;

#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("CineShot.Warn." + id, false))
                return;
            UnityEditor.SessionState.SetBool("CineShot.Warn." + id, true);
#else
            if (!s_Runtime.Add(id))
                return;
#endif
            Debug.LogWarning(message);
        }
    }
}
