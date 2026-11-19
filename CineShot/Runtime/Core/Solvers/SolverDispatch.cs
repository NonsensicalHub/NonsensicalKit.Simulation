using UnityEngine.Assertions;

namespace CineShot.Core
{
    public static class SolverDispatch
    {
        static readonly SolveDelegate[] s_Table =
        {
            StaticSolver.Solve,
            DollyTrackSolver.Solve,
            OrbitSolver.Solve,
            PushPullSolver.Solve,
            CraneSolver.Solve,
            ShoulderSolver.Solve,
            HandheldSolver.Solve,
            SplineDollySolver.Solve,
            PanSolver.Solve
        };

        static SolverDispatch()
        {
            Assert.AreEqual((int)RigKind.Pan + 1, s_Table.Length, "RigKind 与求解器表长度不一致");
        }

        public static SolveDelegate Get(RigKind kind)
        {
            int i = (int)kind;
            if (i < 0 || i >= s_Table.Length)
                return StaticSolver.Solve;
            return s_Table[i];
        }

        public static void Solve(RigKind kind, in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            Get(kind)(in ctx, in p, ref pose);
        }
    }
}
