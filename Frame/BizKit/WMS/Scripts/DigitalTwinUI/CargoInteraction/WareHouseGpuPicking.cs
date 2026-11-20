using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Frame.BizKit.WMS
{
public class WareHouseGpuPicking : MonoBehaviour
{
    [SerializeField] private WarehouseManager m_warehouseManager;
    [SerializeField] private Camera m_camera;
    [SerializeField] private int m_mouseButton;
    [SerializeField] private bool m_locateHighlight = true;
    [SerializeField] private bool m_enableGpuPicking = true;
    [SerializeField] private bool m_debugLog = false;


    [SerializeField] private UnityEvent<Int4> m_onClick;

    private bool _picking, _unEnablePick;


    private void Reset()
    {
        m_warehouseManager = FindAnyObjectByType<WarehouseManager>();
        m_camera = Camera.main;
    }

    private void OnValidate()
    {
        if (m_warehouseManager != null)
        {
            m_warehouseManager.EnableGpuPicking = m_enableGpuPicking;
        }
    }

    private void Start()
    {
        if (m_warehouseManager == null) return;
        m_warehouseManager.EnableGpuPicking = m_enableGpuPicking;
        IOCC.AddListener<bool>("EnableGpuPicking", (v) => _unEnablePick = !v);
    }

    private void Update()
    {
        if (_picking || _unEnablePick ||
            m_warehouseManager == null ||
            !m_warehouseManager.Inited ||
            !Input.GetMouseButtonDown(m_mouseButton) ||
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        PickAsync(Input.mousePosition).Forget();
    }


    private async UniTaskVoid PickAsync(Vector2 screenPosition)
    {
        _picking = true;
        try
        {
            Camera pickCamera = m_camera != null ? m_camera : Camera.main;
            bool hit = await m_warehouseManager.TryPickCargoAsync(
                screenPosition,
                pickCamera,
                location =>
                {
                    m_onClick.Invoke(location);
                    if (m_debugLog) Debug.Log(Format(location));
                },
                m_locateHighlight);
        }
        finally
        {
            _picking = false;
        }
    }

    private static string Format(Int4 location)
    {
        return $"layer={location.X}, column={location.Y}, row={location.Z}, depth={location.W}";
    }
}
}
