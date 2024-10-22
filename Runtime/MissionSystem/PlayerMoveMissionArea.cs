using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.Simulation.Mission
{
    public class PlayerMoveMissionArea : NonsensicalMono
    {
        [SerializeField] private string m_missionID;

        [SerializeField] private UnityEvent m_Enter;

        private MissionSystem _missionSystem;
        private Collider _collider;
        private bool Running;

        private void Awake()
        {
            _missionSystem = ServiceCore.Get<MissionSystem>();
            _collider = GetComponent<Collider>();
            _collider.enabled = false;
            Subscribe<string>("StartMission", MissingTypeText.PlayerMove, OnStartMission);
            Subscribe<string>("StopMission", MissingTypeText.PlayerMove, OnStopMission);
        }

        private void OnStartMission(string missionID)
        {
            if (missionID == m_missionID)
            {
                _collider.enabled = true;
                Running = true;
            }
        }

        private void OnStopMission(string missionID)
        {
            if (missionID == m_missionID)
            {
                _collider.enabled = false;
                Running = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Running)
            {
                if (other.tag == "Player")
                {
                    m_Enter?.Invoke();
                    _missionSystem.MissionCompleted(m_missionID);
                }
            }
        }
    }
}
