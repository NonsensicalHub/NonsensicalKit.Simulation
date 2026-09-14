using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class FadeBehaviour : PlayableBehaviour
    {
        public FadeClipData Data = new FadeClipData();

        [NonSerialized] public FadeClip ClipAsset;
    }
}
