# unity 远程输入

## 原理：

从远端获取鼠标/键盘的输入，通过Websocket将设备的输入信息发送给Unity，Unity 通过InputSystem输入系统的设备事件队列中，从而将远端的设备输入信息转换为Unity内部的设备输入信息；通过将设备信息注入到InputHub中，从而触发本地的交互事件。

### 已完成：

1、web 输入，PC接收流程 ：exe作为Socket主机，由web端通过Websocket链接主机，监听输入并将信息发送给Unity；

2、web输入，本地Web接收：Web：客户端通过Iframe嵌入父窗体，父窗体通过向子窗体发送信息实现鼠标穿透；

3、完成Web设备监听与消息处理封装，见remoteInput.js

### 待实现：

1、UI交互：当前仅完成设备信息输入的处理，暂未实现UI点击、拖动，射线等交互

2、PC To PC，Web To Web流程；