import type { ReactNode } from 'react';
import { ArrowDownRight, ArrowUpRight, Inbox } from 'lucide-react';
import type { LeaveStatus } from '../types';

export function PageHeader({ eyebrow, title, description, actions }: { eyebrow?: string; title: string; description?: string; actions?: ReactNode }) {
  return (
    <div className="page-header">
      <div>
        {eyebrow && <div className="eyebrow">{eyebrow}</div>}
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </div>
  );
}

export function StatCard({ label, value, hint, icon, tone = 'blue', trend }: { label: string; value: string | number; hint: string; icon: ReactNode; tone?: 'blue' | 'violet' | 'amber' | 'green'; trend?: 'up' | 'down' }) {
  return (
    <article className="stat-card">
      <div className="stat-card-top"><span>{label}</span><span className={`stat-icon stat-icon-${tone}`}>{icon}</span></div>
      <div className="stat-value">{value}</div>
      <div className="stat-foot">{trend && <span className={`trend trend-${trend}`}>{trend === 'up' ? <ArrowUpRight size={14} /> : <ArrowDownRight size={14} />}</span>}{hint}</div>
    </article>
  );
}

export function StatusBadge({ status }: { status: LeaveStatus }) {
  const key = status.toLowerCase();
  const labels: Record<LeaveStatus, string> = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Từ chối', Cancelled: 'Đã hủy' };
  return <span className={`status-badge status-${key}`}><span className="status-dot" />{labels[status]}</span>;
}

export function Avatar({ name, size = 'md' }: { name: string; size?: 'sm' | 'md' | 'lg' }) {
  const initials = name.trim().split(/\s+/).slice(-2).map((part) => part[0] || '').join('').toUpperCase();
  return <span className={`avatar avatar-${size}`} aria-label={name}>{initials || 'U'}</span>;
}

export function EmptyState({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return <div className="empty-state"><span className="empty-icon"><Inbox size={23} /></span><h3>{title}</h3><p>{description}</p>{action}</div>;
}

export function formatDate(value: string, options?: Intl.DateTimeFormatOptions): string {
  if (!value) return '—';
  const date = new Date(`${value.slice(0, 10)}T12:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('vi-VN', options ?? { day: '2-digit', month: '2-digit', year: 'numeric' }).format(date);
}

export function initialsColor(seed: string): string {
  const colors = ['#e6efff', '#efe8ff', '#ffeddc', '#dff7ed', '#ffe8ec'];
  return colors[[...seed].reduce((sum, char) => sum + char.charCodeAt(0), 0) % colors.length];
}
