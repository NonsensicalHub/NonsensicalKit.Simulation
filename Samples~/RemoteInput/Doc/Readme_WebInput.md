# RemoteInput - 远程输入控制器

一个轻量级的JavaScript库，用于监听鼠标/键盘输入事件，并将它们序列化为JSON消息通过WebSocket或Unity WebGL发送。

## 📦 文件说明

- **remote-input.js** - 核心库文件（必需）
- **example.html** - 交互式使用示例（推荐）
- **云渲染远程输入功能.html** - 完整功能的演示页面

## 🚀 快速开始

### 1. 引入库文件

```html
<script src="remote-input.js"></script>
```

### 2. 基本使用

```javascript
// 配置参数
RemoteInput.configure({
    wsAddress: '127.0.0.1',
    wsPort: 9099,
    throttleMs: 16
});

// 启用通信方式
RemoteInput.enableWebSocket(true);  // 启用WebSocket
RemoteInput.enableWebGL(false);     // 禁用Unity WebGL

// 启动监听
RemoteInput.start();

// 启用数据发送
RemoteInput.enableSending(true);
```

### 3. 停止监听

```javascript
RemoteInput.stop();
```

## 📖 API 文档

### 配置方法

#### `configure(options)`
配置库的参数。

**参数：**
- `options.wsAddress` (string) - WebSocket地址，默认: '127.0.0.1'
- `options.wsPort` (number) - WebSocket端口，默认: 9099
- `options.throttleMs` (number) - 节流间隔（毫秒），默认: 16
- `options.debounceMs` (number) - 防抖延迟（毫秒），默认: 300

**示例：**
```javascript
RemoteInput.configure({
    wsAddress: '192.168.1.100',
    wsPort: 8080,
    throttleMs: 20
});
```

### 控制方法

#### `start(options?)`
启动输入事件监听。

**参数：**
- `options` (Object, 可选) - 配置选项，会先应用这些配置再启动

**示例：**
```javascript
// 直接启动
RemoteInput.start();

// 带配置启动
RemoteInput.start({
    wsAddress: '127.0.0.1',
    wsPort: 9099
});
```

#### `stop()`
停止输入事件监听。

**示例：**
```javascript
RemoteInput.stop();
```

#### `enableSending(enabled)`
启用或禁用数据发送。

**参数：**
- `enabled` (boolean) - 是否启用发送

**示例：**
```javascript
RemoteInput.enableSending(true);  // 启用发送
RemoteInput.enableSending(false); // 禁用发送
```

#### `enableOptimization(enabled)`
启用或禁用性能优化（节流/防抖）。

**参数：**
- `enabled` (boolean) - 是否启用优化

**示例：**
```javascript
RemoteInput.enableOptimization(true);  // 启用优化
RemoteInput.enableOptimization(false); // 禁用优化
```

#### `enableWebGL(enabled)`
启用或禁用Unity WebGL通信。

**参数：**
- `enabled` (boolean) - 是否启用WebGL

**示例：**
```javascript
RemoteInput.enableWebGL(true);
```

#### `enableWebSocket(enabled)`
启用或禁用WebSocket通信。支持动态连接和断开。

**参数：**
- `enabled` (boolean) - 是否启用WebSocket

**示例：**
```javascript
// 启用WebSocket并建立连接
RemoteInput.enableWebSocket(true);

// 断开WebSocket连接
RemoteInput.enableWebSocket(false);
```

**说明：**
- 调用 `enableWebSocket(true)` 时会尝试建立新的WebSocket连接
- 调用 `enableWebSocket(false)` 时会立即断开当前连接并清理资源
- 可以在运行时动态切换连接状态，无需重启监听

### 回调方法

#### `onEvent(callback)`
设置事件处理回调函数。

**参数：**
- `callback` (Function) - 回调函数，接收 `(event, jsonData)` 参数
  - `event` - 原始浏览器事件对象
  - `jsonData` - 序列化后的JSON字符串

**示例：**
```javascript
RemoteInput.onEvent((event, jsonData) => {
    console.log('事件类型:', event.type);
    console.log('JSON数据:', jsonData);
});
```

#### `onConnectionChange(callback)`
设置WebSocket连接状态变化回调。

**参数：**
- `callback` (Function) - 回调函数，接收状态字符串
  - `'connected'` - 已连接
  - `'disconnected'` - 已断开
  - `'error'` - 错误

**示例：**
```javascript
RemoteInput.onConnectionChange((status) => {
    switch(status) {
        case 'connected':
            console.log('WebSocket已连接');
            break;
        case 'disconnected':
            console.log('WebSocket已断开');
            break;
        case 'error':
            console.log('WebSocket连接错误');
            break;
    }
});
```

### 查询方法

#### `getStatus()`
获取当前状态信息。

**返回：** Object - 包含所有状态信息
```javascript
{
    isListening: boolean,           // 是否在监听
    isSending: boolean,             // 是否启用发送
    isOptimizationEnabled: boolean, // 是否启用性能优化
    useWebGL: boolean,              // 是否启用WebGL
    useWebSocket: boolean,          // 是否启用WebSocket
    wsConnected: boolean,           // WebSocket是否已连接
    config: { ... }                 // 当前配置
}
```

**示例：**
```javascript
const status = RemoteInput.getStatus();
console.log('是否在监听:', status.isListening);
console.log('WebSocket是否连接:', status.wsConnected);
console.log('配置信息:', status.config);
```

#### `sendMessage(data)`
手动发送自定义消息。

**参数：**
- `data` (Object|string) - 要发送的数据对象或JSON字符串

**返回：** string - 发送的JSON字符串

**示例：**
```javascript
// 发送对象
RemoteInput.sendMessage({
    type: 'custom',
    message: 'Hello'
});

// 发送JSON字符串
RemoteInput.sendMessage('{"type":"custom","message":"Hello"}');
```

## 💡 使用场景

### 场景1：简单的WebSocket通信

```javascript
// 配置并启动
RemoteInput.configure({
    wsAddress: '127.0.0.1',
    wsPort: 9099
});

RemoteInput.enableWebSocket(true);
RemoteInput.enableSending(true);
RemoteInput.start();
```

### 场景2：动态控制WebSocket连接

```javascript
// 启动监听
RemoteInput.start();

// 需要时再连接WebSocket
document.getElementById('connect-btn').addEventListener('click', () => {
    RemoteInput.enableWebSocket(true);
});

// 断开WebSocket但保持监听
document.getElementById('disconnect-btn').addEventListener('click', () => {
    RemoteInput.enableWebSocket(false);
});
```

### 场景3：同时使用WebSocket和WebGL

```javascript
RemoteInput.configure({
    wsAddress: '127.0.0.1',
    wsPort: 9099
});

RemoteInput.enableWebGL(true);
RemoteInput.enableWebSocket(true);
RemoteInput.enableSending(true);
RemoteInput.start();
```

### 场景4：自定义事件处理

```javascript
// 设置事件回调
RemoteInput.onEvent((event, jsonData) => {
    // 记录到本地日志
    console.log('捕获到事件:', event.type);
    
    // 可以在此处进行额外的处理
    if (event.type === 'keydown') {
        console.log('按下的键:', event.key);
    }
});

// 启动监听
RemoteInput.start();
RemoteInput.enableSending(true);
```

### 场景5：监听连接状态

```javascript
// 设置连接状态回调
RemoteInput.onConnectionChange((status) => {
    const indicator = document.getElementById('connection-indicator');
    
    switch(status) {
        case 'connected':
            indicator.textContent = '✅ 已连接';
            indicator.className = 'connected';
            break;
        case 'disconnected':
            indicator.textContent = '❌ 已断开';
            indicator.className = 'disconnected';
            break;
        case 'error':
            indicator.textContent = '⚠️ 连接错误';
            indicator.className = 'error';
            break;
    }
});

// 启用WebSocket
RemoteInput.enableWebSocket(true);
```

### 场景6：动态控制

```javascript
// 启动监听但不发送
RemoteInput.start();
RemoteInput.enableSending(false);

// 用户点击按钮后才开始发送
document.getElementById('start-btn').addEventListener('click', () => {
    RemoteInput.enableSending(true);
});

// 暂停发送
document.getElementById('pause-btn').addEventListener('click', () => {
    RemoteInput.enableSending(false);
});
```

## 📊 消息格式

### 鼠标事件
```json
{
    "type": "mousedown",
    "timestamp": 1234567890,
    "viewportWidth": 1920,
    "viewportHeight": 1080,
    "screenWidth": 1920,
    "screenHeight": 1080,
    "clientX": 100,
    "clientY": 200,
    "pageX": 100,
    "pageY": 200,
    "screenX": 100,
    "screenY": 200,
    "button": 0,
    "buttons": 1,
    "ctrlKey": false,
    "shiftKey": false,
    "altKey": false,
    "metaKey": false
}
```

### 键盘事件
```json
{
    "type": "keydown",
    "timestamp": 1234567890,
    "viewportWidth": 1920,
    "viewportHeight": 1080,
    "screenWidth": 1920,
    "screenHeight": 1080,
    "key": "a",
    "code": "KeyA",
    "keyCode": 65,
    "location": 0,
    "ctrlKey": false,
    "shiftKey": false,
    "altKey": false,
    "metaKey": false,
    "repeat": false
}
```

### 滚轮事件
```json
{
    "type": "wheel",
    "timestamp": 1234567890,
    "viewportWidth": 1920,
    "viewportHeight": 1080,
    "screenWidth": 1920,
    "screenHeight": 1080,
    "deltaX": 0,
    "deltaY": -100,
    "deltaZ": 0,
    "deltaMode": 0
}
```

## ⚙️ 性能优化

### 节流（Throttle）
- 应用于 `mousemove` 事件
- 默认每16ms执行一次（约60fps）
- 可通过 `throttleMs` 配置调整

### 防抖（Debounce）
- 库内部提供了debounce工具函数
- 可根据需要在应用层使用

## 🔧 高级用法

### 与Unity集成

在Unity C#脚本中：

```csharp
using UnityEngine;

public class InputManager : MonoBehaviour
{
    public void OnRemoteInput(string jsonData)
    {
        // 解析JSON数据
        var data = JsonUtility.FromJson<InputData>(jsonData);
        
        // 处理输入
        HandleInput(data);
    }
    
    void HandleInput(InputData data)
    {
        if (data.type == "mousedown")
        {
            Debug.Log($"鼠标点击: ({data.clientX}, {data.clientY})");
        }
        else if (data.type == "keydown")
        {
            Debug.Log($"按键按下: {data.key}");
        }
    }
}

[System.Serializable]
public class InputData
{
    public string type;
    public long timestamp;
    public int viewportWidth;
    public int viewportHeight;
    public int screenWidth;
    public int screenHeight;
    
    // 鼠标相关
    public float clientX;
    public float clientY;
    public int button;
    
    // 键盘相关
    public string key;
    public string code;
    public int keyCode;
}
```

### 自定义事件过滤

```javascript
RemoteInput.onEvent((event, jsonData) => {
    // 只处理特定事件
    if (event.type === 'keydown' && event.code === 'Space') {
        RemoteInput.sendMessage({
            type: 'special_action',
            timestamp: Date.now()
        });
    }
});
```

### 重连机制

```javascript
let reconnectAttempts = 0;
const maxReconnectAttempts = 5;

RemoteInput.onConnectionChange((status) => {
    if (status === 'disconnected' || status === 'error') {
        if (reconnectAttempts < maxReconnectAttempts) {
            reconnectAttempts++;
            console.log(`尝试重连 (${reconnectAttempts}/${maxReconnectAttempts})...`);
            
            setTimeout(() => {
                RemoteInput.enableWebSocket(true);
            }, 2000 * reconnectAttempts); // 指数退避
        } else {
            console.error('达到最大重连次数');
        }
    } else if (status === 'connected') {
        reconnectAttempts = 0; // 重置计数
    }
});
```

## 🎨 示例页面特性

[example.html](file://c:\Users\liwu\Desktop\RemoteInput\example.html) 提供了一个完整的交互式示例，包含以下特性：

- **独立WebSocket控制**：可以单独控制WebSocket的连接和断开
- **实时状态反馈**：连接状态、监听状态、发送状态实时更新
- **事件日志**：详细记录所有输入事件，最多保留50条

打开 [example.html](file://c:\Users\liwu\Desktop\RemoteInput\example.html) 即可体验完整功能！

## 📝 注意事项

1. **性能考虑**
   - 默认启用了节流优化，避免过多的mousemove事件
   - 如需更高精度，可降低throttleMs值或禁用优化

2. **安全性**
   - WebSocket连接需要确保服务器端正确处理数据
   - 建议在生产环境使用wss://加密连接

3. **兼容性**
   - 支持所有现代浏览器
   - Unity WebGL需要Unity 2019+版本

4. **事件阻止**
   - 库会自动阻止方向键和空格键的默认行为（防止页面滚动）
   - 会自动阻止滚轮的默认滚动行为

5. **WebSocket连接管理**
   - `enableWebSocket(false)` 会立即断开连接并清理资源
   - 重新调用 `enableWebSocket(true)` 会建立新连接
   - 建议在不需要时及时断开以节省资源

## 🐛 故障排除

### WebSocket连接失败
- 检查服务器是否运行
- 确认地址和端口正确
- 检查浏览器控制台错误信息
- 尝试使用 `enableWebSocket(false)` 断开后重新连接

### Unity WebGL无法接收消息
- 确认UnityInstance已正确初始化
- 检查gameObjectName和methodName是否正确
- 查看浏览器控制台是否有警告信息

### 事件未触发
- 确认已调用`start()`方法
- 检查`isListening`状态
- 确认`enableSending(true)`已调用

### WebSocket断开不正常
- 确保使用最新版本的 remote-input.js
- 调用 `enableWebSocket(false)` 会正确清理连接
- 检查是否有其他地方调用了 `enableWebSocket(true)`

## 📄 许可证

MIT License
