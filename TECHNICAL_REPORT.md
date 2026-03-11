# Technical Report: Demo Aplikasi Pajak

**Project:** demo-aplikasi-pajak
**Author:** Diar Firman
**Date:** March 2026
**Repository:** https://github.com/diarfirman/demo-aplikasi-pajak

---

## 1. Overview

Demo Aplikasi Pajak is a simplified tax reporting web application built with a microservices architecture. The primary goal is to demonstrate **bidirectional (back-and-forth) communication via RabbitMQ** between services, with data persistence on Elasticsearch Cloud and full observability through Elastic APM.

This project was intentionally designed to be **flat and minimal** — avoiding the over-engineering of enterprise Clean Architecture patterns — while still showcasing meaningful inter-service messaging.

---

## 2. Architecture

```
┌──────────────────────────────────────────────────────────┐
│  Frontend (React + TypeScript + Vite)       :3000        │
│  - Polling GET /api/reports & /api/notifications         │
└────────────────────────┬─────────────────────────────────┘
                         │ HTTP (Axios)
┌────────────────────────▼─────────────────────────────────┐
│  TaxApi (.NET 9 Minimal API)                :5001        │
│  - CRUD Wajib Pajak, Perhitungan Pajak, Laporan SPT      │
│  - PUBLISH → "pajak.laporan.submitted"                   │
│  - CONSUME ← "pajak.laporan.result"                      │
│    → update status laporan + buat notifikasi di ES       │
└──────────┬──────────────────────────┬────────────────────┘
           │ AMQP (2 arah)            │ HTTPS
┌──────────▼──────────┐  ┌────────────▼──────────────────┐
│  RabbitMQ           │  │  Elasticsearch Cloud           │
│  Docker :5672       │  │  pajak-taxpayers               │
│  UI: :15672         │  │  pajak-calculations            │
│                     │  │  pajak-reports                 │
│  Queues:            │  │  pajak-notifications           │
│  laporan.submitted  │  └───────────────────────────────┘
│  laporan.result     │
└──────────┬──────────┘
           │ AMQP (2 arah)
┌──────────▼─────────────────────────────────────────────┐
│  ReportProcessor (.NET 9 Worker Service)               │
│  - CONSUME ← "pajak.laporan.submitted"                 │
│    → validasi laporan (NPWP, total, periode, dll.)     │
│  - PUBLISH → "pajak.laporan.result" (approved/rejected)│
└────────────────────────────────────────────────────────┘
```

### Back-and-Forth RabbitMQ Flow

```
POST /api/reports/{id}/submit
  │
  ▼  TaxApi: status = "Submitted", PUBLISH ke pajak.laporan.submitted
              ↓
  ReportProcessor: CONSUME, validasi laporan
  → PUBLISH result ke pajak.laporan.result
              ↓
  TaxApi: CONSUME result, update status ES + buat notifikasi
              ↓
GET /api/reports/{id}  → status = "Approved" / "Rejected"
GET /api/notifications → notifikasi baru muncul
```

---

## 3. Technology Stack

### Backend

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 9.0 |
| API Style | Minimal API | — |
| Message Broker Client | RabbitMQ.Client | 7.x |
| Elasticsearch Client | Elastic.Clients.Elasticsearch | 9.3.x |
| APM Agent (API) | Elastic.Apm.NetCoreAll | 1.34.x |
| APM Agent (Worker) | Elastic.Apm.Extensions.Hosting | 1.34.x |

### Frontend

| Component | Technology | Version |
|-----------|-----------|---------|
| Framework | React | 18.x |
| Language | TypeScript | 5.x |
| Build Tool | Vite | 6.x |
| HTTP Client | Axios | 1.x |
| Router | React Router | 6.x |
| Styling | CSS (custom) | — |

### Infrastructure

| Component | Technology |
|-----------|-----------|
| Message Broker | RabbitMQ 3.13 (Docker) |
| Database | Elasticsearch Cloud (AWS ap-southeast-1) |
| APM Server | Elastic Cloud APM (AWS ap-southeast-1) |
| Container | Docker / Docker Compose |

---

## 4. Project Structure

```
aplikasi-pajak/
├── frontend/                         # React + TypeScript + Vite
│   ├── src/
│   │   ├── pages/
│   │   │   ├── TaxPayersPage.tsx     # CRUD wajib pajak
│   │   │   ├── CalculationsPage.tsx  # Hitung PPh21 / PPN
│   │   │   ├── ReportsPage.tsx       # Laporan SPT + submit flow
│   │   │   └── NotificationsPage.tsx # Notifikasi hasil review
│   │   ├── services/api.ts           # Axios client
│   │   └── App.tsx                   # Router + Navbar
│   ├── Dockerfile
│   └── nginx.conf
│
├── src/
│   ├── TaxApi/                       # .NET 9 Minimal API
│   │   ├── Endpoints/
│   │   │   ├── TaxPayerEndpoints.cs
│   │   │   ├── CalculationEndpoints.cs
│   │   │   ├── ReportEndpoints.cs
│   │   │   └── NotificationEndpoints.cs
│   │   ├── Models/
│   │   │   ├── TaxPayer.cs
│   │   │   ├── TaxCalculation.cs
│   │   │   ├── TaxReport.cs
│   │   │   └── Notification.cs
│   │   ├── Services/
│   │   │   ├── ElasticsearchService.cs
│   │   │   ├── RabbitMqService.cs    # Publish + Consume (IHostedService)
│   │   │   └── TaxCalculatorService.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── ReportProcessor/              # .NET 9 Worker Service
│       ├── Models/
│       │   └── ReportMessages.cs     # Message contracts
│       ├── Services/
│       │   └── RabbitMqService.cs    # Consumer + Publisher
│       ├── Worker.cs                 # Validation logic
│       ├── Program.cs
│       └── appsettings.json
│
├── docker/
│   └── docker-compose.yml
└── PajakApp.sln
```

---

## 5. API Endpoints

### Wajib Pajak
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/taxpayers` | Daftar wajib pajak baru |
| GET | `/api/taxpayers` | List semua wajib pajak |
| GET | `/api/taxpayers/{id}` | Detail wajib pajak |
| GET | `/api/taxpayers/npwp/{npwp}` | Cari by NPWP |

### Perhitungan Pajak
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/calculations` | Hitung PPh21 / PPN |
| GET | `/api/calculations` | List semua perhitungan |
| GET | `/api/calculations/taxpayer/{id}` | Perhitungan by wajib pajak |

### Laporan SPT
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/reports` | Buat laporan baru |
| GET | `/api/reports` | List semua laporan |
| GET | `/api/reports/{id}` | Detail laporan |
| POST | `/api/reports/{id}/submit` | **Submit laporan → trigger RabbitMQ flow** |

### Notifikasi
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/notifications` | List semua notifikasi |
| GET | `/api/notifications/taxpayer/{id}` | Notifikasi by wajib pajak |

### Health
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Health check |

---

## 6. Tax Calculation Logic

### PPh 21 (Pajak Penghasilan Orang Pribadi)

Menggunakan tarif progresif berdasarkan Penghasilan Kena Pajak (PKP) setelah dikurangi PTKP:

| PKP | Tarif |
|-----|-------|
| s.d. Rp 60.000.000 | 5% |
| Rp 60.000.001 – Rp 250.000.000 | 15% |
| Rp 250.000.001 – Rp 500.000.000 | 25% |
| Rp 500.000.001 – Rp 5.000.000.000 | 30% |
| > Rp 5.000.000.000 | 35% |

**PTKP (Penghasilan Tidak Kena Pajak):** Rp 54.000.000/tahun

### PPN (Pajak Pertambahan Nilai)

Tarif flat **12%** dari Dasar Pengenaan Pajak (DPP).

---

## 7. RabbitMQ Message Contracts

### `pajak.laporan.submitted` (TaxApi → ReportProcessor)

```json
{
  "reportId": "string",
  "taxPayerId": "string",
  "taxPayerNpwp": "string",
  "taxPayerName": "string",
  "reportType": "PPh21 | PPN",
  "period": "string",
  "totalIncome": 0.00,
  "totalTax": 0.00
}
```

### `pajak.laporan.result` (ReportProcessor → TaxApi)

```json
{
  "reportId": "string",
  "taxPayerId": "string",
  "isApproved": true,
  "rejectionReason": "string | null"
}
```

---

## 8. Report Validation Rules (ReportProcessor)

| # | Validasi | Kondisi Reject |
|---|----------|---------------|
| 1 | NPWP | Kosong atau null |
| 2 | Total Penghasilan | ≤ 0 |
| 3 | Total Pajak | Negatif |
| 4 | Sanity Check Pajak | Pajak > 50% penghasilan |
| 5 | Periode | Kosong atau null |

---

## 9. Elasticsearch Indexes

| Index | Deskripsi | Key Fields |
|-------|-----------|-----------|
| `pajak-taxpayers` | Data wajib pajak | id, npwp, name, address, email |
| `pajak-calculations` | Hasil perhitungan pajak | taxPayerId, type, income, tax, createdAt |
| `pajak-reports` | Laporan SPT | taxPayerId, period, status, submittedAt, processedAt |
| `pajak-notifications` | Notifikasi hasil review | taxPayerId, title, message, type, createdAt |

---

## 10. Elastic APM Instrumentation

### TaxApi (`pajak-taxapi`)

Auto-instrumented oleh `Elastic.Apm.NetCoreAll`:
- Semua HTTP request/response (method, URL, status code, duration)
- Outbound HTTP calls ke Elasticsearch

Manual spans ditambahkan di `RabbitMqService.cs`:
- **`RabbitMQ PUBLISH pajak.laporan.submitted`** — span anak dari HTTP transaction saat submit laporan, dengan label `report_id` dan `queue`
- **`RabbitMQ CONSUME pajak.laporan.result`** — transaction baru saat menerima hasil review, dengan child spans:
  - `ES UpdateReport` — update status laporan di Elasticsearch
  - `ES IndexNotification` — simpan notifikasi ke Elasticsearch

### ReportProcessor (`pajak-report-processor`)

Manual transactions via `Elastic.Apm.Extensions.Hosting`:
- **`RabbitMQ CONSUME pajak.laporan.submitted`** — transaction per pesan dengan labels `report_id`, `tax_payer_id`, `period`, `result` (approved/rejected), dan child span:
  - `ValidasiLaporan` — span untuk proses validasi business rules

### Service Map (Elastic APM)

```
pajak-taxapi ──→ rabbitmq/amq.default ──→ pajak-report-processor
     │                                              │
     └──→ elasticsearch-id.es.ap-southeast-1...    │
                                            (publish result back)
```

---

## 11. Docker Setup

### Services

```yaml
services:
  rabbitmq:    # RabbitMQ 3.13-management, port 5672 & 15672
  taxapi:      # TaxApi .NET 9, port 5001
  reportprocessor:  # ReportProcessor .NET 9 Worker
  frontend:    # React + Nginx, port 3000
```

### Run with Docker Compose

```bash
# Hanya RabbitMQ (untuk development lokal)
docker compose -f docker/docker-compose.yml up rabbitmq -d

# Semua services
docker compose -f docker/docker-compose.yml up -d
```

---

## 12. How to Run (Local Development)

### Prerequisites
- .NET 9 SDK
- Node.js 20+
- Docker Desktop

### Steps

```bash
# 1. Start RabbitMQ
docker compose -f docker/docker-compose.yml up rabbitmq -d

# 2. Start TaxApi (Terminal 1)
cd src/TaxApi
dotnet run
# → Swagger: http://localhost:5001/swagger

# 3. Start ReportProcessor (Terminal 2)
cd src/ReportProcessor
dotnet run

# 4. Start Frontend (Terminal 3)
cd frontend
npm install
npm run dev
# → http://localhost:3000

# 5. RabbitMQ Management UI
# → http://localhost:15672 (user: pajak / pajak123)
```

### End-to-End Test Flow
1. POST `/api/taxpayers` — daftar wajib pajak
2. POST `/api/calculations` — hitung PPh21
3. POST `/api/reports` — buat laporan
4. POST `/api/reports/{id}/submit` — submit → tunggu ~2-3 detik
5. GET `/api/reports/{id}` → status: `"Approved"`
6. GET `/api/notifications` → notifikasi muncul

---

## 13. Key Design Decisions

| Keputusan | Pilihan | Alasan |
|-----------|---------|--------|
| Architecture | Flat (no layers) | Fokus pada demonstrasi komunikasi, bukan struktur |
| ORM | Tidak ada (langsung ES client) | Elasticsearch bukan relational DB |
| Message library | RabbitMQ.Client langsung | Menghindari abstraksi MassTransit yang kompleks |
| Frontend ↔ Backend | HTTP polling | Lebih sederhana dari WebSocket/STOMP untuk frontend |
| APM | Manual transactions untuk messaging | Auto-instrumentation tidak menangkap AMQP consumer |

---

## 14. Known Limitations

- Tidak ada authentication/authorization
- Tidak ada rate limiting
- Data wajib pajak tidak terenkripsi di Elasticsearch
- Frontend menggunakan polling (bukan WebSocket/SSE) untuk update real-time
- Tidak ada unit/integration tests
