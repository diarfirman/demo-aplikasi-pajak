# Demo Aplikasi Pajak

Demo aplikasi pengelolaan pajak sederhana yang menampilkan komunikasi dua arah (back-and-forth) melalui RabbitMQ dan distributed tracing end-to-end dengan Elastic APM.

## Stack Teknologi

| Layer | Teknologi |
|-------|-----------|
| Frontend | React 18 + TypeScript + Vite |
| API | .NET 9 Minimal API |
| Worker | .NET 9 Background Service |
| Messaging | RabbitMQ (direct AMQP, tanpa MassTransit) |
| Database | Elasticsearch (cloud) |
| Observability | Elastic APM (distributed tracing) |

## Arsitektur

```
Frontend (React)  :5173
        │ HTTP (Axios)
        ▼
TaxApi (.NET 9)   :5001
  ├── CRUD: Wajib Pajak, Perhitungan, Laporan, Notifikasi
  ├── PUBLISH → pajak.laporan.submitted
  └── CONSUME ← pajak.laporan.result → update ES + buat notifikasi
        │ AMQP ↕
     RabbitMQ
        │ AMQP ↕
ReportProcessor (.NET 9 Worker)
  ├── CONSUME ← pajak.laporan.submitted → validasi laporan
  └── PUBLISH → pajak.laporan.result (approved/rejected)
```

**Flow back-and-forth:**
```
POST /api/reports/{id}/submit
  → TaxApi: status = "Submitted", PUBLISH ke MQ
       ↓
  ReportProcessor: CONSUME, validasi (NPWP, total, dll.)
  → PUBLISH result ke MQ
       ↓
  TaxApi: CONSUME result, update status + buat notifikasi
       ↓
GET /api/reports/{id}  → status = "Approved" / "Rejected"
GET /api/notifications → notifikasi baru muncul
```

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (untuk RabbitMQ)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 18+](https://nodejs.org/)
- Akun [Elastic Cloud](https://cloud.elastic.co/) (untuk Elasticsearch + APM)

## Setup Konfigurasi

Credentials **tidak** disimpan di repository. Buat file override lokal:

### TaxApi

Buat file `src/TaxApi/appsettings.Development.json`:

```json
{
  "Elasticsearch": {
    "Url": "https://<cluster-id>.es.<region>.aws.found.io:443",
    "ApiKey": "<elasticsearch-api-key>"
  },
  "ElasticApm": {
    "SecretToken": "<apm-secret-token>",
    "ServerUrl": "https://<apm-id>.apm.<region>.aws.cloud.es.io:443"
  }
}
```

### ReportProcessor

Buat file `src/ReportProcessor/appsettings.Development.json`:

```json
{
  "ElasticApm": {
    "SecretToken": "<apm-secret-token>",
    "ServerUrl": "https://<apm-id>.apm.<region>.aws.cloud.es.io:443"
  }
}
```

> File `appsettings.Development.json` sudah ada di `.gitignore` — tidak akan ter-commit.

## Cara Menjalankan

**1. Start RabbitMQ:**
```bash
cd docker
docker compose up rabbitmq -d
```
UI RabbitMQ: http://localhost:15672 (user: `pajak`, password: `pajak123`)

**2. Start TaxApi:**
```bash
cd src/TaxApi
dotnet run
```
Swagger: http://localhost:5001/swagger

**3. Start ReportProcessor:**
```bash
cd src/ReportProcessor
dotnet run
```

**4. Start Frontend:**
```bash
cd frontend
npm install
npm run dev
```
UI: http://localhost:5173

## Elasticsearch Indexes

| Index | Isi |
|-------|-----|
| `pajak-taxpayers` | Data Wajib Pajak |
| `pajak-calculations` | Hasil perhitungan PPh21/PPN |
| `pajak-reports` | Laporan SPT + status review |
| `pajak-notifications` | Notifikasi hasil review |

---

## Telemetry dengan Elastic APM

Project ini menggunakan [Elastic APM .NET Agent](https://www.elastic.co/guide/en/apm/agent/dotnet/current/index.html) untuk distributed tracing otomatis dan manual.

### Konfigurasi APM Agent

APM agent dikonfigurasi via section `ElasticApm` di `appsettings.json`:

```json
{
  "ElasticApm": {
    "ServiceName": "pajak-taxapi",
    "SecretToken": "YOUR_APM_SECRET_TOKEN",
    "ServerUrl": "https://YOUR_APM_ID.apm.REGION.aws.cloud.es.io:443",
    "Environment": "development",
    "LogLevel": "Error"
  }
}
```

| Field | Keterangan |
|-------|-----------|
| `ServiceName` | Nama service yang tampil di APM UI dan Service Map |
| `SecretToken` | Token autentikasi ke APM Server |
| `ServerUrl` | URL APM Server (Elastic Cloud) |
| `Environment` | Label environment (`development`, `production`, dll.) |
| `LogLevel` | Level log agent APM itu sendiri |

### Alternatif: Environment Variables

Untuk deployment Docker/CI, gunakan environment variables (override `appsettings.json`):

```bash
ELASTIC_APM_SERVICE_NAME=pajak-taxapi
ELASTIC_APM_SECRET_TOKEN=xxx
ELASTIC_APM_SERVER_URL=https://...
ELASTIC_APM_ENVIRONMENT=production
```

### Registrasi APM Agent

Di `Program.cs` masing-masing service:

```csharp
// TaxApi/Program.cs
builder.Services.AddAllElasticApm();

// ReportProcessor/Program.cs
builder.Services.AddElasticApm();
```

`AddAllElasticApm()` menambahkan instrumentasi HTTP otomatis untuk ASP.NET Core. `AddElasticApm()` untuk worker service tanpa HTTP.

---

## Manual Trace Propagation via RabbitMQ

Elastic APM tidak otomatis mempropagasi trace context melalui RabbitMQ messages. Project ini mengimplementasikan propagasi manual menggunakan **W3C TraceContext** (`traceparent` header).

### Mengapa Perlu Propagasi Manual?

Tanpa propagasi, setiap service membuat trace sendiri-sendiri yang tidak terhubung di APM UI. Dengan propagasi, satu submit laporan menghasilkan **satu trace panjang** yang melintasi TaxApi → ReportProcessor → TaxApi.

### Implementasi

#### 1. TaxApi: Inject traceparent saat PUBLISH

```csharp
// src/TaxApi/Services/RabbitMqService.cs — PublishReportSubmittedAsync
await currentTransaction.CaptureSpan(
    $"RabbitMQ PUBLISH {SubmittedQueue}",
    ApiConstants.TypeMessaging,
    async (span) =>
    {
        var props = new BasicProperties
        {
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                {
                    "elastic-apm-traceparent",
                    Encoding.UTF8.GetBytes(
                        span.OutgoingDistributedTracingData?.SerializeToString() ?? "")
                }
            }
        };
        await _channel.BasicPublishAsync("", SubmittedQueue, false, props, body);
    }
);
```

`span.OutgoingDistributedTracingData?.SerializeToString()` menghasilkan string W3C traceparent seperti:
```
00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
```

#### 2. ReportProcessor: Extract traceparent, buat child transaction

```csharp
// src/ReportProcessor/Services/RabbitMqService.cs — StartConsumingAsync
var incomingTraceparent = GetTraceparentFromHeaders(args.BasicProperties.Headers);
var (result, outgoingTraceparent) = await handler(message, incomingTraceparent);

// src/ReportProcessor/Worker.cs — ProcessReportAsync
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
var transaction = Agent.Tracer.StartTransaction(
    "RabbitMQ CONSUME pajak.laporan.submitted",
    ApiConstants.TypeMessaging,
    tracingData);  // linked ke TaxApi trace
```

#### 3. ReportProcessor: Inject traceparent baru saat PUBLISH result

```csharp
// src/ReportProcessor/Services/RabbitMqService.cs
var outgoingTraceparent = transaction.OutgoingDistributedTracingData?.SerializeToString();

var props = new BasicProperties
{
    Headers = new Dictionary<string, object?>
    {
        { "elastic-apm-traceparent", Encoding.UTF8.GetBytes(outgoingTraceparent ?? "") }
    }
};
await _channel.BasicPublishAsync("", ResultQueue, false, props, resultBody);
```

#### 4. TaxApi: Extract traceparent dari result, buat child transaction

```csharp
// src/TaxApi/Services/RabbitMqService.cs — OnReportResultReceived
var traceparent = GetTraceparentFromHeaders(args.BasicProperties.Headers);
var tracingData = DistributedTracingData.TryDeserializeFromString(traceparent);
var transaction = Agent.Tracer.StartTransaction(
    "RabbitMQ CONSUME pajak.laporan.result",
    ApiConstants.TypeMessaging,
    tracingData);  // linked ke ReportProcessor trace
```

### Trace End-to-End di APM UI

Setelah implementasi, satu submit laporan menghasilkan trace seperti ini:

```
TaxApi: POST /api/reports/{id}/submit
  └── span: RabbitMQ PUBLISH pajak.laporan.submitted
                    ↓ (traceparent di header)
ReportProcessor: RabbitMQ CONSUME pajak.laporan.submitted  ← child dari TaxApi
  └── span: ValidasiLaporan
                    ↓ (traceparent di header result)
TaxApi: RabbitMQ CONSUME pajak.laporan.result  ← child dari ReportProcessor
  ├── span: ES UpdateReport
  └── span: ES IndexNotification
```

### Helper: Extract traceparent dari RabbitMQ Headers

RabbitMQ menyimpan header string sebagai `byte[]`, bukan `string`:

```csharp
private static string? GetTraceparentFromHeaders(IDictionary<string, object?>? headers)
{
    if (headers?.TryGetValue("elastic-apm-traceparent", out var val) == true)
    {
        if (val is byte[] bytes) return Encoding.UTF8.GetString(bytes);
        if (val is string str) return str;
    }
    return null;
}
```
