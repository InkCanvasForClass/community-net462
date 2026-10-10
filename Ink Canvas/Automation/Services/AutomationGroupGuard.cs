using System;
using System.Collections.Generic;
using System.Threading;

namespace Ink_Canvas.WorkflowAutomation.Services
{
    /// <summary>
    /// 组引用重入保护，防止状态组/动作组相互引用或自引用导致无限递归（栈溢出）。
    /// 使用写时复制（copy-on-write）的 AsyncLocal 集合：进入时复制并加入 id，
    /// 退出时复制并移除。由于不原地修改集合，Task.Run 捕获的执行上下文快照仍然
    /// 保留已加入的 id，因此异步派发的动作组嵌套调用也能正确检测到重入。
    /// </summary>
    internal static class AutomationGroupGuard
    {
        private static readonly AsyncLocal<HashSet<string>> _active = new AsyncLocal<HashSet<string>>();

        /// <summary>
        /// 尝试进入组。若该组已在当前执行链中，返回 false。
        /// </summary>
        public static bool TryEnter(string id)
        {
            var current = _active.Value ?? new HashSet<string>();
            if (current.Contains(id)) return false;

            var next = new HashSet<string>(current) { id };
            _active.Value = next;
            return true;
        }

        /// <summary>
        /// 退出组。
        /// </summary>
        public static void Exit(string id)
        {
            var current = _active.Value;
            if (current == null || !current.Contains(id)) return;

            var next = new HashSet<string>(current);
            next.Remove(id);
            _active.Value = next;
        }
    }
}
