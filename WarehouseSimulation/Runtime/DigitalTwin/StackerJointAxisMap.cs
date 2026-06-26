using NonsensicalKit.DigitalTwin;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin
{
    /// <summary>
    /// 堆垛机三轴与 <see cref="JointController.Joints"/> 数组下标的约定映射。
    /// 在 JointController 中按顺序配置：0=层向、1=排向、2=货叉（列向伸叉）。
    /// </summary>
    internal static class StackerJointAxisMap
    {
        public const int LevelJointIndex = 0;
        public const int RowJointIndex = 1;
        public const int ForkJointIndex = 2;
        public const int RequiredJointCount = 3;

        public static bool IsValid(JointController controller) =>
            controller != null
            && controller.Joints != null
            && controller.Joints.Length >= RequiredJointCount;

        public static bool TryFillValues(
            JointController controller,
            in StackerAxisValueResolver.StackerAxisValues axes,
            float[] buffer)
        {
            if (!IsValid(controller) || buffer == null || buffer.Length < controller.Joints.Length)
            {
                return false;
            }

            var current = controller.GetJointsValue();
            System.Array.Copy(current, buffer, controller.Joints.Length);
            buffer[LevelJointIndex] = axes.Level;
            buffer[RowJointIndex] = axes.Row;
            buffer[ForkJointIndex] = axes.Fork;
            return true;
        }

        public static void SetInitialValues(
            JointController controller,
            in StackerAxisValueResolver.StackerAxisValues origin)
        {
            if (!IsValid(controller))
            {
                return;
            }

            controller.Joints[LevelJointIndex].InitialValue = origin.Level;
            controller.Joints[RowJointIndex].InitialValue = origin.Row;
            controller.Joints[ForkJointIndex].InitialValue = origin.Fork;
        }

        public static bool TryGetRowJointNode(JointController controller, out Transform node)
        {
            node = null;
            if (!IsValid(controller))
            {
                return false;
            }

            node = controller.Joints[RowJointIndex].JointsNode;
            return node != null;
        }
    }
}
