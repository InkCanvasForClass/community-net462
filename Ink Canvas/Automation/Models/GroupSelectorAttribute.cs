using System;

namespace Ink_Canvas.WorkflowAutomation.Models
{
    /// <summary>
    /// 组类型。
    /// </summary>
    public enum GroupKind
    {
        /// <summary>状态组</summary>
        State,
        /// <summary>动作组</summary>
        Action
    }

    /// <summary>
    /// 标记设置类中的组引用属性，使其在设置界面渲染为组选择下拉框。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class GroupSelectorAttribute : Attribute
    {
        public GroupKind Kind { get; }

        public GroupSelectorAttribute(GroupKind kind)
        {
            Kind = kind;
        }
    }
}
