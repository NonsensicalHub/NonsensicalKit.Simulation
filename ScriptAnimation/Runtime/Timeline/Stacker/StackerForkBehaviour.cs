using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class StackerForkBehaviour : PlayableBehaviour
    {
        public StackerForkClipData Data = new StackerForkClipData();

        [NonSerialized] public StackerForkClip ClipAsset;
    }
}
