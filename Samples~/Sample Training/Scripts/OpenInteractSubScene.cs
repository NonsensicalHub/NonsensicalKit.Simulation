using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Tools.LogicNodeTreeSystem;
using UnityEngine;

public class OpenInteractSubScene : NonsensicalMono
{
    [SerializeField] private string m_sceneName;
    [SerializeField] private string m_logicNodeName;

    private GameObject _go;

    private string _logicNodeBuffer;
    private string _missionIDBuffer;

    public void OpenSubScene(string missionID)
    {
        IOCC.Set<string>("interactSubSceneMissionID", missionID);

        _go = Instantiate( Resources.Load<GameObject>("Play A Ball"));

        _logicNodeBuffer = ServiceCore.Get<LogicNodeManager>().CrtSelectNode.NodeID;
        ServiceCore.Get<LogicNodeManager>().SwitchNode(m_logicNodeName);
        _missionIDBuffer = missionID;
        Subscribe<bool>("InteractCompleted", missionID, OnSubSceneCompleted);
    }

    private void OnSubSceneCompleted(bool playerWin)
    {
        Destroy(_go);
        ServiceCore.Get<LogicNodeManager>().SwitchNode(_logicNodeBuffer);
        Unsubscribe<bool>("InteractCompleted", _missionIDBuffer, OnSubSceneCompleted);
    }
}
