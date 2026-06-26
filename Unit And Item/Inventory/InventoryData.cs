namespace NonsensicalKit.Simulation.Inventory
{
    public class InventoryData
    {
        public string ID;
        public int Size; //初始尺寸

        public InventoryData(string id)
        {
            ID = id;
            Size = 50;
        }

        public InventoryData(string id, int size)
        {
            ID = id;
            Size = size;
        }
    }
}
