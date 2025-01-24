using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public class ShelvesBase : MonoBehaviour
    {
        [SerializeField] protected Vector3Int m_cellCount;
        [SerializeField] protected bool m_useCommonSize;

        [SerializeField] [ShowIf("m_useCommonSize")]
        protected Vector3 m_commonCellSize;

        [SerializeField] [HideIf("m_useCommonSize")]
        protected float[] m_cellXSize;

        [SerializeField] [HideIf("m_useCommonSize")]
        protected float[] m_cellYSize;

        [SerializeField] [HideIf("m_useCommonSize")]
        protected float[] m_cellZSize;

        [SerializeField] protected float m_bottomHeight;

        [SerializeField] protected Vector3Int[] m_simpleExclude;

        public Vector3Int Size => m_cellCount;

        protected Array3<Vector3> CellsPos;
        protected Array3<Vector3> PillarsPos;
        protected Array3<Vector3> HorizontalCrossbarPos;
        protected Array3<Vector3> VerticalCrossbarPos;

        protected float[] CellXSize;
        protected float[] CellYSize;
        protected float[] CellZSize;
        protected Vector3 BottomOffset;

        protected virtual void Init()
        {
            CellsPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);
            PillarsPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);
            HorizontalCrossbarPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);
            VerticalCrossbarPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);

            CellXSize = new float[m_cellCount.x + 1];
            CellYSize = new float[m_cellCount.y];
            CellZSize = new float[m_cellCount.z + 1];
            float shelvesXSize = 0;
            float shelvesZSize = 0;

            BottomOffset = new Vector3(0, -m_bottomHeight, 0);

            if (m_useCommonSize)
            {
                CellXSize.Fill(m_commonCellSize.x);
                CellYSize.Fill(m_commonCellSize.y);
                CellZSize.Fill(m_commonCellSize.z);
                shelvesXSize = m_commonCellSize.x * m_cellCount.x;
                shelvesZSize = m_commonCellSize.z * m_cellCount.z;
            }
            else
            {
                float buffer = 1;
                for (int i = 0; i <= m_cellCount.x; i++)
                {
                    if (i < m_cellXSize.Length)
                    {
                        buffer = m_cellXSize[i];
                    }

                    CellXSize[i] = buffer;
                    shelvesXSize += buffer;
                }

                shelvesXSize -= buffer;

                buffer = 1;
                for (int i = 0; i < m_cellCount.y; i++)
                {
                    if (i < m_cellYSize.Length)
                    {
                        buffer = m_cellYSize[i];
                    }

                    CellYSize[i] = buffer;
                }

                buffer = 1;
                for (int i = 0; i <= m_cellCount.z; i++)
                {
                    if (i < m_cellZSize.Length)
                    {
                        buffer = m_cellZSize[i];
                    }

                    CellZSize[i] = buffer;
                    shelvesZSize += buffer;
                }

                shelvesZSize -= buffer;
            }

            Vector3 basePos = new Vector3(-shelvesXSize * 0.5f, 0, -shelvesZSize * 0.5f) - BottomOffset;
            Vector3 cellBasePos = basePos;

            for (int x = 0; x <= m_cellCount.x; x++)
            {
                cellBasePos.y = basePos.y;
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    cellBasePos.z = basePos.z;
                    for (int z = 0; z <= m_cellCount.z; z++)
                    {
                        PillarsPos[x, y, z] = cellBasePos;
                        HorizontalCrossbarPos[x, y, z] = cellBasePos + new Vector3(CellXSize[x], 0, 0) * 0.5f;
                        VerticalCrossbarPos[x, y, z] = cellBasePos + new Vector3(0, 0, CellZSize[z]) * 0.5f;
                        CellsPos[x, y, z] = cellBasePos + new Vector3(CellXSize[x], 0, CellZSize[z]) * 0.5f;

                        cellBasePos.z += CellZSize[z];
                    }

                    cellBasePos.y += CellYSize[y];
                }

                cellBasePos.x += CellXSize[x];
            }
        }

        protected void CopyAndInit(ShelvesBase copyTarget)
        {
            m_cellCount = copyTarget.m_cellCount;
            m_useCommonSize = copyTarget.m_useCommonSize;
            m_commonCellSize = copyTarget.m_commonCellSize;
            m_cellXSize = copyTarget.m_cellXSize;
            m_cellYSize = copyTarget.m_cellYSize;
            m_cellZSize = copyTarget.m_cellZSize;
            m_bottomHeight = copyTarget.m_bottomHeight;
            m_simpleExclude = copyTarget.m_simpleExclude;

            Init();
        }
    }
}
