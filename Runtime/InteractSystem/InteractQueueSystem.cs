using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.InteractQueueSystem
{
    /// <summary>
    /// 交互队列系统
    /// 规范化一套固定的交互逻辑，可交互对象通过某种判断进入和移除等待队列，可通过UI在画面上同步显示等待队列和当前选中交互对象
    /// 通过调用切换方法可切换等待队列中选中的交互对象，通过调用交互方法可对当前选中对象进行交互
    /// </summary>
    public class InteractQueueSystem : NonsensicalMono, IMonoService
    {
        public bool IsReady { get; set; }

        public Action InitCompleted { get; set; }

        /// <summary>
        /// 当前等待交互的对象
        /// </summary>
        private List<InteractableObject> _interactableObjects = new List<InteractableObject>();

        /// <summary>
        /// 当前选中的InteractableObject在_interactableObjects中的索引
        /// 无可交互对象时会变为-1，可通过判断此值是否为-1来判断是否存在可交互对象
        /// </summary>
        private int _selectIndex;

        private void Awake()
        {
            _selectIndex = -1;

            Subscribe<int>("InteractMenuClick", OnMenuClick);

            IsReady = true;
            InitCompleted?.Invoke();
        }

        public void Interact()
        {
            if (_selectIndex >= 0)
            {
                _interactableObjects[_selectIndex].Interact();
            }
        }

        public void Switch()
        {
            if (_selectIndex >= 0)
            {
                _selectIndex++;
                if (_selectIndex >= _interactableObjects.Count)
                {
                    _selectIndex = 0;
                }
                UpdateMenu();
            }
        }

        public void EnterQueue(InteractableObject obj)
        {
            if (_interactableObjects.Contains(obj) == false)
            {
                _interactableObjects.Add(obj);

                if (_selectIndex == -1)
                {
                    _selectIndex = 0;
                }
                UpdateMenu();
            }
        }

        public void ExitQueue(InteractableObject obj)
        {
            if (_interactableObjects.Contains(obj) == true)
            {
                int index = _interactableObjects.IndexOf(obj);
                if (index < _selectIndex)
                {
                    _selectIndex--;
                }
                _interactableObjects.Remove(obj);

                if (_selectIndex>= _interactableObjects.Count)
                {
                    _selectIndex = _interactableObjects.Count - 1;
                }
                UpdateMenu();
            }
        }

        private void OnMenuClick(int menuIndex)
        {
            if (menuIndex>=0&& menuIndex< _interactableObjects.Count)
            {
                _selectIndex = menuIndex;
                _interactableObjects[menuIndex].Interact();
            }
        }

        private void UpdateMenu()
        {
            List<string> menuNames = new List<string>();
            foreach (var item in _interactableObjects)
            {
                menuNames.Add(item.InteractName);
            }
            Publish<List<string>,int>("updateInteractMenu", menuNames,_selectIndex);
        }
    }
}
