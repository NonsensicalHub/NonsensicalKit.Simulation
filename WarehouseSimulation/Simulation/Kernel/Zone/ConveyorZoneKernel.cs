using System;
using System.Collections.Generic;
using System.Text;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Kernel.Zone
{
    /// <summary>通道锁申领：同一 Key 同时最多 Capacity 个持有者，且持有者方向必须一致。</summary>
    public struct ZoneLockClaim
    {
        public string Key;
        public int Capacity;
        public int Direction;
    }

    /// <summary>通道锁释放：在到达 StepIndex 后再过 DelaySeconds 释放。</summary>
    public struct ZoneLockRelease
    {
        public string Key;
        public int StepIndex;
        public double DelaySeconds;
    }

    /// <summary>路径上的一个独占位置（停留点 / 路口 / 加工站 / 取货点）。</summary>
    public sealed class ZoneKernelStep
    {
        public string ZoneKey;
        /// <summary>从上一位置驶入本位置的行驶时长。</summary>
        public double HopSeconds;
        /// <summary>到达后至少停留的时长（门前等待、加工服务）。</summary>
        public double MinDwellSeconds;
        /// <summary>开始驶入本位置时需一次性拿到的锁。</summary>
        public List<ZoneLockClaim> AcquireOnMoveIn = new List<ZoneLockClaim>();
    }

    public enum ZoneKernelOutputKind
    {
        /// <summary>到达第一个输送位置（已离开起点，如入库口可释放）：普通提交为第 0 步，SubmitPlaced 为第 1 步。</summary>
        OriginDeparted,
        /// <summary>到达最后一步（目标位置，保持占用直到 ReleaseGoal）。</summary>
        GoalArrived,
        /// <summary>开始驶离第 0 步（出库：货箱离开堆垛机交互点）。</summary>
        StartStepLeft,
    }

    public struct ZoneKernelOutput
    {
        public ZoneKernelOutputKind Kind;
        public int CargoId;
        public double Time;
    }

    /// <summary>
    /// 输送段 zone 级在线调度内核（纯 C#，不依赖 Unity 与旧预约表）。
    /// 语义：货物仅当下一位置空闲且所需锁全部可得时才开始驶入；开始驶入即释放当前位置；
    /// 目标位置一直占用到 <see cref="ReleaseGoal"/>。同一时刻按等待先后（再按提交序）仲裁。
    /// 因为每一步都在“现在”按真实占用决定，不存在旧引擎那种先订后被顺延、而跟随者未重验的情况。
    /// </summary>
    public sealed class ConveyorZoneKernel
    {
        private const double Eps = 1e-9;

        private sealed class Cargo
        {
            public int Id;
            public long Seq;
            public ZoneKernelStep[] Steps;
            public List<ZoneLockRelease> Releases;
            public int Current = -1;      // 已到达（或正驶向）的步
            public bool Moving;
            public double ReadyAt;
            public double WaitingSince;
            public bool AtGoal;
            public bool Done;
            public double[] MoveStart;
            public double[] Arrive;
            public double[] ReadyTime;
            public HashSet<string> HeldLocks = new HashSet<string>();
            /// <summary>以 <see cref="SubmitPlaced"/> 提交（起点已由外部占住）。</summary>
            public bool Placed;
            /// <summary>占起点时一并拿的锁：驶离起点时若下一步不再需要就释放。</summary>
            public HashSet<string> OriginLocks;
        }

        private sealed class LockState
        {
            public int Count;
            public int Direction;
        }

        private enum EvKind { Arrive = 0, LockRelease = 1, Ready = 2 }

        private struct Ev
        {
            public double Time;
            public EvKind Kind;
            public int CargoId;
            public string Key;
            public long Seq;
        }

        private readonly Dictionary<int, Cargo> _cargos = new Dictionary<int, Cargo>();
        private readonly List<Cargo> _active = new List<Cargo>();
        private readonly Dictionary<string, int> _zoneOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, LockState> _locks = new Dictionary<string, LockState>(StringComparer.Ordinal);
        private readonly List<Ev> _events = new List<Ev>();
        // 锁的等待者（被对向持有挡住）：防止同向源源不断续上导致对向饿死。
        private readonly Dictionary<string, Dictionary<int, (int Direction, double Since)>> _lockWaiters =
            new Dictionary<string, Dictionary<int, (int Direction, double Since)>>(StringComparer.Ordinal);
        private readonly List<ZoneKernelOutput> _outputs = new List<ZoneKernelOutput>();
        private readonly List<string> _violations = new List<string>();
        // TryClaimZone 一并拿到、尚未转交给货物的锁。
        private readonly Dictionary<int, HashSet<string>> _claimLocks = new Dictionary<int, HashSet<string>>();
        private long _seq;
        private double _now;

        public double Now => _now;

        /// <summary>
        /// 车身间距规则：返回 true 表示两个位置中心沿输送线的距离小于一个箱长，不能同时被不同货物占用。
        /// 为 null 时只按位置独占（旧语义）。
        /// </summary>
        public Func<string, string, bool> ZonesTooClose;

        private bool IsBlockedByNeighbour(string zoneKey, int selfId)
        {
            if (ZonesTooClose == null) return false;
            foreach (var pair in _zoneOwner)
            {
                if (pair.Value != selfId && !string.Equals(pair.Key, zoneKey, StringComparison.Ordinal) && ZonesTooClose(zoneKey, pair.Key))
                {
                    return true;
                }
            }

            return false;
        }
        public IReadOnlyList<string> Violations => _violations;
        public int ActiveCount => _active.Count;

        public void Submit(int cargoId, IReadOnlyList<ZoneKernelStep> steps, IReadOnlyList<ZoneLockRelease> releases, double readyTime)
        {
            if (steps == null || steps.Count == 0)
            {
                throw new ArgumentException("steps 为空");
            }

            if (_cargos.ContainsKey(cargoId))
            {
                throw new InvalidOperationException($"cargo {cargoId} 已提交");
            }

            var n = steps.Count;
            var c = new Cargo
            {
                Id = cargoId,
                Seq = _seq++,
                Steps = new List<ZoneKernelStep>(steps).ToArray(),
                Releases = releases != null ? new List<ZoneLockRelease>(releases) : new List<ZoneLockRelease>(),
                ReadyAt = Math.Max(readyTime, _now),
                WaitingSince = Math.Max(readyTime, _now),
                MoveStart = Fill(n),
                Arrive = Fill(n),
                ReadyTime = Fill(n),
            };
            _cargos[cargoId] = c;
            _active.Add(c);
            Push(c.ReadyAt, EvKind.Ready, cargoId, null);
        }

        /// <summary>
        /// 货物已在第 0 步上（例如堆垛机放到交互点）：第 0 步须已由 <see cref="TryClaimZone"/> 以同一 id 占住。
        /// </summary>
        public void SubmitPlaced(int cargoId, IReadOnlyList<ZoneKernelStep> steps, IReadOnlyList<ZoneLockRelease> releases, double readyTime)
        {
            if (steps == null || steps.Count == 0) throw new ArgumentException("steps 为空");
            if (_cargos.ContainsKey(cargoId)) throw new InvalidOperationException($"cargo {cargoId} 已提交");
            var key = steps[0].ZoneKey;
            if (!_zoneOwner.TryGetValue(key, out var owner) || owner != cargoId)
            {
                throw new InvalidOperationException($"cargo {cargoId} 未占住起点 {key}");
            }

            var n = steps.Count;
            var t = Math.Max(readyTime, _now);
            var c = new Cargo
            {
                Id = cargoId,
                Seq = _seq++,
                Steps = new List<ZoneKernelStep>(steps).ToArray(),
                Releases = releases != null ? new List<ZoneLockRelease>(releases) : new List<ZoneLockRelease>(),
                Current = 0,
                Placed = true,
                ReadyAt = t,
                WaitingSince = t,
                MoveStart = Fill(n),
                Arrive = Fill(n),
                ReadyTime = Fill(n),
            };
            c.MoveStart[0] = t;
            c.Arrive[0] = t;
            c.ReadyTime[0] = t;
            if (_claimLocks.TryGetValue(cargoId, out var claimed))
            {
                _claimLocks.Remove(cargoId);
                c.OriginLocks = claimed;
                foreach (var held in claimed) c.HeldLocks.Add(held);
            }

            foreach (var claim in steps[0].AcquireOnMoveIn)
            {
                if (c.HeldLocks.Add(claim.Key))
                {
                    if (!_locks.TryGetValue(claim.Key, out var ls)) { ls = new LockState(); _locks[claim.Key] = ls; }
                    ls.Count++;
                    ls.Direction = claim.Direction;
                }
            }

            _cargos[cargoId] = c;
            _active.Add(c);
            if (n == 1)
            {
                c.AtGoal = true;
                _outputs.Add(new ZoneKernelOutput { Kind = ZoneKernelOutputKind.GoalArrived, CargoId = c.Id, Time = t });
            }

            Push(t, EvKind.Ready, cargoId, null);
        }

        /// <summary>
        /// 外部占用一个位置（例如堆垛机准备往交互点放货）。位置空闲且没有已就绪的货在等它时才成功（等待中的货优先）。
        /// </summary>
        public bool TryClaimZone(string zoneKey, int ownerId) => TryClaimZone(zoneKey, ownerId, null);

        /// <summary>
        /// 同上，并一次性拿到 <paramref name="locks"/>（例如起点附近节点的邻域锁）；拿不全则什么都不占。
        /// 这些锁在 <see cref="SubmitPlaced"/> 时转交给货物，驶离起点后若下一步不再需要即释放。
        /// </summary>
        public bool TryClaimZone(string zoneKey, int ownerId, IReadOnlyList<ZoneLockClaim> locks)
        {
            if (_zoneOwner.TryGetValue(zoneKey, out var owner))
            {
                return owner == ownerId;
            }

            if (locks != null)
            {
                foreach (var claim in locks)
                {
                    if (_locks.TryGetValue(claim.Key, out var ls) && ls.Count > 0
                        && (ls.Direction != claim.Direction || ls.Count >= Math.Max(1, claim.Capacity)))
                    {
                        return false;
                    }
                }
            }

            foreach (var c in _active)
            {
                if (!c.Moving && !c.AtGoal && c.Current + 1 < c.Steps.Length
                    && c.ReadyAt <= _now + Eps
                    && string.Equals(c.Steps[c.Current + 1].ZoneKey, zoneKey, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (IsBlockedByNeighbour(zoneKey, ownerId))
            {
                return false;
            }

            _zoneOwner[zoneKey] = ownerId;
            if (locks != null && locks.Count > 0)
            {
                var held = new HashSet<string>(StringComparer.Ordinal);
                foreach (var claim in locks)
                {
                    if (!held.Add(claim.Key)) continue;
                    if (!_locks.TryGetValue(claim.Key, out var ls)) { ls = new LockState(); _locks[claim.Key] = ls; }
                    ls.Count++;
                    ls.Direction = claim.Direction;
                }

                _claimLocks[ownerId] = held;
            }

            return true;
        }

        /// <summary>撤销外部占用（未提交为货物时）。</summary>
        public void ReleaseClaim(string zoneKey, int ownerId)
        {
            if (_zoneOwner.TryGetValue(zoneKey, out var owner) && owner == ownerId && !_cargos.ContainsKey(ownerId))
            {
                _zoneOwner.Remove(zoneKey);
                if (_claimLocks.TryGetValue(ownerId, out var held))
                {
                    _claimLocks.Remove(ownerId);
                    foreach (var key in held)
                    {
                        if (_locks.TryGetValue(key, out var ls)) ls.Count = Math.Max(0, ls.Count - 1);
                    }
                }

                Dispatch();
            }
        }

        public bool IsZoneFree(string zoneKey) => !_zoneOwner.ContainsKey(zoneKey);

        private static double[] Fill(int n)
        {
            var a = new double[n];
            for (var i = 0; i < n; i++) a[i] = double.NaN;
            return a;
        }

        public bool IsSubmitted(int cargoId) => _cargos.ContainsKey(cargoId);

        public bool TryGetTimeline(int cargoId, out double[] moveStart, out double[] arrive, out double[] readyTime)
        {
            moveStart = arrive = readyTime = null;
            if (!_cargos.TryGetValue(cargoId, out var c)) return false;
            moveStart = c.MoveStart;
            arrive = c.Arrive;
            readyTime = c.ReadyTime;
            return true;
        }

        /// <summary>目标位置上的货物被取走（堆垛机叉起完成），释放目标位置及残留锁。</summary>
        public void ReleaseGoal(int cargoId, double now)
        {
            if (!_cargos.TryGetValue(cargoId, out var c) || c.Done) return;
            AdvanceTo(now);
            if (!c.AtGoal)
            {
                _violations.Add($"ReleaseGoal cargo={cargoId} 尚未到达目标 t={now:F2}");
                return;
            }

            FreeZone(c, c.Steps[c.Current].ZoneKey);
            foreach (var key in new List<string>(c.HeldLocks)) ReleaseLock(c, key);
            c.Done = true;
            _active.Remove(c);
            _cargos.Remove(cargoId);
            Dispatch();
        }

        public bool TryPeekNextEventTime(out double time)
        {
            time = double.PositiveInfinity;
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i].Time < time) time = _events[i].Time;
            }

            return _events.Count > 0;
        }

        public List<ZoneKernelOutput> DrainOutputs()
        {
            var list = new List<ZoneKernelOutput>(_outputs);
            _outputs.Clear();
            return list;
        }

        /// <summary>推进到 now：按时间顺序处理到达 / 锁释放 / 就绪，每个时刻处理后做派发。</summary>
        public void AdvanceTo(double now)
        {
            if (now < _now - Eps)
            {
                _violations.Add($"时间倒退 {now:F4} < {_now:F4}");
                now = _now;
            }

            while (true)
            {
                if (!TryPeekNextEventTime(out var t) || t > now + Eps)
                {
                    break;
                }

                _now = Math.Max(_now, t);
                // 同一时刻的事件全部处理后再派发（到达先于锁释放先于就绪）。
                var batch = new List<Ev>();
                for (var i = _events.Count - 1; i >= 0; i--)
                {
                    if (_events[i].Time <= t + Eps)
                    {
                        batch.Add(_events[i]);
                        _events.RemoveAt(i);
                    }
                }

                batch.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Seq.CompareTo(b.Seq));
                foreach (var ev in batch) Handle(ev);
                Dispatch();
            }

            _now = Math.Max(_now, now);
            Dispatch();
        }

        private void Handle(Ev ev)
        {
            if (!_cargos.TryGetValue(ev.CargoId, out var c)) return;
            switch (ev.Kind)
            {
                case EvKind.Arrive:
                {
                    c.Moving = false;
                    var k = c.Current;
                    c.Arrive[k] = _now;
                    var step = c.Steps[k];
                    c.ReadyAt = _now + Math.Max(0, step.MinDwellSeconds);
                    c.WaitingSince = c.ReadyAt;
                    foreach (var r in c.Releases)
                    {
                        if (r.StepIndex == k)
                        {
                            if (r.DelaySeconds <= Eps) ReleaseLock(c, r.Key);
                            else Push(_now + r.DelaySeconds, EvKind.LockRelease, c.Id, r.Key);
                        }
                    }

                    if (k == (c.Placed ? 1 : 0)) _outputs.Add(new ZoneKernelOutput { Kind = ZoneKernelOutputKind.OriginDeparted, CargoId = c.Id, Time = _now });
                    if (k == c.Steps.Length - 1)
                    {
                        c.AtGoal = true;
                        _outputs.Add(new ZoneKernelOutput { Kind = ZoneKernelOutputKind.GoalArrived, CargoId = c.Id, Time = _now });
                    }
                    else if (c.ReadyAt > _now + Eps)
                    {
                        Push(c.ReadyAt, EvKind.Ready, c.Id, null);
                    }

                    break;
                }
                case EvKind.LockRelease:
                    ReleaseLock(c, ev.Key);
                    break;
            }
        }

        private void Dispatch()
        {
            var progressed = true;
            var guard = 0;
            while (progressed && guard++ < 100000)
            {
                progressed = false;
                var candidates = new List<Cargo>();
                foreach (var c in _active)
                {
                    if (!c.Moving && !c.AtGoal && c.ReadyAt <= _now + Eps && c.Current + 1 < c.Steps.Length)
                    {
                        candidates.Add(c);
                    }
                }

                candidates.Sort((a, b) =>
                {
                    var w = a.WaitingSince.CompareTo(b.WaitingSince);
                    return w != 0 ? w : a.Seq.CompareTo(b.Seq);
                });

                foreach (var c in candidates)
                {
                    if (TryStartMove(c))
                    {
                        progressed = true;
                    }
                }

                // 零时长 hop：同一时刻立即到达，再派发一轮。
                for (var i = _events.Count - 1; i >= 0; i--)
                {
                    var ev = _events[i];
                    if (ev.Kind == EvKind.Arrive && ev.Time <= _now + Eps)
                    {
                        _events.RemoveAt(i);
                        Handle(ev);
                        progressed = true;
                    }
                }
            }
        }

        private bool TryStartMove(Cargo c)
        {
            var next = c.Current + 1;
            var step = c.Steps[next];
            if (_zoneOwner.TryGetValue(step.ZoneKey, out var owner) && owner != c.Id)
            {
                return false;
            }

            if (IsBlockedByNeighbour(step.ZoneKey, c.Id))
            {
                return false;
            }

            foreach (var claim in step.AcquireOnMoveIn)
            {
                if (c.HeldLocks.Contains(claim.Key)) continue;
                if (_locks.TryGetValue(claim.Key, out var ls) && ls.Count > 0)
                {
                    if (ls.Direction != claim.Direction)
                    {
                        RegisterLockWaiter(claim.Key, c, claim.Direction);
                        return false;
                    }

                    if (ls.Count >= Math.Max(1, claim.Capacity) || HasEarlierOppositeWaiter(claim.Key, c, claim.Direction))
                    {
                        return false;
                    }
                }
            }

            foreach (var claim in step.AcquireOnMoveIn)
            {
                if (_lockWaiters.TryGetValue(claim.Key, out var w)) w.Remove(c.Id);
            }

            foreach (var claim in step.AcquireOnMoveIn)
            {
                if (!c.HeldLocks.Add(claim.Key)) continue;
                if (!_locks.TryGetValue(claim.Key, out var ls))
                {
                    ls = new LockState();
                    _locks[claim.Key] = ls;
                }

                ls.Count++;
                ls.Direction = claim.Direction;
            }

            if (c.Current == 0 && c.OriginLocks != null)
            {
                foreach (var key in c.OriginLocks)
                {
                    if (!step.AcquireOnMoveIn.Exists(x => string.Equals(x.Key, key, StringComparison.Ordinal))) ReleaseLock(c, key);
                }

                c.OriginLocks = null;
            }

            if (c.Current >= 0) FreeZone(c, c.Steps[c.Current].ZoneKey);
            if (c.Current == 0) _outputs.Add(new ZoneKernelOutput { Kind = ZoneKernelOutputKind.StartStepLeft, CargoId = c.Id, Time = _now });
            _zoneOwner[step.ZoneKey] = c.Id;
            c.ReadyTime[next] = c.ReadyAt;
            c.MoveStart[next] = _now;
            c.Current = next;
            c.Moving = true;
            Push(_now + Math.Max(0, step.HopSeconds), EvKind.Arrive, c.Id, null);
            return true;
        }

        private void RegisterLockWaiter(string key, Cargo c, int direction)
        {
            if (!_lockWaiters.TryGetValue(key, out var w))
            {
                w = new Dictionary<int, (int Direction, double Since)>();
                _lockWaiters[key] = w;
            }

            if (!w.ContainsKey(c.Id)) w[c.Id] = (direction, c.WaitingSince);
        }

        private bool HasEarlierOppositeWaiter(string key, Cargo c, int direction)
        {
            if (!_lockWaiters.TryGetValue(key, out var w)) return false;
            foreach (var pair in w)
            {
                if (pair.Key != c.Id && pair.Value.Direction != direction && pair.Value.Since < c.WaitingSince - Eps)
                {
                    return true;
                }
            }

            return false;
        }

        private void FreeZone(Cargo c, string key)
        {
            if (_zoneOwner.TryGetValue(key, out var owner) && owner == c.Id)
            {
                _zoneOwner.Remove(key);
            }
            else
            {
                _violations.Add($"释放未持有位置 cargo={c.Id} zone={key}");
            }
        }

        private void ReleaseLock(Cargo c, string key)
        {
            if (!c.HeldLocks.Remove(key)) return;
            if (_locks.TryGetValue(key, out var ls))
            {
                ls.Count--;
                if (ls.Count < 0)
                {
                    _violations.Add($"锁计数为负 key={key}");
                    ls.Count = 0;
                }
            }
        }

        private void Push(double t, EvKind kind, int cargoId, string key)
        {
            _events.Add(new Ev { Time = t, Kind = kind, CargoId = cargoId, Key = key, Seq = _seq++ });
        }

        /// <summary>没有待处理事件却仍有未到目标的货物时，给出等待关系（疑似死锁）。</summary>
        public string DescribeStall()
        {
            var sb = new StringBuilder();
            foreach (var c in _active)
            {
                if (c.AtGoal || c.Moving) continue;
                var next = c.Current + 1;
                if (next >= c.Steps.Length) continue;
                var step = c.Steps[next];
                sb.Append($"cargo={c.Id} 在步{c.Current} 等 {step.ZoneKey}");
                if (_zoneOwner.TryGetValue(step.ZoneKey, out var o) && o != c.Id) sb.Append($"(被 {o} 占)");
                if (ZonesTooClose != null)
                {
                    foreach (var pair in _zoneOwner)
                    {
                        if (pair.Value != c.Id && !string.Equals(pair.Key, step.ZoneKey, StringComparison.Ordinal) && ZonesTooClose(step.ZoneKey, pair.Key))
                        {
                            sb.Append($" 车距<{pair.Key}@{pair.Value}>");
                        }
                    }
                }
                foreach (var claim in step.AcquireOnMoveIn)
                {
                    if (!c.HeldLocks.Contains(claim.Key) && _locks.TryGetValue(claim.Key, out var ls) && ls.Count > 0
                        && (ls.Direction != claim.Direction || ls.Count >= claim.Capacity))
                    {
                        sb.Append($" 锁{claim.Key}(n={ls.Count},dir={ls.Direction})");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
