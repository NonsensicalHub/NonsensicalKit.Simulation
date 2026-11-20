using UnityEngine;

namespace Frame.Core
{
public class SerializeFieldStringAttribute : PropertyAttribute
{
    public string Describe;
    public string ConfigPath;
    
    public SerializeFieldStringAttribute(string describe, string configPath)
    {
        this.Describe = describe;
        this.ConfigPath = configPath;
    }
}}
