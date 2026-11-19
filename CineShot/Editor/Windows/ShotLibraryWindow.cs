using System.Collections.Generic;
using CineShot.Core;
using UnityEditor;
using UnityEngine;

namespace CineShot.Editor
{
    public sealed class ShotLibraryWindow : EditorWindow
    {
        const string k_Folder = ShotLibrary.UserShotFolder;

        ShotQuery m_Query;
        List<ShotAsset> m_Hits = new List<ShotAsset>();
        Vector2 m_Scroll;
        string m_SelectedId;
        double m_LastQueryTime;

        [MenuItem("Tools/Cine/镜头库 %#&l")]
        public static void Open()
        {
            GetWindow<ShotLibraryWindow>("镜头库");
        }

        void OnEnable()
        {
            Refresh();
        }

        void OnGUI()
        {
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawInspector();
            }
            DrawStatus();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                m_Query.text = GUILayout.TextField(m_Query.text ?? string.Empty, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
                if (EditorGUI.EndChangeCheck())
                    m_LastQueryTime = EditorApplication.timeSinceStartup;

                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    ShotLibrary.Invalidate();
                    Refresh();
                }

                if (GUILayout.Button("创建 ▾", EditorStyles.toolbarDropDown, GUILayout.Width(64)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("从 Scene 视角"), false, CreateFromSceneView);
                    menu.AddItem(new GUIContent("从选中相机"), false, CreateFromSelectedCamera);
                    menu.AddSeparator(string.Empty);
                    foreach (RigKind kind in System.Enum.GetValues(typeof(RigKind)))
                    {
                        var k = kind;
                        menu.AddItem(new GUIContent("按机位/" + CineShotSceneEditor.RigLabel(k)), false, () => CreateDefault(k));
                    }
                    menu.ShowAsContext();
                }
            }

            if (EditorApplication.timeSinceStartup - m_LastQueryTime > 0.2 && m_LastQueryTime > 0)
            {
                Refresh();
                m_LastQueryTime = 0;
            }
        }

        void DrawList()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll, GUILayout.ExpandWidth(true));
            for (int i = 0; i < m_Hits.Count; i++)
            {
                var s = m_Hits[i];
                string name = string.IsNullOrEmpty(s.displayName) ? s.name : s.displayName;
                string tags = ShotLibrary.FormatTags(s.tags);
                string label = name + "  [" + CineShotSceneEditor.RigLabel(s.rigKind) + "]  " + s.defaultDuration.ToString("0.0") + "s";
                if (!string.IsNullOrEmpty(tags))
                    label += "  #" + tags.Replace(" · ", " #");
                bool selected = s.Id == m_SelectedId;
                if (GUILayout.Toggle(selected, label, "Button"))
                    m_SelectedId = s.Id;
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
            {
                var shot = FindSelected();
                if (shot == null)
                {
                    EditorGUILayout.LabelField("选中镜头可复制变体或送入分镜表。", EditorStyles.miniLabel);
                    return;
                }
                EditorGUILayout.LabelField(shot.displayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("编号", shot.Id);
                EditorGUILayout.LabelField("机位类型", CineShotSceneEditor.RigLabel(shot.rigKind));
                EditorGUILayout.LabelField("默认时长", shot.defaultDuration.ToString("0.00") + " 秒");
                EditorGUILayout.LabelField("视场角 FOV", shot.fov.ToString("0.0"));
                EditorGUILayout.LabelField("优先级", shot.priority.ToString());
                string tags = ShotLibrary.FormatTags(shot.tags);
                EditorGUILayout.LabelField("标签", string.IsNullOrEmpty(tags) ? "（无）" : tags);
                EditorGUILayout.LabelField("引用次数", ShotLibrary.CountReferences(shot).ToString());
                EditorGUILayout.LabelField("搜索：名称 / Id / 标签；#近景 只筛标签", EditorStyles.miniLabel);
                if (GUILayout.Button("在工程中定位"))
                    EditorGUIUtility.PingObject(shot);
                if (GUILayout.Button("复制为变体"))
                {
                    var v = ShotLibrary.DuplicateAsVariant(shot);
                    if (v != null)
                    {
                        m_SelectedId = v.Id;
                        Refresh();
                    }
                }
                if (GUILayout.Button("发送到分镜表"))
                    StoryboardWindow.OpenWithShot(shot);
            }
        }

        void DrawStatus()
        {
            EditorGUILayout.LabelField("匹配结果：" + m_Hits.Count + " 条", EditorStyles.miniLabel);
        }

        void Refresh()
        {
            m_Hits = ShotLibrary.Query(m_Query);
            Repaint();
        }

        ShotAsset FindSelected()
        {
            for (int i = 0; i < m_Hits.Count; i++)
                if (m_Hits[i].Id == m_SelectedId)
                    return m_Hits[i];
            return null;
        }

        void CreateFromSceneView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
            {
                EditorUtility.DisplayDialog("CineShot", "没有活动的 Scene 视图相机。", "确定");
                return;
            }
            var cam = sv.camera;
            var shot = ShotLibrary.Create(k_Folder, "Shot_SceneCapture", RigKind.Static);
            shot.fov = cam.fieldOfView;
            shot.nearClip = cam.nearClipPlane;
            shot.farClip = cam.farClipPlane;
            shot.rigParams.A = cam.transform.position;
            shot.rigParams.P0 = cam.transform.eulerAngles.y;
            shot.rigParams.P1 = cam.transform.eulerAngles.x;
            EditorUtility.SetDirty(shot);
            AssetDatabase.SaveAssets();
            m_SelectedId = shot.Id;
            Refresh();
        }

        void CreateFromSelectedCamera()
        {
            var cam = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Camera>() : null;
            if (cam == null)
            {
                EditorUtility.DisplayDialog("CineShot", "请先选中带 Camera 的对象。", "确定");
                return;
            }
            var shot = ShotLibrary.Create(k_Folder, "Shot_FromCamera", RigKind.Static);
            shot.fov = cam.fieldOfView;
            shot.nearClip = cam.nearClipPlane;
            shot.farClip = cam.farClipPlane;
            EditorUtility.SetDirty(shot);
            AssetDatabase.SaveAssets();
            m_SelectedId = shot.Id;
            Refresh();
        }

        void CreateDefault(RigKind kind)
        {
            var shot = ShotLibrary.Create(k_Folder, "Shot_" + kind, kind);
            m_SelectedId = shot.Id;
            Refresh();
        }
    }
}
