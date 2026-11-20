using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    /// <summary>
    /// 温度传感器数据管理：增删、模拟波动、外部注入、手动覆盖。
    /// </summary>
    public class TemperatureSensorManager : MonoBehaviour
    {
        [Label("体积边界")]
        [SerializeField] private TemperatureVolumeBounds m_VolumeBounds;

        [Label("传感器列表")]
        [SerializeField] private List<TemperatureSensor> m_Sensors = new List<TemperatureSensor>();

        [Label("默认生成数量")]
        [SerializeField] private int m_DefaultSensorCount = 8;

        [Label("启动时自动生成")]
        [SerializeField] private bool m_AutoGenerateOnStart = true;

        [Label("启用温度模拟")]
        [SerializeField] private bool m_EnableSimulation;

        [Label("模拟速度")]
        [SerializeField] private float m_SimulationSpeed = 0.55f;

        [Label("模拟温度振幅 (℃)")]
        [SerializeField] private float m_SimulationAmplitude = 14f;

        [Label("基准温度 (℃)")]
        [SerializeField] private float m_BaseTemperature = 22f;

        [Label("显示传感器标记球")]
        [SerializeField] private bool m_ShowSensorMarkers = true;

        [Label("标记球缩放")]
        [SerializeField] private float m_MarkerScale = 0.25f;

        [Label("标记球材质")]
        [SerializeField] private Material m_MarkerMaterial;

        private readonly Dictionary<string, Transform> m_MarkerById = new Dictionary<string, Transform>();
        private readonly HashSet<string> m_ManualOverrides = new HashSet<string>();
        private float m_SimulationTime;
        private bool m_SimulationDirty;

        public IReadOnlyList<TemperatureSensor> Sensors => m_Sensors;
        public event Action SensorsChanged;

        public bool EnableSimulation
        {
            get => m_EnableSimulation;
            set
            {
                if (m_EnableSimulation == value) return;
                m_EnableSimulation = value;
                if (!value) return;

                m_SimulationDirty = false;
                SimulateTemperatures(0f);
                if (m_SimulationDirty)
                    NotifyChanged();
            }
        }

        public TemperatureVolumeBounds VolumeBounds
        {
            get => m_VolumeBounds;
            set => m_VolumeBounds = value;
        }

        /// <summary>初始化并确保传感器与标记已创建（建议在场景启动时调用）。</summary>
        public void Initialize(TemperatureVolumeBounds volumeBounds, bool generateIfEmpty = true)
        {
            m_VolumeBounds = volumeBounds;

            if (generateIfEmpty && m_Sensors.Count < 4)
            {
                GenerateDefaultSensors(m_DefaultSensorCount);
            }
            else
            {
                RebuildMarkers();
            }
        }

        public TemperatureSensor FindSensor(string id)
        {
            for (int i = 0; i < m_Sensors.Count; i++)
            {
                if (m_Sensors[i].Id == id) return m_Sensors[i];
            }

            return null;
        }

        /// <summary>将传感器本地坐标转为世界坐标。</summary>
        public Vector3 GetSensorWorldPosition(TemperatureSensor sensor)
        {
            if (sensor == null) return Vector3.zero;
            if (m_VolumeBounds == null) return sensor.Position;
            return m_VolumeBounds.transform.TransformPoint(sensor.Position);
        }

        /// <summary>将世界坐标转为体积本地坐标并写入传感器。</summary>
        public void SetSensorWorldPosition(TemperatureSensor sensor, Vector3 worldPosition)
        {
            if (sensor == null) return;
            sensor.Position = m_VolumeBounds != null
                ? m_VolumeBounds.transform.InverseTransformPoint(worldPosition)
                : worldPosition;
        }

        private void Awake()
        {
            if (m_VolumeBounds != null && m_AutoGenerateOnStart && m_Sensors.Count < 4)
            {
                GenerateDefaultSensors(m_DefaultSensorCount);
            }
        }

        private void Start()
        {
            if (m_Sensors.Count > 0 && m_MarkerById.Count == 0)
            {
                RebuildMarkers();
            }
        }

        private void Update()
        {
            if (!m_EnableSimulation) return;

            SimulateTemperatures(Time.deltaTime);
            if (!m_SimulationDirty) return;

            m_SimulationDirty = false;
            NotifyChanged();
        }

        [Button("生成传感器")]
        private void GenerateSensors()
        {
            GenerateDefaultSensors(m_DefaultSensorCount);
        }

        public void GenerateDefaultSensors(int count)
        {
            m_Sensors.Clear();
            m_ManualOverrides.Clear();
            count = Mathf.Max(count, 4);

            if (m_VolumeBounds == null)
            {
                Debug.LogWarning("[TemperatureSensorManager] 未设置 VolumeBounds，无法生成默认传感器。");
                return;
            }

            // 在体积本地空间采样，与插值网格 / 等温面使用同一坐标系
            Vector3 localCenter = m_VolumeBounds.Center;
            Vector3 localSize = m_VolumeBounds.Size;
            var random = new System.Random(42);
            for (int i = 0; i < count; i++)
            {
                float x = (float)random.NextDouble();
                float y = (float)random.NextDouble();
                float z = (float)random.NextDouble();
                Vector3 localPos = localCenter + new Vector3(
                    (x - 0.5f) * localSize.x,
                    (y - 0.5f) * localSize.y,
                    (z - 0.5f) * localSize.z);
                float temp = m_BaseTemperature + (float)(random.NextDouble() * 10f - 5f);
                m_Sensors.Add(new TemperatureSensor($"sensor_{i}", localPos, temp));
            }

            NotifyChanged();
            RebuildMarkers();
        }

        public TemperatureSensor AddSensor(Vector3 worldPosition, float temperature)
        {
            Vector3 localPosition = m_VolumeBounds != null
                ? m_VolumeBounds.transform.InverseTransformPoint(worldPosition)
                : worldPosition;
            var sensor = new TemperatureSensor(Guid.NewGuid().ToString("N"), localPosition, temperature);
            m_Sensors.Add(sensor);
            NotifyChanged();
            CreateMarker(sensor);
            return sensor;
        }

        public bool RemoveSensor(string id)
        {
            int index = m_Sensors.FindIndex(s => s.Id == id);
            if (index < 0) return false;

            m_Sensors.RemoveAt(index);
            m_ManualOverrides.Remove(id);
            DestroyMarker(id);
            NotifyChanged();
            return true;
        }

        public void ClearSensors()
        {
            m_Sensors.Clear();
            m_ManualOverrides.Clear();
            ClearMarkers();
            NotifyChanged();
        }

        public bool SetTemperature(string id, float temperature)
        {
            return SetTemperatureInternal(id, temperature, manualOverride: false);
        }

        /// <summary>手动设置传感器温度，并锁定该传感器不受模拟覆盖。</summary>
        public bool SetTemperatureManual(string id, float temperature)
        {
            return SetTemperatureInternal(id, temperature, manualOverride: true);
        }

        public void ClearManualOverride(string id)
        {
            m_ManualOverrides.Remove(id);
        }

        public bool IsManualOverride(string id) => m_ManualOverrides.Contains(id);

        public void InjectTemperatures(IReadOnlyList<(string id, float temperature)> data)
        {
            if (data == null || data.Count == 0) return;

            bool changed = false;
            for (int i = 0; i < data.Count; i++)
            {
                changed |= SetTemperatureInternal(data[i].id, data[i].temperature, manualOverride: false);
            }

            if (changed)
            {
                NotifyChanged();
            }
        }

        public void SimulateTemperatures(float deltaTime)
        {
            if (m_Sensors.Count == 0) return;

            m_SimulationTime += deltaTime * m_SimulationSpeed;

            for (int i = 0; i < m_Sensors.Count; i++)
            {
                TemperatureSensor sensor = m_Sensors[i];
                if (m_ManualOverrides.Contains(sensor.Id)) continue;

                float phase = sensor.Id.GetHashCode() * 0.001f;
                float noise = Mathf.PerlinNoise(sensor.Position.x * 0.1f + m_SimulationTime, sensor.Position.z * 0.1f + phase);
                float wave = Mathf.Sin(m_SimulationTime + phase * 10f);
                float newTemp = m_BaseTemperature + wave * m_SimulationAmplitude * 0.5f + (noise - 0.5f) * m_SimulationAmplitude;
                if (!Mathf.Approximately(sensor.Temperature, newTemp))
                {
                    sensor.Temperature = newTemp;
                    m_SimulationDirty = true;
                }
            }
        }

        private bool SetTemperatureInternal(string id, float temperature, bool manualOverride)
        {
            for (int i = 0; i < m_Sensors.Count; i++)
            {
                if (m_Sensors[i].Id != id) continue;
                if (Mathf.Approximately(m_Sensors[i].Temperature, temperature) && (!manualOverride || m_ManualOverrides.Contains(id)))
                {
                    return false;
                }

                m_Sensors[i].Temperature = temperature;
                if (manualOverride)
                {
                    m_ManualOverrides.Add(id);
                }

                NotifyChanged();
                return true;
            }

            return false;
        }

        private void NotifyChanged()
        {
            UpdateMarkerColors();
            SensorsChanged?.Invoke();
        }

        private void RebuildMarkers()
        {
            ClearMarkers();
            if (!m_ShowSensorMarkers) return;

            for (int i = 0; i < m_Sensors.Count; i++)
            {
                CreateMarker(m_Sensors[i]);
            }
        }

        private void CreateMarker(TemperatureSensor sensor)
        {
            if (!m_ShowSensorMarkers) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = GetSensorWorldPosition(sensor);
            go.transform.SetParent(transform, true);
            ApplyMarkerLocalScale(go.transform);
            Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<Renderer>();
            if (m_MarkerMaterial != null)
            {
                renderer.sharedMaterial = m_MarkerMaterial;
            }
            else
            {
                renderer.material = CreateMarkerMaterial();
            }

            var marker = go.AddComponent<TemperatureSensorMarker>();
            marker.Bind(this, sensor.Id);

            m_MarkerById[sensor.Id] = go.transform;
            ApplyMarkerColor(renderer, sensor.Temperature);
        }

        private void ApplyMarkerLocalScale(Transform markerTransform)
        {
            Vector3 parentScale = transform.lossyScale;
            markerTransform.localScale = new Vector3(
                m_MarkerScale / Mathf.Max(Mathf.Abs(parentScale.x), 1e-6f),
                m_MarkerScale / Mathf.Max(Mathf.Abs(parentScale.y), 1e-6f),
                m_MarkerScale / Mathf.Max(Mathf.Abs(parentScale.z), 1e-6f));
        }

        private static Material CreateMarkerMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            return new Material(shader);
        }

        private void DestroyMarker(string id)
        {
            if (!m_MarkerById.TryGetValue(id, out Transform marker)) return;
            if (marker != null)
            {
                if (Application.isPlaying) Destroy(marker.gameObject);
                else DestroyImmediate(marker.gameObject);
            }

            m_MarkerById.Remove(id);
        }

        private void ClearMarkers()
        {
            foreach (var pair in m_MarkerById)
            {
                if (pair.Value != null)
                {
                    if (Application.isPlaying) Destroy(pair.Value.gameObject);
                    else DestroyImmediate(pair.Value.gameObject);
                }
            }

            m_MarkerById.Clear();
        }

        private void UpdateMarkerColors()
        {
            for (int i = 0; i < m_Sensors.Count; i++)
            {
                TemperatureSensor sensor = m_Sensors[i];
                if (!m_MarkerById.TryGetValue(sensor.Id, out Transform marker) || marker == null) continue;

                var markerComponent = marker.GetComponent<TemperatureSensorMarker>();
                if (markerComponent != null)
                {
                    markerComponent.SyncFromData();
                    continue;
                }

                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    ApplyMarkerColor(renderer, sensor.Temperature);
                }
            }
        }

        public static void ApplyMarkerColor(Renderer renderer, float temperature)
        {
            float t = Mathf.InverseLerp(10f, 40f, temperature);
            Color color = Color.Lerp(Color.cyan, Color.red, t);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                renderer.sharedMaterial.color = color;
            }
            else
            {
                renderer.material.color = color;
            }
        }
    }
}
