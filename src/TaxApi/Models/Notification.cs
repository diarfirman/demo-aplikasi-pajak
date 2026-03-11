namespace TaxApi.Models;

public record Notification
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string TaxPayerId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Type { get; init; } = "Info"; // Info, Success, Warning, Error
    public bool IsRead { get; init; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
