import { useEffect, useRef, type ReactNode } from 'react';
import { X } from 'lucide-react';

export function Modal({ title, description, children, onClose, size = 'md' }: { title: string; description?: string; children: ReactNode; onClose: () => void; size?: 'md' | 'lg' }) {
  const dialogRef = useRef<HTMLElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const dialog = dialogRef.current;
    const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    const focusableElements = () => Array.from(dialog?.querySelectorAll<HTMLElement>(focusableSelector) ?? []);
    (focusableElements()[0] ?? dialog)?.focus();
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') closeRef.current(); };
    const keepFocusInside = (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || !dialog) return;
      const elements = focusableElements();
      const first = elements[0];
      const last = elements[elements.length - 1];
      if (!first || !last) {
        event.preventDefault();
        dialog.focus();
      } else if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    window.addEventListener('keydown', closeOnEscape);
    window.addEventListener('keydown', keepFocusInside);
    return () => {
      window.removeEventListener('keydown', closeOnEscape);
      window.removeEventListener('keydown', keepFocusInside);
      previouslyFocused?.focus();
    };
  }, []);

  return (
    <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section ref={dialogRef} className={`modal modal-${size}`} role="dialog" aria-modal="true" aria-label={title} tabIndex={-1}>
        <div className="modal-heading"><div><h2>{title}</h2>{description && <p>{description}</p>}</div><button className="icon-button" aria-label="Đóng" onClick={onClose}><X size={19} /></button></div>
        {children}
      </section>
    </div>
  );
}
