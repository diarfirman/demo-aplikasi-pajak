namespace TaxApi.Models;

public record TaxReport
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string TaxPayerId { get; init; } = "";
    public string TaxPayerName { get; init; } = "";
    public string TaxPayerNpwp { get; init; } = "";
    public string ReportType { get; init; } = "SPT Tahunan"; // SPT Tahunan, SPT Masa
    public string Period { get; init; } = "";
    public decimal TotalIncome { get; init; }
    public decimal TotalTax { get; init; }
    public string Status { get; init; } = "Draft"; // Draft, Submitted, Approved, Rejected
    public string? RejectionReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

public record CreateReportRequest(
    string TaxPayerId,
    string ReportType,
    string Period,
    decimal TotalIncome,
    decimal TotalTax
);
