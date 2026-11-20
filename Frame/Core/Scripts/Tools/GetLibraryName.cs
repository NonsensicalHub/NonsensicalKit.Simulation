using System;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.Core.Service;
using UnityEngine;

namespace Frame.Core.Tools
{
public class GetLibraryName : NonsensicalMono
{
    [SerializeField] private string[] m_libraryNames;

    private DagLogicManager _dagLogicManager;

    private void Awake()
    {
        AddHandler<string>("getCurrentLibraryName", GetCurrentLibraryName);
        ServiceCore.SafeGet<DagLogicManager>(OnGetDag);
    }

    private void OnGetDag(DagLogicManager obj)
    {
        _dagLogicManager = obj;
    }

    private string GetCurrentLibraryName()
    {
        if (_dagLogicManager == null) return null;
        foreach (var s in m_libraryNames)
        {
            if (_dagLogicManager.IsInActivationChain(s) == false)
            {
                Debug.Log(s);
                continue;
            }

            return s;
        }

        return null;
    }
}
}
