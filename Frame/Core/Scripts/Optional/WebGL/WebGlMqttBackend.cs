using Frame.Core.Abstractions;
using NonsensicalKit.WebGL;

namespace Frame.Core.WebGL
{
internal sealed class WebGlMqttBackend : IWebMqttBackend
{
    public void Connect(string url, string userName, string password) =>
        WebMQTT.Instance.Connect(url, userName, password);

    public void Subscribe(string url, string topic) =>
        WebMQTT.Instance.Subscribe(url, topic);

    public void SendMessage(string url, string topic, string message) =>
        WebMQTT.Instance.SendMessage(url, topic, message);

    public void CloseAll() => WebMQTT.Instance.CloseAll();
}
}
