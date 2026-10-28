using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        private void AttachCargoByJob(int jobId)
        {
            if (_cargoRegistry == null)
            {
                return;
            }

            // TryGet 优先：避免 Acquire 反复 EnsureDetached 把已挂在叉上的货扯掉。
            if (!_cargoRegistry.TryGet(jobId, out var cargo) || cargo == null)
            {
                cargo = _cargoRegistry.Acquire(jobId);
            }

            if (cargo == null)
            {
                return;
            }

            var mount = ResolveCargoMount();
            if (mount == null)
            {
                return;
            }

            cargo.gameObject.SetActive(true);
            _cargoRegistry.UnparkJob(jobId);
            // 挂载点局部原点跟货叉走。堆垛机根节点把 Y/Z 对调过，局部旋转用 identity 会把料箱放倒。
            AttachCargoUpright(cargo, mount);
        }

        private void ReleaseCargoByJob(int jobId) => _cargoRegistry?.Release(jobId);

        /// <summary>
        /// 取货提交前料箱还不在货叉上。回退到等待/驶近时要撤掉已经挂上的料箱。
        /// </summary>
        private void ReconcileCargoBeforePick(in StackerJobPlaybackTask task)
        {
            if (task.IsOutbound)
            {
                _cargoRegistry?.UnparkJob(task.JobId);
                ReleaseCargoByJob(task.JobId);
            }
            else
            {
                _cargoRegistry?.UnparkJob(task.JobId);
                RelinquishForkHold(task.JobId);
            }
        }

        /// <summary>
        /// 取货交接以时间轴上的交接时刻为准。
        /// 出库交接前货位由货架渲染；入库交接前料箱留给输送线，只在仍挂在本货叉上时解开。
        /// </summary>
        private void ReconcilePickCargo(in StackerJobPlaybackTask task, in SimSubTask pickSubTask)
        {
            if (!StackerCargoHandover.HasTransferred(in pickSubTask, _logSimTime))
            {
                ReconcileCargoBeforePick(in task);
                return;
            }

            AttachCargoByJob(task.JobId);
        }

        /// <summary>
        /// 放货交接后：入库按货位坐标停放；出库若还挂在本货叉上，解到放货姿态，留给输送线。
        /// </summary>
        private void ReconcilePlaceCargo(in StackerJobPlaybackTask task, in SimSubTask placeSubTask)
        {
            BeginPlaceCargoDiag(task.JobId);
            var committed = StackerCargoHandover.HasTransferred(in placeSubTask, _logSimTime);
            if (!committed)
            {
                _placeDiagCommitted = false;
                LogPlaceCargoStarted(in task, placeSubTask.NormalizedProgress(_logSimTime), in placeSubTask);
                AttachCargoByJob(task.JobId);
                return;
            }

            if (task.IsOutbound)
            {
                ReleaseForkHoldAtPose(task.JobId, placeSubTask.HasStackerPose ? placeSubTask.StackerToSlot : task.TargetSlot);
            }
            else
            {
                var visual = placeSubTask.HasStackerPose ? placeSubTask.StackerToSlot : task.TargetSlot;
                PlaceCargoAtSlotByJob(task.JobId, task.TargetSlot, visual);
                LogPlaceCargoCommitted("放货提交", in task, visual, in placeSubTask, placeSubTask.HasStackerPose);
            }

            if (task.IsOutbound)
            {
                LogPlaceCargoCommitted("出库放货", in task, placeSubTask.StackerToSlot, in placeSubTask, placeSubTask.HasStackerPose);
            }
        }

        private void BeginPlaceCargoDiag(int jobId)
        {
            if (_placeDiagJobId == jobId)
            {
                return;
            }

            _placeDiagJobId = jobId;
            _placeDiagStarted = false;
            _placeDiagCommitted = false;
        }

        private void LogPlaceCargoStarted(in StackerJobPlaybackTask task, float progress, in SimSubTask placeSubTask)
        {
            if (_placeDiagStarted)
            {
                return;
            }

            _placeDiagStarted = true;
            SimPlaybackLog.CargoSync(
                $"放货开始 t={_logSimTime:F3} stacker={m_stackerId} job={task.JobId} 出库={task.IsOutbound} " +
                $"progress={progress:F3} 占用货位={task.TargetSlot} 放货姿态={placeSubTask.StackerToSlot} " +
                $"起点={placeSubTask.StackerFromSlot} rail={placeSubTask.StackerRailColumn} " +
                $"段={placeSubTask.StartSimTime:F3}-{placeSubTask.EndSimTime:F3} 货叉={FormatForkWorld()} " +
                DescribePositionIndex(),
                this);
        }

        private void LogPlaceCargoCommitted(
            string phase,
            in StackerJobPlaybackTask task,
            GridIndex visualSlot,
            in SimSubTask placeSubTask,
            bool hasPose)
        {
            if (_placeDiagJobId != task.JobId)
            {
                _placeDiagJobId = task.JobId;
                _placeDiagStarted = false;
                _placeDiagCommitted = false;
            }

            if (_placeDiagCommitted)
            {
                return;
            }

            _placeDiagCommitted = true;
            var cargoWorld = "无料箱";
            var forkCargoDelta = "无";
            var mount = ResolveCargoMount();
            if (_cargoRegistry != null && _cargoRegistry.TryGet(task.JobId, out var cargo) && cargo != null)
            {
                cargoWorld = cargo.position.ToString("F2");
                if (mount != null)
                {
                    forkCargoDelta = Vector3.Distance(mount.position, cargo.position).ToString("F2");
                }
            }

            var visualWorld = DescribeWorld(visualSlot);
            var occupancyWorld = DescribeWorld(task.TargetSlot);
            var pose = hasPose
                ? $"起点={placeSubTask.StackerFromSlot} 终点={placeSubTask.StackerToSlot} rail={placeSubTask.StackerRailColumn} 段={placeSubTask.StartSimTime:F3}-{placeSubTask.EndSimTime:F3}"
                : "无放货姿态";
            var side = "同列";
            if (placeSubTask.HasStackerPose)
            {
                var rail = placeSubTask.StackerRailColumn;
                var toCol = placeSubTask.StackerToSlot.Column;
                if (toCol < rail)
                {
                    side = "左";
                }
                else if (toCol > rail)
                {
                    side = "右";
                }
            }

            SimPlaybackLog.CargoSync(
                $"{phase} t={_logSimTime:F3} stacker={m_stackerId} job={task.JobId} 出库={task.IsOutbound} " +
                $"侧={side} 占用货位={task.TargetSlot}({occupancyWorld}) 显示货位={visualSlot}({visualWorld}) {pose} " +
                $"货叉={FormatForkWorld()} 料箱={cargoWorld} 叉货距={forkCargoDelta} 存储货位={IsStorageSlot(task.TargetSlot)} " +
                $"已停放={_cargoRegistry != null && _cargoRegistry.IsJobParked(task.JobId)} {DescribePositionIndex()}",
                this);
        }

        private string FormatForkWorld()
        {
            var mount = ResolveCargoMount();
            return mount != null ? mount.position.ToString("F2") : "无挂点";
        }

        private string DescribeWorld(GridIndex slot)
        {
            if (_positionIndex == null)
            {
                return "无索引";
            }

            return _positionIndex.TryGetWorldPosition(slot, out var world)
                ? world.ToString("F2")
                : "查不到坐标";
        }

        private string DescribePositionIndex()
        {
            if (_positionIndex == null)
            {
                return "索引=无";
            }

            var manager = _positionIndex.BoundManager != null ? _positionIndex.BoundManager.name : "无管理器";
            return $"索引仓库={_positionIndex.WarehouseId} 管理器={manager}";
        }

        /// <summary>货物不该在货叉上，且当前确实挂在本机挂载点时才解开。已经交给输送线或货位的不动。</summary>
        private void RelinquishForkHold(int jobId)
        {
            if (_cargoRegistry == null || !_cargoRegistry.TryGet(jobId, out var cargo) || cargo == null)
            {
                return;
            }

            var mount = ResolveCargoMount();
            if (mount == null || cargo.parent != mount)
            {
                return;
            }

            cargo.SetParent(null, true);
        }

        /// <summary>出库放货交接：只在仍挂着本货叉时，放到记录的交接姿态，而不是收叉后的世界坐标。</summary>
        private void ReleaseForkHoldAtPose(int jobId, GridIndex pose)
        {
            if (_cargoRegistry == null || !_cargoRegistry.TryGet(jobId, out var cargo) || cargo == null)
            {
                return;
            }

            var mount = ResolveCargoMount();
            if (mount == null || cargo.parent != mount)
            {
                return;
            }

            if (_positionIndex != null && _positionIndex.TryGetWorldPosition(pose, out var world))
            {
                PlaceCargoUpright(cargo, world);
            }
            else
            {
                cargo.SetParent(null, false);
                cargo.rotation = UprightCargoRotation();
            }

            cargo.gameObject.SetActive(true);
        }

        private void PlaceCargoAtSlotByJob(int jobId, GridIndex occupancySlot) =>
            PlaceCargoAtSlotByJob(jobId, occupancySlot, occupancySlot);

        /// <summary>
        /// 伸叉提交帧被跳过时，把已经到期的入库料箱补停到货位上。
        /// 已经停着的不动。调用方要先确认该货位在当前时刻仍然占用。
        /// </summary>
        public void EnsureInboundCargoParked(
            int jobId,
            GridIndex occupancySlot,
            GridIndex visualSlot,
            in SimSubTask placeSubTask,
            double simTime)
        {
            if (jobId < 0 || (_cargoRegistry != null && _cargoRegistry.IsJobParked(jobId)))
            {
                return;
            }

            _logSimTime = simTime;
            PlaceCargoAtSlotByJob(jobId, occupancySlot, visualSlot);
            var task = new StackerJobPlaybackTask
            {
                JobId = jobId,
                StackerId = m_stackerId,
                TargetSlot = occupancySlot,
                IsOutbound = false,
            };
            LogPlaceCargoCommitted("放货补交", in task, visualSlot, in placeSubTask, placeSubTask.HasStackerPose);
        }

        /// <summary>
        /// 入库放货：料箱停在货叉尖，不回收、不交给货架 GPU。
        /// 货叉关节只允许相对巷道中心伸缩；若再让货叉去追货位索引的绝对列坐标，会整段灌进子节点乱飞。
        /// 若料箱去追索引世界坐标、货叉留在场景巷道，二者一偏（常见于一号库摆放与索引差一截）就不同步。
        /// 所以运动跟货叉走，货跟货叉尖走；占用号仍记 occupancySlot，供快照/GPU 跳过叠画。
        /// </summary>
        private void PlaceCargoAtSlotByJob(int jobId, GridIndex occupancySlot, GridIndex visualSlot)
        {
            if (_cargoRegistry == null)
            {
                return;
            }

            if (!IsStorageSlot(occupancySlot))
            {
                if (!_cargoRegistry.TryGet(jobId, out var loose) || loose == null)
                {
                    return;
                }

                PlaceInboundCargoAtDeliveryPose(loose, visualSlot, occupancySlot);
                loose.gameObject.SetActive(true);
                _cargoRegistry.ParkJob(jobId, occupancySlot);
                return;
            }

            var cargo = _cargoRegistry.Acquire(jobId);
            if (cargo == null)
            {
                return;
            }

            PlaceInboundCargoAtDeliveryPose(cargo, visualSlot, occupancySlot);
            cargo.gameObject.SetActive(true);
            _cargoRegistry.ParkJob(jobId, occupancySlot);
        }

        /// <summary>
        /// 交接时货还在挂载点上 → 就地放下（与伸叉尖一致）。
        /// 倍速补交时货往往已不在叉上、货叉也可能已收回 → 只能用货位索引坐标。
        /// </summary>
        private void PlaceInboundCargoAtDeliveryPose(Transform cargo, GridIndex visualSlot, GridIndex occupancySlot)
        {
            var mount = ResolveCargoMount();
            if (mount != null && cargo.parent == mount)
            {
                PlaceCargoUpright(cargo, mount.position);
                return;
            }

            if (_positionIndex != null
                && (_positionIndex.TryGetWorldPosition(visualSlot, out var world)
                    || _positionIndex.TryGetWorldPosition(occupancySlot, out world)))
            {
                PlaceCargoUpright(cargo, world);
                return;
            }

            if (mount != null)
            {
                PlaceCargoUpright(cargo, mount.position);
                return;
            }

            cargo.SetParent(null, false);
            cargo.rotation = UprightCargoRotation();
        }

        /// <summary>
        /// 料箱跟货架 GPU 用同一套仓库旋转。堆垛机根节点把局部 Y 转到世界 Z，
        /// 跟挂载点用同一个局部旋转会侧倒；放到货架上若沿用这个世界旋转，货架上也是倒的。
        /// </summary>
        private Quaternion UprightCargoRotation()
        {
            var manager = _positionIndex != null ? _positionIndex.BoundManager : null;
            return manager != null ? manager.transform.rotation : Quaternion.identity;
        }

        private void AttachCargoUpright(Transform cargo, Transform mount)
        {
            var upright = UprightCargoRotation();
            cargo.SetParent(mount, false);
            cargo.localPosition = Vector3.zero;
            cargo.localRotation = Quaternion.Inverse(mount.rotation) * upright;
        }

        private void PlaceCargoUpright(Transform cargo, Vector3 world)
        {
            cargo.SetParent(null, false);
            cargo.SetPositionAndRotation(world, UprightCargoRotation());
        }

        /// <summary>挂载点必须是货叉轴自身或其子节点。缺失或挂错时拒绝挂货，不再退到排向轴。</summary>
        private Transform ResolveCargoMount()
        {
            if (!EnsureDriver() || m_transformDriver.ForkAxis == null)
            {
                LogMountErrorOnce("货叉轴未配置");
                return null;
            }

            var fork = m_transformDriver.ForkAxis;
            if (m_cargoMount == null)
            {
                LogMountErrorOnce("未配置料箱挂载点");
                return null;
            }

            if (m_cargoMount != fork && !m_cargoMount.IsChildOf(fork))
            {
                LogMountErrorOnce("料箱挂载点不在货叉轴下");
                return null;
            }

            return m_cargoMount;
        }

        private void LogMountErrorOnce(string reason)
        {
            if (_loggedMountError)
            {
                return;
            }

            _loggedMountError = true;
            SimPlaybackLog.Warn(
                $"JointStackerRig 堆垛机 {m_stackerId} {reason}，本机不挂载料箱。",
                this);
        }

        private bool IsStorageSlot(GridIndex slot) =>
            _grid == null
            || SlotGridUtility.IsStorageSlot(_grid, slot.Level, slot.Column, slot.Row, _exclusionZones);
    }
}
