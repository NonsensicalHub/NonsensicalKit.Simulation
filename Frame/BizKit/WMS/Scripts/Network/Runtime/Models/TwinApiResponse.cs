using System;
using System.Collections.Generic;

/// <summary>
/// 中台 twin 接口统一响应包装：success / code / message / data。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class TwinApiResponse<T>
{
    public bool success;
    public int code;
    public string message;
    public List<T> data;
}
}
