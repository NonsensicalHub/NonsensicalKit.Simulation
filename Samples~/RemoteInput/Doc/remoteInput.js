/**
 * RemoteInput - 远程输入控制器核心库
 * 
 * 功能：监听鼠标/键盘输入事件，序列化为JSON消息,通过WebSocket或Unity WebGL发送
 * 
 * 使用方法：
 * 1. 引入此文件：<script src="remote-input.js"></script>
 * 2. 配置参数：RemoteInput.configure({ wsAddress: '127.0.0.1', wsPort: 9099 })
 * 3. 启动监听：RemoteInput.start()
 * 4. 停止监听：RemoteInput.stop()
 */

(function (global)
{
    'use strict';
    // 内部状态管理
    const state = {
        isListening: false,
        isSending: false,
        isOptimizationEnabled: true,
        useWebGL: true,
        useWebSocket: true,
        webSocket: null,
        wsConnected: false,
        reconnectAttempts: 0,                     // 当前重连尝试次数
        maxReconnectAttempts: 5,                  // 最大重连尝试次数
        config: {
            wsAddress: '127.0.0.1',               // WebSocket地址
            wsPort: 9099,                         // WebSocket端口
            throttleMs: 16,                       // 节流间隔（毫秒），用于mousemove
            debounceMs: 300                       // 防抖延迟（毫秒）
        },
        listeners: [],                            // 已注册的事件监听器
        throttledMouseMove: null                  // 节流后的mousemove处理函数
    };

    /**
     * 工具函数：节流（Throttle）
     * 限制函数执行频率，在固定时间间隔内只执行一次
     * @param {Function} func - 要节流的函数
     * @param {number} limit - 时间间隔（毫秒）
     * @returns {Function} 节流后的函数
     */
    function throttle(func, limit)
    {
        let inThrottle;
        return function (...args)
        {
            if (!inThrottle)
            {
                func.apply(this, args);
                inThrottle = true;
                setTimeout(() => inThrottle = false, limit);
            }
        };
    }

    /**
     * 工具函数：防抖（Debounce）
     * 在用户停止操作一段时间后才执行函数
     * @param {Function} func - 要防抖的函数
     * @param {number} delay - 延迟时间（毫秒）
     * @returns {Function} 防抖后的函数
     */
    function debounce(func, delay)
    {
        let debounceTimer;
        return function (...args)
        {
            const context = this;
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => func.apply(context, args), delay);
        };
    }

    /**
     * 事件序列化：将浏览器事件转换为JSON格式
     * @param {Event} event - 浏览器事件对象
     * @returns {string} JSON字符串
     */
    function serializeEvent(event)
    {
        const baseData = {
            type: event.type,
            timestamp: Date.now(),
            viewportWidth: window.innerWidth,
            viewportHeight: window.innerHeight,
            screenWidth: screen.width,
            screenHeight: screen.height,
        };

        // 鼠标事件数据
        if (event.type.startsWith('mouse') || event.type === 'click')
        {
            Object.assign(baseData, {
                clientX: event.clientX,
                clientY: event.clientY,
                pageX: event.pageX,
                pageY: event.pageY,
                screenX: event.screenX,
                screenY: event.screenY,
                button: event.button,
                buttons: event.buttons,
                ctrlKey: event.ctrlKey,
                shiftKey: event.shiftKey,
                altKey: event.altKey,
                metaKey: event.metaKey
            });
        }

        // 滚轮事件数据
        if (event.type === 'wheel')
        {
            Object.assign(baseData, {
                deltaX: event.deltaX,
                deltaY: event.deltaY,
                deltaZ: event.deltaZ,
                deltaMode: event.deltaMode
            });
        }

        // 键盘事件数据
        if (event.type.startsWith('key'))
        {
            Object.assign(baseData, {
                key: event.key,
                code: event.code,
                keyCode: event.keyCode,
                location: event.location,
                ctrlKey: event.ctrlKey,
                shiftKey: event.shiftKey,
                altKey: event.altKey,
                metaKey: event.metaKey,
                repeat: event.repeat
            });
        }

        return JSON.stringify(baseData);
    }
    /**
     * 初始化WebSocket连接
     */
    function initWebSocket()
    {
        // 关闭已有连接
        if (state.webSocket)
        {
            state.webSocket.close();
        }

        if (!state.useWebSocket)
        {
            state.wsConnected = false;
            return;
        }

        const url = `ws://${state.config.wsAddress}:${state.config.wsPort}`;

        // 重置重连计数器
        state.reconnectAttempts = 0;
        state.maxReconnectAttempts = 5;

        try
        {
            state.webSocket = new WebSocket(url);

            state.webSocket.onopen = () =>
            {
                state.wsConnected = true;
                state.reconnectAttempts = 0; // 连接成功后重置计数器
                console.log('[RemoteInput] WebSocket已连接');
                if (state.onConnectionChange)
                {
                    state.onConnectionChange('connected');
                }
            };

            state.webSocket.onclose = () =>
            {
                state.wsConnected = false;
                console.log('[RemoteInput] WebSocket连接断开');
                if (state.onConnectionChange)
                {
                    state.onConnectionChange('disconnected');
                }

                // 尝试自动重连
                attemptReconnect(url);
            };

            state.webSocket.onerror = (error) =>
            {
                console.error('[RemoteInput] WebSocket错误:', error);
                if (state.onConnectionChange)
                {
                    state.onConnectionChange('error');
                }
            };

        } catch (e)
        {
            console.error('[RemoteInput] WebSocket URL格式错误:', e);
            if (state.onConnectionChange)
            {
                state.onConnectionChange('error');
            }
        }
    }

    /**
     * 尝试重新连接WebSocket
     * @param {string} url - WebSocket连接地址
     */
    function attemptReconnect(url)
    {
        if (!state.useWebSocket) return;
        
        if (state.reconnectAttempts >= state.maxReconnectAttempts)
        {
            console.log(`[RemoteInput] 已达到最大重连次数(${state.maxReconnectAttempts}),停止重连`);
            return;
        }

        state.reconnectAttempts++;
        const delay = Math.min(1000 * Math.pow(2, state.reconnectAttempts - 1), 30000); // 指数退避,最大30秒
        
        console.log(`[RemoteInput] ${delay/1000}秒后尝试第${state.reconnectAttempts}次重连...`);
        
        setTimeout(() =>
        {
            if (!state.useWebSocket) return;
            
            try
            {
                console.log(`[RemoteInput] 正在执行第${state.reconnectAttempts}次重连...`);
                state.webSocket = new WebSocket(url);

                state.webSocket.onopen = () =>
                {
                    state.wsConnected = true;
                    state.reconnectAttempts = 0; // 连接成功后重置计数器
                    console.log('[RemoteInput] WebSocket重连成功');
                    if (state.onConnectionChange)
                    {
                        state.onConnectionChange('connected');
                    }
                };

                state.webSocket.onclose = () =>
                {
                    state.wsConnected = false;
                    console.log(`[RemoteInput] WebSocket重连失败(第${state.reconnectAttempts}次)`);
                    if (state.onConnectionChange)
                    {
                        state.onConnectionChange('disconnected');
                    }

                    // 继续尝试重连
                    attemptReconnect(url);
                };

                state.webSocket.onerror = (error) =>
                {
                    console.error('[RemoteInput] WebSocket重连错误:', error);
                    if (state.onConnectionChange)
                    {
                        state.onConnectionChange('error');
                    }
                };

            } catch (e)
            {
                console.error('[RemoteInput] WebSocket重连URL格式错误:', e);
                if (state.onConnectionChange)
                {
                    state.onConnectionChange('error');
                }
                
                // 发生异常也继续尝试重连
                attemptReconnect(url);
            }
        }, delay);
    }
    /**
     * WebGL发送到Unity 
     * @param {string} jsonData - JSON格式的事件数据
     */
    function sendToUnityWebGL(jsonData)
    {
        if (!state.useWebGL) return;
        sendMessageToUnity("RemoteInput", jsonData);
    }
    /**
     * 通过WebSocket发送数据
     * @param {string} jsonData - JSON格式的事件数据
     */
    function sendToWebSocket(jsonData)
    {
        if (state.useWebSocket && state.webSocket && state.webSocket.readyState === WebSocket.OPEN)
        {
            state.webSocket.send(jsonData);
        }
    }

    /**
     * 主事件处理器：处理所有输入事件
     * @param {Event} event - 浏览器事件对象
     */
    function processEvent(event)
    {
        if (!state.isListening) return;

        // 阻止游戏常用键的默认行为（防止页面滚动）
        if (['Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.code))
        {
            event.preventDefault();
        }

        // 序列化事件
        const jsonData = serializeEvent(event);

        // 触发回调（如果设置了）
        if (state.onEvent)
        {
            state.onEvent(event, jsonData);
        }

        // 发送数据
        if (state.isSending)
        {
            sendToUnityWebGL(jsonData);
            sendToWebSocket(jsonData);
        }
    }

    /**
     * 注册所有事件监听器
     */
    function registerEventListeners()
    {
        // 需要监听的事件列表
        const events = [
            'mousedown', 'mouseup', 'click',
            'keydown', 'keyup'
        ];

        // 注册基础事件
        events.forEach(evt =>
        {
            const handler = (e) => processEvent(e);
            window.addEventListener(evt, handler);
            state.listeners.push({ event: evt, handler: handler });
        });

        // 特殊处理：mousemove（带节流）
        state.throttledMouseMove = throttle((e) => processEvent(e), state.config.throttleMs);
        const mouseMoveHandler = (e) =>
        {
            if (state.isOptimizationEnabled)
            {
                state.throttledMouseMove(e);
            } else
            {
                processEvent(e);
            }
        };
        window.addEventListener('mousemove', mouseMoveHandler);
        state.listeners.push({ event: 'mousemove', handler: mouseMoveHandler });

        // 特殊处理：wheel（阻止默认滚动）
        const wheelHandler = (e) =>
        {
            e.preventDefault();
            processEvent(e);
        };
        window.addEventListener('wheel', wheelHandler, { passive: false });
        state.listeners.push({ event: 'wheel', handler: wheelHandler });

        // 禁用右键菜单
        const contextMenuHandler = (e) =>
        {
            e.preventDefault();
            return false;
        };
        window.addEventListener('contextmenu', contextMenuHandler);
        state.listeners.push({ event: 'contextmenu', handler: contextMenuHandler });

        console.log('[RemoteInput] 事件监听器已注册');
    }

    /**
     * 移除所有事件监听器
     */
    function removeEventListeners()
    {
        state.listeners.forEach(({ event, handler }) =>
        {
            window.removeEventListener(event, handler);
        });
        state.listeners = [];
        console.log('[RemoteInput] 事件监听器已移除');
    }

    /**
     * 公共API：配置参数
     * @param {Object} options - 配置选项
     */
    function configure(options)
    {
        if (options.wsAddress) state.config.wsAddress = options.wsAddress;
        if (options.wsPort) state.config.wsPort = options.wsPort;
        if (options.throttleMs) state.config.throttleMs = options.throttleMs;
        if (options.debounceMs) state.config.debounceMs = options.debounceMs;

        console.log('[RemoteInput] 配置已更新:', state.config);
    }

    /**
     * 公共API：启动监听
     * @param {Object} options - 可选的配置选项
     */
    function start(options)
    {
        if (options)
        {
            configure(options);
        }

        if (state.isListening)
        {
            console.warn('[RemoteInput] 已经在监听中');
            return;
        }

        state.isListening = true;
        registerEventListeners();
        console.log('[RemoteInput] 开始监听输入事件');

        // 如果启用了WebSocket，初始化连接
        if (state.useWebSocket)
        {
            initWebSocket();
        }
    }

    /**
     * 公共API：停止监听
     */
    function stop()
    {
        if (!state.isListening)
        {
            console.warn('[RemoteInput] 未在监听中');
            return;
        }

        state.isListening = false;
        removeEventListeners();
        console.log('[RemoteInput] 停止监听输入事件');
    }

    /**
     * 公共API：启用/禁用数据发送
     * @param {boolean} enabled - 是否启用发送
     */
    function enableSending(enabled)
    {
        state.isSending = enabled;
        console.log('[RemoteInput] 数据发送:', enabled ? '启用' : '禁用');
    }

    /**
     * 公共API：启用/禁用性能优化（节流/防抖）
     * @param {boolean} enabled - 是否启用优化
     */
    function enableOptimization(enabled)
    {
        state.isOptimizationEnabled = enabled;
        console.log('[RemoteInput] 性能优化:', enabled ? '启用' : '禁用');
    }

    /**
     * 公共API：启用/禁用Unity WebGL通信
     * @param {boolean} enabled - 是否启用WebGL
     */
    function enableWebGL(enabled)
    {
        state.useWebGL = enabled;
        console.log('[RemoteInput] Unity WebGL:', enabled ? '启用' : '禁用');
    }

    /**
     * 公共API：启用/禁用WebSocket通信
     * @param {boolean} enabled - 是否启用WebSocket
     */
    function enableWebSocket(enabled)
    {
        state.useWebSocket = enabled;
        if (enabled)
        {
            initWebSocket();
        } else
        {
            if (state.webSocket)
            {
                // 先移除事件处理器，避免重复触发
                state.webSocket.onopen = null;
                state.webSocket.onclose = null;
                state.webSocket.onerror = null;

                // 关闭连接
                state.webSocket.close();
                state.webSocket = null;
                state.wsConnected = false;

                // 手动触发断开回调
                if (state.onConnectionChange)
                {
                    state.onConnectionChange('disconnected');
                }
            }
        }
        console.log('[RemoteInput] WebSocket:', enabled ? '启用' : '禁用');
    }

    /**
     * 公共API：设置连接状态变化回调
     * @param {Function} callback - 回调函数，接收状态字符串 ('connected'|'disconnected'|'error')
     */
    function onConnectionChange(callback)
    {
        state.onConnectionChange = callback;
    }

    /**
     * 公共API：设置事件处理回调
     * @param {Function} callback - 回调函数，接收(event, jsonData)参数
     */
    function onEvent(callback)
    {
        state.onEvent = callback;
    }

    /**
     * 公共API：获取当前状态
     * @returns {Object} 当前状态对象
     */
    function getStatus()
    {
        return {
            isListening: state.isListening,
            isSending: state.isSending,
            isOptimizationEnabled: state.isOptimizationEnabled,
            useWebGL: state.useWebGL,
            useWebSocket: state.useWebSocket,
            wsConnected: state.wsConnected,
            config: { ...state.config }
        };
    }

    /**
     * 公共API：手动发送自定义消息
     * @param {Object} data - 要发送的数据对象
     */
    function sendMessage(data)
    {
        const jsonData = typeof data === 'string' ? data : JSON.stringify(data);

        if (state.isSending)
        {
            sendToUnityWebGL(jsonData);
            sendToWebSocket(jsonData);
        }

        return jsonData;
    }

    // 暴露公共API
    global.RemoteInput = {
        configure: configure,
        start: start,
        stop: stop,
        enableSending: enableSending,
        enableOptimization: enableOptimization,
        enableWebGL: enableWebGL,
        enableWebSocket: enableWebSocket,
        onConnectionChange: onConnectionChange,
        onEvent: onEvent,
        getStatus: getStatus,
        sendMessage: sendMessage
    };

})(typeof window !== 'undefined' ? window : this);
