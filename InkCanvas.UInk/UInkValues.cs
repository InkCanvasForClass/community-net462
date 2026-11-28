using System;
using System.Collections.Generic;

namespace InkUInk
{
    /// <summary>
    /// 字段读取辅助。实现 TYPE-5：数值字段容错接受其他 MessagePack 数值编码，
    /// 但必须是无损转换，并通过字段自身的范围检查；bool 不算数值（TYPE-6）。
    /// </summary>
    public static class UInkValues
    {
        public static object Get(IDictionary<string, object> map, string key)
        {
            if (map == null || key == null) return null;
            object value;
            return map.TryGetValue(key, out value) ? value : null;
        }

        public static bool TryGetLong(object raw, out long value)
        {
            value = 0;
            if (raw is long) { value = (long)raw; return true; }
            if (raw is ulong)
            {
                ulong u = (ulong)raw;
                if (u > long.MaxValue) return false;
                value = (long)u;
                return true;
            }
            if (raw is double)
            {
                double d = (double)raw;
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                if (Math.Floor(d) != d) return false;
                if (d < long.MinValue || d > long.MaxValue) return false;
                value = (long)d;
                return true;
            }
            return false;
        }

        public static bool TryGetULong(object raw, out ulong value)
        {
            value = 0;
            if (raw is ulong) { value = (ulong)raw; return true; }
            if (raw is long)
            {
                long l = (long)raw;
                if (l < 0) return false;
                value = (ulong)l;
                return true;
            }
            if (raw is double)
            {
                double d = (double)raw;
                if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) return false;
                if (Math.Floor(d) != d) return false;
                if (d > ulong.MaxValue) return false;
                value = (ulong)d;
                return true;
            }
            return false;
        }

        public static bool TryGetDouble(object raw, out double value)
        {
            value = 0;
            if (raw is double) { value = (double)raw; return true; }
            if (raw is long)
            {
                long l = (long)raw;
                double d = l;
                if ((long)d != l) return false;   // 精确表示要求
                value = d;
                return true;
            }
            if (raw is ulong)
            {
                ulong u = (ulong)raw;
                double d = u;
                if ((ulong)d != u) return false;
                value = d;
                return true;
            }
            return false;
        }

        public static long? Long(IDictionary<string, object> map, string key)
        {
            long v;
            return TryGetLong(Get(map, key), out v) ? v : (long?)null;
        }

        public static uint? UInt32(IDictionary<string, object> map, string key)
        {
            long v;
            if (!TryGetLong(Get(map, key), out v)) return null;
            if (v < 0 || v > uint.MaxValue) return null;
            return (uint)v;
        }

        public static ushort? UInt16(IDictionary<string, object> map, string key)
        {
            long v;
            if (!TryGetLong(Get(map, key), out v)) return null;
            if (v < 0 || v > ushort.MaxValue) return null;
            return (ushort)v;
        }

        public static ulong? UInt64(IDictionary<string, object> map, string key)
        {
            ulong v;
            return TryGetULong(Get(map, key), out v) ? v : (ulong?)null;
        }

        public static int? Int32(IDictionary<string, object> map, string key)
        {
            long v;
            if (!TryGetLong(Get(map, key), out v)) return null;
            if (v < int.MinValue || v > int.MaxValue) return null;
            return (int)v;
        }

        public static double? Double(IDictionary<string, object> map, string key)
        {
            double v;
            return TryGetDouble(Get(map, key), out v) ? v : (double?)null;
        }

        /// <summary>有限数校验；NaN / Infinity 视为字段无效。</summary>
        public static double? FiniteDouble(IDictionary<string, object> map, string key)
        {
            double v;
            if (!TryGetDouble(Get(map, key), out v)) return null;
            if (double.IsNaN(v) || double.IsInfinity(v)) return null;
            return v;
        }

        public static float? Float(IDictionary<string, object> map, string key)
        {
            double? d = FiniteDouble(map, key);
            if (!d.HasValue) return null;
            if (d.Value < -float.MaxValue || d.Value > float.MaxValue) return null;
            return (float)d.Value;
        }

        public static string String(IDictionary<string, object> map, string key)
        {
            return Get(map, key) as string;
        }

        public static bool? Bool(IDictionary<string, object> map, string key)
        {
            object raw = Get(map, key);
            if (raw is bool) return (bool)raw;
            return null;
        }

        public static IDictionary<string, object> Map(IDictionary<string, object> map, string key)
        {
            return Get(map, key) as IDictionary<string, object>;
        }

        public static List<object> Array(IDictionary<string, object> map, string key)
        {
            return Get(map, key) as List<object>;
        }

        public static double[] FloatArray(IDictionary<string, object> map, string key, int requiredLength)
        {
            var arr = Array(map, key);
            if (arr == null) return null;
            if (requiredLength > 0 && arr.Count != requiredLength) return null;
            var result = new double[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                double v;
                if (!TryGetDouble(arr[i], out v)) return null;
                if (double.IsNaN(v) || double.IsInfinity(v)) return null;
                result[i] = v;
            }
            return result;
        }

        public static double? ClampOpacity(double? value)
        {
            if (!value.HasValue) return null;
            double v = value.Value;
            if (double.IsNaN(v) || double.IsInfinity(v)) return null;
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        public static bool IsUuid(string value)
        {
            if (value == null || value.Length != 36) return false;
            for (int i = 0; i < 36; i++)
            {
                char c = value[i];
                if (i == 8 || i == 13 || i == 18 || i == 23)
                {
                    if (c != '-') return false;
                }
                else if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
