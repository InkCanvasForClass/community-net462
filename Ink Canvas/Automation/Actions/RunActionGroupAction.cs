using Ink_Canvas.WorkflowAutomation.Models;

namespace Ink_Canvas.WorkflowAutomation.Actions
{
    /// <summary>
    /// 「执行动作组」行动的设置
    /// </summary>
    public class RunActionGroupActionSettings
    {
        /// <summary>
        /// 引用的动作组 Guid
        /// </summary>
        [GroupSelector(GroupKind.Action)]
        public string GroupId { get; set; } = "";
    }

    /// <summary>
    /// 执行一个可复用动作组的行动。
    /// </summary>
    public static class RunActionGroupAction
    {
        public const string ActionId = "inkcanvas.runactiongroup";
    }
}
