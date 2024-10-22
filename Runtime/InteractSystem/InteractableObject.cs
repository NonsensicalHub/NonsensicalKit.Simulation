using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.Simulation.InteractQueueSystem
{
    /// <summary>
    /// 可交互对象基类
    /// </summary>
    public abstract class InteractableObject : NonsensicalMono
    {
        /// <summary>
        /// 交互操作的名称
        /// </summary>
        [SerializeField] private string m_interactName;
        /// <summary>
        /// 从默认状态变为等待状态时
        /// </summary>
        [SerializeField] private UnityEvent m_onStartWaiting = new();
        /// <summary>
        /// 从等待状态变为默认状态时
        /// </summary>
        [SerializeField] private UnityEvent m_onStopWaiting = new();
        /// <summary>
        /// 交互时
        /// </summary>
        [SerializeField] private UnityEvent m_onInteract = new();

        public string InteractName => m_interactName;
        public UnityEvent OnStartWaiting => m_onStartWaiting;
        public UnityEvent OnStopWaiting => m_onStopWaiting;
        public UnityEvent OnInteract => m_onInteract;

        /// <summary>
        /// 验证是否激活的方法
        /// </summary>
        public Func<bool> ValidateFunc
        {
            get
            {
                return _validate;
            }
            set
            {
                _validate = value;
                Refresh();
            }
        }

        private Func<bool> _validate;

        public bool CanInteract { get { return _canInteract; } set { _canInteract = value; } }
        /// <summary>
        /// 可由外界控制的是否可交互变量
        /// </summary>
        private bool _canInteract = true;
        /// <summary>
        /// 是否正在等待交互，用于判断此时是否能通过使用的交互方式进行交互，比如碰撞交互此值代表就代表是否碰到玩家，鼠标交互此值就代表鼠标是否悬停在可交互区域
        /// </summary>
        private bool _isWaitingInteract;

        private InteractQueueSystem _System
        {
            get
            {
                if (_system == null)
                {

                    _system = ServiceCore.Get<InteractQueueSystem>();
                }
                return _system;
            }
        }
        private InteractQueueSystem _system;

        public virtual void Interact()
        {
            m_onInteract?.Invoke();
            Refresh();
        }

        /// <summary>
        /// 强制刷新，当激活状态中途由于特殊原因改变时调用
        /// </summary>
        public void ForceRefresh()
        {
            Refresh();
        }

        /// <summary>
        /// 
        /// </summary>
        protected virtual void StartWaiting()
        {
            if (!_isWaitingInteract)
            {
                _isWaitingInteract = true;
                if (Verify())
                {
                    m_onStartWaiting?.Invoke();
                    _System.EnterQueue(this);
                }
            }
        }

        protected virtual void StopWaiting()
        {
            if (_isWaitingInteract)
            {
                _isWaitingInteract = false;
                m_onStartWaiting?.Invoke();
                _System.ExitQueue(this);
            }
        }

        /// <summary>
        /// 验证是否通过，可用于触发后就需要验证的状态，如物品拾取后应当验证是否还需要继续显示拾取交互菜单
        /// </summary>
        private bool Verify()
        {
            if (!_canInteract)
            {
                return false;
            }
            if (ValidateFunc == null)
            {
                return true;
            }
            foreach (Func<bool> f in ValidateFunc.GetInvocationList())
            {
                if (!f())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 率性状态
        /// </summary>
        private void Refresh()
        {
            if (_isWaitingInteract && Verify())
            {
                StartWaiting();
            }
            else
            {
                StopWaiting();
            }
        }
    }
}
