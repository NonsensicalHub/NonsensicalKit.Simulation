using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

public class NetManager : MonoBehaviour
{

    private void Awake()
    {
        SearchServer();
    }

    public void StartHost()
    {
    }

    public void SearchServer()
    {

        //获取本地主机名
        string localHostName = Dns.GetHostName();
        Debug.Log(localHostName);

        //通过主机名获取该主机下存储所有IP地址信息的容器
        IPHostEntry IpEntry = Dns.GetHostEntry(localHostName);
        Debug.Log(IpEntry.HostName);
        IPAddress[] ipList = IpEntry.AddressList;
        for (int i = 0; i < ipList.Length; i++)
        {
            //从IP地址列表中筛选出IPv4类型的IP地址
            //AddressFamily.InterNetwork表示此IP为IPv4,
            //AddressFamily.InterNetworkV6表示此地址为IPv6类型
            if (ipList[i].AddressFamily == AddressFamily.InterNetwork)
            {
                Debug.Log(ipList[i].ToString());
              
            }
        }
        //获取本机回环地址
        IPAddress loopbackIP = IPAddress.Loopback;
        Debug.Log(loopbackIP.ToString());
    }

    public void StartClient()
    {

    }
}

class SocketServer
{
    private byte[] result = new byte[1024];
    private int myProt = 2350;   //端口  
    Socket serverSocket;
    public SocketServer()
    {
        //服务器IP地址  
        IPAddress ip = IPAddress.Parse("127.0.0.1");
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        serverSocket.Bind(new IPEndPoint(ip, myProt));  //绑定IP地址：端口  
        serverSocket.Listen(10);    //设定最多10个排队连接请求  
        Console.WriteLine("启动监听{0}成功", serverSocket.LocalEndPoint.ToString());
        //通过Clientsoket发送数据  
        Thread myThread = new Thread(ListenClientConnect);
        myThread.Start();
        Console.ReadLine();
    }

    /// <summary>  
            /// 监听客户端连接  
            /// </summary>  
    private void ListenClientConnect()
    {
        while (true)
        {
            Socket clientSocket = serverSocket.Accept();
            clientSocket.Send(Encoding.ASCII.GetBytes("Server Say Hello"));
            Thread receiveThread = new Thread(ReceiveMessage);
            receiveThread.Start(clientSocket);
        }
    }

    /// <summary>  
            /// 接收消息  
            /// </summary>  
            /// <param name="clientSocket"></param>  
    private void ReceiveMessage(object clientSocket)
    {
        Socket myClientSocket = (Socket)clientSocket;
        while (true)
        {
            try
            {
                //通过clientSocket接收数据  
                int receiveNumber = myClientSocket.Receive(result);
                Console.WriteLine("接收客户端{0}消息{1}", myClientSocket.RemoteEndPoint.ToString(), Encoding.ASCII.GetString(result, 0, receiveNumber));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                myClientSocket.Shutdown(SocketShutdown.Both);
                myClientSocket.Close();
                break;
            }
        }
    }
}

class SocketSlient
{
    private byte[] result = new byte[1024];
    public SocketSlient()
    {
        //设定服务器IP地址  
        IPAddress ip = IPAddress.Parse("127.0.0.1");
        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            clientSocket.Connect(new IPEndPoint(ip, 8885)); //配置服务器IP与端口  
            Console.WriteLine("连接服务器成功");
        }
        catch
        {
            Console.WriteLine("连接服务器失败，请按回车键退出！");
            return;
        }
        //通过clientSocket接收数据  
        int receiveLength = clientSocket.Receive(result);
        Console.WriteLine("接收服务器消息：{0}", Encoding.ASCII.GetString(result, 0, receiveLength));
        //通过 clientSocket 发送数据  
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Thread.Sleep(1000);    //等待1秒钟  
                string sendMessage = "client send Message Hellp" + DateTime.Now;
                clientSocket.Send(Encoding.ASCII.GetBytes(sendMessage));
                Console.WriteLine("向服务器发送消息：{0}" + sendMessage);
            }
            catch
            {
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
                break;
            }
        }
        Console.WriteLine("发送完毕，按回车键退出");
        Console.ReadLine();
    }
}