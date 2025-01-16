using System;
using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace  NonsensicalKit.Simulation.ParametricModelingShelves.Samples
{
    public class ParametricModelingShelvesSample : MonoBehaviour
    {
        [SerializeField] private ShelvesBuilder m_builder;
        [SerializeField] private ShelvesManager m_manager;
        [SerializeField] private Button m_btn_rebuild;
        [SerializeField] private Button m_btn_invertals;
        [SerializeField] private Button m_btn_changeVisible;

        private bool _invertaling;

        private void Awake()
        {
            m_btn_rebuild.onClick.AddListener(OnRebuild);
            m_btn_invertals.onClick.AddListener(OnInvertaling);
            m_btn_changeVisible.onClick.AddListener(OnChangeVisible);
        }

        private void OnRebuild()
        {
            m_builder.Rebuild();
        }

        private void OnInvertaling()
        {
            if (_invertaling)
            {
                m_manager.LayerIntervals = null;
            }
            else
            {
                float[] ins=new float[m_manager.Size.y];
                for (int i = 0; i < ins.Length; i++)
                {
                    ins[i]=Random.Range(0.0f,1.0f);
                }

                m_manager.LayerIntervals = ins;
            }

            _invertaling = !_invertaling;
        }
        
        private void OnChangeVisible()
        {
            Array3<bool> vs = new Array3<bool>(m_builder.Size.x, m_builder.Size.y, m_builder.Size.z);

            for (int i = 0; i < vs.m_Array.Length; i++)
            {
                vs.m_Array[i] = Random.Range(0, 2) == 1;
            }

            m_manager.SetLoadsVisible(vs);
        }
    }
}
