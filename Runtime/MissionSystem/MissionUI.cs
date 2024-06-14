using NonsensicalKit.Core.Service;
using NonsensicalKit.UGUI.Table;

public class MissionUI : ListTableManager<MissionUIElement, MissionData>
{
    private MissionSystem _missionSystem;

    protected override void Awake()
    {
        base.Awake();
        _missionSystem = ServiceCore.Get<MissionSystem>();
    }

    protected override void Start()
    {
        base.Start();
        OnMissionStatusChanged();
        _missionSystem.OnMissionStatusChanged += OnMissionStatusChanged;
    }

    private void OnMissionStatusChanged()
    {
        var runningMission = _missionSystem.GetRunningMissions();
        if (runningMission.Count > 0)
        {
            OpenSelf();
            UpdateUI(runningMission);
        }
        else
        {
            CloseSelf();
        }
    }
}
