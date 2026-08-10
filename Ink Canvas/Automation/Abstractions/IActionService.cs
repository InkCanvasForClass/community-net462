using Ink_Canvas.WorkflowAutomation.Models;

namespace Ink_Canvas.WorkflowAutomation.Abstractions
{
    /// <summary>
    /// 行动服务接口。
    /// </summary>
    public interface IActionService
    {
        /// <summary>
        /// 注册行动处理程序
        /// </summary>
        void RegisterActionHandler(string id, ActionRegistryInfo.HandleDelegate handler);

        /// <summary>
        /// 取消注册行动处理程序。
        /// </summary>
        void UnregisterActionHandler(string id, ActionRegistryInfo.HandleDelegate handler);

        /// <summary>
        /// 注册行动恢复处理程序。
        /// 同一 handler 注册多次将自动去重。
        /// </summary>
        void RegisterRevertHandler(string id, ActionRegistryInfo.HandleDelegate handler);

        /// <summary>
        /// 取消注册行动恢复处理程序。
        /// </summary>
        void UnregisterRevertHandler(string id, ActionRegistryInfo.HandleDelegate handler);

        /// <summary>
        /// 触发行动组
        /// </summary>
        void Invoke(ActionSet actionSet);

        /// <summary>
        /// 恢复行动组
        /// </summary>
        void Revert(ActionSet actionSet);

        /// <summary>
        /// 行动是否有内建的恢复
        /// </summary>
        bool ExistRevertHandler(Models.Action action);
    }
}
