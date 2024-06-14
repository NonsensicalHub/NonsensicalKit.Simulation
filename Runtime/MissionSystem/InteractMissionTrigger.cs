using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;

public class InteractMissionTrigger : NonsensicalMono
{
    [SerializeField] private string m_missionID;

    private MissionSystem _missionSystem;
    private bool Running;

    private void Awake()
    {
        _missionSystem = ServiceCore.Get<MissionSystem>();
        Subscribe<string>("StartMission", MissingTypeText.Interact, OnStartMission);
        Subscribe<string>("StopMission", MissingTypeText.Interact, OnStopMission);
        Subscribe<bool>("InteractCompleted", m_missionID, InteractCompleted);
    }

    private void OnStartMission(string missionID)
    {
        if (missionID == m_missionID)
        {
            Running = true;
        }
    }

    private void OnStopMission(string missionID)
    {
        if (missionID == m_missionID)
        {
            Running = false;
        }
    }

    public void InteractCompleted(bool playerWin)
    {
        if (playerWin&&Running)
        {
            _missionSystem.MissionCompleted(m_missionID);
        }
    }
}
