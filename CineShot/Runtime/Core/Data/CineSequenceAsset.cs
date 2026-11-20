using System;
using System.Collections.Generic;
using UnityEngine;

namespace CineShot.Core
{
    [Serializable]
    public class StoryboardRow
    {
        public string shotGuid;
        public int rowIndex;
        public double duration = 4.0;
        public double gap;
        public string note;
        public ShotBlendMode blendMode = ShotBlendMode.Blend;
        public float blendDuration = 0.5f;
    }

    [CreateAssetMenu(menuName = "CineShot/分镜资产", fileName = "Seq_")]
    public class CineSequenceAsset : ScriptableObject
    {
        public TimelineAssetRef timeline;
        public List<StoryboardRow> rows = new List<StoryboardRow>();

        [Serializable]
        public class TimelineAssetRef
        {
            public UnityEngine.Object asset;
        }
    }
}
