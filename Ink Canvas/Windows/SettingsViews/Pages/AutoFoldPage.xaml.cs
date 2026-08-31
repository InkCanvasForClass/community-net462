using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class AutoFoldPage : Page
    {
        private bool _isLoaded = false;

        public AutoFoldPage()
        {
            InitializeComponent();
            Loaded += AutoFoldPage_Loaded;
            Unloaded += AutoFoldPage_Unloaded;
        }

        private void AutoFoldPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void AutoFoldPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            _isLoaded = false;
            var auto = SettingsManager.Settings.Automation;

            CardAutoFoldInEasiNote.IsOn = auto.IsAutoFoldInEasiNote;
            CardAutoFoldInEasiCamera.IsOn = auto.IsAutoFoldInEasiCamera;
            CardAutoFoldInEasiNote3.IsOn = auto.IsAutoFoldInEasiNote3;
            CardAutoFoldInEasiNote3C.IsOn = auto.IsAutoFoldInEasiNote3C;
            CardAutoFoldInEasiNote5C.IsOn = auto.IsAutoFoldInEasiNote5C;
            CardAutoFoldInSeewoPincoTeacher.IsOn = auto.IsAutoFoldInSeewoPincoTeacher;
            CardAutoFoldInHiteTouchPro.IsOn = auto.IsAutoFoldInHiteTouchPro;
            CardAutoFoldInHiteLightBoard.IsOn = auto.IsAutoFoldInHiteLightBoard;
            CardAutoFoldInHiteCamera.IsOn = auto.IsAutoFoldInHiteCamera;
            CardAutoFoldInWxBoardMain.IsOn = auto.IsAutoFoldInWxBoardMain;
            CardAutoFoldInOldZyBoard.IsOn = auto.IsAutoFoldInOldZyBoard;
            CardAutoFoldInMSWhiteboard.IsOn = auto.IsAutoFoldInMSWhiteboard;
            CardAutoFoldInAdmoxWhiteboard.IsOn = auto.IsAutoFoldInAdmoxWhiteboard;
            CardAutoFoldInAdmoxBooth.IsOn = auto.IsAutoFoldInAdmoxBooth;
            CardAutoFoldInQPoint.IsOn = auto.IsAutoFoldInQPoint;
            CardAutoFoldInYiYunVisualPresenter.IsOn = auto.IsAutoFoldInYiYunVisualPresenter;
            CardAutoFoldInMaxHubWhiteboard.IsOn = auto.IsAutoFoldInMaxHubWhiteboard;
            CardAutoFoldInPPTSlideShow.IsOn = auto.IsAutoFoldInPPTSlideShow;

            CardAutoKillPPTService.IsOn = auto.IsAutoKillPPTService;
            CardAutoKillEasiNote.IsOn = auto.IsAutoKillEasiNote;
            CardAutoKillHiteAnnotation.IsOn = auto.IsAutoKillHiteAnnotation;
            CardAutoKillVComYouJiao.IsOn = auto.IsAutoKillVComYouJiao;
            CardAutoKillSeewoLauncher2DesktopAnnotation.IsOn = auto.IsAutoKillSeewoLauncher2DesktopAnnotation;
            CardAutoKillInkCanvas.IsOn = auto.IsAutoKillInkCanvas;
            CardAutoKillICA.IsOn = auto.IsAutoKillICA;
            CardAutoKillIDT.IsOn = auto.IsAutoKillIDT;
            CardAutoEnterAnnotationAfterKillHite.IsOn = auto.IsAutoEnterAnnotationAfterKillHite;

            CardAutoEnterAnnotationModeWhenExitFoldMode.IsOn = auto.IsAutoEnterAnnotationModeWhenExitFoldMode;
            CardAutoFoldWhenExitWhiteboard.IsOn = auto.IsAutoFoldWhenExitWhiteboard;
            CardAutoFoldAfterPPTSlideShow.IsOn = auto.IsAutoFoldAfterPPTSlideShow;
            CardKeepFoldAfterSoftwareExit.IsOn = auto.KeepFoldAfterSoftwareExit;

            if (auto.FloatingWindowInterceptor.InterceptRules != null)
            {
                ToggleSwitchSeewoWhiteboard3Floating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoWhiteboard3Floating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoWhiteboard3Floating"];
                ToggleSwitchSeewoWhiteboard5Floating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoWhiteboard5Floating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoWhiteboard5Floating"];
                ToggleSwitchSeewoWhiteboard5CFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoWhiteboard5CFloating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoWhiteboard5CFloating"];
                ToggleSwitchSeewoPincoSideBarFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoPincoSideBarFloating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoPincoSideBarFloating"];
                ToggleSwitchSeewoPincoDrawingFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoPincoDrawingFloating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoPincoDrawingFloating"];
                ToggleSwitchSeewoPPTFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoPPTFloating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoPPTFloating"];
                ToggleSwitchAiClassFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("AiClassFloating") && auto.FloatingWindowInterceptor.InterceptRules["AiClassFloating"];
                ToggleSwitchHiteAnnotationFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("HiteAnnotationFloating") && auto.FloatingWindowInterceptor.InterceptRules["HiteAnnotationFloating"];
                ToggleSwitchChangYanFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("ChangYanFloating") && auto.FloatingWindowInterceptor.InterceptRules["ChangYanFloating"];
                ToggleSwitchChangYanPPTFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("ChangYanPPTFloating") && auto.FloatingWindowInterceptor.InterceptRules["ChangYanPPTFloating"];
                ToggleSwitchIntelligentClassFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("IntelligentClassFloating") && auto.FloatingWindowInterceptor.InterceptRules["IntelligentClassFloating"];
                ToggleSwitchSeewoDesktopAnnotationFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoDesktopAnnotationFloating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoDesktopAnnotationFloating"];
                ToggleSwitchSeewoDesktopSideBarFloating.IsOn = auto.FloatingWindowInterceptor.InterceptRules.ContainsKey("SeewoDesktopSideBarFloating") && auto.FloatingWindowInterceptor.InterceptRules["SeewoDesktopSideBarFloating"];
            }

            UpdateFloatingWindowInterceptorEnabled();

            _isLoaded = true;
        }

        #region AutoFold

        private void ToggleSwitchAutoFoldInEasiNote_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInEasiNote = CardAutoFoldInEasiNote.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInEasiCamera_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInEasiCamera = CardAutoFoldInEasiCamera.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInEasiNote3_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInEasiNote3 = CardAutoFoldInEasiNote3.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInEasiNote3C_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInEasiNote3C = CardAutoFoldInEasiNote3C.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInEasiNote5C_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInEasiNote5C = CardAutoFoldInEasiNote5C.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInSeewoPincoTeacher_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInSeewoPincoTeacher = CardAutoFoldInSeewoPincoTeacher.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInHiteTouchPro_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInHiteTouchPro = CardAutoFoldInHiteTouchPro.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInHiteLightBoard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInHiteLightBoard = CardAutoFoldInHiteLightBoard.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInHiteCamera_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInHiteCamera = CardAutoFoldInHiteCamera.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInWxBoardMain_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInWxBoardMain = CardAutoFoldInWxBoardMain.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInOldZyBoard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInOldZyBoard = CardAutoFoldInOldZyBoard.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInMSWhiteboard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInMSWhiteboard = CardAutoFoldInMSWhiteboard.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInAdmoxWhiteboard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInAdmoxWhiteboard = CardAutoFoldInAdmoxWhiteboard.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInAdmoxBooth_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInAdmoxBooth = CardAutoFoldInAdmoxBooth.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInQPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInQPoint = CardAutoFoldInQPoint.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInYiYunVisualPresenter_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInYiYunVisualPresenter = CardAutoFoldInYiYunVisualPresenter.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInMaxHubWhiteboard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldInMaxHubWhiteboard = CardAutoFoldInMaxHubWhiteboard.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        private void ToggleSwitchAutoFoldInPPTSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var auto = SettingsManager.Settings.Automation;
            bool previousState = auto.IsAutoFoldInPPTSlideShow;
            auto.IsAutoFoldInPPTSlideShow = CardAutoFoldInPPTSlideShow.IsOn;
            if (previousState != auto.IsAutoFoldInPPTSlideShow)
            {
                LogHelper.WriteLogToFile($"PPT自动收纳设置已变更: {auto.IsAutoFoldInPPTSlideShow}", LogHelper.LogType.Trace);
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoFoldChanged();
        }

        #endregion

        #region AutoKill

        private void UpdateAutoKillTimer()
        {
            SettingsActionHub.OnAutoKillChanged();
        }

        private void ToggleSwitchAutoKillPPTService_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillPPTService = CardAutoKillPPTService.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillEasiNote_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillEasiNote = CardAutoKillEasiNote.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillHiteAnnotation_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillHiteAnnotation = CardAutoKillHiteAnnotation.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillVComYouJiao_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillVComYouJiao = CardAutoKillVComYouJiao.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillSeewoLauncher2DesktopAnnotation_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillSeewoLauncher2DesktopAnnotation = CardAutoKillSeewoLauncher2DesktopAnnotation.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillInkCanvas_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillInkCanvas = CardAutoKillInkCanvas.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillICA_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillICA = CardAutoKillICA.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoKillIDT_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoKillIDT = CardAutoKillIDT.IsOn;
            SettingsManager.SaveSettingsToFile();
            UpdateAutoKillTimer();
        }

        private void ToggleSwitchAutoEnterAnnotationAfterKillHite_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoEnterAnnotationAfterKillHite = CardAutoEnterAnnotationAfterKillHite.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Fold Mode

        private void ToggleSwitchAutoEnterAnnotationModeWhenExitFoldMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoEnterAnnotationModeWhenExitFoldMode = CardAutoEnterAnnotationModeWhenExitFoldMode.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchAutoFoldWhenExitWhiteboard_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldWhenExitWhiteboard = CardAutoFoldWhenExitWhiteboard.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchAutoFoldAfterPPTSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.IsAutoFoldAfterPPTSlideShow = CardAutoFoldAfterPPTSlideShow.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchKeepFoldAfterSoftwareExit_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.KeepFoldAfterSoftwareExit = CardKeepFoldAfterSoftwareExit.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Floating Window Interceptor

        private void UpdateFloatingWindowInterceptorEnabled()
        {
            var auto = SettingsManager.Settings.Automation;
            bool anyOn = ToggleSwitchSeewoWhiteboard3Floating.IsOn
                || ToggleSwitchSeewoWhiteboard5Floating.IsOn
                || ToggleSwitchSeewoWhiteboard5CFloating.IsOn
                || ToggleSwitchSeewoPincoSideBarFloating.IsOn
                || ToggleSwitchSeewoPincoDrawingFloating.IsOn
                || ToggleSwitchSeewoPPTFloating.IsOn
                || ToggleSwitchAiClassFloating.IsOn
                || ToggleSwitchHiteAnnotationFloating.IsOn
                || ToggleSwitchChangYanFloating.IsOn
                || ToggleSwitchChangYanPPTFloating.IsOn
                || ToggleSwitchIntelligentClassFloating.IsOn
                || ToggleSwitchSeewoDesktopAnnotationFloating.IsOn
                || ToggleSwitchSeewoDesktopSideBarFloating.IsOn;
            auto.FloatingWindowInterceptor.IsEnabled = anyOn;
            SettingsActionHub.OnFloatingWindowInterceptorEnabledCheck(anyOn);
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchSeewoWhiteboard3Floating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoWhiteboard3Floating", ToggleSwitchSeewoWhiteboard3Floating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoWhiteboard5Floating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoWhiteboard5Floating", ToggleSwitchSeewoWhiteboard5Floating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoWhiteboard5CFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoWhiteboard5CFloating", ToggleSwitchSeewoWhiteboard5CFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoPincoSideBarFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoPincoSideBarFloating", ToggleSwitchSeewoPincoSideBarFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoPincoDrawingFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoPincoDrawingFloating", ToggleSwitchSeewoPincoDrawingFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoPPTFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoPPTFloating", ToggleSwitchSeewoPPTFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchAiClassFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("AiClassFloating", ToggleSwitchAiClassFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchHiteAnnotationFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("HiteAnnotationFloating", ToggleSwitchHiteAnnotationFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchChangYanFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("ChangYanFloating", ToggleSwitchChangYanFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchChangYanPPTFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("ChangYanPPTFloating", ToggleSwitchChangYanPPTFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchIntelligentClassFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("IntelligentClassFloating", ToggleSwitchIntelligentClassFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoDesktopAnnotationFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoDesktopAnnotationFloating", ToggleSwitchSeewoDesktopAnnotationFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        private void ToggleSwitchSeewoDesktopSideBarFloating_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsActionHub.OnFloatingWindowInterceptorRuleChanged("SeewoDesktopSideBarFloating", ToggleSwitchSeewoDesktopSideBarFloating.IsOn);
            UpdateFloatingWindowInterceptorEnabled();
        }

        #endregion
    }
}
