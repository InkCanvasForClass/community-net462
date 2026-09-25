using System;

namespace Ink_Canvas
{
    /// <summary>
    /// 设置项标签（可扩展：新增 tag 时在此枚举追加一个位即可）。
    /// </summary>
    [Flags]
    public enum SettingsTag
    {
        None = 0,

        /// <summary>
        /// 用户收藏的设置项（动态，由用户在界面上收藏/取消收藏，不由特性声明）。
        /// </summary>
        Favourite = 1 << 0,

        /// <summary>
        /// 打开这些设置项可能导致程序行为异常。
        /// </summary>
        Warn = 1 << 1,

        /// <summary>
        /// 更新中的新增功能。
        /// </summary>
        New = 1 << 2,

        /// <summary>
        /// 实验性设置项。
        /// </summary>
        Experimental = 1 << 3,

        /// <summary>
        /// 在反馈时应该屏蔽的设置项。
        /// </summary>
        Secret = 1 << 4,
    }

    /// <summary>
    /// 声明设置项上的标签。作用于 Settings.cs 中的属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SettingsTagAttribute : Attribute
    {
        public SettingsTag Tags { get; }

        public SettingsTagAttribute(SettingsTag tags)
        {
            Tags = tags;
        }

        public bool Has(SettingsTag tag) => (Tags & tag) == tag;
    }
}
