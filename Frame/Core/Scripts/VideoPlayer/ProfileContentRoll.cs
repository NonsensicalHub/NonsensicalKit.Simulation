using System.Collections;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Frame.Core.VideoPlayer
{
    public class ProfileContentRoll : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform rect;

        public GameObject Parent;

        [SerializeField] private int waitTime = 2;
        [SerializeField] private ScrollRect _scrollView;

        [Tooltip("进入后允许滚动的 DAG 节点名；空则不订阅节点事件。")]
        [SerializeField] private string m_enableNodeName = "设备管理";

        private RectTransform parentRect;

        private float rectOriginY;

        [SerializeField] private float rollSpeed = 60;

        [SerializeField] private bool isAtBottom = false;

        [SerializeField] private bool isRoll = true;

        [SerializeField] private bool canStartCoroutine = true;

        private bool isContentBiggerThanView = false;

        private int mul;

        public bool CanRun = true;

        private bool _nodeSubscribed;

        private void Awake()
        {
            if (string.IsNullOrEmpty(m_enableNodeName))
            {
                return;
            }

            IOCC.Subscribe((int)DagLogicNodeEnum.NodeEnter, m_enableNodeName, OnEnableNodeEnter);
            IOCC.Subscribe((int)DagLogicNodeEnum.NodeExit, m_enableNodeName, OnEnableNodeExit);
            _nodeSubscribed = true;
        }

        private void OnDestroy()
        {
            if (!_nodeSubscribed || string.IsNullOrEmpty(m_enableNodeName))
            {
                return;
            }

            IOCC.Unsubscribe((int)DagLogicNodeEnum.NodeEnter, m_enableNodeName, OnEnableNodeEnter);
            IOCC.Unsubscribe((int)DagLogicNodeEnum.NodeExit, m_enableNodeName, OnEnableNodeExit);
            _nodeSubscribed = false;
        }

        private void OnEnableNodeEnter()
        {
            if (NonsensicalInstance.Instance != null)
            {
                NonsensicalInstance.Instance.DelayDoIt(0, () => CanRun = true);
            }
            else
            {
                CanRun = true;
            }
        }

        private void OnEnableNodeExit()
        {
            if (NonsensicalInstance.Instance != null)
            {
                NonsensicalInstance.Instance.DelayDoIt(0, () => CanRun = false);
            }
            else
            {
                CanRun = false;
            }
        }

        void Start()
        {
            rect = GetComponent<RectTransform>();
            if (Parent == null)
            {
                Debug.LogWarning("[ProfileContentRoll] Parent 未赋值。", this);
                enabled = false;
                return;
            }

            parentRect = Parent.GetComponent<RectTransform>();
            if (parentRect == null || _scrollView == null)
            {
                Debug.LogWarning("[ProfileContentRoll] 缺少 RectTransform / ScrollRect。", this);
                enabled = false;
                return;
            }

            rectOriginY = rect.position.y;
        }

        void Update()
        {
            if (CanRun == false || rect == null || parentRect == null || _scrollView == null) return;

            isContentBiggerThanView = rect.sizeDelta.y > parentRect.sizeDelta.y;
            isAtBottom = _scrollView.normalizedPosition.y <= 0f;
            if (_scrollView.normalizedPosition.y >= 1f)
            {
                mul = 1;
                IOCC.Set<int>("rollDirection", mul);
            }

            if (isAtBottom && isRoll && canStartCoroutine && isContentBiggerThanView)
            {
                canStartCoroutine = false;
                StartCoroutine(StopAndBack());
            }

            if (isRoll && isContentBiggerThanView)
            {
                rect.position = new Vector3(rect.position.x, rect.position.y + (rollSpeed * Time.deltaTime * mul), 0);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CanRun = true;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            CanRun = false;
        }

        IEnumerator StopAndBack()
        {
            isRoll = false;
            yield return new WaitForSeconds(waitTime);
            if (_scrollView != null)
            {
                _scrollView.normalizedPosition = new Vector2(0, 0.001f);
            }

            mul = -1;
            IOCC.Set<int>("rollDirection", mul);
            isRoll = true;
            canStartCoroutine = true;
        }
    }
}
