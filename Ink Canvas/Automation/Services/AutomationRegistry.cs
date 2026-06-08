using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ink_Canvas.WorkflowAutomation.Services
{
    /// <summary>
    /// 自动化注册中心，管理所有已注册的触发器、行动和规则。
    /// 对齐 ClassIsland：行动和规则字典集中存储在 AutomationRegistry 中，
    /// 触发器存储在此处并通过 DI 容器解析实例。
    /// </summary>
    public static class AutomationRegistry
    {
        /// <summary>
        /// 已注册的行动字典
        /// </summary>
        public static Dictionary<string, ActionRegistryInfo> RegisteredActions { get; } = new Dictionary<string, ActionRegistryInfo>();

        /// <summary>
        /// 已注册的规则字典
        /// </summary>
        public static Dictionary<string, RuleRegistryInfo> RegisteredRules { get; } = new Dictionary<string, RuleRegistryInfo>();

        /// <summary>
        /// 已注册的触发器列表
        /// </summary>
        public static List<TriggerInfo> RegisteredTriggers { get; } = new();

        /// <summary>
        /// 注册触发器（仅元数据，实例通过 DI 容器解析）
        /// </summary>
        public static void RegisterTrigger(TriggerInfo info)
        {
            if (RegisteredTriggers.Any(x => x.Id == info.Id))
            {
                throw new InvalidOperationException($"已注册ID为 {info.Id} 的触发器。");
            }
            RegisteredTriggers.Add(info);
        }

        /// <summary>
        /// 注册行动
        /// </summary>
        public static void RegisterAction(ActionRegistryInfo info)
        {
            RegisteredActions[info.Id] = info;
        }

        /// <summary>
        /// 注册规则
        /// </summary>
        public static void RegisterRule(RuleRegistryInfo info)
        {
            RegisteredRules[info.Id] = info;
        }

        /// <summary>
        /// 通过 DI 容器解析触发器实例。
        /// 对齐 ClassIsland 的 GetKeyedService&lt;TriggerBase&gt;(id)。
        /// </summary>
        public static TriggerBase ResolveTrigger(IServiceProvider serviceProvider, string id)
        {
            var info = RegisteredTriggers.FirstOrDefault(x => x.Id == id);
            if (info?.TriggerType == null) return null;

            try
            {
                return (TriggerBase)serviceProvider.GetService(info.TriggerType);
            }
            catch
            {
                return null;
            }
        }
    }
}
