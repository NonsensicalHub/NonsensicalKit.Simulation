using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar
{
    /// <summary>
    /// 统一时空资源日历（阶段2）。
    /// <list type="bullet">
    /// <item><see cref="FindEarliestWindow"/> 只查询，绝不写入</item>
    /// <item><see cref="TryCommitExact"/> 原子提交；失败零副作用</item>
    /// <item><see cref="CommitEarliest"/> 查询并提交；成功才有句柄</item>
    /// <item>半开区间 [start,end)；同刻释放与进入不冲突（T11）</item>
    /// </list>
    /// 与旧 <c>ReservationTable.TryReserve</c> 不同：本 API 不会在返回失败时仍写入顺延预约。
    /// </summary>
    public sealed class ResourceCalendar
    {
        private const double Eps = 1e-9;
        private const int MaxEarliestIterations = 100000;

        private readonly Dictionary<string, ResourceDefinition> _resources =
            new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ReservationRecord>> _byResource =
            new Dictionary<string, List<ReservationRecord>>(StringComparer.Ordinal);
        private readonly Dictionary<long, ReservationRecord> _byId = new Dictionary<long, ReservationRecord>();
        private readonly Dictionary<string, HashSet<string>> _groupMembers =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        private long _nextId = 1;

        private sealed class ReservationRecord
        {
            public long Id;
            public string ResourceId;
            public double Start;
            public double End;
            public int Units;
            public TravelDirection Direction;
            public int JobId;
            public int RouteOccurrenceIndex;
        }

        public void Clear()
        {
            _resources.Clear();
            _byResource.Clear();
            _byId.Clear();
            _groupMembers.Clear();
            _nextId = 1;
        }

        public bool TryGetResource(string resourceId, out ResourceDefinition definition) =>
            _resources.TryGetValue(resourceId ?? string.Empty, out definition);

        public int ReservationCount => _byId.Count;

        /// <summary>注册容量资源。重复 ID 返回 false。</summary>
        public bool Register(ResourceDefinition definition, out ResourceCalendarErrorCode error)
        {
            error = ResourceCalendarErrorCode.Ok;
            if (definition == null || string.IsNullOrWhiteSpace(definition.ResourceId))
            {
                error = ResourceCalendarErrorCode.NullRequest;
                return false;
            }

            var id = definition.ResourceId.Trim();
            definition.ResourceId = id;
            if (definition.Capacity < 1)
            {
                definition.Capacity = 1;
            }

            if (_resources.ContainsKey(id))
            {
                error = ResourceCalendarErrorCode.DuplicateResourceId;
                return false;
            }

            _resources[id] = definition;
            _byResource[id] = new List<ReservationRecord>();

            if (!string.IsNullOrWhiteSpace(definition.ConflictGroupId))
            {
                var gid = definition.ConflictGroupId.Trim();
                definition.ConflictGroupId = gid;
                if (!_groupMembers.TryGetValue(gid, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    _groupMembers[gid] = set;
                }

                set.Add(id);

                var groupResourceId = ResourceIds.MovementConflictGroup(gid);
                if (!_resources.ContainsKey(groupResourceId))
                {
                    _resources[groupResourceId] = new ResourceDefinition
                    {
                        ResourceId = groupResourceId,
                        Kind = ResourceKind.MovementConflictGroup,
                        Capacity = 1,
                    };
                    _byResource[groupResourceId] = new List<ReservationRecord>();
                }
            }

            return true;
        }

        public bool Register(
            string resourceId,
            ResourceKind kind,
            int capacity = 1,
            string conflictGroupId = null) =>
            Register(
                new ResourceDefinition
                {
                    ResourceId = resourceId,
                    Kind = kind,
                    Capacity = capacity,
                    ConflictGroupId = conflictGroupId,
                },
                out _);

        /// <summary>确保资源存在；若不存在则按给定定义注册。</summary>
        public void EnsureRegistered(ResourceDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.ResourceId))
            {
                return;
            }

            if (_resources.ContainsKey(definition.ResourceId.Trim()))
            {
                return;
            }

            Register(definition, out _);
        }

        /// <summary>查询共同最早锚点；绝不写入。</summary>
        public FindWindowResult FindEarliestWindow(
            IReadOnlyList<ResourceWindowRequest> requests,
            double earliestStart)
        {
            if (requests == null || requests.Count == 0)
            {
                return FindWindowResult.Fail(new ResourceConflictInfo
                {
                    Code = ResourceCalendarErrorCode.NullRequest,
                    Message = "请求列表为空。",
                });
            }

            for (var i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                if (req == null || string.IsNullOrWhiteSpace(req.ResourceId))
                {
                    return FindWindowResult.Fail(new ResourceConflictInfo
                    {
                        Code = ResourceCalendarErrorCode.NullRequest,
                        Message = $"请求[{i}] 无效。",
                    });
                }

                if (req.Duration <= 0 || double.IsNaN(req.Duration) || double.IsInfinity(req.Duration))
                {
                    return FindWindowResult.Fail(new ResourceConflictInfo
                    {
                        Code = ResourceCalendarErrorCode.InvalidWindow,
                        ResourceId = req.ResourceId,
                        Message = $"资源 {req.ResourceId} 时长非法（要求 duration > 0，半开窗 end > start）。",
                    });
                }

                if (!_resources.ContainsKey(req.ResourceId))
                {
                    return FindWindowResult.Fail(new ResourceConflictInfo
                    {
                        Code = ResourceCalendarErrorCode.UnknownResource,
                        ResourceId = req.ResourceId,
                        Message = $"未知资源：{req.ResourceId}",
                    });
                }
            }

            var anchor = Math.Max(0, earliestStart);
            for (var iter = 0; iter < MaxEarliestIterations; iter++)
            {
                if (TryEvaluateAnchor(requests, anchor, out var windows, out var conflict, out var nextAnchor))
                {
                    return FindWindowResult.Ok(anchor, windows);
                }

                if (nextAnchor <= anchor + Eps)
                {
                    nextAnchor = anchor + Eps;
                }

                if (double.IsInfinity(nextAnchor) || double.IsNaN(nextAnchor))
                {
                    return FindWindowResult.Fail(conflict ?? new ResourceConflictInfo
                    {
                        Code = ResourceCalendarErrorCode.CapacityExceeded,
                        Message = "无法找到共同时间窗。",
                    });
                }

                anchor = nextAnchor;
            }

            return FindWindowResult.Fail(new ResourceConflictInfo
            {
                Code = ResourceCalendarErrorCode.CapacityExceeded,
                Message = "查找最早空档超过迭代上限。",
            });
        }

        /// <summary>
        /// 原子精确提交。全部可写入才写入；任一冲突则零副作用并返回 Conflict。
        /// </summary>
        public CommitResult TryCommitExact(IReadOnlyList<ExactReservationSpec> specs)
        {
            if (specs == null || specs.Count == 0)
            {
                return CommitResult.Fail(new ResourceConflictInfo
                {
                    Code = ResourceCalendarErrorCode.NullRequest,
                    Message = "提交列表为空。",
                });
            }

            // 预检：不写入
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var check = ValidateSpec(spec);
                if (check != null)
                {
                    return CommitResult.Fail(check);
                }
            }

            // 二次预检：考虑本批次内互相占用（同一资源多条）
            if (!CanCommitBatchTogether(specs, out var batchConflict))
            {
                return CommitResult.Fail(batchConflict);
            }

            var handles = new List<ReservationHandle>(specs.Count);
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var record = new ReservationRecord
                {
                    Id = _nextId++,
                    ResourceId = spec.ResourceId.Trim(),
                    Start = spec.Start,
                    End = spec.End,
                    Units = Math.Max(1, spec.Units),
                    Direction = spec.Direction,
                    JobId = spec.JobId,
                    RouteOccurrenceIndex = spec.RouteOccurrenceIndex,
                };
                InsertRecord(record);
                handles.Add(ToHandle(record));

                // 若资源属于冲突组，同步占用组资源（容量1）
                if (_resources.TryGetValue(record.ResourceId, out var def)
                    && !string.IsNullOrEmpty(def.ConflictGroupId))
                {
                    var groupId = ResourceIds.MovementConflictGroup(def.ConflictGroupId);
                    var groupRecord = new ReservationRecord
                    {
                        Id = _nextId++,
                        ResourceId = groupId,
                        Start = record.Start,
                        End = record.End,
                        Units = 1,
                        Direction = TravelDirection.None,
                        JobId = record.JobId,
                        RouteOccurrenceIndex = record.RouteOccurrenceIndex,
                    };
                    InsertRecord(groupRecord);
                    handles.Add(ToHandle(groupRecord));
                }
            }

            return CommitResult.Ok(handles);
        }

        /// <summary>查询最早共同窗并原子提交；失败不产生句柄。</summary>
        public CommitResult CommitEarliest(
            IReadOnlyList<ResourceWindowRequest> requests,
            double earliestStart,
            int jobId = 0,
            int routeOccurrenceIndex = -1)
        {
            var found = FindEarliestWindow(requests, earliestStart);
            if (!found.Success)
            {
                return CommitResult.Fail(found.Conflict);
            }

            var specs = new List<ExactReservationSpec>(requests.Count);
            for (var i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                var window = found.Windows[i];
                specs.Add(new ExactReservationSpec
                {
                    ResourceId = req.ResourceId,
                    Start = window.Start,
                    End = window.End,
                    Units = req.Units,
                    Direction = req.Direction,
                    JobId = jobId,
                    RouteOccurrenceIndex = routeOccurrenceIndex,
                });
            }

            return TryCommitExact(specs);
        }

        /// <summary>按 ReservationId 释放；未知 ID 返回 false。</summary>
        public bool Release(long reservationId)
        {
            if (!_byId.TryGetValue(reservationId, out var record))
            {
                return false;
            }

            RemoveRecord(record);
            return true;
        }

        /// <summary>批量按 ID 释放；返回实际释放数量。</summary>
        public int ReleaseAll(IEnumerable<long> reservationIds)
        {
            if (reservationIds == null)
            {
                return 0;
            }

            var n = 0;
            foreach (var id in reservationIds)
            {
                if (Release(id))
                {
                    n++;
                }
            }

            return n;
        }

        public int ReleaseAll(IReadOnlyList<ReservationHandle> handles)
        {
            if (handles == null)
            {
                return 0;
            }

            var n = 0;
            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i] != null && Release(handles[i].ReservationId))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// 缩短已提交预约的结束时刻（只缩不延，永不引入新冲突）。newEnd ≤ start 时等价于撤销。
        /// 用于“尾部清空时间确定后释放占位”。
        /// </summary>
        public bool TryTruncate(long reservationId, double newEnd)
        {
            if (!_byId.TryGetValue(reservationId, out var record)
                || double.IsNaN(newEnd)
                || newEnd > record.End + Eps)
            {
                return false;
            }

            if (newEnd <= record.Start + Eps)
            {
                RemoveRecord(record);
                return true;
            }

            record.End = newEnd;
            return true;
        }

        /// <summary>某资源上的全部已提交预约快照（按开始时刻排序；只读副本）。</summary>
        public List<ReservationHandle> GetReservations(string resourceId)
        {
            var result = new List<ReservationHandle>();
            if (_byResource.TryGetValue(resourceId ?? string.Empty, out var list))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    result.Add(ToHandle(list[i]));
                }
            }

            return result;
        }

        /// <summary>查询某资源在时刻 t 的占用单位数（半开：End==t 不算占用）。</summary>
        public int GetOccupancyAt(string resourceId, double time)
        {
            if (!_byResource.TryGetValue(resourceId ?? string.Empty, out var list))
            {
                return 0;
            }

            var sum = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r.Start <= time + Eps && time + Eps < r.End)
                {
                    sum += r.Units;
                }
            }

            return sum;
        }

        /// <summary>不变量检查：任意资源任意采样时刻不超过容量（供测试）。</summary>
        public bool ValidateCapacityInvariant(out string error)
        {
            error = null;
            foreach (var kv in _byResource)
            {
                if (!_resources.TryGetValue(kv.Key, out var def))
                {
                    continue;
                }

                var list = kv.Value;
                if (list.Count == 0)
                {
                    continue;
                }

                var points = new List<double>(list.Count * 2);
                for (var i = 0; i < list.Count; i++)
                {
                    points.Add(list[i].Start);
                    points.Add(list[i].End);
                }

                points.Sort();
                for (var i = 0; i < points.Count; i++)
                {
                    var t = points[i];
                    if (def.Kind == ResourceKind.DirectionLock)
                    {
                        var fwd = CountDirectionAt(list, t, TravelDirection.Forward);
                        var rev = CountDirectionAt(list, t, TravelDirection.Reverse);
                        if (fwd > 0 && rev > 0)
                        {
                            error = $"DirectionLock {kv.Key} 在 t={t} 出现对向共存。";
                            return false;
                        }

                        if (fwd > def.Capacity || rev > def.Capacity)
                        {
                            error = $"DirectionLock {kv.Key} 在 t={t} 同向超容 {Math.Max(fwd, rev)}/{def.Capacity}。";
                            return false;
                        }
                    }
                    else if (GetOccupancyAt(kv.Key, t) > def.Capacity)
                    {
                        error = $"资源 {kv.Key} 在 t={t} 占用 {GetOccupancyAt(kv.Key, t)} > 容量 {def.Capacity}。";
                        return false;
                    }

                    if (i + 1 < points.Count)
                    {
                        var mid = (points[i] + points[i + 1]) * 0.5;
                        if (def.Kind == ResourceKind.DirectionLock)
                        {
                            var fwd = CountDirectionAt(list, mid, TravelDirection.Forward);
                            var rev = CountDirectionAt(list, mid, TravelDirection.Reverse);
                            if (fwd > 0 && rev > 0)
                            {
                                error = $"DirectionLock {kv.Key} 在 t={mid} 出现对向共存。";
                                return false;
                            }

                            if (fwd > def.Capacity || rev > def.Capacity)
                            {
                                error = $"DirectionLock {kv.Key} 在 t={mid} 同向超容。";
                                return false;
                            }
                        }
                        else if (GetOccupancyAt(kv.Key, mid) > def.Capacity)
                        {
                            error = $"资源 {kv.Key} 在 t={mid} 占用超容。";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private ResourceConflictInfo ValidateSpec(ExactReservationSpec spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.ResourceId))
            {
                return new ResourceConflictInfo
                {
                    Code = ResourceCalendarErrorCode.NullRequest,
                    Message = "预约规格为空。",
                };
            }

            var id = spec.ResourceId.Trim();
            if (!_resources.TryGetValue(id, out var def))
            {
                return new ResourceConflictInfo
                {
                    Code = ResourceCalendarErrorCode.UnknownResource,
                    ResourceId = id,
                    Message = $"未知资源：{id}",
                };
            }

            var window = new TimeWindow(spec.Start, spec.End);
            if (!window.IsValid)
            {
                return new ResourceConflictInfo
                {
                    Code = ResourceCalendarErrorCode.InvalidWindow,
                    ResourceId = id,
                    Message = $"非法时窗 {window}（要求 end > start）。",
                };
            }

            var units = Math.Max(1, spec.Units);
            if (!CanPlace(id, def, window.Start, window.End, units, spec.Direction, ignoreId: -1, out var retryAt, out var code))
            {
                return new ResourceConflictInfo
                {
                    Code = code,
                    ResourceId = id,
                    Message = $"无法在 {window} 放置资源 {id}。",
                    SuggestedRetryAt = retryAt,
                };
            }

            // 冲突组
            if (!string.IsNullOrEmpty(def.ConflictGroupId))
            {
                var groupId = ResourceIds.MovementConflictGroup(def.ConflictGroupId);
                if (_resources.TryGetValue(groupId, out var groupDef)
                    && !CanPlace(groupId, groupDef, window.Start, window.End, 1, TravelDirection.None, -1, out retryAt, out code))
                {
                    return new ResourceConflictInfo
                    {
                        Code = ResourceCalendarErrorCode.ConflictGroupBusy,
                        ResourceId = groupId,
                        Message = $"冲突组 {def.ConflictGroupId} 在 {window} 忙碌。",
                        SuggestedRetryAt = retryAt,
                    };
                }
            }

            return null;
        }

        private bool CanCommitBatchTogether(
            IReadOnlyList<ExactReservationSpec> specs,
            out ResourceConflictInfo conflict)
        {
            conflict = null;
            // 模拟写入到临时层
            var temp = new Dictionary<string, List<ReservationRecord>>(StringComparer.Ordinal);
            foreach (var kv in _byResource)
            {
                temp[kv.Key] = new List<ReservationRecord>(kv.Value);
            }

            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var id = spec.ResourceId.Trim();
                var def = _resources[id];
                var units = Math.Max(1, spec.Units);
                if (!CanPlaceOnList(temp[id], def, spec.Start, spec.End, units, spec.Direction, out var retry, out var code))
                {
                    conflict = new ResourceConflictInfo
                    {
                        Code = code,
                        ResourceId = id,
                        Message = $"批次内预检失败：{id} @ [{spec.Start},{spec.End})",
                        SuggestedRetryAt = retry,
                    };
                    return false;
                }

                temp[id].Add(new ReservationRecord
                {
                    Id = -1 - i,
                    ResourceId = id,
                    Start = spec.Start,
                    End = spec.End,
                    Units = units,
                    Direction = spec.Direction,
                });
                SortByStart(temp[id]);

                if (!string.IsNullOrEmpty(def.ConflictGroupId))
                {
                    var groupId = ResourceIds.MovementConflictGroup(def.ConflictGroupId);
                    var groupDef = _resources[groupId];
                    if (!temp.TryGetValue(groupId, out var groupList))
                    {
                        groupList = new List<ReservationRecord>();
                        temp[groupId] = groupList;
                    }

                    if (!CanPlaceOnList(groupList, groupDef, spec.Start, spec.End, 1, TravelDirection.None, out retry, out code))
                    {
                        conflict = new ResourceConflictInfo
                        {
                            Code = ResourceCalendarErrorCode.ConflictGroupBusy,
                            ResourceId = groupId,
                            Message = $"批次内冲突组忙碌：{def.ConflictGroupId}",
                            SuggestedRetryAt = retry,
                        };
                        return false;
                    }

                    groupList.Add(new ReservationRecord
                    {
                        Id = -1000 - i,
                        ResourceId = groupId,
                        Start = spec.Start,
                        End = spec.End,
                        Units = 1,
                    });
                    SortByStart(groupList);
                }
            }

            return true;
        }

        private bool TryEvaluateAnchor(
            IReadOnlyList<ResourceWindowRequest> requests,
            double anchor,
            out List<TimeWindow> windows,
            out ResourceConflictInfo conflict,
            out double nextAnchor)
        {
            windows = new List<TimeWindow>(requests.Count);
            conflict = null;
            nextAnchor = double.PositiveInfinity;

            for (var i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                var start = anchor + req.OffsetFromAnchor;
                var end = start + req.Duration;
                if (!_resources.TryGetValue(req.ResourceId, out var def))
                {
                    conflict = new ResourceConflictInfo
                    {
                        Code = ResourceCalendarErrorCode.UnknownResource,
                        ResourceId = req.ResourceId,
                        Message = $"未知资源：{req.ResourceId}",
                    };
                    return false;
                }

                if (!CanPlace(req.ResourceId, def, start, end, req.Units, req.Direction, -1, out var retryAt, out var code))
                {
                    conflict = new ResourceConflictInfo
                    {
                        Code = code,
                        ResourceId = req.ResourceId,
                        Message = $"锚点 {anchor} 处资源 {req.ResourceId} 不可用。",
                        SuggestedRetryAt = retryAt,
                    };
                    if (retryAt < nextAnchor)
                    {
                        nextAnchor = retryAt;
                    }

                    // 继续扫描其它请求以取最小 next；但本锚点已失败
                    for (var j = i + 1; j < requests.Count; j++)
                    {
                        var r2 = requests[j];
                        var s2 = anchor + r2.OffsetFromAnchor;
                        var e2 = s2 + r2.Duration;
                        if (r2.Duration <= Eps || !_resources.TryGetValue(r2.ResourceId, out var d2))
                        {
                            continue;
                        }

                        if (!CanPlace(r2.ResourceId, d2, s2, e2, r2.Units, r2.Direction, -1, out var retry2, out _))
                        {
                            if (retry2 < nextAnchor)
                            {
                                nextAnchor = retry2;
                            }
                        }
                    }

                    return false;
                }

                if (!string.IsNullOrEmpty(def.ConflictGroupId))
                {
                    var groupId = ResourceIds.MovementConflictGroup(def.ConflictGroupId);
                    if (_resources.TryGetValue(groupId, out var groupDef)
                        && !CanPlace(groupId, groupDef, start, end, 1, TravelDirection.None, -1, out var gretry, out _))
                    {
                        conflict = new ResourceConflictInfo
                        {
                            Code = ResourceCalendarErrorCode.ConflictGroupBusy,
                            ResourceId = groupId,
                            SuggestedRetryAt = gretry,
                            Message = $"冲突组忙碌：{def.ConflictGroupId}",
                        };
                        nextAnchor = gretry;
                        return false;
                    }
                }

                windows.Add(new TimeWindow(start, end));
            }

            return true;
        }

        private bool CanPlace(
            string resourceId,
            ResourceDefinition def,
            double start,
            double end,
            int units,
            TravelDirection direction,
            long ignoreId,
            out double retryAt,
            out ResourceCalendarErrorCode code)
        {
            var list = _byResource[resourceId];
            return CanPlaceOnListFiltered(list, def, start, end, units, direction, ignoreId, out retryAt, out code);
        }

        private static bool CanPlaceOnList(
            List<ReservationRecord> list,
            ResourceDefinition def,
            double start,
            double end,
            int units,
            TravelDirection direction,
            out double retryAt,
            out ResourceCalendarErrorCode code) =>
            CanPlaceOnListFiltered(list, def, start, end, units, direction, ignoreId: long.MinValue, out retryAt, out code);

        private static bool CanPlaceOnListFiltered(
            List<ReservationRecord> list,
            ResourceDefinition def,
            double start,
            double end,
            int units,
            TravelDirection direction,
            long ignoreId,
            out double retryAt,
            out ResourceCalendarErrorCode code)
        {
            retryAt = end;
            code = ResourceCalendarErrorCode.Ok;

            if (def.Kind == ResourceKind.DirectionLock)
            {
                return CanPlaceDirectionLock(list, def, start, end, units, direction, ignoreId, out retryAt, out code);
            }

            // 容量资源：在 [start,end) 内任意时刻 occupancy+units <= capacity
            var points = new List<double> { start };
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (ignoreId != long.MinValue && r.Id == ignoreId)
                {
                    continue;
                }

                // 半开：r.End == start 不重叠
                if (r.End <= start + Eps || r.Start >= end - Eps)
                {
                    continue;
                }

                points.Add(r.Start);
                points.Add(r.End);
            }

            points.Sort();
            for (var i = 0; i < points.Count; i++)
            {
                var t = points[i];
                if (t < start - Eps || t >= end - Eps)
                {
                    continue;
                }

                var occ = 0;
                for (var j = 0; j < list.Count; j++)
                {
                    var r = list[j];
                    if (ignoreId != long.MinValue && r.Id == ignoreId)
                    {
                        continue;
                    }

                    if (r.Start <= t + Eps && t + Eps < r.End)
                    {
                        occ += r.Units;
                    }
                }

                if (occ + units > def.Capacity)
                {
                    code = ResourceCalendarErrorCode.CapacityExceeded;
                    // 推进到当前挡住的某条预约的 End
                    retryAt = end;
                    for (var j = 0; j < list.Count; j++)
                    {
                        var r = list[j];
                        if (ignoreId != long.MinValue && r.Id == ignoreId)
                        {
                            continue;
                        }

                        if (r.Start <= t + Eps && t + Eps < r.End && r.End > start + Eps)
                        {
                            if (r.End < retryAt)
                            {
                                retryAt = r.End;
                            }
                        }
                    }

                    return false;
                }
            }

            return true;
        }

        private static bool CanPlaceDirectionLock(
            List<ReservationRecord> list,
            ResourceDefinition def,
            double start,
            double end,
            int units,
            TravelDirection direction,
            long ignoreId,
            out double retryAt,
            out ResourceCalendarErrorCode code)
        {
            retryAt = end;
            code = ResourceCalendarErrorCode.Ok;
            if (direction == TravelDirection.None)
            {
                direction = TravelDirection.Forward;
            }

            var opposite = direction == TravelDirection.Forward
                ? TravelDirection.Reverse
                : TravelDirection.Forward;

            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (ignoreId != long.MinValue && r.Id == ignoreId)
                {
                    continue;
                }

                if (r.End <= start + Eps || r.Start >= end - Eps)
                {
                    continue;
                }

                var rDir = r.Direction == TravelDirection.None ? TravelDirection.Forward : r.Direction;
                if (rDir == opposite)
                {
                    code = ResourceCalendarErrorCode.DirectionConflict;
                    if (r.End < retryAt)
                    {
                        retryAt = r.End;
                    }

                    return false;
                }
            }

            // 同向容量
            var points = new List<double> { start };
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (ignoreId != long.MinValue && r.Id == ignoreId)
                {
                    continue;
                }

                var rDir = r.Direction == TravelDirection.None ? TravelDirection.Forward : r.Direction;
                if (rDir != direction)
                {
                    continue;
                }

                if (r.End <= start + Eps || r.Start >= end - Eps)
                {
                    continue;
                }

                points.Add(r.Start);
                points.Add(r.End);
            }

            points.Sort();
            for (var i = 0; i < points.Count; i++)
            {
                var t = points[i];
                if (t < start - Eps || t >= end - Eps)
                {
                    continue;
                }

                var occ = 0;
                for (var j = 0; j < list.Count; j++)
                {
                    var r = list[j];
                    if (ignoreId != long.MinValue && r.Id == ignoreId)
                    {
                        continue;
                    }

                    var rDir = r.Direction == TravelDirection.None ? TravelDirection.Forward : r.Direction;
                    if (rDir == direction && r.Start <= t + Eps && t + Eps < r.End)
                    {
                        occ += r.Units;
                    }
                }

                if (occ + units > def.Capacity)
                {
                    code = ResourceCalendarErrorCode.CapacityExceeded;
                    retryAt = end;
                    for (var j = 0; j < list.Count; j++)
                    {
                        var r = list[j];
                        if (ignoreId != long.MinValue && r.Id == ignoreId)
                        {
                            continue;
                        }

                        var rDir = r.Direction == TravelDirection.None ? TravelDirection.Forward : r.Direction;
                        if (rDir == direction && r.Start <= t + Eps && t + Eps < r.End && r.End < retryAt)
                        {
                            retryAt = r.End;
                        }
                    }

                    return false;
                }
            }

            return true;
        }

        private static int CountDirectionAt(List<ReservationRecord> list, double time, TravelDirection direction)
        {
            var sum = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                var rDir = r.Direction == TravelDirection.None ? TravelDirection.Forward : r.Direction;
                if (rDir == direction && r.Start <= time + Eps && time + Eps < r.End)
                {
                    sum += r.Units;
                }
            }

            return sum;
        }

        private void InsertRecord(ReservationRecord record)
        {
            if (!_byResource.TryGetValue(record.ResourceId, out var list))
            {
                list = new List<ReservationRecord>();
                _byResource[record.ResourceId] = list;
            }

            list.Add(record);
            SortByStart(list);
            _byId[record.Id] = record;
        }

        private void RemoveRecord(ReservationRecord record)
        {
            _byId.Remove(record.Id);
            if (_byResource.TryGetValue(record.ResourceId, out var list))
            {
                list.Remove(record);
            }
        }

        private static void SortByStart(List<ReservationRecord> list) =>
            list.Sort((a, b) =>
            {
                var c = a.Start.CompareTo(b.Start);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });

        private static ReservationHandle ToHandle(ReservationRecord r) =>
            new ReservationHandle
            {
                ReservationId = r.Id,
                ResourceId = r.ResourceId,
                Start = r.Start,
                End = r.End,
                Units = r.Units,
                Direction = r.Direction,
                JobId = r.JobId,
                RouteOccurrenceIndex = r.RouteOccurrenceIndex,
            };
    }
}
