using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class AddressableManager : MonoBehaviour, IMonoService
{
    private Dictionary<string, AsyncOperationHandle<SceneInstance>> assDic = new();
    public bool IsReady { get; set; }

    public Action InitCompleted { get; set; }

    public AddressableManager()
    {
        IsReady = true;
        InitCompleted?.Invoke();
    }

    /// <summary>
    /// 加载普通场景
    /// </summary>
    /// <param name="sceneName">加载场景名称</param>
    /// <param name="NeedUnloadScenePath">需要卸载的场景</param>
    /// <param name="loadMode">加载模式</param>
    public void LoadScene(string sceneName, string NeedUnloadScenePath = null, LoadSceneMode loadMode = LoadSceneMode.Single)
    {
        if (NeedUnloadScenePath != null)
        {
            UnLoadAddressableScene(NeedUnloadScenePath);
        }
        StartCoroutine(LoadBuildINScene(sceneName, loadMode));
    }

    /// <summary>
    /// 加载Addressable场景
    /// </summary>
    /// <param name="path">路径</param>
    /// <param name="loadmode">场景添加模式</param>
    /// <param name="needLoadingPanel">需要过渡页面 </param>
    /// <param name="activeOnload">加载完成后激活</param>
    public void LoadAddressableScene(string path, LoadSceneMode loadmode = LoadSceneMode.Additive, bool needLoadingPanel = false, bool activeOnload = true)
    {
        if (needLoadingPanel)
        {
            IOCC.Publish("showLoading", true);
            StartCoroutine(LoadLoadingScene(path, loadmode));
        }
        else
        {
            StartCoroutine(LoadScene(path, loadmode, activeOnload));
        }
    }

    /// <summary>
    /// 卸载场景
    /// </summary>
    /// <param name="path">路径</param>
    /// <param name="unloadSceneOptions">卸载模式</param>
    /// <param name="autoReleaseHandle">是否自动释放handle</param>
    public void UnLoadAddressableScene(string path, UnloadSceneOptions unloadSceneOptions = UnloadSceneOptions.UnloadAllEmbeddedSceneObjects, bool autoReleaseHandle = true)
    {
        StartCoroutine(UnLoadScene(path, unloadSceneOptions, autoReleaseHandle));
    }

    public void ReloadAddressableScene(string path)
    {
        if (SceneManager.sceneCount == 1)
        {
      StartCoroutine(      ForceLoadScene(path, LoadSceneMode.Single,true));
        }
        else
        {
            StartCoroutine(Reload(path));
        }

    }


    /// <summary>
    /// 加载资源
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="path">路径</param>
    /// <param name="callback"></param>
    public void LoadAddressableAsset<T>(string path, UnityAction<T> callback) where T : Object
    {
        var handle = Addressables.LoadAssetAsync<T>(path);
        handle.Completed += (obj) =>
        {
            T temp = obj.Result;
            Debug.Log(temp.name);
            callback(temp);
        };
        //  Addressables.Release(handle);

    }
    /// <summary>
    /// 卸载资源
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="path">路径</param>
    /// <param name="callback"></param>
    public void ReleaseAddressableAsset(string path)
    {
        if (assDic.ContainsKey(path))
        {
            Addressables.Release(assDic[path]);
            assDic.Remove(path);
        }
        else
        {
            Debug.Log($"不存在该资源:{path}");
        }
    }


    #region 场景加卸载协程

    IEnumerator Reload(string path)
    {
        yield return UnLoadScene(path, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects, true);
        StartCoroutine(LoadScene(path, LoadSceneMode.Additive, true));
    }

    IEnumerator LoadBuildINScene(string sceneName, LoadSceneMode loadmod)
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, loadmod);
        while (!operation.isDone)   //当场景没有加载完毕
        {
            Debug.Log((operation.progress * 100).ToString() + "%");
            yield return null;
        }
    }
    IEnumerator ForceLoadScene(string path, LoadSceneMode loadmode, bool activeOnload)
    {
        AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(path, loadmode, activeOnload);
        if (handle.Status == AsyncOperationStatus.Failed)
        {
            Debug.LogError("场景加载异常: " + handle.OperationException.ToString());
            yield break;
        }
        while (!handle.IsDone)
        {
            //var percentage = handle.GetDownloadStatus().Percent;
            //Debug.Log("进度: " + percentage);
            yield return null;
        }

        SceneManager.SetActiveScene(handle.Result.Scene);
        Debug.Log("场景加载完毕");
    }


    IEnumerator LoadScene(string path, LoadSceneMode loadmode, bool activeOnload)
    {
        AsyncOperationHandle<SceneInstance> handle;
        if (assDic.ContainsKey(path))
        {
            handle = assDic[path];
        }
        else
        {
            handle = Addressables.LoadSceneAsync(path, loadmode, activeOnload);
        }
        if (handle.Status == AsyncOperationStatus.Failed)
        {
            Debug.LogError("场景加载异常: " + handle.OperationException.ToString());
            yield break;
        }
        assDic.Add(path, handle);
        while (!handle.IsDone)
        {
            // 进度（0~1）
            // float percentage = handle.PercentComplete;
            var percentage = handle.GetDownloadStatus().Percent;
            Debug.Log("进度: " + percentage);
            yield return null;
        }

        SceneManager.SetActiveScene(handle.Result.Scene);
        Debug.Log("场景加载完毕");
    }

    IEnumerator LoadLoadingScene(string path, LoadSceneMode loadmode)
    {
        AsyncOperationHandle<SceneInstance> handle;
        if (assDic.ContainsKey(path))
        {
            handle = assDic[path];
        }
        else
        {
            handle = Addressables.LoadSceneAsync(path, loadmode, false);
        }
        if (handle.Status == AsyncOperationStatus.Failed)
        {
            Debug.LogError("场景加载异常: " + handle.OperationException.ToString());
            yield break;
        }

        assDic.Add(path, handle);
        float percentage = 0;
        while (!handle.IsDone)
        {
            var vv = handle.GetDownloadStatus();
            percentage = vv.Percent;
            IOCC.Publish("sceneSchedule", percentage);
            Debug.Log("进度: " + percentage);
           // yield return new WaitForEndOfFrame();
            yield return null;
        }
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            handle.Result.ActivateAsync();
            Debug.Log("场景加载完毕");
        }
        else
        {
            Debug.Log("场景加载失败");
        }
    }



    IEnumerator UnLoadScene(string path, UnloadSceneOptions unloadOptions, bool autoReleaseHandle)
    {
        if (assDic.ContainsKey(path))
        {
            var handle = Addressables.UnloadSceneAsync(assDic[path], unloadOptions, autoReleaseHandle);
            if (handle.Status == AsyncOperationStatus.Failed)
            {
                Debug.LogError("场景卸载异常: " + handle.OperationException.ToString());
                yield break;
            }
            Addressables.Release(assDic[path]);
            assDic.Remove(path);


            while (handle.Status == AsyncOperationStatus.None)
            {
                yield return null;
            }
        }
        yield return null;
    }
    #endregion
}


