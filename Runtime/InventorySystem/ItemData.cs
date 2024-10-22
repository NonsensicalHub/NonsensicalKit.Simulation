using UnityEngine;

namespace NonsensicalKit.Simulation.Inventory
{
    [CreateAssetMenu(fileName = "ItemData", menuName = "ScriptableObjects/ItemData")]
    public class ItemData : ScriptableObject
    {
        /// <summary>
        /// id
        /// </summary>
        public string ID;
        /// <summary>
        /// 优先级，决定排序后的先后顺序
        /// </summary>
        public int Priority=-1;
        /// <summary>
        /// 名称
        /// </summary>
        public string ItemName;
        /// <summary>
        /// 图标Addressable的路径
        /// </summary>
        public string Sprite;
        /// <summary>
        /// 最大堆叠个数
        /// </summary>
        public int MaxStackNumber = 1;
        /// <summary>
        /// 描述信息
        /// </summary>
        [TextArea(3, 100)]
        public string Description;
    }
}
