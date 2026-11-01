using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ink_Canvas.Controls.Toolbar.BoardToolbar
{
    public static class BoardToolbarRegistry
    {
        private static List<IBoardToolbarItem> _items;
        private static readonly string ConfigSubDir = Path.Combine("Configs", "BoardToolbarConfigs");

        #region 视频展台专用组件

        public const string BoothCaptureItemId = "board.boothCapture";
        public const string BoothRotateItemId = "board.boothRotate";
        public const string BoothCorrectionItemId = "board.boothCorrection";
        public const string BoothMenuItemId = "board.boothMenu";
        public const string BoothExpandItemId = "board.boothExpand";

        /// <summary>是否属于"仅视频展台模式显示"的组件。</summary>
        public static bool IsBoothItem(string id)
            => !string.IsNullOrEmpty(id)
               && id.StartsWith("board.booth", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 本次渲染是否处于视频展台模式。用于让设置了「仅视频展台显示」的组件
        /// 在非展台模式下隐藏（渲染前由 MainWindow 设置）。
        /// </summary>
        public static bool RenderInBoothMode { get; set; }

        #endregion

        public static IReadOnlyList<IBoardToolbarItem> Discover()
        {
            if (_items != null) return _items;

            var itemType = typeof(IBoardToolbarItem);
            Type[] types;
            try
            {
                types = Assembly.GetExecutingAssembly().GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // On Windows 8, GetTypes() may fail if some types reference Win10-only APIs.
                // We can still work with the types that were successfully loaded.
                types = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: GetTypes() 抛出 ReflectionTypeLoadException，成功加载 {types.Length} 个类型，失败 {ex.LoaderExceptions?.Length ?? 0} 个", LogHelper.LogType.Warning);
            }

            _items = types
                .Where(t => !t.IsAbstract && !t.IsInterface && itemType.IsAssignableFrom(t))
                .Select(t =>
                {
                    try { return (IBoardToolbarItem)Activator.CreateInstance(t); }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"BoardToolbarRegistry: 实例化 {t.FullName} 失败: {ex.Message}", LogHelper.LogType.Warning);
                        return null;
                    }
                })
                .Where(i => i != null)
                .ToList();
            return _items;
        }

        public static IBoardToolbarItem FindItem(string id)
        {
            var items = Discover();
            return items.FirstOrDefault(i => i.Id == id);
        }

        public static FrameworkElement BuildView(string id, IBoardToolbarHost host)
        {
            var item = FindItem(id);
            if (item == null)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 未找到组件 [{id}]", LogHelper.LogType.Warning);
                return null;
            }

            try
            {
                var view = item.BuildView(host);
                if (view != null)
                {
                    host.RegisterView(id, view);
                }
                return view;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 构建 {id} 失败: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        public static List<FrameworkElement> BuildGroup(IBoardToolbarHost host, List<BoardToolbarComponentEntry> components, string areaId = null)
        {
            var views = new List<FrameworkElement>();
            var items = Discover();
            var itemMap = items.ToDictionary(i => i.Id, i => i);

            for (int i = 0; i < components.Count; i++)
            {
                var entry = components[i];

                if (!itemMap.TryGetValue(entry.Id, out var item))
                {
                    LogHelper.WriteLogToFile($"BoardToolbarRegistry: 未找到组件 [{entry.Id}]", LogHelper.LogType.Warning);
                    continue;
                }

                try
                {
                    FrameworkElement view;
                    if (item is Items.BoardPageInfoToolItem pageInfoItem)
                    {
                        view = Items.BoardPageInfoToolItem.BuildPageInfoView(host, areaId);
                    }
                    else
                    {
                        view = item.BuildView(host);
                    }

                    if (view != null)
                    {
                        var position = ComputeButtonPosition(i, components.Count);
                        item.ApplyPosition(view, position);
                        ApplyComponentSettings(view, entry);
                        host.RegisterView(entry.Id, view);
                        if (areaId != null)
                            host.RegisterView($"{entry.Id}.{areaId}", view);
                        views.Add(view);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"BoardToolbarRegistry: 构建 {entry.Id} 失败: {ex.Message}", LogHelper.LogType.Error);
                }
            }

            return views;
        }

        internal static ButtonPosition ComputeButtonPosition(int index, int totalCount)
        {
            if (totalCount == 1) return ButtonPosition.Single;
            if (index == 0) return ButtonPosition.First;
            if (index == totalCount - 1) return ButtonPosition.Last;
            return ButtonPosition.Middle;
        }

        public static List<FrameworkElement> BuildGroup(IBoardToolbarHost host, params string[] ids)
        {
            var components = ids.Select(id => new BoardToolbarComponentEntry { Id = id }).ToList();
            return BuildGroup(host, components);
        }

        private static void ApplyComponentSettings(FrameworkElement view, BoardToolbarComponentEntry entry)
        {
            if (view == null || entry == null) return;

            var fixedWidth = entry.GetSettingDouble("fixedWidth");
            if (fixedWidth.HasValue && fixedWidth.Value > 0)
                view.Width = fixedWidth.Value;

            var fixedHeight = entry.GetSettingDouble("fixedHeight");
            if (fixedHeight.HasValue && fixedHeight.Value > 0)
                view.Height = fixedHeight.Value;

            var minWidth = entry.GetSettingDouble("minWidth");
            if (minWidth.HasValue && minWidth.Value > 0)
                view.MinWidth = minWidth.Value;

            var minHeight = entry.GetSettingDouble("minHeight");
            if (minHeight.HasValue && minHeight.Value > 0)
                view.MinHeight = minHeight.Value;

            var opacity = entry.GetSettingDouble("opacity");
            if (opacity.HasValue)
                view.Opacity = Math.Min(1, Math.Max(0, opacity.Value));

            // 按规则隐藏：仅在视频展台模式下显示的组件，在普通白板模式下不渲染。
            // 展台专用组件（board.booth*）本身也只服务于展台，一律遵循该规则。
            bool boothOnly = entry.GetSettingBool(ShowInBoothOnlySettingKey) || IsBoothItem(entry.Id);
            if (boothOnly && !RenderInBoothMode)
                view.Visibility = Visibility.Collapsed;
        }

        public static Border CreateGroupBorder(List<FrameworkElement> views, Orientation orientation = Orientation.Horizontal)
        {
            var panel = new StackPanel
            {
                Orientation = orientation,
                Margin = new Thickness(0)
            };

            foreach (var view in views)
            {
                panel.Children.Add(view);
            }

            var border = new Border
            {
                CornerRadius = new CornerRadius(5, 5, 5, 5),
                Margin = new Thickness(0),
                Child = panel
            };
            border.SetResourceReference(Border.BackgroundProperty, "FloatingBarBackgroundBrush");

            return border;
        }

        #region Config file system

        public static string GetConfigDirectory()
        {
            return Path.Combine(App.RootPath, ConfigSubDir);
        }

        public static string GetConfigFilePath(string name)
        {
            return Path.Combine(GetConfigDirectory(), name + ".json");
        }

        public static BoardToolbarLayoutSettings LoadConfigFile(string name)
        {
            var path = GetConfigFilePath(name);
            if (!File.Exists(path))
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 配置文件不存在 [{path}]", LogHelper.LogType.Warning);
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var layout = JsonConvert.DeserializeObject<BoardToolbarLayoutSettings>(json);
                if (layout?.Areas == null || layout.Areas.Count == 0)
                {
                    LogHelper.WriteLogToFile($"BoardToolbarRegistry: 配置 [{name}] 内容为空或无效", LogHelper.LogType.Warning);
                    return null;
                }
                return layout;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 加载配置 [{name}] 失败: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        public static void SaveConfigFile(string name, BoardToolbarLayoutSettings layout)
        {
            try
            {
                var dir = GetConfigDirectory();
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var path = GetConfigFilePath(name);
                var json = JsonConvert.SerializeObject(layout, Formatting.Indented);
                // 临时文件 + File.Replace/Move 原子替换，避免断电/进程被杀导致 default.json
                // 停在 0 字节或半截，下次启动 LoadConfigFile 反序列化失败→fallback CreateDefault，
                // 用户整套自定义布局静默丢失。
                var tmpPath = path + ".tmp";
                try
                {
                    File.WriteAllText(tmpPath, json);
                    if (File.Exists(path))
                        File.Replace(tmpPath, path, null);
                    else
                        File.Move(tmpPath, path);
                }
                catch (Exception innerEx)
                {
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"[Toolbar] BoardToolbarRegistry 原子写入失败后删除临时文件 {Path.GetFileName(tmpPath)} 失败: {ex.Message}", LogHelper.LogType.Info);
                    }
                    throw new Exception($"原子写入失败: {innerEx.Message}", innerEx);
                }
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 保存配置 [{name}] 成功", LogHelper.LogType.Info);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 保存配置 [{name}] 失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        public static List<string> ListConfigFiles()
        {
            try
            {
                var dir = GetConfigDirectory();
                if (!Directory.Exists(dir))
                    return new List<string> { "default" };

                var files = Directory.GetFiles(dir, "*.json");
                var names = new List<string>();
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
                if (names.Count == 0)
                    names.Add("default");
                names.Sort();
                return names;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 列出配置失败: {ex.Message}", LogHelper.LogType.Error);
                return new List<string> { "default" };
            }
        }

        public static void DeleteConfigFile(string name)
        {
            try
            {
                var path = GetConfigFilePath(name);
                if (File.Exists(path))
                    File.Delete(path);

                var bakPath = path + ".bak";
                if (File.Exists(bakPath))
                    File.Delete(bakPath);

                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 删除配置 [{name}]", LogHelper.LogType.Info);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 删除配置 [{name}] 失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private static volatile bool _defaultConfigEnsured;

        public static void EnsureDefaultConfigExists()
        {
            if (_defaultConfigEnsured) return;

            var dir = GetConfigDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var defaultPath = GetConfigFilePath("default");
            if (!File.Exists(defaultPath))
            {
                SaveConfigFile("default", BoardToolbarLayoutSettings.CreateDefault());
                LogHelper.WriteLogToFile("BoardToolbarRegistry: 首次启动，创建 default.json", LogHelper.LogType.Info);
            }

            _defaultConfigEnsured = true;
        }

        public static BoardToolbarLayoutSettings LoadActiveConfig()
        {
            var layout = LoadConfigFile("default");
            if (layout != null && layout.Areas != null && layout.Areas.Count > 0)
                return layout;

            return BoardToolbarLayoutSettings.CreateDefault();
        }

        #endregion

        #region 视频展台组件首次播种

        /// <summary>
        /// 首次运行时把展台专用组件播种到"中间区域末尾（中间栏最右侧）"的独立分组里，只播一次。
        /// 播种后用户如果手动删除了它们，不会再次自动恢复。
        /// 另外会把旧版本误放在右侧区域的同名分组迁移到中间区域。
        /// </summary>
        public static void EnsureBoothComponentsSeeded()
        {
            try
            {
                if (SettingsManager.Settings == null) return;

                var configName = SettingsManager.Settings.BoardToolbarConfigName;
                if (string.IsNullOrWhiteSpace(configName)) configName = "default";

                var layout = LoadConfigFile(configName);
                if (layout == null) return;
                layout.Areas ??= new List<BoardToolbarAreaEntry>();

                var centerArea = layout.Areas.FirstOrDefault(a => string.Equals(a.Id, "center", StringComparison.OrdinalIgnoreCase));
                bool changed = false;

                // 迁移：旧版本把分组放在右侧区域，这里搬到中间区域末尾。
                // 仅在中间区域还没有该分组时执行，避免用户之后手动摆放的位置被反复挪动。
                var hasCenterGroup = centerArea?.Groups?.Any(g => string.Equals(g.Id, "videoBooth", StringComparison.OrdinalIgnoreCase)) == true;
                var rightArea = layout.Areas.FirstOrDefault(a => string.Equals(a.Id, "right", StringComparison.OrdinalIgnoreCase));
                var misplaced = rightArea?.Groups?.FirstOrDefault(g => string.Equals(g.Id, "videoBooth", StringComparison.OrdinalIgnoreCase));
                if (misplaced != null && !hasCenterGroup)
                {
                    rightArea.Groups.Remove(misplaced);
                    if (centerArea == null)
                    {
                        centerArea = new BoardToolbarAreaEntry { Id = "center", Groups = new List<BoardToolbarGroupEntry>() };
                        layout.Areas.Add(centerArea);
                    }
                    centerArea.Groups ??= new List<BoardToolbarGroupEntry>();
                    centerArea.Groups.Add(misplaced);
                    changed = true;
                    LogHelper.WriteLogToFile("BoardToolbarRegistry: 展台分组已从右侧区域迁移到中间区域", LogHelper.LogType.Info);
                }

                // 播种（只做一次）：用户删掉后不再补回
                if (!SettingsManager.Settings.BoothToolbarSeeded)
                {
                    if (centerArea == null)
                    {
                        centerArea = new BoardToolbarAreaEntry { Id = "center", Groups = new List<BoardToolbarGroupEntry>() };
                        layout.Areas.Add(centerArea);
                    }
                    centerArea.Groups ??= new List<BoardToolbarGroupEntry>();

                    var group = centerArea.Groups.FirstOrDefault(g => string.Equals(g.Id, "videoBooth", StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                    {
                        group = new BoardToolbarGroupEntry
                        {
                            Id = "videoBooth",
                            Components = new List<BoardToolbarComponentEntry>()
                        };
                        // 追加到中间区域末尾 = 中间栏最右侧（紧跟在原有工具之后）
                        centerArea.Groups.Add(group);
                    }
                    group.Components ??= new List<BoardToolbarComponentEntry>();

                    foreach (var id in BoothToolbarComponentIds)
                    {
                        if (group.Components.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var entry = new BoardToolbarComponentEntry { Id = id };
                        entry.SetSetting(ShowInBoothOnlySettingKey, true);
                        group.Components.Add(entry);
                        changed = true;
                    }

                    SettingsManager.Settings.BoothToolbarSeeded = true;
                    SettingsManager.SaveSettingsToFile();
                    LogHelper.WriteLogToFile(
                        $"BoardToolbarRegistry: 已把展台组件播种到 center→videoBooth（末尾）(配置={configName})",
                        LogHelper.LogType.Info);
                }

                if (changed)
                    SaveConfigFile(configName, layout);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 播种展台组件失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 首次运行时把独立的「展开」组件放到右侧区域最左侧（"新页面"按钮所在组的第一个位置）。
        /// 展台模式下"新页面"被隐藏，正好由它占位；只播一次，用户删除后不再补回。
        /// </summary>
        public static void EnsureBoothExpandSeeded()
        {
            try
            {
                if (SettingsManager.Settings == null) return;
                if (SettingsManager.Settings.BoothExpandToolbarSeeded) return;

                var configName = SettingsManager.Settings.BoardToolbarConfigName;
                if (string.IsNullOrWhiteSpace(configName)) configName = "default";

                var layout = LoadConfigFile(configName);
                if (layout == null) return;
                layout.Areas ??= new List<BoardToolbarAreaEntry>();

                var rightArea = layout.Areas.FirstOrDefault(a => string.Equals(a.Id, "right", StringComparison.OrdinalIgnoreCase));
                if (rightArea == null)
                {
                    rightArea = new BoardToolbarAreaEntry { Id = "right", Groups = new List<BoardToolbarGroupEntry>() };
                    layout.Areas.Add(rightArea);
                }
                rightArea.Groups ??= new List<BoardToolbarGroupEntry>();

                var addGroup = rightArea.Groups.FirstOrDefault(g => string.Equals(g.Id, "addPage", StringComparison.OrdinalIgnoreCase));
                if (addGroup == null)
                {
                    addGroup = new BoardToolbarGroupEntry
                    {
                        Id = "addPage",
                        Components = new List<BoardToolbarComponentEntry>()
                    };
                    rightArea.Groups.Insert(0, addGroup);
                }
                addGroup.Components ??= new List<BoardToolbarComponentEntry>();

                if (!addGroup.Components.Any(c => string.Equals(c.Id, BoothExpandItemId, StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = new BoardToolbarComponentEntry { Id = BoothExpandItemId };
                    entry.SetSetting(ShowInBoothOnlySettingKey, true);
                    // 放在"新页面"左侧，展台下正好占据被隐藏按钮的位置
                    addGroup.Components.Insert(0, entry);
                    SaveConfigFile(configName, layout);
                }

                SettingsManager.Settings.BoothExpandToolbarSeeded = true;
                SettingsManager.SaveSettingsToFile();
                LogHelper.WriteLogToFile(
                    $"BoardToolbarRegistry: 已把「展开」组件放到 right→addPage 首位 (配置={configName})",
                    LogHelper.LogType.Info);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"BoardToolbarRegistry: 播种「展开」组件失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>组件设置键：仅在视频展台模式下显示该组件。</summary>
        public const string ShowInBoothOnlySettingKey = "showInBoothOnly";

        /// <summary>播种到中间栏末尾分组的展台组件（顺序即默认显示顺序）。</summary>
        private static readonly string[] BoothToolbarComponentIds =
        {
            BoothCaptureItemId,
            BoothRotateItemId,
            BoothCorrectionItemId,
            BoothMenuItemId
        };

        #endregion

        #region Rebuild methods

        public static void RebuildToolbar(IBoardToolbarHost host, Panel leftContainer, Panel centerContainer, Panel rightContainer)
        {
            var layout = LoadActiveConfig();
            RebuildToolbar(host, leftContainer, centerContainer, rightContainer, layout);
        }

        /// <summary>
        /// 重建白板工具栏。<paramref name="boothMode"/> 决定「仅视频展台显示」的组件是否可见。
        /// </summary>
        public static void RebuildToolbar(IBoardToolbarHost host, Panel leftContainer, Panel centerContainer, Panel rightContainer,
            BoardToolbarLayoutSettings layout, bool boothMode = false)
        {
            if (layout == null)
                layout = BoardToolbarLayoutSettings.CreateDefault();

            RenderInBoothMode = boothMode;

            foreach (var area in layout.Areas)
            {
                switch (area.Id.ToLower())
                {
                    case "left":
                        RebuildArea(host, leftContainer, area);
                        break;
                    case "center":
                        RebuildArea(host, centerContainer, area);
                        break;
                    case "right":
                        RebuildArea(host, rightContainer, area);
                        break;
                }
            }
        }

        private static void RebuildArea(IBoardToolbarHost host, Panel container, BoardToolbarAreaEntry area)
        {
            if (container == null) return;

            container.Children.Clear();

            bool isFirst = true;
            foreach (var group in area.Groups)
            {
                var views = BuildGroup(host, group.Components, area.Id);
                // 整组都被"按规则隐藏"时（如非展台模式下的展台分组）不渲染空胶囊
                if (views.Count > 0 && views.All(v => v.Visibility == Visibility.Collapsed))
                    continue;
                if (views.Count > 0)
                {
                    var groupBorder = CreateGroupBorder(views);
                    if (!isFirst)
                    {
                        groupBorder.Margin = new Thickness(3, 0, 0, 0);
                    }
                    container.Children.Add(groupBorder);
                    isFirst = false;
                }
            }
        }

        public static void RebuildLeftToolbar(IBoardToolbarHost host, Panel container) { }

        public static void RebuildCenterToolbar(IBoardToolbarHost host, Panel container) { }

        public static void RebuildRightToolbar(IBoardToolbarHost host, Panel container) { }

        #endregion
    }
}
