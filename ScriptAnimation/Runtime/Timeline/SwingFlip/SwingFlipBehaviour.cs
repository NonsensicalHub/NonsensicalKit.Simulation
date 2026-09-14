using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class SwingFlipBehaviour : PlayableBehaviour
    {
        public SwingFlipClipData Data = new SwingFlipClipData();

        [NonSerialized] public SwingFlipClip ClipAsset;
    }
}
