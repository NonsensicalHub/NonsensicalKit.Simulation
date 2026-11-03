using System;
using System.Collections.Generic;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

[CreateAssetMenu(fileName = "CameraControlConfig", menuName = "ScriptableObjects/CameraControlConfig")]
public class CameraControl : ConfigObject
{   
    [ContextMenuItem("设为默认值","ResetValue")]
    public CameraControlData data;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as CameraControlData;
    }
    
    private void ResetValue()
    {
        foreach (var item in data.cameraConfigs)
        {
            item.SetDefault();
        }
    }
}

[Serializable]
public class CameraControlData : ConfigData
{
    public List<CameraConfig> cameraConfigs = new List<CameraConfig>();
    
}   

[Serializable]
public class CameraConfig
{
    public string CameraID;

    public float minPitch;
    public float maxPitch;
    public float minDistance;
    public float maxDistance;
    public float moveSpeedMin;
    public float moveSpeedMax;
    public float rotationSpeed;
    public float zoomSpeed;
    public float dragZoomSpeed;
    public bool checkUI;
    
    public void SetDefault()
    {
        this.CameraID = Guid.NewGuid().ToString()[..8];
        this.minPitch = -90.0f;
        this.maxPitch = 90f;
        this.minDistance = 1;
        this.maxDistance = 100f;
        this.moveSpeedMin = 1;
        this.moveSpeedMax = 10;
        this.rotationSpeed = 1;
        this.zoomSpeed = 1.5f;
        this.dragZoomSpeed = 1f;
        this.checkUI = true;
    }
}
