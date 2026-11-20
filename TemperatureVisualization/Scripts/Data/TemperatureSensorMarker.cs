using System;
using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    /// <summary>
    /// 传感器场景标记，与 TemperatureSensorManager 中的数据绑定。
    /// </summary>
    public class TemperatureSensorMarker : MonoBehaviour
    {
        [Label("传感器 ID")]
        [SerializeField] private string m_SensorId;

        private TemperatureSensorManager m_Manager;

        public string SensorId => m_SensorId;

        public void Bind(TemperatureSensorManager manager, string sensorId)
        {
            m_Manager = manager;
            m_SensorId = sensorId;
            gameObject.name = $"Sensor_{sensorId}";
            SyncFromData();
        }

        public void SyncFromData()
        {
            if (m_Manager == null || string.IsNullOrEmpty(m_SensorId)) return;

            TemperatureSensor sensor = m_Manager.FindSensor(m_SensorId);
            if (sensor == null) return;

            transform.position = m_Manager.GetSensorWorldPosition(sensor);
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                TemperatureSensorManager.ApplyMarkerColor(renderer, sensor.Temperature);
            }
        }
    }
}
