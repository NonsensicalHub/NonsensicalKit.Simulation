using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class BlinkBehaviour : PlayableBehaviour
    {
        public BlinkClipData Data = new BlinkClipData();

        [NonSerialized] public BlinkClip ClipAsset;
    }
}
