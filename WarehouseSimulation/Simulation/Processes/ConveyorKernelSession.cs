using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Kernel.Zone;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 新输送内核适配层：入库任务的输送段（入库口驶离 → 取货点）交给
    /// <see cref="ConveyorZoneKernel"/> 在线调度，旧的 zone 逐步预约 / 回卷不再参与。
    /// 内核结果在货物到达取货点时一次性写回：zone 链时刻、路段时刻表（ConveyorSegmentSchedule）、
    /// 子任务与回放快照，然后照旧发 ConveyorTransitComplete，由旧代码继续堆垛机阶段。
    /// 入库口（放货/物理释放）与堆垛机仍由旧代码调度；交接时刻：入库口在货物到达首个停留点时释放，
    /// 取货点一直占用到堆垛机取货完成（ReleaseGoal）。
    /// 入库、出库输送的唯一调度路径（出入库共用一个内核与同一套位置/锁）。
    /// </content>
    internal sealed class ConveyorKernelSession
    {
        private readonly SimRunContext Ctx;

        public ConveyorKernelSession(SimRunContext ctx) => Ctx = ctx;

        internal bool IsActive => _primaryKernel != null;

        internal bool IsSubmitted(int jobId) => _primaryKernel != null && _primaryKernel.IsSubmitted(jobId);

        private ConveyorZoneKernel _primaryKernel;
        private double _primaryNextTick = double.PositiveInfinity;
        private int _primaryGoalCount;

        /// <summary>每次 Run 新建内核；入库一律由内核调度。</summary>
        internal void Init()
        {
            _primaryKernel = null;
            _primaryNextTick = double.PositiveInfinity;
            _primaryGoalCount = 0;
            if (Ctx.Topology == null || Ctx.Bindings?.ConveyorMap == null)
            {
                WarehouseSimLog.Error("[ConveyorKernel] 未配置 ConveyorMap，入库输送无法调度。");
                return;
            }

            _primaryKernel = new ConveyorZoneKernel();
            _primaryPickupWaiters.Clear();
            _primaryInfeedOriginBlocked = false;
            _primaryTrack = new ConveyorTrackGeometry(Ctx.Bindings.ConveyorMap, Ctx.Topology);
            _primaryZonePoints.Clear();
            _primaryTooCloseCache.Clear();
            _originNearByNode = null;
            _primaryKernel.ZonesTooClose = PrimaryZonesTooClose;
            BuildStraightCorridors();
        }

        /// <summary>无向边 → 直线走廊编号。只收录至少两段相连、同一朝向的双向边。</summary>
        private Dictionary<long, int> _straightCorridorId;

        private void BuildStraightCorridors()
        {
            _straightCorridorId = null;
            var map = Ctx.Bindings.ConveyorMap;
            var nodes = map?.Nodes;
            var mapEdges = map?.Edges;
            if (nodes == null || mapEdges == null)
            {
                return;
            }

            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < nodes.Length; i++)
            {
                var id = nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id) && !index.ContainsKey(id))
                {
                    index[id] = i;
                }
            }

            var directed = new HashSet<long>();
            foreach (var edge in mapEdges)
            {
                if (edge.FromNodeId == null || edge.ToNodeId == null
                    || !index.TryGetValue(edge.FromNodeId.Trim(), out var from)
                    || !index.TryGetValue(edge.ToNodeId.Trim(), out var to)
                    || from == to)
                {
                    continue;
                }

                directed.Add(((long)from << 32) | (uint)to);
            }

            var undirected = new List<(int A, int B, int Bucket)>();
            var seen = new HashSet<long>();
            foreach (var key in directed)
            {
                var a = (int)(key >> 32);
                var b = (int)(key & 0xffffffff);
                if (a > b)
                {
                    continue;
                }

                if (!directed.Contains(((long)b << 32) | (uint)a))
                {
                    continue;
                }

                var uk = EdgeKey(a, b);
                if (!seen.Add(uk))
                {
                    continue;
                }

                if (!ConveyorJunctionAxis.TryGetBucket(map, a, b, out var bucket))
                {
                    continue;
                }

                undirected.Add((a, b, bucket));
            }

            var count = undirected.Count;
            if (count == 0)
            {
                return;
            }

            var parent = new int[count];
            for (var i = 0; i < count; i++)
            {
                parent[i] = i;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            var incident = new Dictionary<int, List<(int Edge, int Bucket)>>();
            for (var i = 0; i < count; i++)
            {
                void Touch(int node)
                {
                    if (!incident.TryGetValue(node, out var list))
                    {
                        list = new List<(int, int)>();
                        incident[node] = list;
                    }

                    list.Add((i, undirected[i].Bucket));
                }

                Touch(undirected[i].A);
                Touch(undirected[i].B);
            }

            foreach (var list in incident.Values)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    for (var j = i + 1; j < list.Count; j++)
                    {
                        if (list[i].Bucket != list[j].Bucket)
                        {
                            continue;
                        }

                        var ra = Find(list[i].Edge);
                        var rb = Find(list[j].Edge);
                        if (ra != rb)
                        {
                            parent[rb] = ra;
                        }
                    }
                }
            }

            var size = new int[count];
            for (var i = 0; i < count; i++)
            {
                size[Find(i)]++;
            }

            _straightCorridorId = new Dictionary<long, int>();
            var ids = new Dictionary<int, int>();
            var nextId = 1;
            for (var i = 0; i < count; i++)
            {
                var root = Find(i);
                if (size[root] < 2)
                {
                    continue;
                }

                if (!ids.TryGetValue(root, out var id))
                {
                    id = nextId++;
                    ids[root] = id;
                }

                _straightCorridorId[EdgeKey(undirected[i].A, undirected[i].B)] = id;
            }
        }

        private static long EdgeKey(int a, int b) =>
            a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>沿直线走廊的行驶方向：对向符号相反。桶只表示这条线的朝向，不区分去回。</summary>
        private static int TravelSense(WarehouseConveyorMap map, int from, int to)
        {
            if (!ConveyorJunctionAxis.TryGetBucket(map, from, to, out var bucket))
            {
                return 0;
            }

            var a = map.Nodes[from].EditorPosition;
            var b = map.Nodes[to].EditorPosition;
            var center = bucket * (Math.PI / 4d);
            var proj = (b.x - a.x) * Math.Cos(center) + (b.y - a.y) * Math.Sin(center);
            return proj >= 0d ? 1 : 2;
        }

        // 车身间距：zone 中心位置（与回放 / 间距自检同一套几何），两两距离缓存。
        private ConveyorTrackGeometry _primaryTrack;
        private readonly Dictionary<string, ConveyorTrackPoint> _primaryZonePoints = new Dictionary<string, ConveyorTrackPoint>(StringComparer.Ordinal);
        private readonly Dictionary<(string, string), bool> _primaryTooCloseCache = new Dictionary<(string, string), bool>();
        private List<ZoneLockClaim>[] _originNearByNode;

        private void RegisterPrimaryZonePoint(string key, in ConveyorTrackPoint point)
        {
            if (!string.IsNullOrEmpty(key) && !_primaryZonePoints.ContainsKey(key))
            {
                _primaryZonePoints[key] = point;
            }
        }

        /// <summary>两个位置中心沿输送线的距离小于一个箱长：不能同时被两件货物占着。</summary>
        private bool PrimaryZonesTooClose(string a, string b)
        {
            var key = string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
            if (_primaryTooCloseCache.TryGetValue(key, out var close))
            {
                return close;
            }

            close = _primaryTrack != null
                    && _primaryZonePoints.TryGetValue(a, out var pa)
                    && _primaryZonePoints.TryGetValue(b, out var pb)
                    && _primaryTrack.Distance(pa, pb) < _primaryTrack.CargoLength - 1e-3;
            _primaryTooCloseCache[key] = close;
            return close;
        }

        /// <summary>出库：等交互点空出来才能开始堆垛机作业的任务。</summary>
        private readonly List<int> _primaryPickupWaiters = new List<int>();

        private bool IsPrimaryKernelJob(WarehouseJob job) => _primaryKernel != null && job != null;

        private static string NodeNearLockKey(int nodeIndex) => $"near:n{nodeIndex}";

        /// <summary>
        /// 起点（入库口 / 交互点）离哪些节点不足一个箱长，就在占起点时一并拿这些节点的邻域锁，
        /// 否则「我占着起点等邻域锁、你拿着邻域锁等离我太近的节点」会对顶。
        /// </summary>
        private List<ZoneLockClaim> OriginNearClaims(int originNodeIndex)
        {
            var nodes = Ctx.Bindings?.ConveyorMap?.Nodes;
            if (_primaryTrack == null || nodes == null || originNodeIndex < 0)
            {
                return new List<ZoneLockClaim>();
            }

            if (originNodeIndex < nodes.Length)
            {
                if (_originNearByNode == null || _originNearByNode.Length != nodes.Length)
                {
                    _originNearByNode = new List<ZoneLockClaim>[nodes.Length];
                }

                var cached = _originNearByNode[originNodeIndex];
                if (cached != null)
                {
                    return cached;
                }
            }

            var claims = new List<ZoneLockClaim>();
            var origin = ConveyorTrackGeometry.Node(originNodeIndex);
            var limit = _primaryTrack.CargoLength - 1e-3;
            for (var m = 0; m < nodes.Length; m++)
            {
                if (m == originNodeIndex || _primaryTrack.Distance(origin, ConveyorTrackGeometry.Node(m)) < limit)
                {
                    claims.Add(new ZoneLockClaim { Key = NodeNearLockKey(m), Capacity = 1, Direction = 0 });
                }
            }

            if (originNodeIndex < nodes.Length && _originNearByNode != null)
            {
                _originNearByNode[originNodeIndex] = claims;
            }

            return claims;
        }

        private string InfeedZoneKey(int infeedNodeIndex)
        {
            var key = $"infeed-origin:n{infeedNodeIndex}";
            RegisterPrimaryZonePoint(key, ConveyorTrackGeometry.Node(infeedNodeIndex));
            return key;
        }

        private bool _primaryInfeedOriginBlocked;

        /// <summary>
        /// 入库放货前在内核里占住入库口位置（并拿起点附近节点的邻域锁）。附近节点上 / 近点上有货时放不上去，
        /// 等内核里有货移动后再唤醒入库放货重试。
        /// </summary>
        internal bool TryClaimInfeedOrigin(int portIndex, int ownerId)
        {
            if (_primaryKernel == null)
            {
                return true;
            }

            var node = Ctx.Topology.InfeedNodeIndices[portIndex];
            if (_primaryKernel.TryClaimZone(InfeedZoneKey(node), ownerId, OriginNearClaims(node)))
            {
                return true;
            }

            _primaryInfeedOriginBlocked = true;
            return false;
        }

        internal void ReleaseInfeedOriginClaim(WarehouseJob job)
        {
            if (_primaryKernel == null || job == null || job.InfeedPortIndex < 0 || _primaryKernel.IsSubmitted(job.JobId))
            {
                return;
            }

            _primaryKernel.ReleaseClaim(InfeedZoneKey(Ctx.Topology.InfeedNodeIndices[job.InfeedPortIndex]), job.JobId);
        }

        private string PickupZoneKey(int pickupNodeIndex)
        {
            ref var node = ref Ctx.Topology.GetNode(pickupNodeIndex);
            var key = SimEntityNaming.PickupResourceId(node, pickupNodeIndex);
            RegisterPrimaryZonePoint(key, ConveyorTrackGeometry.Node(pickupNodeIndex));
            return key;
        }

        /// <summary>
        /// 出库堆垛机作业开始前先在内核里占住交互点（与入库到达交互点同一把位置锁）；
        /// 占不到（入库箱在上面或正有箱在等它）就挂起，交互点空出后由 <see cref="RetryPrimaryPickupWaiters"/> 重试。
        /// </summary>
        internal bool TryClaimOutboundPickup(WarehouseJob job)
        {
            if (_primaryKernel == null || job.PickupPointIndex < 0)
            {
                return true;
            }

            if (_primaryKernel.TryClaimZone(PickupZoneKey(job.PickupPointIndex), job.JobId, OriginNearClaims(job.PickupPointIndex)))
            {
                _primaryPickupWaiters.Remove(job.JobId);
                return true;
            }

            if (!_primaryPickupWaiters.Contains(job.JobId))
            {
                _primaryPickupWaiters.Add(job.JobId);
            }

            return false;
        }

        private bool _retryingPickupWaiters;

        private void RetryPrimaryPickupWaiters()
        {
            if (_retryingPickupWaiters || _primaryPickupWaiters.Count == 0)
            {
                return;
            }

            _retryingPickupWaiters = true;
            try
            {
                foreach (var id in _primaryPickupWaiters.ToArray())
                {
                    if (Ctx.Jobs.TryGetValue(id, out var job) && job.State == WarehouseJobState.WaitingStacker)
                    {
                        Ctx.Stacker.TryBegin(job);
                    }
                    else
                    {
                        _primaryPickupWaiters.Remove(id);
                    }
                }
            }
            finally
            {
                _retryingPickupWaiters = false;
            }
        }

        /// <summary>路径已选定后，把整条 zone 链提交给内核。状态推进和 Pump 由调用方负责。</summary>
        internal bool TrySubmitPlaced(WarehouseJob job)
        {
            List<ZoneKernelStep> steps;
            List<ZoneLockRelease> releases;
            var outbound = job.Direction == SimFlowDirection.Outbound;
            try
            {
                BuildPrimarySteps(job, out steps, out releases);
                _primaryKernel.AdvanceTo(Ctx.Clock.Now);
                // 起点作为内核第 0 步：出库 = 堆垛机交互点（堆垛机作业前已占住），入库 = 入库口（放货前已占住）。
                var originKey = outbound
                    ? PickupZoneKey(job.PickupPointIndex)
                    : InfeedZoneKey(Ctx.Topology.InfeedNodeIndices[job.InfeedPortIndex]);
                if (!outbound && _primaryKernel.IsZoneFree(originKey))
                {
                    // 兜底：放货时未占住（不应发生），现在补占；补占失败就按普通提交，从第一个停留点开始。
                    _primaryKernel.TryClaimZone(originKey, job.JobId);
                }

                steps.Insert(0, new ZoneKernelStep { ZoneKey = originKey, HopSeconds = 0 });
                for (var i = 0; i < releases.Count; i++)
                {
                    var r = releases[i];
                    r.StepIndex += 1;
                    releases[i] = r;
                }

                _primaryKernel.SubmitPlaced(job.JobId, steps, releases, Ctx.Clock.Now);
            }
            catch (Exception ex)
            {
                WarehouseSimLog.Error($"[ConveyorKernel/Primary] job={job.JobId} 提交内核失败：{ex}");
                return false;
            }

            return true;
        }

        internal void ReleaseFailedOutboundPickup(WarehouseJob job)
        {
            if (_primaryKernel != null && job != null && job.PickupPointIndex >= 0)
            {
                _primaryKernel.ReleaseClaim(PickupZoneKey(job.PickupPointIndex), job.JobId);
                _primaryPickupWaiters.Remove(job.JobId);
            }
        }

        private void BuildPrimarySteps(WarehouseJob job, out List<ZoneKernelStep> steps, out List<ZoneLockRelease> releases)
        {
            var chain = job.ConveyorPathZones;
            var path = job.ConveyorPathNodeIndices;
            var map = Ctx.Bindings.ConveyorMap;
            steps = new List<ZoneKernelStep>(chain.Count);
            releases = new List<ZoneLockRelease>();
            var last = chain.Count - 1;

            for (var k = 0; k < chain.Count; k++)
            {
                var z = chain[k];
                double dwell = 0;
                if (z.Kind == ConveyorPathZoneKind.EdgeSlot
                    && Ctx.Topology.TryGetEdge(z.FromNodeIndex, z.ToNodeIndex, out var edge)
                    && ConveyorMapMath.TryGetGateHoldSlot(map, in edge, out var gateSlot, out var gateSeconds)
                    && gateSlot == z.SlotIndex)
                {
                    dwell = gateSeconds;
                }
                else if (z.Kind == ConveyorPathZoneKind.ProcessStation)
                {
                    ref var node = ref Ctx.Topology.GetNode(z.ToNodeIndex);
                    if (ConveyorProcessStationUtility.JobRequiresStationTag(job, in node))
                    {
                        dwell = node.ProcessServiceSeconds > 0f ? node.ProcessServiceSeconds : Ctx.Bindings.ProcessStationServiceSeconds;
                    }
                }

                var zoneKey = string.IsNullOrEmpty(z.ResourceId) ? $"zone:{z.Kind}:{z.FromNodeIndex}-{z.ToNodeIndex}:{z.SlotIndex}" : z.ResourceId;
                if (_primaryTrack != null && _primaryTrack.TryGetZonePoint(z, out var zonePoint))
                {
                    RegisterPrimaryZonePoint(zoneKey, zonePoint);
                }

                steps.Add(new ZoneKernelStep
                {
                    ZoneKey = zoneKey,
                    HopSeconds = Math.Max(0f, z.HopSeconds),
                    MinDwellSeconds = dwell,
                });
            }

            // 每条路径边的槽位 zone 区间。
            var firstZ = new Dictionary<int, int>();
            var lastZ = new Dictionary<int, int>();
            for (var k = 0; k < chain.Count; k++)
            {
                if (chain[k].Kind != ConveyorPathZoneKind.EdgeSlot) continue;
                var e = chain[k].PathEdgeIndex;
                if (!firstZ.ContainsKey(e)) firstZ[e] = k;
                lastZ[e] = k;
            }

            var spurEdges = new HashSet<int>();
            var spurAcquire = new Dictionary<int, int>();
            foreach (var e in firstZ.Keys)
            {
                if (path == null || e + 2 >= path.Count || path[e] != path[e + 2] || !firstZ.ContainsKey(e + 1)) continue;
                // 折返支线（如死胡同加工站）：进出两条边共用一把独占锁，在进入支线口路口前拿，返程驶出路口后放。
                spurEdges.Add(e);
                spurEdges.Add(e + 1);
                var key = $"spur:{Math.Min(path[e], path[e + 1])}-{Math.Max(path[e], path[e + 1])}";
                var acquireAt = firstZ[e] > 0 && chain[firstZ[e] - 1].Kind == ConveyorPathZoneKind.Junction ? firstZ[e] - 1 : firstZ[e];
                steps[acquireAt].AcquireOnMoveIn.Add(new ZoneLockClaim { Key = key, Capacity = 1, Direction = 0 });
                spurAcquire[e] = acquireAt;
                spurAcquire[e + 1] = acquireAt;
                var rel = Math.Min(last, lastZ[e + 1] + 2);
                releases.Add(new ZoneLockRelease { Key = key, StepIndex = rel, DelaySeconds = 0 });
            }

            AddJunctionAxisLocks(job, steps, releases, firstZ, lastZ, spurAcquire);
            var nearSpans = AddNodeProximityLocks(job, steps, releases, spurEdges);
            foreach (var e in firstZ.Keys)
            {
                if (spurEdges.Contains(e)) continue;
                var from = chain[firstZ[e]].FromNodeIndex;
                var to = chain[firstZ[e]].ToNodeIndex;
                if (!Ctx.Topology.TryGetEdge(from, to, out var edge)) continue;
                var cap = lastZ[e] - firstZ[e] + 1;
                var twoWay = Ctx.Topology.TryGetEdge(to, from, out _);
                var destIsJunction = Ctx.Topology.GetNode(to).Kind == SimConveyorNodeKind.Junction;
                var approachHop = ConveyorMapMath.GetNodeApproachHopSeconds(map, edge);
                var key = $"lane:{Math.Min(from, to)}-{Math.Max(from, to)}";
                var acquireAt = twoWay && firstZ[e] > 0 && chain[firstZ[e] - 1].Kind == ConveyorPathZoneKind.Junction
                    ? firstZ[e] - 1
                    : firstZ[e];
                steps[acquireAt].AcquireOnMoveIn.Add(new ZoneLockClaim { Key = key, Capacity = cap, Direction = from });
                // 与占用自检的同向在途窗 [Entry-entryHop, Exit+approachHop] 对齐；双向段多留一步覆盖车尾。
                int rel;
                double delay;
                if (twoWay)
                {
                    // 对向：车尾离开本段才可放行。
                    rel = ResolveTailClearStep(chain, lastZ[e] + 1 + (destIsJunction ? 1 : 0), out delay);
                }
                else
                {
                    rel = Math.Min(last, lastZ[e] + 1);
                    delay = destIsJunction ? 0d : approachHop;
                }

                releases.Add(new ZoneLockRelease { Key = key, StepIndex = rel, DelaySeconds = delay });
            }

            // 通道锁仍按段拿。整段提前占容量锁会把同向也卡死，对向直段只加一把方向锁。
            AddStraightCorridorTokens(chain, firstZ, lastZ, spurEdges, steps, releases);
            HoistClaimsIntoNodeApproach(steps, nearSpans);
        }

        /// <summary>
        /// 同一条物理直道上，同一时刻只允许一个行驶方向。同向不限数量，仍由各段通道锁限流；
        /// 对向在入口外等待，直到这条直道上的车尾都离开。
        /// </summary>
        private void AddStraightCorridorTokens(
            List<ConveyorPathZone> chain,
            Dictionary<int, int> firstZ,
            Dictionary<int, int> lastZ,
            HashSet<int> spurEdges,
            List<ZoneKernelStep> steps,
            List<ZoneLockRelease> releases)
        {
            if (_straightCorridorId == null || _straightCorridorId.Count == 0)
            {
                return;
            }

            var map = Ctx.Bindings.ConveyorMap;
            var edges = new List<int>();
            foreach (var e in firstZ.Keys)
            {
                if (spurEdges != null && spurEdges.Contains(e))
                {
                    continue;
                }

                edges.Add(e);
            }

            if (edges.Count == 0)
            {
                return;
            }

            edges.Sort();
            var acquireAt = new Dictionary<(int Id, int Sense), int>();
            var releaseAt = new Dictionary<(int Id, int Sense), (int Step, double Delay)>();
            foreach (var e in edges)
            {
                var from = chain[firstZ[e]].FromNodeIndex;
                var to = chain[firstZ[e]].ToNodeIndex;
                if (!_straightCorridorId.TryGetValue(EdgeKey(from, to), out var id))
                {
                    continue;
                }

                var sense = TravelSense(map, from, to);
                if (sense == 0)
                {
                    continue;
                }

                var pair = (id, sense);
                var entry = firstZ[e] > 0 && chain[firstZ[e] - 1].Kind == ConveyorPathZoneKind.Junction
                    ? firstZ[e] - 1
                    : firstZ[e];
                if (!acquireAt.TryGetValue(pair, out var existing) || entry < existing)
                {
                    acquireAt[pair] = entry;
                }

                var destIsJunction = Ctx.Topology.GetNode(to).Kind == SimConveyorNodeKind.Junction;
                var rel = ResolveTailClearStep(chain, lastZ[e] + 1 + (destIsJunction ? 1 : 0), out var delay);
                if (!releaseAt.TryGetValue(pair, out var prev)
                    || rel > prev.Step
                    || (rel == prev.Step && delay > prev.Delay))
                {
                    releaseAt[pair] = (rel, delay);
                }
            }

            foreach (var pair in acquireAt.Keys)
            {
                if (!releaseAt.TryGetValue(pair, out var rel))
                {
                    continue;
                }

                var key = $"corr:{pair.Id}";
                steps[acquireAt[pair]].AcquireOnMoveIn.Add(new ZoneLockClaim
                {
                    Key = key,
                    Capacity = int.MaxValue,
                    Direction = pair.Sense,
                });
                releases.Add(new ZoneLockRelease
                {
                    Key = key,
                    StepIndex = rel.Step,
                    DelaySeconds = rel.Delay,
                });
            }
        }

        /// <summary>
        /// 进入节点邻域那一步把邻域内后续各步要拿的锁（通道 / 直线 / 支线）一并原子拿到。
        /// 否则会出现「我拿着邻域锁在近点等你的直线锁、你拿着直线锁等我的邻域锁」的对顶。
        /// </summary>
        private static void HoistClaimsIntoNodeApproach(List<ZoneKernelStep> steps, List<(int Acquire, int Node)> spans)
        {
            if (spans == null || spans.Count == 0) return;
            for (var pass = 0; pass < 3; pass++)
            {
                var changed = false;
                foreach (var (a, j) in spans)
                {
                    var target = steps[a].AcquireOnMoveIn;
                    for (var k = a + 1; k <= j && k < steps.Count; k++)
                    {
                        foreach (var claim in steps[k].AcquireOnMoveIn)
                        {
                            if (!target.Exists(c => string.Equals(c.Key, claim.Key, StringComparison.Ordinal)))
                            {
                                target.Add(claim);
                                changed = true;
                            }
                        }
                    }
                }

                if (!changed) break;
            }
        }

        /// <summary>
        /// 节点邻域锁：短路段上的停留点离节点不足一个箱长时，停在那里的货和节点上的货会重叠，内核的车身间距规则
        /// 不允许两者同时有货。若两件货各自停在节点两侧的近点互相等对方，就会对顶。这里给每个节点类 zone 一把独占锁，
        /// 从「连续都离该节点不足一个箱长」的最早一步开始拿，开到离节点满一个箱长的位置再放，同一时刻只有一件货在该节点邻域内。
        /// 一步里拿到的多把锁是一次性原子拿的，相邻的几个近距节点会在同一步一起拿，不会出现交叉持有。
        /// </summary>
        private List<(int Acquire, int Node)> AddNodeProximityLocks(WarehouseJob job, List<ZoneKernelStep> steps, List<ZoneLockRelease> releases, HashSet<int> spurEdges)
        {
            var chain = job.ConveyorPathZones;
            var hoist = new List<(int Acquire, int Node)>();
            if (_primaryTrack == null || chain == null || chain.Count == 0)
            {
                return hoist;
            }

            var n = chain.Count;
            var pts = new ConveyorTrackPoint[n];
            var ok = new bool[n];
            for (var k = 0; k < n; k++)
            {
                ok[k] = _primaryTrack.TryGetZonePoint(chain[k], out pts[k]);
            }

            var limit = _primaryTrack.CargoLength - 1e-3;
            var spans = new Dictionary<string, List<(int Acquire, int Release)>>(StringComparer.Ordinal);
            var order = new List<string>();
            for (var j = 0; j < n; j++)
            {
                if (chain[j].Kind == ConveyorPathZoneKind.EdgeSlot || !ok[j])
                {
                    continue;
                }

                var acquireAt = j;
                while (acquireAt - 1 >= 0 && ok[acquireAt - 1] && _primaryTrack.Distance(pts[acquireAt - 1], pts[j]) < limit)
                {
                    acquireAt--;
                }

                var release = j + 1;
                while (release < n && ok[release] && _primaryTrack.Distance(pts[release], pts[j]) < limit)
                {
                    release++;
                }

                // 所有经过该节点的货都要拿这把锁（哪怕自己的路线前后都离节点够远），否则别人在近点占着锁、
                // 自己直接驶入节点时仍会被车距规则挡住而对顶。
                // 同一节点在路线上出现多次（如死胡同加工站进出都过同一路口）：区间重叠时合并成一段，
                // 否则前一次的释放会把后一次的锁提前放掉；不重叠的各自拿放，避免绕一圈期间一直占着。
                var key = NodeNearLockKey(chain[j].ToNodeIndex);
                var rel = Math.Min(release, n - 1);
                if (acquireAt < j) hoist.Add((acquireAt, j));
                if (!spans.TryGetValue(key, out var list))
                {
                    list = new List<(int Acquire, int Release)>();
                    spans[key] = list;
                    order.Add(key);
                }

                if (list.Count > 0 && (acquireAt <= list[list.Count - 1].Release || OnlySpurBetween(list[list.Count - 1].Release, acquireAt)))
                {
                    var lastSpan = list[list.Count - 1];
                    list[list.Count - 1] = (Math.Min(lastSpan.Acquire, acquireAt), Math.Max(lastSpan.Release, rel));
                }
                else
                {
                    list.Add((acquireAt, rel));
                }
            }

            bool OnlySpurBetween(int from, int to)
            {
                // 进折返支线再原路回到同一路口：整段支线往返期间一直占着该路口邻域，否则返程时会和在路口外等着进支线的货对顶。
                if (spurEdges == null || spurEdges.Count == 0) return false;
                for (var k = from; k < to; k++)
                {
                    if (chain[k].Kind != ConveyorPathZoneKind.Junction && !spurEdges.Contains(chain[k].PathEdgeIndex)) return false;
                    if (chain[k].Kind == ConveyorPathZoneKind.Junction && k > from && !spurEdges.Contains(chain[k].PathEdgeIndex)) return false;
                }

                return true;
            }

            foreach (var key in order)
            {
                foreach (var span in spans[key])
                {
                    steps[span.Acquire].AcquireOnMoveIn.Add(new ZoneLockClaim { Key = key, Capacity = 1, Direction = 0 });
                    releases.Add(new ZoneLockRelease { Key = key, StepIndex = span.Release, DelaySeconds = 0 });
                }
            }

            return hoist;
        }

        /// <summary>
        /// 路口直线锁：同一路口沿同一直线从两侧驶入的边，靠近路口的停留点不能同时有货（与路段同时占用自检的
        /// 路口直线检查一致）。同一条边上的货可同时持有，对向互斥；车尾离开该段后释放。
        /// </summary>
        private void AddJunctionAxisLocks(
            WarehouseJob job,
            List<ZoneKernelStep> steps,
            List<ZoneLockRelease> releases,
            Dictionary<int, int> firstZ,
            Dictionary<int, int> lastZ,
            Dictionary<int, int> spurAcquireStep)
        {
            var chain = job.ConveyorPathZones;
            var map = Ctx.Bindings.ConveyorMap;
            var last = chain.Count - 1;
            var cargo = ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            foreach (var e in firstZ.Keys)
            {
                var from = chain[firstZ[e]].FromNodeIndex;
                var to = chain[firstZ[e]].ToNodeIndex;
                if (!Ctx.Topology.TryGetEdge(from, to, out var edge)
                    || !ConveyorJunctionAxis.TryBuildResourceId(map, from, to, out var axisId))
                {
                    continue;
                }

                var cap = lastZ[e] - firstZ[e] + 1;
                var length = Math.Max(0f, edge.DistanceMeters);
                var speed = Math.Max(0.01f, ConveyorMapMath.ResolveSpeedMetersPerSecond(map, edge));
                var tailSeconds = cargo / speed;
                int acquireAt;
                if (length <= cargo + 1e-3f)
                {
                    acquireAt = firstZ[e];
                }
                else
                {
                    acquireAt = lastZ[e] + 1; // 默认：驶离 S0 时
                    for (var k = firstZ[e]; k <= lastZ[e]; k++)
                    {
                        var s = chain[k].SlotIndex;
                        var meters = ConveyorMapMath.GetZoneNormalizedPosition(map, edge, cap, s) * length;
                        if (length - meters <= cargo + 0.05f)
                        {
                            acquireAt = k;
                            break;
                        }
                    }

                    acquireAt = Math.Min(acquireAt, last);
                }

                // 双向段：直线锁与通道锁同一步拿（进入本段前），否则会出现“我占直线等你的通道、你占通道等我的直线”的对顶死锁。
                if (Ctx.Topology.TryGetEdge(to, from, out _))
                {
                    var entryAcquire = firstZ[e] > 0 && chain[firstZ[e] - 1].Kind == ConveyorPathZoneKind.Junction
                        ? firstZ[e] - 1
                        : firstZ[e];
                    if (spurAcquireStep != null && spurAcquireStep.TryGetValue(e, out var spurStep))
                    {
                        entryAcquire = spurStep;
                    }

                    acquireAt = Math.Min(acquireAt, entryAcquire);
                }

                steps[acquireAt].AcquireOnMoveIn.Add(new ZoneLockClaim { Key = axisId, Capacity = int.MaxValue, Direction = from });
                var tailStep = ResolveTailClearStep(chain, lastZ[e] + 2, out var tailDelay);
                releases.Add(new ZoneLockRelease
                {
                    Key = axisId,
                    StepIndex = tailStep,
                    DelaySeconds = tailDelay,
                });
            }
        }

        /// <summary>
        /// 车尾离开路口所在边的时刻：从 fromStep 起沿下游累计行驶距离，满一个车身长度的那一步到达时释放。
        /// 下游都是短边（短于车身）时车尾会在路口边上停很久，必须按距离而不是按步数算。
        /// 路径剩余距离不够车身时，在终点到达后再补一个车身时长。
        /// </summary>
        private int ResolveTailClearStep(List<ConveyorPathZone> chain, int fromStep, out double delaySeconds)
        {
            var map = Ctx.Bindings.ConveyorMap;
            var cargo = (double)ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            var travelled = 0d;
            var last = chain.Count - 1;
            delaySeconds = 0;
            for (var k = Math.Max(0, fromStep); k <= last; k++)
            {
                var z = chain[k];
                var speed = 1f;
                if (Ctx.Topology.TryGetEdge(z.FromNodeIndex, z.ToNodeIndex, out var edge))
                {
                    speed = Math.Max(0.01f, ConveyorMapMath.ResolveSpeedMetersPerSecond(map, edge));
                }

                travelled += Math.Max(0f, z.HopSeconds) * speed;
                if (travelled >= cargo - 1e-3)
                {
                    return k;
                }
            }

            var lastSpeed = 1f;
            if (Ctx.Topology.TryGetEdge(chain[last].FromNodeIndex, chain[last].ToNodeIndex, out var lastEdge))
            {
                lastSpeed = Math.Max(0.01f, ConveyorMapMath.ResolveSpeedMetersPerSecond(map, lastEdge));
            }

            delaySeconds = Math.Max(0d, cargo - travelled) / lastSpeed;
            return last;
        }

        private static double[] Slice(double[] a)
        {
            var r = new double[a.Length - 1];
            Array.Copy(a, 1, r, 0, r.Length);
            return r;
        }

        /// <summary>出库口服务结束、货箱离开出库口：出库口位置交还内核。</summary>
        internal void ReleaseOutfeed(WarehouseJob job)
        {
            if (!IsPrimaryKernelJob(job) || !_primaryKernel.IsSubmitted(job.JobId))
            {
                return;
            }

            job.ConveyorGoalReleaseSimTime = Ctx.Clock.Now;
            _primaryKernel.ReleaseGoal(job.JobId, Ctx.Clock.Now);
            Pump();
        }

        /// <summary>主循环 tick：推进内核到当前时刻并处理输出。</summary>
        internal void OnTick()
        {
            if (_primaryKernel == null)
            {
                return;
            }

            if (Ctx.Clock.Now >= _primaryNextTick - 1e-9)
            {
                _primaryNextTick = double.PositiveInfinity;
            }

            Pump();
        }

        internal void Pump()
        {
            try
            {
                _primaryKernel.AdvanceTo(Ctx.Clock.Now);
                foreach (var output in _primaryKernel.DrainOutputs())
                {
                    if (!Ctx.Jobs.TryGetValue(output.CargoId, out var job))
                    {
                        WarehouseSimLog.Error($"[ConveyorKernel/Primary] 输出指向不存在的 job={output.CargoId}");
                        continue;
                    }

                    switch (output.Kind)
                    {
                        case ZoneKernelOutputKind.OriginDeparted:
                            if (job.Direction == SimFlowDirection.Inbound)
                            {
                                Ctx.Infeed.SchedulePhysicalRelease(job, output.Time);
                            }

                            break;
                        case ZoneKernelOutputKind.StartStepLeft:
                            if (job.Direction == SimFlowDirection.Outbound)
                            {
                                // 货箱离开交互点：堆垛机可接下一出库单。
                                Ctx.Outfeed.ReleaseStackerAfterPickupDeparture(job);
                            }

                            break;
                        case ZoneKernelOutputKind.GoalArrived:
                            WriteBackPrimaryTimeline(job, output.Time);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                WarehouseSimLog.Error($"[ConveyorKernel/Primary] 内核推进异常 t={Ctx.Clock.Now:F2}：{ex}");
            }

            EnsurePrimaryTick();
            RetryPrimaryPickupWaiters();
            if (_primaryInfeedOriginBlocked)
            {
                _primaryInfeedOriginBlocked = false;
                Ctx.Infeed.ScheduleQueueFill(Ctx.Clock.Now);
            }
        }

        private void EnsurePrimaryTick()
        {
            if (!_primaryKernel.TryPeekNextEventTime(out var next))
            {
                return;
            }

            next = Math.Max(next, Ctx.Clock.Now);
            if (next < _primaryNextTick - 1e-9)
            {
                _primaryNextTick = next;
                Ctx.Queue.Enqueue(new ScheduledSimEvent(next, SimEventType.ConveyorKernelTick, -1));
            }
        }

        /// <summary>堆垛机取货完成：取货点交还内核。</summary>
        internal void ReleaseGoal(WarehouseJob job)
        {
            if (!IsPrimaryKernelJob(job) || !_primaryKernel.IsSubmitted(job.JobId))
            {
                return;
            }

            job.ConveyorGoalReleaseSimTime = Ctx.Clock.Now;
            _primaryKernel.ReleaseGoal(job.JobId, Ctx.Clock.Now);
            Pump();
        }

        /// <summary>仿真结束时报告内核残留 / 违例。返回滞留说明（无滞留则为 null）。</summary>
        internal string ReportDiagnostics()
        {
            if (_primaryKernel == null)
            {
                return null;
            }

            foreach (var v in _primaryKernel.Violations)
            {
                WarehouseSimLog.Error($"[ConveyorKernel/Primary] 不变量违例：{v}");
            }

            if (_primaryKernel.ActiveCount <= 0)
            {
                WarehouseSimLog.Info($"[ConveyorKernel/Primary] 完成：{_primaryGoalCount} 件货物经内核送达取货点。");
                return null;
            }

            var stall = _primaryKernel.DescribeStall();
            if (string.IsNullOrWhiteSpace(stall))
            {
                stall = $"仍有 {_primaryKernel.ActiveCount} 件货物在内核中，但没有可推进事件。";
            }

            WarehouseSimLog.Error(
                $"[ConveyorKernel/Primary] 结束时仍有 {_primaryKernel.ActiveCount} 件货物在内核中（疑似死锁）：\n{stall}");
            return stall;
        }

        /// <summary>到达取货点：把内核时间线写回旧数据结构（zone 链、路段时刻表、子任务、回放），再交给旧堆垛机流程。</summary>
        private void WriteBackPrimaryTimeline(WarehouseJob job, double now)
        {
            _primaryGoalCount++;
            if (!_primaryKernel.TryGetTimeline(job.JobId, out var ms, out var arrive, out var ready))
            {
                WarehouseSimLog.Error($"[ConveyorKernel/Primary] job={job.JobId} 找不到内核时间线");
                return;
            }

            var chain = job.ConveyorPathZones;
            var n = chain.Count;
            var map = Ctx.Bindings.ConveyorMap;
            if (ms.Length == n + 1)
            {
                // 出库：内核第 0 步是交互点，去掉后与 zone 链对齐。
                ms = Slice(ms);
                arrive = Slice(arrive);
                ready = Slice(ready);
            }

            job.ConveyorStartMoveSimTime = ms[0];

            var originReady = job.Direction == SimFlowDirection.Outbound ? job.PickupCompleteSimTime : job.InfeedCompleteSimTime;
            for (var k = 0; k < n; k++)
            {
                var z = chain[k];
                z.DesiredArriveSimTime = ready[k] + z.HopSeconds;
                z.ArriveSimTime = arrive[k];
                z.LeaveSimTime = k + 1 < n ? ms[k + 1] : arrive[k];
                chain[k] = z;
            }

            // 路段时刻表：每条路径边一条。
            job.ConveyorSegmentSchedule ??= new List<ConveyorSegmentScheduleEntry>();
            job.ConveyorSegmentSchedule.Clear();
            var k0 = 0;
            while (k0 < n)
            {
                if (chain[k0].Kind != ConveyorPathZoneKind.EdgeSlot)
                {
                    k0++;
                    continue;
                }

                var e = chain[k0].PathEdgeIndex;
                var k1 = k0;
                while (k1 + 1 < n && chain[k1 + 1].Kind == ConveyorPathZoneKind.EdgeSlot && chain[k1 + 1].PathEdgeIndex == e) k1++;
                var cap = k1 - k0 + 1;
                var from = chain[k0].FromNodeIndex;
                var to = chain[k0].ToNodeIndex;
                var stops = new double[cap];
                for (var k = k0; k <= k1; k++)
                {
                    var s = chain[k].SlotIndex;
                    if (s >= 0 && s < cap) stops[s] = arrive[k];
                }

                var nextK = Math.Min(k1 + 1, n - 1);
                var destIsJunction = Ctx.Topology.GetNode(to).Kind == SimConveyorNodeKind.Junction;
                double approachHop = 0;
                if (Ctx.Topology.TryGetEdge(from, to, out var edge))
                {
                    approachHop = ConveyorMapMath.GetNodeApproachHopSeconds(map, edge);
                }

                var exit = destIsJunction ? ms[nextK] : ms[nextK] + approachHop;
                job.ConveyorSegmentSchedule.Add(new ConveyorSegmentScheduleEntry
                {
                    FromNodeIndex = from,
                    ToNodeIndex = to,
                    PathEdgeIndex = e,
                    SlotIndex = cap - 1,
                    DesiredEntrySimTime = k0 == 0 ? originReady : ready[k0],
                    EntrySimTime = stops[cap - 1],
                    ExitSimTime = exit,
                    OccupancyEndSimTime = Math.Max(exit, arrive[nextK]),
                    StopArriveSimTimes = stops,
                });
                k0 = k1 + 1;
            }

            Ctx.EnsurePlaybackSnapshot(job);
            if (Ctx.ShouldRecordPlayback)
            {
                RecordPrimarySubTasks(job, ms, arrive);
                Ctx.RecordPlaybackAt(
                    arrive[n - 1],
                    job,
                    SimPlaybackPhase.ConveyorRouted,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    attachPathContext: true);
            }

            job.ScheduledCompleteTime = now;
            Ctx.Queue.Enqueue(new ScheduledSimEvent(now, SimEventType.ConveyorTransitComplete, job.JobId));
        }

        /// <summary>按内核时间线生成与旧引擎同类的子任务，时间轴首尾相接、无空档。</summary>
        private void RecordPrimarySubTasks(WarehouseJob job, double[] ms, double[] arrive)
        {
            var chain = job.ConveyorPathZones;
            var n = chain.Count;
            var map = Ctx.Bindings.ConveyorMap;
            const double eps = 1e-9;

            void Rec(SimSubTaskKind kind, double a, double b, int from, int to, int slot, bool attach = false)
            {
                if (b > a + eps)
                {
                    Ctx.RecordSubTask(job, kind, a, b, job.AssignedStackerId, job.TargetSlot, from, to, slot, attach);
                }
            }

            var z0 = chain[0];
            if (job.Direction == SimFlowDirection.Outbound)
            {
                // 交互点排队（slot=-1）→ 驶离交互点进入首段入口停留点。
                Rec(SimSubTaskKind.SegmentQueue, job.PickupCompleteSimTime, ms[0], z0.FromNodeIndex, z0.ToNodeIndex, -1, attach: true);
                Rec(SimSubTaskKind.OutboundMove, ms[0], arrive[0], z0.FromNodeIndex, z0.ToNodeIndex, Math.Max(0, z0.SlotIndex), attach: ms[0] <= job.PickupCompleteSimTime + eps);
            }
            else
            {
                Rec(SimSubTaskKind.SegmentQueue, job.InfeedCompleteSimTime, ms[0], z0.FromNodeIndex, z0.ToNodeIndex, Math.Max(0, z0.SlotIndex), attach: true);
                Rec(SimSubTaskKind.InfeedMove, ms[0], arrive[0], z0.FromNodeIndex, z0.ToNodeIndex, Math.Max(0, z0.SlotIndex), attach: ms[0] <= job.InfeedCompleteSimTime + eps);
            }

            for (var k = 0; k < n; k++)
            {
                var z = chain[k];
                var prev = k > 0 ? chain[k - 1] : default;
                // 驶入 k（k=0 已记为 InfeedMove；路口后的入口由路口驶出记录）。
                if (k > 0)
                {
                    switch (z.Kind)
                    {
                        case ConveyorPathZoneKind.Junction:
                            Rec(SimSubTaskKind.JunctionEnter, ms[k], arrive[k], z.FromNodeIndex, z.ToNodeIndex, 0);
                            break;
                        case ConveyorPathZoneKind.EdgeSlot:
                            if (prev.Kind != ConveyorPathZoneKind.Junction)
                            {
                                Rec(SimSubTaskKind.SegmentHopMove, ms[k], arrive[k], z.FromNodeIndex, z.ToNodeIndex, z.SlotIndex);
                            }

                            break;
                        default:
                            Rec(SimSubTaskKind.SegmentHopMove, ms[k], arrive[k], z.FromNodeIndex, z.ToNodeIndex, -1, attach: k == n - 1);
                            break;
                    }
                }

                if (k == n - 1)
                {
                    break; // 取货点停留由堆垛机子任务（等待/取货）接续。
                }

                var leave = ms[k + 1];
                switch (z.Kind)
                {
                    case ConveyorPathZoneKind.Junction:
                        Rec(SimSubTaskKind.JunctionWait, arrive[k], leave, z.FromNodeIndex, z.ToNodeIndex, 0);
                        if (chain[k + 1].Kind == ConveyorPathZoneKind.EdgeSlot)
                        {
                            Rec(SimSubTaskKind.JunctionExit, leave, arrive[k + 1], z.FromNodeIndex, z.ToNodeIndex, 0);
                        }

                        break;
                    case ConveyorPathZoneKind.EdgeSlot:
                    {
                        var holdStart = arrive[k];
                        if (Ctx.Topology.TryGetEdge(z.FromNodeIndex, z.ToNodeIndex, out var edge)
                            && ConveyorMapMath.TryGetGateHoldSlot(map, in edge, out var gateSlot, out var gateSeconds)
                            && gateSlot == z.SlotIndex)
                        {
                            var gateEnd = Math.Min(leave, holdStart + gateSeconds);
                            Rec(SimSubTaskKind.SegmentGateWait, holdStart, gateEnd, z.FromNodeIndex, z.ToNodeIndex, z.SlotIndex);
                            holdStart = gateEnd;
                        }

                        Rec(SimSubTaskKind.SegmentStopDwell, holdStart, leave, z.FromNodeIndex, z.ToNodeIndex, z.SlotIndex);
                        break;
                    }
                    case ConveyorPathZoneKind.ProcessStation:
                    {
                        ref var node = ref Ctx.Topology.GetNode(z.ToNodeIndex);
                        var kind = ConveyorProcessStationUtility.JobRequiresStationTag(job, in node)
                            ? SimSubTaskKind.ProcessStationService
                            : SimSubTaskKind.SegmentStopDwell;
                        Rec(kind, arrive[k], leave, z.FromNodeIndex, z.ToNodeIndex, -1, attach: kind == SimSubTaskKind.ProcessStationService);
                        break;
                    }
                    case ConveyorPathZoneKind.VerticalTransfer:
                        Rec(SimSubTaskKind.VerticalTransferMove, arrive[k], leave, z.FromNodeIndex, z.ToNodeIndex, -1);
                        break;
                    default:
                        Rec(SimSubTaskKind.SegmentStopDwell, arrive[k], leave, z.FromNodeIndex, z.ToNodeIndex, -1);
                        break;
                }
            }
        }
    }
}
