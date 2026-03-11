# Aplikasi Pajak Sederhana

Aplikasi pelaporan pajak sederhana dengan arsitektur microservices.

## Arsitektur

```
Frontend (React + TS) :3000 → HTTP → TaxApi (.NET 9) :5001
                                         ↕ RabbitMQ (publish/consume)
                                    ReportProcessor (Worker)
                                         ↕
                                   Elasticsearch (Cloud)
```

## Cara Menjalankan

### Development (tanpa Docker)

**1. Jalankan RabbitMQ saja:**
```bash
cd docker
docker compose up rabbitmq -d
```

**2. Jalankan TaxApi:**
```bash
cd src/TaxApi
dotnet run
# Swagger: http://localhost:5001/swagger
```

**3. Jalankan ReportProcessor:**
```bash
cd src/ReportProcessor
dotnet run
```

**4. Jalankan Frontend:**
```bash
cd frontend
npm run dev
# UI: http://localhost:5173
```

### Production (Docker penuh)

```bash
cd docker
docker compose up -d
```

- Frontend: http://localhost:3000
- TaxApi Swagger: http://localhost:5001/swagger
- RabbitMQ UI: http://localhost:15672 (user: pajak / pajak123)

## Flow Back-and-Forth RabbitMQ

1. User submit laporan → TaxApi publish `pajak.laporan.submitted`
2. ReportProcessor consume → validasi → publish `pajak.laporan.result`
3. TaxApi consume result → update status + buat notifikasi
4. Frontend auto-refresh → tampilkan status terbaru

## Stack

- **Frontend**: React 18 + TypeScript + Vite
- **Backend**: .NET 9 Minimal API + Worker Service
- **Messaging**: RabbitMQ (2 queues, 2 arah)
- **Database**: Elasticsearch Cloud
