using NonsensicalKit.Core.Service;
using UnityEngine;

/// <summary>
/// 挂载在悬浮标签跟随节点上,用于生成标签
/// </summary>
public class DigitalTwinUIPoint : MonoBehaviour
{
    [SerializeField] private string m_Type;
    [SerializeField] private string m_ID;
    [SerializeField] private ShowDagNode m_dagNodeConfig;
    [SerializeField] private bool autoSetID;

    private void Start()
    {
        ServiceCore.SafeGet<DigitalTwinUIManager>(OnGetConfig);
    }

    private void OnGetConfig(DigitalTwinUIManager manager)
    {
        manager.Register(new DigitalTwinUIInfo(gameObject, m_Type, m_ID,m_dagNodeConfig));
    }

    void OnValidate()
    {
        if (autoSetID)
            m_ID = this.name;
    }
}

public class DigitalTwinUIInfo
{
    public GameObject Point;
    public string Type;
    public string ID;
    public ShowDagNode DagNode;

    public DigitalTwinUIInfo(GameObject point, string type, string iD, ShowDagNode showNodes)
    {
        Point = point;
        Type = type;
        ID = iD;
        DagNode = showNodes;
    }
}

[System.Serializable]
public class ShowDagNode
{
    public string DependencyNode;
    public string[] ShowNodes;
}
