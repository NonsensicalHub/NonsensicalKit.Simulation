using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>解析 Clip 上的 <see cref="ScriptAnimPoint"/> ExposedReference。</summary>
    public static class ScriptAnimPointUtility
    {
        public static ScriptAnimPoint Resolve(
            ExposedReference<ScriptAnimPoint> exposed,
            IExposedPropertyTable resolver)
        {
            if (resolver != null)
            {
                Object obj = resolver.GetReferenceValue(exposed.exposedName, out bool valid);
                ScriptAnimPoint fromTable = AsPoint(obj);
                if (valid && fromTable != null)
                    return fromTable;
            }

            return AsPoint(exposed.defaultValue);
        }

        /// <summary>将 Object 转为 ScriptAnimPoint（支持 Component / GameObject）。</summary>
        public static ScriptAnimPoint AsPoint(Object obj)
        {
            switch (obj)
            {
                case ScriptAnimPoint point:
                    return point;
                case Component component:
                    return component.GetComponent<ScriptAnimPoint>();
                case GameObject gameObject:
                    return gameObject.GetComponent<ScriptAnimPoint>();
                default:
                    return null;
            }
        }

        public static Transform AsTransform(ScriptAnimPoint point) =>
            point != null ? point.transform : null;
    }
}
