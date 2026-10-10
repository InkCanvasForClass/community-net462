using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using ProcessProtectionManager = Ink_Canvas.Helpers.ProcessProtectionManager;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    public static class SettingsManager
    {
        public static Settings Settings { get; set; } = new Settings();

        public static string SettingsFileName { get; } = Path.Combine("Configs", "Settings.json");

        public static bool ReadEnableWindowChromeRendering()
        {
            try
            {
                var path = Path.Combine(App.RootPath, SettingsFileName);
                if (!File.Exists(path)) return Settings?.Startup?.EnableWindowChromeRendering ?? DefaultEnableWindowChromeRendering();

                var json = File.ReadAllText(path);
                var obj = JObject.Parse(json);
                return obj.SelectToken("startup.enableWindowChromeRendering")?.Value<bool>() ?? DefaultEnableWindowChromeRendering();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return Settings?.Startup?.EnableWindowChromeRendering ?? DefaultEnableWindowChromeRendering();
            }
        }

        // 键缺失时的默认值：Win8+ 默认启用 DWM/WindowChrome 渲染（GPU 合成，低端机启动更快），
        // Win7 保持旧的透明回退以兼容。
        private static bool DefaultEnableWindowChromeRendering()
        {
            return Environment.OSVersion.Version >= new Version(6, 2);
        }

        public static void SaveSettingsToFile()
        {
            var text = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            try
            {
                string configsDir = Path.Combine(App.RootPath, "Configs");
                if (!Directory.Exists(configsDir))
                {
                    ProcessProtectionManager.WithWriteAccess(configsDir, () => Directory.CreateDirectory(configsDir));
                }

                var path = App.RootPath + SettingsFileName;
                ProcessProtectionManager.WithWriteAccess(path, () => File.WriteAllText(path, text));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }
}
