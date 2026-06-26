using UnityEngine;

namespace NonsensicalKit.Simulation.InteractQueueSystem
{
    /// <summary>
    /// 可以执行碰撞交互的对象
    /// </summary>
    public class TriggerInteractableObject : InteractableObject
    {
        [SerializeField] private LayerMask m_layers;
        [SerializeField] private bool m_oneShot;

        private void OnTriggerEnter(Collider other)
        {
            if (0 != (m_layers.value & 1 << other.gameObject.layer))
            {
                if (other.GetComponent<ColliderInteractExecutor>() != null)
                {
                    StartWaiting();
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (0 != (m_layers.value & 1 << other.gameObject.layer))
            {
                if (other.GetComponent<ColliderInteractExecutor>() != null)
                {
                    StopWaiting();
                }
            }
        }

        private void Reset()
        {
            m_layers.value = int.MaxValue;
        }

        public override void Interact()
        {
            base.Interact();
            if (m_oneShot)
            {
                CanInteract = false;
                ForceRefresh();
            }
        }
    }
}
