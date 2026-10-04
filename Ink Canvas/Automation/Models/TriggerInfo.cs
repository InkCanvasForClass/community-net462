using System;

namespace Ink_Canvas.WorkflowAutomation.Models
{
    /// <summary>
    /// 自动化触发器注册信息。
    /// </summary>
    public class TriggerInfo
    {
        /// <summary>
        /// 触发器 ID
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 触发器名称（资源键或字面量）。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 本地化后的显示名称。
        /// </summary>
        public string DisplayName => Ink_Canvas.Properties.AutomationStrings.GetString(Name) ?? Name;

        /// <summary>
        /// 触发器图标（字符串标识，如 Unicode 或图标名）
        /// </summary>
        public string IconKind { get; }

        /// <summary>
        /// 触发器类型
        /// </summary>
        public Type TriggerType { get; internal set; }

        /// <summary>
        /// 设置界面类型
        /// </summary>
        public Type SettingsControlType { get; set; }

        /// <summary>
        /// 设置数据类型
        /// </summary>
        public Type SettingsType { get; internal set; }

        public TriggerInfo(string id, string name, string iconKind = "ClockOutline")
        {
            Id = id;
            Name = name;
            IconKind = iconKind;
        }
    }
}
