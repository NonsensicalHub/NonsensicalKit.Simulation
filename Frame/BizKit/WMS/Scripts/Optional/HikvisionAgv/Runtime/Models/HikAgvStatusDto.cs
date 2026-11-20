using System;

namespace HikvisionAgv
{
/// <summary>queryAgvStatus 返回的单车状态。</summary>
[Serializable]
public class HikAgvStatusDto
{
    public string battery;
    public string exclType;
    public string mapCode;
    public string podCode;
    public string podDir;
    public string posX;
    public string posY;
    public string robotCode;
    public string robotDir;
    public string robotIp;
    public string speed;
    public string status;
    public string stop;
}
}
