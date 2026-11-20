using System;
using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    /// <summary>
    /// 单个温度传感器的数据。
    /// </summary>
    [Serializable]
    public class TemperatureSensor
    {
        [Label("传感器 ID")]
        [AllowNesting]
        [SerializeField] private string m_Id;

        [Label("体积本地坐标")]
        [AllowNesting]
        [SerializeField] private Vector3 m_Position;

        [Label("温度 (℃)")]
        [AllowNesting]
        [SerializeField] private float m_Temperature;

        /// <summary>唯一标识，用于外部数据注入。</summary>
        public string Id
        {
            get => m_Id;
            set => m_Id = value;
        }

        /// <summary>相对 <see cref="TemperatureVolumeBounds"/> Transform 的本地坐标。</summary>
        public Vector3 Position
        {
            get => m_Position;
            set => m_Position = value;
        }

        /// <summary>当前温度（℃）。</summary>
        public float Temperature
        {
            get => m_Temperature;
            set => m_Temperature = value;
        }

        public TemperatureSensor()
        {
            m_Id = Guid.NewGuid().ToString("N");
            m_Position = Vector3.zero;
            m_Temperature = 20f;
        }

        public TemperatureSensor(string id, Vector3 position, float temperature)
        {
            m_Id = id;
            m_Position = position;
            m_Temperature = temperature;
        }
    }
}
