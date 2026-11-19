using System.Collections.Generic;
using CineShot.Core;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Playables;

namespace CineShot.Editor
{
    public sealed class StoryboardWindow : EditorWindow
    {
        CineSequenceAsset m_Sequence;
        PlayableDirector m_Director;
        ReorderableList m_List;
        Vector2 m_Scroll;
        readonly List<ShotAsset> m_PendingShots = new List<ShotAsset>();

        [MenuItem("Tools/Cine/分镜表 %#&b")]
        public static void Open()
        {
            GetWindow<StoryboardWindow>("分镜表");
        }

        public static void OpenWithShot(ShotAsset shot)
        {
            var win = GetWindow<StoryboardWindow>("分镜表");
            win.m_PendingShots.Add(shot);
            win.EnsureSequence();
            win.FlushPending();
        }

        void OnEnable()
        {
            EnsureSequence();
            RebuildList();
        }

        void OnGUI()
        {
            EditorGUI.LabelField(CineHelp.Line(CineTips.Storyboard), "分镜表", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("导演", GUILayout.Width(28));
                m_Director = (PlayableDirector)EditorGUILayout.ObjectField(m_Director, typeof(PlayableDirector), true, GUILayout.Width(160));
                EditorGUILayout.LabelField("分镜", GUILayout.Width(28));
                m_Sequence = (CineSequenceAsset)EditorGUILayout.ObjectField(m_Sequence, typeof(CineSequenceAsset), false);
                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(44)))
                    CreateSequence();
                if (GUILayout.Button("铺轨", EditorStyles.toolbarButton, GUILayout.Width(44)))
                    LayOut();
            }

            FlushPending();
            if (m_Sequence == null)
            {
                EditorGUILayout.LabelField("请创建或指定分镜资产。", EditorStyles.miniLabel);
                return;
            }

            if (m_List == null)
                RebuildList();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            m_List.DoLayoutList();
            EditorGUILayout.EndScrollView();
        }

        void EnsureSequence()
        {
            if (m_Sequence != null)
                return;
            string[] guids = AssetDatabase.FindAssets("t:CineSequenceAsset");
            if (guids.Length > 0)
                m_Sequence = AssetDatabase.LoadAssetAtPath<CineSequenceAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        void CreateSequence()
        {
            const string folder = ShotLibrary.UserSequenceFolder;
            ShotLibrary.EnsureAssetFolder(folder);
            m_Sequence = ScriptableObject.CreateInstance<CineSequenceAsset>();
            AssetDatabase.CreateAsset(m_Sequence, AssetDatabase.GenerateUniqueAssetPath(folder + "/Seq_.asset"));
            AssetDatabase.SaveAssets();
            RebuildList();
        }

        void FlushPending()
        {
            if (m_Sequence == null || m_PendingShots.Count == 0)
                return;
            Undo.RecordObject(m_Sequence, "添加分镜行");
            for (int i = 0; i < m_PendingShots.Count; i++)
            {
                var s = m_PendingShots[i];
                if (s == null) continue;
                m_Sequence.rows.Add(new StoryboardRow
                {
                    shotGuid = s.Id,
                    rowIndex = m_Sequence.rows.Count,
                    duration = s.defaultDuration,
                    blendMode = ShotBlendMode.Blend,
                    blendDuration = 0.5f
                });
            }
            m_PendingShots.Clear();
            EditorUtility.SetDirty(m_Sequence);
            RebuildList();
        }

        void RebuildList()
        {
            if (m_Sequence == null)
            {
                m_List = null;
                return;
            }
            m_List = new ReorderableList(m_Sequence.rows, typeof(StoryboardRow), true, true, true, true);
            m_List.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "分镜行（可拖拽排序）");
            m_List.elementHeight = 44f;
            m_List.drawElementCallback = (rect, index, active, focused) =>
            {
                if (index < 0 || index >= m_Sequence.rows.Count)
                    return;
                var row = m_Sequence.rows[index];
                float y = rect.y + 2;
                float w = rect.width;
                EditorGUI.LabelField(new Rect(rect.x, y, 28, 18), (index + 1).ToString());
                var current = ShotLibrary.FindById(row.shotGuid);
                var picked = (ShotAsset)EditorGUI.ObjectField(new Rect(rect.x + 30, y, w * 0.32f, 18), current, typeof(ShotAsset), false);
                if (picked != current)
                {
                    Undo.RecordObject(m_Sequence, "改分镜镜头");
                    row.shotGuid = picked != null ? picked.Id : string.Empty;
                    EditorUtility.SetDirty(m_Sequence);
                }
                row.duration = EditorGUI.DoubleField(new Rect(rect.x + w * 0.35f, y, 54, 18), new GUIContent(string.Empty, "时长（秒）"), row.duration);
                var modeRect = new Rect(rect.x + w * 0.35f + 58, y, 110, 18);
                modeRect = CineHelp.Trim(modeRect, CineTips.Blend);
                row.blendMode = (ShotBlendMode)EditorGUI.EnumPopup(modeRect, row.blendMode);
                float noteX = rect.x + w * 0.35f + 172;
                if (row.blendMode == ShotBlendMode.Blend)
                {
                    var durRect = CineHelp.Trim(new Rect(noteX, y, 68, 18), CineTips.BlendDuration);
                    row.blendDuration = EditorGUI.FloatField(durRect, row.blendDuration);
                    noteX += 72f;
                }
                row.note = EditorGUI.TextField(new Rect(noteX, y, rect.xMax - noteX, 18), new GUIContent(string.Empty, "备注"), row.note ?? string.Empty);
                row.rowIndex = index;
            };
            m_List.onReorderCallback = list =>
            {
                Undo.RecordObject(m_Sequence, "重排分镜");
                for (int i = 0; i < m_Sequence.rows.Count; i++)
                    m_Sequence.rows[i].rowIndex = i;
                EditorUtility.SetDirty(m_Sequence);
            };
            m_List.onAddCallback = list =>
            {
                Undo.RecordObject(m_Sequence, "添加分镜行");
                m_Sequence.rows.Add(new StoryboardRow { rowIndex = m_Sequence.rows.Count, duration = 4.0 });
                EditorUtility.SetDirty(m_Sequence);
            };
        }

        void LayOut()
        {
            if (m_Director == null)
                m_Director = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponent<PlayableDirector>()
                    : null;
            if (m_Director == null)
            {
                EditorUtility.DisplayDialog("CineShot", "请先选中带 PlayableDirector 的对象，或在窗口顶部指定。", "确定");
                return;
            }
            ShotTrackLayer.Reconcile(m_Director, m_Sequence, ConflictPolicy.Ask);
        }
    }
}
