using TaxApi.Models;
using TaxApi.Services;

namespace TaxApi.Endpoints;

public static class CalculationEndpoints
{
    public static IEndpointRouteBuilder MapCalculationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calculations").WithTags("Perhitungan Pajak");

        group.MapPost("/pph21", CalculatePPh21Async)
            .WithName("CalculatePPh21")
            .WithSummary("Hitung PPh Pasal 21 (Penghasilan Karyawan)");

        group.MapPost("/ppn", CalculatePPNAsync)
            .WithName("CalculatePPN")
            .WithSummary("Hitung PPN 12%");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllCalculations")
            .WithSummary("Daftar semua perhitungan");

        group.MapGet("/taxpayer/{taxPayerId}", GetByTaxPayerAsync)
            .WithName("GetCalculationsByTaxPayer")
            .WithSummary("Perhitungan by Wajib Pajak");

        return app;
    }

    private static async Task<IResult> CalculatePPh21Async(
        CalculatePPh21Request req,
        ElasticsearchService esService,
        ILogger<Program> logger)
    {
        var taxpayer = await esService.GetTaxPayerByIdAsync(req.TaxPayerId);
        if (taxpayer is null)
            return Results.NotFound(new { error = "Wajib Pajak tidak ditemukan" });

        var result = TaxCalculatorService.HitungPPh21(req.GrossIncome, req.MaritalStatus);

        var calc = new TaxCalculation
        {
            TaxPayerId = req.TaxPayerId,
            TaxPayerName = taxpayer.Name,
            TaxType = "PPh21",
            GrossAmount = result.GrossAmount,
            TaxAmount = result.TaxAmount,
            NetAmount = result.NetAmount,
            Period = req.Period,
            Notes = req.Notes ?? result.Description,
        };

        await esService.IndexCalculationAsync(calc);
        logger.LogInformation("Calculated PPh21 for {Name}: Rp {Tax:N0}", taxpayer.Name, result.TaxAmount);

        return Results.Ok(calc);
    }

    private static async Task<IResult> CalculatePPNAsync(
        CalculatePPNRequest req,
        ElasticsearchService esService,
        ILogger<Program> logger)
    {
        var taxpayer = await esService.GetTaxPayerByIdAsync(req.TaxPayerId);
        if (taxpayer is null)
            return Results.NotFound(new { error = "Wajib Pajak tidak ditemukan" });

        var result = TaxCalculatorService.HitungPPN(req.Amount);

        var calc = new TaxCalculation
        {
            TaxPayerId = req.TaxPayerId,
            TaxPayerName = taxpayer.Name,
            TaxType = "PPN",
            GrossAmount = result.GrossAmount,
            TaxAmount = result.TaxAmount,
            NetAmount = result.NetAmount,
            Period = req.Period,
            Notes = req.Notes ?? result.Description,
        };

        await esService.IndexCalculationAsync(calc);
        logger.LogInformation("Calculated PPN for {Name}: Rp {Tax:N0}", taxpayer.Name, result.TaxAmount);

        return Results.Ok(calc);
    }

    private static async Task<IResult> GetAllAsync(ElasticsearchService esService)
    {
        var list = await esService.GetAllCalculationsAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetByTaxPayerAsync(string taxPayerId, ElasticsearchService esService)
    {
        var list = await esService.GetCalculationsByTaxPayerAsync(taxPayerId);
        return Results.Ok(list);
    }
}
