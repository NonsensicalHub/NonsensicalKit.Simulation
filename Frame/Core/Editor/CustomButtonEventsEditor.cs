using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Frame.Core.UI;

namespace Frame.Core.Editor
{
[CustomEditor(typeof(CustomButtonEvents))]
public class CustomButtonEventsEditor : UnityEditor.Editor
{
    private SerializedProperty _items;
    private SerializedProperty _bindOnEnable;
    private bool[] _foldouts = System.Array.Empty<bool>();

    private void OnEnable()
    {
        _items = serializedObject.FindProperty("m_items");
        _bindOnEnable = serializedObject.FindProperty("m_bindOnEnable");
        SyncFoldouts();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SyncFoldouts();

        EditorGUILayout.PropertyField(_bindOnEnable, new GUIContent("启用时自动绑定"));
        EditorGUILayout.HelpBox(
            "编辑模式下点击「触发」会立刻执行该条 OnClick（含 Runtime Only 监听）。调用模式为 Off 的不会执行。",
            MessageType.Info);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"按钮列表（{_items.arraySize}）", EditorStyles.boldLabel);

        for (var i = 0; i < _items.arraySize; i++)
        {
            var element = _items.GetArrayElementAtIndex(i);
            var nameProp = element.FindPropertyRelative("Name");
            var buttonProp = element.FindPropertyRelative("Button");
            var onClickProp = element.FindPropertyRelative("OnClick");

            var title = string.IsNullOrEmpty(nameProp.stringValue)
                ? $"按钮 {i}"
                : $"按钮 {i} - {nameProp.stringValue}";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _foldouts[i] = EditorGUILayout.Foldout(_foldouts[i], title, true);

                    GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
                    if (GUILayout.Button("触发", GUILayout.Width(56), GUILayout.Height(20)))
                        InvokeItem(i);
                    GUI.backgroundColor = Color.white;

                    if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0)
                    {
                        _items.MoveArrayElement(i, i - 1);
                        SwapFoldout(i, i - 1);
                        break;
                    }

                    if (GUILayout.Button("↓", GUILayout.Width(24)) && i < _items.arraySize - 1)
                    {
                        _items.MoveArrayElement(i, i + 1);
                        SwapFoldout(i, i + 1);
                        break;
                    }

                    if (GUILayout.Button("×", GUILayout.Width(24)))
                    {
                        _items.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }

                if (!_foldouts[i])
                    continue;

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(nameProp, new GUIContent("备注"));
                EditorGUILayout.PropertyField(buttonProp, new GUIContent("Button"));
                EditorGUILayout.PropertyField(onClickProp, new GUIContent("点击事件"), true);

                GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
                if (GUILayout.Button("在 Inspector 中点击触发", GUILayout.Height(28)))
                    InvokeItem(i);
                GUI.backgroundColor = Color.white;

                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 添加按钮", GUILayout.Height(24)))
            {
                var index = _items.arraySize;
                _items.InsertArrayElementAtIndex(index);
                var added = _items.GetArrayElementAtIndex(index);
                added.FindPropertyRelative("Name").stringValue = $"按钮{index}";
                added.FindPropertyRelative("Button").objectReferenceValue = null;
                var calls = added.FindPropertyRelative("OnClick.m_PersistentCalls.m_Calls");
                if (calls != null)
                    calls.ClearArray();
            }

            if (GUILayout.Button("全部重新绑定", GUILayout.Height(24)))
            {
                serializedObject.ApplyModifiedProperties();
                foreach (var t in targets)
                {
                    if (t is CustomButtonEvents events)
                        events.BindAll();
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void InvokeItem(int index)
    {
        GUI.FocusControl(null);
        serializedObject.ApplyModifiedProperties();

        var events = (CustomButtonEvents)target;
        if (events == null || events.Items == null || index < 0 || index >= events.Items.Count)
            return;

        var item = events.Items[index];
        if (item?.OnClick == null)
            return;

        InvokeUnityEventForEditor(item.OnClick);
        EditorUtility.SetDirty(events);
        SceneView.RepaintAll();
        Repaint();
    }

    /// <summary>
    /// 编辑模式可触发：临时把 RuntimeOnly 提升为 EditorAndRuntime，再走标准 Invoke。
    /// </summary>
    private static void InvokeUnityEventForEditor(UnityEvent unityEvent)
    {
        var count = unityEvent.GetPersistentEventCount();
        if (count <= 0)
        {
            if (!Application.isPlaying)
                Debug.LogWarning("[CustomButtonEvents] 没有 Inspector 持久化事件可触发。请先在「点击事件」里绑定方法。");
            else
                unityEvent.Invoke();
            return;
        }

        if (Application.isPlaying)
        {
            unityEvent.Invoke();
            return;
        }

        var previousStates = new UnityEventCallState[count];
        for (var i = 0; i < count; i++)
        {
            previousStates[i] = unityEvent.GetPersistentListenerState(i);
            if (previousStates[i] == UnityEventCallState.RuntimeOnly)
                unityEvent.SetPersistentListenerState(i, UnityEventCallState.EditorAndRuntime);
        }

        try
        {
            unityEvent.Invoke();
        }
        finally
        {
            for (var i = 0; i < count; i++)
                unityEvent.SetPersistentListenerState(i, previousStates[i]);
        }
    }

    private void SyncFoldouts()
    {
        if (_foldouts.Length == _items.arraySize)
            return;

        var next = new bool[_items.arraySize];
        for (var i = 0; i < next.Length; i++)
            next[i] = i < _foldouts.Length && _foldouts[i];
        _foldouts = next;
    }

    private void SwapFoldout(int a, int b)
    {
        if (a < 0 || b < 0 || a >= _foldouts.Length || b >= _foldouts.Length)
            return;
        (_foldouts[a], _foldouts[b]) = (_foldouts[b], _foldouts[a]);
    }
}
}
