# Changelog

本文件记录 InkCanvasForClass Community Edition 的版本变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## 1.7.19.4-ep01

- 提交范围：`131a29ce`（`1.7.19.4-net462`）.. `8e174cb6`
- 时间范围：2026-07-26 ~ 2026-10-10
- 相关 PR：#4、#5、#7、#9、#10、#11、#12（`community-net462`）

### 新增

- **设置项 tag 系统**：设置卡片头部显示标签 chip 与收藏标记。标签由 `SettingsTag` 特性驱动，通过反射解析属性路径并静态缓存，跨页搜索与收藏定位均基于真实设置键。[#5](https://github.com/InkCanvasForClass/community-net462/pull/5)
- **设置项「更多」按钮**：卡片头部新增更多按钮，菜单提供「复制设置键」「复制 URL」「收藏/取消收藏」。复制的 URL 形如 `icc://settings/entry?page=<页面>&path=<设置键>`，可直接定位到指定设置项。[#7](https://github.com/InkCanvasForClass/community-net462/pull/7)
- **重做设置主页**：新增 PowerToys 风格仪表盘，包含应用信息条、常用设置磁贴、快捷键一览、收藏夹列表。[#10](https://github.com/InkCanvasForClass/community-net462/pull/10)
- **搜索框移到设置窗口标题栏**：搜索入口从导航栏移至标题栏居中，宽度固定 320。[#10](https://github.com/InkCanvasForClass/community-net462/pull/10)
- **墨迹/截图管理页**：设置 → 存储 → 「管理」。自动扫描自动保存目录，将墨迹文件（`.icstk` / `.xml` / `.zip`）与截图（`.png`）分列表展示，支持点击列头排序、多选批量删除、在资源管理器中定位、直接打开。[#11](https://github.com/InkCanvasForClass/community-net462/pull/11)
- **自动化：状态组**：可将一组规则保存为命名状态组，在其他工作流中通过「状态组满足」规则引用，实现条件复用。[#12](https://github.com/InkCanvasForClass/community-net462/pull/12)
- **自动化：动作组**：可将一组动作保存为命名动作组，通过「执行动作组」行动调用，支持手动触发与恢复（revert）。[#12](https://github.com/InkCanvasForClass/community-net462/pull/12)
- **自动化 UI 重做**：编辑器改为「列表 + 详情」分栏布局，顶部切换「工作流 / 状态组 / 动作组」三类。详情列宽按 20%（未选中）/ 50%（选中）自适应，用户手动拖动分隔条后不再自动调整；编辑器以右侧滑入 + 淡入动画呈现。[#12](https://github.com/InkCanvasForClass/community-net462/pull/12)
- **小白板设置页**：新增独立设置页，管理小 whiteboard 的启用状态、尺寸、不透明度、与 PPT 同步等。[#4](https://github.com/InkCanvasForClass/community-net462/pull/4)
- **直线归一化阈值**：墨迹识别页新增阈值滑块。[#4](https://github.com/InkCanvasForClass/community-net462/pull/4)
- **窗口 Chrome 渲染开关**：显示页新增开关，切换窗口渲染路径（WindowChrome/DWM GPU 合成 vs AllowsTransparency 软件合成）。仅在窗口构造时读取，修改后需重启生效；Windows 7 上自动回退旧路径。[#12](https://github.com/InkCanvasForClass/community-net462/pull/12)
- **设置窗口预热开关**：性能页新增开关，启动后在屏外预先创建设置窗口，使首次打开设置无需等待 XAML 解析与首渲染。默认关闭，代价是常驻内存占用较高。[#12](https://github.com/InkCanvasForClass/community-net462/pull/12)

### 改进

- **设置整体重排**：设置导航树重构为分组结构（基础/显示/个性化/组件/通知/高级）。多个页面合并或更名：外观 + 高级 → 显示 / 屏幕触控，窗口 → PPT 联动，备份 → 配置，随机点名 → 点名，云存储 → 同步，自动化 → 自动收纳。白板的工具栏设置、菜单设置、外观设置三页合并为「白板」单页。[#4](https://github.com/InkCanvasForClass/community-net462/pull/4)
- **图标实现替换为 Fluent System Icons**：全应用图标字形体系由 Segoe Fluent Icons 换为 Fluent System Icons，并通过 `RenderTransform` 中心缩放（1.25×/1.3×）避免字形内边距差异导致的溢出裁切。[#9](https://github.com/InkCanvasForClass/community-net462/pull/9)
- **主题字体替换**：统一替换应用内字体声明，覆盖主窗口、计时器、崩溃窗口、反馈页、点名窗口、OOBE 及全部设置页。[#9](https://github.com/InkCanvasForClass/community-net462/pull/9) [#10](https://github.com/InkCanvasForClass/community-net462/pull/10)
- **版本号显示**：主页版本号改为四段式，并附带构建来源后缀。[#10](https://github.com/InkCanvasForClass/community-net462/pull/10)

### 修复

- **更新页无法打开**：`iNKORE.UI.WPF.Modern 0.10.2.1` 内嵌 `Expander` 在 Load 时若模板未应用会走动画路径并空引用，现于构造期提前应用模板。[#7](https://github.com/InkCanvasForClass/community-net462/pull/7)
- **收藏夹不显示**：搜索索引未覆盖折叠分组内与组顶行模板卡片，导致收藏项匹配不到。现补全索引枚举，并静默清理身份规则演进后失配的旧收藏。[#7](https://github.com/InkCanvasForClass/community-net462/pull/7)
- **部分卡片缺少更多按钮**：未选中 `TabItem` 与折叠 `SettingsExpander` 内的卡片在页面 Loaded 时尚未实化，取不到内层 `SettingsCard`。改为挂 `Loaded` 事件，待真正可见时补注入。[#7](https://github.com/InkCanvasForClass/community-net462/pull/7)
- **更多按钮位置错误**：左间距收窄至 4px。[#7](https://github.com/InkCanvasForClass/community-net462/pull/7)
- **本地化缺键**：补全 zh-MN / en-US 缺失资源约 6000 行，并同步 `key_mapping.json` 与 `Strings.cs` 映射表。修复浮动窗拦截器启用后拦截规则不生效的问题。[#10](https://github.com/InkCanvasForClass/community-net462/pull/10)
- **NativeImage 编译失败**：[#10](https://github.com/InkCanvasForClass/community-net462/pull/10)
- **非 Windows 环境无法编译**：[#4](https://github.com/InkCanvasForClass/community-net462/pull/4)
- **iNKORE Strings 惰性初始化前 NRE**：查未知键（如 `TextContextMenu`）时空回退，现跳过未初始化时的补丁安装。[#7](https://github.com/InkCanvasForClass/community-net462/pull/7)

### 性能

- **待机占用优化** [#7](https://github.com/InkCanvasForClass/community-net462/pull/7)
  - CPU 采样由每秒常驻改为按需，仅在写崩溃日志的故障路径触发
  - UIA 跨进程窗口枚举结果加 1.5s TTL 缓存（自动收纳 500ms 轮询可接受该滞后）
  - PowerPoint / WPS 演示进程存在性加 2s TTL 缓存；未连接且无相关进程时跳过 COM ROT 探测
  - 无工作流时停止 5s 兜底轮询；自动收纳关闭时停止窗口概览周期扫描
- **设置窗口复用** [#12](https://github.com/InkCanvasForClass/community-net462/pull/12)
  - 设置窗口关闭改为隐藏，XAML 解析、渲染、JIT 只付一次，消除「每次打开都卡」
  - 搜索/收藏索引改为后台增量构建，静态项即时可用，卡片项逐页构建，不再在打开窗口的那一帧同步实例化全部页面
  - 移除主窗口销毁的 WinEvent 钩子（无实际消费方）

### 开发者附录

<details>
<summary>构建、CI、依赖、TFM 变更</summary>

**CI**

- 构建矩阵：`nativeImage` 恢复 x86 / x64 组合（原被 exclude）
- `dotnet restore` 补上 `-p:platform` 与 `-p:EnableNativeImage`，与 build 步骤对齐
- 修复 `EnableNativeImage` 被引号包裹导致属性判定失效的问题
- 简化构建矩阵
- 新增 VSTO 产物上传（`InkCanvas.PowerPointAddIn` + `InkCanvas.PPTAgent.Contracts`，压缩级别 9）及构建摘要
- workflow 触发分支新增 `17194-ep`，并对 `net462` / `17194-ep` 启用 PR 触发
- super-linter 升级至 v9.0.0，`DEFAULT_BRANCH: 17194-ep`，启用 `SAVE_SUPER_LINTER_SUMMARY`

**TFM / 项目**

- `InkCanvas.PptAgent.Contracts` 重命名为 `InkCanvas.PPTAgent.Contracts`，并加入解决方案
- `InkCanvas.PPTAgent.Contracts` 多目标：`netstandard2.0` → `net462;netstandard2.0`
- VSTO `packages.config` 目标框架 `net472` → `net462`
- `InkCanvas.IACoreHelper` 文档中的 TFM 更正为 `net462`

**依赖**

| 包 | 变更 |
| --- | --- |
| Costura.Fody | 6.1.0 → 6.2.0（仅非 NativeImage 构建引入） |
| CommunityToolkit.Mvvm | 8.4.0 → 8.4.2 |
| Microsoft.Extensions.DependencyInjection | 6.0.0 → 10.0.1（修 NativeImage） |

**代码规范**

- `dotnet format` 与 prettier 全量格式化
- 移除主工程的 `NoWarn` 屏蔽（`CA1416;NU1701;MSB3270;CS8012;NETSDK1138`）

**配置格式兼容性**

- 自动化配置文件由裸工作流数组升级为 `{ workflows, stateGroups, actionGroups }` 对象。加载时检测旧格式并兼容，配置文件损坏时静默回退为空集合。

</details>