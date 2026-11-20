using CommunityToolkit.Mvvm.ComponentModel;
using RcloneHelper.Helpers;
using System;
using System.Threading;

namespace RcloneHelper.Models;

/// <summary>
/// 挂载进程信息（用于重启后恢复进程追踪）
/// </summary>
public class RcloneMountInfo
{
    public int Pid { get; set; }
    public string MountName { get; set; } = "";
    public string DriveLetter { get; set; } = "";
    public string CommandLine { get; set; } = "";
}

public class MountConfig
{
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    public string Name { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string LocalDrive { get; set; } = "Z:";
    public string Type { get; set; } = "webdav";
    public string Url { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Vendor { get; set; } = "other";
    public bool AutoMountOnStart { get; set; } = true;
    public bool UseNetworkMode { get; set; } = false;

    // URL 组件字段
    public bool UseHttps { get; set; } = true;
    public string Host { get; set; } = "";

    private int _port = 443;
    public int Port
    {
        get => _port;
        set => _port = value >= MinPort && value <= MaxPort ? value : 443;
    }

    public string Path { get; set; } = "";

    // S3 专用字段
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Region { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string S3Provider { get; set; } = "AWS";
}

public partial class MountInfo : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _remotePath = "";

    [ObservableProperty]
    private string _localDrive = "Z:";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWebDavType))]
    [NotifyPropertyChangedFor(nameof(IsFtpType))]
    [NotifyPropertyChangedFor(nameof(IsSftpType))]
    [NotifyPropertyChangedFor(nameof(IsS3Type))]
    [NotifyPropertyChangedFor(nameof(UseUrlInput))]
    [NotifyPropertyChangedFor(nameof(TypeLabel))]
    private string _type = "webdav";

    /// <summary>
    /// 类型变更时自动更新默认端口
    /// </summary>
    partial void OnTypeChanged(string value)
    {
        // 根据类型设置默认端口
        Port = value switch
        {
            "webdav" => UseHttps ? 443 : 80,
            "ftp" => 21,
            "sftp" => 22,
            "smb" => 445,
            _ => 443
        };
    }

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _user = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _vendor = "other";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMount))]
    [NotifyPropertyChangedFor(nameof(CanUnmount))]
    private bool _isMounted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMount))]
    [NotifyPropertyChangedFor(nameof(CanUnmount))]
    private bool _isMounting;

    [ObservableProperty]
    private string _status = "未挂载";

    /// <summary>
    /// 后台上传中的文件数量（来自 VFS 写缓存）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingUploads))]
    private int _pendingUploadFiles;

    /// <summary>
    /// 后台上传中的字节数
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingUploadText))]
    private long _pendingUploadBytes;

    /// <summary>
    /// 是否存在后台上传任务
    /// </summary>
    public bool HasPendingUploads => PendingUploadFiles > 0;

    /// <summary>
    /// 后台上传进度文本，例如 "2 个文件 · 3.4 GB"
    /// </summary>
    public string PendingUploadText
        => PendingUploadFiles > 0
            ? $"{PendingUploadFiles} 个文件 · {PathUtil.FormatSize(PendingUploadBytes)}"
            : "";

    /// <summary>
    /// 用于取消挂载操作的 CancellationTokenSource
    /// </summary>
    public CancellationTokenSource? MountCancellationTokenSource { get; set; }

    /// <summary>
    /// 计算属性：是否可以挂载（未挂载且未在挂载中）
    /// </summary>
    public bool CanMount => !IsMounted && !IsMounting;

    /// <summary>
    /// 计算属性：是否可以卸载（已挂载且未在挂载中）
    /// </summary>
    public bool CanUnmount => IsMounted && !IsMounting;

    [ObservableProperty]
    private bool _autoMountOnStart = true;

    [ObservableProperty]
    private bool _useNetworkMode = false;

    // URL 组件字段
    [ObservableProperty]
    private bool _useHttps = true;

    /// <summary>
    /// 协议类型（用于 UI 绑定）
    /// </summary>
    public string Protocol
    {
        get => UseHttps ? "HTTPS" : "HTTP";
        set
        {
            if (value == "HTTPS" && !UseHttps)
            {
                UseHttps = true;
                // WebDAV 切换到 HTTPS 时更新端口
                if (Type == "webdav" && Port == 80)
                {
                    Port = 443;
                }
                OnPropertyChanged(nameof(Protocol));
                OnPropertyChanged(nameof(ComputedUrl));
            }
            else if (value == "HTTP" && UseHttps)
            {
                UseHttps = false;
                // WebDAV 切换到 HTTP 时更新端口
                if (Type == "webdav" && Port == 443)
                {
                    Port = 80;
                }
                OnPropertyChanged(nameof(Protocol));
                OnPropertyChanged(nameof(ComputedUrl));
            }
        }
    }

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    private int _port = 443;

    private const int MinPort = 1;
    private const int MaxPort = 65535;

    /// <summary>
    /// 端口值变更时验证并纠正
    /// </summary>
    partial void OnPortChanged(int value)
    {
        // 如果端口无效，重置为默认值
        if (value < MinPort || value > MaxPort)
        {
            Port = DefaultPort;
        }
    }

    [ObservableProperty]
    private string _path = "";

    // S3 专用字段
    [ObservableProperty]
    private string _accessKeyId = "";

    [ObservableProperty]
    private string _secretAccessKey = "";

    [ObservableProperty]
    private string _region = "";

    [ObservableProperty]
    private string _endpoint = "";

    [ObservableProperty]
    private string _s3Provider = "AWS";

    /// <summary>
    /// 计算属性：是否为 WebDAV 类型
    /// </summary>
    public bool IsWebDavType => Type == "webdav";

    /// <summary>
    /// 计算属性：是否为 FTP 类型
    /// </summary>
    public bool IsFtpType => Type == "ftp";

    /// <summary>
    /// 计算属性：是否为 SFTP 类型
    /// </summary>
    public bool IsSftpType => Type == "sftp";

    /// <summary>
    /// 计算属性：是否为 S3 类型
    /// </summary>
    public bool IsS3Type => Type == "s3";

    /// <summary>
    /// 计算属性：是否为网络存储类型（WebDAV/FTP/SFTP）
    /// </summary>
    public bool IsNetworkStorageType => IsWebDavType || IsFtpType || IsSftpType;

    /// <summary>
    /// 计算属性：是否使用 URL 输入（非 WebDAV/FTP/SFTP 类型）
    /// </summary>
    public bool UseUrlInput => !IsNetworkStorageType;

    /// <summary>
    /// 类型显示名（用于列表徽标）
    /// </summary>
    public string TypeLabel => Type.ToLowerInvariant() switch
    {
        "webdav" => "WebDAV",
        "ftp" => "FTP",
        "sftp" => "SFTP",
        "s3" => "S3",
        var other => other.ToUpperInvariant()
    };

    /// <summary>
    /// 校验配置是否完整，返回第一条错误信息；校验通过返回 null。
    /// 新增/编辑挂载与导入配置共用，避免各层重复校验。
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "请输入名称";

        if (IsS3Type)
        {
            if (string.IsNullOrWhiteSpace(AccessKeyId))
                return "请输入 Access Key ID";
            if (string.IsNullOrWhiteSpace(SecretAccessKey))
                return "请输入 Secret Access Key";
            if (string.IsNullOrWhiteSpace(RemotePath))
                return "请输入 Bucket 名称";
        }
        else if (IsNetworkStorageType)
        {
            if (string.IsNullOrWhiteSpace(Host))
                return "请输入主机地址";
            if (Port is < MinPort or > MaxPort)
                return "端口号范围应为 1-65535";
        }
        else if (string.IsNullOrWhiteSpace(Url))
        {
            return "请输入 URL";
        }

        if (string.IsNullOrWhiteSpace(LocalDrive))
            return "请选择挂载点";

        return null;
    }

    /// <summary>
    /// 兼容早期版本保存的配置：只有完整 Url、没有拆分的 Host/Port/Path 时，
    /// 从 Url 反向解析出表单字段，避免编辑旧配置时输入框全空。
    /// </summary>
    public void EnsureUrlPartsFromUrl()
    {
        if (!IsNetworkStorageType || !string.IsNullOrWhiteSpace(Host))
            return;

        if (!Uri.TryCreate((Url ?? "").Trim(), UriKind.Absolute, out var uri))
            return;

        if (IsWebDavType)
            UseHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        Host = uri.Host;
        Port = uri.Port > 0 ? uri.Port : DefaultPort;
        Path = uri.AbsolutePath.TrimStart('/');
    }

    /// <summary>
    /// 获取默认端口
    /// </summary>
    public int DefaultPort => Type switch
    {
        "webdav" => UseHttps ? 443 : 80,
        "ftp" => 21,
        "sftp" => 22,
        "smb" => 445,
        _ => 443
    };

    /// <summary>
    /// 计算完整 URL
    /// </summary>
    public string ComputedUrl
    {
        get
        {
            if (IsWebDavType && !string.IsNullOrWhiteSpace(Host))
            {
                var protocol = UseHttps ? "https" : "http";
                var portPart = (UseHttps && Port == 443) || (!UseHttps && Port == 80) ? "" : $":{Port}";
                var pathPart = string.IsNullOrWhiteSpace(Path) ? "" : (Path.StartsWith("/") ? Path : $"/{Path}");
                return $"{protocol}://{Host}{portPart}{pathPart}";
            }

            if ((IsFtpType || IsSftpType) && !string.IsNullOrWhiteSpace(Host))
            {
                var protocol = IsFtpType ? "ftp" : "sftp";
                var portPart = Port == DefaultPort ? "" : $":{Port}";
                var pathPart = string.IsNullOrWhiteSpace(Path) ? "" : (Path.StartsWith("/") ? Path : $"/{Path}");
                return $"{protocol}://{Host}{portPart}{pathPart}";
            }

            return Url;
        }
    }

    /// <summary>
    /// 从另一个 MountInfo 复制配置字段（用于编辑/保存操作）
    /// </summary>
    public void CopyFieldsFrom(MountInfo source)
    {
        Name = source.Name;
        RemotePath = source.RemotePath;
        LocalDrive = source.LocalDrive;
        Type = source.Type;
        Url = source.Url;
        User = source.User;
        Password = source.Password;
        Vendor = source.Vendor;
        AutoMountOnStart = source.AutoMountOnStart;
        UseNetworkMode = source.UseNetworkMode;
        UseHttps = source.UseHttps;
        Host = source.Host;
        Port = source.Port;
        Path = source.Path;
        // S3 字段
        AccessKeyId = source.AccessKeyId;
        SecretAccessKey = source.SecretAccessKey;
        Region = source.Region;
        Endpoint = source.Endpoint;
        S3Provider = source.S3Provider;
    }

    public static MountInfo FromConfig(MountConfig config)
    {
        var info = new MountInfo
        {
            Name = config.Name,
            RemotePath = config.RemotePath,
            LocalDrive = config.LocalDrive,
            Type = config.Type,
            Url = config.Url,
            User = config.User,
            // 解密密码（兼容未加密的旧数据）
            Password = SecureStorageHelper.Decrypt(config.Password),
            Vendor = config.Vendor,
            AutoMountOnStart = config.AutoMountOnStart,
            UseNetworkMode = config.UseNetworkMode,
            UseHttps = config.UseHttps,
            Host = config.Host,
            Port = config.Port,
            Path = config.Path,
            // S3 字段
            AccessKeyId = config.AccessKeyId,
            SecretAccessKey = SecureStorageHelper.Decrypt(config.SecretAccessKey),
            Region = config.Region,
            Endpoint = config.Endpoint,
            S3Provider = config.S3Provider,
            IsMounted = false,
            Status = "未挂载"
        };

        // 兼容旧配置：早期版本只保存完整 Url，没有拆分 Host/Port/Path
        info.EnsureUrlPartsFromUrl();

        return info;
    }

    public MountConfig ToConfig()
    {
        return new MountConfig
        {
            Name = Name,
            RemotePath = RemotePath,
            LocalDrive = LocalDrive,
            Type = Type,
            Url = IsWebDavType || IsFtpType || IsSftpType ? ComputedUrl : Url,
            User = User,
            // 加密密码
            Password = SecureStorageHelper.Encrypt(Password),
            Vendor = Vendor,
            AutoMountOnStart = AutoMountOnStart,
            UseNetworkMode = UseNetworkMode,
            UseHttps = UseHttps,
            Host = Host,
            Port = Port,
            Path = Path,
            // S3 字段
            AccessKeyId = AccessKeyId,
            SecretAccessKey = SecureStorageHelper.Encrypt(SecretAccessKey),
            Region = Region,
            Endpoint = Endpoint,
            S3Provider = S3Provider
        };
    }
}
