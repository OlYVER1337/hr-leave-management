import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import type { AuthUser } from './types';
import { clearSession, getStoredUser, login as loginService } from './services/authService';

type AuthContextValue = {
  user: AuthUser | null;
  busy: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
};

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => getStoredUser());
  const [busy, setBusy] = useState(false);
  const value = useMemo<AuthContextValue>(() => ({
    user,
    busy,
    signIn: async (email, password) => {
      setBusy(true);
      try { setUser(await loginService(email, password)); }
      finally { setBusy(false); }
    },
    signOut: () => { clearSession(); setUser(null); },
  }), [user, busy]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside AuthProvider.');
  return value;
}
