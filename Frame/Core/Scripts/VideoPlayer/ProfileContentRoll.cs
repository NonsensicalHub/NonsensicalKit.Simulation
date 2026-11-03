using System.Collections;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Frame.VideoPlayer
{
    public class ProfileContentRoll : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>
        /// 内容区域RectTransform
        /// </summary>
        private RectTransform rect;

        /// <summary>
        /// ScrollView
        /// </summary>
        public GameObject Parent;

        [SerializeField] private int waitTime = 2;
        [SerializeField] private ScrollRect _scrollView;

        /// <summary>
        /// ScrollView的RectTransform
        /// </summary>
        private RectTransform parentRect;

        private float rectOriginY;

        [SerializeField] private float rollSpeed = 60;

        [SerializeField] private bool isAtBottom = false;

        [SerializeField] private bool isRoll = true;

        [SerializeField] private bool canStartCoroutine = true;

        /// <summary>
        /// 内容高度是否大于窗口高度
        /// </summary>
        private bool isContentBiggerThanView = false;

        /// <summary>
        /// 倍率,此处当作方向使用
        /// </summary>
        private int mul;

        public bool CanRun = true;

        private void Awake()
        {
            IOCC.Subscribe((int)DagLogicNodeEnum.NodeEnter, "设备管理", () => NonsensicalInstance.Instance.DelayDoIt(0, () => CanRun = true));
            IOCC.Subscribe((int)DagLogicNodeEnum.NodeExit, "设备管理", () => NonsensicalInstance.Instance.DelayDoIt(0, () => CanRun = false));
        }

        void Start()
        {
            rect = GetComponent<RectTransform>();
            parentRect = Parent.GetComponent<RectTransform>();
            rectOriginY = rect.position.y;
        }

        // Update is called once per frame
        void Update()
        {
            if (CanRun == false) return;

            isContentBiggerThanView = rect.sizeDelta.y > parentRect.sizeDelta.y;
            isAtBottom = _scrollView.normalizedPosition.y <= 0f;
            if (_scrollView.normalizedPosition.y >= 1f)
            {
                mul = 1;
                IOCC.Set<int>("rollDirection", mul);
            }

            //如果滚动到底，运行协程
            if (isAtBottom && isRoll && canStartCoroutine && isContentBiggerThanView)
            {
                canStartCoroutine = false;
                StartCoroutine(StopAndBack());
            }

            //滚动
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
            //结尾停
            yield return new WaitForSeconds(waitTime);
            _scrollView.normalizedPosition = new Vector2(0, 0.001f);
            mul = -1;
            IOCC.Set<int>("rollDirection", mul);
            isRoll = true;
            canStartCoroutine = true;
        }


        /*    /// <summary>
            /// 返回顶部
            /// </summary>
            private void BackToTop()
            {
                rect.position = new Vector3(rect.position.x, rectOriginY, 0);
            }*/
    }
}
