using NonsensicalKit.Simulation.InteractQueueSystem;
using UnityEngine;

namespace NonsensicalKit.Temp.InteractQueueSystem.Sample
{
    public enum ItemInteractType
    {
        [InspectorName("仅交互")]
        JustInteract,
        [InspectorName("可拾取")]
        Pickalbe,
    }

    public class InteractableItem : MonoBehaviour
    {
        [SerializeField] private ItemInteractType m_type;
        [SerializeField] private TriggerInteractableObject m_object;

        private bool _itemExist;

        private void Awake()
        {
            _itemExist = true;
            m_object.ValidateFunc = Validate;

            m_object.OnInteract.AddListener(OnInteract);
        }

        private void OnInteract()
        {
            switch (m_type)
            {
                case ItemInteractType.JustInteract:
                    Debug.Log($"与{m_object.InteractName}进行交互");
                    break;
                case ItemInteractType.Pickalbe:
                    Debug.Log($"拾取{m_object.InteractName}");
                    _itemExist = false;
                    m_object.gameObject.SetActive(false);
                    break;
                default:
                    break;
            }
        }

        private bool Validate()
        {
            switch (m_type)
            {
                case ItemInteractType.Pickalbe:
                    return _itemExist;
                default:
                    return true;
            }
        }
    }
}
