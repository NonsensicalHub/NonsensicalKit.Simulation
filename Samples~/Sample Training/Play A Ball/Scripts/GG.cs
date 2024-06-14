using NonsensicalKit.Core;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// https://catlikecoding.com/unity/tutorials/prototypes/paddle-square/
/// 3.2
/// </summary>
public class GG : MonoBehaviour
{
    [SerializeField] private UnityEvent<bool> m_gameOver;

    [SerializeField]
    TextMeshPro countdownText;

    [SerializeField, Min(1f)]
    float newGameDelay = 3f;

    float countdownUntilNewGame;
    [SerializeField, Min(2)]
    int pointsToWin = 3;
    [SerializeField]
    Ball ball;

    [SerializeField]
    Paddle topPaddle, bottomPaddle;
    [SerializeField, Min(0f)]
    Vector2 arenaExtents = new Vector2(10f, 10f);
    [SerializeField]
    LivelyCamera livelyCamera;
    void Awake() => countdownUntilNewGame = newGameDelay;

    void Update()
    {
        topPaddle.Move(ball.Position.x, arenaExtents.x);
        bottomPaddle.Move(ball.Position.x, arenaExtents.x);

        if (countdownUntilNewGame <= 0f)
        {
            UpdateGame();
        }
        else
        {
            UpdateCountdown();
        }
    }

    void UpdateGame()
    {
        ball.Move();
        BounceYIfNeeded();
        BounceXIfNeeded(ball.Position.x);
        ball.UpdateVisualization();
    }

    void UpdateCountdown()
    {
        countdownUntilNewGame -= Time.deltaTime;

        if (countdownUntilNewGame <= 0f)
        {
            countdownText.gameObject.SetActive(false);
            StartNewGame();
        }
        else
        {
            float displayValue = Mathf.Ceil(countdownUntilNewGame);
            if (displayValue < newGameDelay)
            {
                countdownText.SetText("{0}", displayValue);
            }
        }
    }
    void StartNewGame()
    {
        ball.StartNewGame();
        topPaddle.StartNewGame();
        bottomPaddle.StartNewGame();
    }
    void BounceXIfNeeded(float x)
    {
        float xExtents = arenaExtents.x - ball.Extents;
        if (x < -xExtents)
        {
            livelyCamera.PushXZ(ball.Velocity);
            ball.BounceX(-xExtents);
        }
        else if (x > xExtents)
        {
            livelyCamera.PushXZ(ball.Velocity);
            ball.BounceX(xExtents);
        }
    }

    void BounceYIfNeeded()
    {
        float yExtents = arenaExtents.y - ball.Extents;
        if (ball.Position.y < -yExtents)
        {
            BounceY(-yExtents, bottomPaddle, topPaddle);
        }
        else if (ball.Position.y > yExtents)
        {
            BounceY(yExtents, topPaddle, bottomPaddle);
        }
    }

    void BounceY(float boundary, Paddle defender, Paddle attacker)
    {
        float durationAfterBounce = (ball.Position.y - boundary) / ball.Velocity.y;//多久后会发生碰撞
        float bounceX = ball.Position.x - ball.Velocity.x * durationAfterBounce;//计算此时的x

        BounceXIfNeeded(bounceX);//先判断x，防止超出边界
        bounceX = ball.Position.x - ball.Velocity.x * durationAfterBounce;//重新计算碰撞时的x（若x未碰撞结果不会改变）
        livelyCamera.PushXZ(ball.Velocity);
        ball.BounceY(boundary);//无论是否击中都会反弹
        if (defender.HitBall(bounceX, ball.Extents, out float hitFactor))
        {//击中时改变球速度并根据新速度修改位置
            ball.SetXPositionAndSpeed(bounceX, hitFactor, durationAfterBounce);
        }
        else
        {//否则给攻击者加分，达到获胜分数时结束
            livelyCamera.JostleY();
            if (attacker.ScorePoint(pointsToWin))
            {
                EndGame(!attacker.IsAI);
            }
        }
    }

    void EndGame(bool playerWin)
    {
        countdownUntilNewGame = newGameDelay;
        countdownText.SetText("GAME OVER");
        countdownText.gameObject.SetActive(true);
        ball.EndGame();
        m_gameOver?.Invoke(playerWin);
    }
}
