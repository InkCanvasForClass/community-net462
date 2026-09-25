using Ink_Canvas.Windows.SettingsViews.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 设置项 tag 解析器：根据属性路径（如 "Canvas.IsShowCursor"）反射读取 [SettingsTag] 特性。
    /// 结果静态缓存，避免重复反射。
    /// </summary>
    public static class SettingsTagResolver
    {
        private static readonly ConcurrentDictionary<string, SettingsTag> _cache =
            new ConcurrentDictionary<string, SettingsTag>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 解析属性路径上的静态标签（不含 Favourite，Favourite 由用户收藏状态决定）。
        /// </summary>
        public static SettingsTag GetTags(string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath)) return SettingsTag.None;
            return _cache.GetOrAdd(propertyPath.Trim(), ResolveTagsCore);
        }

        private static SettingsTag ResolveTagsCore(string path)
        {
            var parts = path.Split('.');
            if (parts.Length == 0) return SettingsTag.None;

            Type type = typeof(Settings);
            PropertyInfo prop = null;
            foreach (var part in parts)
            {
                prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return SettingsTag.None;
                type = prop.PropertyType;
            }
            if (prop == null) return SettingsTag.None;

            var attr = prop.GetCustomAttribute<SettingsTagAttribute>();
            return attr?.Tags ?? SettingsTag.None;
        }

        /// <summary>
        /// 某个属性路径是否为用户收藏。
        /// </summary>
        public static bool IsFavourite(string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath)) return false;
            var favourites = SettingsManager.Settings?.FavouriteSettings;
            if (favourites == null) return false;
            return favourites.Contains(propertyPath.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 用户收藏的属性路径列表。
        /// </summary>
        public static List<string> GetFavouritePaths() =>
            SettingsManager.Settings?.FavouriteSettings ?? new List<string>();

        /// <summary>
        /// 全部标记了 [SettingsTag(Secret)] 的属性的 JsonProperty 名称集合（供反馈脱敏）。
        /// </summary>
        public static HashSet<string> GetSecretJsonPropertyNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectSecretNames(typeof(Settings), result);
            return result;
        }

        private static void CollectSecretNames(Type type, HashSet<string> result)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<SettingsTagAttribute>();
                if (attr != null && attr.Has(SettingsTag.Secret))
                {
                    var json = prop.GetCustomAttribute<JsonPropertyAttribute>();
                    result.Add(json?.PropertyName ?? prop.Name);
                }

                // 递归收集嵌套设置类（排除基元类型与常见容器）
                var pt = prop.PropertyType;
                if (pt.IsClass && pt != typeof(string) && pt.Namespace?.StartsWith("Ink_Canvas") == true)
                {
                    if (pt.IsGenericType)
                    {
                        var generic = pt.GetGenericArguments().FirstOrDefault();
                        if (generic != null && generic.IsClass && generic != typeof(string) &&
                            generic.Namespace?.StartsWith("Ink_Canvas") == true)
                        {
                            CollectSecretNames(generic, result);
                        }
                    }
                    else
                    {
                        CollectSecretNames(pt, result);
                    }
                }
            }
        }
    }
}
