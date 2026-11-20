using Frame.Core.FocusCamera;

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NonsensicalKit.Core;
using UnityEditor;
using UnityEngine;

namespace Frame.Core.Editor
{
public class ThumbnailGenerator : EditorWindow
{
    // UI元素
    private Camera targetCamera;
    private int width = 200;
    private int height = 200;
    private string savePath = "Assets/Thumbnails";
    private string fileName = "thumbnail";
    private bool transparentBackground = true;
    private Color backgroundColor = Color.white;
    private Texture2D previewTexture;

    private bool batchCreateImages = false;
    private GameObject[] batchObjects = new GameObject[0];
    private string batchPrefix = "obj_";
    private Vector2 scrollPosition;

    ConfigurableCamera _camera;

    // 窗口菜单项
    [MenuItem("Tools/Frame/缩略图生成器")]
    public static void ShowWindow()
    {
        GetWindow<ThumbnailGenerator>("缩略图生成器");
    }

    private void OnGUI()
    {
        GUILayout.Label("摄像机缩略图生成工具", EditorStyles.boldLabel);

        // 摄像机选择
        targetCamera = (Camera)EditorGUILayout.ObjectField("目标摄像机", targetCamera, typeof(Camera), true);
        targetCamera ??= Camera.main;
        _camera = (ConfigurableCamera)EditorGUILayout.ObjectField("ConfigurableCamera", _camera, typeof(ConfigurableCamera), true);
        _camera = FindAnyObjectByType<ConfigurableCamera>();

        // 分辨率设置
        EditorGUILayout.BeginHorizontal();
        width = EditorGUILayout.IntField("宽度", width);
        height = EditorGUILayout.IntField("高度", height);
        EditorGUILayout.EndHorizontal();

        // 保存路径设置
        EditorGUILayout.BeginHorizontal();
        savePath = EditorGUILayout.TextField("保存路径", savePath);
        if (GUILayout.Button("浏览"))
        {
            string selectedPath = EditorUtility.SaveFolderPanel("选择保存路径", savePath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                savePath = selectedPath;
            }
        }

        EditorGUILayout.EndHorizontal();

        fileName = EditorGUILayout.TextField("文件名", fileName);

        // 背景设置
        transparentBackground = EditorGUILayout.Toggle("透明背景", transparentBackground);
        if (!transparentBackground)
        {
            backgroundColor = EditorGUILayout.ColorField("背景颜色", backgroundColor);
        }

        // 批量生成设置
        batchCreateImages = EditorGUILayout.Toggle("批量生成模式", batchCreateImages);

        if (batchCreateImages)
        {
            GUILayout.Label("批量生成模式下,相机保持固定,需手动调整物体角度");
            EditorGUILayout.LabelField("批量生成设置", EditorStyles.boldLabel);

            batchPrefix = EditorGUILayout.TextField("文件前缀", batchPrefix);

            if (GUILayout.Button("从选择的对象添加"))
            {
                AddSelectedObjects();
            }

            if (GUILayout.Button("清除所有对象"))
            {
                batchObjects = Array.Empty<GameObject>();
            }

            EditorGUILayout.LabelField($"已选择对象: {batchObjects.Length}个");

            // 显示已选择的对象列表
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(150));
            for (int i = 0; i < batchObjects.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                batchObjects[i] = (GameObject)EditorGUILayout.ObjectField(batchObjects[i], typeof(GameObject), true);

                if (GUILayout.Button("移除", GUILayout.Width(60)))
                {
                    RemoveObjectAt(i);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // 生成按钮
        if (GUILayout.Button(batchCreateImages ? "批量生成缩略图" : "生成缩略图"))
        {
            if (batchCreateImages)
            {
                NonsensicalInstance.Instance.StartCoroutine(BatchGenerateThumbnails());
            }
            else
            {
                GenerateThumbnail();
            }
        }

        if (GUILayout.Button("注视选中对象"))
        {
            if (Selection.activeGameObject != null)
            {
                _camera?.Focus(Selection.activeGameObject.transform, true, true);
            }
        }


        // 预览区域
        if (previewTexture != null)
        {
            GUILayout.Label("预览:");
            EditorGUILayout.LabelField("", GUILayout.Width(width), GUILayout.Height(height));
            Rect previewRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawPreviewTexture(previewRect, previewTexture);
        }
    }

    private void AddSelectedObjects()
    {
        List<GameObject> tempList = new List<GameObject>(batchObjects);

        foreach (GameObject obj in Selection.gameObjects)
        {
            if (!tempList.Contains(obj))
            {
                tempList.Add(obj);
            }
        }

        batchObjects = tempList.ToArray();
    }

    private void RemoveObjectAt(int index)
    {
        if (index >= 0 && index < batchObjects.Length)
        {
            List<GameObject> tempList = new List<GameObject>(batchObjects);
            tempList.RemoveAt(index);
            batchObjects = tempList.ToArray();
        }
    }

    private IEnumerator BatchGenerateThumbnails()
    {
        if (targetCamera == null)
        {
            EditorUtility.DisplayDialog("错误", "请选择目标摄像机", "确定");
            yield break;
        }

        if (batchObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("错误", "请添加要生成缩略图的对象", "确定");
            yield break;
        }

        int successCount = 0;
        int errorCount = 0;
        string errorMessages = "";

        // 创建保存目录(如果不存在)
        Directory.CreateDirectory(savePath);

        // 保存原始摄像机设置
        Transform originalCameraParent = targetCamera.transform.parent;
        Vector3 originalPosition = targetCamera.transform.position;
        Quaternion originalRotation = targetCamera.transform.rotation;
        bool originalOrthographic = targetCamera.orthographic;
        float originalSize = targetCamera.orthographicSize;
        float originalFOV = targetCamera.fieldOfView;

        try
        {
            foreach (GameObject obj1 in batchObjects)
            {
                obj1.SetActive(false);
            }

            foreach (var t in batchObjects)
            {
                if (t == null) continue;

                t.SetActive(true);
                _camera.Focus(t.transform, true, true);

                //使用cinemachine控制相机时，cinemachineBrain移动有延迟，要等两帧
                yield return null;
                yield return null;

                string objectName = t.name.Replace(" ", "_");
                string objectFileName = $"{batchPrefix}{objectName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                // 保存当前对象的缩略图
                string fullPath = Path.Combine(savePath, objectFileName + ".png");
                try
                {
                    GenerateSingleThumbnail(fullPath);
                    successCount++;
                }
                catch (Exception e)
                {
                    errorCount++;
                    errorMessages += $"{t.name}: {e.Message}\n";
                    t.SetActive(false);
                }

                t.SetActive(false);
            }

            string message = $"批量生成完成!\n成功: {successCount}\n失败: {errorCount}";
            if (errorCount > 0)
            {
                message += $"\n\n错误详情:\n{errorMessages}";
            }

            EditorUtility.DisplayDialog("批量生成结果", message, "确定");
        }
        finally
        {
            // 恢复摄像机原始设置
            targetCamera.transform.SetParent(originalCameraParent);
            targetCamera.transform.position = originalPosition;
            targetCamera.transform.rotation = originalRotation;
            targetCamera.orthographic = originalOrthographic;
            targetCamera.orthographicSize = originalSize;
            targetCamera.fieldOfView = originalFOV;
        }
    }

    private void GenerateSingleThumbnail(string fullPath)
    {
        // 创建临时RenderTexture
        RenderTexture tempRT = new RenderTexture(width, height, 24);

        // 保存原始设置
        RenderTexture originalRT = targetCamera.targetTexture;
        Color originalBackgroundColor = targetCamera.backgroundColor;
        bool originalClearFlags = targetCamera.clearFlags == CameraClearFlags.SolidColor;

        try
        {
            // 设置摄像机渲染目标
            targetCamera.targetTexture = tempRT;

            // 根据选项设置背景
            if (transparentBackground)
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0, 0, 0, 0);
            }
            else
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = backgroundColor;
            }

            // 渲染摄像机
            targetCamera.Render();

            // 读取像素
            RenderTexture.active = tempRT;
            Texture2D thumbnail = new Texture2D(width, height, TextureFormat.ARGB32, false);
            thumbnail.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            thumbnail.Apply();

            // 保存为PNG
            byte[] bytes = thumbnail.EncodeToPNG();
            File.WriteAllBytes(fullPath, bytes);

            // 更新预览为最后一个生成的缩略图
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }

            previewTexture = thumbnail;
        }
        finally
        {
            // 恢复原始设置
            targetCamera.targetTexture = originalRT;
            targetCamera.backgroundColor = originalBackgroundColor;
            targetCamera.clearFlags = originalClearFlags ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;

            // 清理临时对象
            RenderTexture.active = null;
            DestroyImmediate(tempRT);
        }
    }

    private void GenerateThumbnail()
    {
        GenerateSingleThumbnail(Path.Combine(savePath, fileName + ".png"));
        AssetDatabase.Refresh();
    }
}
}
#endif
