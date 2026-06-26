using System;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.Simulation
{
    public abstract class UnitBase : NonsensicalMono
    {
        [SerializeField] protected string m_typeID;
        [SerializeField] protected string m_unitID;


        public UnitData Data => _data;
        private string _id;
        private UnitData _data;
        private IUnitHighlight _highlight;

        protected virtual void Awake()
        {
            _id = string.IsNullOrEmpty(m_unitID) ? Guid.NewGuid().ToString() : m_unitID;
            IOCC.Set<UnitBase>(_id, this);

            ServiceCore.SafeGet<ConfigService>(OnGetService);
            _highlight = GetComponent<IUnitHighlight>();
        }

        private void OnGetService(ConfigService configService)
        {
            _data= configService.GetConfig<UnitData>(m_typeID);
            Debug.Log(JsonTool.SerializeObject(_data));
        }
    }

    public interface IUnitHighlight
    {
        void SetColor(Color color);
        void On();
        void Off();
    }
}
