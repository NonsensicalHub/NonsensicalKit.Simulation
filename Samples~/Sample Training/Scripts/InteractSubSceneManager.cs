using NonsensicalKit.Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InteractSubSceneManager : NonsensicalMono
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
    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_mainCamera!=null)
        {
            _mainCamera.clearFlags = _originalFlags;
            _mainCamera.backgroundColor = _originalColor;
        }
    }
    public void InteractCompleted(bool playerWin)
    {
        PublishWithID("InteractCompleted", IOCC.Get<string>("interactSubSceneMissionID"),playerWin);
    }
}
