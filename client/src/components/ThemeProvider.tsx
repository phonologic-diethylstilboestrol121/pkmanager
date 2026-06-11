// ── ThemeProvider ─────────────────────────────────────────────────
// 管理亮色/暗色/跟随系统三态主题切换。
// 关键：同步 data-theme 到 document.documentElement，让 CSS 变量
// 和硬编码内联样式都能感知当前主题，避免"组件变暗、壳层仍亮"。
//
// 持久化：localStorage key = "pkmanager_theme"
// 值: "light" | "dark" | "system"

import React, { createContext, useContext, useEffect, useState, useMemo, useCallback, useSyncExternalStore } from 'react';
import { ConfigProvider, theme, App as AntdApp } from 'antd';
import zhCN from 'antd/locale/zh_CN';

export type ThemeMode = 'light' | 'dark' | 'system';

interface ThemeContextValue {
  mode: ThemeMode;
  isDark: boolean;
  setMode: (mode: ThemeMode) => void;
}

const ThemeContext = createContext<ThemeContextValue>({
  mode: 'system',
  isDark: false,
  setMode: () => {},
});

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext);
}

// ── useSystemPrefersDark ─────────────────────────────────────────

function useSystemPrefersDark(): boolean {
  const subscribe = useCallback((callback: () => void) => {
    const mql = window.matchMedia('(prefers-color-scheme: dark)');
    mql.addEventListener('change', callback);
    return () => mql.removeEventListener('change', callback);
  }, []);

  const getSnapshot = useCallback(() => {
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
  }, []);

  return useSyncExternalStore(subscribe, getSnapshot);
}

// ── ThemeProvider component ───────────────────────────────────────

const THEME_STORAGE_KEY = 'pkmanager_theme';

function readStoredMode(): ThemeMode {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
  } catch { /* localStorage disabled */ }
  return 'system';
}

export const ThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [mode, setModeState] = useState<ThemeMode>(readStoredMode);
  const prefersDark = useSystemPrefersDark();

  const isDark = mode === 'dark' || (mode === 'system' && prefersDark);
  const algorithm = isDark ? theme.darkAlgorithm : theme.defaultAlgorithm;

  // 同步 data-theme 到 documentElement — 关键：让 CSS 变量和内联样式感知主题
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
  }, [isDark]);

  const setMode = useCallback((newMode: ThemeMode) => {
    setModeState(newMode);
    try { localStorage.setItem(THEME_STORAGE_KEY, newMode); } catch { /* ignore */ }
  }, []);

  const ctxValue = useMemo<ThemeContextValue>(
    () => ({ mode, isDark, setMode }),
    [mode, isDark, setMode],
  );

  return (
    <ThemeContext.Provider value={ctxValue}>
      <ConfigProvider locale={zhCN} theme={{ algorithm }}>
        <AntdApp>{children}</AntdApp>
      </ConfigProvider>
    </ThemeContext.Provider>
  );
};
