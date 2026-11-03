using System.Linq;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using TMPro;
using UnityEngine;

public class StorageAnatomyControl : NonsensicalMono
{
    [SerializeField] private TMP_Dropdown m_dropDown;
    [SerializeField] private string m_crtNode;

    private readonly string _levelCommand = "storageAnatomyLevel";

    private void Awake()
    {
        Subscribe((int)DagLogicNodeEnum.NodeEnter, m_crtNode, OnEnter);
        Subscribe((int)DagLogicNodeEnum.NodeExit, m_crtNode, OnExit);

        m_dropDown ??= GetComponentInChildren<TMP_Dropdown>();
        m_dropDown.onValueChanged.AddListener(ShowRackLevel);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        m_dropDown.onValueChanged.RemoveAllListeners();
    }

    public void ChangeOptions(int count, string quantifier)
    {
        m_dropDown.ClearOptions();
        m_dropDown.AddOptions(Enumerable.Range(1, count).Select(x => $"{x}{quantifier}").Reverse().ToList());
    }

    private void ShowRackLevel(int arg0)
    {
        IOCC.Set<int>(_levelCommand, m_dropDown.options.Count - arg0);
    }

    private void OnEnter()
    {
        IOCC.Set<int>(_levelCommand, m_dropDown.options.Count - m_dropDown.value);
    }

    private void OnExit()
    {
        IOCC.Set<int>(_levelCommand, -1);
    }
}
