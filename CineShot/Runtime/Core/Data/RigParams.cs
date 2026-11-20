using System;
using UnityEngine;

namespace CineShot.Core
{
    [Serializable]
    public struct RigParams
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 C;
        public float P0;
        public float P1;
        public float P2;
        public float P3;
        public int I0;
        public int I1;

        public static RigParams DefaultFor(RigKind kind)
        {
            var p = new RigParams();
            switch (kind)
            {
                case RigKind.Static:
                    p.A = new Vector3(0f, 1.6f, -3.5f);
                    break;
                case RigKind.DollyTrack:
                    p.A = new Vector3(-4f, 1.6f, -3f);
                    p.B = new Vector3(4f, 1.6f, -3f);
                    p.P0 = 0.5f;
                    p.P1 = 0.8f;
                    p.P2 = 0f;
                    break;
                case RigKind.Orbit:
                    p.P0 = 45f;
                    p.P1 = 24f;
                    p.P2 = 4f;
                    p.P3 = 1.2f;
                    p.A = new Vector3(40f, 0f, 0f);
                    break;
                case RigKind.PushPull:
                    p.P0 = 6f;
                    p.P1 = 2.4f;
                    p.P2 = 1.6f;
                    p.P3 = 1f;
                    p.A = new Vector3(34f, 0f, 0f);
                    break;
                case RigKind.Crane:
                    p.A = new Vector3(0f, 1.6f, 0f);
                    p.B = new Vector3(0f, 3.2f, -4f);
                    p.P0 = 2f;
                    break;
                case RigKind.Shoulder:
                    p.I0 = 1;
                    p.P0 = 0.35f;
                    p.P1 = 1.45f;
                    p.P2 = 0f;
                    p.P3 = 0f;
                    break;
                case RigKind.Handheld:
                    p.A = new Vector3(0f, 1.6f, -3.5f);
                    p.P0 = 0.12f;
                    p.P1 = 1.6f;
                    p.P2 = 1.2f;
                    p.P3 = 0.6f;
                    break;
                case RigKind.SplineDolly:
                    p.P0 = 0.5f;
                    p.P1 = 1f;
                    p.P2 = 0f;
                    p.I0 = 4;
                    break;
                case RigKind.Pan:
                    p.A = new Vector3(0f, 1.6f, -4f);
                    p.B = new Vector3(-2.5f, 1.5f, 0f);
                    p.C = new Vector3(2.5f, 1.5f, 0f);
                    p.P1 = 0.35f;
                    break;
            }
            return p;
        }
    }

    [Serializable]
    public struct TargetBinding
    {
        public Vector3 lookAtOffset;
        public Vector3 followOffset;

        public static TargetBinding LookAtDefault
        {
            get
            {
                return new TargetBinding
                {
                    lookAtOffset = new Vector3(0f, 1.6f, 0f),
                    followOffset = Vector3.zero
                };
            }
        }
    }

    [Serializable]
    public struct HandheldLayer
    {
        public bool enabled;
        public RigParams parameters;
    }

    [Serializable]
    public struct MatchPoint
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fov;
        public bool valid;
    }
}
