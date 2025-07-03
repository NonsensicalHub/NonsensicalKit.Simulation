using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.Simulation.Logistics
{
    public enum PorterState
    {
        Idle,
        Take,
        Store,
        Back,
        Control,
    }

    public class PortageMission
    {
        public string TakeID;
        public string StoreID;
        public string ItemID;
        public int Number;
    }

    public abstract class Porter : UnitBase
    {
        [SerializeField] protected PorterState m_state;

        private Vector3 _startPosition;
        private Quaternion _startRotation;

        private Queue<PortageMission> _missions=new Queue<PortageMission>();
        private PortageMission _currentMission;
        private Storage _currentTarget;

        private float _timer;

        protected override void Awake()
        {
            base.Awake();
            _startPosition = transform.position;
            _startRotation = transform.rotation;
        }

        protected virtual  void Update()
        {
            switch (m_state)
            {
                case PorterState.Idle: Idle(); break;
                case PorterState.Take: GoTake(); break;
                case PorterState.Store: GoStore(); break;
                case PorterState.Back: Backing(); break;
                case PorterState.Control:  break;
            }
        }

        public void Portage(PortageMission mission)
        {
            _missions.Enqueue(mission);
            if (m_state is not (PorterState.Take or PorterState.Store))
            {
                StartNewMission();
            }
        }

        public void StartNewMission()
        {
            if (_missions.Count != 0)
            {
                _currentMission = _missions.Dequeue();

                _currentTarget = IOCC.Get<UnitBase>(_currentMission.TakeID) as Storage;

                if (_currentTarget != null)
                {
                    m_state = PorterState.Take;
                    return;
                }
            }

            if (m_state is PorterState.Take or PorterState.Store)
            {
                m_state = PorterState.Back;
            }
        }

        protected virtual void Idle()
        {
            //DoNothing
        }

        protected virtual void GoTake()
        {
            MoveToTarget(_currentTarget.AccessPos, _currentTarget.AccessRot);
            var distance = Vector3.Distance(_currentTarget.AccessPos, transform.position);
            if (distance < 0.5f)
            {
                _timer += Time.deltaTime;
            }

            if (_timer >= 0.5f)
            {
                if (_currentTarget.TakeItem(_currentMission.ItemID, _currentMission.Number))
                {
                    _currentTarget = IOCC.Get<UnitBase>(_currentMission.StoreID) as Storage;
                    m_state = PorterState.Store;
                }
            }
        }

        protected virtual void GoStore()
        {
            MoveToTarget(_currentTarget.AccessPos, _currentTarget.AccessRot);
            var distance = Vector3.Distance(_currentTarget.AccessPos, transform.position);
            if (distance < 0.5f)
            {
                _timer += Time.deltaTime;
            }

            if (_timer >= 0.5f)
            {
                if (_currentTarget.StoreItem(_currentMission.ItemID, _currentMission.Number))
                {
                    _currentTarget = null;
                    StartNewMission();
                }
            }
        }

        protected virtual void Backing()
        {
            MoveToTarget(_currentTarget.AccessPos, _currentTarget.AccessRot);

            if (transform.position == _startPosition && transform.rotation == _startRotation)
            {
                m_state = PorterState.Idle;
            }
        }

        public abstract void MoveToTarget(Vector3 target, Quaternion rotation);
    }
}
