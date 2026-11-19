using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using CineShot.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CineShot.Editor
{
    public enum ConflictPolicy
    {
        Ask = 0,
        PreferUser = 1,
        PreferStoryboard = 2
    }

    public struct ReconcileReport
    {
        public int Created;
        public int Updated;
        public int Remapped;
        public int Conflict;
        public int Clean;
        public int Orphan;
        public override string ToString()
        {
            return "新建 " + Created + " / 更新 " + Updated + " / 重新匹配 " + Remapped
                   + " / 冲突 " + Conflict + " / 未变 " + Clean + " / 多余片段 " + Orphan;
        }
    }

    public static class ShotTrackLayer
    {
        public static ReconcileReport Reconcile(PlayableDirector director, CineSequenceAsset sequence, ConflictPolicy policy)
        {
            var report = new ReconcileReport();
            if (director == null || sequence == null)
            {
                EditorUtility.DisplayDialog("CineShot", "需要 PlayableDirector 与 CineSequenceAsset。", "确定");
                return report;
            }

            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null)
            {
                EditorUtility.DisplayDialog("CineShot", "PlayableDirector 未绑定 TimelineAsset。", "确定");
                return report;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(timeline, "CineShot Lay Out Tracks");

            CineShotTrack track = null;
            foreach (var outputTrack in timeline.GetOutputTracks())
            {
                track = outputTrack as CineShotTrack;
                if (track != null)
                    break;
            }
            if (track == null)
            {
                track = timeline.CreateTrack<CineShotTrack>(null, "CineShot Track");
                Undo.RegisterCreatedObjectUndo(track, "Create CineShotTrack");
            }

            var byKey = new Dictionary<string, List<TimelineClip>>();
            foreach (var c in track.GetClips())
            {
                var asset = c.asset as CineShotClip;
                if (asset == null)
                    continue;
                string key = string.IsNullOrEmpty(asset.matchKey) ? GuessKey(asset) : asset.matchKey;
                List<TimelineClip> list;
                if (!byKey.TryGetValue(key, out list))
                {
                    list = new List<TimelineClip>();
                    byKey[key] = list;
                }
                list.Add(c);
            }

            var claimed = new HashSet<TimelineClip>();
            double cursor = 0.0;
            float fps = (float)timeline.editorSettings.frameRate;
            if (fps < 1f) fps = 60f;

            for (int i = 0; i < sequence.rows.Count; i++)
            {
                var row = sequence.rows[i];
                if (row == null)
                    continue;
                row.rowIndex = i;
                string key = row.shotGuid + "#" + i;
                string hash = HashRow(row);
                double duration = Quantize(row.duration, fps);

                TimelineClip clip = null;
                List<TimelineClip> existing;
                if (byKey.TryGetValue(key, out existing) && existing.Count > 0)
                {
                    clip = existing[0];
                    for (int e = 1; e < existing.Count; e++)
                        report.Orphan++;
                }
                else
                {
                    clip = FindOrphanByShot(byKey, claimed, row.shotGuid);
                    if (clip != null)
                        report.Remapped++;
                }

                var shot = ShotLibrary.FindById(row.shotGuid);
                if (clip == null)
                {
                    clip = track.CreateDefaultClip();
                    clip.displayName = shot != null ? shot.displayName : ("Shot " + i);
                    var created = clip.asset as CineShotClip;
                    if (created == null)
                    {
                        created = ScriptableObject.CreateInstance<CineShotClip>();
                        clip.asset = created;
                    }
                    Undo.RegisterCreatedObjectUndo(created, "Create CineShotClip");
                    report.Created++;
                }
                else
                {
                    var asset = clip.asset as CineShotClip;
                    bool drift = asset != null && Math.Abs(clip.duration - asset.lastAppliedDuration) > 0.5 / fps;
                    bool hashChanged = asset == null || asset.sourceHash != hash;
                    if (!hashChanged && !drift)
                    {
                        report.Clean++;
                        claimed.Add(clip);
                        clip.start = cursor;
                        cursor += duration + row.gap;
                        continue;
                    }
                    if (drift && !hashChanged)
                    {
                        report.Conflict++;
                        if (policy == ConflictPolicy.PreferUser)
                        {
                            row.duration = clip.duration;
                            claimed.Add(clip);
                            cursor += clip.duration + row.gap;
                            continue;
                        }
                        if (policy == ConflictPolicy.Ask)
                        {
                            int choice = EditorUtility.DisplayDialogComplex(
                                "CineShot 冲突",
                                "Clip \"" + clip.displayName + "\" 被手工改过时长。",
                                "用分镜表覆盖", "保留用户值", "跳过");
                            if (choice == 1)
                            {
                                row.duration = clip.duration;
                                claimed.Add(clip);
                                cursor += clip.duration + row.gap;
                                continue;
                            }
                            if (choice == 2)
                            {
                                claimed.Add(clip);
                                cursor += clip.duration + row.gap;
                                continue;
                            }
                        }
                    }
                    report.Updated++;
                }

                claimed.Add(clip);
                var cineClip = clip.asset as CineShotClip;
                if (cineClip == null)
                {
                    cineClip = ScriptableObject.CreateInstance<CineShotClip>();
                    clip.asset = cineClip;
                }
                Undo.RecordObject(cineClip, "Update CineShotClip");
                cineClip.shot = shot;
                cineClip.matchKey = key;
                cineClip.sourceHash = hash;
                cineClip.blendMode = row.blendMode;
                cineClip.blendDuration = ShotBlend.ResolveDuration(row.blendDuration);
                cineClip.lastAppliedDuration = duration;
                clip.start = cursor;
                clip.duration = duration;
                clip.displayName = shot != null ? shot.displayName : ("Shot " + i);
                EditorUtility.SetDirty(cineClip);
                cursor += duration + row.gap;
            }

            foreach (var kv in byKey)
            {
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (!claimed.Contains(kv.Value[i]))
                        report.Orphan++;
                }
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            Debug.Log("[CineShot] 分镜铺轨：" + report);
            return report;
        }

        static TimelineClip FindOrphanByShot(Dictionary<string, List<TimelineClip>> byKey, HashSet<TimelineClip> claimed, string shotGuid)
        {
            foreach (var kv in byKey)
            {
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var c = kv.Value[i];
                    if (claimed.Contains(c))
                        continue;
                    var asset = c.asset as CineShotClip;
                    if (asset != null && asset.shot != null && asset.shot.Id == shotGuid)
                        return c;
                }
            }
            return null;
        }

        static string GuessKey(CineShotClip asset)
        {
            string guid = asset.shot != null ? asset.shot.Id : "null";
            return guid + "#0";
        }

        static string HashRow(StoryboardRow row)
        {
            string raw = row.shotGuid + "|" + row.duration + "|" + row.gap + "|" + row.blendMode + "|" + row.blendDuration;
            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static double Quantize(double v, float fps)
        {
            return Math.Round(v * fps) / fps;
        }
    }
}
