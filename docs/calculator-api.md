# CalculatorApi — Tax Calculation & Validation Service

CalculatorApi is a .NET 9 Background Service that acts as the centralized tax calculation engine for the application. It communicates exclusively via **AMQP RPC over RabbitMQ** — it has no HTTP API surface (except a `/health` endpoint for Docker health checks).

## Role in the Architecture

```
TaxApi ──────── pajak.calculate.request ──────► CalculatorApi
       ◄─────── pajak.calculate.reply.taxapi ───

ReportProcessor ─── pajak.validate.request ───► CalculatorApi
                ◄─── (reply-to queue) ──────────
```

Both TaxApi and ReportProcessor send calculation/validation requests as **AMQP RPC calls**: they publish a message to a durable request queue with a `CorrelationId` and `ReplyTo` header, then wait up to **10 seconds** for a reply on their exclusive reply queue.

## Queues

| Queue | Durable | Purpose |
|-------|---------|---------|
| `pajak.calculate.request` | Yes | Tax calculation requests (6 types) |
| `pajak.validate.request` | Yes | Report validation requests from ReportProcessor |
| `pajak.calculate.reply.taxapi` | No (exclusive, auto-delete) | Replies to TaxApi RPC calls |

> ReportProcessor uses an anonymous server-generated reply queue (`ReplyTo` in the message properties).

## RPC Message Pattern

### Request (publisher → CalculatorApi)

```
BasicProperties:
  CorrelationId  = <guid>           // identifies this specific RPC call
  ReplyTo        = <reply-queue>    // queue where CalculatorApi sends the result
  Headers:
    elastic-apm-traceparent = <W3C traceparent bytes>  // distributed trace propagation
```

For `pajak.calculate.request`, the body is a JSON envelope:

```json
{
  "CalculationType": "Pph21",
  "PayloadJson": "{...}"
}
```

`CalculationType` must be one of: `Pph21`, `Pph21Thr`, `Pph21Desember`, `Pph23`, `Ppn`, `PphFinal`.

`PayloadJson` is the JSON-serialized request model for the given type (see below).

For `pajak.validate.request`, the body is a `ValidasiLaporanRequest` object directly (no envelope).

### Reply (CalculatorApi → publisher)

For calculation requests, the reply is a `CalculateReply` envelope:

```json
{
  "Success": true,
  "ResultJson": "{...}",
  "ErrorMessage": null
}
```

If `Success` is `false`, `ErrorMessage` contains the error and the publisher throws an exception.

For validation requests, the reply is a `ValidasiLaporanResponse` object directly.

---

## Tax Calculation Types

### 1. PPh 21 — Monthly Routine (`Pph21`)

Calculates monthly income tax for employees (January–November) using the TER (Tarif Efektif Rata-rata) method per PMK 168/2023.

**Request:**
```json
{
  "GajiPokok": 10000000,
  "TunjanganTetap": 2000000,
  "TunjanganTidakTetap": 500000,
  "JhtEmployee": 200000,
  "JpEmployee": 100000,
  "PtkpStatus": "TK0",
  "Bulan": 3,
  "NpwpPemotong": "01.234.567.8-000.000"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `GajiPokok` | decimal | Basic salary |
| `TunjanganTetap` | decimal | Fixed allowances |
| `TunjanganTidakTetap` | decimal | Non-fixed allowances |
| `JhtEmployee` | decimal | JHT employee contribution (nominal, not %) |
| `JpEmployee` | decimal | JP employee contribution (nominal, not %) |
| `PtkpStatus` | enum | PTKP status: `TK0`/`TK1`/`TK2`/`TK3`/`K0`/`K1`/`K2`/`K3` |
| `Bulan` | int | Month number (1–12; use `Pph21Desember` for December) |
| `NpwpPemotong` | string? | NPWP of the withholding party (optional) |

**Response:** `Pph21Response` — includes `GajiBruto`, `BiayaJabatan`, `PenghasilanNeto`, `PtkpBulanan`, `PkpBulanan`, `TerKategori` (A/B/C), `TerRate`, `PphBulanan`, `RegulasiAcuan`, and a full `Breakdown` object.

---

### 2. PPh 21 THR/Bonus (`Pph21Thr`)

Calculates withholding tax on THR (Tunjangan Hari Raya) or bonus payments using the annualization method.

**Request:**
```json
{
  "GajiPokokBulanan": 10000000,
  "TunjanganTetapBulanan": 2000000,
  "JumlahThrAtauBonus": 12000000,
  "TotalGajiBrutoJanSdBulanIni": 36000000,
  "TotalPphRutinJanSdBulanIni": 450000,
  "PtkpStatus": "K1",
  "JhtEmployeeBulanan": 200000,
  "JpEmployeeBulanan": 100000,
  "NpwpPemotong": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `TotalGajiBrutoJanSdBulanIni` | decimal | Cumulative gross salary Jan–current month (excl. THR) |
| `TotalPphRutinJanSdBulanIni` | decimal | Cumulative PPh already withheld Jan–current month |

**Response:** `Pph21ThrResponse` — includes `PphAtasGajiDanThr`, `PphAtasGajiSaja`, `PphTerutangThr`, `RegulasiAcuan`.

---

### 3. PPh 21 December (`Pph21Desember`)

Year-end correction calculation. Computes annual PPh then subtracts taxes already withheld Jan–November.

**Request:**
```json
{
  "GajiPokok": 10000000,
  "TunjanganTetap": 2000000,
  "TunjanganTidakTetap": 0,
  "JhtEmployee": 200000,
  "JpEmployee": 100000,
  "PtkpStatus": "K2",
  "TotalGajiBrutoJanNov": 110000000,
  "TotalPphJanNov": 4500000,
  "NpwpPemotong": null
}
```

**Response:** `Pph21DesemberResponse` — includes `TotalGajiBrutoSetahun`, `PphSetahun`, `TotalPphJanNov`, `PphDesember`, progressive tax breakdown.

---

### 4. PPh 23 (`Pph23`)

Calculates withholding tax on services, dividends, interest, royalties, and non-land/building rental income.

**Request:**
```json
{
  "JumlahBruto": 5000000,
  "JenisPenghasilan": "Jasa",
  "HasNpwp": true,
  "NpwpPenerima": "02.345.678.9-000.000"
}
```

| Field | `JenisPenghasilan` values |
|-------|--------------------------|
| Services | `Jasa` |
| Dividends | `Dividen` |
| Interest | `Bunga` |
| Royalties | `Royalti` |
| Non-land rental | `SewaSelainTanahBangunan` |

> If `HasNpwp` is `false`, the effective rate is **doubled** (200% of normal rate).

**Response:** `Pph23Response` — includes `TarifNormal`, `TarifEfektif`, `TarifDilipatkan`, `PphDipotong`, `JumlahDiterima`.

---

### 5. PPN — Value Added Tax (`Ppn`)

Calculates PPN at the current rate of **12%** per UU HPP.

**Request:**
```json
{
  "Jumlah": 1000000,
  "IsInclusive": false
}
```

| Field | Description |
|-------|-------------|
| `Jumlah` | Transaction amount |
| `IsInclusive` | `true` = amount already includes PPN (tax-inclusive); `false` = amount is DPP (tax-exclusive) |

**Response:** `PpnResponse` — includes `Dpp`, `Ppn`, `Total`, `TarifPpn` (12%), `RegulasiAcuan`.

---

### 6. PPh Final UMKM (`PphFinal`)

Calculates final income tax for UMKM (SMEs) at **0.5% of monthly revenue** per PP 55/2022 (as amended).

**Request:**
```json
{
  "OmzetBulanIni": 30000000,
  "OmzetTahunBerjalan": 200000000,
  "NpwpWp": "03.456.789.0-000.000"
}
```

| Field | Description |
|-------|-------------|
| `OmzetBulanIni` | Revenue for current month |
| `OmzetTahunBerjalan` | Cumulative revenue Jan–previous month |
| `NpwpWp` | NPWP of the taxpayer |

> The facility applies as long as cumulative annual revenue stays below IDR 4.8 billion. `MasihBerhakFasilitas` in the response indicates eligibility for the current month.

**Response:** `PphFinalUmkmResponse` — includes `Tarif` (0.5%), `PphFinal`, `MasihBerhakFasilitas`, `OmzetSetelahBulanIni`.

---

## Report Validation (`pajak.validate.request`)

Used by ReportProcessor after consuming a submitted report from `pajak.laporan.submitted`.

**Request (`ValidasiLaporanRequest`):**
```json
{
  "JenisSpt": "SPT Masa PPh21",
  "TotalIncome": 120000000,
  "TotalTax": 1500000,
  "PtkpStatus": "TK0",
  "Period": "2025-03",
  "AdditionalJson": null
}
```

| `JenisSpt` values |
|-------------------|
| `SPT Tahunan OP` |
| `SPT Tahunan Badan` |
| `SPT Masa PPh21` |
| `SPT Masa PPh23` |
| `SPT Masa PPN` |
| `SPT Final UMKM` |

**Response (`ValidasiLaporanResponse`):**
```json
{
  "IsValid": true,
  "Reason": "Validasi berhasil",
  "ExpectedTaxMin": 1200000,
  "ExpectedTaxMax": 2000000,
  "ActualTax": 1500000,
  "Suggestion": null
}
```

If `IsValid` is `false`, `Reason` explains why and `Suggestion` may provide guidance.

### Fallback Validation in ReportProcessor

If CalculatorApi is unavailable (RPC timeout or connection failure), ReportProcessor falls back to a **basic local validation**:

- NPWP must not be empty
- `TotalIncome` must be > 0
- `TotalTax` must be >= 0

This ensures the report submission flow does not block indefinitely when CalculatorApi is down. The fallback result is marked as valid if these basic checks pass.

---

## Distributed Tracing

CalculatorApi participates in the distributed trace initiated by TaxApi or ReportProcessor. The `elastic-apm-traceparent` header is read from incoming AMQP message headers and used to create a **child transaction**, keeping the full calculation flow visible as a single trace in APM UI.

```csharp
// CalculatorConsumerService.cs
var distributedTracingData = traceParent is not null
    ? DistributedTracingData.TryDeserializeFromString(traceParent)
    : null;

var transaction = distributedTracingData is not null
    ? Agent.Tracer.StartTransaction("pajak.calculate.request", ApiConstants.TypeMessaging, distributedTracingData)
    : Agent.Tracer.StartTransaction("pajak.calculate.request", ApiConstants.TypeMessaging);
```

For more details on end-to-end trace propagation, see [instrumentasi-elastic-apm.md](instrumentasi-elastic-apm.md).

---

## APM Service Name

| Environment | Service Name |
|-------------|-------------|
| Local dev | `pajak-calculator` (from `appsettings.json`) |
| Docker | `pajak-calculator` (from `docker-compose.yml` env var) |
