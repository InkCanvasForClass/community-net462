# 仓库工作指引

## 分支与依据

- PR 统一提交到 `17194-ep`。
- 当前主应用与 WPF 子项目目标为 `net462`，主应用使用 C# 10；`InkCanvas.PPTAgent.Contracts` 同时目标为 `net462;netstandard2.0`。不要把新版本 .NET 的 API 当作可用 API。
- 先读 [CONTRIBUTING.md](CONTRIBUTING.md)。UI、设置、工具栏改动分别参考 [控件规则](rules/xaml_controls.md)、[设置规则](rules/settings_pages.md)、[工具栏规则](rules/toolbar.md)。其中框架、个人绝对路径、页面列表及双字典注册说明有旧信息，以 `.csproj`、CI 和当前代码为准。

## 构建与验证

在仓库根目录、Windows 环境使用 .NET SDK；不要复制 `rules/build.md` 中其他开发者的机器路径。

```powershell
dotnet restore "Ink Canvas.sln" -p:Platform=x86
dotnet build "Ink Canvas.sln" -c Release -p:Platform=x86 --no-restore
```

聚焦验证可直接构建主应用或独立设置目录查看器；`dotnet build` 默认会还原依赖：

```powershell
dotnet build "Ink Canvas/InkCanvasForClass.csproj" -c Release -p:Platform=x86
dotnet build "InkCanvas.SettingsTreeView/InkCanvas.SettingsTreeView.csproj"
```

- CI 构建参数见 [.github/workflows/dotnet-desktop.yml](.github/workflows/dotnet-desktop.yml)。改变 `Platform` 或 `EnableNativeImage` 时，restore 与 build 使用相同参数；`Minimize=true` 会定义 `MINIMIZE` 并排除部分资源及 VSTO 复制。
- `EnableNativeImage` 默认关闭；启用时改变依赖并关闭 Costura 嵌入。CI 不对 ARM64 启用 NativeImage；不要把它当成普通构建的必要步骤。
- 仓库没有已跟踪的测试项目或单测试命令；不能用 `dotnet test` 的成功退出代替运行验证。按 `CONTRIBUTING.md`，AI 改动须做运行及受影响功能的回归测试；UI 须检查样式、行为、三语文本和图标。无法验证的项目明确注明原因，不勾选完成。
- [Lint 工作流](.github/workflows/linter.yml) 设置 `DISABLE_ERRORS: true`；绿灯不代表没有 lint 错误，需查看输出。当前 CI 不运行自动化测试。

## 容易改错的边界

- 主应用入口为 `Ink Canvas/App.xaml.cs` 的 `App_Startup`，不是 `App.xaml` 的 `StartupUri`。主窗口逻辑拆在 `Ink Canvas/MainWindow_cs/MW_*.cs`，功能改动放到对应 partial 文件。
- `InkCanvas.Controls` 是共享控件库；`InkCanvas.SettingsTreeView` 是独立 WPF 工具，不是主应用设置窗口。实际设置窗口位于 `Ink Canvas/Windows/SettingsViews/`。
- `InkCanvas.IACoreHelper` 是固定 x86 的独立进程；主应用项目引用设置了 `ReferenceOutputAssembly=false`，由构建目标复制产物，不要改成进程内库引用。
- `InkCanvas.PowerPointAddIn` 不在解决方案内，须用 Visual Studio MSBuild 单独构建；OfficeTools/VSTO 依赖及签名步骤见构建工作流。主应用 `CopyVstoAgent` 只查找其 `bin/<Configuration>/`，而 CI 指定 `bin/<architecture>/<Configuration>/`；主应用编译成功不能证明 `ppt-agent` 已包含加载项。
- PPT 协议修改同时检查主应用、`InkCanvas.PPTAgent.Contracts` 和加载项；运行验证需真实 PowerPoint，且与主应用使用相同权限级别。

## 设置与资源

- 设置数据模型在 `Ink Canvas/Resources/Settings.cs`，不是 `Properties/Settings.Designer.cs`；通过 `SettingsManager` 保存到应用目录下的 `Configs/Settings.json`，不要假定它在仓库根目录。
- 新设置页需同步 XAML 导航 Tag 与 `SettingsWindow.xaml.cs` 的 `_pageTypes`。当前没有 `_staticPageTypes`，不要照搬旧规则新增第二套注册。
- 设置事件保留 `_isLoaded` 等初始化守卫，防止加载控件时写回默认值；增删设置同时检查加载、保存、默认值及主窗口应用逻辑。
- 用户可见文本使用现有 resx/i18n 机制；修改对应默认中文、`en-US`、`zh-ME` 三份资源，保持键一致并检查对应 Designer 属性。导航文本使用 `NavStrings`。本项目把两种本地化 resx 设为 `WithCulture=false`，不要擅自改成默认卫星程序集布局。
- 独立开关设置使用 `LabeledSettingsCard`；例外是 `SettingsExpander.Items`，其子项用 `ui:SettingsCard`。互斥选项用 ComboBox，不设置 `Width`、`MinWidth`、`MaxWidth`。
- 新增敏感设置属性标记 `[SettingsTag(SettingsTag.Secret)]`；反馈脱敏由特性反射驱动，不在 `FeedbackSanitizer` 硬编码字段名。
- `DLASS_TELEMETRY_TOKEN` 可由环境注入并生成临时 `telemetry_token.generated.txt`；不要提交该文件、遥测密钥或本地签名证书。

## PR 与审查

- 使用 [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md)，填写实际命令、运行结果、未验证项，以及 AI 使用范围和人工验证情况；不要把测试计划写成已完成测试。
- 解决代码审查线程前，先修复、提交、推送，再回复对应修复提交的完整 SHA 链接；无代码修复的意见由维护者确认，不以“已知悉”冒充修复。
