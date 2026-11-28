using System;
using System.Collections.Generic;
using InkUInk.Model;

namespace InkUInk
{
    /// <summary>
    /// 把对象流里的 Canvas 组织成页面模型，实现 FILE-7、FILE-8、FILE-9、SAVE-14：
    /// 按 Workspace 注册顺序、pageIndex 排序；同页取第一个注册 Device 上的 Canvas；
    /// 同一页的多个图层按 layerIndex 叠加。
    /// </summary>
    internal static class UInkPageBuilder
    {
        private sealed class PageGroup
        {
            public string PageGuid;
            public int LastSeen;
            public uint PageIndex;
            public readonly List<UInkLayer> Layers = new List<UInkLayer>();
        }

        public static void Build(UInkDocument doc, List<UInkLayer> layers)
        {
            var handled = new HashSet<UInkLayer>();

            if (doc.Workspaces.Count > 0)
            {
                foreach (var workspace in doc.Workspaces)
                {
                    BuildWorkspace(doc, layers, workspace.Guid, handled);
                }
            }
            else
            {
                // HEXT-3：注册表缺失时建立隐式单例 Workspace。
                BuildWorkspace(doc, layers, null, handled);
            }

            // CANVAS-24：Canvas 引用了不存在的 Workspace 时，建立临时序列，只用于本次显示。
            var orphans = new List<UInkLayer>();
            foreach (var layer in layers)
            {
                if (!handled.Contains(layer)) orphans.Add(layer);
            }

            if (orphans.Count > 0)
            {
                doc.Warnings.Add($"有 {orphans.Count} 个 Canvas 引用了未注册的 Workspace，已作为临时页面显示");
                BuildWorkspace(doc, orphans, null, new HashSet<UInkLayer>());
            }
        }

        private static void BuildWorkspace(UInkDocument doc, List<UInkLayer> layers, string workspaceGuid, HashSet<UInkLayer> handled)
        {
            var groups = new List<PageGroup>();
            var map = new Dictionary<string, PageGroup>(StringComparer.Ordinal);

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (!GuidEquals(layer.Canvas.WorkspaceGuid, workspaceGuid)) continue;

                handled.Add(layer);

                string pageGuid = layer.Canvas.PageGuid;
                PageGroup group;
                if (!map.TryGetValue(pageGuid, out group))
                {
                    group = new PageGroup
                    {
                        PageGuid = pageGuid,
                        PageIndex = layer.Canvas.PageIndex,
                        LastSeen = i
                    };
                    map[pageGuid] = group;
                    groups.Add(group);
                }

                group.Layers.Add(layer);
                group.LastSeen = i;
            }

            // FILE-7 / SAVE-14：按 pageIndex 排序，相同值按最后出现的位置排序。
            groups.Sort(delegate (PageGroup a, PageGroup b)
            {
                int c = a.PageIndex.CompareTo(b.PageIndex);
                return c != 0 ? c : a.LastSeen.CompareTo(b.LastSeen);
            });

            foreach (var group in groups)
            {
                string deviceGuid = SelectDevice(doc, group, workspaceGuid);
                var pageLayers = new List<UInkLayer>();
                foreach (var layer in group.Layers)
                {
                    if (GuidEquals(layer.Canvas.DeviceGuid, deviceGuid)) pageLayers.Add(layer);
                }

                if (pageLayers.Count == 0) continue;

                pageLayers.Sort(delegate (UInkLayer a, UInkLayer b)
                {
                    return a.Canvas.LayerIndex.CompareTo(b.Canvas.LayerIndex);
                });

                var page = new UInkPage
                {
                    WorkspaceGuid = workspaceGuid,
                    DeviceGuid = deviceGuid,
                    PageGuid = group.PageGuid,
                    PageIndex = group.PageIndex,
                    PageNumber = pageLayers[0].Canvas.PageNumber
                };
                page.Layers.AddRange(pageLayers);
                doc.Pages.Add(page);
            }
        }

        /// <summary>FILE-9：优先第一个注册 Device；该 Device 上没有这一页时，按注册顺序取第一个有的。</summary>
        private static string SelectDevice(UInkDocument doc, PageGroup group, string workspaceGuid)
        {
            if (doc.Devices.Count == 0) return null;

            string first = doc.Devices[0].Guid;
            foreach (var layer in group.Layers)
            {
                if (GuidEquals(layer.Canvas.DeviceGuid, first)) return first;
            }

            foreach (var device in doc.Devices)
            {
                foreach (var layer in group.Layers)
                {
                    if (GuidEquals(layer.Canvas.DeviceGuid, device.Guid)) return device.Guid;
                }
            }

            return first;
        }

        private static bool GuidEquals(string a, string b)
        {
            bool aEmpty = string.IsNullOrEmpty(a);
            bool bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty && bEmpty) return true;
            if (aEmpty || bEmpty) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
