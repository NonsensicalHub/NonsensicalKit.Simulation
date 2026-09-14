using System;
using UnityEngine.Playables;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class FilmWrapBehaviour : PlayableBehaviour
    {
        public FilmWrapClipData Data = new FilmWrapClipData();

        [NonSerialized] public FilmWrapClip ClipAsset;
    }
}
