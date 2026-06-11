import React, { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ThemeProvider } from './components/ThemeProvider';

import LoginPage from './pages/Login';
import RegisterPage from './pages/Register';
import DashboardPage from './pages/Dashboard';
import SavesPage from './pages/Saves';
import BankPage from './pages/Bank';
import SaveEditor from './pages/SaveEditor';
import SettingsPage from './pages/Settings';
import ProtectedRoute from './components/ProtectedRoute';
import ErrorBoundary from './components/ErrorBoundary';
import DiagnosticPanel from './components/DiagnosticPanel';
import { useDiagnosticStore } from './stores/diagnosticStore';
import { authApi } from './api/auth';

const EmulatorPage = React.lazy(() => import('./pages/Emulator'));
const NdsEmulatorPage = React.lazy(() => import('./pages/NdsEmulator'));

// ── Lazy route wrapper with ErrorBoundary ───────────────────────────

const LazyRoute: React.FC<{
  name: string;
  fallback: string;
  children: React.ReactNode;
}> = ({ name, fallback, children }) => (
  <ErrorBoundary name={`lazy-${name}`}>
    <React.Suspense
      fallback={
        <div style={{ padding: 48, textAlign: 'center', color: '#888' }}>
          {fallback}
        </div>
      }
    >
      {children}
    </React.Suspense>
  </ErrorBoundary>
);

// ── Health check on mount ───────────────────────────────────────────

const HealthChecker: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  useEffect(() => {
    const store = useDiagnosticStore.getState();

    // 1. API reachability
    fetch('/api/health', { signal: AbortSignal.timeout(5000) })
      .then((r) => {
        if (r.ok) {
          store.log({ category: 'health', level: 'info', message: 'API 可达' });
          store.setHealth('ok');
        } else {
          store.log({
            category: 'health',
            level: 'error',
            message: `API 返回状态 ${r.status}`,
          });
          store.setHealth('degraded');
        }
      })
      .catch((err) => {
        store.log({
          category: 'health',
          level: 'error',
          message: `API 不可达: ${err.message}`,
        });
        store.setHealth('down');
      });

    // 2. Auth token validity (if token exists)
    const token = localStorage.getItem('access_token');
    if (token) {
      authApi
        .me()
        .then(() => {
          store.log({ category: 'health', level: 'info', message: 'Auth Token 有效' });
        })
        .catch((err) => {
          store.log({
            category: 'auth',
            level: 'warn',
            message: `Auth Token 验证失败: ${err.response?.status || err.message}`,
          });
          store.setHealth('degraded');
        });
    }
  }, []);

  return <>{children}</>;
};

// ── App ─────────────────────────────────────────────────────────────

const App: React.FC = () => {
  return (
    <ThemeProvider>
        <ErrorBoundary name="app-root">
          <HealthChecker>
            <BrowserRouter>
              <Routes>
                <Route path="/login" element={<LoginPage />} />
                <Route path="/register" element={<RegisterPage />} />
                <Route
                  path="/dashboard"
                  element={
                    <ProtectedRoute>
                      <DashboardPage />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/saves"
                  element={
                    <ProtectedRoute>
                      <SavesPage />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/bank"
                  element={
                    <ProtectedRoute>
                      <BankPage />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/saves/:id"
                  element={
                    <ProtectedRoute>
                      <SaveEditor />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/play/:saveFileId"
                  element={
                    <ProtectedRoute>
                      <LazyRoute name="gba-emulator" fallback="加载模拟器...">
                        <EmulatorPage />
                      </LazyRoute>
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/play/new/:gameId"
                  element={
                    <ProtectedRoute>
                      <LazyRoute name="gba-emulator-new" fallback="加载模拟器 (新游戏)...">
                        <EmulatorPage />
                      </LazyRoute>
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/play-nds/:saveFileId"
                  element={
                    <ProtectedRoute>
                      <LazyRoute name="nds-emulator" fallback="加载 NDS 模拟器...">
                        <NdsEmulatorPage />
                      </LazyRoute>
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/play-nds/new/:gameId"
                  element={
                    <ProtectedRoute>
                      <LazyRoute name="nds-emulator-new" fallback="加载 NDS 模拟器 (新游戏)...">
                        <NdsEmulatorPage />
                      </LazyRoute>
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="/settings"
                  element={
                    <ProtectedRoute>
                      <SettingsPage />
                    </ProtectedRoute>
                  }
                />
                <Route path="*" element={<Navigate to="/dashboard" replace />} />
              </Routes>
            </BrowserRouter>
          </HealthChecker>
        </ErrorBoundary>

        {/* Diagnostic panel — outside router so accessible on all pages */}
        <DiagnosticPanel />
    </ThemeProvider>
  );
};

export default App;
