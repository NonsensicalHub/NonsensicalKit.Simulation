using System;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

namespace NonsensicalKit.Simulation
{
    [CreateAssetMenu(fileName = "UnitData", menuName = "NonsensicalKit/ScriptableObject/UnitData", order = -100)]
    public class UnitConfigData : ConfigObject
    {
       public UnitData m_UnitData; 
        public override ConfigData GetData()
        {
            return m_UnitData;
        }

        public override void SetData(ConfigData cd)
        {
            if (CheckType<UnitData>(cd))
            {
                m_UnitData = cd as UnitData;
            }
        }
    }
    [Serializable]
    public class UnitData : ConfigData
    {
        
        public string TypeID;
        public string TypeName;
        public string Sprite;
        [TextArea(3,100)]
        public string Description;
    }
}
