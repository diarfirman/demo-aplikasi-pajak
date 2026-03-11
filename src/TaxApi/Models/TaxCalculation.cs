namespace TaxApi.Models;

public record TaxCalculation
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string TaxPayerId { get; init; } = "";
    public string TaxPayerName { get; init; } = "";
    public string TaxType { get; init; } = ""; // PPh21, PPN
    public decimal GrossAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal NetAmount { get; init; }
    public string Period { get; init; } = ""; // e.g. "2025-01"
    public string? Notes { get; init; }
    public DateTimeOffset CalculatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record CalculatePPh21Request(
    string TaxPayerId,
    decimal GrossIncome,   // Penghasilan bruto per bulan
    string MaritalStatus,  // TK0, K0, K1, K2, K3
    string Period,
    string? Notes
);

public record CalculatePPNRequest(
    string TaxPayerId,
    decimal Amount,        // Nilai transaksi
    string Period,
    string? Notes
);

public record CalculationResult(
    decimal GrossAmount,
    decimal TaxAmount,
    decimal NetAmount,
    string Description
);
