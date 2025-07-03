using System;
using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.Simulation
{
    public class UnitUI : NonsensicalMono
    {
        [SerializeField] private UnitUIPage m_defaultPage;
        [SerializeField] private UnitUISetting[] m_setting;

        private void Awake()
        {
            Subscribe<UnitBase>("ShowUnitUI",ShowUnit);
        }

        private void ShowUnit(UnitBase unit)
        {
            Publish("HideUnitUI");
            UnitUIPage page = m_defaultPage;

            var type=unit.GetType().ToString();
            foreach (var setting in m_setting)
            {
                if (type==setting.TypeName)
                {
                    page=setting.Page;
                    break;
                }
            }
            
            page.ShowUnitUI(unit);
        }
    }
    
    [Serializable]
    public class UnitUISetting
    {
        public string TypeName;
        public UnitUIPage Page;
    }
}