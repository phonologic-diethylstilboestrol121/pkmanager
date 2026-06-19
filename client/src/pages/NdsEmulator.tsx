import React, { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Button, Space, Tag, Slider, Modal, Switch, Tooltip } from 'antd';
import { ArrowLeftOutlined, ReloadOutlined, SettingOutlined, SaveOutlined, CheckCircleOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { createNdsEmulator, type NdsEmulator, getNdsRomDisplayName } from '../lib/melonds';
import type { DsInputButton } from '../lib/melonds';
import { NDS_VERSION_MAP } from '../constants/games';
import {
  cloneGamepadBinds,
  codeLabel,
  DEFAULT_NDS_GAMEPAD_BINDS,
  emptyGamepadBinds,
  errorMessage,
  formatGamepadButtons,
  getFirstGamepad,
  getPressedGamepadButtons,
  GAMEPAD_DEADZONE_DEFAULT,
  loadGamepadBinds,
  loadNumberSetting,
  saveGamepadBinds,
  saveNumberSetting,
  toArrayBuffer,
} from '../lib/inputUtil';
import { getI18nText } from '../i18n/i18n';

type ScreenScale = 1 | 2;
const SZ: Record<ScreenScale, { w: number; h: number }> = { 1: { w: 256, h: 192 }, 2: { w: 512, h: 384 } };
type GamepadBindingMode = 'replace' | 'add';

const NDS_BTNS: DsInputButton[] = ['DPAD_UP','DPAD_DOWN','DPAD_LEFT','DPAD_RIGHT','A','B','X','Y','L','R','START','SELECT'];
const DEFAULT_KEYS: Record<string,string> = {
  DPAD_UP:'ArrowUp',DPAD_DOWN:'ArrowDown',DPAD_LEFT:'ArrowLeft',DPAD_RIGHT:'ArrowRight',
  A:'KeyZ',B:'KeyX',X:'KeyA',Y:'KeyS',L:'KeyQ',R:'KeyW',START:'Enter',SELECT:'Backspace',
};
const NDS_GAMEPAD_KEY = 'nds_gp_km';
const NDS_GAMEPAD_DEADZONE_KEY = 'nds_gp_deadzone';
const NDS_GAMEPAD_RUMBLE_KEY = 'nds_gp_rumble';
const NDS_GAMEPAD_RUMBLE_INTENSITY_KEY = 'nds_gp_rumble_intensity';
const WEBMELON_AXIS_DISABLED = -1;

function loadKM(): Record<string,string> { try { const s=localStorage.getItem('nds_km'); if(s) return JSON.parse(s); } catch { /* ignore invalid saved keyboard mapping */ } return {...DEFAULT_KEYS}; }
function saveKM(m: Record<string,string>) { localStorage.setItem('nds_km', JSON.stringify(m)); }
function loadGamepadKM(): Record<string, number[]> { return loadGamepadBinds(NDS_GAMEPAD_KEY, DEFAULT_NDS_GAMEPAD_BINDS); }
function saveGamepadKM(m: Record<string, number[]>) { saveGamepadBinds(NDS_GAMEPAD_KEY, m); }

// webmelon keybinds 使用 event.key → NDS bitmask
// bitmask: A=1,B=2,SELECT=4,START=8,RIGHT=16,LEFT=32,UP=64,DOWN=128,R=256,L=512,X=1024,Y=2048
const DS_BITMASK: Record<string, number> = {
  A:1, B:2, SELECT:4, START:8, DPAD_RIGHT:16, DPAD_LEFT:32, DPAD_UP:64, DPAD_DOWN:128, R:256, L:512, X:1024, Y:2048,
};
/** Convert event.code → event.key (approximate, works for standard QWERTY) */
function codeToKey(code: string): string {
  if (code.startsWith('Key')) return code.slice(3).toLowerCase();
  if (code.startsWith('Digit')) return code.slice(5);
  // Arrow keys etc. — code equals key for most non-letter keys
  return code;
}

/** Convert Uint8Array to base64 (chunked to avoid stack overflow) */
function u8b64(data: Uint8Array): string {
  const CHUNK = 0x2000; const parts: string[] = [];
  for (let i = 0; i < data.length; i += CHUNK) parts.push(String.fromCharCode(...data.subarray(i, i + CHUNK)));
  return btoa(parts.join(''));
}

const NdsEmulatorPage: React.FC = () => {
  const { saveFileId, gameId } = useParams<{ saveFileId?: string; gameId?: string }>();
  const { t } = useTranslation('emulator');
  const isNewGame = !!gameId;
  const topRef = useRef<HTMLCanvasElement>(null);
  const bottomRef = useRef<HTMLCanvasElement>(null);
  const effectiveSaveId = useRef<string | null>(saveFileId || null);
  const [scale, setScale] = useState<ScreenScale>(2);
  const [paused, setPaused] = useState(false);
  const [speed, setSpeed] = useState(1);
  const [volume, setVolume] = useState(100);
  const [fps, setFps] = useState(0);
  const [romName, setRomName] = useState('');
  const [saveName, setSaveName] = useState(isNewGame ? getI18nText('newGame', undefined, 'emulator') : '');
  const [ready, setReady] = useState(false);
  const [status, setStatus] = useState(getI18nText('status.initializing', undefined, 'emulator'));
  const [keyMap, setKeyMap] = useState<Record<string,string>>(loadKM);
  const [gamepadMap, setGamepadMap] = useState<Record<string, number[]>>(loadGamepadKM);
  const [gamepadDeadzone, setGamepadDeadzone] = useState<number>(() => loadNumberSetting(NDS_GAMEPAD_DEADZONE_KEY, GAMEPAD_DEADZONE_DEFAULT));
  const [gamepadRumble, setGamepadRumble] = useState<boolean>(() => localStorage.getItem(NDS_GAMEPAD_RUMBLE_KEY) !== 'false');
  const [gamepadRumbleIntensity, setGamepadRumbleIntensity] = useState<number>(() => loadNumberSetting(NDS_GAMEPAD_RUMBLE_INTENSITY_KEY, 0.5));
  const [keyDlg, setKeyDlg] = useState(false);
  const [binding, setBinding] = useState<string|null>(null);
  const [gamepadBindingMode, setGamepadBindingMode] = useState<GamepadBindingMode>('replace');
  const [micOn, setMicOn] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [synced, setSynced] = useState(false);
  const emuRef = useRef<NdsEmulator|null>(null);
  const initDone = useRef(false);
  const [gpConnected, setGpConnected] = useState(false);
  const [gpId, setGpId] = useState<string | null>(null);
  const initialGamepadMapRef = useRef(gamepadMap);
  const initialGamepadDeadzoneRef = useRef(gamepadDeadzone);
  const initialGamepadRumbleRef = useRef(gamepadRumble);
  const initialGamepadRumbleIntensityRef = useRef(gamepadRumbleIntensity);
  const et = (key: string, defaultValue: string, options?: Record<string, unknown>) => t(key, { defaultValue, ...(options ?? {}) });
  const buttonLabels: Record<string, string> = {
    DPAD_UP: et('button.up', '↑上'),
    DPAD_DOWN: et('button.down', '↓下'),
    DPAD_LEFT: et('button.left', '←左'),
    DPAD_RIGHT: et('button.right', '→右'),
    A: 'A',
    B: 'B',
    X: 'X',
    Y: 'Y',
    L: 'L',
    R: 'R',
    START: et('button.start', 'Start'),
    SELECT: et('button.select', 'Select'),
  };

  // Init emulator
  useEffect(() => {
    if (initDone.current || !topRef.current || !bottomRef.current) return;
    initDone.current = true;
    const auth = `Bearer ${localStorage.getItem('access_token')}`;
    (async () => {
      try {
        let gid: string;
        if (saveFileId) {
          setStatus(et('status.fetchingSaveInfo', '获取存档信息...'));
          const infoRes = await fetch(`/api/SaveFile/${saveFileId}`, { headers: { Authorization: auth } });
          if (!infoRes.ok) { setStatus(et('status.saveMissing', '存档不存在')); return; }
          const sd = (await infoRes.json()).data;
          setSaveName(sd.filename);
          gid = NDS_VERSION_MAP[sd.gameVersion] || 'pkm_diamond';
        } else if (gameId) {
          gid = gameId;
          setSaveName(getNdsRomDisplayName(gid));
        } else {
          setStatus(et('status.missingParams', '缺少参数')); return;
        }
        setRomName(getNdsRomDisplayName(gid));

        setStatus(et('status.downloadingRom', '下载ROM...'));
        const romRes = await fetch(`/api/Emulator/roms/${gid}`, { headers: { Authorization: auth } });
        if (!romRes.ok) { setStatus(et('status.romMissing', 'ROM缺失: {{gameId}}', { gameId: gid })); return; }
        const rom = new Uint8Array(await romRes.arrayBuffer());

        // 加载已有存档
        if (saveFileId) {
          const rawRes = await fetch(`/api/SaveFile/${saveFileId}/raw`, { headers: { Authorization: auth } });
          if (rawRes.ok) { const sav = new Uint8Array(await rawRes.arrayBuffer()); if (sav.length > 0) { /* pre-load below */ } }
        }

        setStatus(et('status.starting', '启动模拟器...'));
        const emu = await createNdsEmulator(topRef.current!, bottomRef.current!);
        emuRef.current = emu;
        const savedSettings = emu.getInputSettings?.();
        if (savedSettings) {
          emu.setInputSettings({
            ...savedSettings,
            keybinds: savedSettings.keybinds ?? {},
            gamepadBinds: initialGamepadMapRef.current,
            gamepadAxisSensitivity: 1 - initialGamepadDeadzoneRef.current,
            rumbleEnabled: initialGamepadRumbleRef.current,
            gamepadRumbleIntensity: initialGamepadRumbleIntensityRef.current,
          });
        }
        // 加载已有存档到模拟器
        if (saveFileId) {
          const rawRes = await fetch(`/api/SaveFile/${saveFileId}/raw`, { headers: { Authorization: auth } });
          if (rawRes.ok) { const sav = new Uint8Array(await rawRes.arrayBuffer()); if (sav.length > 0) emu.loadSave(sav); }
        }
        await emu.loadRom(rom);
        setReady(true); setStatus(et('status.ready', '就绪'));
      } catch (err: unknown) { setStatus(et('status.failed', '失败: {{error}}', { error: errorMessage(err) })); }
    })();
  }, [saveFileId, gameId, t]);

  useEffect(() => {
    saveKM(keyMap);
  }, [keyMap]);

  useEffect(() => {
    saveGamepadKM(gamepadMap);
  }, [gamepadMap]);

  useEffect(() => {
    saveNumberSetting(NDS_GAMEPAD_DEADZONE_KEY, gamepadDeadzone);
  }, [gamepadDeadzone]);

  useEffect(() => {
    localStorage.setItem(NDS_GAMEPAD_RUMBLE_KEY, String(gamepadRumble));
  }, [gamepadRumble]);

  useEffect(() => {
    saveNumberSetting(NDS_GAMEPAD_RUMBLE_INTENSITY_KEY, gamepadRumbleIntensity);
  }, [gamepadRumbleIntensity]);

  // FPS counter
  useEffect(() => {
    let c = 0, last = performance.now(), run = true;
    const loop = () => { if (!run) return; c++; const n = performance.now(); if (n - last >= 1000) { setFps(c); c = 0; last = n; } requestAnimationFrame(loop); };
    requestAnimationFrame(loop);
    return () => { run = false; };
  }, []);

  // Sync save to server (manual trigger or close)
  const syncSaveNow = async (): Promise<boolean> => {
    const emu = emuRef.current;
    if (!emu) return false;
    const sd = emu.getSave();
    if (!sd?.length) { setStatus(et('status.noInGameSaveCannotSync', '尚未在游戏中存档，无法同步')); setTimeout(() => { if (ready) setStatus(et('status.ready', '就绪')); }, 2500); return false; }
    const token = localStorage.getItem('access_token');
    if (!token) return false;
    setSyncing(true); setSynced(false);
    try {
      const encoded = u8b64(sd);
      const body: { saveFileId: string; saveDataBase64: string; gameId?: string } = {
        saveFileId: effectiveSaveId.current || '00000000-0000-0000-0000-000000000000',
        saveDataBase64: encoded,
      };
      if (isNewGame && gameId) body.gameId = gameId;
      console.log(`[ndsSync] Sending ${sd.length} bytes, new=${isNewGame}, id=${effectiveSaveId.current || '(new)'}`);
      const r = await fetch('/api/Emulator/sync-save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify(body),
      });
      if (r.ok) {
        const resp = await r.json();
        if (resp.data?.skipped) {
          setStatus(resp.message || et('status.noInGameSaveNoCreate', '尚未在游戏中存档，未创建存档'));
          setTimeout(() => { if (ready) setStatus(et('status.ready', '就绪')); }, 2500);
          return false;
        }
        if (resp.data?.saveFileId && !effectiveSaveId.current) {
          effectiveSaveId.current = resp.data.saveFileId;
          setSaveName(et('saveNameTemplate', '存档 - {{trainerName}}', {
            trainerName: resp.data.trainerName || et('trainerFallback', '训练家'),
          }));
          console.log(`[ndsSync] New save created: ${effectiveSaveId.current}`);
        }
        setSynced(true);
        return true;
      }
      console.error('[ndsSync] Server error:', r.status);
    } catch (err) {
      console.error('[ndsSync] Network error:', err);
    } finally {
      setSyncing(false);
    }
    return false;
  };

  // Keyboard — 写入 webmelon 原生 keybinds 格式 (event.key → bitmask)
  useEffect(() => {
    if (!ready) return;
    const keybinds: Record<string, number> = {};
    for (const [btn, code] of Object.entries(keyMap)) {
      const mask = DS_BITMASK[btn];
      if (mask !== undefined && code) {
        keybinds[codeToKey(code)] = mask;
      }
    }
    emuRef.current?.setKeyBinds(keybinds);

    // beforeunload: sendBeacon binary sync
    const unload = () => {
      const emu = emuRef.current;
      if (!emu) return;
      const sd = emu.getSave();
      if (!sd?.length) return;
      const token = localStorage.getItem('access_token');
      if (!token) return;
      const id = effectiveSaveId.current;
      const blob = new Blob([toArrayBuffer(sd)], { type: 'application/octet-stream' });
      if (id) {
        navigator.sendBeacon(`/api/Emulator/sync-save/${id}?token=${encodeURIComponent(token)}`, blob);
      } else if (isNewGame && gameId) {
        navigator.sendBeacon(`/api/Emulator/sync-save/new/${gameId}?token=${encodeURIComponent(token)}`, blob);
      }
    };
    window.addEventListener('beforeunload', unload);
    return () => { window.removeEventListener('beforeunload', unload); };
  }, [ready, saveFileId, keyMap, isNewGame, gameId]);

  useEffect(() => {
    if (!ready) return;
    const current = emuRef.current?.getInputSettings?.();
    if (!current) return;
    emuRef.current?.setInputSettings({
      ...current,
      gamepadBinds: binding ? emptyGamepadBinds(DEFAULT_NDS_GAMEPAD_BINDS) : gamepadMap,
      gamepadAxisSensitivity: binding ? WEBMELON_AXIS_DISABLED : 1 - gamepadDeadzone,
      rumbleEnabled: gamepadRumble,
      gamepadRumbleIntensity,
    });
  }, [ready, binding, gamepadMap, gamepadDeadzone, gamepadRumble, gamepadRumbleIntensity]);

  // Key / gamepad binding listener
  useEffect(() => {
    if (!binding) return;
    const h = (e: KeyboardEvent) => {
      e.preventDefault(); e.stopPropagation();
      if (e.code === 'Escape') { setBinding(null); return; }
      if (e.repeat) return;
      if (gamepadBindingMode === 'add') return;
      // 清除其他按键对此键码的旧绑定
      const nm = { ...keyMap };
      for (const [btn, code] of Object.entries(nm)) {
        if (code === e.code) delete nm[btn];
      }
      nm[binding] = e.code;
      setKeyMap(nm);
      setBinding(null);
    };
    window.addEventListener('keydown', h, true);
    let previousPressed = getPressedGamepadButtons();
    const poll = window.setInterval(() => {
      if (!binding) {
        return;
      }
      const pad = getFirstGamepad();
      if (!pad) {
        previousPressed = new Set<number>();
        return;
      }
      const currentPressed = new Set<number>();
      let pressed: number | null = null;
      for (const [idx, btn] of pad.buttons.entries()) {
        if (btn?.pressed) {
          currentPressed.add(idx);
          if (pressed == null && !previousPressed.has(idx)) {
            pressed = idx;
          }
        }
      }
      previousPressed = currentPressed;
      if (pressed != null) {
        const nm: Record<string, number[]> = {};
        for (const [btn, indices] of Object.entries(gamepadMap)) {
          nm[btn] = indices.filter((index) => index !== pressed);
        }
        nm[binding] = gamepadBindingMode === 'add'
          ? [...(nm[binding] || []), pressed]
          : [pressed];
        setGamepadMap(nm);
        setBinding(null);
      }
    }, 33);
    return () => {
      window.removeEventListener('keydown', h, true);
      window.clearInterval(poll);
    };
  }, [binding, keyMap, gamepadMap, gamepadBindingMode]);

  useEffect(() => {
    if (!ready) return;
    const syncPrimaryGamepad = () => {
      const pad = getFirstGamepad();
      setGpConnected(!!pad);
      setGpId(pad?.id || null);
    };
    const onConnected = () => syncPrimaryGamepad();
    const onDisconnected = () => syncPrimaryGamepad();
    syncPrimaryGamepad();
    window.addEventListener('gamepadconnected', onConnected);
    window.addEventListener('gamepaddisconnected', onDisconnected);
    return () => {
      window.removeEventListener('gamepadconnected', onConnected);
      window.removeEventListener('gamepaddisconnected', onDisconnected);
    };
  }, [ready]);

  const { w, h } = SZ[scale];

  return (
    <div style={{ minHeight: '100vh', background: '#1a1a2e', color: '#eee' }}>
      {/* Toolbar */}
      <div style={{ background: '#16213e', padding: '6px 16px', display: 'flex', alignItems: 'center', gap: 10, borderBottom: '1px solid #0f3460', flexWrap: 'wrap' }}>
        <Button icon={<ArrowLeftOutlined />} ghost size="small" onClick={async () => { await syncSaveNow(); setTimeout(() => window.close(), 300); }}>{et('toolbar.close', '关闭')}</Button>
        <Button ghost size="small" icon={synced ? <CheckCircleOutlined /> : <SaveOutlined />}
          onClick={syncSaveNow} loading={syncing} disabled={!ready}
          style={{ color: synced ? '#52c41a' : undefined }}>{et('toolbar.syncSave', '同步存档')}</Button>
        <span style={{ fontWeight: 600, fontSize: 13 }}>{saveName || 'NDS'}</span>
        <Tag color="purple" style={{ fontSize: 11 }}>{romName}</Tag>
        <div style={{ flex: 1 }} />
        <Space size={4} wrap>
          <span style={{ fontSize: 11, color: '#888' }}>{et('toolbar.screen', '画面')}</span>
          {([1,2] as const).map(s => (
            <Button key={`scale-${s}`} ghost size="small" type={scale===s?'primary':'default'}
              onClick={() => setScale(s)} disabled={!ready} style={{ padding: '0 8px', fontSize: 12 }}>{s}×</Button>
          ))}
          <span style={{ fontSize: 11, color: '#888', marginLeft: 4 }}>{et('toolbar.speed', '速度')}</span>
          {([1,2,4] as const).map(s => (
            <Button key={`speed-${s}`} ghost size="small" type={speed===s?'primary':'default'}
              onClick={() => { emuRef.current?.setSpeed(s); setSpeed(s); }} disabled={!ready} style={{ padding: '0 8px', fontSize: 12 }}>{s}×</Button>
          ))}
          <Button ghost size="small" onClick={() => { const e=emuRef.current; if(!e)return; if(paused){e.resume();setPaused(false);}else{e.pause();setPaused(true);} }} disabled={!ready}>
            {paused ? et('toolbar.resume', '继续') : et('toolbar.pause', '暂停')}
          </Button>
          <Button ghost size="small" icon={<ReloadOutlined />} onClick={() => { window.location.reload(); }} disabled={!ready}>{et('toolbar.reset', '重置')}</Button>
          <Tooltip title={et('tooltip.ndsCheatsUnavailable', 'melonDS WASM 当前未暴露金手指接口，请使用桌面版模拟器')}>
            <span>
              <Button ghost size="small" icon={<ThunderboltOutlined />} disabled>{et('toolbar.cheats', '金手指')}</Button>
            </span>
          </Tooltip>
          <div style={{ width: 80, display: 'flex', alignItems: 'center', gap: 4 }}>
            <span style={{ fontSize: 11, color: '#888', cursor: 'pointer' }} onClick={() => { const v = volume > 0 ? 0 : 100; setVolume(v); emuRef.current?.setVolume(v); }}>
              {volume > 0 ? '🔊' : '🔇'}
            </span>
            <Slider min={0} max={100} value={volume} onChange={v => { setVolume(v); emuRef.current?.setVolume(v); }} disabled={!ready} style={{ width: 60, margin: 0 }} tooltip={{formatter:v=>`${v}%`}} />
          </div>
          {/* Mic noise toggle */}
          <Button ghost size="small" disabled={!ready}
            onClick={() => { const next = !micOn; setMicOn(next); emuRef.current?.setMicNoise(next); }}
            type={micOn ? 'primary' : 'default'}
            style={{ padding: '0 6px', fontSize: 11 }}>🎤</Button>
          {gpConnected && <Tag color="cyan" style={{ fontSize: 11 }} title={gpId || undefined}>🎮 {et('toolbar.connected', '已连接')}</Tag>}
          <Button ghost size="small" icon={<SettingOutlined />} disabled={!ready} onClick={() => setKeyDlg(true)}>{et('toolbar.buttons', '按键')}</Button>
          <Tag color="green" style={{ fontSize: 11 }}>{fps} FPS</Tag>
        </Space>
      </div>

      {/* Dual Screens — NDS style: tight stack with hinge gap */}
      <div style={{
        display: 'flex', flexDirection: 'column', alignItems: 'center', marginTop: 12,
        background: '#1a1a1a', borderRadius: 8, padding: '12px 16px 16px 16px',
        width: 'fit-content', marginLeft: 'auto', marginRight: 'auto',
        boxShadow: '0 0 20px rgba(0,0,0,0.5)',
      }}>
        {/* Top Screen */}
        <div style={{ position: 'relative', width: w, height: h, marginBottom: 6 }}>
          <canvas ref={topRef} width={256} height={192}
            style={{ width: w, height: h, imageRendering: 'pixelated', border: '3px solid #2a2a2a', borderRadius: '6px 6px 0 0', background: '#000', display: ready ? 'block' : 'none' }} />
        </div>
        {/* Hinge line */}
        <div style={{ width: w + 6, height: 4, background: '#333', borderRadius: 2, marginBottom: 6 }} />
        {/* Bottom Screen (touch) */}
        <div style={{ position: 'relative', width: w, height: h }}>
          <canvas ref={bottomRef} width={256} height={192}
            style={{ width: w, height: h, imageRendering: 'pixelated', border: '3px solid #2a2a2a', borderRadius: '0 0 6px 6px', background: '#000', cursor: 'crosshair', display: ready ? 'block' : 'none' }}
            title={et('screen.touchTitle', '触摸屏 (webmelon 自动处理触控)')}
          />
          {!ready && (
            <div style={{
              position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
              background: '#000', color: '#888', borderRadius: '0 0 6px 6px'
            }}>
              <div style={{ fontSize: 16 }}>{et('loading', '加载中...')}</div>
              <div style={{ fontSize: 12, color: '#666', marginTop: 4 }}>{status}</div>
            </div>
          )}
        </div>
        {/* Bottom label */}
        <div style={{ color: '#555', fontSize: 10, marginTop: 8, textAlign: 'center' }}>
          {et('screen.touchLabel', '下屏 — 触摸屏 (鼠标点击 / 触屏)')}
        </div>
      </div>

      {/* Key Mapping Modal */}
      <Modal title={et('keymap.ndsTitle', 'NDS 按键映射设置')} open={keyDlg} onCancel={() => setKeyDlg(false)} footer={null} width={420}>
        <div style={{ color: '#888', fontSize: 12, marginBottom: 8 }}>
          {et('keymap.captureHint', '按下按键或手柄按钮，先捕获到的绑定生效。')}
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 8 }}>
          {NDS_BTNS.map(btn => (
            <div key={btn} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 10px', background: '#f5f5f5', borderRadius: 6, gap: 8 }}>
              <span style={{ fontWeight: 600 }}>{buttonLabels[btn] ?? btn}</span>
              <Button size="small" type={binding === btn ? 'primary' : 'default'} onClick={() => { setGamepadBindingMode('replace'); setBinding(binding === btn ? null : btn); }} style={{ minWidth: 170, textAlign: 'left', height: 'auto', whiteSpace: 'normal' }}>
                {binding === btn ? et('keymap.capturing', '按下按键或手柄按钮...') : (
                  <span>
                    <span>{codeLabel(keyMap[btn] || '?')}</span>
                    <span style={{ display: 'block', fontSize: 11, opacity: 0.8 }}>{formatGamepadButtons(gamepadMap[btn])}</span>
                  </span>
                )}
              </Button>
              <Button size="small" onClick={() => { setGamepadBindingMode('add'); setBinding(btn); }}>{et('keymap.addGamepad', '追加手柄')}</Button>
              <Button size="small" danger onClick={() => setGamepadMap((prev) => ({ ...prev, [btn]: [] }))}>{et('keymap.clearGamepad', '清除手柄')}</Button>
            </div>
          ))}
        </div>
        <div style={{ marginTop: 12 }}>
          <Button size="small" onClick={() => {
            setKeyMap({...DEFAULT_KEYS});
            setGamepadMap(cloneGamepadBinds(DEFAULT_NDS_GAMEPAD_BINDS));
            setGamepadDeadzone(GAMEPAD_DEADZONE_DEFAULT);
            setGamepadRumble(true);
            setGamepadRumbleIntensity(0.5);
          }}>{et('keymap.restoreDefault', '恢复默认')}</Button>
        </div>
        <div style={{ marginTop: 12, background: '#fafafa', borderRadius: 6, padding: 10 }}>
          <div style={{ fontSize: 12, marginBottom: 6 }}>{et('keymap.gamepadDeadzone', '手柄 deadzone: {{value}}', { value: gamepadDeadzone.toFixed(2) })}</div>
          <Slider min={0} max={1} step={0.05} value={gamepadDeadzone} onChange={setGamepadDeadzone} />
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 12 }}>
            <span style={{ fontSize: 12 }}>{et('keymap.rumble', '震动')}</span>
            <Switch checked={gamepadRumble} onChange={setGamepadRumble} />
          </div>
          <div style={{ fontSize: 12, marginTop: 12, marginBottom: 6 }}>{et('keymap.rumbleIntensity', '震动强度: {{value}}', { value: gamepadRumbleIntensity.toFixed(2) })}</div>
          <Slider min={0} max={1} step={0.05} value={gamepadRumbleIntensity} onChange={setGamepadRumbleIntensity} disabled={!gamepadRumble} />
        </div>
      </Modal>

      {/* Key guide */}
      <div style={{ textAlign: 'center', color: '#555', fontSize: 11, marginTop: 4 }}>
        {NDS_BTNS.slice(0,4).map(b=>codeLabel(keyMap[b]||'?')).join('')} {et('summary.direction', '方向')} |
        A={codeLabel(keyMap['A']||'?')} B={codeLabel(keyMap['B']||'?')} X={codeLabel(keyMap['X']||'?')} Y={codeLabel(keyMap['Y']||'?')} |
        L={codeLabel(keyMap['L']||'?')} R={codeLabel(keyMap['R']||'?')} |
        {formatGamepadButtons(gamepadMap['A'])} A {formatGamepadButtons(gamepadMap['B'])} B
      </div>

      {/* Mobile touch gamepad */}
      {ready && 'ontouchstart' in window && (
        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 16px', maxWidth: 560, margin: '0 auto', userSelect: 'none', touchAction: 'none' }}>
          {/* D-Pad */}
          <div style={{ display: 'grid', gridTemplateColumns: '40px 40px 40px', gridTemplateRows: '40px 40px 40px', gap: 2 }}>
            <div />
            <NdsTouchBtn label="↑" onPress={() => emuRef.current?.pressButton('DPAD_UP')} onRelease={() => emuRef.current?.releaseButton('DPAD_UP')} />
            <div />
            <NdsTouchBtn label="←" onPress={() => emuRef.current?.pressButton('DPAD_LEFT')} onRelease={() => emuRef.current?.releaseButton('DPAD_LEFT')} />
            <div style={{ background: '#222', borderRadius: 4 }} />
            <NdsTouchBtn label="→" onPress={() => emuRef.current?.pressButton('DPAD_RIGHT')} onRelease={() => emuRef.current?.releaseButton('DPAD_RIGHT')} />
            <div />
            <NdsTouchBtn label="↓" onPress={() => emuRef.current?.pressButton('DPAD_DOWN')} onRelease={() => emuRef.current?.releaseButton('DPAD_DOWN')} />
            <div />
          </div>
          {/* Center: Select/Start */}
          <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', gap: 6 }}>
            <NdsTouchBtn label={et('button.selectShort', 'Sel')} onPress={() => emuRef.current?.pressButton('SELECT')} onRelease={() => emuRef.current?.releaseButton('SELECT')} small />
            <NdsTouchBtn label={et('button.start', 'Start')} onPress={() => emuRef.current?.pressButton('START')} onRelease={() => emuRef.current?.releaseButton('START')} small />
          </div>
          {/* Action buttons */}
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
            <div style={{ display: 'flex', gap: 12 }}>
              <NdsTouchBtn label="L" onPress={() => emuRef.current?.pressButton('L')} onRelease={() => emuRef.current?.releaseButton('L')} />
              <NdsTouchBtn label="R" onPress={() => emuRef.current?.pressButton('R')} onRelease={() => emuRef.current?.releaseButton('R')} />
            </div>
            <div style={{ display: 'flex', gap: 6 }}>
              <NdsTouchBtn label="X" onPress={() => emuRef.current?.pressButton('X')} onRelease={() => emuRef.current?.releaseButton('X')} style={{ background: '#1a3a6a' }} />
              <NdsTouchBtn label="Y" onPress={() => emuRef.current?.pressButton('Y')} onRelease={() => emuRef.current?.releaseButton('Y')} style={{ background: '#1a6a3a' }} />
            </div>
            <div style={{ display: 'flex', gap: 6 }}>
              <NdsTouchBtn label="B" onPress={() => emuRef.current?.pressButton('B')} onRelease={() => emuRef.current?.releaseButton('B')} style={{ background: '#c41a1a' }} />
              <NdsTouchBtn label="A" onPress={() => emuRef.current?.pressButton('A')} onRelease={() => emuRef.current?.releaseButton('A')} style={{ background: '#1a7a1a' }} />
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

/** Touch button for mobile gamepad */
const NdsTouchBtn: React.FC<{
  label: string; onPress: () => void; onRelease: () => void; small?: boolean; style?: React.CSSProperties;
}> = ({ label, onPress, onRelease, small, style }) => {
  const pressing = useRef(false);
  const press = (e: React.TouchEvent) => { e.preventDefault(); if (!pressing.current) { onPress(); pressing.current = true; } };
  const release = (e: React.TouchEvent) => { e.preventDefault(); if (pressing.current) { onRelease(); pressing.current = false; } };
  const size = small ? 36 : 40;
  return (
    <div
      onTouchStart={press} onTouchEnd={release} onTouchCancel={release}
      style={{
        width: size, height: size, borderRadius: 6, display: 'flex', alignItems: 'center', justifyContent: 'center',
        background: '#333', color: '#eee', fontWeight: 700, fontSize: small ? 10 : 13,
        border: '2px solid #555', cursor: 'pointer', ...style,
      }}
    >{label}</div>
  );
};

export default NdsEmulatorPage;
