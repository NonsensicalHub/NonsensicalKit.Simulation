using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace NonsensicalKit.Simulation.Mission
{
    [RequireComponent(typeof(Collider))]
    public class PlayerMoveMissionArea : NonsensicalMono
    {
        [SerializeField] private string m_missionID;

        [FormerlySerializedAs("m_Enter")] [SerializeField]
        private UnityEvent m_enter;

        private MissionSystem _missionSystem;
        private Collider _collider;
        private bool _running;

        private void Awake()
        {
            _missionSystem = ServiceCore.Get<MissionSystem>();
            _collider = GetComponent<Collider>();
            if (_collider == null)
            {
                return;
            }

            _collider.enabled = false;
            Subscribe<string>("StartMission", MissingTypeText.PlayerMove, OnStartMission);
            Subscribe<string>("StopMission", MissingTypeText.PlayerMove, OnStopMission);
        }

        private void OnStartMission(string missionID)
        {
            if (missionID == m_missionID)
            {
                if (_collider == null) return;
                _collider.enabled = true;
                _running = true;
            }
        }

        private void OnStopMission(string missionID)
        {
            if (missionID == m_missionID)
            {
                if (_collider == null) return;
                _collider.enabled = false;
                _running = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_running)
            {
                if (other.CompareTag("Player"))
                {
                    m_enter?.Invoke();
                    _missionSystem.MissionCompleted(m_missionID);
                }
            }
        }
    }
}
