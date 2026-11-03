using System;

namespace Frame.Core.Abstractions
{
    public interface IWebMqttBackend
    {
        void Connect(string url, string userName, string password);
        void Subscribe(string url, string topic);
        void SendMessage(string url, string topic, string message);
        void CloseAll();
    }
}
