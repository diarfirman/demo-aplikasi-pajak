namespace ReportProcessor.Models;

// Message yang diterima dari TaxApi (via pajak.laporan.submitted)
public record ReportSubmittedMessage(
    string ReportId,
    string TaxPayerId,
    string TaxPayerNpwp,
    string TaxPayerName,
    string ReportType,
    string Period,
    decimal TotalIncome,
    decimal TotalTax
);

// Message yang dikirim balik ke TaxApi (via pajak.laporan.result)
public record ReportResultMessage(
    string ReportId,
    string TaxPayerId,
    bool IsApproved,
    string? RejectionReason
);
