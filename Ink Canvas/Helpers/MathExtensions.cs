using System;

namespace Ink_Canvas.Helpers
{
    public static class MathExtensions
    {
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static T GetLast<T>(this T[] array)
        {
            if (array == null || array.Length == 0)
                throw new ArgumentException("Array cannot be null or empty");
            return array[array.Length - 1];
        }
    }
}
