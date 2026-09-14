using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class SequentialPositionBehaviour : PlayableBehaviour
    {
        public SequentialPositionClipData Data = new SequentialPositionClipData();

        [NonSerialized] public SequentialPositionClip ClipAsset;
    }
}
