import React, { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Typography, Button, App, Spin, Tag, Tooltip, Space, Popconfirm, Dropdown, Select,
  Tabs, Segmented,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  SaveOutlined, DownloadOutlined, ArrowLeftOutlined, BankOutlined,
  SafetyCertificateOutlined, AppstoreOutlined, LeftOutlined, RightOutlined,
  StarFilled, SortAscendingOutlined, SunOutlined, MoonOutlined, DesktopOutlined,
} from '@ant-design/icons';
import {
  DndContext, DragOverlay, closestCenter, PointerSensor, useSensor, useSensors,
  useDraggable, useDroppable,
  type DragStartEvent, type DragEndEvent,
} from '@dnd-kit/core';
import { saveFileApi, type SaveFileDetail, type BoxSlotDto, type PokemonDto, type SaveBackupDto, type LegalityStatus, type SaveBoxSortBy } from '../api/saveFile';
import { useDiagnosticStore } from '../stores/diagnosticStore';
import { useTheme, type ThemeMode } from '../components/ThemeProvider';
import { bankApi, type BankListItem } from '../api/bank';
import EditPanel from '../components/editor/EditPanel';
import BagPanel from '../components/editor/BagPanel';
import TrainerPanel from '../components/editor/TrainerPanel';
import PokedexPanel from '../components/editor/PokedexPanel';
import GenToolsPanel from '../components/editor/GenToolsPanel';
import AllBoxesModal from '../components/AllBoxesModal';
import { useAuthStore } from '../stores/authStore';
import GameCover from '../components/GameCover';
import PokemonSprite from '../components/PokemonSprite';
import { getStoredSpriteStyle, type SpriteStyle } from '../lib/spriteUrl';

const { Title, Text } = Typography;

// ── ID helpers ────────────────────────────────────────
const saveSlotId = (box: number, slot: number) => `save:${box}:${slot}`;
const bankItemId = (bankId: string) => `bank:${bankId}`;
const bankDropId = 'bank-drop-zone';
const parseSaveSlot = (id: string) => ({ boxIndex: +id.split(':')[1], slotIndex: +id.split(':')[2] });
const BOX_SORT_LABELS: Record<SaveBoxSortBy, string> = {
  species: '物种编号',
  level: '等级',
  shiny: '闪光优先',
  name: '名称',
};
const BOX_SORT_MENU_ITEMS: MenuProps['items'] = [
  { key: 'species', label: '按物种编号' },
  { key: 'level', label: '按等级' },
  { key: 'shiny', label: '闪光优先' },
  { key: 'name', label: '按名称' },
];

const getDownloadFileName = (contentDisposition?: string, fallback = 'save.sav') => {
  if (!contentDisposition) return fallback;

  const utf8Match = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }

  const plainMatch = contentDisposition.match(/filename="?([^"]+)"?/i);
  if (plainMatch?.[1]) return plainMatch[1];
  return fallback;
};

// ── Draggable Slot Component ─────────────────────────
const DraggableSlot: React.FC<{
  boxIndex: number; slot: BoxSlotDto; onPokemonClick?: (p: PokemonDto) => void;
  legalityStatus?: LegalityStatus;
  spriteStyle?: SpriteStyle;
}> = ({ boxIndex, slot, onPokemonClick, legalityStatus, spriteStyle }) => {
  const slotId = saveSlotId(boxIndex, slot.slotIndex);
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: slotId, disabled: slot.isEmpty });
  const { setNodeRef: setDropRef, isOver } = useDroppable({ id: slotId });

  const p = slot.pokemon;
  const isEmpty = slot.isEmpty;

  // Legality dot color
  const legalityColor =
    legalityStatus === 'Legal' ? '#52c41a' :
    legalityStatus === 'Fishy' ? '#faad14' :
    legalityStatus === 'Illegal' ? '#ff4d4f' :
    undefined;

  return (
    <div
      ref={(node) => { setNodeRef(node); setDropRef(node); }}
      style={{
        aspectRatio: '1',
        border: isOver ? '2px solid #52c41a' : isEmpty ? '2px dashed #d9d9d9' : '1px solid #e8e8e8',
        borderRadius: 8,
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        cursor: isEmpty ? 'default' : 'grab',
        background: isOver ? '#f6ffed' : isEmpty ? '#fafafa' : '#fff',
        opacity: isDragging ? 0.5 : 1,
        transition: 'border-color 0.2s, background 0.2s',
        userSelect: 'none',
        position: 'relative',
      }}
      {...(!isEmpty ? { ...attributes, ...listeners } : {})}
      onClick={() => { if (!isEmpty && p && onPokemonClick) onPokemonClick(p); }}
    >
      {isEmpty ? (
        <Text type="secondary" style={{ fontSize: 11 }}>{slot.slotIndex + 1}</Text>
      ) : (
        <>
          <div style={{ position: 'relative' }}>
            <PokemonSprite speciesId={p!.species} width={32} height={32}
              variant={spriteStyle}
            />
            {/* Alpha badge — top-left */}
            {p!.isAlpha && (
              <span style={{
                position: 'absolute', top: -4, left: -4,
                background: '#ff4d4f', color: '#fff', borderRadius: '50%',
                width: 14, height: 14, fontSize: 9, lineHeight: '14px', textAlign: 'center',
                border: '1px solid #fff',
              }} title="头目 (Alpha)">α</span>
            )}
            {/* Shiny star — top-right */}
            {p!.isShiny && (
              <StarFilled style={{
                position: 'absolute', top: -4, right: -4,
                fontSize: 12, color: '#faad14', filter: 'drop-shadow(0 0 1px rgba(0,0,0,0.3))',
              }} title="闪光" />
            )}
            {/* Gmax badge — bottom-right */}
            {p!.canGigantamax && (
              <span style={{
                position: 'absolute', bottom: -4, right: -4,
                background: '#fa541c', color: '#fff', borderRadius: '50%',
                width: 14, height: 14, fontSize: 10, lineHeight: '14px', textAlign: 'center',
                border: '1px solid #fff',
              }} title="超极巨化">G</span>
            )}
            {/* Legality indicator dot — bottom-left (tri-color) */}
            {legalityColor && (
              <span style={{
                position: 'absolute', bottom: -2, left: -2,
                width: 8, height: 8, borderRadius: '50%',
                background: legalityColor,
                border: '1px solid #fff',
              }} title={
                legalityStatus === 'Legal' ? '合法' :
                legalityStatus === 'Fishy' ? '可疑' : '不合法'
              } />
            )}
          </div>
          <div style={{ fontSize: 10, lineHeight: 1.2, textAlign: 'center', maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {p!.nickname || p!.speciesName}
          </div>
          <Tag color="blue" style={{ fontSize: 9, margin: 0, padding: '0 3px', lineHeight: '14px' }}>Lv.{p!.level}</Tag>
        </>
      )}
    </div>
  );
};

// ── Draggable Bank Item ──────────────────────────────
const DraggableBankItem: React.FC<{ pokemon: BankListItem; spriteStyle?: SpriteStyle }> = ({ pokemon, spriteStyle }) => {
  const id = bankItemId(pokemon.id);
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id });

  return (
    <div
      ref={setNodeRef}
      {...attributes}
      {...listeners}
      style={{
        width: 64, textAlign: 'center', cursor: 'grab', padding: 4, borderRadius: 6,
        border: pokemon.isShiny ? '2px solid #faad14' : '1px solid #e8e8e8',
        background: '#fff', opacity: isDragging ? 0.5 : 1, flexShrink: 0,
      }}
    >
      <PokemonSprite speciesId={pokemon.species} width={40} height={40}
        variant={spriteStyle}
      />
      <div style={{ fontSize: 10, lineHeight: 1.2 }}>{pokemon.nickname || pokemon.speciesName}</div>
      <Tag color="blue" style={{ fontSize: 9, margin: 0, padding: '0 4px', lineHeight: '16px' }}>Lv.{pokemon.level}</Tag>
    </div>
  );
};

// ── Droppable Bank Zone ──────────────────────────────
const DroppableBankZone: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { setNodeRef, isOver } = useDroppable({ id: bankDropId });

  return (
    <div
      ref={setNodeRef}
      style={{
        display: 'flex', gap: 8, overflow: 'auto', padding: 8, minHeight: 64,
        border: isOver ? '2px solid #52c41a' : '2px dashed #d9d9d9',
        borderRadius: 8, background: isOver ? '#f6ffed' : '#fafafa',
        transition: 'border-color 0.2s, background 0.2s',
      }}
    >
      {children}
    </div>
  );
};

// ── Main Editor Page ─────────────────────────────────
const SaveEditor: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const { mode: themeMode, setMode: setThemeMode } = useTheme();
  const [spriteStyle, setSpriteStyle] = useState<SpriteStyle>(getStoredSpriteStyle);

  const [saveData, setSaveData] = useState<SaveFileDetail | null>(null);
  const [activeBox, setActiveBox] = useState(0);
  const [bankPokemon, setBankPokemon] = useState<BankListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [activeDrag, setActiveDrag] = useState<{ label: string } | null>(null);
  const [editingPokemon, setEditingPokemon] = useState<PokemonDto | null>(null);
  const [editingBoxIndex, setEditingBoxIndex] = useState<number | undefined>();
  const [editingSlotIndex, setEditingSlotIndex] = useState<number | undefined>();
  const [editingIsParty, setEditingIsParty] = useState(false);
  const [editPanelOpen, setEditPanelOpen] = useState(false);
  const [legalityScanning, setLegalityScanning] = useState(false);
  const [sortingCurrentBox, setSortingCurrentBox] = useState(false);
  const [sortingBoxes, setSortingBoxes] = useState(false);
  const [legalityMap, setLegalityMap] = useState<Record<string, LegalityStatus>>({});
  const [allBoxesOpen, setAllBoxesOpen] = useState(false);
  const [activeTab, setActiveTab] = useState('boxes');

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const fetchData = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const [saveRes, bankRes] = await Promise.all([
        saveFileApi.getDetail(id),
        bankApi.list({ pageSize: 50 }),
      ]);
      setSaveData(saveRes.data);
      setBankPokemon(bankRes.data.items);
    } catch {
      message.error('加载存档数据失败');
    } finally {
      setLoading(false);
    }
  }, [id, message]);

  useEffect(() => { fetchData(); }, [fetchData]);

  // Keyboard: Left/Right arrow keys to navigate boxes
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
      if (editPanelOpen) return; // Don't navigate while editing
      if (e.key === 'ArrowLeft') setActiveBox(a => Math.max(0, a - 1));
      else if (e.key === 'ArrowRight') setActiveBox(a => Math.min((saveData?.boxes.length || 1) - 1, a + 1));
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [saveData?.boxes.length, editPanelOpen]);

  // After saveData refreshes, update editingPokemon if the panel is open
  useEffect(() => {
    if (!editPanelOpen || !editingPokemon || !saveData) return;
    const species = editingPokemon.species;
    // Check party
    for (const s of saveData.party) {
      if (s.pokemon && s.pokemon.species === species && s.slotIndex === editingSlotIndex) {
        setEditingPokemon(s.pokemon);
        return;
      }
    }
    // Check boxes
    for (const box of saveData.boxes) {
      for (const s of box.slots) {
        if (s.pokemon && s.pokemon.id === editingPokemon.id && editingPokemon.id) {
          setEditingPokemon(s.pokemon);
          return;
        }
      }
    }
  }, [saveData]);

  const handleDragStart = (event: DragStartEvent) => {
    const activeId = String(event.active.id);
    if (activeId.startsWith('save:')) {
      const { boxIndex, slotIndex } = parseSaveSlot(activeId);
      const slot = saveData?.boxes[boxIndex]?.slots[slotIndex];
      setActiveDrag({ label: slot?.pokemon?.nickname || slot?.pokemon?.speciesName || '宝可梦' });
    } else if (activeId.startsWith('bank:')) {
      const bankId = activeId.replace('bank:', '');
      const item = bankPokemon.find(p => p.id === bankId);
      setActiveDrag({ label: item?.nickname || item?.speciesName || '宝可梦' });
    }
  };

  const handleDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveDrag(null);
    if (!over || !id) return;

    const fromId = String(active.id);
    const toId = String(over.id);

    // Save → Bank
    if (fromId.startsWith('save:') && toId === bankDropId) {
      const { boxIndex, slotIndex } = parseSaveSlot(fromId);
      try {
        await bankApi.fromSave({ saveFileId: id, boxIndex, slotIndex });
        message.success('已存入银行');
        fetchData();
      } catch { message.error('操作失败'); }
      return;
    }

    // Bank → Save
    if (fromId.startsWith('bank:') && toId.startsWith('save:')) {
      const bankPokemonId = fromId.replace('bank:', '');
      const { boxIndex, slotIndex } = parseSaveSlot(toId);
      try {
        await bankApi.moveToSave(id, { bankPokemonId, targetBoxIndex: boxIndex, targetSlotIndex: slotIndex });
        message.success('已移入存档');
        fetchData();
      } catch { message.error('操作失败'); }
      return;
    }

    // Save → Save (internal move)
    if (fromId.startsWith('save:') && toId.startsWith('save:')) {
      const from = parseSaveSlot(fromId);
      const to = parseSaveSlot(toId);
      if (from.boxIndex === to.boxIndex && from.slotIndex === to.slotIndex) return;
      try {
        await saveFileApi.moveSlot(id, {
          fromBoxIndex: from.boxIndex, fromSlotIndex: from.slotIndex,
          toBoxIndex: to.boxIndex, toSlotIndex: to.slotIndex,
        });
        message.success('移动成功');
        fetchData();
      } catch { message.error('移动失败'); }
    }
  };

  const handleSave = async () => { if (!id) return; try { await saveFileApi.save(id); message.success('已创建备份'); } catch { message.error('创建备份失败'); } };
  const handleDownload = async () => {
    if (!id) return;
    try {
      const res = await saveFileApi.download(id);
      const blob = res.data as Blob;
      const fileName = getDownloadFileName(
        res.headers['content-disposition'],
        saveData?.filename || `save_${id}.sav`,
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      message.success(`已导出存档：${fileName}`);
    } catch {
      message.error('导出失败');
    }
  };
  const handleBatchLegalityScan = async () => {
    if (!id) return;
    setLegalityScanning(true);
    try {
      const res = await saveFileApi.batchLegalityReport(id);
      // Build legality map: slotId → status
      const map: Record<string, LegalityStatus> = {};
      for (const s of res.data.slots) {
        const slotKey = s.isParty ? `party-${s.slotIndex}` : `box-${s.boxIndex}-${s.slotIndex}`;
        map[slotKey] = s.status;
      }
      setLegalityMap(map);
      message.success(`扫描完成: ${res.data.total}只, ${res.data.legalCount}合法, ${res.data.fishyCount}可疑, ${res.data.illegalCount}不合法`);
    } catch { message.error('扫描失败'); }
    finally { setLegalityScanning(false); }
  };
  const handleSortBoxes = async (sortBy: SaveBoxSortBy) => {
    if (!id || sortingBoxes) return;
    if (editPanelOpen) {
      message.warning('请先关闭编辑面板后再排序');
      return;
    }

    setSortingBoxes(true);
    try {
      await saveFileApi.sortBoxes(id, sortBy);
      setLegalityMap({});
      await fetchData();
      message.success(`已按${BOX_SORT_LABELS[sortBy]}完成箱子排序`);
    } catch (err: any) {
      message.error(err?.response?.data?.message || '排序失败');
    } finally {
      setSortingBoxes(false);
    }
  };
  const handleSortCurrentBox = async (sortBy: SaveBoxSortBy) => {
    if (!id || sortingCurrentBox || !currentBox) return;
    if (editPanelOpen) {
      message.warning('请先关闭编辑面板后再排序');
      return;
    }

    setSortingCurrentBox(true);
    try {
      await saveFileApi.sortBox(id, currentBox.boxIndex, sortBy);
      setLegalityMap({});
      await fetchData();
      message.success(`已按${BOX_SORT_LABELS[sortBy]}完成当前箱排序`);
    } catch (err: any) {
      message.error(err?.response?.data?.message || '排序失败');
    } finally {
      setSortingCurrentBox(false);
    }
  };
  const sortMenu: MenuProps = {
    items: BOX_SORT_MENU_ITEMS,
    onClick: ({ key }) => { void handleSortBoxes(key as SaveBoxSortBy); },
  };
  const currentBoxSortMenu: MenuProps = {
    items: BOX_SORT_MENU_ITEMS,
    onClick: ({ key }) => { void handleSortCurrentBox(key as SaveBoxSortBy); },
  };

  const tabItems = useMemo(() => {
    const items: Array<{ key: string; label: string }> = [
      { key: 'boxes', label: '📦 箱子' },
      { key: 'bag', label: '🎒 背包' },
      { key: 'trainer', label: '👤 训练家' },
      { key: 'pokedex', label: '📖 图鉴' },
    ];
    // Gen7: 支持具体版本 30-33，以及历史复合版本 71/72；排除 LGPE 42/43/73
    const isGen7SMUSUM = saveData?.gameVersion != null
      && [30, 31, 32, 33, 71, 72].includes(saveData.gameVersion);
    if (saveData?.generation === 3 || saveData?.generation === 6 || isGen7SMUSUM) {
      items.push({ key: 'gen-tools', label: '🔧 专用工具' });
    }
    return items;
  }, [saveData?.generation, saveData?.gameVersion]);

  const isGenToolsTab = activeTab === 'gen-tools';
  const isGenToolsSupported = saveData?.generation === 3 || saveData?.generation === 6
    || (saveData?.gameVersion != null && [30, 31, 32, 33, 71, 72].includes(saveData.gameVersion));
  const visibleActiveTab = isGenToolsTab && !isGenToolsSupported ? 'boxes' : activeTab;

  if (!isAuthenticated) return <div style={{ padding: 48, textAlign: 'center' }}>请先登录</div>;
  if (loading) return <div style={{ padding: 48, textAlign: 'center' }}><Spin size="large" /></div>;

  if (!saveData) return <div style={{ padding: 48, textAlign: 'center' }}><Title level={4}>存档不存在</Title><Button onClick={() => navigate('/saves')}>返回</Button></div>;

  const currentBox = saveData.boxes[activeBox];
  const boxList = saveData.boxes;

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
      <div style={{ minHeight: '100vh', background: 'var(--bg-body, #f5f5f5)' }}>
        {/* Toolbar */}
        <div style={{ background: 'var(--bg-toolbar, #fff)', padding: '8px 24px', display: 'flex', alignItems: 'center', gap: 12, borderBottom: '1px solid var(--border-color, #e8e8e8)' }}>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/saves')}>返回</Button>
          <Title level={5} style={{ margin: 0, flex: 1 }}>
            {saveData.filename}
            {saveData.isModified && <Tag color="orange" style={{ marginLeft: 8 }}>已修改</Tag>}
          </Title>
          <Space>
            <GameCover gameVersion={saveData.gameVersion} size="small" showPlatform={false}
              style={{ minWidth: 0, minHeight: 0, padding: 0 }} />
            <Text type="secondary">Gen{saveData.generation} | {saveData.gameVersionName}</Text>
          </Space>
          <Space>
            <Tooltip title="手动创建备份"><Button icon={<SaveOutlined />} onClick={handleSave}>备份</Button></Tooltip>
            <Tooltip title="导出下载"><Button icon={<DownloadOutlined />} onClick={handleDownload}>导出</Button></Tooltip>
          </Space>
          <Segmented
            size="small"
            options={[
              { value: 'light' as ThemeMode, icon: <SunOutlined /> },
              { value: 'dark' as ThemeMode, icon: <MoonOutlined /> },
              { value: 'system' as ThemeMode, icon: <DesktopOutlined /> },
            ]}
            value={themeMode}
            onChange={(v) => setThemeMode(v as ThemeMode)}
          />
          <Segmented
            size="small"
            options={[
              { value: 'game' as SpriteStyle, label: '🎮 Game' },
              { value: 'home' as SpriteStyle, label: '🏠 Home' },
            ]}
            value={spriteStyle}
            onChange={(v) => {
              const style = v as SpriteStyle;
              setSpriteStyle(style);
              localStorage.setItem('pkmanager_sprite_style', style);
            }}
          />
        </div>

        {/* Tab navigation — archive-level features */}
        <div style={{ background: 'var(--bg-toolbar, #fff)', padding: '0 24px', borderBottom: '1px solid var(--border-color, #e8e8e8)' }}>
          <Tabs
            activeKey={visibleActiveTab}
            onChange={setActiveTab}
            size="small"
            items={tabItems}
          />
        </div>

        {/* Content area — switches based on active tab */}
        {visibleActiveTab === 'boxes' && (
        <div style={{ padding: 12 }}>
          <div style={{
            display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap',
            background: 'var(--bg-surface, #fff)', borderRadius: 8, padding: '10px 12px',
            border: '1px solid var(--border-color, #e8e8e8)', marginBottom: 12,
          }}>
            <Text strong>箱子工具</Text>
            <Select size="small" value={activeBox} style={{ width: 240, maxWidth: '100%' }}
              onChange={setActiveBox}
              options={boxList.map(b => {
                const used = b.slots.filter(s => !s.isEmpty).length;
                return {
                  value: b.boxIndex,
                  label: `Box ${b.boxIndex + 1}: ${b.boxName} (${used}/${b.capacity})`,
                };
              })}
            />
            <div style={{ flex: 1 }} />
            <Tooltip title="扫描当前存档中所有箱子与队伍宝可梦的合法性">
              <Button icon={<SafetyCertificateOutlined />} onClick={handleBatchLegalityScan}
                loading={legalityScanning}>
                合法性扫描
              </Button>
            </Tooltip>
            <Dropdown menu={sortMenu} trigger={['click']} disabled={sortingBoxes || saveData.boxes.length === 0}>
              <Button icon={<SortAscendingOutlined />} loading={sortingBoxes}>全部排序</Button>
            </Dropdown>
          </div>

          <div style={{ display: 'flex', gap: 12, overflowX: 'auto' }}>
            {/* Box List Sidebar — height matches box grid; collapses on narrow screens */}
            <div style={{
              minWidth: 130, maxWidth: 150, flex: '0 1 150px', background: 'var(--bg-surface, #fff)', borderRadius: 8, padding: '8px 12px', flexShrink: 0,
              border: '1px solid var(--border-color, #e8e8e8)', alignSelf: 'flex-start',
              display: 'flex', flexDirection: 'column',
            }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6, flexShrink: 0 }}>
                <Text strong>箱子列表</Text>
                <Space size={2}>
                  <Button size="small" type="text" icon={<LeftOutlined />}
                    disabled={activeBox === 0}
                    onClick={() => setActiveBox(a => Math.max(0, a - 1))} />
                  <Button size="small" type="text" icon={<RightOutlined />}
                    disabled={activeBox >= boxList.length - 1}
                    onClick={() => setActiveBox(a => Math.min(boxList.length - 1, a + 1))} />
                </Space>
              </div>
              <div style={{ overflow: 'auto', maxHeight: 480 }}>
                {boxList.map(box => {
                  const count = box.slots.filter(s => !s.isEmpty).length;
                  return (
                    <div key={box.boxIndex} onClick={() => setActiveBox(box.boxIndex)}
                      style={{ padding: '6px 10px', borderRadius: 4, cursor: 'pointer', marginBottom: 2,
                        background: activeBox === box.boxIndex ? '#e6f4ff' : 'transparent',
                        border: activeBox === box.boxIndex ? '1px solid #1677ff' : '1px solid transparent' }}>
                      <Text style={{ fontSize: 12 }}>Box {box.boxIndex + 1}: {box.boxName}</Text>
                      <Text type="secondary" style={{ fontSize: 10, display: 'block' }}>{count}/{box.capacity}</Text>
                    </div>
                  );
                })}
              </div>
              <Button size="small" type="dashed" icon={<AppstoreOutlined />}
                style={{ marginTop: 8, flexShrink: 0 }}
                onClick={() => setAllBoxesOpen(true)}>
                全部箱子
              </Button>
            </div>

            {/* Box Grid */}
            <div style={{ flex: 1, alignSelf: 'flex-start', background: 'var(--bg-surface, #fff)', borderRadius: 8, padding: 16, border: '1px solid var(--border-color, #e8e8e8)' }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, marginBottom: 12 }}>
                <Text strong>{currentBox?.boxName || `Box ${activeBox + 1}`}</Text>
                <Tooltip title="只对当前箱子内部排序，空槽位会排到末尾">
                  <Dropdown menu={currentBoxSortMenu} trigger={['click']} disabled={sortingCurrentBox || !currentBox}>
                    <Button size="small" icon={<SortAscendingOutlined />} loading={sortingCurrentBox}>当前箱排序</Button>
                  </Dropdown>
                </Tooltip>
              </div>
              {currentBox && (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, 1fr)', gap: 'clamp(3px, 1vw, 8px)', maxWidth: 'clamp(300px, 100%, 600px)', overflowX: 'auto' }}>
                  {currentBox.slots.map(slot => {
                    const slotKey = `box-${activeBox}-${slot.slotIndex}`;
                    return (
                      <DraggableSlot key={slot.slotIndex} boxIndex={activeBox} slot={slot}
                        legalityStatus={legalityMap[slotKey]}
                        spriteStyle={spriteStyle}
                        onPokemonClick={(p) => { setEditingPokemon(p); setEditingBoxIndex(activeBox); setEditingSlotIndex(slot.slotIndex); setEditingIsParty(false); setEditPanelOpen(true); }} />
                    );
                  })}
                </div>
              )}
            </div>
          </div>

          {/* Party Pokémon (随行宝可梦) */}
          {saveData.party && saveData.party.length > 0 && (
            <div style={{ marginTop: 12, background: 'var(--bg-surface, #fff)', borderRadius: 8, padding: 16, border: '1px solid var(--border-color, #e8e8e8)' }}>
              <Text strong style={{ marginBottom: 8, display: 'block' }}>🎒 随行宝可梦</Text>
              <div style={{ display: 'flex', gap: 8, maxWidth: 600 }}>
                {saveData.party.map((slot: BoxSlotDto) => (
                  <div key={slot.slotIndex} style={{ flex: 1, maxWidth: 96 }}>
                    {slot.isEmpty ? (
                      <div style={{
                        aspectRatio: '1', border: '2px dashed #d9d9d9', borderRadius: 8,
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        background: '#fafafa', maxWidth: 80,
                      }}>
                        <Text type="secondary" style={{ fontSize: 11 }}>空</Text>
                      </div>
                    ) : (
                      <div style={{
                        aspectRatio: '1', border: slot.pokemon?.isShiny ? '2px solid #faad14' : '1px solid #e8e8e8',
                        borderRadius: 8, display: 'flex', flexDirection: 'column', cursor: 'pointer',
                        alignItems: 'center', justifyContent: 'center', background: '#fff', maxWidth: 80,
                      }} onClick={() => { if (slot.pokemon) { setEditingPokemon(slot.pokemon); setEditingBoxIndex(-1); setEditingSlotIndex(slot.slotIndex); setEditingIsParty(true); setEditPanelOpen(true); } }}>
                        <PokemonSprite speciesId={slot.pokemon!.species} width={32} height={32}
                          variant={spriteStyle}
                        />
                        <div style={{ fontSize: 10, lineHeight: 1.2, textAlign: 'center', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '100%' }}>
                          {slot.pokemon!.nickname || slot.pokemon!.speciesName}
                        </div>
                        <Tag color="blue" style={{ fontSize: 9, margin: 0, padding: '0 3px', lineHeight: '14px' }}>Lv.{slot.pokemon!.level}</Tag>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Bank Panel */}
          <div style={{ marginTop: 12, background: 'var(--bg-surface, #fff)', borderRadius: 8, padding: 16, border: '1px solid var(--border-color, #e8e8e8)', minHeight: 120 }}>
            <Text strong style={{ marginBottom: 8, display: 'block' }}><BankOutlined /> 我的银行</Text>
            <DroppableBankZone>
              {bankPokemon.length === 0 ? (
                <Text type="secondary" style={{ padding: 16 }}>拖拽宝可梦到这里存入银行</Text>
              ) : (
                bankPokemon.map(p => <DraggableBankItem key={p.id} pokemon={p} spriteStyle={spriteStyle} />)
              )}
            </DroppableBankZone>
          </div>

          {/* Backup Section */}
          <BackupSection saveFileId={id!} />
        </div>
        )}

        {visibleActiveTab === 'bag' && (
          <div style={{ padding: 12, background: 'var(--bg-surface, #fff)', borderRadius: 8, margin: 12, border: '1px solid var(--border-color, #e8e8e8)' }}>
            <BagPanel saveFileId={id!} />
          </div>
        )}

        {visibleActiveTab === 'trainer' && (
          <div style={{ padding: 12, background: 'var(--bg-surface, #fff)', borderRadius: 8, margin: 12, border: '1px solid var(--border-color, #e8e8e8)' }}>
            <TrainerPanel saveFileId={id!} />
          </div>
        )}

        {visibleActiveTab === 'pokedex' && (
          <div style={{ background: 'var(--bg-surface, #fff)', borderRadius: 8, margin: 12, border: '1px solid var(--border-color, #e8e8e8)', overflow: 'hidden' }}>
            <PokedexPanel saveFileId={id!} />
          </div>
        )}
        {visibleActiveTab === 'gen-tools' && (
          <div style={{ padding: 12 }}>
            <GenToolsPanel key={id} saveFileId={id!} />
          </div>
        )}
      </div>

      <DragOverlay>
        {activeDrag ? (
          <div style={{ background: '#fff', padding: '8px 12px', borderRadius: 8, boxShadow: '0 4px 12px rgba(0,0,0,0.15)', opacity: 0.85 }}>
            <Text>{activeDrag.label}</Text>
          </div>
        ) : null}
      </DragOverlay>

      <EditPanel
        open={editPanelOpen}
        pokemon={editingPokemon}
        generation={saveData.generation}
        saveFileId={id}
        boxIndex={editingBoxIndex}
        slotIndex={editingSlotIndex}
        isParty={editingIsParty}
        onClose={() => { setEditPanelOpen(false); setEditingPokemon(null); setEditingBoxIndex(undefined); setEditingSlotIndex(undefined); }}
        onSaved={fetchData}
      />

      <AllBoxesModal
        open={allBoxesOpen}
        onClose={() => setAllBoxesOpen(false)}
        boxes={saveData.boxes}
        legalityMap={legalityMap}
        activeBox={activeBox}
        saveFileId={id!}
        onSelectBox={(boxIdx) => setActiveBox(boxIdx)}
        onSwapped={fetchData}
        spriteStyle={spriteStyle}
      />
    </DndContext>
  );
};

// ── Backup Section ──────────────────────────────────
const BackupSection: React.FC<{ saveFileId: string }> = ({ saveFileId }) => {
  const [backups, setBackups] = useState<SaveBackupDto[]>([]);
  const [loading, setLoading] = useState<string | null>(null);
  const { message } = App.useApp();

  const loadBackups = async () => {
    try {
      const r = await saveFileApi.listBackups(saveFileId);
      setBackups(r.data || []);
    } catch (err: any) {
      useDiagnosticStore.getState().log({
        category: 'api', level: 'error',
        message: '加载备份列表失败',
        stack: err?.message,
      });
    }
  };
  useEffect(() => { loadBackups(); }, [saveFileId]);

  const handleRestore = async (backupId: string) => {
    setLoading(backupId);
    try {
      await saveFileApi.restoreBackup(saveFileId, backupId);
      message.success('已从备份恢复！页面将刷新');
      setTimeout(() => window.location.reload(), 800);
    } catch { message.error('恢复失败'); }
    finally { setLoading(null); }
  };

  if (backups.length === 0) return null;

  return (
    <div style={{ marginTop: 12, background: 'var(--bg-surface, #fff)', borderRadius: 8, padding: 16, border: '1px solid var(--border-color, #e8e8e8)' }}>
      <Text strong style={{ marginBottom: 10, display: 'block' }}>💾 存档备份 (最近5次)</Text>
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        {backups.map((b, i) => (
          <div key={b.id} style={{
            padding: '10px 14px', borderRadius: 8, border: '1px solid #e8e8e8',
            background: i === 0 ? '#f6ffed' : '#fafafa', minWidth: 200,
          }}>
            <div style={{ fontWeight: 600, fontSize: 13, marginBottom: 4 }}>
              {b.label || '备份'}
            </div>
            <div style={{ fontSize: 12, color: '#666', lineHeight: 1.6 }}>
              <div>🕐 {new Date(b.createdAt).toLocaleString('zh-CN')}</div>
              <div>🎮 {b.gameVersion || '—'}</div>
              <div>👤 {b.trainerName || '—'}</div>
              <div>📦 {b.pokemonCount} 只宝可梦 · {b.boxCount} 箱</div>
              <div>⏱ {b.playTime || '—'}</div>
            </div>
            <Popconfirm
              title="确定恢复到此备份？当前修改将丢失"
              onConfirm={() => handleRestore(b.id)}
              okText="恢复" cancelText="取消">
              <Button size="small" type="primary" danger
                loading={loading === b.id}
                style={{ marginTop: 8, width: '100%' }}>
                恢复此备份
              </Button>
            </Popconfirm>
          </div>
        ))}
      </div>
    </div>
  );
};

export default SaveEditor;
