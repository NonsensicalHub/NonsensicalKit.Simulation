using UnityEngine;

namespace CineShot.Core
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CineShotRig : MonoBehaviour
    {
        [SerializeField] Component m_OutputVcam;
        [SerializeField] Component m_GameVcam;
        [SerializeField] float m_MinTargetDistance = 0.35f;

        readonly FocusSolver m_FocusSolver = new FocusSolver();
        readonly FocusRequest[] m_FocusBuffer = new FocusRequest[8];
        ICameraOutput m_Output = NullCameraOutput.Instance;
        IFocusOutput m_FocusOutput = NullFocusOutput.Instance;
        CameraPose m_LastPose = CameraPose.Identity;
        bool m_HasPose;
        struct SampleSlot
        {
            public int Id;
            public int Key;
            public Vector3 Last;
            public Vector3 Velocity;
            public int Frame;
            public bool Has;
        }

        ShotAsset m_ActiveShot;
        readonly SampleSlot[] m_LookSlots = new SampleSlot[8];
        readonly SampleSlot[] m_FollowSlots = new SampleSlot[8];

        public ICameraOutput Output { get { return m_Output; } }
        public CameraPose LastPose { get { return m_LastPose; } }
        public bool HasPose { get { return m_HasPose; } }
        public ShotAsset ActiveShot { get { return m_ActiveShot; } }
        public Component OutputVcam { get { return m_OutputVcam; } }
        public Component GameVcam { get { return m_GameVcam; } }

        public void SetActiveShot(ShotAsset shot)
        {
            m_ActiveShot = shot;
        }

        void OnEnable()
        {
            RebuildOutputs();
            m_FocusSolver.Reset(10f);
        }

        void OnDisable()
        {
            if (m_FocusOutput != null)
            {
                m_FocusOutput.Dispose();
                m_FocusOutput = NullFocusOutput.Instance;
            }
            m_Output = NullCameraOutput.Instance;
            m_HasPose = false;
            m_ActiveShot = null;
            System.Array.Clear(m_LookSlots, 0, m_LookSlots.Length);
            System.Array.Clear(m_FollowSlots, 0, m_FollowSlots.Length);
        }

        public void RebuildOutputs()
        {
            m_Output = CineOutputFactory.GetCameraOutput(m_OutputVcam);
            if (m_FocusOutput != null && !ReferenceEquals(m_FocusOutput, NullFocusOutput.Instance))
                m_FocusOutput.Dispose();
            m_FocusOutput = CineOutputFactory.GetFocusOutput(transform);
        }

        public TargetSample SampleLookAt(Transform target, Vector3 offset, float dt)
        {
            if (target == null)
                return default(TargetSample);
            return SampleTracked(target, offset, dt, m_LookSlots);
        }

        public TargetSample SampleFollow(Transform target, Vector3 offset, float dt)
        {
            if (target == null)
                return default(TargetSample);
            return SampleTracked(target, offset, dt, m_FollowSlots);
        }

        static TargetSample SampleTracked(Transform target, Vector3 offset, float dt, SampleSlot[] slots)
        {
            Vector3 pos = target.TransformPoint(offset);
            int id = target.GetInstanceID();
            int key = OffsetKey(offset);
            int frame = Time.frameCount;
            int slot = -1;
            int free = -1;
            int oldest = 0;
            int oldestFrame = int.MaxValue;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Has && slots[i].Id == id && slots[i].Key == key)
                {
                    slot = i;
                    break;
                }
                if (!slots[i].Has && free < 0)
                    free = i;
                if (slots[i].Frame < oldestFrame)
                {
                    oldestFrame = slots[i].Frame;
                    oldest = i;
                }
            }
            if (slot < 0)
                slot = free >= 0 ? free : oldest;

            var mem = slots[slot];
            Vector3 vel;
            bool same = mem.Has && mem.Id == id && mem.Key == key;
            if (same && mem.Frame == frame)
            {
                // 同一帧里多条镜头会各采样一次；第二次不能把本帧位移当成目标速度
                vel = mem.Velocity;
            }
            else if (!same || dt <= 1e-3f)
            {
                // dt≈0 的那次采样不能把速度清零，否则前馈会一帧开、一帧关
                vel = same ? mem.Velocity : Vector3.zero;
                if (!same)
                {
                    mem.Id = id;
                    mem.Key = key;
                    mem.Last = pos;
                    mem.Velocity = vel;
                    mem.Frame = frame;
                    mem.Has = true;
                    slots[slot] = mem;
                }
            }
            else
            {
                vel = (pos - mem.Last) / dt;
                float maxSpeed = 12f;
                float sqr = vel.sqrMagnitude;
                if (sqr > maxSpeed * maxSpeed)
                    vel *= maxSpeed / Mathf.Sqrt(sqr);
                float blend = 1f - Mathf.Exp(-dt / 0.12f);
                vel = Vector3.Lerp(mem.Velocity, vel, blend);
                mem.Id = id;
                mem.Key = key;
                mem.Last = pos;
                mem.Velocity = vel;
                mem.Frame = frame;
                mem.Has = true;
                slots[slot] = mem;
            }

            return new TargetSample
            {
                Position = pos,
                Velocity = vel,
                Rotation = target.rotation,
                Valid = true
            };
        }

        static int OffsetKey(Vector3 offset)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(offset.x * 1000f);
                h = h * 31 + Mathf.RoundToInt(offset.y * 1000f);
                h = h * 31 + Mathf.RoundToInt(offset.z * 1000f);
                return h;
            }
        }

        public void SubmitPose(in CameraPose pose, float dt, float focusDistanceHint, FocusMode focusMode, Transform focusTarget, in FocusSettings focusSettings, Vector3 aimPoint, bool hasAim)
        {
            CameraPose finalPose = pose;
            SolverMath.ClampOptics(ref finalPose);
            if (finalPose.Rotation == default(Quaternion))
                finalPose.Rotation = Quaternion.identity;

            TargetSample protect = default(TargetSample);
            if (hasAim)
            {
                protect.Position = aimPoint;
                protect.Valid = true;
            }
            else if (focusTarget != null)
            {
                protect.Position = focusTarget.position;
                protect.Valid = true;
            }
            Vector3 beforeProtect = finalPose.Position;
            SolverMath.KeepMinDistance(ref finalPose, in protect, m_MinTargetDistance);
            // 仅在被推开后重注视，避免抹掉求解器里的偏航/俯仰/手持瞄偏
            if (protect.Valid && (finalPose.Position - beforeProtect).sqrMagnitude > 1e-8f)
            {
                Vector3 fwd = protect.Position - finalPose.Position;
                if (fwd.sqrMagnitude > 1e-6f)
                {
                    float dutch = finalPose.Dutch;
                    finalPose.Rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                    finalPose.Dutch = dutch;
                }
            }

            m_LastPose = finalPose;
            m_HasPose = true;
            m_Output.Apply(in finalPose);

            var settings = focusSettings;
            settings.Sanitize();
            int count = 0;
            if (focusMode == FocusMode.FixedDistance)
            {
                m_FocusBuffer[count++] = new FocusRequest
                {
                    Distance = focusDistanceHint,
                    Weight = 1f,
                    SourceId = 1
                };
            }
            else if (focusMode == FocusMode.Target && protect.Valid)
            {
                m_FocusBuffer[count++] = new FocusRequest
                {
                    Distance = Mathf.Max(0.1f, Vector3.Distance(finalPose.Position, protect.Position)),
                    Weight = 1f,
                    SourceId = 2
                };
            }
            else if (focusMode == FocusMode.Hyperfocal)
            {
                m_FocusBuffer[count++] = new FocusRequest
                {
                    Distance = settings.HyperfocalDistance,
                    Weight = 1f,
                    SourceId = 3
                };
            }

            var state = m_FocusSolver.Evaluate(m_FocusBuffer, count, focusDistanceHint, dt, in settings);
            finalPose.FocusDistance = state.Distance;
            m_LastPose = finalPose;
            m_FocusOutput.Apply(in state, in settings);
        }

        public void BlendToGameCamera(float k)
        {
            if (m_GameVcam == null || !m_HasPose)
                return;
            Transform gameTr = m_GameVcam.transform;
            CameraPose blended = m_LastPose;
            blended.Position = Vector3.Lerp(m_LastPose.Position, gameTr.position, k);
            blended.Rotation = Quaternion.Slerp(m_LastPose.Rotation, gameTr.rotation, k);
            m_Output.Apply(in blended);
            m_LastPose = blended;
        }

        public void SetControlling(bool controlling)
        {
            m_Output.SetControlling(controlling);
        }
    }
}
