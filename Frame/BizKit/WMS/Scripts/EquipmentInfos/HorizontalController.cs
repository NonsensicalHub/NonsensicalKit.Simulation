using NonsensicalKit.UGUI.Table;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Equipment
{
    public class HorizontalController : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollView;
        [SerializeField] private float _deltaOfffect = 0.2f;

        public void MoveLeft()
        {
            _scrollView.normalizedPosition = new Vector2(_scrollView.normalizedPosition.x + _deltaOfffect, 0);
        }

        public void MoveRight()
        {
            _scrollView.normalizedPosition = new Vector2(_scrollView.normalizedPosition.x - _deltaOfffect, 0);
        }
    }
}
