using System;
using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Asset;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frame.Core
{
public class ABAssetLoader : NonsensicalMono
{
    private Dictionary<string, Action<string, GameObject>> _callbacks = new Dictionary<string, Action<string, GameObject>>();
    private AssetBundleService _assetBundleService;

    private int count = 0;

    private void Awake()
    {
        count = 0;
        //IOCC.Set<int>("sceneCount", count);
        ServiceCore.SafeGet<AssetBundleService>(GetService);
        Subscribe<string, string, Action<string, GameObject>>("LoadABResource", LoadABResource);
        Subscribe<string[]>("LoadABundleScene", LoadSceneResource);
        Subscribe("sceneLoadCountAdd", AddSceneLoadCount);
    }

    private void AddSceneLoadCount()
    {
        count++;
        IOCC.Set<int>("sceneCount", count);
    }

    private void GetService(AssetBundleService obj)
    {
        _assetBundleService = obj;
        // _assetBundleService.OnBundleCompleted += LoadScene;
    }

    private void OnApplicationQuit()
    {
        _assetBundleService?.UnloadAllBundles();
    }

    private void LoadSceneResource(string[] boundlePaths)

    {
        if (_assetBundleService == null)
        {
            Debug.LogError("ABAssetLoader: AssetBundleService 未初始化，无法加载场景资源。", this);
            return;
        }

        foreach (var scenePath in boundlePaths)
        {
            _assetBundleService.LoadAssetBundle(scenePath);
            StartCoroutine(WaitToLoadScene(scenePath));
        }
    }

    private IEnumerator WaitToLoadScene(string sceneName)
    {
        if (_assetBundleService == null)
        {
            Debug.LogError("ABAssetLoader: AssetBundleService 未初始化，终止场景加载。", this);
            yield break;
        }

        yield return new WaitUntil(() => _assetBundleService.CheckAssetBundle(sceneName));

        Debug.Log($"————————开始加载场景———{sceneName} ———");
        AsyncOperation a = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (a == null)
        {
            Debug.LogError($"ABAssetLoader: 场景加载失败，场景名或路径无效: {sceneName}", this);
            yield break;
        }

        yield return new WaitUntil(() => a.isDone);
        IOCC.Publish("sceneLoadCountAdd");
    }


    #region 加载物体

    private void LoadABResource(string bundlePath, string resourceName, Action<string, GameObject> callback)
    {
        if (callback != null)
        {
            _callbacks.ActionAdd(bundlePath + resourceName, callback);
        }

        Publish("transferStart", resourceName, "下载" + resourceName);
        ServiceCore.Get<AssetBundleService>().LoadResource<GameObject>(resourceName, bundlePath, OnLoadCompleted, OnResourceLoading);
    }

    private void OnResourceLoading(string bundlePath, string resourceName, float process)
    {
        Publish("transferProcess", resourceName, process);
    }

    private void OnLoadCompleted(string bundlePath, string resourceName, GameObject crtParticle)
    {
        Publish("transferEnd", resourceName);
        if (crtParticle != null)
        {
            var key = bundlePath + resourceName;
            if (_callbacks.ContainsKey(key))
            {
                if (_callbacks[key] != null)
                {
                    foreach (Action<string, GameObject> eh in _callbacks[key].GetInvocationList())
                    {
                        GameObject newParticle = Instantiate(crtParticle);
                        newParticle.SetActive(true);
                        eh?.Invoke(resourceName, newParticle);
                    }

                    _callbacks.Remove(key);
                }
            }
        }
    }

    #endregion
}
}
