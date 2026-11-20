using System;
using System.Collections.Generic;

namespace HikvisionAgv
{
/// <summary>海康 RCS-Lite 统一响应信封（成功 code==0）。</summary>
[Serializable]
public class HikApiResponse<T>
{
    public int code;
    public string message;
    public string reqCode;
    public List<T> data;
}
}
