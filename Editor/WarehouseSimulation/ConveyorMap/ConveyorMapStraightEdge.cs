#if UNITY_EDITOR
using System.Reflection;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>GraphView 默认边为贝塞尔曲线；将控制点与端点重合后即为直线。</summary>
    public class ConveyorMapStraightEdge : Edge
    {
        private static readonly PropertyInfo ControlPointsProperty = typeof(EdgeControl).GetProperty(
            "controlPoints",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo ControlPointsField = typeof(EdgeControl).GetField(
            "m_ControlPoints",
            BindingFlags.Instance | BindingFlags.NonPublic);

        protected bool UpdateStraightEdgeControl()
        {
            if (!base.UpdateEdgeControl())
            {
                return false;
            }

            var from = edgeControl.from;
            var to = edgeControl.to;
            SetControlPoints(edgeControl, new[] { from, from, to, to });
            edgeControl.MarkDirtyRepaint();
            return true;
        }

        private static void SetControlPoints(EdgeControl control, Vector2[] points)
        {
            if (ControlPointsProperty != null)
            {
                var setter = ControlPointsProperty.GetSetMethod(nonPublic: true);
                if (setter != null)
                {
                    setter.Invoke(control, new object[] { points });
                    return;
                }
            }

            if (ControlPointsField != null)
            {
                ControlPointsField.SetValue(control, points);
            }
        }

        public override bool UpdateEdgeControl() => UpdateStraightEdgeControl();
    }
}
#endif
