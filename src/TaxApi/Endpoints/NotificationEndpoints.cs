using TaxApi.Services;

namespace TaxApi.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifikasi");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllNotifications")
            .WithSummary("Semua notifikasi");

        group.MapGet("/taxpayer/{taxPayerId}", GetByTaxPayerAsync)
            .WithName("GetNotificationsByTaxPayer")
            .WithSummary("Notifikasi by Wajib Pajak");

        return app;
    }

    private static async Task<IResult> GetAllAsync(ElasticsearchService esService)
    {
        var list = await esService.GetAllNotificationsAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetByTaxPayerAsync(string taxPayerId, ElasticsearchService esService)
    {
        var list = await esService.GetNotificationsByTaxPayerAsync(taxPayerId);
        return Results.Ok(list);
    }
}
