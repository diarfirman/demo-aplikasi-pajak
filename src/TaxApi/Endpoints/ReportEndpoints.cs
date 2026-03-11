using TaxApi.Models;
using TaxApi.Services;

namespace TaxApi.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Laporan SPT");

        group.MapPost("/", CreateAsync)
            .WithName("CreateReport")
            .WithSummary("Buat Laporan SPT baru");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllReports")
            .WithSummary("Daftar semua Laporan SPT");

        group.MapGet("/{id}", GetByIdAsync)
            .WithName("GetReportById")
            .WithSummary("Detail Laporan SPT");

        group.MapGet("/taxpayer/{taxPayerId}", GetByTaxPayerAsync)
            .WithName("GetReportsByTaxPayer")
            .WithSummary("Laporan by Wajib Pajak");

        group.MapPost("/{id}/submit", SubmitAsync)
            .WithName("SubmitReport")
            .WithSummary("Submit Laporan SPT untuk direview (via RabbitMQ)");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateReportRequest req,
        ElasticsearchService esService,
        ILogger<Program> logger)
    {
        var taxpayer = await esService.GetTaxPayerByIdAsync(req.TaxPayerId);
        if (taxpayer is null)
            return Results.NotFound(new { error = "Wajib Pajak tidak ditemukan" });

        var report = new TaxReport
        {
            TaxPayerId = req.TaxPayerId,
            TaxPayerName = taxpayer.Name,
            TaxPayerNpwp = taxpayer.Npwp,
            ReportType = req.ReportType,
            Period = req.Period,
            TotalIncome = req.TotalIncome,
            TotalTax = req.TotalTax,
            Status = "Draft",
        };

        await esService.IndexReportAsync(report);
        logger.LogInformation("Created report {Id} for {Name}", report.Id, taxpayer.Name);

        return Results.Created($"/api/reports/{report.Id}", report);
    }

    private static async Task<IResult> GetAllAsync(ElasticsearchService esService)
    {
        var list = await esService.GetAllReportsAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetByIdAsync(string id, ElasticsearchService esService)
    {
        var report = await esService.GetReportByIdAsync(id);
        return report is not null ? Results.Ok(report) : Results.NotFound(new { error = "Laporan tidak ditemukan" });
    }

    private static async Task<IResult> GetByTaxPayerAsync(string taxPayerId, ElasticsearchService esService)
    {
        var list = await esService.GetReportsByTaxPayerAsync(taxPayerId);
        return Results.Ok(list);
    }

    private static async Task<IResult> SubmitAsync(
        string id,
        ElasticsearchService esService,
        RabbitMqService mqService,
        ILogger<Program> logger)
    {
        var report = await esService.GetReportByIdAsync(id);
        if (report is null)
            return Results.NotFound(new { error = "Laporan tidak ditemukan" });

        if (report.Status != "Draft")
            return Results.BadRequest(new { error = $"Laporan sudah dalam status {report.Status}. Hanya Draft yang bisa disubmit." });

        // Update ke Submitted
        var submitted = report with
        {
            Status = "Submitted",
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        await esService.UpdateReportAsync(submitted);

        // Publish ke RabbitMQ → ReportProcessor akan consume
        var message = new ReportSubmittedMessage(
            ReportId: report.Id,
            TaxPayerId: report.TaxPayerId,
            TaxPayerNpwp: report.TaxPayerNpwp,
            TaxPayerName: report.TaxPayerName,
            ReportType: report.ReportType,
            Period: report.Period,
            TotalIncome: report.TotalIncome,
            TotalTax: report.TotalTax
        );
        await mqService.PublishReportSubmittedAsync(message);

        logger.LogInformation("Report {Id} submitted and published to RabbitMQ", id);

        return Results.Ok(new
        {
            message = "Laporan berhasil disubmit. Sedang dalam proses review...",
            report = submitted
        });
    }
}
