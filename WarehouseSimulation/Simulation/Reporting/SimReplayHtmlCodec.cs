using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using NonsensicalKit.Tools;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 可回放 HTML：浏览器当报告打开；内嵌回放用 v2 gzip+base64 紧凑格式，Unity 可加载复现。
    /// </summary>
    public static class SimReplayHtmlCodec
    {
        public const string Magic = "NK_WAREHOUSE_REPLAY";
        public const int CurrentVersion = 2;
        public const int MinSupportedVersion = 1;
        public const string EncodingGzipBase64 = "gzip-base64";
        public const string ScriptType = "application/x-warehouse-replay+json";
        public const string ScriptId = "warehouse-replay";
        public const string HtmlMarkerComment = "<!-- NK_WAREHOUSE_REPLAY_V2 -->";

        private const string ScriptOpen =
            "<script type=\"" + ScriptType + "\" id=\"" + ScriptId + "\">";
        private const string ScriptClose = "</script>";

        /// <summary>加载后展开的完整回放（供 SetTimeline）。</summary>
        [Serializable]
        public sealed class Payload
        {
            public string Magic = SimReplayHtmlCodec.Magic;
            public int Version = CurrentVersion;
            public double TotalSimSeconds;
            public string ScenarioName;
            public int CompletedJobCount;
            public int TargetJobCount;
            public double ThroughputPerHour;
            public string Message;
            public SimPlaybackEvent[] Events;
            public SimSubTask[] SubTasks;
            public GridIndex[] InitialOccupiedSlots;
        }

        /// <summary>写进 HTML 的外壳（体积很小）；真正数据在 Data 里。</summary>
        [Serializable]
        private sealed class Envelope
        {
            public string Magic;
            public int Version;
            public string Encoding;
            public string Data;
            // v1 兼容：无 Encoding 时下列字段直接存在于同一 JSON。
            public double TotalSimSeconds;
            public string ScenarioName;
            public int CompletedJobCount;
            public int TargetJobCount;
            public double ThroughputPerHour;
            public string Message;
            public SimPlaybackEvent[] Events;
            public SimSubTask[] SubTasks;
            public GridIndex[] InitialOccupiedSlots;
        }

        /// <summary>压缩前的紧凑体：路径/时刻表按任务去重，初始占用打成 int 数组。</summary>
        [Serializable]
        private sealed class CompactBody
        {
            public string Magic;
            public int Version;
            public double TotalSimSeconds;
            public string ScenarioName;
            public int CompletedJobCount;
            public int TargetJobCount;
            public double ThroughputPerHour;
            public string Message;
            public SimPlaybackEvent[] Events;
            public SimSubTask[] SubTasks;
            /// <summary>每 5 个一组：Level,Column,Row,Depth,WarehouseId。</summary>
            public int[] InitialOccupiedPacked;
            public int[][] Paths;
            public ConveyorSegmentScheduleEntry[][] Schedules;
            public int[] JobIdsForPaths;
            public int[] PathIndexByJob;
            public int[] JobIdsForSchedules;
            public int[] ScheduleIndexByJob;
        }

        public static Payload BuildPayload(
            IReadOnlyList<SimPlaybackEvent> events,
            IReadOnlyList<SimSubTask> subTasks,
            IReadOnlyList<GridIndex> initialOccupiedSlots,
            double totalSimSeconds,
            SimRunResult result = null,
            string scenarioName = null)
        {
            return new Payload
            {
                Magic = Magic,
                Version = CurrentVersion,
                TotalSimSeconds = totalSimSeconds,
                ScenarioName = scenarioName ?? string.Empty,
                CompletedJobCount = result?.CompletedJobCount ?? 0,
                TargetJobCount = result?.TargetJobCount ?? 0,
                ThroughputPerHour = result?.ThroughputPerHour ?? 0d,
                Message = result?.Message,
                Events = ToArray(events),
                SubTasks = ToArray(subTasks),
                InitialOccupiedSlots = ToArray(initialOccupiedSlots),
            };
        }

        /// <summary>序列化为 HTML 内嵌用的外壳 JSON（v2：gzip+base64）。</summary>
        public static string SerializePayload(Payload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var compact = ToCompact(payload);
            var compressed = GzipCompact(compact);
            var envelope = new Envelope
            {
                Magic = Magic,
                Version = CurrentVersion,
                Encoding = EncodingGzipBase64,
                Data = Convert.ToBase64String(compressed),
            };
            return JsonTool.SerializeObject(envelope);
        }

        public static void AppendEmbeddedReplay(StringBuilder sb, Payload payload)
        {
            if (sb == null || payload == null)
            {
                return;
            }

            var json = SerializePayload(payload);
            json = json.Replace("</", "<\\/");

            var eventCount = payload.Events?.Length ?? 0;
            var subTaskCount = payload.SubTasks?.Length ?? 0;
            var initialCount = payload.InitialOccupiedSlots?.Length ?? 0;
            var embedBytes = Encoding.UTF8.GetByteCount(json);

            sb.AppendLine("<section class=\"card\" id=\"replay-embed\">");
            sb.AppendLine("<h2>可回放数据</h2>");
            sb.AppendLine("<p class=\"meta\">本 HTML 已内嵌完整仿真回放（gzip 压缩）。用浏览器可正常阅读报告；在 Unity 仿真面板点「加载回放」选本文件即可播放。</p>");
            sb.Append("<p class=\"meta\">事件 ");
            sb.Append(eventCount.ToString());
            sb.Append(" 条 · 子任务 ");
            sb.Append(subTaskCount.ToString());
            sb.Append(" 条 · 初始占用货位 ");
            sb.Append(initialCount.ToString());
            sb.Append(" 个 · 内嵌约 ");
            sb.Append((embedBytes / 1024d).ToString("0.#"));
            sb.AppendLine(" KB</p>");
            sb.AppendLine("</section>");
            sb.AppendLine(HtmlMarkerComment);
            sb.AppendLine(ScriptOpen);
            sb.AppendLine(json);
            sb.AppendLine(ScriptClose);
        }

        public static bool TryLoadFromFile(string path, out Payload payload, out string error)
        {
            payload = null;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "路径为空。";
                return false;
            }

            if (!File.Exists(path))
            {
                error = $"找不到文件：{path}";
                return false;
            }

            string html;
            try
            {
                html = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = $"读取失败：{ex.Message}";
                return false;
            }

            return TryParseHtml(html, out payload, out error);
        }

        public static bool TryParseHtml(string html, out Payload payload, out string error)
        {
            payload = null;
            error = null;
            if (string.IsNullOrEmpty(html))
            {
                error = "文件内容为空。";
                return false;
            }

            if (html.IndexOf(Magic, StringComparison.Ordinal) < 0
                && html.IndexOf(ScriptId, StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = "这是普通报告 HTML，不含回放数据。请导出「可回放报告」后再加载。";
                return false;
            }

            if (!TryExtractJson(html, out var json, out error))
            {
                return false;
            }

            Envelope envelope;
            try
            {
                envelope = JsonTool.DeserializeObject<Envelope>(json);
            }
            catch (Exception ex)
            {
                error = $"回放数据解析失败：{ex.Message}";
                return false;
            }

            if (envelope == null
                || !string.Equals(envelope.Magic, Magic, StringComparison.Ordinal))
            {
                error = "回放数据魔数不匹配，无法识别为仓储仿真回放文件。";
                return false;
            }

            if (envelope.Version < MinSupportedVersion)
            {
                error = $"不支持的回放数据版本：{envelope.Version}。";
                return false;
            }

            if (envelope.Version > CurrentVersion)
            {
                error = $"回放数据版本 {envelope.Version} 过新（当前支持到 {CurrentVersion}），请升级仿真包或重新导出。";
                return false;
            }

            try
            {
                if (string.Equals(envelope.Encoding, EncodingGzipBase64, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(envelope.Data))
                    {
                        error = "回放压缩数据为空。";
                        return false;
                    }

                    var bytes = Convert.FromBase64String(envelope.Data);
                    var compactJson = Encoding.UTF8.GetString(Gunzip(bytes));
                    var compact = JsonTool.DeserializeObject<CompactBody>(compactJson);
                    payload = FromCompact(compact);
                }
                else if (string.IsNullOrEmpty(envelope.Encoding))
                {
                    // v1：整份 Payload 直接写在 script 里。
                    payload = new Payload
                    {
                        Magic = envelope.Magic,
                        Version = envelope.Version,
                        TotalSimSeconds = envelope.TotalSimSeconds,
                        ScenarioName = envelope.ScenarioName,
                        CompletedJobCount = envelope.CompletedJobCount,
                        TargetJobCount = envelope.TargetJobCount,
                        ThroughputPerHour = envelope.ThroughputPerHour,
                        Message = envelope.Message,
                        Events = envelope.Events ?? Array.Empty<SimPlaybackEvent>(),
                        SubTasks = envelope.SubTasks,
                        InitialOccupiedSlots = envelope.InitialOccupiedSlots ?? Array.Empty<GridIndex>(),
                    };
                }
                else
                {
                    error = $"不支持的回放编码：{envelope.Encoding}。请重新导出可回放报告。";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"回放数据解码失败：{ex.Message}";
                return false;
            }

            if (payload?.SubTasks == null || payload.SubTasks.Length == 0)
            {
                error = "回放数据里没有子任务时间轴，无法播放。";
                payload = null;
                return false;
            }

            payload.Events ??= Array.Empty<SimPlaybackEvent>();
            payload.InitialOccupiedSlots ??= Array.Empty<GridIndex>();
            payload.Magic = Magic;
            return true;
        }

        private static CompactBody ToCompact(Payload payload)
        {
            var paths = new List<int[]>();
            var schedules = new List<ConveyorSegmentScheduleEntry[]>();
            var pathByJob = new Dictionary<int, int>();
            var scheduleByJob = new Dictionary<int, int>();
            var pathKeyToIndex = new Dictionary<string, int>();
            var scheduleKeyToIndex = new Dictionary<string, int>();

            var subTasks = payload.SubTasks ?? Array.Empty<SimSubTask>();
            var strippedSubTasks = new SimSubTask[subTasks.Length];
            for (var i = 0; i < subTasks.Length; i++)
            {
                var src = subTasks[i];
                RegisterPath(src.JobId, src.PathNodeIndices, paths, pathByJob, pathKeyToIndex);
                RegisterSchedule(src.JobId, src.SegmentSchedule, schedules, scheduleByJob, scheduleKeyToIndex);

                src.PathNodeIndices = null;
                src.SegmentSchedule = null;
                strippedSubTasks[i] = src;
            }

            // 事件列表与子任务时刻表重复，v2 不再落盘；加载后 Events 为空数组即可播放。
            FlattenMaps(pathByJob, out var jobIdsForPaths, out var pathIndexByJob);
            FlattenMaps(scheduleByJob, out var jobIdsForSchedules, out var scheduleIndexByJob);

            return new CompactBody
            {
                Magic = Magic,
                Version = CurrentVersion,
                TotalSimSeconds = payload.TotalSimSeconds,
                ScenarioName = payload.ScenarioName,
                CompletedJobCount = payload.CompletedJobCount,
                TargetJobCount = payload.TargetJobCount,
                ThroughputPerHour = payload.ThroughputPerHour,
                Message = payload.Message,
                Events = Array.Empty<SimPlaybackEvent>(),
                SubTasks = strippedSubTasks,
                InitialOccupiedPacked = PackSlots(payload.InitialOccupiedSlots),
                Paths = paths.ToArray(),
                Schedules = schedules.ToArray(),
                JobIdsForPaths = jobIdsForPaths,
                PathIndexByJob = pathIndexByJob,
                JobIdsForSchedules = jobIdsForSchedules,
                ScheduleIndexByJob = scheduleIndexByJob,
            };
        }

        private static Payload FromCompact(CompactBody compact)
        {
            if (compact == null)
            {
                return null;
            }

            var pathByJob = BuildMap(compact.JobIdsForPaths, compact.PathIndexByJob);
            var scheduleByJob = BuildMap(compact.JobIdsForSchedules, compact.ScheduleIndexByJob);
            var paths = compact.Paths ?? Array.Empty<int[]>();
            var schedules = compact.Schedules ?? Array.Empty<ConveyorSegmentScheduleEntry[]>();

            var subTasks = compact.SubTasks ?? Array.Empty<SimSubTask>();
            var restored = new SimSubTask[subTasks.Length];
            var attachedPath = new HashSet<int>();
            var attachedSchedule = new HashSet<int>();
            for (var i = 0; i < subTasks.Length; i++)
            {
                var task = subTasks[i];
                if (pathByJob.TryGetValue(task.JobId, out var pathIdx)
                    && pathIdx >= 0
                    && pathIdx < paths.Length
                    && attachedPath.Add(task.JobId))
                {
                    task.PathNodeIndices = paths[pathIdx];
                }

                if (scheduleByJob.TryGetValue(task.JobId, out var schedIdx)
                    && schedIdx >= 0
                    && schedIdx < schedules.Length
                    && attachedSchedule.Add(task.JobId))
                {
                    task.SegmentSchedule = schedules[schedIdx];
                }

                restored[i] = task;
            }

            return new Payload
            {
                Magic = Magic,
                Version = compact.Version,
                TotalSimSeconds = compact.TotalSimSeconds,
                ScenarioName = compact.ScenarioName,
                CompletedJobCount = compact.CompletedJobCount,
                TargetJobCount = compact.TargetJobCount,
                ThroughputPerHour = compact.ThroughputPerHour,
                Message = compact.Message,
                Events = compact.Events ?? Array.Empty<SimPlaybackEvent>(),
                SubTasks = restored,
                InitialOccupiedSlots = UnpackSlots(compact.InitialOccupiedPacked),
            };
        }

        private static void RegisterPath(
            int jobId,
            int[] path,
            List<int[]> paths,
            Dictionary<int, int> pathByJob,
            Dictionary<string, int> keyToIndex)
        {
            if (path == null || path.Length == 0 || pathByJob.ContainsKey(jobId))
            {
                return;
            }

            var key = BuildIntArrayKey(path);
            if (!keyToIndex.TryGetValue(key, out var index))
            {
                index = paths.Count;
                paths.Add(path);
                keyToIndex[key] = index;
            }

            pathByJob[jobId] = index;
        }

        private static void RegisterSchedule(
            int jobId,
            ConveyorSegmentScheduleEntry[] schedule,
            List<ConveyorSegmentScheduleEntry[]> schedules,
            Dictionary<int, int> scheduleByJob,
            Dictionary<string, int> keyToIndex)
        {
            if (schedule == null || schedule.Length == 0)
            {
                return;
            }

            // 同任务保留更长的时刻表（与回放自检逻辑一致）。
            if (scheduleByJob.TryGetValue(jobId, out var existingIdx))
            {
                var existing = schedules[existingIdx];
                if (existing != null && existing.Length >= schedule.Length)
                {
                    return;
                }
            }

            var key = BuildScheduleKey(schedule);
            if (!keyToIndex.TryGetValue(key, out var index))
            {
                index = schedules.Count;
                schedules.Add(schedule);
                keyToIndex[key] = index;
            }

            scheduleByJob[jobId] = index;
        }

        private static string BuildIntArrayKey(int[] values)
        {
            var sb = new StringBuilder(values.Length * 4);
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(values[i]);
            }

            return sb.ToString();
        }

        private static void FlattenMaps(
            Dictionary<int, int> map,
            out int[] jobIds,
            out int[] indices)
        {
            jobIds = new int[map.Count];
            indices = new int[map.Count];
            var i = 0;
            foreach (var pair in map)
            {
                jobIds[i] = pair.Key;
                indices[i] = pair.Value;
                i++;
            }
        }

        private static Dictionary<int, int> BuildMap(int[] jobIds, int[] indices)
        {
            var map = new Dictionary<int, int>();
            if (jobIds == null || indices == null)
            {
                return map;
            }

            var n = Math.Min(jobIds.Length, indices.Length);
            for (var i = 0; i < n; i++)
            {
                map[jobIds[i]] = indices[i];
            }

            return map;
        }

        private static int[] PackSlots(GridIndex[] slots)
        {
            if (slots == null || slots.Length == 0)
            {
                return Array.Empty<int>();
            }

            var packed = new int[slots.Length * 5];
            for (var i = 0; i < slots.Length; i++)
            {
                var o = i * 5;
                packed[o] = slots[i].Level;
                packed[o + 1] = slots[i].Column;
                packed[o + 2] = slots[i].Row;
                packed[o + 3] = slots[i].Depth;
                packed[o + 4] = slots[i].WarehouseId;
            }

            return packed;
        }

        private static GridIndex[] UnpackSlots(int[] packed)
        {
            if (packed == null || packed.Length < 5)
            {
                return Array.Empty<GridIndex>();
            }

            var count = packed.Length / 5;
            var slots = new GridIndex[count];
            for (var i = 0; i < count; i++)
            {
                var o = i * 5;
                slots[i] = new GridIndex(
                    packed[o],
                    packed[o + 1],
                    packed[o + 2],
                    packed[o + 3],
                    packed[o + 4]);
            }

            return slots;
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [ThreadStatic]
        private static char[] t_digits;

        private static byte[] GzipCompact(CompactBody compact)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 65536, leaveOpen: true))
            {
                WriteCompact(writer, compact);
            }

            return output.ToArray();
        }

        private static string BuildScheduleKey(ConveyorSegmentScheduleEntry[] schedule)
        {
            var sb = new StringBuilder(schedule.Length * 48);
            for (var i = 0; i < schedule.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(';');
                }

                var entry = schedule[i];
                sb.Append(entry.FromNodeIndex).Append(',')
                    .Append(entry.ToNodeIndex).Append(',')
                    .Append(entry.PathEdgeIndex).Append(',')
                    .Append(entry.SlotIndex).Append(',')
                    .Append(entry.DesiredEntrySimTime.ToString("G17", Inv)).Append(',')
                    .Append(entry.EntrySimTime.ToString("G17", Inv)).Append(',')
                    .Append(entry.ExitSimTime.ToString("G17", Inv)).Append(',')
                    .Append(entry.OccupancyEndSimTime.ToString("G17", Inv));
                var stops = entry.StopArriveSimTimes;
                if (stops == null)
                {
                    continue;
                }

                for (var s = 0; s < stops.Length; s++)
                {
                    sb.Append('|').Append(stops[s].ToString("G17", Inv));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 手写与 Newtonsoft 默认可对上的字段名，边写边压缩。
        /// 反序列化按名字取字段，不依赖属性顺序。
        /// </summary>
        private static void WriteCompact(TextWriter w, CompactBody body)
        {
            w.Write('{');
            WriteStringProp(w, "Magic", body.Magic, first: true);
            WriteIntProp(w, "Version", body.Version);
            WriteDoubleProp(w, "TotalSimSeconds", body.TotalSimSeconds);
            WriteStringProp(w, "ScenarioName", body.ScenarioName ?? string.Empty);
            WriteIntProp(w, "CompletedJobCount", body.CompletedJobCount);
            WriteIntProp(w, "TargetJobCount", body.TargetJobCount);
            WriteDoubleProp(w, "ThroughputPerHour", body.ThroughputPerHour);
            if (body.Message == null)
            {
                w.Write(",\"Message\":null");
            }
            else
            {
                WriteStringProp(w, "Message", body.Message);
            }

            w.Write(",\"Events\":[]");
            w.Write(",\"SubTasks\":");
            WriteSubTasks(w, body.SubTasks);
            w.Write(",\"InitialOccupiedPacked\":");
            WriteIntArray(w, body.InitialOccupiedPacked);
            w.Write(",\"Paths\":");
            WriteJaggedInts(w, body.Paths);
            w.Write(",\"Schedules\":");
            WriteSchedules(w, body.Schedules);
            w.Write(",\"JobIdsForPaths\":");
            WriteIntArray(w, body.JobIdsForPaths);
            w.Write(",\"PathIndexByJob\":");
            WriteIntArray(w, body.PathIndexByJob);
            w.Write(",\"JobIdsForSchedules\":");
            WriteIntArray(w, body.JobIdsForSchedules);
            w.Write(",\"ScheduleIndexByJob\":");
            WriteIntArray(w, body.ScheduleIndexByJob);
            w.Write('}');
        }

        private static void WriteSubTasks(TextWriter w, SimSubTask[] tasks)
        {
            w.Write('[');
            if (tasks != null)
            {
                for (var i = 0; i < tasks.Length; i++)
                {
                    if (i > 0)
                    {
                        w.Write(',');
                    }

                    WriteSubTask(w, tasks[i]);
                }
            }

            w.Write(']');
        }

        private static void WriteSubTask(TextWriter w, SimSubTask task)
        {
            w.Write("{\"SubTaskId\":");
            WriteInt(w, task.SubTaskId);
            w.Write(",\"JobId\":");
            WriteInt(w, task.JobId);
            w.Write(",\"Kind\":");
            WriteInt(w, (int)task.Kind);
            w.Write(",\"StartSimTime\":");
            WriteDouble(w, task.StartSimTime);
            w.Write(",\"EndSimTime\":");
            WriteDouble(w, task.EndSimTime);
            w.Write(",\"StackerId\":");
            WriteInt(w, task.StackerId);
            w.Write(",\"Slot\":");
            WriteGrid(w, task.Slot);
            w.Write(",\"InfeedPortIndex\":");
            WriteInt(w, task.InfeedPortIndex);
            w.Write(",\"OutfeedPortIndex\":");
            WriteInt(w, task.OutfeedPortIndex);
            w.Write(",\"PickupPointIndex\":");
            WriteInt(w, task.PickupPointIndex);
            w.Write(",\"FromNodeIndex\":");
            WriteInt(w, task.FromNodeIndex);
            w.Write(",\"ToNodeIndex\":");
            WriteInt(w, task.ToNodeIndex);
            w.Write(",\"SegmentSlotIndex\":");
            WriteInt(w, task.SegmentSlotIndex);
            w.Write(",\"StackerFromSlot\":");
            WriteGrid(w, task.StackerFromSlot);
            w.Write(",\"StackerToSlot\":");
            WriteGrid(w, task.StackerToSlot);
            w.Write(",\"StackerRailColumn\":");
            WriteInt(w, task.StackerRailColumn);
            w.Write(",\"StackerToUsesForkDepth\":");
            w.Write(task.StackerToUsesForkDepth ? "true" : "false");
            w.Write(",\"StackerToForkDepthMeters\":");
            WriteDouble(w, task.StackerToForkDepthMeters);
            w.Write(",\"HasStackerPose\":");
            w.Write(task.HasStackerPose ? "true" : "false");
            w.Write(",\"HasCargoHandover\":");
            w.Write(task.HasCargoHandover ? "true" : "false");
            w.Write(",\"CargoHandoverSimTime\":");
            WriteDouble(w, task.CargoHandoverSimTime);
            w.Write(",\"PathNodeIndices\":");
            WriteIntArray(w, task.PathNodeIndices);
            w.Write(",\"SegmentSchedule\":");
            if (task.SegmentSchedule == null)
            {
                w.Write("null");
            }
            else
            {
                WriteScheduleArray(w, task.SegmentSchedule);
            }

            w.Write('}');
        }

        private static void WriteSchedules(TextWriter w, ConveyorSegmentScheduleEntry[][] schedules)
        {
            w.Write('[');
            if (schedules != null)
            {
                for (var i = 0; i < schedules.Length; i++)
                {
                    if (i > 0)
                    {
                        w.Write(',');
                    }

                    WriteScheduleArray(w, schedules[i]);
                }
            }

            w.Write(']');
        }

        private static void WriteScheduleArray(TextWriter w, ConveyorSegmentScheduleEntry[] schedule)
        {
            if (schedule == null)
            {
                w.Write("null");
                return;
            }

            w.Write('[');
            for (var i = 0; i < schedule.Length; i++)
            {
                if (i > 0)
                {
                    w.Write(',');
                }

                var entry = schedule[i];
                w.Write("{\"FromNodeIndex\":");
                WriteInt(w, entry.FromNodeIndex);
                w.Write(",\"ToNodeIndex\":");
                WriteInt(w, entry.ToNodeIndex);
                w.Write(",\"PathEdgeIndex\":");
                WriteInt(w, entry.PathEdgeIndex);
                w.Write(",\"SlotIndex\":");
                WriteInt(w, entry.SlotIndex);
                w.Write(",\"DesiredEntrySimTime\":");
                WriteDouble(w, entry.DesiredEntrySimTime);
                w.Write(",\"EntrySimTime\":");
                WriteDouble(w, entry.EntrySimTime);
                w.Write(",\"ExitSimTime\":");
                WriteDouble(w, entry.ExitSimTime);
                w.Write(",\"OccupancyEndSimTime\":");
                WriteDouble(w, entry.OccupancyEndSimTime);
                w.Write(",\"StopArriveSimTimes\":");
                WriteDoubleArray(w, entry.StopArriveSimTimes);
                w.Write('}');
            }

            w.Write(']');
        }

        private static void WriteGrid(TextWriter w, GridIndex slot)
        {
            w.Write("{\"Level\":");
            WriteInt(w, slot.Level);
            w.Write(",\"Column\":");
            WriteInt(w, slot.Column);
            w.Write(",\"Row\":");
            WriteInt(w, slot.Row);
            w.Write(",\"Depth\":");
            WriteInt(w, slot.Depth);
            w.Write(",\"WarehouseId\":");
            WriteInt(w, slot.WarehouseId);
            w.Write('}');
        }

        private static void WriteJaggedInts(TextWriter w, int[][] rows)
        {
            w.Write('[');
            if (rows != null)
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    if (i > 0)
                    {
                        w.Write(',');
                    }

                    WriteIntArray(w, rows[i]);
                }
            }

            w.Write(']');
        }

        private static void WriteIntArray(TextWriter w, int[] values)
        {
            if (values == null)
            {
                w.Write("null");
                return;
            }

            w.Write('[');
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    w.Write(',');
                }

                WriteInt(w, values[i]);
            }

            w.Write(']');
        }

        private static void WriteDoubleArray(TextWriter w, double[] values)
        {
            if (values == null)
            {
                w.Write("null");
                return;
            }

            w.Write('[');
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    w.Write(',');
                }

                WriteDouble(w, values[i]);
            }

            w.Write(']');
        }

        private static void WriteStringProp(TextWriter w, string name, string value, bool first = false)
        {
            if (!first)
            {
                w.Write(',');
            }

            w.Write('"');
            w.Write(name);
            w.Write("\":");
            WriteString(w, value);
        }

        private static void WriteIntProp(TextWriter w, string name, int value)
        {
            w.Write(",\"");
            w.Write(name);
            w.Write("\":");
            WriteInt(w, value);
        }

        private static void WriteDoubleProp(TextWriter w, string name, double value)
        {
            w.Write(",\"");
            w.Write(name);
            w.Write("\":");
            WriteDouble(w, value);
        }

        private static void WriteString(TextWriter w, string value)
        {
            w.Write('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var ch = value[i];
                    switch (ch)
                    {
                        case '"': w.Write("\\\""); break;
                        case '\\': w.Write("\\\\"); break;
                        case '\n': w.Write("\\n"); break;
                        case '\r': w.Write("\\r"); break;
                        case '\t': w.Write("\\t"); break;
                        default:
                            if (ch < 0x20)
                            {
                                w.Write("\\u");
                                w.Write(((int)ch).ToString("x4", Inv));
                            }
                            else
                            {
                                w.Write(ch);
                            }

                            break;
                    }
                }
            }

            w.Write('"');
        }

        private static void WriteInt(TextWriter w, int value)
        {
            if (value == 0)
            {
                w.Write('0');
                return;
            }

            if (value == int.MinValue)
            {
                w.Write("-2147483648");
                return;
            }

            var buf = t_digits ??= new char[16];
            var neg = value < 0;
            var n = neg ? -value : value;
            var i = buf.Length;
            while (n > 0)
            {
                buf[--i] = (char)('0' + n % 10);
                n /= 10;
            }

            if (neg)
            {
                buf[--i] = '-';
            }

            w.Write(buf, i, buf.Length - i);
        }

        private static void WriteDouble(TextWriter w, double value)
        {
            if (double.IsNaN(value))
            {
                w.Write("\"NaN\"");
                return;
            }

            if (double.IsPositiveInfinity(value))
            {
                w.Write("\"Infinity\"");
                return;
            }

            if (double.IsNegativeInfinity(value))
            {
                w.Write("\"-Infinity\"");
                return;
            }

            w.Write(value.ToString("G17", Inv));
        }

        private static byte[] Gzip(byte[] raw)
        {
            using var output = new MemoryStream(Math.Max(64, raw.Length / 4));
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return output.ToArray();
        }

        private static byte[] Gunzip(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private static bool TryExtractJson(string html, out string json, out string error)
        {
            json = null;
            error = null;

            var openIdx = html.IndexOf(ScriptOpen, StringComparison.OrdinalIgnoreCase);
            if (openIdx < 0)
            {
                openIdx = html.IndexOf("id=\"" + ScriptId + "\"", StringComparison.OrdinalIgnoreCase);
                if (openIdx < 0)
                {
                    openIdx = html.IndexOf("id='" + ScriptId + "'", StringComparison.OrdinalIgnoreCase);
                }

                if (openIdx < 0)
                {
                    error = "未找到内嵌回放数据块（warehouse-replay）。";
                    return false;
                }

                var gt = html.IndexOf('>', openIdx);
                if (gt < 0)
                {
                    error = "回放数据标签损坏。";
                    return false;
                }

                openIdx = gt + 1;
            }
            else
            {
                openIdx += ScriptOpen.Length;
            }

            var closeIdx = html.IndexOf(ScriptClose, openIdx, StringComparison.OrdinalIgnoreCase);
            if (closeIdx < 0)
            {
                error = "回放数据标签未正确闭合。";
                return false;
            }

            json = html.Substring(openIdx, closeIdx - openIdx).Trim();
            if (json.Length == 0)
            {
                error = "回放数据块为空。";
                return false;
            }

            json = json.Replace("<\\/", "</");
            return true;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0)
            {
                return Array.Empty<T>();
            }

            if (list is T[] arr)
            {
                return arr;
            }

            var copy = new T[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                copy[i] = list[i];
            }

            return copy;
        }
    }
}
