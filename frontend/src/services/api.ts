import axios from 'axios';

const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:5001';

const api = axios.create({ baseURL: API_BASE });

// Types
export interface TaxPayer {
  id: string;
  npwp: string;
  name: string;
  type: string;
  email: string;
  phone?: string;
  address?: string;
  createdAt: string;
}

export interface TaxCalculation {
  id: string;
  taxPayerId: string;
  taxPayerName: string;
  taxType: string;
  grossAmount: number;
  taxAmount: number;
  netAmount: number;
  period: string;
  notes?: string;
  calculatedAt: string;
}

export interface TaxReport {
  id: string;
  taxPayerId: string;
  taxPayerName: string;
  taxPayerNpwp: string;
  reportType: string;
  period: string;
  totalIncome: number;
  totalTax: number;
  status: string;
  rejectionReason?: string;
  createdAt: string;
  submittedAt?: string;
  processedAt?: string;
}

export interface Notification {
  id: string;
  taxPayerId: string;
  title: string;
  message: string;
  type: string;
  isRead: boolean;
  createdAt: string;
}

// TaxPayer API
export const taxPayerApi = {
  getAll: () => api.get<TaxPayer[]>('/api/taxpayers'),
  getById: (id: string) => api.get<TaxPayer>(`/api/taxpayers/${id}`),
  getByNpwp: (npwp: string) => api.get<TaxPayer>(`/api/taxpayers/npwp/${npwp}`),
  register: (data: {
    npwp: string; name: string; type: string; email: string;
    phone?: string; address?: string;
  }) => api.post<TaxPayer>('/api/taxpayers', data),
};

// Calculation API
export const calculationApi = {
  getAll: () => api.get<TaxCalculation[]>('/api/calculations'),
  getByTaxPayer: (id: string) => api.get<TaxCalculation[]>(`/api/calculations/taxpayer/${id}`),
  calculatePPh21: (data: {
    taxPayerId: string; grossIncome: number; maritalStatus: string;
    period: string; notes?: string;
  }) => api.post<TaxCalculation>('/api/calculations/pph21', data),
  calculatePPN: (data: {
    taxPayerId: string; amount: number; period: string; notes?: string;
  }) => api.post<TaxCalculation>('/api/calculations/ppn', data),
};

// Report API
export const reportApi = {
  getAll: () => api.get<TaxReport[]>('/api/reports'),
  getById: (id: string) => api.get<TaxReport>(`/api/reports/${id}`),
  getByTaxPayer: (id: string) => api.get<TaxReport[]>(`/api/reports/taxpayer/${id}`),
  create: (data: {
    taxPayerId: string; reportType: string; period: string;
    totalIncome: number; totalTax: number;
  }) => api.post<TaxReport>('/api/reports', data),
  submit: (id: string) => api.post(`/api/reports/${id}/submit`),
};

// Notification API
export const notificationApi = {
  getAll: () => api.get<Notification[]>('/api/notifications'),
  getByTaxPayer: (id: string) => api.get<Notification[]>(`/api/notifications/taxpayer/${id}`),
};

export const formatRupiah = (amount: number) =>
  new Intl.NumberFormat('id-ID', { style: 'currency', currency: 'IDR', minimumFractionDigits: 0 }).format(amount);

export const formatDate = (dateStr: string) =>
  new Date(dateStr).toLocaleString('id-ID', { dateStyle: 'medium', timeStyle: 'short' });
