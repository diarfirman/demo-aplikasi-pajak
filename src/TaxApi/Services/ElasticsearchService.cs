using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using TaxApi.Models;

namespace TaxApi.Services;

public class ElasticsearchService
{
    private readonly ElasticsearchClient _client;
    private readonly ILogger<ElasticsearchService> _logger;

    private const string TaxPayersIndex = "pajak-taxpayers";
    private const string CalculationsIndex = "pajak-calculations";
    private const string ReportsIndex = "pajak-reports";
    private const string NotificationsIndex = "pajak-notifications";

    public ElasticsearchService(IConfiguration config, ILogger<ElasticsearchService> logger)
    {
        _logger = logger;
        var url = config["Elasticsearch:Url"] ?? throw new InvalidOperationException("Elasticsearch:Url not configured");
        var apiKey = config["Elasticsearch:ApiKey"] ?? throw new InvalidOperationException("Elasticsearch:ApiKey not configured");

        var settings = new ElasticsearchClientSettings(new Uri(url))
            .Authentication(new Elastic.Transport.ApiKey(apiKey))
            .DefaultMappingFor<TaxPayer>(m => m.IndexName(TaxPayersIndex))
            .DefaultMappingFor<TaxCalculation>(m => m.IndexName(CalculationsIndex))
            .DefaultMappingFor<TaxReport>(m => m.IndexName(ReportsIndex))
            .DefaultMappingFor<Notification>(m => m.IndexName(NotificationsIndex));

        _client = new ElasticsearchClient(settings);
    }

    // --- TaxPayer ---

    public async Task<TaxPayer> IndexTaxPayerAsync(TaxPayer taxPayer)
    {
        var response = await _client.IndexAsync(taxPayer, i => i.Index(TaxPayersIndex).Id(taxPayer.Id));
        if (!response.IsValidResponse)
            throw new Exception($"Failed to index taxpayer: {response.ElasticsearchServerError?.Error?.Reason}");
        return taxPayer;
    }

    public async Task<List<TaxPayer>> GetAllTaxPayersAsync()
    {
        var response = await _client.SearchAsync<TaxPayer>(s => s
            .Index(TaxPayersIndex)
            .Size(100)
            .Sort(sort => sort.Field(f => f.CreatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }

    public async Task<TaxPayer?> GetTaxPayerByIdAsync(string id)
    {
        var response = await _client.GetAsync<TaxPayer>(id, g => g.Index(TaxPayersIndex));
        return response.IsValidResponse ? response.Source : null;
    }

    public async Task<TaxPayer?> GetTaxPayerByNpwpAsync(string npwp)
    {
        var response = await _client.SearchAsync<TaxPayer>(s => s
            .Index(TaxPayersIndex)
            .Query(q => q.Term(t => t.Field(f => f.Npwp).Value(npwp))));
        return response.IsValidResponse ? response.Documents.FirstOrDefault() : null;
    }

    // --- TaxCalculation ---

    public async Task<TaxCalculation> IndexCalculationAsync(TaxCalculation calc)
    {
        var response = await _client.IndexAsync(calc, i => i.Index(CalculationsIndex).Id(calc.Id));
        if (!response.IsValidResponse)
            throw new Exception($"Failed to index calculation: {response.ElasticsearchServerError?.Error?.Reason}");
        return calc;
    }

    public async Task<List<TaxCalculation>> GetCalculationsByTaxPayerAsync(string taxPayerId)
    {
        var response = await _client.SearchAsync<TaxCalculation>(s => s
            .Index(CalculationsIndex)
            .Size(50)
            .Query(q => q.Term(t => t.Field(f => f.TaxPayerId).Value(taxPayerId)))
            .Sort(sort => sort.Field(f => f.CalculatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }

    public async Task<List<TaxCalculation>> GetAllCalculationsAsync()
    {
        var response = await _client.SearchAsync<TaxCalculation>(s => s
            .Index(CalculationsIndex)
            .Size(100)
            .Sort(sort => sort.Field(f => f.CalculatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }

    // --- TaxReport ---

    public async Task<TaxReport> IndexReportAsync(TaxReport report)
    {
        var response = await _client.IndexAsync(report, i => i.Index(ReportsIndex).Id(report.Id));
        if (!response.IsValidResponse)
            throw new Exception($"Failed to index report: {response.ElasticsearchServerError?.Error?.Reason}");
        return report;
    }

    public async Task<TaxReport?> GetReportByIdAsync(string id)
    {
        var response = await _client.GetAsync<TaxReport>(id, g => g.Index(ReportsIndex));
        return response.IsValidResponse ? response.Source : null;
    }

    public async Task<List<TaxReport>> GetAllReportsAsync()
    {
        var response = await _client.SearchAsync<TaxReport>(s => s
            .Index(ReportsIndex)
            .Size(100)
            .Sort(sort => sort.Field(f => f.CreatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }

    public async Task<List<TaxReport>> GetReportsByTaxPayerAsync(string taxPayerId)
    {
        var response = await _client.SearchAsync<TaxReport>(s => s
            .Index(ReportsIndex)
            .Size(50)
            .Query(q => q.Term(t => t.Field(f => f.TaxPayerId).Value(taxPayerId)))
            .Sort(sort => sort.Field(f => f.CreatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }

    public async Task UpdateReportAsync(TaxReport report)
    {
        var response = await _client.IndexAsync(report, i => i.Index(ReportsIndex).Id(report.Id));
        if (!response.IsValidResponse)
            throw new Exception($"Failed to update report: {response.ElasticsearchServerError?.Error?.Reason}");
    }

    // --- Notification ---

    public async Task<Notification> IndexNotificationAsync(Notification notification)
    {
        var response = await _client.IndexAsync(notification, i => i.Index(NotificationsIndex).Id(notification.Id));
        if (!response.IsValidResponse)
            throw new Exception($"Failed to index notification: {response.ElasticsearchServerError?.Error?.Reason}");
        return notification;
    }

    public async Task<List<Notification>> GetAllNotificationsAsync()
    {
        var response = await _client.SearchAsync<Notification>(s => s
            .Index(NotificationsIndex)
            .Size(50)
            .Sort(sort => sort.Field(f => f.CreatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }

    public async Task<List<Notification>> GetNotificationsByTaxPayerAsync(string taxPayerId)
    {
        var response = await _client.SearchAsync<Notification>(s => s
            .Index(NotificationsIndex)
            .Size(50)
            .Query(q => q.Term(t => t.Field(f => f.TaxPayerId).Value(taxPayerId)))
            .Sort(sort => sort.Field(f => f.CreatedAt, o => o.Order(SortOrder.Desc))));
        return response.IsValidResponse ? response.Documents.ToList() : [];
    }
}
