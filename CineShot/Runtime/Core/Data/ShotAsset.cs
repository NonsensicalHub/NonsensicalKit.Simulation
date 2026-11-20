using UnityEngine;

namespace CineShot.Core
{
    [CreateAssetMenu(menuName = "CineShot/镜头资产", fileName = "Shot_")]
    public class ShotAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] string m_Id;
        public string displayName;
        public RigKind rigKind = RigKind.Static;
        public RigParams rigParams;
        public float defaultDuration = 4f;
        public TargetBinding targetBinding = TargetBinding.LookAtDefault;
        public Vector3 compositionOffset;
        public float fov = 40f;
        public float nearClip = 0.1f;
        public float farClip = 1000f;
        public FocusMode focusMode = FocusMode.Target;
        public float focusDistance = 10f;
        public FocusSettings focusSettings = FocusSettings.Default;
        public EasingAsset easeIn;
        public EasingAsset easeOut;
        public int priority;
        public string[] tags;
        public HandheldLayer handheld;
        public MatchPoint matchPoint;
        public string parentGuid;

        public string Id { get { return m_Id; } }

        public int Seed
        {
            get
            {
                if (string.IsNullOrEmpty(m_Id))
                    return GetInstanceID();
                unchecked
                {
                    int h = 17;
                    for (int i = 0; i < m_Id.Length; i++)
                        h = h * 31 + m_Id[i];
                    return h;
                }
            }
        }

        void OnValidate()
        {
            EnsureId();
            defaultDuration = Mathf.Clamp(defaultDuration, 0.1f, 120f);
            fov = Mathf.Clamp(fov, 12f, 100f);
            nearClip = Mathf.Clamp(nearClip, 0.01f, 1f);
            farClip = Mathf.Max(farClip, nearClip + 0.1f);
            focusDistance = Mathf.Clamp(focusDistance, 0.1f, 200f);
            focusSettings.Sanitize();
            if (string.IsNullOrEmpty(displayName))
                displayName = name;
        }

        void Reset()
        {
            EnsureId();
            displayName = name;
            rigParams = RigParams.DefaultFor(rigKind);
            targetBinding = TargetBinding.LookAtDefault;
            fov = 40f;
            nearClip = 0.1f;
            farClip = 1000f;
            focusDistance = 10f;
            focusSettings = FocusSettings.Default;
            defaultDuration = 4f;
        }

        void EnsureId()
        {
            if (string.IsNullOrEmpty(m_Id))
                m_Id = System.Guid.NewGuid().ToString("N");
        }
    }

    [System.Serializable]
    public struct ShotOverride
    {
        public ShotOverrideMask mask;
        public RigParams rigParams;
        public Vector3 compositionOffset;
        public float fov;
        public float nearClip;
        public float farClip;
        public FocusMode focusMode;
        public float focusDistance;
        public FocusSettings focusSettings;
        public EasingAsset easeIn;
        public EasingAsset easeOut;
        public int priority;
        public Vector3 lookAtOffset;
        public Vector3 followOffset;
        public HandheldLayer handheld;
        public MatchPoint matchPoint;

        public Vector3 ResolveLookOffset(ShotAsset source)
        {
            if (source == null || (mask & ShotOverrideMask.TargetOffset) != 0)
                return lookAtOffset;
            return source.targetBinding.lookAtOffset;
        }

        public Vector3 ResolveFollowOffset(ShotAsset source)
        {
            if (source == null || (mask & ShotOverrideMask.TargetOffset) != 0)
                return followOffset;
            return source.targetBinding.followOffset;
        }

        public HandheldLayer ResolveHandheld(ShotAsset source)
        {
            if (source == null)
                return default(HandheldLayer);
            if ((mask & ShotOverrideMask.Handheld) != 0)
                return handheld;
            return source.handheld;
        }

        public int ResolvePriority(ShotAsset source)
        {
            if (source == null)
                return priority;
            if ((mask & ShotOverrideMask.PriorityTags) != 0)
                return priority;
            return source.priority;
        }

        public MatchPoint ResolveMatchPoint(ShotAsset source)
        {
            if (source == null || (mask & ShotOverrideMask.MatchPoint) != 0)
                return matchPoint;
            return source.matchPoint;
        }

        public void ApplyTo(ShotAsset source, out RigParams rig, out Vector3 composition,
            out float outFov, out float outNear, out float outFar,
            out FocusMode outFocusMode, out float outFocusDistance, out FocusSettings outFocusSettings)
        {
            rig = (mask & ShotOverrideMask.RigParams) != 0 ? rigParams : source.rigParams;
            composition = (mask & ShotOverrideMask.Composition) != 0 ? compositionOffset : source.compositionOffset;
            outFov = (mask & ShotOverrideMask.Optics) != 0 ? fov : source.fov;
            outNear = (mask & ShotOverrideMask.Optics) != 0 ? nearClip : source.nearClip;
            outFar = (mask & ShotOverrideMask.Optics) != 0 ? farClip : source.farClip;
            bool focusMasked = (mask & ShotOverrideMask.Focus) != 0;
            outFocusMode = focusMasked ? focusMode : source.focusMode;
            outFocusDistance = focusMasked ? focusDistance : source.focusDistance;
            outFocusSettings = focusMasked ? focusSettings : source.focusSettings;
            outFocusSettings.Sanitize();
        }
    }
}
