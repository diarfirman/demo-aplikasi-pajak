# Demo Aplikasi Pajak

Demo aplikasi pengelolaan pajak sederhana yang menampilkan komunikasi dua arah (back-and-forth) melalui RabbitMQ dan distributed tracing end-to-end dengan Elastic APM.

<img width="1000" height="600" alt="image" src="https://github.com/user-attachments/assets/aafee06a-1102-4984-956d-5c97e2221ffa" />


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
    "Url": "https://your.elasticsearch.url:443",
    "ApiKey": "<elasticsearch-api-key>"
  },
  "ElasticApm": {
    "SecretToken": "<apm-secret-token>",
    "ServerUrl": "https://your.apm.server.url:443"
  }
}
```

### ReportProcessor

Buat file `src/ReportProcessor/appsettings.Development.json`:

```json
{
  "ElasticApm": {
    "SecretToken": "<apm-secret-token>",
    "ServerUrl": "https://your.apm.server.url:443"
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
    "ServerUrl": "https://your.apm.server.url:443",
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

#### 0. Frontend (RUM Agent): Inject traceparent otomatis

Sisi browser **tidak memerlukan kode manual** — RUM agent menanganinya secara otomatis berdasarkan konfigurasi `distributedTracingOrigins` di `frontend/src/apm.ts`:

```typescript
// frontend/src/apm.ts
const config = {
  serviceName: 'pajak-frontend',
  serverUrl: import.meta.env.VITE_ELASTIC_APM_SERVER_URL,
  secretToken: import.meta.env.VITE_ELASTIC_APM_SECRET_TOKEN,
  distributedTracingOrigins: [
    import.meta.env.VITE_API_URL ?? 'http://localhost:5001',
  ],
};
```

Setiap kali Axios mengirim request ke origin yang ada di `distributedTracingOrigins`, RUM agent **otomatis menambahkan** header `traceparent`:

```
POST http://localhost:5001/api/reports/{id}/submit
traceparent: 00-7152c87c4d97eaf4a41c8b6f8ce4434a-a1b2c3d4e5f6a7b8-01
             ↑ Header ini ditambahkan oleh RUM agent, bukan kode aplikasi
```

`ApmRoutes` (membungkus React Router di `App.tsx`) membuat APM transaction baru di setiap pergantian route, sehingga setiap navigasi halaman juga tercatat sebagai span tersendiri. `traceparent` yang diinjeksikan ke HTTP request membawa traceId dari **transaksi halaman saat ini**, menjadikan browser sebagai akar dari seluruh distributed trace.

> Catatan: `distributedTracingOrigins` diperlukan karena frontend (`localhost:5173`) dan TaxApi (`localhost:5001`) berada di origin yang berbeda. Tanpanya, CORS policy browser akan memblokir custom header tersebut, dan RUM agent akan melewati injeksi untuk request lintas origin. TaxApi mengizinkan ini melalui `AllowAnyHeader()` di konfigurasi CORS-nya.

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

---

## Bagaimana Aplikasi Memutuskan: Trace Baru atau Teruskan Trace?

Setiap service harus memutuskan: apakah pesan yang datang membawa context trace dari service sebelumnya, atau harus memulai trace baru?

### Format W3C traceparent

String yang dipropagasi mengikuti standar [W3C TraceContext](https://www.w3.org/TR/trace-context/):

```
00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
│  │                                │                │
│  └─ traceId (16 bytes / 32 hex)  │                └─ flags (sampled=01)
│     Unik per satu request end-to-end               │
└─ version (selalu "00")            └─ parentSpanId (8 bytes / 16 hex)
                                       Span yang mengirim pesan ini
```

**Aturan sederhananya:**
- `traceparent` ada di header → buat **child transaction** (traceId sama, span baru)
- `traceparent` tidak ada / null → buat **trace baru** (traceId baru)

### Mekanisme di .NET APM Agent

```csharp
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
//               ↑ Ini yang membuat keputusan:
//               - string valid  → return DistributedTracingData (tidak null)
//               - string null/invalid → return null

var transaction = Agent.Tracer.StartTransaction(
    "RabbitMQ CONSUME pajak.laporan.submitted",
    ApiConstants.TypeMessaging,
    tracingData);
//  ↑ tracingData tidak null → child transaction (traceId diwarisi dari TaxApi)
//  ↑ tracingData null       → trace baru (traceId di-generate random)
```

### Contoh Konkret: Submit Laporan

Misalkan user klik "Submit" di browser. Berikut bagaimana `traceId` yang sama mengalir:

**Langkah 1 — Browser (RUM Agent)**
```
Browser kirim: POST http://localhost:5001/api/reports/5281ff79-7241-4be8-9a3b-ac1dfa1c4be9/submit
Header otomatis:  traceparent: 00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-browser>-01
                                  ↑ traceId baru di-generate RUM agent
```

**Langkah 2 — TaxApi menerima HTTP request**
```
APM agent .NET baca header traceparent dari request
→ Buat child transaction "POST /api/reports/{id}/submit"
→ traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a  (sama dengan browser)
→ parentSpanId: <spanId-browser>
```

**Langkah 3 — TaxApi PUBLISH ke RabbitMQ**
```csharp
span.OutgoingDistributedTracingData?.SerializeToString()
// Menghasilkan:
// "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-taxapi-publish>-01"
//   traceId SAMA, parentSpanId = ID span PUBLISH TaxApi
```

Header ini dimasukkan ke pesan RabbitMQ.

**Langkah 4 — ReportProcessor CONSUME dari RabbitMQ**
```csharp
var incomingTraceparent = "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-taxapi-publish>-01";
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
// tracingData != null → child transaction
// traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a  (MASIH SAMA)
```

**Langkah 5 — ReportProcessor PUBLISH result ke RabbitMQ**
```csharp
transaction.OutgoingDistributedTracingData?.SerializeToString()
// Menghasilkan:
// "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-reportprocessor>-01"
//   traceId SAMA, parentSpanId = ID transaction ReportProcessor
```

**Langkah 6 — TaxApi CONSUME result dari RabbitMQ**
```csharp
var incomingTraceparent = "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-reportprocessor>-01";
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
// tracingData != null → child transaction
// traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a  (MASIH SAMA)
```

### Visualisasi Trace di APM UI

Satu traceId yang sama muncul sebagai satu trace tunggal di APM, meskipun melewati 3 proses berbeda.

Contoh nyata dengan `traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a`:

```
traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a
│
├── [pajak-frontend]  Click - button  171 ms
│       │
│       └── POST http://localhost:5001/api/reports/.../submit  170 ms
│                │
│       [pajak-taxapi]  HTTP 2xx POST /api/reports/{id}/submit  153 ms
│                │
│                ├── GET elasticsearch (baca data laporan)  29 ms
│                ├── PUT elasticsearch (update status → Submitted)  123 ms
│                └── RabbitMQ PUBLISH pajak.laporan.submitted  481 µs
│                           │  (header: traceparent 7152c87c...)
│                           ▼
│       [pajak-report-processor]  RabbitMQ CONSUME pajak.laporan.submitted  2,014 ms
│                           │
│                           └── ValidasiLaporan  122 µs
│                               │  (header: traceparent 7152c87c... di result)
│                               ▼
│       [pajak-taxapi]  RabbitMQ CONSUME pajak.laporan.result  95 ms
│                           │
│                           ├── GET elasticsearch  28 ms
│                           └── ES UpdateReport  31 ms
```

<!-- Screenshot trace waterfall di Elastic APM dapat ditempatkan di sini -->
<!-- Contoh: ![Trace Waterfall](docs/trace-waterfall.png) -->

### Ringkasan: Kapan Trace Baru, Kapan Terusan?

| Kondisi | `tracingData` | Hasil `StartTransaction` |
|---------|---------------|--------------------------|
| Request HTTP dari browser (ada header `traceparent`) | tidak null | Child transaction — traceId sama dengan browser |
| Request HTTP tanpa header `traceparent` (misal: curl langsung) | null | Trace baru — traceId random |
| RabbitMQ message dengan header `elastic-apm-traceparent` | tidak null | Child transaction — traceId sama dengan publisher |
| RabbitMQ message tanpa header (misal: pesan lama/manual) | null | Trace baru — traceId random |
| Header ada tapi corrupt / format salah | null | Trace baru — traceId random |

**Singkatnya:** `TryDeserializeFromString()` melakukan validasi format. Jika valid → teruskan trace. Jika tidak → mulai trace baru. Tidak ada exception, tidak ada konfigurasi tambahan — hanya cek null.
