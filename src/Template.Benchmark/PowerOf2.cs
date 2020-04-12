using BenchmarkDotNet.Attributes;

namespace Template.Benchmark
{
    public class PowerOf2
    {
        public const int n = 1000;
        [Benchmark(OperationsPerInvoke = n)]
        public bool Custom()
        {
            bool result = false;
            for (int i = 0; i < n; i++)
            {
                result = MathUtils.PowerOf2(1024);
            }
            return result;
        }
    }
}
