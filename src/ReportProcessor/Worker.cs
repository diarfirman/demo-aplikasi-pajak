using Elastic.Apm;
using Elastic.Apm.Api;
using ReportProcessor.Models;
using ReportProcessor.Services;

namespace ReportProcessor;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly RabbitMqConsumerService _mqService;

    public Worker(ILogger<Worker> logger, RabbitMqConsumerService mqService)
    {
        _logger = logger;
        _mqService = mqService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReportProcessor Worker starting...");

        // Retry connection dengan backoff
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await _mqService.ConnectAsync(stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ connection attempt {Attempt}/5 failed: {Error}", attempt, ex.Message);
                if (attempt == 5)
                {
                    _logger.LogError("Cannot connect to RabbitMQ after 5 attempts. Exiting.");
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(attempt * 3), stoppingToken);
            }
        }

        await _mqService.StartConsumingAsync(ProcessReportAsync, stoppingToken);

        _logger.LogInformation("ReportProcessor ready. Waiting for reports to review...");

        // Keep alive sampai cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task<ReportResultMessage> ProcessReportAsync(ReportSubmittedMessage report)
    {
        return await Agent.Tracer.CaptureTransaction(
            $"RabbitMQ CONSUME pajak.laporan.submitted",
            ApiConstants.TypeMessaging,
            async (transaction) =>
            {
                transaction.SetLabel("report_id", report.ReportId);
                transaction.SetLabel("tax_payer_id", report.TaxPayerId);
                transaction.SetLabel("period", report.Period ?? "");

                // Simulasi review dengan delay 2-3 detik (seperti proses nyata)
                await Task.Delay(TimeSpan.FromSeconds(2));

                var result = await transaction.CaptureSpan(
                    "ValidasiLaporan",
                    ApiConstants.TypeApp,
                    () =>
                    {
                        var r = ValidasiLaporan(report);
                        return Task.FromResult(r);
                    }
                );

                if (result.IsValid)
                {
                    transaction.SetLabel("result", "approved");
                    _logger.LogInformation("Report {Id} APPROVED: {Reason}", report.ReportId, result.Reason);
                    return new ReportResultMessage(
                        ReportId: report.ReportId,
                        TaxPayerId: report.TaxPayerId,
                        IsApproved: true,
                        RejectionReason: null
                    );
                }
                else
                {
                    transaction.SetLabel("result", "rejected");
                    transaction.SetLabel("rejection_reason", result.Reason);
                    _logger.LogWarning("Report {Id} REJECTED: {Reason}", report.ReportId, result.Reason);
                    return new ReportResultMessage(
                        ReportId: report.ReportId,
                        TaxPayerId: report.TaxPayerId,
                        IsApproved: false,
                        RejectionReason: result.Reason
                    );
                }
            }
        );
    }

    private static (bool IsValid, string Reason) ValidasiLaporan(ReportSubmittedMessage report)
    {
        // Validasi 1: NPWP harus ada (tidak kosong)
        if (string.IsNullOrWhiteSpace(report.TaxPayerNpwp))
            return (false, "NPWP Wajib Pajak tidak valid atau kosong.");

        // Validasi 2: Total penghasilan harus positif
        if (report.TotalIncome <= 0)
            return (false, "Total penghasilan harus lebih dari 0.");

        // Validasi 3: Total pajak tidak boleh negatif
        if (report.TotalTax < 0)
            return (false, "Total pajak tidak boleh bernilai negatif.");

        // Validasi 4: Pajak tidak boleh melebihi 50% penghasilan (sanity check)
        if (report.TotalTax > report.TotalIncome * 0.5m)
            return (false, $"Total pajak (Rp {report.TotalTax:N0}) melebihi 50% dari penghasilan. Mohon periksa kembali.");

        // Validasi 5: Periode harus diisi
        if (string.IsNullOrWhiteSpace(report.Period))
            return (false, "Periode laporan tidak boleh kosong.");

        return (true, "Laporan memenuhi semua persyaratan.");
    }
}
