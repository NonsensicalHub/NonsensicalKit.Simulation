 #if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes;
using UnityEngine;

public class RequestTestManager : MonoBehaviour
{
    [Button("测试接口")]
    public void SendRequest()
    {
        List<IRequestTool> components = GetAllComponentsImplementingInterface<IRequestTool>();
        // 调用每个组件的 MyMethod 方法
        foreach (var component in components)
        {
            component.SendRequestTest();
        }
    }

    // 获取场景中所有实现了指定接口的组件
    private List<T> GetAllComponentsImplementingInterface<T>() where T : class
    {
        var results = new List<T>();
        var allGameObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        foreach (var go in allGameObjects)
        {
            var components = go.GetComponents<MonoBehaviour>().OfType<T>();

            if (components != null)
            {
                results.AddRange(components);
            }
        }

        return results;
    }
    
}

#endif
