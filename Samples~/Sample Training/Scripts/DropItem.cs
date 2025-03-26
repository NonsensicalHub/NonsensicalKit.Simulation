using NonsensicalKit.Core.Service;
using NonsensicalKit.Simulation.InteractQueueSystem;
using NonsensicalKit.Simulation.Inventory;
using UnityEngine;

public class DropItem : MonoBehaviour
{
    [SerializeField] private TriggerInteractableObject m_object;
    [SerializeField] private string m_itemID;
    [SerializeField] private int m_itemCount=1;

    private bool _itemExist;

    private void Awake()
    {
        _itemExist = true;
        m_object.ValidateFunc = Validate;
        m_object.OnInteract.AddListener(OnInteract);
    }

    private void OnInteract()
    {
        if (_itemExist)
        {
            _itemExist = false;
            m_object.gameObject.SetActive(false);
            ServiceCore.Get<InventorySystem>().AddItem(m_itemID, m_itemCount);
        }
    }

    private bool Validate()
    {
        return _itemExist;
    }
}
