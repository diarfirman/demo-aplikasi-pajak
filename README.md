# Demo Tax Application (Aplikasi Pajak)

A simple tax management demo application showcasing **RabbitMQ back-and-forth messaging** and **end-to-end distributed tracing** with Elastic APM.

> Indonesian version: [README.id.md](README.id.md)

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 18 + TypeScript + Vite |
| API | .NET 9 Minimal API |
| Worker | .NET 9 Background Service |
| Messaging | RabbitMQ (direct AMQP, no MassTransit) |
| Database | Elasticsearch (cloud) |
| Observability | Elastic APM (distributed tracing) |

## Architecture

```
Frontend (React)  :5173
        │ HTTP (Axios)
        ▼
TaxApi (.NET 9)   :5001
  ├── CRUD: Taxpayers, Calculations, Reports, Notifications
  ├── PUBLISH → pajak.laporan.submitted
  └── CONSUME ← pajak.laporan.result → update ES + create notification
        │ AMQP ↕
     RabbitMQ
        │ AMQP ↕
ReportProcessor (.NET 9 Worker)
  ├── CONSUME ← pajak.laporan.submitted → validate report
  └── PUBLISH → pajak.laporan.result (approved/rejected)
```

**Back-and-forth flow:**
```
POST /api/reports/{id}/submit
  → TaxApi: status = "Submitted", PUBLISH to MQ
       ↓
  ReportProcessor: CONSUME, validate (NPWP, totals, etc.)
  → PUBLISH result to MQ
       ↓
  TaxApi: CONSUME result, update status + create notification
       ↓
GET /api/reports/{id}  → status = "Approved" / "Rejected"
GET /api/notifications → new notification appears
```

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for RabbitMQ)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 18+](https://nodejs.org/)
- [Elastic Cloud](https://cloud.elastic.co/) account (for Elasticsearch + APM)

## Configuration

Credentials are **not** stored in the repository. Create local override files:

### TaxApi

Create `src/TaxApi/appsettings.Development.json`:

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

Create `src/ReportProcessor/appsettings.Development.json`:

```json
{
  "ElasticApm": {
    "SecretToken": "<apm-secret-token>",
    "ServerUrl": "https://<apm-id>.apm.<region>.aws.cloud.es.io:443"
  }
}
```

> `appsettings.Development.json` is already in `.gitignore` — it will never be committed.
>
> .NET automatically loads this file when `ASPNETCORE_ENVIRONMENT=Development` (default when running `dotnet run`), overriding the placeholder values in `appsettings.json`.

### Frontend

Create `frontend/.env.local`:

```env
VITE_ELASTIC_APM_SERVER_URL=https://<apm-id>.apm.<region>.aws.cloud.es.io:443
VITE_ELASTIC_APM_SECRET_TOKEN=<apm-secret-token>
```

> If this file is absent, the frontend RUM agent will be disabled automatically — the app still runs, just without frontend traces.

## Running Locally

**1. Start RabbitMQ:**
```bash
cd docker
docker compose up rabbitmq -d
```
RabbitMQ UI: http://localhost:15672 (user: `pajak`, password: `pajak123`)

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

| Index | Contents |
|-------|----------|
| `pajak-taxpayers` | Taxpayer records |
| `pajak-calculations` | PPh21/PPN calculation results |
| `pajak-reports` | Tax reports + review status |
| `pajak-notifications` | Review result notifications |

---

## Telemetry with Elastic APM

This project uses [Elastic APM .NET Agent](https://www.elastic.co/guide/en/apm/agent/dotnet/current/index.html) for automatic and manual distributed tracing, and [Elastic APM RUM Agent](https://www.elastic.co/guide/en/apm/agent/rum-js/current/index.html) for browser-side tracing.

### APM Agent Configuration

The APM agent is configured via the `ElasticApm` section in `appsettings.json`:

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

| Field | Description |
|-------|-------------|
| `ServiceName` | Service name shown in APM UI and Service Map |
| `SecretToken` | Authentication token for APM Server |
| `ServerUrl` | APM Server URL (Elastic Cloud) |
| `Environment` | Environment label (`development`, `production`, etc.) |
| `LogLevel` | Log level for the APM agent itself |

### Alternative: Environment Variables

For Docker/CI deployments, use environment variables (these override `appsettings.json`):

```bash
ELASTIC_APM_SERVICE_NAME=pajak-taxapi
ELASTIC_APM_SECRET_TOKEN=xxx
ELASTIC_APM_SERVER_URL=https://...
ELASTIC_APM_ENVIRONMENT=production
```

### APM Agent Registration

In each service's `Program.cs`:

```csharp
// TaxApi/Program.cs — ASP.NET Core (includes automatic HTTP instrumentation)
builder.Services.AddAllElasticApm();

// ReportProcessor/Program.cs — Worker Service (manual instrumentation only)
builder.Services.AddElasticApm();
```

### Frontend RUM Agent

The browser-side APM agent (`@elastic/apm-rum`) is initialized in `frontend/src/apm.ts` before any other imports:

```typescript
// frontend/src/main.tsx
import './apm';  // must be first — intercepts all HTTP requests
import { StrictMode } from 'react'
// ...
```

`ApmRoutes` from `@elastic/apm-rum-react` wraps React Router to automatically create a transaction per route change. `distributedTracingOrigins` tells the RUM agent to inject `traceparent` into cross-origin requests to TaxApi.

---

## Manual Trace Propagation via RabbitMQ

Elastic APM does **not** automatically propagate trace context through RabbitMQ messages. This project implements manual propagation using the **W3C TraceContext** standard (`traceparent` header).

### Why Manual Propagation?

Without propagation, each service creates its own disconnected trace in APM UI. With propagation, a single report submission produces **one long trace** spanning Frontend → TaxApi → ReportProcessor → TaxApi.

### Implementation

#### 1. TaxApi: Inject traceparent on PUBLISH

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

`span.OutgoingDistributedTracingData?.SerializeToString()` produces a W3C traceparent string like:
```
00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
```

#### 2. ReportProcessor: Extract traceparent, create child transaction

```csharp
// src/ReportProcessor/Services/RabbitMqService.cs — StartConsumingAsync
var incomingTraceparent = GetTraceparentFromHeaders(args.BasicProperties.Headers);
var (result, outgoingTraceparent) = await handler(message, incomingTraceparent);

// src/ReportProcessor/Worker.cs — ProcessReportAsync
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
var transaction = Agent.Tracer.StartTransaction(
    "RabbitMQ CONSUME pajak.laporan.submitted",
    ApiConstants.TypeMessaging,
    tracingData);  // linked to TaxApi trace
```

#### 3. ReportProcessor: Inject traceparent on PUBLISH result

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

#### 4. TaxApi: Extract traceparent from result, create child transaction

```csharp
// src/TaxApi/Services/RabbitMqService.cs — OnReportResultReceived
var traceparent = GetTraceparentFromHeaders(args.BasicProperties.Headers);
var tracingData = DistributedTracingData.TryDeserializeFromString(traceparent);
var transaction = Agent.Tracer.StartTransaction(
    "RabbitMQ CONSUME pajak.laporan.result",
    ApiConstants.TypeMessaging,
    tracingData);  // linked to ReportProcessor trace
```

### Helper: Extract traceparent from RabbitMQ Headers

RabbitMQ stores string header values as `byte[]`, not `string`:

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

## How the App Decides: New Trace or Continue Existing?

Every service must decide: does the incoming message carry trace context from a previous service, or should it start a fresh trace?

### W3C traceparent Format

The propagated string follows the [W3C TraceContext](https://www.w3.org/TR/trace-context/) standard:

```
00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
│  │                                │                │
│  └─ traceId (16 bytes / 32 hex)  │                └─ flags (sampled=01)
│     Unique per end-to-end request │
└─ version (always "00")            └─ parentSpanId (8 bytes / 16 hex)
                                       Span that sent this message
```

**Simple rule:**
- `traceparent` present in header → create **child transaction** (same traceId, new span)
- `traceparent` absent / null → create **new trace** (new traceId)

### Mechanism in .NET APM Agent

```csharp
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
//               ↑ This makes the decision:
//               - valid string  → returns DistributedTracingData (not null)
//               - null/invalid  → returns null

var transaction = Agent.Tracer.StartTransaction(
    "RabbitMQ CONSUME pajak.laporan.submitted",
    ApiConstants.TypeMessaging,
    tracingData);
//  ↑ tracingData not null → child transaction (traceId inherited from TaxApi)
//  ↑ tracingData null     → new trace (traceId randomly generated)
```

### Concrete Example: Report Submission

When a user clicks "Submit" in the browser, here is how the same `traceId` flows through each service:

**Step 1 — Browser (RUM Agent)**
```
Browser sends: POST http://localhost:5001/api/reports/5281ff79-7241-4be8-9a3b-ac1dfa1c4be9/submit
Automatic header:  traceparent: 00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-browser>-01
                                   ↑ new traceId generated by RUM agent
```

**Step 2 — TaxApi receives HTTP request**
```
.NET APM agent reads traceparent header from request
→ Creates child transaction "POST /api/reports/{id}/submit"
→ traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a  (same as browser)
→ parentSpanId: <spanId-browser>
```

**Step 3 — TaxApi PUBLISHes to RabbitMQ**
```csharp
span.OutgoingDistributedTracingData?.SerializeToString()
// Produces:
// "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-taxapi-publish>-01"
//   SAME traceId, parentSpanId = ID of TaxApi PUBLISH span
```

This string is written into the RabbitMQ message header.

**Step 4 — ReportProcessor CONSUMEs from RabbitMQ**
```csharp
var incomingTraceparent = "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-taxapi-publish>-01";
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
// tracingData != null → child transaction
// traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a  (STILL THE SAME)
```

**Step 5 — ReportProcessor PUBLISHes result to RabbitMQ**
```csharp
transaction.OutgoingDistributedTracingData?.SerializeToString()
// Produces:
// "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-reportprocessor>-01"
//   SAME traceId, parentSpanId = ID of ReportProcessor transaction
```

**Step 6 — TaxApi CONSUMEs result from RabbitMQ**
```csharp
var incomingTraceparent = "00-7152c87c4d97eaf4a41c8b6f8ce4434a-<spanId-reportprocessor>-01";
var tracingData = DistributedTracingData.TryDeserializeFromString(incomingTraceparent);
// tracingData != null → child transaction
// traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a  (STILL THE SAME)
```

### Trace Waterfall in APM UI

The same traceId appears as a single unified trace in APM, spanning 3 separate processes.

Real example with `traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a`:

```
traceId: 7152c87c4d97eaf4a41c8b6f8ce4434a
│
├── [pajak-frontend]  Click - button  171 ms
│       │
│       └── POST http://localhost:5001/api/reports/.../submit  170 ms
│                │
│       [pajak-taxapi]  HTTP 2xx POST /api/reports/{id}/submit  153 ms
│                │
│                ├── GET elasticsearch (read report data)  29 ms
│                ├── PUT elasticsearch (update status → Submitted)  123 ms
│                └── RabbitMQ PUBLISH pajak.laporan.submitted  481 µs
│                           │  (header: traceparent 7152c87c...)
│                           ▼
│       [pajak-report-processor]  RabbitMQ CONSUME pajak.laporan.submitted  2,014 ms
│                           │
│                           └── ValidasiLaporan  122 µs
│                               │  (header: traceparent 7152c87c... in result)
│                               ▼
│       [pajak-taxapi]  RabbitMQ CONSUME pajak.laporan.result  95 ms
│                           │
│                           ├── GET elasticsearch  28 ms
│                           └── ES UpdateReport  31 ms
```

<img width="1000" height="600" alt="APM Trace Waterfall" src="https://github.com/user-attachments/assets/0d2b199f-d547-44f2-a0ca-cb190bfaea70" />

### Summary: When New Trace vs. Continue Existing?

| Condition | `tracingData` | Result of `StartTransaction` |
|-----------|---------------|------------------------------|
| HTTP request from browser (with `traceparent` header) | not null | Child transaction — traceId same as browser |
| HTTP request without `traceparent` header (e.g. direct curl) | null | New trace — random traceId |
| RabbitMQ message with `elastic-apm-traceparent` header | not null | Child transaction — traceId same as publisher |
| RabbitMQ message without header (e.g. legacy/manual message) | null | New trace — random traceId |
| Header present but corrupt / invalid format | null | New trace — random traceId |

**In short:** `TryDeserializeFromString()` validates the format. If valid → continue the trace. If not → start a new one. No exceptions thrown, no extra configuration — just a null check.
