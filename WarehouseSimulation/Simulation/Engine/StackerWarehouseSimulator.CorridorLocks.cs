using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 通道锁：对向与路口直线互斥，代替原来在途改预约窗、互相顶开的做法。
    /// <list type="bullet">
    /// <item>方向锁：每段路一把，同一方向可多箱同时持有。出发前一次性锁住整条路径，
    /// 拿不到就停在起点（入库口/交互点）等，手里不持有任何锁，所以对向车不会在路上顶牛卡死。</item>
    /// <item>直线锁：路口所在的一条直线一把，同一条来向路段可多箱同时持有。
    /// 到该路段末端停留点之前才申请（短路段提前到上一个路口之前），在上一个停留点等。
    /// 持锁车只需通过路口驶离，不依赖等锁的车。</item>
    /// </list>
    /// <para>每段路的锁在车尾离开该段后单独释放，口径与 <see cref="SimSegmentCoOccupancyChecker"/> 一致。</para>
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        private const double CorridorTimeEpsilon = 1e-6;

        /// <summary>
        /// 先来的等锁任务被同向车流插队的最长时间。期间与当前持锁方向一致的后来者可以先走，
        /// 让同向货箱成批通过，避免对向逐箱交替；超过后后来者必须排在它后面，保证不会饿死。
        /// </summary>
        private const double CorridorBypassSeconds = 1800d;

        private sealed class CorridorClaims
        {
            public List<ConveyorPathZone> Chain;
            public string[] Keys;
            public string[] Sides;
            public int[] EdgeOfClaim;
            /// <summary>预约到该 zone 之前必须拿到这把锁。</summary>
            public int[] AcquireZoneOfClaim;
            public bool[] ClaimHeld;
            /// <summary>每段路释放所看的 zone：车头到达它时车尾已离开该段终点；-1 表示车尾要等货箱离开输送线。</summary>
            public int[] ReleaseZoneByEdge;
            public bool[] EdgeReleased;
            public readonly List<int> Requested = new();
            public readonly List<int> JustAcquired = new();
            public bool BlockedOnLock;
            public bool LeftConveyor;
            public double CheckScheduledAt = -1;
            public double WaitSince;
        }

        /// <summary>锁键 → 持有任务 → 持有方向。</summary>
        private readonly Dictionary<string, Dictionary<int, string>> _corridorHolders = new();
        private readonly Dictionary<int, CorridorClaims> _corridorClaimsByJob = new();
        /// <summary>按登记先后排队的等锁任务；后来者不能越过与之冲突、且已等够久的先来者。</summary>
        private readonly List<int> _corridorWaiters = new();
        private readonly HashSet<string> _corridorReleasedKeysScratch = new();
        private readonly List<int> _corridorPendingScratch = new();

        private void ResetCorridorLocks()
        {
            _corridorHolders.Clear();
            _corridorClaimsByJob.Clear();
            _corridorWaiters.Clear();
            _corridorReleasedKeysScratch.Clear();
            _corridorPendingScratch.Clear();
        }

        private bool IsCorridorLockBlocked(WarehouseJob job) =>
            job != null
            && _corridorClaimsByJob.TryGetValue(job.JobId, out var claims)
            && claims.BlockedOnLock;

        /// <summary>
        /// 预约 <paramref name="zoneIndex"/> 之前申请该处要拿的锁。拿不到时任务登记为等锁，由释放时唤醒。
        /// </summary>
        private bool TryAcquireCorridorAt(WarehouseJob job, int zoneIndex)
        {
            var claims = GetOrBuildCorridorClaims(job);
            claims?.JustAcquired.Clear();
            if (claims == null)
            {
                return true;
            }

            var pending = _corridorPendingScratch;
            pending.Clear();
            for (var i = 0; i < claims.Keys.Length; i++)
            {
                if (claims.AcquireZoneOfClaim[i] == zoneIndex
                    && !claims.ClaimHeld[i]
                    && !claims.EdgeReleased[claims.EdgeOfClaim[i]])
                {
                    pending.Add(i);
                }
            }

            if (pending.Count == 0)
            {
                return true;
            }

            if (!CanAcquireCorridor(job.JobId, claims, pending))
            {
                claims.BlockedOnLock = true;
                claims.Requested.Clear();
                claims.Requested.AddRange(pending);
                if (!_corridorWaiters.Contains(job.JobId))
                {
                    _corridorWaiters.Add(job.JobId);
                    claims.WaitSince = _clock.Now;
                }

                return false;
            }

            for (var p = 0; p < pending.Count; p++)
            {
                var i = pending[p];
                if (!_corridorHolders.TryGetValue(claims.Keys[i], out var holders))
                {
                    holders = new Dictionary<int, string>();
                    _corridorHolders[claims.Keys[i]] = holders;
                }

                holders[job.JobId] = claims.Sides[i];
                claims.ClaimHeld[i] = true;
                claims.JustAcquired.Add(i);
            }

            return true;
        }

        /// <summary>拿到锁但 zone 没订上：原样退回，不唤醒别人（锁表回到申请前）。</summary>
        private void RollbackCorridorAcquisition(WarehouseJob job)
        {
            if (!_corridorClaimsByJob.TryGetValue(job.JobId, out var claims))
            {
                return;
            }

            for (var p = 0; p < claims.JustAcquired.Count; p++)
            {
                var i = claims.JustAcquired[p];
                RemoveCorridorHolder(claims.Keys[i], job.JobId);
                claims.ClaimHeld[i] = false;
            }

            claims.JustAcquired.Clear();
        }

        /// <summary>zone 已订上：退出等锁队列。</summary>
        private void CommitCorridorStep(WarehouseJob job)
        {
            if (!_corridorClaimsByJob.TryGetValue(job.JobId, out var claims))
            {
                return;
            }

            claims.JustAcquired.Clear();
            if (claims.BlockedOnLock || claims.Requested.Count > 0)
            {
                claims.BlockedOnLock = false;
                claims.Requested.Clear();
                _corridorWaiters.Remove(job.JobId);
            }
        }

        private bool CanAcquireCorridor(int jobId, CorridorClaims claims, List<int> pending)
        {
            for (var p = 0; p < pending.Count; p++)
            {
                var i = pending[p];
                if (!_corridorHolders.TryGetValue(claims.Keys[i], out var holders))
                {
                    continue;
                }

                foreach (var pair in holders)
                {
                    if (pair.Key != jobId && !string.Equals(pair.Value, claims.Sides[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            for (var w = 0; w < _corridorWaiters.Count; w++)
            {
                var waiterId = _corridorWaiters[w];
                if (waiterId == jobId)
                {
                    break;
                }

                if (_corridorClaimsByJob.TryGetValue(waiterId, out var waiter)
                    && waiter.BlockedOnLock
                    && _clock.Now - waiter.WaitSince >= CorridorBypassSeconds
                    && RequestsConflict(claims, pending, waiter, waiter.Requested))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RequestsConflict(CorridorClaims a, List<int> aClaims, CorridorClaims b, List<int> bClaims)
        {
            for (var x = 0; x < aClaims.Count; x++)
            {
                var i = aClaims[x];
                for (var y = 0; y < bClaims.Count; y++)
                {
                    var j = bClaims[y];
                    if (string.Equals(a.Keys[i], b.Keys[j], StringComparison.Ordinal)
                        && !string.Equals(a.Sides[i], b.Sides[j], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private CorridorClaims GetOrBuildCorridorClaims(WarehouseJob job)
        {
            var chain = job.ConveyorPathZones;
            var path = job.ConveyorPathNodeIndices;
            if (chain == null || chain.Count == 0 || path == null || path.Count < 2)
            {
                return null;
            }

            if (_corridorClaimsByJob.TryGetValue(job.JobId, out var cached)
                && ReferenceEquals(cached.Chain, chain))
            {
                return cached;
            }

            if (cached != null)
            {
                ReleaseAllCorridorLocks(job);
            }

            var map = _bindings.ConveyorMap;
            var edgeCount = path.Count - 1;
            var edgeStart = new double[edgeCount + 1];
            for (var e = 0; e < edgeCount; e++)
            {
                var length = _conveyorTopology.TryGetEdge(path[e], path[e + 1], out var edge)
                    ? Math.Max(0f, edge.DistanceMeters)
                    : 0f;
                edgeStart[e + 1] = edgeStart[e] + length;
            }

            var zonePosition = new double[chain.Count];
            for (var k = 0; k < chain.Count; k++)
            {
                var zone = chain[k];
                var e = Math.Max(0, Math.Min(edgeCount - 1, zone.PathEdgeIndex));
                if (zone.Kind == ConveyorPathZoneKind.EdgeSlot
                    && _conveyorTopology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var edge))
                {
                    var capacity = _conveyorTopology.Map.GetEdgeCapacity(edge);
                    var normalized = ConveyorMapMath.GetZoneNormalizedPosition(map, edge, capacity, zone.SlotIndex);
                    zonePosition[k] = edgeStart[e] + normalized * (edgeStart[e + 1] - edgeStart[e]);
                }
                else
                {
                    zonePosition[k] = edgeStart[e + 1];
                }
            }

            var cargo = ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            var releaseZone = new int[edgeCount];
            for (var e = 0; e < edgeCount; e++)
            {
                releaseZone[e] = -1;
                var clearAt = edgeStart[e + 1] + cargo - 1e-3;
                for (var k = 0; k < chain.Count; k++)
                {
                    if (zonePosition[k] >= clearAt)
                    {
                        releaseZone[e] = k;
                        break;
                    }
                }
            }

            var keys = new List<string>(edgeCount * 2);
            var sides = new List<string>(edgeCount * 2);
            var edgeOfClaim = new List<int>(edgeCount * 2);
            var acquireZone = new List<int>(edgeCount * 2);
            for (var e = 0; e < edgeCount; e++)
            {
                var from = path[e];
                var to = path[e + 1];
                var segment = SimEntityNaming.FormatSegment(map, from, to);
                keys.Add("dir|" + segment);
                sides.Add(from < to ? "f" : "r");
                edgeOfClaim.Add(e);
                acquireZone.Add(0);

                if (ConveyorJunctionAxis.TryBuildResourceId(map, from, to, out var axisId))
                {
                    keys.Add("axis|" + axisId);
                    sides.Add(segment);
                    edgeOfClaim.Add(e);
                    acquireZone.Add(ResolveAxisAcquireZone(chain, e));
                }
            }

            var claims = new CorridorClaims
            {
                Chain = chain,
                Keys = keys.ToArray(),
                Sides = sides.ToArray(),
                EdgeOfClaim = edgeOfClaim.ToArray(),
                AcquireZoneOfClaim = acquireZone.ToArray(),
                ClaimHeld = new bool[keys.Count],
                ReleaseZoneByEdge = releaseZone,
                EdgeReleased = new bool[edgeCount],
            };
            _corridorClaimsByJob[job.JobId] = claims;
            return claims;
        }

        /// <summary>
        /// 直线锁的申请点：本段末端停留点（slot 0）之前；本段只有一个停留点且紧跟在路口之后时，提前到驶入该路口之前，
        /// 免得车停在路口中心等锁把路口堵住。
        /// </summary>
        private static int ResolveAxisAcquireZone(IReadOnlyList<ConveyorPathZone> chain, int pathEdgeIndex)
        {
            for (var k = 0; k < chain.Count; k++)
            {
                var zone = chain[k];
                if (zone.Kind != ConveyorPathZoneKind.EdgeSlot
                    || zone.PathEdgeIndex != pathEdgeIndex
                    || zone.SlotIndex != 0)
                {
                    continue;
                }

                var isEntry = k == 0
                              || chain[k - 1].Kind != ConveyorPathZoneKind.EdgeSlot
                              || chain[k - 1].PathEdgeIndex != pathEdgeIndex;
                if (isEntry && k > 0 && chain[k - 1].Kind == ConveyorPathZoneKind.Junction)
                {
                    return k - 1;
                }

                return k;
            }

            return 0;
        }

        /// <summary>按当前已订 zone 与路段时刻表，释放车尾已离开的路段；还没到的排一次复查。</summary>
        private void EvaluateCorridorRelease(WarehouseJob job)
        {
            if (job == null
                || !_corridorClaimsByJob.TryGetValue(job.JobId, out var claims)
                || !ReferenceEquals(job.ConveyorPathZones, claims.Chain))
            {
                return;
            }

            var chain = claims.Chain;
            var path = job.ConveyorPathNodeIndices;
            var now = _clock.Now;
            var committed = job.NextConveyorZoneIndex;
            var nextCheck = double.MaxValue;
            _corridorReleasedKeysScratch.Clear();
            for (var e = 0; e < claims.EdgeReleased.Length; e++)
            {
                if (claims.EdgeReleased[e] || !EdgeHasHeldClaim(claims, e))
                {
                    continue;
                }

                double releaseAt;
                var k = claims.ReleaseZoneByEdge[e];
                if (k >= 0 && k < committed && k < chain.Count)
                {
                    releaseAt = chain[k].ArriveSimTime;
                }
                else if (claims.LeftConveyor)
                {
                    releaseAt = now;
                }
                else
                {
                    continue;
                }

                if (!TryGetScheduledBodyEnd(job, e, path[e], path[e + 1], out var bodyEnd))
                {
                    continue;
                }

                if (bodyEnd > releaseAt)
                {
                    releaseAt = bodyEnd;
                }

                if (releaseAt <= now + CorridorTimeEpsilon)
                {
                    ReleaseCorridorEdge(job.JobId, claims, e);
                }
                else if (releaseAt < nextCheck)
                {
                    nextCheck = releaseAt;
                }
            }

            if (nextCheck < double.MaxValue
                && !(claims.CheckScheduledAt > now - CorridorTimeEpsilon
                     && claims.CheckScheduledAt <= nextCheck + CorridorTimeEpsilon))
            {
                claims.CheckScheduledAt = nextCheck;
                _queue.Enqueue(new ScheduledSimEvent(nextCheck, SimEventType.CorridorLockCheck, job.JobId));
            }

            WakeCorridorWaiters(_corridorReleasedKeysScratch);
        }

        private static bool EdgeHasHeldClaim(CorridorClaims claims, int edgeIndex)
        {
            for (var i = 0; i < claims.Keys.Length; i++)
            {
                if (claims.EdgeOfClaim[i] == edgeIndex && claims.ClaimHeld[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void OnCorridorLockCheck(WarehouseJob job)
        {
            if (_corridorClaimsByJob.TryGetValue(job.JobId, out var claims)
                && Math.Abs(claims.CheckScheduledAt - _clock.Now) <= CorridorTimeEpsilon)
            {
                claims.CheckScheduledAt = -1;
            }

            EvaluateCorridorRelease(job);
        }

        /// <summary>货箱离开输送线（入库被堆垛机取走、出库完成）：剩下的路段等车尾按时刻表离开后释放。</summary>
        private void MarkCorridorLeftConveyor(WarehouseJob job)
        {
            if (job == null || !_corridorClaimsByJob.TryGetValue(job.JobId, out var claims))
            {
                return;
            }

            claims.LeftConveyor = true;
            EvaluateCorridorRelease(job);
        }

        /// <summary>任务失败或换路：立即放掉全部锁并退出等锁队列。</summary>
        private void ReleaseAllCorridorLocks(WarehouseJob job)
        {
            if (job == null || !_corridorClaimsByJob.TryGetValue(job.JobId, out var claims))
            {
                return;
            }

            var wasWaiting = _corridorWaiters.Remove(job.JobId);
            _corridorReleasedKeysScratch.Clear();
            for (var i = 0; i < claims.Keys.Length; i++)
            {
                if (claims.ClaimHeld[i] && RemoveCorridorHolder(claims.Keys[i], job.JobId))
                {
                    _corridorReleasedKeysScratch.Add(claims.Keys[i]);
                }

                claims.ClaimHeld[i] = false;
            }

            _corridorClaimsByJob.Remove(job.JobId);
            WakeCorridorWaiters(wasWaiting ? null : _corridorReleasedKeysScratch);
        }

        private void ReleaseCorridorEdge(int jobId, CorridorClaims claims, int edgeIndex)
        {
            claims.EdgeReleased[edgeIndex] = true;
            for (var i = 0; i < claims.Keys.Length; i++)
            {
                if (claims.EdgeOfClaim[i] != edgeIndex || !claims.ClaimHeld[i])
                {
                    continue;
                }

                claims.ClaimHeld[i] = false;
                if (RemoveCorridorHolder(claims.Keys[i], jobId))
                {
                    _corridorReleasedKeysScratch.Add(claims.Keys[i]);
                }
            }
        }

        private bool RemoveCorridorHolder(string key, int jobId)
        {
            if (!_corridorHolders.TryGetValue(key, out var holders) || !holders.Remove(jobId))
            {
                return false;
            }

            if (holders.Count == 0)
            {
                _corridorHolders.Remove(key);
            }

            return true;
        }

        /// <summary>唤醒请求的锁键与 <paramref name="releasedKeys"/> 有交集的等锁任务；为 null 时全部唤醒。</summary>
        private void WakeCorridorWaiters(HashSet<string> releasedKeys)
        {
            if (_corridorWaiters.Count == 0 || (releasedKeys != null && releasedKeys.Count == 0))
            {
                releasedKeys?.Clear();
                return;
            }

            var delay = _bindings != null ? _bindings.OccupancyNotifyDelaySeconds : 0.1f;
            for (var w = 0; w < _corridorWaiters.Count; w++)
            {
                var waiterId = _corridorWaiters[w];
                if (!_jobs.TryGetValue(waiterId, out var waiter)
                    || !_corridorClaimsByJob.TryGetValue(waiterId, out var claims))
                {
                    continue;
                }

                if (releasedKeys != null && !RequestTouchesAny(claims, releasedKeys))
                {
                    continue;
                }

                ScheduleConveyorRouteRetry(waiter, _clock.Now + delay);
            }

            releasedKeys?.Clear();
        }

        private static bool RequestTouchesAny(CorridorClaims claims, HashSet<string> keys)
        {
            for (var r = 0; r < claims.Requested.Count; r++)
            {
                if (keys.Contains(claims.Keys[claims.Requested[r]]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 自检口径的车尾离开时刻。下游路段时刻表可能还没写入，这时自检会少算节点停留后的尾距，
        /// 所以车头在节点停过时再按停留结束补一个尾距。
        /// </summary>
        private bool TryGetScheduledBodyEnd(
            WarehouseJob job,
            int pathEdgeIndex,
            int fromNode,
            int toNode,
            out double bodyEnd)
        {
            bodyEnd = 0d;
            var schedule = job.ConveyorSegmentSchedule;
            var index = ConveyorSegmentScheduleNavigation.FindPathEdgeIndex(schedule, pathEdgeIndex);
            if (index < 0)
            {
                index = ConveyorSegmentScheduleNavigation.FindEndpointIndex(schedule, fromNode, toNode);
            }
            if (index < 0
                || !SimSegmentCoOccupancyChecker.TryGetBodyEnd(
                    _bindings.ConveyorMap,
                    _conveyorTopology,
                    schedule,
                    index,
                    out bodyEnd))
            {
                return false;
            }

            if (_conveyorTopology.TryGetEdge(fromNode, toNode, out var edge))
            {
                var map = _bindings.ConveyorMap;
                var seg = schedule[index];
                var frontAtNode = seg.ExitSimTime + ConveyorMapMath.GetNodeApproachHopSeconds(map, edge);
                if (seg.OccupancyEndSimTime > frontAtNode + CorridorTimeEpsilon)
                {
                    var tailed = seg.OccupancyEndSimTime + ConveyorMapMath.GetCargoTailClearanceSeconds(map, edge);
                    if (tailed > bodyEnd)
                    {
                        bodyEnd = tailed;
                    }
                }
            }

            return true;
        }
    }
}
