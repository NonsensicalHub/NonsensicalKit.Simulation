using System;
using System.Diagnostics;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

namespace Frame.Core.UI
{
public enum OffsetDirection
{
    None = 0,
    X = 1 << 1,
    Y = 1 << 2,
    All = ~0,
}

public class UVAnimation : MonoBehaviour
{
    [SerializeField] private float m_speed = 1f;
    [SerializeField] private Material m_material;

    [EnumFlags] public OffsetDirection _direction = OffsetDirection.All;
    private bool _isRotating = false;

    private void Awake()
    {
        IOCC.Subscribe("ResetUVAnimation", OnReset);
    }

    #region public

    //正转
    [Button]
    public void SetForward()
    {
        SetDirection(m_speed > 0 ? 1 : -1);
        _isRotating = true;
    }

    //倒转
    [Button]
    public void SetBackward()
    {
        SetDirection(m_speed < 0 ? 1 : -1);
        _isRotating = true;
    }

    //停止
    [Button]
    public void SetStop()
    {
        _isRotating = false;
    }

    #endregion

    private void SetDirection(int direction)
    {
        m_speed = m_speed * direction;
    }

    private void FixedUpdate()
    {
        if (_isRotating == false) return;
        // Update the UV offset.
        m_material.mainTextureOffset += _direction switch
        {
            OffsetDirection.X => new Vector2(m_speed * Time.deltaTime, 0),
            OffsetDirection.Y => new Vector2(0, m_speed * Time.deltaTime),
            OffsetDirection.None => new Vector2(0, 0),
            _ => new Vector2(m_speed * Time.deltaTime, m_speed * Time.deltaTime)
        };
    }

    private void OnReset()
    {
        _isRotating = false;
        m_material.mainTextureOffset = new Vector2(0, 0);
    }

    private void OnApplicationQuit()
    {
        OnReset();
    }
}
}
