import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, Info, X, XCircle } from 'lucide-react';

type ToastKind = 'success' | 'error' | 'info';
type ToastItem = { id: number; message: string; kind: ToastKind };
const ToastContext = createContext<(message: string, kind?: ToastKind) => void>(() => undefined);

export function useToast() { return useContext(ToastContext); }

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const recentMessages = useRef(new Map<string, number>());
  const notify = useCallback((message: string, kind: ToastKind = 'success') => {
    const key = `${kind}:${message}`;
    const now = Date.now();
    const previous = recentMessages.current.get(key);
    if (previous !== undefined && now - previous < 3600) return;

    recentMessages.current.set(key, now);
    const id = now + Math.random();
    setItems((current) => [...current, { id, message, kind }]);
    window.setTimeout(() => {
      setItems((current) => current.filter((item) => item.id !== id));
      if (recentMessages.current.get(key) === now) {
        recentMessages.current.delete(key);
      }
    }, 3600);
  }, []);
  return (
    <ToastContext.Provider value={notify}>
      {children}
      <div className="toast-stack" aria-live="polite" aria-atomic="false">
        {items.map((item) => (
          <div className={`toast toast-${item.kind}`} key={item.id} role="status">
            {item.kind === 'success' ? <CheckCircle2 size={19} /> : item.kind === 'error' ? <XCircle size={19} /> : <Info size={19} />}
            <span>{item.message}</span>
            <button className="icon-button toast-close" aria-label="Đóng thông báo" onClick={() => setItems((current) => current.filter((entry) => entry.id !== item.id))}><X size={15} /></button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
