using TaxApi.Models;

namespace TaxApi.Services;

public static class TaxCalculatorService
{
    // PTKP per tahun (2024)
    private static readonly Dictionary<string, decimal> PtkpMap = new()
    {
        ["TK0"] = 54_000_000m,
        ["K0"]  = 58_500_000m,
        ["K1"]  = 63_000_000m,
        ["K2"]  = 67_500_000m,
        ["K3"]  = 72_000_000m,
    };

    public static CalculationResult HitungPPh21(decimal grossMonthly, string maritalStatus)
    {
        var ptkpTahunan = PtkpMap.GetValueOrDefault(maritalStatus, 54_000_000m);
        var penghasilanBrutoTahunan = grossMonthly * 12;
        var pkp = Math.Max(0, penghasilanBrutoTahunan - ptkpTahunan);

        var pajakTahunan = HitungTarifProgresif(pkp);
        var pajakBulanan = pajakTahunan / 12;

        return new CalculationResult(
            GrossAmount: grossMonthly,
            TaxAmount: Math.Round(pajakBulanan, 0),
            NetAmount: grossMonthly - Math.Round(pajakBulanan, 0),
            Description: $"PPh21 bulanan. PKP tahunan: Rp {pkp:N0}, Tarif progresif"
        );
    }

    public static CalculationResult HitungPPN(decimal amount)
    {
        const decimal ppnRate = 0.12m; // PPN 12% per 2025
        var ppn = amount * ppnRate;

        return new CalculationResult(
            GrossAmount: amount,
            TaxAmount: Math.Round(ppn, 0),
            NetAmount: amount + Math.Round(ppn, 0),
            Description: $"PPN 12% dari Rp {amount:N0}"
        );
    }

    private static decimal HitungTarifProgresif(decimal pkp)
    {
        decimal pajak = 0;

        // 5% untuk 0 - 60 juta
        if (pkp <= 60_000_000)
            return pkp * 0.05m;

        pajak += 60_000_000 * 0.05m;
        pkp -= 60_000_000;

        // 15% untuk 60 - 250 juta
        if (pkp <= 190_000_000)
            return pajak + pkp * 0.15m;

        pajak += 190_000_000 * 0.15m;
        pkp -= 190_000_000;

        // 25% untuk 250 - 500 juta
        if (pkp <= 250_000_000)
            return pajak + pkp * 0.25m;

        pajak += 250_000_000 * 0.25m;
        pkp -= 250_000_000;

        // 30% untuk 500 juta - 5 miliar
        if (pkp <= 4_500_000_000)
            return pajak + pkp * 0.30m;

        pajak += 4_500_000_000 * 0.30m;
        pkp -= 4_500_000_000;

        // 35% di atas 5 miliar
        return pajak + pkp * 0.35m;
    }
}
