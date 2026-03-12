import { useState, useEffect } from 'react';
import { notificationApi, formatDate } from '../services/api';
import type { Notification } from '../services/api';
import HelpPanel from '../components/HelpPanel';

const HELP_NOTES = [
  { icon: '🤖', text: 'Notifikasi dibuat otomatis oleh ReportProcessor via RabbitMQ — bukan oleh TaxApi secara langsung.' },
  { icon: '🔄', text: 'Halaman ini auto-refresh setiap 5 detik. Gunakan tombol Refresh untuk memuat secara manual.' },
  { icon: '✅', text: 'Notifikasi hijau = laporan disetujui. Notifikasi merah = laporan ditolak beserta alasannya.' },
  { icon: '🔗', text: 'Untuk melihat trace lengkap alur notifikasi, buka Elastic APM → Traces dan cari transaksi "POST /api/reports/{id}/submit".' },
];

const TYPE_STYLES: Record<string, { bg: string; icon: string }> = {
  Success: { bg: '#dcfce7', icon: '✅' },
  Error: { bg: '#fee2e2', icon: '❌' },
  Info: { bg: '#dbeafe', icon: 'ℹ️' },
  Warning: { bg: '#fef9c3', icon: '⚠️' },
};

export default function NotificationsPage() {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const res = await notificationApi.getAll();
      setNotifications(res.data);
      setError('');
    } catch {
      setError('Gagal memuat notifikasi');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    const timer = setInterval(load, 5000); // Auto-refresh tiap 5 detik
    return () => clearInterval(timer);
  }, []);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <h2 style={{ margin: 0 }}>Notifikasi</h2>
        <button onClick={load} className="btn-secondary" style={{ fontSize: 13 }}>🔄 Refresh</button>
      </div>

      <HelpPanel
        title="Tentang halaman Notifikasi:"
        notes={HELP_NOTES}
      />

      {error && <div className="alert-error">{error}</div>}

      {loading ? (
        <div className="loading">Memuat...</div>
      ) : notifications.length === 0 ? (
        <div className="card" style={{ textAlign: 'center', color: '#888', padding: 48 }}>
          Belum ada notifikasi. Submit laporan SPT untuk melihat notifikasi.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {notifications.map(n => {
            const style = TYPE_STYLES[n.type] || TYPE_STYLES.Info;
            return (
              <div
                key={n.id}
                className="card"
                style={{ background: style.bg, borderLeft: `4px solid currentColor` }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                  <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
                    <span style={{ fontSize: 20 }}>{style.icon}</span>
                    <div>
                      <div style={{ fontWeight: 600, marginBottom: 4 }}>{n.title}</div>
                      <div style={{ color: '#444' }}>{n.message}</div>
                    </div>
                  </div>
                  <div style={{ fontSize: 12, color: '#888', whiteSpace: 'nowrap', marginLeft: 16 }}>
                    {formatDate(n.createdAt)}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
