namespace ReleaseTicketing.Core.Configuration;

public class BusinessOptions
{
    public const string Section = "Business";
    /// <summary>Used to decide what "today" means when counting working days for cancellation.</summary>
    public string BusinessTimeZone { get; set; } = "Asia/Kolkata";
}

public class AttachmentOptions
{
    public const string Section = "Attachments";
    public string[] AllowedExtensions { get; set; } = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".msg", ".eml", ".png", ".jpg", ".jpeg"];
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public string Container { get; set; } = "release-attachments";
    /// <summary>e.g. https://mystorage.blob.core.windows.net. Empty = use local-disk store (development only).</summary>
    public string? StorageAccountUrl { get; set; }
    public string LocalFolder { get; set; } = "local-attachments";
}

public class GraphOptions
{
    public const string Section = "Graph";
    public bool Enabled { get; set; }
    /// <summary>Mailbox (UPN) the application sends from. Needs Mail.Send application permission.</summary>
    public string SenderUser { get; set; } = "";
}

public class NotificationOptions
{
    public const string Section = "Notifications";
    public int BatchSize { get; set; } = 50;
    public int MaxRetries { get; set; } = 5;
    public int BaseRetryMinutes { get; set; } = 5;
    public string AppUrl { get; set; } = "https://apex.example.com/release-requests";
}
