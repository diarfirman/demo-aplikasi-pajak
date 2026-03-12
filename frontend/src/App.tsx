import { BrowserRouter, Route, NavLink } from 'react-router-dom';
import { ApmRoutes } from '@elastic/apm-rum-react';
import TaxPayersPage from './pages/TaxPayersPage';
import CalculationsPage from './pages/CalculationsPage';
import ReportsPage from './pages/ReportsPage';
import NotificationsPage from './pages/NotificationsPage';
import './App.css';

function App() {
  return (
    <BrowserRouter>
      <div className="layout">
        <nav className="sidebar">
          <div className="sidebar-header">
            <h1>🏛️ Aplikasi Pajak</h1>
            <p>Pelaporan Pajak Sederhana</p>
          </div>
          <ul className="nav-list">
            <li><NavLink to="/" end className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>👤 Wajib Pajak</NavLink></li>
            <li><NavLink to="/calculations" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>🧮 Perhitungan</NavLink></li>
            <li><NavLink to="/reports" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>📋 Laporan SPT</NavLink></li>
            <li><NavLink to="/notifications" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>🔔 Notifikasi</NavLink></li>
          </ul>
          <div className="sidebar-footer">
            <div className="tech-badge">React + .NET 9</div>
            <div className="tech-badge">RabbitMQ ↔ Elasticsearch</div>
          </div>
        </nav>
        <main className="main-content">
          <ApmRoutes>
            <Route path="/" element={<TaxPayersPage />} />
            <Route path="/calculations" element={<CalculationsPage />} />
            <Route path="/reports" element={<ReportsPage />} />
            <Route path="/notifications" element={<NotificationsPage />} />
          </ApmRoutes>
        </main>
      </div>
    </BrowserRouter>
  );
}

export default App;
