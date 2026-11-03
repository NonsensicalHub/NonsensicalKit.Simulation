using UnityEngine;
using UnityEngine.UI;

public interface IVideoPlayer
{
    public void Init(RawImage image);
    public void Play();
    public void Open();
    public void Open(string path);
    public void Stop();
}
