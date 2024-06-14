using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.DigitalTwin.LogicNodeTreeSystem;
using UnityEngine;

public class OpenInteractSubScene : NonsensicalMono
{
    [SerializeField] private string m_sceneName;
    [SerializeField] private string m_logicNodeName;

    private string _logicNodeBuffer;
    private string _missionIDBuffer;

    public void OpenSubScene(string missionID)
    {
        IOCC.Set<string>("interactSubSceneMissionID", missionID);

        ServiceCore.Get<AddressableManager>().LoadAddressableScene(m_sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);

        _logicNodeBuffer = ServiceCore.Get<LogicNodeManager>().CrtSelectNode.NodeID;
        ServiceCore.Get<LogicNodeManager>().SwitchNode(m_logicNodeName);
        _missionIDBuffer = missionID;
        Subscribe<bool>("InteractCompleted", missionID, OnSubSceneCompleted);
    }

    private void OnSubSceneCompleted(bool playerWin)
    {
        ServiceCore.Get<AddressableManager>().UnLoadAddressableScene(m_sceneName);
        ServiceCore.Get<LogicNodeManager>().SwitchNode(_logicNodeBuffer);
        Unsubscribe<bool>("InteractCompleted", _missionIDBuffer, OnSubSceneCompleted);
    }
}
