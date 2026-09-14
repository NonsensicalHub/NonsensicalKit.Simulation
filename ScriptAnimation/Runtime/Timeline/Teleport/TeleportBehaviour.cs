using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class TeleportBehaviour : PlayableBehaviour
    {
        public TeleportClipData Data = new TeleportClipData();

        [NonSerialized] public TeleportClip ClipAsset;
        [NonSerialized] public PathNode TargetNode;
    }
}
