import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography, Card, Input, Select, Switch, Row, Col, Pagination,
  Tag, Empty, App, Button, Popconfirm, Space,
  Checkbox, Modal, Radio, Segmented,
} from 'antd';
import {
  SearchOutlined, DeleteOutlined, StarFilled, AppstoreOutlined,
  UnorderedListOutlined, ExportOutlined,
  SwapOutlined,
} from '@ant-design/icons';
import type { PokemonDto } from '../api/saveFile';
import { saveFileApi, type SaveFileInfo, type SaveFileDetail } from '../api/saveFile';
import { bankApi, type BankListItem } from '../api/bank';
import { useResourceStore } from '../stores/resourceStore';
import BankEditDrawer from '../components/bank/BankEditDrawer';
import PokemonSprite from '../components/PokemonSprite';
import { getStoredSpriteStyle, type SpriteStyle } from '../lib/spriteUrl';
import PageContainer from '../components/PageContainer';

const { Text } = Typography;

const GENERATION_OPTIONS = [
  { value: 3, label: 'Gen3 (GBA)' },
  { value: 4, label: 'Gen4 (NDS)' },
  { value: 5, label: 'Gen5 (NDS)' },
  { value: 6, label: 'Gen6 (3DS)' },
  { value: 7, label: 'Gen7 (3DS)' },
];

const SORT_OPTIONS = [
  { value: 'created_desc', label: '添加时间 ↓' },
  { value: 'level_desc', label: '等级 ↓' },
  { value: 'level_asc', label: '等级 ↑' },
  { value: 'species_desc', label: '物种编号 ↓' },
  { value: 'species_asc', label: '物种编号 ↑' },
];

const BankPage: React.FC = () => {
  const [pokemon, setPokemon] = useState<BankListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');
  const [selectedRowKeys, setSelectedRowKeys] = useState<string[]>([]);
  const [spriteStyle, setSpriteStyle] = useState<SpriteStyle>(getStoredSpriteStyle);

  // Filters
  const [generation, setGeneration] = useState<number | undefined>();
  const [isShiny, setIsShiny] = useState<boolean | undefined>();
  const [nature, setNature] = useState<number | undefined>();
  const [ability, setAbility] = useState<number | undefined>();
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState<string>('created_desc');
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);

  // Edit drawer (C.4 BankEditDrawer)
  const [editDrawerOpen, setEditDrawerOpen] = useState(false);
  const [editingPokemon, setEditingPokemon] = useState<PokemonDto | null>(null);
  const [editingBankId, setEditingBankId] = useState<string>('');

  // Move-to-save modal
  const [moveModalOpen, setMoveModalOpen] = useState(false);
  const [moveSaves, setMoveSaves] = useState<SaveFileInfo[]>([]);
  const [moveTargetSaveId, setMoveTargetSaveId] = useState<string | undefined>();
  const [moveTargetBox, setMoveTargetBox] = useState<number>(0);
  const [moveSaveDetail, setMoveSaveDetail] = useState<SaveFileDetail | null>(null);
  const [moveLoading, setMoveLoading] = useState(false);

  // Resources (natures / abilities)
  const { natures, abilities, loadAll } = useResourceStore();
  const natureOptions = natures.map(n => ({ value: n.id, label: n.name }));
  const abilityOptions = abilities.map(a => ({ value: a.id, label: a.name }));

  const { message } = App.useApp();

  // ── Data fetching ────────────────────────────────────

  const fetchBank = useCallback(async () => {
    setLoading(true);
    try {
      const sortParts = sortBy.split('_');
      const res = await bankApi.list({
        generation,
        isShiny,
        nature,
        ability,
        search: search || undefined,
        sortBy: sortParts[0],
        sortAsc: sortParts[1] === 'asc',
        page,
        pageSize,
      });
      setPokemon(res.data.items);
      setTotal(res.data.total);
    } catch {
      message.error('加载银行数据失败');
    } finally {
      setLoading(false);
    }
  }, [generation, isShiny, nature, ability, search, sortBy, page, pageSize, message]);

  // Load resources once on mount
  useEffect(() => { loadAll(); }, [loadAll]);

  // Fetch when filters change
  useEffect(() => { fetchBank(); }, [fetchBank]);

  // Clear selection when filters/page change
  useEffect(() => { setSelectedRowKeys([]); }, [generation, isShiny, nature, ability, search, sortBy, page]);

  // ── Single delete ────────────────────────────────────

  const handleDelete = async (id: string) => {
    try {
      await bankApi.delete(id);
      message.success('已从银行移除');
      setSelectedRowKeys(prev => prev.filter(k => k !== id));
      fetchBank();
    } catch {
      message.error('删除失败');
    }
  };

  // ── Batch delete ─────────────────────────────────────

  const handleBatchDelete = async () => {
    try {
      await bankApi.batchDelete(selectedRowKeys);
      message.success(`已删除 ${selectedRowKeys.length} 只宝可梦`);
      setSelectedRowKeys([]);
      fetchBank();
    } catch {
      message.error('批量删除失败');
    }
  };

  // ── Batch export ─────────────────────────────────────

  const handleBatchExport = async () => {
    try {
      const res = await bankApi.batchExport(selectedRowKeys);
      const blob = res.data as unknown as Blob;
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'pokemon_export.zip';
      a.click();
      URL.revokeObjectURL(url);
      message.success(`已导出`);
    } catch {
      message.error('导出失败');
    }
  };

  // ── Move-to-save modal ───────────────────────────────

  const openMoveModal = async () => {
    setMoveModalOpen(true);
    setMoveTargetSaveId(undefined);
    setMoveTargetBox(0);
    setMoveSaveDetail(null);
    try {
      const res = await saveFileApi.list();
      setMoveSaves(res.data || []);
    } catch {
      message.error('加载存档列表失败');
    }
  };

  const handleMoveSaveSelected = async (saveFileId: string) => {
    setMoveTargetSaveId(saveFileId);
    setMoveTargetBox(0);
    try {
      const res = await saveFileApi.getDetail(saveFileId);
      setMoveSaveDetail(res.data);
    } catch {
      message.error('加载存档详情失败');
    }
  };

  const handleBatchMoveToSave = async () => {
    if (!moveTargetSaveId) return;
    setMoveLoading(true);
    try {
      const res = await bankApi.batchMoveToSave({
        ids: selectedRowKeys,
        saveFileId: moveTargetSaveId,
        targetBoxIndex: moveTargetBox,
      });
      const { movedCount, failedCount } = res.data;
      if (failedCount > 0) {
        message.warning(`已移动 ${movedCount} 只，${failedCount} 只未移动`);
      } else {
        message.success(`已移动 ${movedCount} 只宝可梦到存档`);
      }
      setSelectedRowKeys([]);
      setMoveModalOpen(false);
      fetchBank();
    } catch (err: any) {
      const msg = err?.response?.data?.message || '移动失败';
      message.error(msg);
    } finally {
      setMoveLoading(false);
    }
  };

  // ── Detail ───────────────────────────────────────────

  const showDetail = async (p: BankListItem) => {
    try {
      const res = await bankApi.getDetail(p.id);
      setEditingPokemon(res.data);
      setEditingBankId(p.id);
      setEditDrawerOpen(true);
    } catch {
      message.error('加载详情失败');
    }
  };

  // ── Selection helpers ────────────────────────────────

  const isSelected = (id: string) => selectedRowKeys.includes(id);

  const toggleSelect = (id: string) => {
    setSelectedRowKeys(prev =>
      prev.includes(id) ? prev.filter(k => k !== id) : [...prev, id]
    );
  };

  // ── Rendering: Grid Card ─────────────────────────────

  const renderPokemonCard = (p: BankListItem) => {
    const selected = isSelected(p.id);
    return (
      <Card
        key={p.id}
        hoverable
        size="small"
        style={{
          width: 160,
          textAlign: 'center',
          border: selected ? '2px solid #1677ff' : p.isShiny ? '2px solid #faad14' : undefined,
          position: 'relative',
        }}
        onClick={() => showDetail(p)}
      >
        {/* Selection bar — top */}
        <div
          style={{
            position: 'absolute', top: 4, left: 8, zIndex: 2,
            opacity: selected ? 1 : undefined,
          }}
          className="bank-card-checkbox"
          onClick={(e) => e.stopPropagation()}
        >
          <Checkbox
            checked={selected}
            onChange={() => toggleSelect(p.id)}
            style={selected ? {} : { opacity: 0, transition: 'opacity 0.15s' }}
          />
        </div>

        {/* Sprite with overlay icons */}
        <div style={{ padding: '8px 0 0', position: 'relative', display: 'inline-block' }}>
          <PokemonSprite
            speciesId={p.species}
            alt={p.speciesName}
            variant={spriteStyle}
            width={80} height={80}
          />
          {/* Alpha badge — top-left */}
          {p.isAlpha && (
            <span style={{
              position: 'absolute', top: -4, left: -4,
              background: '#ff4d4f', color: '#fff', borderRadius: '50%',
              width: 14, height: 14, fontSize: 9, lineHeight: '14px', textAlign: 'center',
              border: '1px solid #fff',
            }} title="头目 (Alpha)">α</span>
          )}
          {/* Shiny star — top-right */}
          {p.isShiny && (
            <StarFilled style={{
              position: 'absolute', top: -4, right: -4,
              fontSize: 12, color: '#faad14', filter: 'drop-shadow(0 0 1px rgba(0,0,0,0.3))',
            }} title="闪光" />
          )}
          {/* Gmax badge — bottom-right */}
          {p.canGigantamax && (
            <span style={{
              position: 'absolute', bottom: -4, right: -4,
              background: '#fa541c', color: '#fff', borderRadius: '50%',
              width: 14, height: 14, fontSize: 10, lineHeight: '14px', textAlign: 'center',
              border: '1px solid #fff',
            }} title="超极巨化">G</span>
          )}
        </div>

        {/* Info */}
        <div style={{ marginTop: 4, marginBottom: 2 }}>
          <Text strong>{p.nickname || p.speciesName}</Text>
        </div>
        {p.nickname && (
          <div style={{ fontSize: 11, color: '#999', marginBottom: 2 }}>
            {p.speciesName}
          </div>
        )}
        <div>
          <Tag color="blue">Lv.{p.level}</Tag>
          {p.heldItemName && <Tag color="purple">{p.heldItemName}</Tag>}
        </div>
        <div style={{ marginTop: 2 }}>
          {p.isShiny && <Tag color="gold">闪光</Tag>}
          <Tag>{GENERATION_OPTIONS.find(g => g.value === p.generation)?.label || `Gen${p.generation}`}</Tag>
        </div>
      </Card>
    );
  };

  // ── Rendering: List Item ─────────────────────────────

  const renderListView = () => {
    const items = pokemon.map(p => {
      const selected = isSelected(p.id);
      return (
        <Card
          key={p.id}
          hoverable
          size="small"
          style={{
            marginBottom: 8,
            border: selected ? '2px solid #1677ff' : p.isShiny ? '1px solid #faad14' : undefined,
          }}
          onClick={() => showDetail(p)}
        >
          <Row align="middle" gutter={16}>
            {/* Checkbox */}
            <Col flex="32px" onClick={(e) => e.stopPropagation()}>
              <Checkbox checked={selected} onChange={() => toggleSelect(p.id)} />
            </Col>
            {/* Sprite */}
            <Col flex="52px">
              <div style={{ position: 'relative', display: 'inline-block' }}>
                <PokemonSprite
                  speciesId={p.species}
                  alt={p.speciesName}
                  variant={spriteStyle}
                  width={48} height={48}
                />
                {p.isAlpha && (
                  <span style={{
                    position: 'absolute', top: -3, left: -3,
                    background: '#ff4d4f', color: '#fff', borderRadius: '50%',
                    width: 12, height: 12, fontSize: 8, lineHeight: '12px', textAlign: 'center',
                    border: '1px solid #fff',
                  }}>α</span>
                )}
                {p.isShiny && (
                  <StarFilled style={{
                    position: 'absolute', top: -3, right: -3,
                    fontSize: 10, color: '#faad14',
                  }} />
                )}
                {p.canGigantamax && (
                  <span style={{
                    position: 'absolute', bottom: -3, right: -3,
                    background: '#fa541c', color: '#fff', borderRadius: '50%',
                    width: 12, height: 12, fontSize: 8, lineHeight: '12px', textAlign: 'center',
                    border: '1px solid #fff',
                  }}>G</span>
                )}
              </div>
            </Col>
            {/* Info */}
            <Col flex="auto">
              <Text strong>{p.nickname || p.speciesName}</Text>
              {p.nickname && <Text type="secondary" style={{ marginLeft: 8 }}>({p.speciesName})</Text>}
              <div>
                <Tag color="blue">Lv.{p.level}</Tag>
                {p.natureName && <Tag>{p.natureName}</Tag>}
                {p.heldItemName && <Tag color="purple">{p.heldItemName}</Tag>}
                {p.isShiny && <Tag color="gold">✨ 闪光</Tag>}
                <Tag>{GENERATION_OPTIONS.find(g => g.value === p.generation)?.label || `Gen${p.generation}`}</Tag>
              </div>
            </Col>
            {/* Delete */}
            <Col>
              <Popconfirm
                title="确定从银行移除此宝可梦？"
                onConfirm={(e) => { e?.stopPropagation(); handleDelete(p.id); }}
                onCancel={(e) => e?.stopPropagation()}
                okText="确定"
                cancelText="取消"
              >
                <Button
                  type="text"
                  danger
                  icon={<DeleteOutlined />}
                  onClick={(e) => e.stopPropagation()}
                />
              </Popconfirm>
            </Col>
          </Row>
        </Card>
      );
    });
    return <div>{items}</div>;
  };

  // ── Main render ──────────────────────────────────────

  return (
    <PageContainer
      title="我的宝可梦银行"
      backTo="/dashboard"
      maxWidth={1200}
      extra={
        <Space size={12}>
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
          <Button
            icon={<AppstoreOutlined />}
            type={viewMode === 'grid' ? 'primary' : 'default'}
            onClick={() => setViewMode('grid')}
          />
          <Button
            icon={<UnorderedListOutlined />}
            type={viewMode === 'list' ? 'primary' : 'default'}
            onClick={() => setViewMode('list')}
          />
        </Space>
      }
    >

      {/* Filter Bar */}
      <Card size="small" style={{ marginBottom: 16 }}>
        <Row gutter={[16, 12]} align="middle">
          <Col>
            <Select
              placeholder="世代筛选"
              allowClear
              style={{ width: 140 }}
              value={generation}
              onChange={(val) => { setGeneration(val); setPage(1); }}
              options={GENERATION_OPTIONS}
            />
          </Col>
          <Col>
            <Space>
              <Text>闪光</Text>
              <Switch
                checked={isShiny}
                onChange={(val) => { setIsShiny(val || undefined); setPage(1); }}
              />
            </Space>
          </Col>
          <Col>
            <Select
              placeholder="性格"
              allowClear
              showSearch
              style={{ width: 120 }}
              value={nature}
              onChange={(val) => { setNature(val); setPage(1); }}
              options={natureOptions}
              filterOption={(input, option) =>
                (option?.label as string)?.includes(input) ?? false
              }
            />
          </Col>
          <Col>
            <Select
              placeholder="特性"
              allowClear
              showSearch
              style={{ width: 140 }}
              value={ability}
              onChange={(val) => { setAbility(val); setPage(1); }}
              options={abilityOptions}
              filterOption={(input, option) =>
                (option?.label as string)?.includes(input) ?? false
              }
            />
          </Col>
          <Col flex="auto">
            <Input
              placeholder="搜索名称或昵称..."
              prefix={<SearchOutlined />}
              allowClear
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              style={{ maxWidth: 300 }}
            />
          </Col>
          <Col>
            <Select
              style={{ width: 140 }}
              value={sortBy}
              onChange={(val) => { setSortBy(val); setPage(1); }}
              options={SORT_OPTIONS}
            />
          </Col>
          <Col>
            <Text type="secondary">共 {total} 只</Text>
          </Col>
        </Row>
      </Card>

      {/* Batch action bar */}
      {selectedRowKeys.length > 0 && (
        <Card size="small" style={{ marginBottom: 16, background: '#e6f4ff', border: '1px solid #91caff' }}>
          <Row align="middle" justify="space-between">
            <Col>
              <Text strong>已选 {selectedRowKeys.length} 只</Text>
            </Col>
            <Col>
              <Space>
                <Popconfirm
                  title={`确定删除选中的 ${selectedRowKeys.length} 只宝可梦？`}
                  onConfirm={handleBatchDelete}
                  okText="确定"
                  cancelText="取消"
                >
                  <Button danger icon={<DeleteOutlined />}>删除选中</Button>
                </Popconfirm>
                <Button icon={<ExportOutlined />} onClick={handleBatchExport}>导出 .zip</Button>
                <Button icon={<SwapOutlined />} onClick={openMoveModal}>移动到存档</Button>
              </Space>
            </Col>
          </Row>
        </Card>
      )}

      {/* Content */}
      {loading ? (
        <Card style={{ textAlign: 'center', padding: 48 }}>加载中...</Card>
      ) : pokemon.length === 0 ? (
        <Card>
          <Empty description="银行中还没有宝可梦" />
        </Card>
      ) : viewMode === 'grid' ? (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, marginBottom: 24 }}>
          {pokemon.map(renderPokemonCard)}
        </div>
      ) : (
        <div style={{ marginBottom: 24 }}>
          {renderListView()}
        </div>
      )}

      {/* Pagination */}
      {total > pageSize && (
        <div style={{ textAlign: 'center' }}>
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            onChange={setPage}
            showSizeChanger={false}
            showTotal={(t) => `共 ${t} 只`}
          />
        </div>
      )}

      {/* C.4 Bank Edit Drawer */}
      <BankEditDrawer
        open={editDrawerOpen}
        pokemon={editingPokemon}
        bankId={editingBankId}
        onClose={() => { setEditDrawerOpen(false); setEditingPokemon(null); setEditingBankId(''); }}
        onSaved={fetchBank}
      />

      {/* Batch Move-to-save Modal */}
      <Modal
        title="移动到存档"
        open={moveModalOpen}
        onOk={handleBatchMoveToSave}
        onCancel={() => setMoveModalOpen(false)}
        okText="移动"
        cancelText="取消"
        confirmLoading={moveLoading}
        okButtonProps={{ disabled: !moveTargetSaveId }}
      >
        <div style={{ marginBottom: 16 }}>
          <Text strong>1. 选择目标存档</Text>
          <Select
            placeholder="选择存档..."
            style={{ width: '100%', marginTop: 8 }}
            value={moveTargetSaveId}
            onChange={(val) => handleMoveSaveSelected(val)}
            options={moveSaves.map(s => ({
              value: s.saveFileId,
              label: `${s.filename} (${s.trainerName || '?'} · ${s.pokemonCount}只)`,
            }))}
          />
        </div>

        {moveSaveDetail && (
          <div style={{ marginBottom: 16 }}>
            <Text strong>2. 选择目标箱子</Text>
            <Radio.Group
              style={{ width: '100%', marginTop: 8 }}
              value={moveTargetBox}
              onChange={(e) => setMoveTargetBox(e.target.value)}
            >
              <Row gutter={[8, 8]}>
                {moveSaveDetail.boxes.map((box, i) => {
                  const used = box.slots.filter(s => !s.isEmpty).length;
                  const capacity = box.slots.length;
                  const available = capacity - used;
                  return (
                    <Col span={12} key={i}>
                      <Radio value={i}>
                        {box.boxName || `箱子 ${i + 1}`}
                        <Text type="secondary" style={{ marginLeft: 8 }}>
                          ({used}/{capacity})
                        </Text>
                        {available < selectedRowKeys.length && (
                          <Tag color="red" style={{ marginLeft: 4 }}>不足</Tag>
                        )}
                      </Radio>
                    </Col>
                  );
                })}
              </Row>
            </Radio.Group>
          </div>
        )}

        <div>
          <Text type="secondary">
            将移动 {selectedRowKeys.length} 只宝可梦到目标箱子（自动填充空位）
          </Text>
        </div>
      </Modal>
    </PageContainer>
  );
};

// ── CSS-in-JS for card hover checkbox ──────────────────

const style = document.createElement('style');
style.textContent = `
  .bank-card-checkbox .ant-checkbox-wrapper { opacity: 0; transition: opacity 0.15s; }
  .ant-card:hover .bank-card-checkbox .ant-checkbox-wrapper { opacity: 1 !important; }
`;
if (!document.querySelector('style[data-bank]')) {
  style.setAttribute('data-bank', '1');
  document.head.appendChild(style);
}

export default BankPage;
