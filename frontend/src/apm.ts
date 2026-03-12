import { init as apmInit } from '@elastic/apm-rum';
import type { AgentConfigOptions } from '@elastic/apm-rum';

/**
 * Elastic APM RUM Agent — inisialisasi harus dilakukan sebelum import lain.
 *
 * Fitur yang aktif:
 * - Page load & route change transactions (via ApmRoutes di App.tsx)
 * - HTTP request spans (Axios ke TaxApi) dengan traceparent header otomatis
 * - Distributed tracing: setiap request ke TaxApi membawa traceparent,
 *   sehingga trace browser → TaxApi → RabbitMQ → ReportProcessor → TaxApi terhubung
 */

// secretToken tidak ada di TypeScript types versi ini (oversight pada package),
// tapi diterima oleh runtime agent
const config: AgentConfigOptions & { secretToken?: string } = {
  serviceName: 'pajak-frontend',
  serverUrl: import.meta.env.VITE_ELASTIC_APM_SERVER_URL as string,
  secretToken: import.meta.env.VITE_ELASTIC_APM_SECRET_TOKEN as string,
  environment: 'development',

  // Izinkan inject traceparent ke request lintas origin ke TaxApi
  distributedTracingOrigins: [
    import.meta.env.VITE_API_URL as string ?? 'http://localhost:5001',
  ],

  // Nonaktifkan jika env var tidak diset (misal: clone repo baru tanpa .env.local)
  active: !!import.meta.env.VITE_ELASTIC_APM_SERVER_URL,
};

export const apm = apmInit(config);
