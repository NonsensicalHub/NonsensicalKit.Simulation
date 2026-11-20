using System;

namespace HikvisionAgv
{
/// <summary>queryTaskStatus 返回的单条任务状态。</summary>
[Serializable]
public class HikTaskStatusDto
{
    public string agvCode;
    public string taskCode;
    public string taskStatus;
    public string taskTyp;
}
}
