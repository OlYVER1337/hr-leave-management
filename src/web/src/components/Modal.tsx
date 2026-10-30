import { useEffect, type ReactNode } from 'react';
import { X } from 'lucide-react';

export function Modal({ title, description, children, onClose, size = 'md' }: { title: string; description?: string; children: ReactNode; onClose: () => void; size?: 'md' | 'lg' }) {
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    window.addEventListener('keydown', closeOnEscape);
    return () => window.removeEventListener('keydown', closeOnEscape);
  }, [onClose]);
  return (
    <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section className={`modal modal-${size}`} role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-heading"><div><h2>{title}</h2>{description && <p>{description}</p>}</div><button className="icon-button" aria-label="Đóng" onClick={onClose}><X size={19} /></button></div>
        {children}
      </section>
    </div>
  );
}
