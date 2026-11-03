using System.Collections.Generic;
using UnityEngine;

namespace Framework.Core
{
    // =========================================================================
    //  单条场景声明
    // =========================================================================
    [System.Serializable]
    public class SubSceneEntry
    {
        [Tooltip("必须与 Build Settings 中的场景名一致")]
        public string SceneName;

        [Tooltip("依赖的场景会在本场景之前自动加载")]
        public List<string> Dependencies = new();

        [Tooltip("勾选后游戏启动时自动加载该场景")]
        public bool AutoLoadOnStart;
        
        [Tooltip("勾选后加载后将其设为主场景")]
        public bool MainScene;


        [Tooltip("勾选后退出时不自动卸载（常驻场景，如 UI_HUD）")]
        public bool Persistent;
    }
    
    // =========================================================================
    //  SubSceneConfig — 在 Project 右键 → Create → Framework → SubScene Config
    // =========================================================================
    [CreateAssetMenu(menuName = "Framework/SubScene Config", fileName = "SubSceneConfig")]
    public class SubSceneConfig : ScriptableObject
    {
        [Tooltip("所有子场景的声明表，业务开发者只需在这里填写")]
        public List<SubSceneEntry> Scenes = new();

        /// <summary>按名称查找条目</summary>
        public SubSceneEntry Find(string sceneName)
            => Scenes.Find(e => e.SceneName == sceneName);
    }
}
