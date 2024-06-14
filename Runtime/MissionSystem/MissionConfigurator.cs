using NonsensicalKit.Core.Service;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 任务配置器
/// 如果任务依赖于场景，就放在对应场景中
/// 如果任务贯穿整个项目，就放在初始场景中
/// </summary>
public class MissionConfigurator : MonoBehaviour
{
    [SerializeField] private MissionData[] m_missions;
    [SerializeField] private bool m_autoStart=true;

    private void Start()
    {
        ServiceCore.SafeGet<MissionSystem>(OnGetSystem);
    }

    private void OnGetSystem(MissionSystem system)
    {
        system.InitMission(m_missions);
        if (m_autoStart)
        {
            system.AutoAccept();
        }
    }
}
