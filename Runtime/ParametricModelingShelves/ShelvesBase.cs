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
        [SerializeField][ShowIf("m_useCommonSize")] protected Vector3 m_commonCellSize;
        [SerializeField][HideIf("m_useCommonSize")] protected float[] m_cellXSize;
        [SerializeField][HideIf("m_useCommonSize")] protected float[] m_cellYSize;
        [SerializeField][HideIf("m_useCommonSize")] protected float[] m_cellZSize;
        [SerializeField] protected float m_bottomHeight;

        [SerializeField] protected Vector3Int[] m_simpleExclude;

        protected Array3<Vector3> _cellsPos;
        protected Array3<Vector3> _pillarsPos;
        protected Array3<Vector3> _horizontalCorssbarPos;
        protected Array3<Vector3> _verticalCorssbarPos;

        protected float[] _cellXSize;
        protected float[] _cellYSize;
        protected float[] _cellZSize;
        protected Vector3 _bottomOffset;

        protected virtual void Init()
        {
            _cellsPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);
            _pillarsPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);
            _horizontalCorssbarPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);
            _verticalCorssbarPos = new Array3<Vector3>(m_cellCount.x + 1, m_cellCount.y, m_cellCount.z + 1);

            _cellXSize = new float[m_cellCount.x + 1];
            _cellYSize = new float[m_cellCount.y];
            _cellZSize = new float[m_cellCount.z + 1];
            float shelvesXSize = 0;
            float shelvesZSize = 0;

            if (m_useCommonSize)
            {
                _cellXSize.Fill(m_commonCellSize.x);
                _cellYSize.Fill(m_commonCellSize.y);
                _cellZSize.Fill(m_commonCellSize.z);
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
                    _cellXSize[i] = buffer;
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
                    _cellYSize[i] = buffer;
                }

                buffer = 1;
                for (int i = 0; i <= m_cellCount.z; i++)
                {
                    if (i < m_cellZSize.Length)
                    {
                        buffer = m_cellZSize[i];
                    }
                    _cellZSize[i] = buffer;
                    shelvesZSize += buffer;
                }
                shelvesZSize -= buffer;
            }

            Vector3 basePos = new Vector3(-shelvesXSize * 0.5f, 0, -shelvesZSize * 0.5f);
            Vector3 cellBasePos = basePos;

            for (int x = 0; x <= m_cellCount.x; x++)
            {
                cellBasePos.y = basePos.y;
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    cellBasePos.z = basePos.z;
                    for (int z = 0; z <= m_cellCount.z; z++)
                    {
                        _pillarsPos[x, y, z] = cellBasePos;
                        _horizontalCorssbarPos[x, y, z] = cellBasePos + new Vector3(_cellXSize[x], 0, 0) * 0.5f;
                        _verticalCorssbarPos[x, y, z] = cellBasePos + new Vector3(0, 0, _cellZSize[z]) * 0.5f;
                        _cellsPos[x, y, z] = cellBasePos + new Vector3(_cellXSize[x], 0, _cellZSize[z]) * 0.5f;

                        cellBasePos.z += _cellZSize[z];
                    }
                    cellBasePos.y += _cellYSize[y];
                }
                cellBasePos.x += _cellXSize[x];
            }

            _bottomOffset = new Vector3(0, -m_bottomHeight, 0);

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