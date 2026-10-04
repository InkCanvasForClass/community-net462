using Ink_Canvas.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using StorageStrings = Ink_Canvas.Properties.StorageStrings;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    /// <summary>
    /// 设置页内的墨迹 / 截图管理：按日期或字段排序、批量删除、在资源管理器中定位。
    /// </summary>
    public partial class SavesManagerPage
    {
        private readonly ObservableCollection<SavesFileEntry> _inkEntries = new ObservableCollection<SavesFileEntry>();
        private readonly ObservableCollection<SavesFileEntry> _shotEntries = new ObservableCollection<SavesFileEntry>();

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

            InkList.ItemsSource = _inkEntries;
            ShotList.ItemsSource = _shotEntries;
            InkList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            ShotList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            InkSort.SelectedIndex = 0;
            ShotSort.SelectedIndex = 0;

            Loaded += async (s, e) => await LoadAsync();
        }

        #region 加载与分类

        private async Task LoadAsync()
        {
            var root = MainWindow.Settings?.Automation?.AutoSavedStrokesLocation;
            InkCount.Text = StorageStrings.Storage_Calculating;
            ShotCount.Text = StorageStrings.Storage_Calculating;

            List<SavesFileEntry> ink, shots;
            try
            {
                var result = await Task.Run(() => Scan(root));
                ink = result.Ink;
                shots = result.Shots;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("SavesManagerPage | 扫描保存文件失败: " + ex, LogHelper.LogType.Error);
                ink = new List<SavesFileEntry>();
                shots = new List<SavesFileEntry>();
            }

            _inkEntries.Clear();
            foreach (var e in ink) _inkEntries.Add(e);
            _shotEntries.Clear();
            foreach (var e in shots) _shotEntries.Add(e);

            InkSelectAll.IsChecked = false;
            ShotSelectAll.IsChecked = false;

            ApplySort(InkList, _inkSortProp, _inkSortDesc);
            ApplySort(ShotList, _shotSortProp, _shotSortDesc);

            InkCount.Text = string.Format(StorageStrings.Storage_ManageSaves_CountFormat, _inkEntries.Count);
            ShotCount.Text = string.Format(StorageStrings.Storage_ManageSaves_CountFormat, _shotEntries.Count);
        }

        private static (List<SavesFileEntry> Ink, List<SavesFileEntry> Shots) Scan(string root)
        {
            var ink = new List<SavesFileEntry>();
            var shots = new List<SavesFileEntry>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return (ink, shots);

            foreach (var file in EnumerateSafe(root))
            {
                string ext;
                long size;
                DateTime modified;
                DateTime created;
                try
                {
                    ext = Path.GetExtension(file);
                    var fi = new FileInfo(file);
                    if (!fi.Exists) continue;
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
                    Size = size,
                    Modified = modified,
                    Created = created
                };

                if (InkExtensions.Contains(ext))
                    ink.Add(entry);
                else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    shots.Add(entry);
            }

            return (ink, shots);
        }

        private static IEnumerable<string> EnumerateSafe(string dir)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { files = Array.Empty<string>(); }
            foreach (var f in files) yield return f;

            IEnumerable<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(dir); }
            catch { subDirs = Array.Empty<string>(); }
            foreach (var d in subDirs)
            {
                // File Dependency 存放插入画布的图片/PDF，删除会破坏元素，跳过。
                if (string.Equals(Path.GetFileName(d), "File Dependency", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var f in EnumerateSafe(d)) yield return f;
            }
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
            foreach (var entry in _inkEntries) entry.IsSelected = on;
        }

        private void ShotSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            bool on = ShotSelectAll.IsChecked == true;
            foreach (var entry in _shotEntries) entry.IsSelected = on;
        }

        private async void InkDelete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_inkEntries);
        private async void ShotDelete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_shotEntries);

        private async Task DeleteSelectedAsync(ObservableCollection<SavesFileEntry> collection)
        {
            var selected = collection.Where(entry => entry.IsSelected).ToList();
            if (selected.Count == 0) return;

            var confirm = MessageBox.Show(
                string.Format(StorageStrings.Storage_ManageSaves_DeleteConfirm_Body, selected.Count),
                StorageStrings.Storage_ManageSaves_DeleteConfirm_Title,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            var errors = new List<string>();
            await Task.Run(() =>
            {
                foreach (var entry in selected) DeleteEntry(entry, errors);
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
                    string.Format(StorageStrings.Storage_ManageSaves_DeleteDone, selected.Count),
                    StorageStrings.Storage_ManageSaves_DeleteConfirm_Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static void DeleteEntry(SavesFileEntry entry, List<string> errors)
        {
            try
            {
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
            => OpenFolder(MainWindow.Settings?.Automation?.AutoSavedStrokesLocation);

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

    /// <summary>
    /// 管理页面中的单个保存文件条目。
    /// </summary>
    public class SavesFileEntry : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string TypeLabel { get; set; }
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
