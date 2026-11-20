using Cysharp.Threading.Tasks;
using Frame.Core.FocusCamera;
using Frame.Core.Tools;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 实现库存管理点货物,相机注视对应货物 
/// </summary>
public class WareHouseFocus : NonsensicalMono
{
    [SerializeField, Label("监听仓库")] private string m_signalID = "堆垛机库";
    [SerializeField] private string m_warehouseName;

    [SerializeField, Label("注视距离")] private float m_focusZoom = 0.12f;
    [SerializeField] private ConfigurableCamera m_focusCamera;
    [SerializeField] private WarehouseManager m_warehouseManager;
    [SerializeField] private WareHouseShelfTool m_wareHouseShelfTool;

    [ShowNonSerializedField] private bool _isFocusing = false;

    float _zoomTarget;

    private void Awake()
    {
        //退出注视
        //货架显示变化
        Subscribe<float>("SliderValueChanged", m_signalID, ExitFocus);
        //逻辑节点变化，退出注视
        Subscribe((int)DagLogicNodeEnum.NodeExit, "库存管理", OnNodeExit);
        
        Subscribe<Int4>("cameraFocusInt4",EnterFocus);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ExitFocus(0);
            OnNodeExit();
        }
    }

    private void ExitFocus(float _)
    {
        if (!_isFocusing) return;
        _isFocusing = false;
        SetFocusCameraTargetZoom(0.7f,ease:UniTaskInterpolator.Ease.InQuad);
        CancelCargoSelection();
    }

    public void EnterFocus(Int4 arg)
    {
        if (m_warehouseManager == null)
        {
            Debug.LogWarning($"[{nameof(WareHouseFocus)}] 未配置 WarehouseManager，忽略货位聚焦 {arg}。", this);
            return;
        }

        RuntimeBinData pos = m_warehouseManager.GetRuntimeBinData(arg);
        if (pos == null)
        {
            // 搜索结果的层/列/排/深与本地仓库地图不一致（或地图尚未加载完成）时查不到货位，
            // 此时不应切换货架/相机（更不能解引用 pos.Pos），详情标签已由搜索侧单独发布。
            Debug.LogWarning(
                $"[{nameof(WareHouseFocus)}] 无法解析货位 层={arg.X} 列={arg.Y} 排={arg.Z} 深={arg.W}（仓库={m_warehouseName}），跳过聚焦。",
                this);
            return;
        }

        m_wareHouseShelfTool?.ChangeShelfLayer(arg.Y + 1);
        SetFocusTarget(pos.Pos);
        Execute<Int4, bool>("ShowDetailLabelsOnGpuPicking", m_warehouseName, arg);
        _isFocusing = true;
    }

    private void OnNodeExit()
    {
        _isFocusing = false;
        m_wareHouseShelfTool?.ResetToDefault();
        if (m_focusCamera != null)
            m_focusCamera.ResetState();
        CancelCargoSelection();
        SetFocusCameraTargetZoom(0.7f, ease: UniTaskInterpolator.Ease.InQuad);
    }

    /// <summary>
    /// 取消当前货位选择：隐藏详情标签并关闭货位高亮。
    /// </summary>
    private void CancelCargoSelection()
    {
        Execute<bool>("HideDetailLabels", m_warehouseName);
        PublishWithID("HideHighlightBin", m_warehouseName);
    }

    private void SetFocusTarget(Vector3 pos)
    {
        if (m_focusCamera == null || m_warehouseManager == null)
            return;

        if (_isFocusing == false)
        {
            SetFocusCameraTargetZoom(m_focusZoom,ease:UniTaskInterpolator.Ease.InCubic);
        }

        m_focusCamera.Focus(m_warehouseManager.transform.TransformPoint(pos));
    }
    private void SetFocusCameraTargetZoom(float zoom,float duration=0.5f,UniTaskInterpolator.Ease ease=UniTaskInterpolator.Ease.InCubic)
    {
        if (m_focusCamera == null)
            return;

        UniTaskInterpolator.To(m_focusCamera.TargetZoom, zoom, 0.5f, (v) =>
        {
            // 插值期间相机可能已被销毁/切换场景，逐帧回调需再次判空。
            if (m_focusCamera != null)
                m_focusCamera.TargetZoom = v;
        }, ease: UniTaskInterpolator.Ease.InQuad);
    }
}
}
