using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.Simulation.Mission
{
    [RequireComponent(typeof(Collider))]
    public class ObjectMoveMissionArea : NonsensicalMono
    {
        [SerializeField] private ObjectMoveMissionData m_data;
        [SerializeField] private UnityEvent m_enter;
        [SerializeField] private UnityEvent m_exit;

        private MissionSystem _missionSystem;
        private Collider _collider;
        private bool _isRunning;

        private int _number;

        private void Awake()
        {
            _missionSystem = ServiceCore.Get<MissionSystem>();
            _collider = GetComponent<Collider>();
            if (_collider == null || m_data == null)
            {
                return;
            }

            _collider.enabled = false;
            Subscribe<string>("StartMission", MissingTypeText.ObjectMove, OnStartMission);
            Subscribe<string>("StopMission", MissingTypeText.ObjectMove, OnStopMission);
        }

        private void OnStartMission(string missionID)
        {
            if (m_data == null || _collider == null) return;
            if (missionID == m_data.MissionID)
            {
                _collider.enabled = true;
                _isRunning = true;
            }
        }

        private void OnStopMission(string missionID)
        {
            if (m_data == null || _collider == null) return;
            if (missionID == m_data.MissionID)
            {
                _collider.enabled = false;
                _isRunning = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_data == null) return;
            if (_isRunning)
            {
                var v = other.GetComponent<ObjectMoveMissionObject>();
                if (v != null && v.ObjectID == m_data.TargetObjectID)
                {
                    _number++;
                    if (_number >= m_data.RequiredQuantity)
                    {
                        m_enter?.Invoke();
                        _missionSystem.MissionCompleted(m_data.MissionID);
                    }
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (m_data == null) return;
            if (_isRunning)
            {
                var v = other.GetComponent<ObjectMoveMissionObject>();
                if (v != null && v.ObjectID == m_data.TargetObjectID)
                {
                    _number--;
                }
            }
        }
    }
}
