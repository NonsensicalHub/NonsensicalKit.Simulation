using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    public enum SimCargoSubTaskPlaybackState
    {
        Pending,
        Active,
        Completed,
    }

    /// <summary>Inspector 中单条子任务的校验展示行。</summary>
    [Serializable]
    public struct SimCargoSubTaskDisplayEntry
    {
        public int Sequence;
        public int SubTaskId;
        public SimSubTaskKind Kind;
        public string KindLabel;
        public double StartSimTime;
        public double EndSimTime;
        public double DurationSeconds;
        public string Detail;
        public SimCargoSubTaskPlaybackState PlaybackState;
        public float Progress;
        public bool IsActiveAtCurrentTime;

        public int FromNodeIndex;
        public int ToNodeIndex;
        public int SegmentSlotIndex;
        public int StackerId;
        public GridIndex Slot;
        public int InfeedPortIndex;
        public int PickupPointIndex;
    }
}
