using NonsensicalKit.Tools;
using System;
using UnityEngine;

namespace NonsensicalKit.Temp.MeshKit
{
    public class Leg : MonoBehaviour
    {
        [SerializeField] private float m_length = 1.5f;
        [SerializeField] private float m_height = 0.5f;
        [SerializeField][Range(0, 1)] private float m_amplitude = 0.5f;

        public Vector3 Point { get; set; }
        private MeshFilter _mf;
        private MeshRenderer _mr;

        private float _offset;

        MeshBuilderPlus meshbuffer;
        private void Awake()
        {
            _mr = GetComponent<MeshRenderer>();
            _mf = GetComponent<MeshFilter>();
            _offset = UnityEngine.Random.Range(0, 1f);
        }

        Vector3 _groundPoint;

        private void Update()
        {
            _groundPoint = transform.position;
            _groundPoint.y = Point.y;
            if (Vector3.Distance(_groundPoint, Point) > m_length)
            {
                _mr.enabled = false;
                enabled = false;
            }
            else
            {
                UpdateBezierCurvePlus(_mf.mesh, transform.rotation, Vector3.zero,
                        -transform.up * (Mathf.Abs(Mathf.Sin(Time.time * 0.3f + _offset)) * m_amplitude + m_height),
                         (Point + transform.up * (Mathf.Abs(Mathf.Sin(Time.time * 0.4f - _offset)) * m_amplitude + m_height)) - transform.position,
                         Point - transform.position,
                        0.1f);

                _mr.enabled = true;
            }
        }

        public void SetPoint(Vector3 newPoint)
        {
            Point = newPoint;
            _groundPoint = transform.position;
            _groundPoint.y = Point.y;
            if (Vector3.Distance(_groundPoint, Point) < m_length)
            {
                enabled = true;
            }
        }
        Vector3[] v3a1;
        Vector3[] v3a2;
        private void UpdateBezierCurvePlus(Mesh mesh, Quaternion rotate, Vector3 start, Vector3 p1, Vector3 p2, Vector3 end, float radius, int segmentNum = 16, int smoothness = 16)
        {
            rotate = Quaternion.Inverse(rotate);
            GetThreePowerBeizerListWithSlope(rotate * start, rotate * p1, rotate * p2, rotate * end, segmentNum);

            var point = v3a1;
            var slopes = v3a2;

            if (meshbuffer == null)
            {
                meshbuffer = new MeshBuilderPlus();
                meshbuffer.Vertices = new Vector3[2 * 3 * smoothness + (point.Length - 1) * smoothness * 4];
                meshbuffer.Normals = new Vector3[2 * 3 * smoothness + (point.Length - 1) * smoothness * 4];
                meshbuffer.UV = new Vector2[2 * 3 * smoothness + (point.Length - 1) * smoothness * 4];
                meshbuffer.Triangles = new int[2 * 3 * smoothness + (point.Length - 1) * smoothness * 6];
            }

            int vIndex = 0;
            int tIndex = 0;

            meshbuffer.AddRoundPlus(rotate * start, radius, slopes[0], smoothness, vIndex, tIndex);
            vIndex += 3 * smoothness;
            tIndex += 3 * smoothness;
            for (int i = 0; i < point.Length - 1; i++)
            {
                meshbuffer.AddRing3D_2(point[i], radius, slopes[i], point[i + 1], radius, slopes[i + 1], smoothness, vIndex, tIndex);
                vIndex += smoothness * 4;
                tIndex += smoothness * 6;
            }
            meshbuffer.AddRoundPlus(rotate * end, radius, -slopes[segmentNum - 1], smoothness, vIndex, tIndex);

            meshbuffer.Update(mesh);
        }
        Vector3[] path = new Vector3[0];
        Vector3[] slopes = new Vector3[0];
        Vector3 pixel;
        Vector3 slope;
        /// <summary>
        /// 获取三次贝塞尔曲线点和斜率
        /// </summary>
        /// <param name="startPoint"></param>
        /// <param name="controlPoint1"></param>
        /// <param name="controlPoint2"></param>
        /// <param name="endPoint"></param>
        /// <param name="segmentNum">采样点的数量，包含起点终点</param>
        /// <returns></returns>
        private void GetThreePowerBeizerListWithSlope(Vector3 startPoint, Vector3 controlPoint1, Vector3 controlPoint2, Vector3 endPoint, int segmentNum)
        {
            if (path.Length != segmentNum)
            {
                path = new Vector3[segmentNum];
                slopes = new Vector3[segmentNum];
            }
            for (int i = 0; i < segmentNum; i++)
            {
                float t = i / ((float)segmentNum - 1);
                pixel = CalculateThreePowerBezierPoint(t, startPoint,
                   controlPoint1, controlPoint2, endPoint);
                slope = CalculateThreePowerBezierDerivative(t, startPoint,
                   controlPoint1, controlPoint2, endPoint);
                path[i] = pixel;
                slopes[i] = slope;
            }
            v3a1 = path;
            v3a2 = slopes;
        }

        Vector3 point1;
        /// <summary>
        /// 三次贝塞尔曲线，根据T值，计算贝塞尔曲线上面相对应的点
        /// </summary>
        /// <param name="t">插量值</param>
        /// <param name="p0">起点</param>
        /// <param name="p1">控制点1</param>
        /// <param name="p2">控制点2</param>
        /// <param name="p3">终点</param>
        /// <returns></returns>
        private Vector3 CalculateThreePowerBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float ttt = tt * t;
            float uuu = uu * u;

            point1 = uuu * p0;
            point1 += 3 * t * uu * p1;
            point1 += 3 * tt * u * p2;
            point1 += ttt * p3;

            return point1;
        }

        Vector3 point2;
        /// <summary>
        /// 三次贝塞尔曲线导数
        /// </summary>
        /// <param name="t"></param>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <param name="p3"></param>
        /// <returns></returns>
        private Vector3 CalculateThreePowerBezierDerivative(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float tt = t * t;

            point2 = (-3 * tt + 6 * t - 3) * p0;
            point2 += (9 * tt - 12 * t + 3) * p1;
            point2 += (-9 * tt + 6 * t) * p2;
            point2 += 3 * tt * p3;

            return point2;
        }
    }
}
