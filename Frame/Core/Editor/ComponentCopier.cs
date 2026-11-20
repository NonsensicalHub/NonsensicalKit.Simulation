using UnityEngine;
using UnityEditor;

namespace Frame.Core.Editor
{
public class ComponentCopier : EditorWindow
{
    private GameObject sourceObject;
    private GameObject targetObject;

    [MenuItem("Tools/Frame/Component Copier")]
    public static void ShowWindow()
    {
        GetWindow<ComponentCopier>("Component Copier");
    }

    void OnGUI()
    {
        GUILayout.Label("Source Object", EditorStyles.boldLabel);
        sourceObject = (GameObject)EditorGUILayout.ObjectField("Select Source Object", sourceObject, typeof(GameObject), true);

        GUILayout.Label("Target Object", EditorStyles.boldLabel);
        targetObject = (GameObject)EditorGUILayout.ObjectField("Select Target Object", targetObject, typeof(GameObject), true);

        if (GUILayout.Button("Copy Components"))
        {
            CopyComponents();
        }
    }
    
    void CopyComponents()
    {
        if (sourceObject == null || targetObject == null)
        {
            Debug.LogError("Please select both a source and target object.");
            return;
        }

        foreach (UnityEngine.Component component in sourceObject.GetComponents<UnityEngine.Component>())
        {
            if (component is Transform )
            {
                targetObject.transform.position = sourceObject.transform.position;
                targetObject.transform.rotation = sourceObject.transform.rotation;
                targetObject.transform.localScale = sourceObject.transform.localScale;
            }


            System.Type componentType = component.GetType();
            UnityEngine.Component existingComponent = targetObject.GetComponent(componentType);

            if (existingComponent != null)
            {
                // If the component already exists on the target, copy properties
                UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(existingComponent);
                UnityEditor.SerializedObject sourceSo = new UnityEditor.SerializedObject(component);

                UnityEditor.SerializedProperty sp = sourceSo.GetIterator();
                if (sp.NextVisible(true))
                {
                    do
                    {
                        string propertyPath = sp.propertyPath;
                        UnityEditor.SerializedProperty tp = so.FindProperty(propertyPath);
                        if (tp != null)
                        {
                            tp.serializedObject.Update();
                            tp.serializedObject.CopyFromSerializedProperty(sp);
                            tp.serializedObject.ApplyModifiedProperties();
                        }
                    }
                    while (sp.NextVisible(false));
                }
            }
            else
            {
                // If the component does not exist, add it and then copy properties
                UnityEngine.Component newComponent = targetObject.AddComponent(componentType);
                UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(newComponent);
                UnityEditor.SerializedObject sourceSo = new UnityEditor.SerializedObject(component);

                UnityEditor.SerializedProperty sp = sourceSo.GetIterator();
                if (sp.NextVisible(true))
                {
                    do
                    {
                        string propertyPath = sp.propertyPath;
                        UnityEditor.SerializedProperty tp = so.FindProperty(propertyPath);
                        if (tp != null)
                        {
                            tp.serializedObject.Update();
                            tp.serializedObject.CopyFromSerializedProperty(sp);
                            tp.serializedObject.ApplyModifiedProperties();
                        }
                    }
                    while (sp.NextVisible(false));
                }
            }
        }

        Debug.Log("Components copied from " + sourceObject.name + " to " + targetObject.name);
    }
}
}
