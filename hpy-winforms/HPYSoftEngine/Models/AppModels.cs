namespace HPYSoftEngine.Models;

public class Platform
{
    public int Id { get; set; }
    public string Prefix { get; set; } = "";
    public string Category { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public bool InvoiceEnabled { get; set; } = true;
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountParasutId { get; set; }
    public bool MailEnabled { get; set; } = false;
    public bool Active { get; set; } = true;
}

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ParasutId { get; set; } = "";
    public string AccountType { get; set; } = "kasa";
}

public record ProcessedInvoice
{
    public int Id { get; set; }
    public string ParasutInvoiceId { get; set; } = "";
    public string OrderNo { get; set; } = "";
    public string PlatformName { get; set; } = "";
    public string Category { get; set; } = "";
    public bool InvoiceCreated { get; set; }
    public bool PaymentCreated { get; set; }
    public string AccountName { get; set; } = "";
    public bool MailSent { get; set; }
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public class JobLog
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class TodayMetrics
{
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Skip { get; set; }
    public int Err { get; set; }
}

public class JobResult
{
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Skip { get; set; }
    public int Err { get; set; }
    public int Already { get; set; }
}
