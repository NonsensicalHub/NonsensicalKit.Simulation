using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class WorldRotationLockBehaviour : PlayableBehaviour
    {
        public WorldRotationLockClipData Data = new WorldRotationLockClipData();
    }
}
