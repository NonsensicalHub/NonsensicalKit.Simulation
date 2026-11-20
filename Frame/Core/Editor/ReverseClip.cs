// Editor脚本：反转AnimationClip关键帧
using UnityEditor;
using UnityEngine;

namespace Frame.Core.Editor
{
public class ReverseClip : EditorWindow
{
    [MenuItem("Tools/Frame/Reverse Selected Clip")]
    static void Reverse()
    {
        var clip = Selection.activeObject as AnimationClip;
        if (clip == null) return;

        float length = clip.length;
        var bindings = AnimationUtility.GetCurveBindings(clip);

        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var keys = curve.keys;
            var newKeys = new Keyframe[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                int j = keys.Length - 1 - i;
                newKeys[i] = new Keyframe(
                    length - keys[j].time,
                    keys[j].value,
                    -keys[j].outTangent,
                    -keys[j].inTangent
                );
            }

            curve.keys = newKeys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Clip reversed: " + clip.name);
    }
}
}
