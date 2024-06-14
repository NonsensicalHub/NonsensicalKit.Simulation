using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ObjectMoveMissionData", menuName = "ScriptableObjects/ObjectMoveMissionData")]
public class ObjectMoveMissionData : ScriptableObject
{
    public string MissionID;        //任务ID
    public string TargetObjectID;
    public int RequiredQuantity;    //所需数量
}
