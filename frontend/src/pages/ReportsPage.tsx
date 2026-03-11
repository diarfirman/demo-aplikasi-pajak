import { useState, useEffect, useCallback } from 'react';
import { reportApi, taxPayerApi, formatRupiah } from '../services/api';
import type { TaxReport, TaxPayer } from '../services/api';

const STATUS_COLORS: Record<string, string> = {
  Draft: 'badge-gray',
  Submitted: 'badge-yellow',
  Approved: 'badge-green',
  Rejected: 'badge-red',
};

const STATUS_LABELS: Record<string, string> = {
  Draft: 'Draft',
  Submitted: 'Menunggu Review',
  Approved: 'Disetujui',
  Rejected: 'Ditolak',
};

export default function ReportsPage() {
  const [reports, setReports] = useState<TaxReport[]>([]);
  const [taxpayers, setTaxpayers] = useState<TaxPayer[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [submitting, setSubmitting] = useState<string | null>(null);
  const [pollingIds, setPollingIds] = useState<Set<string>>(new Set());

  const [form, setForm] = useState({
    taxPayerId: '', reportType: 'SPT Tahunan', period: '', totalIncome: '', totalTax: ''
  });

  const load = useCallback(async () => {
    try {
      const [repRes, tpRes] = await Promise.all([
        reportApi.getAll(),
        taxPayerApi.getAll(),
      ]);
      setReports(repRes.data);
      setTaxpayers(tpRes.data);
    } catch {
      setError('Gagal memuat data');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Auto-refresh tiap 3 detik jika ada laporan yang masih "Submitted"
  useEffect(() => {
    const submitted = reports.filter(r => r.status === 'Submitted');
    if (submitted.length === 0) return;

    const newIds = new Set(submitted.map(r => r.id));
    setPollingIds(newIds);

    const timer = setTimeout(() => { load(); }, 3000);
    return () => clearTimeout(timer);
  }, [reports, load]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting('create');
    try {
      await reportApi.create({
        taxPayerId: form.taxPayerId,
        reportType: form.reportType,
        period: form.period,
        totalIncome: parseFloat(form.totalIncome),
        totalTax: parseFloat(form.totalTax),
      });
      setShowForm(false);
      setForm({ taxPayerId: '', reportType: 'SPT Tahunan', period: '', totalIncome: '', totalTax: '' });
      await load();
    } catch (err: any) {
      setError(err.response?.data?.error || 'Gagal membuat laporan');
    } finally {
      setSubmitting(null);
    }
  };

  const handleSubmit = async (id: string) => {
    setSubmitting(id);
    try {
      await reportApi.submit(id);
      await load();
    } catch (err: any) {
      setError(err.response?.data?.error || 'Gagal submit laporan');
    } finally {
      setSubmitting(null);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <h2 style={{ margin: 0 }}>Laporan SPT</h2>
        <button onClick={() => setShowForm(!showForm)} className="btn-primary">
          {showForm ? 'Tutup' : '+ Buat Laporan'}
        </button>
      </div>

      {pollingIds.size > 0 && (
        <div className="alert-info">
          ⏳ {pollingIds.size} laporan sedang dalam proses review via RabbitMQ... (auto-refresh setiap 3 detik)
        </div>
      )}

      {error && <div className="alert-error">{error}</div>}

      {showForm && (
        <div className="card" style={{ marginBottom: 24 }}>
          <h3>Buat Laporan SPT Baru</h3>
          <form onSubmit={handleCreate}>
            <div className="form-grid">
              <div className="form-group">
                <label>Wajib Pajak *</label>
                <select required value={form.taxPayerId} onChange={e => setForm({ ...form, taxPayerId: e.target.value })}>
                  <option value="">-- Pilih Wajib Pajak --</option>
                  {taxpayers.map(tp => <option key={tp.id} value={tp.id}>{tp.name} ({tp.npwp})</option>)}
                </select>
              </div>
              <div className="form-group">
                <label>Jenis Laporan *</label>
                <select value={form.reportType} onChange={e => setForm({ ...form, reportType: e.target.value })}>
                  <option value="SPT Tahunan">SPT Tahunan</option>
                  <option value="SPT Masa PPh">SPT Masa PPh</option>
                  <option value="SPT Masa PPN">SPT Masa PPN</option>
                </select>
              </div>
              <div className="form-group">
                <label>Periode *</label>
                <input
                  required placeholder="2024 atau 2025-01"
                  value={form.period}
                  onChange={e => setForm({ ...form, period: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Total Penghasilan (Rp) *</label>
                <input
                  required type="number" min="0" placeholder="120000000"
                  value={form.totalIncome}
                  onChange={e => setForm({ ...form, totalIncome: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Total Pajak Terutang (Rp) *</label>
                <input
                  required type="number" min="0" placeholder="5000000"
                  value={form.totalTax}
                  onChange={e => setForm({ ...form, totalTax: e.target.value })}
                />
              </div>
            </div>
            <button type="submit" className="btn-primary" disabled={submitting === 'create'}>
              {submitting === 'create' ? 'Membuat...' : 'Buat Laporan'}
            </button>
          </form>
        </div>
      )}

      {loading ? (
        <div className="loading">Memuat data...</div>
      ) : (
        <div className="card">
          <table className="table">
            <thead>
              <tr>
                <th>Wajib Pajak</th>
                <th>Jenis</th>
                <th>Periode</th>
                <th>Total Penghasilan</th>
                <th>Total Pajak</th>
                <th>Status</th>
                <th>Aksi</th>
              </tr>
            </thead>
            <tbody>
              {reports.length === 0 ? (
                <tr><td colSpan={7} style={{ textAlign: 'center', color: '#888' }}>Belum ada laporan</td></tr>
              ) : (
                reports.map(r => (
                  <tr key={r.id}>
                    <td>
                      <div>{r.taxPayerName}</div>
                      <div style={{ fontSize: 12, color: '#888' }}>{r.taxPayerNpwp}</div>
                    </td>
                    <td>{r.reportType}</td>
                    <td>{r.period}</td>
                    <td>{formatRupiah(r.totalIncome)}</td>
                    <td>{formatRupiah(r.totalTax)}</td>
                    <td>
                      <span className={`badge ${STATUS_COLORS[r.status] || 'badge-gray'}`}>
                        {STATUS_LABELS[r.status] || r.status}
                      </span>
                      {r.status === 'Rejected' && r.rejectionReason && (
                        <div style={{ fontSize: 11, color: '#dc2626', marginTop: 4 }}>{r.rejectionReason}</div>
                      )}
                    </td>
                    <td>
                      {r.status === 'Draft' && (
                        <button
                          className="btn-small"
                          onClick={() => handleSubmit(r.id)}
                          disabled={submitting === r.id}
                        >
                          {submitting === r.id ? 'Submitting...' : 'Submit'}
                        </button>
                      )}
                      {r.status === 'Submitted' && (
                        <span style={{ fontSize: 12, color: '#d97706' }}>⏳ Review...</span>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
