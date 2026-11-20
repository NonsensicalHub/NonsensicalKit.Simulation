#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TemperatureVisualization.Editor
{
    [CustomEditor(typeof(TemperatureVolumeBounds))]
    [CanEditMultipleObjects]
    public class TemperatureVolumeBoundsEditor : UnityEditor.Editor
    {
        private BoxBoundsHandle m_BoxHandle = new BoxBoundsHandle();

        private void OnEnable()
        {
            m_BoxHandle.axes = PrimitiveBoundsHandle.Axes.All;
        }

        private void OnSceneGUI()
        {
            var boundsComponent = (TemperatureVolumeBounds)target;
            if (!boundsComponent.EditBoundsInScene) return;

            Transform transform = boundsComponent.transform;
            Bounds worldBounds = boundsComponent.WorldBounds;

            m_BoxHandle.center = worldBounds.center;
            m_BoxHandle.size = worldBounds.size;

            EditorGUI.BeginChangeCheck();
            m_BoxHandle.DrawHandle();
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(boundsComponent, "调整温度场体积边界");
            Undo.RecordObject(transform, "调整温度场体积边界");

            boundsComponent.SetWorldBounds(new Bounds(m_BoxHandle.center, m_BoxHandle.size));
            EditorUtility.SetDirty(boundsComponent);
        }
    }
}
#endif
