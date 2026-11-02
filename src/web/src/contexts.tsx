import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import axios from 'axios';
import type { AuthUser } from './types';
import { clearSession, getStoredUser, login as loginService, logout as logoutService, restoreSession } from './services/authService';
import { DEMO_MODE } from './services/config';
import { getApiErrorMessage } from './services/apiClient';

type AuthContextValue = {
  user: AuthUser | null;
  busy: boolean;
  restoreError: string | null;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => getStoredUser());
  const [busy, setBusy] = useState(!DEMO_MODE);
  const [restoreError, setRestoreError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const handleExpired = () => {
      clearSession();
      setUser(null);
    };
    const handleUpdated = (event: Event) => {
      const { accessToken, expiresAtUtc } = (event as CustomEvent<{
        accessToken: string;
        expiresAtUtc: string;
      }>).detail;
      setUser((current) => current ? { ...current, accessToken, expiresAtUtc } : current);
    };

    window.addEventListener('auth:session-expired', handleExpired);
    window.addEventListener('auth:session-updated', handleUpdated);

    async function initializeSession() {
      if (DEMO_MODE) {
        setUser(getStoredUser());
        setBusy(false);
        return;
      }

      const storedUser = getStoredUser();
      if (storedUser) {
        setUser(storedUser);
        setBusy(false);
        return;
      }

      try {
        const restoredUser = await restoreSession();
        if (active) setUser(restoredUser);
      } catch (error) {
        clearSession();
        if (active) {
          setUser(null);
          if (!axios.isAxiosError(error) || error.response?.status !== 401) {
            setRestoreError(getApiErrorMessage(error));
          }
        }
      } finally {
        if (active) setBusy(false);
      }
    }

    void initializeSession();
    return () => {
      active = false;
      window.removeEventListener('auth:session-expired', handleExpired);
      window.removeEventListener('auth:session-updated', handleUpdated);
    };
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    busy,
    restoreError,
    signIn: async (email, password) => {
      setBusy(true);
      setRestoreError(null);
      try { setUser(await loginService(email, password)); }
      finally { setBusy(false); }
    },
    signOut: async () => {
      setBusy(true);
      try {
        await logoutService();
      } finally {
        setUser(null);
        setBusy(false);
      }
    },
  }), [user, busy, restoreError]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside AuthProvider.');
  return value;
}
