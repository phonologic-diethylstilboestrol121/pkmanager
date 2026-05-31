import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ConfigProvider, App as AntdApp } from 'antd';
import zhCN from 'antd/locale/zh_CN';

import LoginPage from './pages/Login';
import RegisterPage from './pages/Register';
import DashboardPage from './pages/Dashboard';
import SavesPage from './pages/Saves';
import BankPage from './pages/Bank';
import SaveEditor from './pages/SaveEditor';
import ProtectedRoute from './components/ProtectedRoute';
const EmulatorPage = React.lazy(() => import('./pages/Emulator'));

const App: React.FC = () => {
  return (
    <ConfigProvider locale={zhCN}>
      <AntdApp>
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
                  <React.Suspense fallback={<div style={{padding:48,textAlign:'center'}}>加载模拟器...</div>}>
                    <EmulatorPage />
                  </React.Suspense>
                </ProtectedRoute>
              }
            />
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </BrowserRouter>
      </AntdApp>
    </ConfigProvider>
  );
};

export default App;
