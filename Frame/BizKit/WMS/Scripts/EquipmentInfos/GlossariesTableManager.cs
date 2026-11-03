using System.Collections.Generic;
using UnityEngine;
namespace Frame.BizKit.WMS.Equipment
{
    public class GlossariesTableManager : MonoBehaviour
    {
        [SerializeField] GlossariesTable manager;
        
        public void UpDateInfo( Dictionary<string, string> info)
        {
            manager.SetElement(info);
        }
    }
}
