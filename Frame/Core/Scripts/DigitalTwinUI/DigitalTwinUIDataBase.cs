using NonsensicalKit.Core.Service.Config;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DigitalTwinUIDataBase : ConfigData
{
    /// <summary>
    /// 在哪些节点会显示
    /// </summary>
    public string[] ShowNodes;
}
