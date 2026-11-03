namespace Frame.Equipment
{
    public enum GlossariesInfoType
    {
        status,
        info,
        electricQuantity
    }

    public class GlossariesTableElementData
    {
        public GlossariesInfoType _type;
        public string title;
        public string description;
        public string deviceID;


        public GlossariesTableElementData(GlossariesInfoType _type, string title, string description, string id = "")
        {
            this._type = _type;
            this.title = title;
            this.description = description;
            this.deviceID = id;
        }
    }
}
