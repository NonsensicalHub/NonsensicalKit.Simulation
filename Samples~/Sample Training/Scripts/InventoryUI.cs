using NonsensicalKit.Core.Service;
using NonsensicalKit.Temp.InventorySystem;
using NonsensicalKit.UGUI.Table;
using UnityEngine;

public class InventoryUI : ListTableManager<ItemUIElement,ItemEntity>
{
    [SerializeField] private string m_inventoryID="Default Inventory";

    protected override void Awake()
    {
        base.Awake();
        ServiceCore.SafeGet<InventorySystem>(OnGetSystem);
    }

    private void OnGetSystem(InventorySystem system)
    {
        UpdateUI(system.GetItemEntity(m_inventoryID));
        system.AddListener(OnUpdateInventory, m_inventoryID);
    }

    private void OnUpdateInventory(ItemEntity[] items)
    {
        UpdateUI(items) ;
    }

    protected override void UpdateUI()
    {
        base.UpdateUI();

        for (int i = 0; i < _elements.Count; i++)
        {
            _elements[i]._inventoryID = m_inventoryID;
            _elements[i]._inventoryIndex = i;
        }
    }
}
