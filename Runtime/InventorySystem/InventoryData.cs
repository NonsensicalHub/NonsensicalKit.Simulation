using UnityEngine;

namespace NonsensicalKit.Simulation.Inventory
{
    [CreateAssetMenu(fileName = "InventoryData", menuName = "ScriptableObjects/InventoryData")]
    public class InventoryData : ScriptableObject
    {
        /// <summary>
        /// id
        /// </summary>
        public string ID;
        /// <summary>
        /// 库存名称
        /// </summary>
        public string Name;
        /// <summary>
        /// 初始尺寸
        /// </summary>
        public int InitialSize = 50;
    }
}
