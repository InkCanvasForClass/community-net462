using Ink_Canvas.WorkflowAutomation.Models;
using System;

namespace Ink_Canvas.WorkflowAutomation.Abstractions
{
    /// <summary>
    /// 规则集服务接口。
    /// 对齐 ClassIsland 的 IRulesetService。
    /// </summary>
    public interface IRulesetService
    {
        /// <summary>
        /// 规则状态更新事件
        /// </summary>
        event EventHandler StatusUpdated;

        /// <summary>
        /// 判断指定的规则集是否成立
        /// </summary>
        bool IsRulesetSatisfied(Ruleset ruleset);

        /// <summary>
        /// 注册规则处理程序
        /// </summary>
        void RegisterRuleHandler(string id, RuleRegistryInfo.HandleDelegate handler);

        /// <summary>
        /// 手动通知规则状态已更新
        /// </summary>
        void NotifyStatusChanged();
    }
}
