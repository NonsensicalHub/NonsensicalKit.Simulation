using System.Collections;
using CineShot.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CineShot.Tests
{
    public class PlaybackSmokeTests
    {
        [UnityTest]
        public IEnumerator CinePlayer_Interrupt_Then_Resume_StateMachine()
        {
            var go = new GameObject("CineTest");
            var rig = go.AddComponent<CineShotRig>();
            var director = go.AddComponent<UnityEngine.Playables.PlayableDirector>();
            var player = new CinePlayer(NullCameraOutput.Instance, director, rig);
            Assert.AreEqual(CineState.Idle, player.State);
            Assert.IsTrue(player.Play(null, 0.0));
            Assert.AreEqual(CineState.Playing, player.State);
            Assert.IsTrue(player.Interrupt(0.1f));
            Assert.AreEqual(CineState.BlendingOut, player.State);
            float t = 0f;
            while (player.State == CineState.BlendingOut && t < 1f)
            {
                player.Tick(0.05f);
                t += 0.05f;
                yield return null;
            }
            Assert.AreEqual(CineState.PausedResumable, player.State);
            Assert.IsTrue(player.Resume());
            Assert.AreEqual(CineState.Playing, player.State);
            player.Stop(true);
            Assert.AreEqual(CineState.Idle, player.State);
            Object.Destroy(go);
        }
    }
}
