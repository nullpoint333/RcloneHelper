using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneHelper.Models;
using RcloneHelper.Services;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Core.Pages;

/// <summary>
/// 挂载管理页视图模型。
/// 列表支持搜索/筛选，编辑已挂载的连接时自动进入只读查看模式。
/// </summary>
public partial class HomePageViewModel : ObservableObject
{
    private readonly MountService _mountService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly SynchronizationContext? _syncContext;
    private readonly HashSet<MountInfo> _observedMounts = new();

    /// <summary>
    /// 协议选项列表
    /// </summary>
    public string[] ProtocolOptions { get; } = { "HTTPS", "HTTP" };

    /// <summary>
    /// 可选的挂载点列表（Windows 为盘符，Unix 为 /mnt 或 /Volumes 下的目录）
    /// </summary>
    public IReadOnlyList<string> MountPointOptions { get; }

    [ObservableProperty]
    private ObservableCollection<MountInfo> _allMounts = new();

    /// <summary>
    /// 当前筛选后实际展示在列表中的挂载
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MountInfo> _visibleMounts = new();

    [ObservableProperty]
    private MountInfo? _selectedMount;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private MountInfo _editingMount = new();

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterAll))]
    [NotifyPropertyChangedFor(nameof(FilterMounted))]
    [NotifyPropertyChangedFor(nameof(FilterUnmounted))]
    private int _filterIndex;

    /// <summary>
    /// 只读查看模式：编辑正在挂载的连接时进入，所有输入框只读
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    [NotifyPropertyChangedFor(nameof(DialogHint))]
    private bool _isViewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    [NotifyPropertyChangedFor(nameof(DialogHint))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool _isNewMount;

    [ObservableProperty]
    private string _summaryText = "共 0 个挂载";

    /// <summary>
    /// 所有挂载的后台上传汇总（无上传时为空）
    /// </summary>
    [ObservableProperty]
    private string _uploadSummaryText = "";

    [ObservableProperty]
    private bool _hasAnyPendingUploads;

    [ObservableProperty]
    private bool _isListEmpty = true;

    [ObservableProperty]
    private string _emptyTitle = "还没有挂载配置";

    [ObservableProperty]
    private string _emptyHint = "点击右上角「添加挂载」创建第一个连接。";

    // 记录正在编辑的原始挂载对象（用于区分新增和编辑）
    private MountInfo? _editingOriginalMount;

    // 上次自动扫描挂载状态的时间（避免切页时重复扫描）
    private DateTime _lastAutoRefresh = DateTime.MinValue;

    // 防止上传统计重入
    private bool _isScanningUploads;

    public bool FilterAll => FilterIndex == 0;
    public bool FilterMounted => FilterIndex == 1;
    public bool FilterUnmounted => FilterIndex == 2;

    /// <summary>当前是否允许修改表单（查看模式下为 false）</summary>
    public bool CanEdit => !IsViewMode;

    public string DialogTitle => IsViewMode ? "查看挂载（只读）" : IsNewMount ? "添加挂载" : "编辑挂载";

    public string DialogHint => IsViewMode
        ? "该挂载正在运行，所有字段均为只读。需要修改请先卸载。"
        : IsNewMount
            ? "填写连接信息后保存，保存后可随时挂载。"
            : "修改会同步到 rclone 配置，重新挂载后生效。";

    public string SaveButtonText => IsNewMount ? "保存并添加" : "保存";

    public HomePageViewModel(MountService mountService, INotificationService notificationService, IDialogService dialogService)
    {
        _mountService = mountService;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _syncContext = SynchronizationContext.Current;
        MountPointOptions = BuildMountPointOptions();

        // 订阅挂载列表变化事件（导入配置等）
        _mountService.MountsChanged += OnMountsChanged;
        // 订阅挂载状态变化事件（挂载成功/卸载/意外退出）
        _mountService.MountStatusChanged += OnMountStatusChanged;

        LoadMountsFromService();
    }

    #region 列表与筛选

    private static IReadOnlyList<string> BuildMountPointOptions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var drives = new List<string>(23);
            for (var c = 'Z'; c >= 'D'; c--)
                drives.Add($"{c}:");
            return drives;
        }

        var baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "/Volumes" : "/mnt";
        var points = new List<string>(12);
        for (var i = 1; i <= 12; i++)
            points.Add($"{baseDir}/rclone{i}");
        return points;
    }

    private void OnMountsChanged()
    {
        RunOnUiThread(LoadMountsFromService);
    }

    private void OnMountStatusChanged()
    {
        RunOnUiThread(ApplyFilter);
    }

    private void RunOnUiThread(Action action)
    {
        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            _syncContext.Post(_ => action(), null);
        else
            action();
    }

    private void LoadMountsFromService()
    {
        foreach (var observed in _observedMounts)
            observed.PropertyChanged -= OnMountPropertyChanged;
        _observedMounts.Clear();

        AllMounts.Clear();
        foreach (var mount in _mountService.Mounts)
        {
            AllMounts.Add(mount);
            mount.PropertyChanged += OnMountPropertyChanged;
            _observedMounts.Add(mount);
        }

        ApplyFilter();
    }

    private void OnMountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 名称/盘符/挂载状态变化都会影响筛选结果与统计
        switch (e.PropertyName)
        {
            case nameof(MountInfo.IsMounted):
            case nameof(MountInfo.IsMounting):
            case nameof(MountInfo.Status):
            case nameof(MountInfo.Name):
            case nameof(MountInfo.LocalDrive):
                RunOnUiThread(ApplyFilter);
                break;
        }
    }

    /// <summary>
    /// 原位同步 <see cref="VisibleMounts"/>，避免每次状态变化都重建集合导致选中项丢失。
    /// </summary>
    private void ApplyFilter()
    {
        var filtered = AllMounts.Where(MatchesFilter).ToList();

        for (var i = VisibleMounts.Count - 1; i >= 0; i--)
        {
            if (!filtered.Contains(VisibleMounts[i]))
                VisibleMounts.RemoveAt(i);
        }

        for (var i = 0; i < filtered.Count; i++)
        {
            var index = VisibleMounts.IndexOf(filtered[i]);
            if (index < 0)
                VisibleMounts.Insert(i, filtered[i]);
            else if (index != i)
                VisibleMounts.Move(index, i);
        }

        if (SelectedMount != null && !VisibleMounts.Contains(SelectedMount))
            SelectedMount = null;

        var mountedCount = AllMounts.Count(m => m.IsMounted);
        SummaryText = AllMounts.Count == 0
            ? "还没有挂载配置"
            : $"共 {AllMounts.Count} 个连接 · 已挂载 {mountedCount} 个";

        IsListEmpty = VisibleMounts.Count == 0;
        if (IsListEmpty)
        {
            if (AllMounts.Count == 0)
            {
                EmptyTitle = "还没有挂载配置";
                EmptyHint = "点击右上角「添加挂载」创建第一个连接。";
            }
            else
            {
                EmptyTitle = "没有匹配的连接";
                EmptyHint = "试试清空搜索内容，或切换到「全部」筛选。";
            }
        }
    }

    private bool MatchesFilter(MountInfo mount)
    {
        if (FilterIndex == 1 && !mount.IsMounted)
            return false;
        if (FilterIndex == 2 && mount.IsMounted)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var keyword = SearchText.Trim();
        return Contains(mount.Name, keyword)
               || Contains(mount.TypeLabel, keyword)
               || Contains(mount.LocalDrive, keyword)
               || Contains(mount.Host, keyword)
               || Contains(mount.Url, keyword);

        static bool Contains(string? value, string keyword)
            => !string.IsNullOrEmpty(value) && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFilterIndexChanged(int value) => ApplyFilter();

    [RelayCommand]
    private void SetFilter(string? mode)
    {
        FilterIndex = mode switch
        {
            "mounted" => 1,
            "unmounted" => 2,
            _ => 0
        };
    }

    /// <summary>
    /// 页面被导航到时调用：同步外部对挂载状态的改变。
    /// 短时间内重复触发时跳过实际扫描（net use / 进程枚举有成本），避免切页卡顿。
    /// </summary>
    public void Activate()
    {
        _ = RefreshPendingUploadsAsync();

        if (DateTime.UtcNow - _lastAutoRefresh < TimeSpan.FromSeconds(2))
        {
            ApplyFilter();
            return;
        }

        _lastAutoRefresh = DateTime.UtcNow;
        RefreshMountStatus();
    }

    /// <summary>
    /// 刷新各挂载的后台上传统计。
    /// rclone 写缓存模式下，复制大文件会立刻返回，真正的上传在后台进行；
    /// 这里通过 VFS 缓存目录反映「还有多少没传完」，并允许用户取消。
    /// </summary>
    public async Task RefreshPendingUploadsAsync()
    {
        if (_isScanningUploads)
            return;

        _isScanningUploads = true;
        try
        {
            var cacheDir = _mountService.EffectiveCacheDir;
            var totalFiles = 0;
            long totalBytes = 0;

            foreach (var mount in AllMounts)
            {
                if (!mount.IsMounted)
                {
                    mount.PendingUploadFiles = 0;
                    mount.PendingUploadBytes = 0;
                    continue;
                }

                var name = mount.Name;
                var (files, bytes) = await Task.Run(
                    () => PathUtil.GetDirectoryStats(PathUtil.GetVfsCacheDir(cacheDir, name)));

                mount.PendingUploadFiles = files;
                mount.PendingUploadBytes = bytes;
                totalFiles += files;
                totalBytes += bytes;
            }

            HasAnyPendingUploads = totalFiles > 0;
            UploadSummaryText = totalFiles > 0
                ? $"后台上传中：{totalFiles} 个文件 · {PathUtil.FormatSize(totalBytes)}"
                : "";
        }
        catch (Exception ex)
        {
            // 统计失败不影响挂载本身
            System.Diagnostics.Debug.WriteLine($"统计后台上传失败: {ex.Message}");
        }
        finally
        {
            _isScanningUploads = false;
        }
    }

    /// <summary>
    /// 取消某个挂载的后台上传（结束进程 + 丢弃未上传缓存 + 自动重新挂载）
    /// </summary>
    [RelayCommand]
    private async Task CancelUpload(MountInfo? mount)
    {
        if (mount == null || !mount.HasPendingUploads)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "取消后台上传",
            $"「{mount.Name}」还有 {mount.PendingUploadText} 没有上传完成。\n\n" +
            "取消会立即停止挂载，并删除这些尚未上传的缓存数据（已经上传到远端的部分无法撤回），" +
            "随后自动重新挂载。是否继续？");
        if (!confirmed)
            return;

        try
        {
            var (freed, remountError) = await _mountService.CancelPendingUploadsAsync(mount.Name, remount: true);

            mount.PendingUploadFiles = 0;
            mount.PendingUploadBytes = 0;
            await RefreshPendingUploadsAsync();

            if (remountError != null)
                _notificationService.ShowWarning($"已取消上传并释放 {PathUtil.FormatSize(freed)}，但重新挂载失败：{remountError}");
            else
                _notificationService.ShowSuccess($"已取消上传，释放 {PathUtil.FormatSize(freed)}，挂载已恢复");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"取消后台上传失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 刷新挂载状态，同步正在运行的 rclone 进程状态
    /// </summary>
    public void RefreshMountStatus()
    {
        _mountService.RefreshMountStatus();
        ApplyFilter();
    }

    [RelayCommand]
    private void RefreshStatus() => RefreshMountStatus();

    /// <summary>
    /// 从进程恢复追踪（窗口启动时调用）
    /// </summary>
    public async Task RestoreMountProcessesAsync()
    {
        await _mountService.RestoreMountProcessesAsync();
        RunOnUiThread(ApplyFilter);
    }

    /// <summary>
    /// 自动挂载所有配置了"启动时自动挂载"的存储
    /// </summary>
    public async Task AutoMountConfiguredAsync()
    {
        var toMount = AllMounts.Where(m => m.AutoMountOnStart && !m.IsMounted).ToList();
        var tasks = toMount.Select(MountAsync);
        await Task.WhenAll(tasks);
    }

    #endregion

    #region 编辑 / 查看

    [RelayCommand]
    private void AddMount()
    {
        _editingOriginalMount = null; // 新增模式
        IsNewMount = true;
        IsViewMode = false;
        EditingMount = new MountInfo
        {
            LocalDrive = _mountService.GetAvailableMountPoint(),
            UseHttps = true,
            Port = 443
        };
        IsEditing = true;
    }

    [RelayCommand]
    private void EditMount()
    {
        if (SelectedMount == null)
            return;
        OpenEditor(SelectedMount);
    }

    [RelayCommand]
    private void EditItem(MountInfo? mount)
    {
        if (mount == null)
            return;
        SelectedMount = mount;
        OpenEditor(mount);
    }

    /// <summary>
    /// 打开编辑器。已挂载的连接直接进入只读查看模式，避免为了看一眼配置而中断挂载。
    /// </summary>
    private void OpenEditor(MountInfo mount)
    {
        _editingOriginalMount = mount;

        var draft = new MountInfo();
        draft.CopyFieldsFrom(mount);
        draft.EnsureUrlPartsFromUrl();
        EditingMount = draft;

        IsNewMount = false;
        IsViewMode = mount.IsMounted;
        IsEditing = true;
    }

    /// <summary>
    /// 查看模式下显式卸载并切换到编辑模式
    /// </summary>
    [RelayCommand]
    private async Task UnmountAndEdit()
    {
        if (!IsViewMode || _editingOriginalMount == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "卸载并编辑",
            $"「{_editingOriginalMount.Name}」正在运行，需要先卸载才能修改。是否卸载并继续编辑？");
        if (!confirmed)
            return;

        await UnmountAsync(_editingOriginalMount);
        if (_editingOriginalMount.IsMounted)
            return; // 卸载失败，保持只读

        IsViewMode = false;
    }

    [RelayCommand]
    private async Task SaveMount()
    {
        if (IsViewMode)
        {
            _notificationService.ShowWarning("查看模式下不能保存，请先卸载再编辑");
            return;
        }

        var validationError = EditingMount.Validate();
        if (validationError != null)
        {
            _notificationService.ShowWarning(validationError);
            return;
        }

        try
        {
            var saved = _editingOriginalMount != null
                ? await SaveEditedMountAsync(_editingOriginalMount)
                : SaveNewMount();

            if (!saved)
                return; // 校验未通过，保持对话框打开

            IsEditing = false;
            _editingOriginalMount = null;
            IsViewMode = false;
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(ex.Message);
        }
    }

    private async Task<bool> SaveEditedMountAsync(MountInfo original)
    {
        var originalName = original.Name;

        // 改名时检查重名
        if (!string.Equals(EditingMount.Name, originalName, StringComparison.Ordinal))
        {
            var nameConflict = AllMounts.Any(m =>
                !ReferenceEquals(m, original) &&
                string.Equals(m.Name, EditingMount.Name, StringComparison.Ordinal));
            if (nameConflict)
            {
                _notificationService.ShowWarning($"已存在名为 {EditingMount.Name} 的挂载");
                return false;
            }
        }

        // 视图模式下不会进入这里；若挂载在别处重新挂上，先卸载保证配置可写
        var wasMounted = original.IsMounted;
        if (wasMounted)
            _mountService.Unmount(originalName);

        original.CopyFieldsFrom(EditingMount);
        original.Url = EditingMount.ComputedUrl;

        await _mountService.UpdateMountAsync(original, originalName);

        if (!wasMounted)
        {
            _notificationService.ShowSuccess("挂载已保存");
            return true;
        }

        try
        {
            await _mountService.MountAsync(original.Name, originalName);
            _notificationService.ShowSuccess("挂载已保存并重新挂载");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"保存成功，但重新挂载失败: {ex.Message}");
        }

        return true;
    }

    private bool SaveNewMount()
    {
        if (AllMounts.Any(m => string.Equals(m.Name, EditingMount.Name, StringComparison.Ordinal)))
        {
            _notificationService.ShowWarning($"已存在名为 {EditingMount.Name} 的挂载");
            return false;
        }

        var newMount = new MountInfo();
        newMount.CopyFieldsFrom(EditingMount);
        newMount.Url = EditingMount.ComputedUrl;
        newMount.IsMounted = false;
        newMount.Status = "未挂载";

        // AddMount 会触发 MountsChanged，列表自动刷新
        _mountService.AddMount(newMount);
        _notificationService.ShowSuccess("挂载已保存");
        return true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsViewMode = false;
        IsNewMount = false;
        _editingOriginalMount = null;
    }

    /// <summary>
    /// 测试编辑中挂载的连通性（不保存、不挂载）
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        if (IsTestingConnection)
            return;

        IsTestingConnection = true;
        try
        {
            var (success, message) = await _mountService.TestConnectionAsync(EditingMount);
            if (success)
                _notificationService.ShowSuccess(message);
            else
                _notificationService.ShowError(message);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"测试异常：{ex.Message}");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    #endregion

    #region 挂载操作

    [RelayCommand]
    private async Task MountSelected()
    {
        if (SelectedMount == null || SelectedMount.IsMounted)
            return;
        await MountAsync(SelectedMount);
    }

    [RelayCommand]
    private async Task UnmountSelected()
    {
        if (SelectedMount == null || !SelectedMount.IsMounted)
            return;
        await UnmountAsync(SelectedMount);
    }

    [RelayCommand]
    private async Task MountAllAsync()
    {
        var toMount = AllMounts.Where(m => m.CanMount).ToList();
        if (toMount.Count == 0)
        {
            _notificationService.Show("没有需要挂载的连接", NotificationType.Info);
            return;
        }

        await Task.WhenAll(toMount.Select(MountAsync));
    }

    [RelayCommand]
    private async Task UnmountAllAsync()
    {
        var toUnmount = AllMounts.Where(m => m.IsMounted).ToList();
        if (toUnmount.Count == 0)
        {
            _notificationService.Show("没有正在挂载的连接", NotificationType.Info);
            return;
        }

        foreach (var mount in toUnmount)
            await UnmountAsync(mount);
    }

    [RelayCommand]
    private async Task MountItem(MountInfo? mount)
    {
        if (mount == null || mount.IsMounted || mount.IsMounting)
            return;
        await MountAsync(mount);
    }

    [RelayCommand]
    private async Task UnmountItem(MountInfo? mount)
    {
        if (mount == null || !mount.IsMounted)
            return;
        await UnmountAsync(mount);
    }

    [RelayCommand]
    private void CancelMountItem(MountInfo? mount)
    {
        if (mount == null || !mount.IsMounting)
            return;
        _mountService.CancelMount(mount.Name);
    }

    [RelayCommand]
    private async Task DeleteItem(MountInfo? mount)
    {
        if (mount == null)
            return;

        var message = mount.IsMounted
            ? $"「{mount.Name}」正在挂载中，删除会先卸载。确定删除吗？此操作不可撤销。"
            : $"确定删除挂载「{mount.Name}」吗？此操作不可撤销。";

        if (!await _dialogService.ShowConfirmationAsync("删除挂载", message))
            return;

        DeleteMountInternal(mount);
    }

    [RelayCommand]
    private async Task DeleteMount()
    {
        if (SelectedMount == null)
            return;

        await DeleteItem(SelectedMount);
    }

    private void DeleteMountInternal(MountInfo mount)
    {
        try
        {
            _mountService.DeleteMount(mount.Name);
            if (ReferenceEquals(SelectedMount, mount))
                SelectedMount = null;
            _notificationService.ShowSuccess("挂载已删除");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(ex.Message);
        }
    }

    private async Task MountAsync(MountInfo mount)
    {
        // 创建取消令牌
        mount.MountCancellationTokenSource = new CancellationTokenSource();

        try
        {
            await _mountService.MountAsync(mount.Name, null, mount.MountCancellationTokenSource.Token);
            _notificationService.ShowSuccess($"{mount.Name} 挂载成功");
        }
        catch (OperationCanceledException)
        {
            mount.IsMounting = false;
            mount.Status = "已取消";
            _notificationService.Show($"{mount.Name} 挂载已取消", NotificationType.Info);
        }
        catch (Exception ex)
        {
            mount.IsMounting = false;
            mount.Status = "挂载失败";
            _notificationService.ShowError(ex.Message);
        }
        finally
        {
            mount.MountCancellationTokenSource?.Dispose();
            mount.MountCancellationTokenSource = null;
        }
    }

    private async Task UnmountAsync(MountInfo mount)
    {
        try
        {
            await _mountService.UnmountAsync(mount.Name);
            _notificationService.ShowSuccess($"{mount.Name} 已卸载");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(ex.Message);
        }
    }

    #endregion
}