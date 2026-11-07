using Ink_Canvas.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using StorageStrings = Ink_Canvas.Properties.StorageStrings;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    /// <summary>
    /// 设置页内的墨迹 / 截图管理：按日期或字段排序、批量删除、在资源管理器中定位。
    /// </summary>
    public partial class SavesManagerPage
    {
        private List<SavesFileEntry> _inkEntries = new List<SavesFileEntry>();
        private List<SavesFileEntry> _visibleInkEntries = new List<SavesFileEntry>();
        private SavesInkCategory? _inkCategory;
        private bool _showScreenshots;
        private List<SavesFileEntry> _shotEntries = new List<SavesFileEntry>();
        private CancellationTokenSource _loadCts;

        private string _inkSortProp = nameof(SavesFileEntry.ModifiedText);
        private bool _inkSortDesc = true;
        private string _shotSortProp = nameof(SavesFileEntry.ModifiedText);
        private bool _shotSortDesc = true;
        private bool _syncingCombo;

        private static readonly HashSet<string> InkExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".icstk", ".xml", ".zip" };

        public SavesManagerPage()
        {
            InitializeComponent();

            UpdateInkCategory();
            ShotList.ItemsSource = _shotEntries;
            InkList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            ShotList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            InkSort.SelectedIndex = 0;
            ShotSort.SelectedIndex = 0;

            Loaded += async (s, e) => await LoadAsync();
            Unloaded += (s, e) =>
            {
                _loadCts?.Cancel();
                CategoryContent.BeginAnimation(OpacityProperty, null);
                ((TranslateTransform)CategoryContent.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
            };
        }

        #region 加载与分类

        internal FrameworkElement TakeNavigationHeader()
        {
            HeaderHost.Content = null;
            HeaderHost.Visibility = Visibility.Collapsed;
            return SavesHeader;
        }

        private async Task LoadAsync()
        {
            if (!IsLoaded) return;
            _loadCts?.Cancel();
            using var cts = new CancellationTokenSource();
            _loadCts = cts;
            try
            {
                var root = MainWindow.Settings?.Automation?.AutoSavedStrokesLocation;
                InkCount.Text = StorageStrings.Storage_Calculating;
                ShotCount.Text = StorageStrings.Storage_Calculating;

                List<SavesFileEntry> ink, shots;
                try
                {
                    var result = await Task.Run(() => Scan(root, cts.Token), cts.Token);
                    ink = result.Ink;
                    shots = result.Shots;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile("SavesManagerPage | 扫描保存文件失败: " + ex, LogHelper.LogType.Error);
                    ink = new List<SavesFileEntry>();
                    shots = new List<SavesFileEntry>();
                }

                if (cts.IsCancellationRequested || !IsLoaded) return;

                // Replace the snapshot once; adding sorted entries individually blocks the UI thread.
                _inkEntries = ink;
                _shotEntries = shots;
                UpdateInkCategory();
                ShotList.ItemsSource = _shotEntries;

                ApplySort(ShotList, _shotSortProp, _shotSortDesc);
            }
            finally
            {
                if (ReferenceEquals(_loadCts, cts)) _loadCts = null;
            }
        }

        private static (List<SavesFileEntry> Ink, List<SavesFileEntry> Shots) Scan(string root, CancellationToken token)
        {
            var ink = new List<SavesFileEntry>();
            var shots = new List<SavesFileEntry>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return (ink, shots);
            root = Path.GetFullPath(root);
            var presentationsRoot = Path.Combine(root, "Auto Saved - Presentations") + Path.DirectorySeparatorChar;
            var boardRoot = Path.Combine(root, "Auto Saved - BlackBoard Strokes") + Path.DirectorySeparatorChar;
            var annotationRoot = Path.Combine(root, "Auto Saved - Annotation Strokes") + Path.DirectorySeparatorChar;
            var presentations = new Dictionary<string, SavesFileEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in EnumerateSafe(root, token))
            {
                string ext;
                long size;
                DateTime modified;
                DateTime created;
                try
                {
                    ext = Path.GetExtension(file);
                    var fi = new FileInfo(file);
                    if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    size = fi.Length;
                    modified = fi.LastWriteTime;
                    created = fi.CreationTime;
                }
                catch
                {
                    continue;
                }

                var entry = new SavesFileEntry
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    TypeLabel = ext.TrimStart('.').ToUpperInvariant(),
                    InkCategory = file.StartsWith(presentationsRoot, StringComparison.OrdinalIgnoreCase)
                        ? SavesInkCategory.Presentations
                        : file.StartsWith(boardRoot, StringComparison.OrdinalIgnoreCase)
                          || file.StartsWith(annotationRoot, StringComparison.OrdinalIgnoreCase)
                            ? SavesInkCategory.Whiteboard : SavesInkCategory.Manual,
                    Size = size,
                    Modified = modified,
                    Created = created
                };

                if (InkExtensions.Contains(ext))
                {
                    int separator = file.StartsWith(presentationsRoot, StringComparison.OrdinalIgnoreCase)
                        ? file.IndexOf(Path.DirectorySeparatorChar, presentationsRoot.Length) : -1;
                    if (separator < 0)
                    {
                        ink.Add(entry);
                        continue;
                    }

                    string folder = file.Substring(0, separator);
                    if (!presentations.TryGetValue(folder, out var presentation))
                    {
                        string folderName = Path.GetFileName(folder);
                        string name = Regex.Replace(folderName, @"_\d+(?:_[0-9a-fA-F]{8})?$", "");
                        presentation = new SavesFileEntry
                        {
                            FilePath = folder,
                            FileName = string.IsNullOrEmpty(name) ? folderName : name,
                            TypeLabel = StorageStrings.Storage_ManageSaves_PresentationType,
                            IsPresentation = true,
                            InkCategory = SavesInkCategory.Presentations,
                            Created = created,
                            Modified = modified
                        };
                        presentations.Add(folder, presentation);
                        ink.Add(presentation);
                    }
                    presentation.Size += size;
                    if (created < presentation.Created) presentation.Created = created;
                    if (modified > presentation.Modified) presentation.Modified = modified;
                }
                else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    shots.Add(entry);
            }

            return (ink, shots);
        }

        private static IEnumerable<string> EnumerateSafe(string dir, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            IEnumerable<string> files;
            try { files = Directory.GetFiles(dir); }
            catch { files = Array.Empty<string>(); }
            foreach (var f in files)
            {
                token.ThrowIfCancellationRequested();
                yield return f;
            }

            IEnumerable<string> subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { subDirs = Array.Empty<string>(); }
            foreach (var d in subDirs)
            {
                token.ThrowIfCancellationRequested();
                // File Dependency 存放插入画布的图片/PDF，删除会破坏元素，跳过。
                if (string.Equals(Path.GetFileName(d), "File Dependency", StringComparison.OrdinalIgnoreCase)) continue;
                try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; }
                catch { continue; }
                foreach (var f in EnumerateSafe(d, token)) yield return f;
            }
        }

        private void InkCategory_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement card) || !(card.Tag is SavesInkCategory category)) return;
            _showScreenshots = false;
            _inkCategory = category;
            UpdateInkCategory();
            AnimateCategoryTransition(false);
        }

        private void InkBack_Click(object sender, RoutedEventArgs e)
        {
            _showScreenshots = false;
            _inkCategory = null;
            UpdateInkCategory();
            AnimateCategoryTransition(true);
        }

        private void NavigationBack_Click(object sender, RoutedEventArgs e)
            => NavigateBack();

        internal void NavigateBack()
        {
            if (_inkCategory.HasValue || _showScreenshots) InkBack_Click(this, null);
            else StorageBreadcrumb_Click(this, null);
        }

        private void StorageBreadcrumb_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as SettingsWindow)?.NavigateToPage("StoragePage");

        private void ScreenshotCategory_Click(object sender, RoutedEventArgs e)
        {
            _showScreenshots = true;
            _inkCategory = null;
            UpdateInkCategory();
            AnimateCategoryTransition(false);
        }

        private void AnimateCategoryTransition(bool back)
        {
            var offset = (TranslateTransform)CategoryContent.RenderTransform;
            CategoryContent.BeginAnimation(OpacityProperty, null);
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            if (!SystemParameters.ClientAreaAnimation) return;

            var duration = TimeSpan.FromMilliseconds(180);
            CategoryContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
            {
                FillBehavior = FillBehavior.Stop
            });
            offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(back ? -16 : 16, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }

        private void UpdateInkCategory()
        {
            foreach (var entry in _inkEntries) entry.IsSelected = false;
            foreach (var entry in _shotEntries) entry.IsSelected = false;
            InkSelectAll.IsChecked = false;
            ShotSelectAll.IsChecked = false;
            _visibleInkEntries = _inkEntries.Where(entry => entry.InkCategory == _inkCategory).ToList();
            InkList.ItemsSource = _visibleInkEntries;
            PresentationList.ItemsSource = _visibleInkEntries;
            ApplySort(InkList, _inkSortProp, _inkSortDesc);

            InkRoot.Visibility = _showScreenshots ? Visibility.Collapsed : Visibility.Visible;
            ShotDetails.Visibility = _showScreenshots ? Visibility.Visible : Visibility.Collapsed;
            InkCategories.Visibility = _inkCategory == null ? Visibility.Visible : Visibility.Collapsed;
            InkDetails.Visibility = _inkCategory == null ? Visibility.Collapsed : Visibility.Visible;
            bool presentations = _inkCategory == SavesInkCategory.Presentations;
            PresentationList.Visibility = presentations ? Visibility.Visible : Visibility.Collapsed;
            InkList.Visibility = presentations ? Visibility.Collapsed : Visibility.Visible;
            InkEmpty.Visibility = _visibleInkEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            InkCount.Text = string.Format(StorageStrings.Storage_ManageSaves_CountFormat, _visibleInkEntries.Count);
            ShotCount.Text = string.Format(StorageStrings.Storage_ManageSaves_CountFormat, _shotEntries.Count);
            bool details = _inkCategory.HasValue || _showScreenshots;
            ManagerBreadcrumb.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            ManagerTitle.Visibility = details ? Visibility.Collapsed : Visibility.Visible;
            CategoryChevron.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            InkCategoryTitle.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            NavigationBack.ToolTip = details ? StorageStrings.Storage_ManageSaves_BackToCategories
                : StorageStrings.Storage_ManageSaves_BackToStorage;
            InkCategoryTitle.Text = _showScreenshots ? StorageStrings.Storage_ManageSaves_CleanScreenshots : _inkCategory switch
            {
                SavesInkCategory.Presentations => StorageStrings.Storage_ManageSaves_CleanPresentations,
                SavesInkCategory.Whiteboard => StorageStrings.Storage_ManageSaves_CleanWhiteboard,
                SavesInkCategory.Manual => StorageStrings.Storage_ManageSaves_CleanManual,
                _ => string.Empty
            };
        }

        #endregion

        #region 排序

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader header)) return;
            if (!(sender is ListView listView)) return;
            var prop = (header.Column?.DisplayMemberBinding as Binding)?.Path?.Path;
            if (string.IsNullOrEmpty(prop)) return;

            bool isInk = ReferenceEquals(listView, InkList);
            bool desc = isInk ? _inkSortDesc : _shotSortDesc;
            string last = isInk ? _inkSortProp : _shotSortProp;

            if (string.Equals(prop, last, StringComparison.Ordinal)) desc = !desc;
            else desc = IsNumericOrDateSort(prop);

            if (isInk) { _inkSortProp = prop; _inkSortDesc = desc; }
            else { _shotSortProp = prop; _shotSortDesc = desc; }

            ApplySort(listView, prop, desc);
            SyncSortCombo(listView, prop, desc);
        }

        private void InkSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplySortSelection(InkSort, InkList, ref _inkSortProp, ref _inkSortDesc);

        private void ShotSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplySortSelection(ShotSort, ShotList, ref _shotSortProp, ref _shotSortDesc);

        private void ApplySortSelection(ComboBox combo, ListView listView, ref string prop, ref bool desc)
        {
            if (_syncingCombo) return;
            if (!(combo.SelectedItem is ComboBoxItem item) || !(item.Tag is string tag)) return;
            if (!TryParseSortTag(tag, out var p, out var d)) return;
            prop = p;
            desc = d;
            ApplySort(listView, p, d);
        }

        private static bool TryParseSortTag(string tag, out string prop, out bool desc)
        {
            prop = null;
            desc = false;
            var parts = tag.Split('|');
            if (parts.Length != 2) return false;
            prop = parts[0];
            desc = parts[1] == "desc";
            return true;
        }

        private void SyncSortCombo(ListView listView, string prop, bool desc)
        {
            var combo = ReferenceEquals(listView, InkList) ? InkSort : ShotSort;
            string want = prop + "|" + (desc ? "desc" : "asc");
            _syncingCombo = true;
            try
            {
                foreach (ComboBoxItem item in combo.Items)
                {
                    if (item.Tag is string tag && string.Equals(tag, want, StringComparison.Ordinal))
                    {
                        combo.SelectedItem = item;
                        return;
                    }
                }
                combo.SelectedIndex = -1;
            }
            finally
            {
                _syncingCombo = false;
            }
        }

        private static bool IsNumericOrDateSort(string prop)
            => prop == nameof(SavesFileEntry.SizeText)
               || prop == nameof(SavesFileEntry.ModifiedText)
               || prop == nameof(SavesFileEntry.CreatedText);

        private static void ApplySort(ListView listView, string prop, bool descending)
        {
            if (listView?.ItemsSource == null) return;
            if (CollectionViewSource.GetDefaultView(listView.ItemsSource) is ListCollectionView view)
                view.CustomSort = new SavesFileEntryComparer(prop, descending);
        }

        private sealed class SavesFileEntryComparer : IComparer
        {
            private readonly string _prop;
            private readonly bool _desc;

            public SavesFileEntryComparer(string prop, bool desc)
            {
                _prop = prop;
                _desc = desc;
            }

            public int Compare(object x, object y)
            {
                var a = (SavesFileEntry)x;
                var b = (SavesFileEntry)y;
                int r;
                switch (_prop)
                {
                    case nameof(SavesFileEntry.SizeText):
                        r = a.Size.CompareTo(b.Size);
                        break;
                    case nameof(SavesFileEntry.ModifiedText):
                        r = a.Modified.CompareTo(b.Modified);
                        break;
                    case nameof(SavesFileEntry.CreatedText):
                        r = a.Created.CompareTo(b.Created);
                        break;
                    case nameof(SavesFileEntry.TypeLabel):
                        r = string.Compare(a.TypeLabel, b.TypeLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                    default:
                        r = string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                        break;
                }
                return _desc ? -r : r;
            }
        }

        #endregion

        #region 选择 / 删除

        private void InkSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            bool on = InkSelectAll.IsChecked == true;
            foreach (var entry in _visibleInkEntries) entry.IsSelected = on;
        }

        private void ShotSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            bool on = ShotSelectAll.IsChecked == true;
            foreach (var entry in _shotEntries) entry.IsSelected = on;
        }

        private async void InkDelete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_visibleInkEntries);
        private async void ShotDelete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_shotEntries);

        private async Task DeleteSelectedAsync(List<SavesFileEntry> collection)
        {
            var selected = collection.Where(entry => entry.IsSelected).ToList();
            if (selected.Count == 0) return;
            bool hasPresentations = selected.Any(entry => entry.IsPresentation);
            var root = MainWindow.Settings?.Automation?.AutoSavedStrokesLocation;

            var confirm = MessageBox.Show(
                string.Format(hasPresentations ? StorageStrings.Storage_ManageSaves_DeletePresentationConfirm_Body
                    : StorageStrings.Storage_ManageSaves_DeleteConfirm_Body, selected.Count),
                StorageStrings.Storage_ManageSaves_DeleteConfirm_Title,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            if (hasPresentations && (Application.Current.MainWindow as MainWindow)?.PPTManager?.IsInSlideShow == true)
            {
                MessageBox.Show(StorageStrings.Storage_ManageSaves_PresentationDeleteBlocked,
                    StorageStrings.Storage_ManageSaves_DeleteConfirm_Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var errors = new List<string>();
            await Task.Run(() =>
            {
                foreach (var entry in selected) DeleteEntry(entry, root, errors);
            });

            await LoadAsync();

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    string.Format(StorageStrings.Storage_ManageSaves_DeleteFailed, string.Join("\n", errors.Take(10))),
                    StorageStrings.Storage_ManageSaves_DeleteConfirm_Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    string.Format(hasPresentations ? StorageStrings.Storage_ManageSaves_DeleteItemsDone
                        : StorageStrings.Storage_ManageSaves_DeleteDone, selected.Count),
                    StorageStrings.Storage_ManageSaves_DeleteConfirm_Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static void DeleteEntry(SavesFileEntry entry, string root, List<string> errors)
        {
            try
            {
                if (entry.IsPresentation)
                {
                    if (string.IsNullOrEmpty(root))
                        throw new IOException(StorageStrings.Storage_ManageSaves_InvalidPresentationFolder);
                    string folder = Path.GetFullPath(entry.FilePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string parent = Path.GetFullPath(Path.Combine(root, "Auto Saved - Presentations"))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.Equals(Directory.GetParent(folder)?.FullName, parent, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(StorageStrings.Storage_ManageSaves_InvalidPresentationFolder);
                    if (!Directory.Exists(folder)) return;

                    // A junction in the folder or its ancestors could redirect deletion outside the save root.
                    for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
                        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                            throw new IOException(StorageStrings.Storage_ManageSaves_InvalidPresentationFolder);
                    Directory.Delete(folder, true);
                    return;
                }
                File.Delete(entry.FilePath);
                var companion = Path.ChangeExtension(entry.FilePath, ".elements.json");
                if (File.Exists(companion)) File.Delete(companion);
            }
            catch (Exception ex)
            {
                errors.Add(entry.FileName + ": " + ex.Message);
            }
        }

        #endregion

        #region 刷新 / 打开

        private async void InkRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
        private async void ShotRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void InkOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var root = MainWindow.Settings?.Automation?.AutoSavedStrokesLocation;
            if (string.IsNullOrEmpty(root)) return;
            var folder = _inkCategory switch
            {
                SavesInkCategory.Presentations => Path.Combine(root, "Auto Saved - Presentations"),
                SavesInkCategory.Whiteboard => Path.Combine(root, "Auto Saved - BlackBoard Strokes"),
                _ => root
            };
            OpenFolder(Directory.Exists(folder) ? folder : root);
        }

        private void ShotOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var root = MainWindow.Settings?.Automation?.AutoSavedStrokesLocation;
            if (string.IsNullOrEmpty(root)) return;
            var shots = Path.Combine(root, "Auto Saved - Screenshots");
            OpenFolder(Directory.Exists(shots) ? shots : root);
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("SavesManagerPage | 打开文件夹失败: " + ex, LogHelper.LogType.Error);
            }
        }

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is SavesFileEntry entry)) return;
            if (entry.IsPresentation)
            {
                OpenFolder(entry.FilePath);
                return;
            }
            try
            {
                Process.Start("explorer.exe", "/select,\"" + entry.FilePath + "\"");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("SavesManagerPage | 定位文件失败: " + ex, LogHelper.LogType.Error);
            }
        }

        #endregion
    }

    public enum SavesInkCategory { Presentations, Whiteboard, Manual }

    /// <summary>
    /// 管理页面中的保存文件或整份演示文稿墨迹条目。
    /// </summary>
    public class SavesFileEntry : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string TypeLabel { get; set; }
        public bool IsPresentation { get; set; }
        public SavesInkCategory InkCategory { get; set; } = SavesInkCategory.Manual;
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public DateTime Created { get; set; }

        public string SizeText => FormatSize(Size);

        public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm:ss");

        public string CreatedText => Created.ToString("yyyy-MM-dd HH:mm:ss");

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int u = 0;
            while (size >= 1024 && u < units.Length - 1)
            {
                size /= 1024;
                u++;
            }
            return u == 0 ? $"{(long)size} {units[u]}" : $"{size:0.##} {units[u]}";
        }
    }
}
