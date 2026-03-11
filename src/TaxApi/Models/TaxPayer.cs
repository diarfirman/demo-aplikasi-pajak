namespace TaxApi.Models;

public record TaxPayer
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Npwp { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "OP"; // OP = Orang Pribadi, Badan
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record RegisterTaxPayerRequest(
    string Npwp,
    string Name,
    string Type,
    string Email,
    string? Phone,
    string? Address
);
