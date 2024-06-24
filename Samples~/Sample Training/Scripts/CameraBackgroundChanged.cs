using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class CameraBackgroundChanged : MonoBehaviour
{
    [SerializeField] private CameraClearFlags m_targetClearFlag;
    [SerializeField] private Color m_targetColor;

    private Camera _mainCamera;
    private CameraClearFlags _originalFlags;
    private Color _originalColor;

    private void Awake()
    {
        _mainCamera = Camera.main;

        _originalFlags = _mainCamera.clearFlags;
        _originalColor = _mainCamera.backgroundColor;

        _mainCamera.clearFlags = m_targetClearFlag;
        _mainCamera.backgroundColor = m_targetColor;
    }

    private void OnDestroy()
    {
        _mainCamera.clearFlags = _originalFlags;
        _mainCamera.backgroundColor = _originalColor;
    }
}
