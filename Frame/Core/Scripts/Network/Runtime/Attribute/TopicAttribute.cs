using UnityEngine;
namespace Frame.Core.Attributes
{
    public class TopicAttribute : PropertyAttribute
    {
        public string Describe;
        public string ConfigPath;
        public string DeviceSNType;

        public TopicAttribute(string describe, string configPath, string deviceSnType = "")
        {
            this.Describe = describe;
            this.DeviceSNType = deviceSnType;
            this.ConfigPath = configPath;
        }
    }
}
