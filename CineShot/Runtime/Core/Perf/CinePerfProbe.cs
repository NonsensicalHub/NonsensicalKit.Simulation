using System.Diagnostics;
using Unity.Profiling;

namespace CineShot.Core
{
    public static class CinePerfProbe
    {
        static readonly ProfilerMarker k_Solve = new ProfilerMarker("CineShot.SolveFrame");
        static readonly Stopwatch k_Clock = new Stopwatch();
        static readonly double[] k_Window = new double[256];
        static int k_Head;
        static int k_Count;
        static double k_Sum;

        public static void BeginSolve()
        {
            k_Solve.Begin();
            k_Clock.Restart();
        }

        public static void EndSolve()
        {
            k_Clock.Stop();
            k_Solve.End();
            Record(k_Clock.Elapsed.TotalMilliseconds);
        }

        static void Record(double ms)
        {
            k_Sum -= k_Window[k_Head];
            k_Sum += ms;
            k_Window[k_Head] = ms;
            k_Head = (k_Head + 1) & 255;
            if (k_Count < k_Window.Length)
                k_Count++;
        }

        public static double AverageMs
        {
            get { return k_Count > 0 ? k_Sum / k_Count : 0.0; }
        }
    }
}
