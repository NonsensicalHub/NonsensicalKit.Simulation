using System;
using NonsensicalKit.Tools.InputTool;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputVerifyDemo : MonoBehaviour
{
    [SerializeField] private bool m_log;
    public Transform target; // 一个Cube即可
    public float moveSpeed = 10f;

    /*
    private void Start()
    {
        InputHub.Instance.OnMouseLeftButtonDown += () =>
        {
            SetColor(Color.red);
        };
        InputHub.Instance.OnMouseLeftButtonUp += () =>
        {
            SetColor(Color.white);
        };
        InputHub.Instance.OnMouseRightButtonDown += () =>
        {
            SetColor(Color.blue);
        };
        InputHub.Instance.OnMouseRightButtonUp += () =>
        {
            SetColor(Color.white);
        };

    }
    */

    void Update()
    {
        VerifyMouse();
        VerifyKeyboard();
    }

    void VerifyMouse()
    {
        //var mouse = Mouse.current;
        var mouse = RemoteInputReceive.Instance.RemoteMouseInstance;
        if (mouse == null)
        {
            return;
        }

        //Debug.Log("mouseDelta: " + mouse.position.ReadValue());
        Vector2 pos = mouse.position.ReadValue();

        if (mouse.leftButton.isPressed)
        {
            //Debug.Log("mouseDelta: " + mouse.position.ReadValue());
            if (m_log)
                Debug.Log("左键按住中");
        }

        if (mouse.leftButton.wasPressedThisFrame)
            if (m_log)
                Debug.Log("左键刚按下");

        if (mouse.leftButton.wasReleasedThisFrame)
            if (m_log)
                Debug.Log("左键刚释放");


        // 1️⃣ 控制物体移动（验证位置）
        if (target != null)
        {
            Vector3 world = Camera.main.ScreenToWorldPoint(
                new Vector3(pos.x, pos.y, 10f)
            );

            target.position = Vector3.Lerp(
                target.position,
                world,
                Time.deltaTime * moveSpeed
            );
        }

        // 2️⃣ 按键测试（验证 buttons）
        if (mouse.leftButton.isPressed)
        {
            if (m_log) Debug.Log("左键按下");
            SetColor(Color.red);
        }
        else if (mouse.rightButton.isPressed)
        {
            if (m_log) Debug.Log("右键按下");
            SetColor(Color.blue);
        }
        else
        {
            SetColor(Color.white);
        }

        // 3️⃣ 滚轮测试
        Vector2 scroll = mouse.scroll.ReadValue();
        if (scroll.y != 0)
        {
            Debug.Log("滚轮: " + scroll.y);
        }

        // 4️⃣ 实时位置输出
        //  Debug.Log($"Mouse Pos: {pos}");
    }

    void VerifyKeyboard()
    {
        var keyboard = RemoteInputReceive.Instance.RemoteKeyboardInstance;
        if (keyboard == null)
        {
            return;
        }

        // WASD 测试
        if (keyboard.wKey.isPressed)
            if (m_log)
                Debug.Log("W 按下");

        if (keyboard.aKey.isPressed)
            if (m_log)
                Debug.Log("A 按下");

        if (keyboard.sKey.isPressed)
            if (m_log)
                Debug.Log("S 按下");

        if (keyboard.dKey.isPressed)
            if (m_log)
                Debug.Log("D 按下");

        // 空格测试
        if (keyboard.spaceKey.wasPressedThisFrame)
            if (m_log)
                Debug.Log("Space 触发");

        // Ctrl 测试
        if (keyboard.ctrlKey.isPressed)
            if (m_log)
                Debug.Log("Ctrl 按住");
    }

    void SetColor(Color c)
    {
        if (target == null) return;

        var renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = c;
        }
    }
}
