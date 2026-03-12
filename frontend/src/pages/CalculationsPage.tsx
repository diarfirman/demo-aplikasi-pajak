import { useState, useEffect } from 'react';
import { calculationApi, taxPayerApi, formatRupiah, formatDate } from '../services/api';
import type { TaxCalculation, TaxPayer } from '../services/api';
import HelpPanel from '../components/HelpPanel';

const HELP_PPH_STEPS = [
  { step: 1, title: 'Pilih tab "PPh Pasal 21"', desc: '— untuk hitung pajak penghasilan karyawan.' },
  { step: 2, title: 'Pilih Wajib Pajak', desc: '— harus sudah terdaftar di halaman Wajib Pajak.' },
  { step: 3, title: 'Pilih Status Kawin', desc: '— menentukan PTKP: TK/0 = Rp 54 jt, K/0 = Rp 58,5 jt, K/1 = Rp 63 jt, dst.' },
  { step: 4, title: 'Isi Penghasilan Bruto/Bulan', desc: '— disetahunkan otomatis. Contoh: Rp 10.000.000.' },
  { step: 5, title: 'Isi Periode', desc: '— format: YYYY-MM. Contoh: 2025-01 untuk Januari 2025.' },
  { step: 6, title: 'Klik "Hitung PPh 21"', desc: '— hasil tampil di bawah form dan tersimpan ke riwayat.' },
];

const HELP_PPH_NOTES = [
  { icon: '📊', text: 'Tarif progresif: 5% (s.d. Rp 60 jt) · 15% (Rp 60–250 jt) · 25% (Rp 250–500 jt) · 30% (Rp 500 jt–5 M) · 35% (di atas Rp 5 M).' },
  { icon: '💡', text: 'PPN 12%: masukkan nilai transaksi (DPP), sistem hitung PPN = DPP × 12%.' },
];

export default function CalculationsPage() {
  const [calculations, setCalculations] = useState<TaxCalculation[]>([]);
  const [taxpayers, setTaxpayers] = useState<TaxPayer[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [taxType, setTaxType] = useState<'PPh21' | 'PPN'>('PPh21');
  const [result, setResult] = useState<TaxCalculation | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [formPph, setFormPph] = useState({
    taxPayerId: '', grossIncome: '', maritalStatus: 'TK0', period: ''
  });
  const [formPpn, setFormPpn] = useState({
    taxPayerId: '', amount: '', period: ''
  });

  const load = async () => {
    try {
      const [calcRes, tpRes] = await Promise.all([
        calculationApi.getAll(),
        taxPayerApi.getAll(),
      ]);
      setCalculations(calcRes.data);
      setTaxpayers(tpRes.data);
    } catch {
      setError('Gagal memuat data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const handlePPh21 = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setResult(null);
    try {
      const res = await calculationApi.calculatePPh21({
        taxPayerId: formPph.taxPayerId,
        grossIncome: parseFloat(formPph.grossIncome),
        maritalStatus: formPph.maritalStatus,
        period: formPph.period,
      });
      setResult(res.data);
      await load();
    } catch (err: any) {
      setError(err.response?.data?.error || 'Gagal menghitung pajak');
    } finally {
      setSubmitting(false);
    }
  };

  const handlePPN = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setResult(null);
    try {
      const res = await calculationApi.calculatePPN({
        taxPayerId: formPpn.taxPayerId,
        amount: parseFloat(formPpn.amount),
        period: formPpn.period,
      });
      setResult(res.data);
      await load();
    } catch (err: any) {
      setError(err.response?.data?.error || 'Gagal menghitung pajak');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div>
      <h2 style={{ marginBottom: 16 }}>Perhitungan Pajak</h2>

      <HelpPanel
        title="Cara menghitung pajak:"
        steps={HELP_PPH_STEPS}
        notes={HELP_PPH_NOTES}
      />

      {error && <div className="alert-error">{error}</div>}

      <div className="card" style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', gap: 16, marginBottom: 20 }}>
          <button
            className={taxType === 'PPh21' ? 'btn-primary' : 'btn-secondary'}
            onClick={() => setTaxType('PPh21')}
          >PPh Pasal 21</button>
          <button
            className={taxType === 'PPN' ? 'btn-primary' : 'btn-secondary'}
            onClick={() => setTaxType('PPN')}
          >PPN 12%</button>
        </div>

        {taxType === 'PPh21' && (
          <form onSubmit={handlePPh21}>
            <h3>Hitung PPh Pasal 21 (Karyawan)</h3>
            <div className="form-grid">
              <div className="form-group">
                <label>Wajib Pajak *</label>
                <select required value={formPph.taxPayerId} onChange={e => setFormPph({ ...formPph, taxPayerId: e.target.value })}>
                  <option value="">-- Pilih Wajib Pajak --</option>
                  {taxpayers.map(tp => <option key={tp.id} value={tp.id}>{tp.name} ({tp.npwp})</option>)}
                </select>
              </div>
              <div className="form-group">
                <label>Status Kawin *</label>
                <select value={formPph.maritalStatus} onChange={e => setFormPph({ ...formPph, maritalStatus: e.target.value })}>
                  <option value="TK0">TK/0 - Tidak Kawin</option>
                  <option value="K0">K/0 - Kawin, 0 tanggungan</option>
                  <option value="K1">K/1 - Kawin, 1 tanggungan</option>
                  <option value="K2">K/2 - Kawin, 2 tanggungan</option>
                  <option value="K3">K/3 - Kawin, 3 tanggungan</option>
                </select>
              </div>
              <div className="form-group">
                <label>Penghasilan Bruto/Bulan (Rp) *</label>
                <input
                  required type="number" min="0" placeholder="10000000"
                  value={formPph.grossIncome}
                  onChange={e => setFormPph({ ...formPph, grossIncome: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Periode *</label>
                <input
                  required placeholder="2025-01"
                  value={formPph.period}
                  onChange={e => setFormPph({ ...formPph, period: e.target.value })}
                />
              </div>
            </div>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Menghitung...' : 'Hitung PPh 21'}
            </button>
          </form>
        )}

        {taxType === 'PPN' && (
          <form onSubmit={handlePPN}>
            <h3>Hitung PPN 12%</h3>
            <div className="form-grid">
              <div className="form-group">
                <label>Wajib Pajak *</label>
                <select required value={formPpn.taxPayerId} onChange={e => setFormPpn({ ...formPpn, taxPayerId: e.target.value })}>
                  <option value="">-- Pilih Wajib Pajak --</option>
                  {taxpayers.map(tp => <option key={tp.id} value={tp.id}>{tp.name} ({tp.npwp})</option>)}
                </select>
              </div>
              <div className="form-group">
                <label>Nilai Transaksi (Rp) *</label>
                <input
                  required type="number" min="0" placeholder="100000000"
                  value={formPpn.amount}
                  onChange={e => setFormPpn({ ...formPpn, amount: e.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Periode *</label>
                <input
                  required placeholder="2025-01"
                  value={formPpn.period}
                  onChange={e => setFormPpn({ ...formPpn, period: e.target.value })}
                />
              </div>
            </div>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Menghitung...' : 'Hitung PPN'}
            </button>
          </form>
        )}

        {result && (
          <div className="result-box">
            <h4>Hasil Perhitungan</h4>
            <div className="result-grid">
              <div><span>Nilai Dasar</span><strong>{formatRupiah(result.grossAmount)}</strong></div>
              <div><span>Pajak ({result.taxType})</span><strong style={{ color: '#dc2626' }}>{formatRupiah(result.taxAmount)}</strong></div>
              <div><span>Nilai Bersih</span><strong style={{ color: '#16a34a' }}>{formatRupiah(result.netAmount)}</strong></div>
            </div>
            {result.notes && <p style={{ marginTop: 8, color: '#666', fontSize: 13 }}>{result.notes}</p>}
          </div>
        )}
      </div>

      <div className="card">
        <h3>Riwayat Perhitungan</h3>
        {loading ? <div className="loading">Memuat...</div> : (
          <table className="table">
            <thead>
              <tr><th>Wajib Pajak</th><th>Jenis</th><th>Penghasilan/Nilai</th><th>Pajak</th><th>Bersih</th><th>Periode</th><th>Tanggal</th></tr>
            </thead>
            <tbody>
              {calculations.length === 0 ? (
                <tr><td colSpan={7} style={{ textAlign: 'center', color: '#888' }}>Belum ada perhitungan</td></tr>
              ) : (
                calculations.map(c => (
                  <tr key={c.id}>
                    <td>{c.taxPayerName}</td>
                    <td><span className="badge badge-blue">{c.taxType}</span></td>
                    <td>{formatRupiah(c.grossAmount)}</td>
                    <td style={{ color: '#dc2626' }}>{formatRupiah(c.taxAmount)}</td>
                    <td style={{ color: '#16a34a' }}>{formatRupiah(c.netAmount)}</td>
                    <td>{c.period}</td>
                    <td>{formatDate(c.calculatedAt)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
