using NonsensicalKit.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Temp.MeshKit
{
    public class Body : MonoBehaviour
    {
        [SerializeField] Leg[] legs;
        [SerializeField][Range(0.1f,1.5f)] float angle =1;    //值越高则越近

        private int groundLayer;

        private void Awake()
        {
            groundLayer = LayerMask.GetMask("Ground");
        }

        private void Start()
        {
            float partAngle = (2 * Mathf.PI) / legs.Length;
            for (int i = 0; i < legs.Length; i++)
            {
                SetLegPoint(new Ray(transform.position, transform.forward * Mathf.Sin(partAngle * i) + transform.right * Mathf.Cos(partAngle * i) - transform.up), legs[i]);
            }
        }

        private void Update()
        {
            if (Time.frameCount%5==0)
            {
                foreach (var item in legs)
                {
                    if (item.enabled == false)
                    {
                        SetNewPoint(item);
                    }
                }
            }
        }

        private void SetLegPoint(Ray ray, Leg leg)
        {
            RaycastHit RaycastHit;
            if (Physics.Raycast(ray, out RaycastHit, 100, groundLayer))
            {
                leg.SetPoint(RaycastHit.point);
            }
            else
            {
                Debug.LogError("发生了什么");
            }
        }

        private void SetNewPoint(Leg leg)
        {
            List<Vector3> points = new List<Vector3>();
            foreach (var item in legs)
            {
                if (item.enabled != false)
                {
                    points.Add(item.Point);
                }
            }

            if (points.Count == 0)
            {
                SetLegPoint(new Ray(transform.position, transform.forward - transform.up), leg);
            }
            else
            {
                Vector3 dir = Vector3.zero;
                foreach (var item in points)
                {
                    dir += item - transform.position;
                }
                dir = VectorTool.PointProjection(dir, transform.up);
                dir = -dir.normalized;
                dir = dir.RotateAroundPivot(transform.up, new Vector3(0, Random.Range(-45, 45), 0));
                SetLegPoint(new Ray(transform.position, dir - transform.up* angle), leg);
            }
        }
    }
}
