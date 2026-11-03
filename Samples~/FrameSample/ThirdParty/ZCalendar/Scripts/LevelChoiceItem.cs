/*
 * Made By JacobKay
 */

using Demo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelChoiceItem : MonoBehaviour
{
    private LoopVerticalScrollRect _layout;
    public TMP_Text txt;
    public Image choiceBox;

    private Vector2 _rangeArea;
    private bool _isChoice;
    private int _index;
    private float _crtPos;
    private float _centerPos;
    private readonly Vector2 _scaleRange = new Vector2(0.8f, 1f);
    private Color _origColor;
    private InitOnStart _prtObj;
    private int _levelNum;
    private Vector2 _screenSize;
    private RectTransform _rectTransform;
    private Button _button;

    public string Text
    {
        set => txt.text = value;
    }

    private void Start()
    {
        _rectTransform = transform as RectTransform;
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClickScrollToSelf);

        _origColor = choiceBox.color;
        _prtObj = GetComponentInParent<InitOnStart>();
        _centerPos = _prtObj.ChoiceBox.transform.position.y;
        _layout = GetComponentInParent<LoopVerticalScrollRect>();

        float halfHeight = _rectTransform.sizeDelta.y * 0.5f;
        _rangeArea.x = _centerPos - halfHeight;
        _rangeArea.y = _centerPos + halfHeight;
        _screenSize = new Vector2(Screen.width, Screen.height);
    }

    private void OnClickScrollToSelf()
    {
        _layout.ScrollToCellWithinTime(_index - 1, 0.1f);
    }

    void ScrollCellIndex(int idx)
    {
        if (_prtObj == null)
        {
            _prtObj = GetComponentInParent<InitOnStart>();
        }

        _index = idx;
        int val = idx + _prtObj.addVal;
        int mod = val % _prtObj.maxValue;
        if (mod < 0)
        {
            _levelNum = _prtObj.maxValue + mod;
        }
        else
        {
            // mod == 0 时与原逻辑一致，结果为 0
            _levelNum = mod;
        }

        txt.text = _levelNum.ToString("00");
    }

    private void Update()
    {
        float y = transform.position.y;
        if (y >= _rangeArea.x && y <= _rangeArea.y)
        {
            if (_layout.isPointDown)
            {
                _isChoice = false;
                return;
            }

            if (_isChoice) return;

            float delta = Mathf.Abs(y - _crtPos);
            _crtPos = y;
            if (delta <= 0.4f)
            {
                _origColor.a = 0.8f;
                choiceBox.color = _origColor;
                _prtObj.choiceTime = _levelNum;
                _isChoice = true;
                Move2Center();
            }
        }
        else
        {
            _origColor.a = 0f;
            choiceBox.color = _origColor;
            _isChoice = false;
        }
    }

    void UpdateSize()
    {
        float distance = Mathf.Abs(_centerPos - transform.position.y);
        float t = distance / _rectTransform.sizeDelta.y;
        float scale = _scaleRange.y - (_scaleRange.y - _scaleRange.x) * t;
        txt.transform.localScale = Vector3.one * scale;
    }

    void Move2Center()
    {
        _layout.ScrollToCellWithinTime(_index - 1, 0.1f);
        CancelInvoke(nameof(TimeChoice));
        Invoke(nameof(TimeChoice), 0.1f);
    }

    void TimeChoice()
    {
        if (!_prtObj.isInit)
        {
            _prtObj.zCalendar.TimeChoice();
        }

        if (_prtObj.isInit)
        {
            _prtObj.isInit = false;
        }
    }

    private void LateUpdate()
    {
        if (_screenSize.x == Screen.width && _screenSize.y == Screen.height) return;

        _centerPos = _prtObj.ChoiceBox.transform.position.y;
        float halfHeight = _rectTransform.sizeDelta.y * 0.5f;
        _rangeArea.x = _centerPos - halfHeight;
        _rangeArea.y = _centerPos + halfHeight;
        _screenSize.x = Screen.width;
        _screenSize.y = Screen.height;
    }
}
