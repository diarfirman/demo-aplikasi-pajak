using TaxApi.Models;
using TaxApi.Services;

namespace TaxApi.Endpoints;

public static class TaxPayerEndpoints
{
    public static IEndpointRouteBuilder MapTaxPayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/taxpayers").WithTags("Wajib Pajak");

        group.MapPost("/", RegisterAsync)
            .WithName("RegisterTaxPayer")
            .WithSummary("Daftarkan Wajib Pajak baru");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllTaxPayers")
            .WithSummary("Daftar semua Wajib Pajak");

        group.MapGet("/{id}", GetByIdAsync)
            .WithName("GetTaxPayerById")
            .WithSummary("Detail Wajib Pajak by ID");

        group.MapGet("/npwp/{npwp}", GetByNpwpAsync)
            .WithName("GetTaxPayerByNpwp")
            .WithSummary("Cari Wajib Pajak by NPWP");

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterTaxPayerRequest req,
        ElasticsearchService esService,
        ILogger<Program> logger)
    {
        // Cek duplikat NPWP
        var existing = await esService.GetTaxPayerByNpwpAsync(req.Npwp);
        if (existing is not null)
            return Results.Conflict(new { error = $"NPWP {req.Npwp} sudah terdaftar" });

        var taxpayer = new TaxPayer
        {
            Npwp = req.Npwp,
            Name = req.Name,
            Type = req.Type,
            Email = req.Email,
            Phone = req.Phone,
            Address = req.Address,
        };

        await esService.IndexTaxPayerAsync(taxpayer);
        logger.LogInformation("Registered taxpayer: {Name} ({Npwp})", taxpayer.Name, taxpayer.Npwp);

        return Results.Created($"/api/taxpayers/{taxpayer.Id}", taxpayer);
    }

    private static async Task<IResult> GetAllAsync(ElasticsearchService esService)
    {
        var list = await esService.GetAllTaxPayersAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetByIdAsync(string id, ElasticsearchService esService)
    {
        var taxpayer = await esService.GetTaxPayerByIdAsync(id);
        return taxpayer is not null ? Results.Ok(taxpayer) : Results.NotFound(new { error = "Wajib Pajak tidak ditemukan" });
    }

    private static async Task<IResult> GetByNpwpAsync(string npwp, ElasticsearchService esService)
    {
        var taxpayer = await esService.GetTaxPayerByNpwpAsync(npwp);
        return taxpayer is not null ? Results.Ok(taxpayer) : Results.NotFound(new { error = "Wajib Pajak tidak ditemukan" });
    }
}
