using System;

namespace Template
{
    public class MathUtils
    {
        public static bool PowerOf2(int v)
        {
            return (v & (v - 1)) == 0;
        }

        public static double Exp(double x)
        {
            return Math.Exp(x);
        }
    }
}
