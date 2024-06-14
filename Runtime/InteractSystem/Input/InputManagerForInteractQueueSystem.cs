using NonsensicalKit.Core.Service;
using UnityEngine;

namespace NonsensicalKit.Temp.InteractQueueSystem
{
    public class InputManagerForInteractQueueSystem : MonoBehaviour
    {
        [SerializeField] private KeyCode m_interactKey = KeyCode.F;
        [SerializeField] private KeyCode m_switchKey = KeyCode.Tab;

        private InteractQueueSystem _system;

        private void Awake()
        {
            _system = ServiceCore.Get<InteractQueueSystem>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(m_interactKey))
            {
                _system.Interact();
            }

            if (Input.GetKeyDown(m_switchKey))
            {
                _system.Switch();
            }
        }
    }
}
