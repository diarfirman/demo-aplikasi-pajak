import { useState, useEffect } from 'react';
import { taxPayerApi, formatDate } from '../services/api';
import type { TaxPayer } from '../services/api';

export default function TaxPayersPage() {
  const [taxpayers, setTaxpayers] = useState<TaxPayer[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({
    npwp: '', name: '', type: 'OP', email: '', phone: '', address: ''
  });
  const [submitting, setSubmitting] = useState(false);

  const load = async () => {
    try {
      const res = await taxPayerApi.getAll();
      setTaxpayers(res.data);
      setError('');
    } catch {
      setError('Gagal memuat data wajib pajak');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await taxPayerApi.register({
        npwp: form.npwp, name: form.name, type: form.type,
        email: form.email, phone: form.phone || undefined,
        address: form.address || undefined,
      });
      setShowForm(false);
      setForm({ npwp: '', name: '', type: 'OP', email: '', phone: '', address: '' });
      await load();
    } catch (err: any) {
      setError(err.response?.data?.error || 'Gagal mendaftarkan wajib pajak');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <h2 style={{ margin: 0 }}>Wajib Pajak</h2>
        <button onClick={() => setShowForm(!showForm)} className="btn-primary">
          {showForm ? 'Tutup' : '+ Daftar Baru'}
        </button>
      </div>

      {error && <div className="alert-error">{error}</div>}

      {showForm && (
        <div className="card" style={{ marginBottom: 24 }}>
          <h3>Daftar Wajib Pajak Baru</h3>
          <form onSubmit={handleSubmit}>
            <div className="form-grid">
              <div className="form-group">
                <label>NPWP *</label>
                <input
                  required placeholder="00.000.000.0-000.000"
                  value={form.npwp}
                  onChange={e => setForm({ ...form, npwp: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Nama *</label>
                <input
                  required placeholder="Nama Wajib Pajak"
                  value={form.name}
                  onChange={e => setForm({ ...form, name: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Jenis *</label>
                <select value={form.type} onChange={e => setForm({ ...form, type: e.target.value })}>
                  <option value="OP">Orang Pribadi</option>
                  <option value="Badan">Badan Usaha</option>
                </select>
              </div>
              <div className="form-group">
                <label>Email *</label>
                <input
                  required type="email" placeholder="email@domain.com"
                  value={form.email}
                  onChange={e => setForm({ ...form, email: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Telepon</label>
                <input
                  placeholder="08xx-xxxx-xxxx"
                  value={form.phone}
                  onChange={e => setForm({ ...form, phone: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Alamat</label>
                <input
                  placeholder="Alamat lengkap"
                  value={form.address}
                  onChange={e => setForm({ ...form, address: e.target.value })}
                />
              </div>
            </div>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Mendaftarkan...' : 'Daftarkan'}
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
                <th>NPWP</th>
                <th>Nama</th>
                <th>Jenis</th>
                <th>Email</th>
                <th>Telepon</th>
                <th>Terdaftar</th>
              </tr>
            </thead>
            <tbody>
              {taxpayers.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: '#888' }}>Belum ada wajib pajak</td></tr>
              ) : (
                taxpayers.map(tp => (
                  <tr key={tp.id}>
                    <td><code>{tp.npwp}</code></td>
                    <td>{tp.name}</td>
                    <td><span className={`badge ${tp.type === 'OP' ? 'badge-blue' : 'badge-purple'}`}>{tp.type === 'OP' ? 'Orang Pribadi' : 'Badan'}</span></td>
                    <td>{tp.email}</td>
                    <td>{tp.phone || '-'}</td>
                    <td>{formatDate(tp.createdAt)}</td>
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
