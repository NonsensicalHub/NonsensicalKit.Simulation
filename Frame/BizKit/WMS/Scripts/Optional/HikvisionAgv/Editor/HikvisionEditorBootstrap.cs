using HikvisionAgv;
using NonsensicalKit.ScriptAnimation;
using UnityEditor;

namespace HikvisionAgv.Editor
{
    [InitializeOnLoad]
    static class HikvisionEditorBootstrap
    {
        static HikvisionEditorBootstrap()
        {
            PathFeatureIndexRegistry.Register<HikvisionPointIndex>();
        }
    }
}
