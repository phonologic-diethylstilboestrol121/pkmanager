import React, { useEffect, useRef, useState } from 'react';
import {
  Drawer, Tabs, Button, App, Space, Tag, Modal, Select, Radio, Row, Col, Typography, Alert, Tooltip, Descriptions,
} from 'antd';
import { SaveOutlined, SendOutlined, ExperimentOutlined } from '@ant-design/icons';
import type { PokemonDto, LegalityStatus, JudgementDto, EditResultDto, LegalityReportDto, SaveFileInfo, SaveFileDetail } from '../../api/saveFile';
import { saveFileApi } from '../../api/saveFile';
import { bankApi } from '../../api/bank';
import { useResourceStore } from '../../stores/resourceStore';
import { getPokemonSpriteUrl, getPokeApiSpriteUrl, getPokeApiArtworkUrl } from '../../lib/spriteUrl';
import { buildEditRequest, validateFields } from '../editor/editHelpers';
import MainTab from '../editor/MainTab';
import MetTab from '../editor/MetTab';
import StatsTab from '../editor/StatsTab';
import MovesTab from '../editor/MovesTab';
import LegalityTab from '../editor/LegalityTab';
import OTMiscTab from '../editor/OTMiscTab';
import CosmeticTab from '../editor/CosmeticTab';
import GenSpecificTab from '../editor/GenSpecificTab';

const { Text } = Typography;

const GENERATION_LABELS: Record<number, string> = {
  3: 'Gen3 (GBA)', 4: 'Gen4 (NDS)', 5: 'Gen5 (NDS)', 6: 'Gen6 (3DS)', 7: 'Gen7 (3DS)',
};

interface Props {
  open: boolean;
  pokemon: PokemonDto | null;
  bankId: string;
  onClose: () => void;
  onSaved: () => void;
}

const BankEditDrawer: React.FC<Props> = ({ open, pokemon, bankId, onClose, onSaved }) => {
  const [loading, setLoading] = useState(false);
  const [legality, setLegality] = useState<{
    status: LegalityStatus;
    report?: string;
    judgements: JudgementDto[];
  } | null>(null);

  const [, forceUpdate] = useState(0);
  const notifyChange = () => forceUpdate(n => n + 1);

  // Fallback stage tracker for artwork → local standard → remote standard → SVG
  const artFallbackStage = useRef(0);
  useEffect(() => {
    artFallbackStage.current = 0;
  }, [pokemon?.species]);

  // Move-to-save modal
  const [moveModalOpen, setMoveModalOpen] = useState(false);
  const [moveLoading, setMoveLoading] = useState(false);
  const [moveSaves, setMoveSaves] = useState<SaveFileInfo[]>([]);
  const [moveTargetSaveId, setMoveTargetSaveId] = useState<string | undefined>();
  const [moveTargetBox, setMoveTargetBox] = useState<number>(0);
  const [moveSaveDetail, setMoveSaveDetail] = useState<SaveFileDetail | null>(null);
  const [moveTargetSlot, setMoveTargetSlot] = useState<number | undefined>();

  const { loadAll } = useResourceStore();
  const { message } = App.useApp();

  useEffect(() => {
    if (open) loadAll();
  }, [open, loadAll]);

  useEffect(() => {
    if (open && pokemon) {
      setLegality(null); // Reset to "unverified"
    }
  }, [open, pokemon]);

  if (!pokemon) return null;

  const generation = pokemon.format || 0;
  const hasPkmData = !!pokemon.pkmDataBase64;

  // ── Save ──────────────────────────────────────────────

  const handleSave = async () => {
    if (!hasPkmData) { message.error('该记录缺少原始数据，无法编辑'); return; }

    const errors = validateFields(pokemon);
    if (errors.length > 0) { message.error(`字段校验失败: ${errors.join('; ')}`); return; }

    const editSnapshot = buildEditRequest(pokemon);
    setLoading(true);
    try {
      const res = await bankApi.saveEdit(bankId, editSnapshot);
      const result: EditResultDto = res.data;
      const updated = result.updatedPokemon;

      setLegality({
        status: result.status,
        report: result.report,
        judgements: result.judgements || [],
      });

      if (updated) {
        for (const key of Object.keys(updated)) {
          (pokemon as any)[key] = (updated as any)[key];
        }
      }
      notifyChange();
      if (result.status === 'Legal') {
        message.success('修改已保存！');
      } else {
        message.warning('已保存（⚠️ 宝可梦不合法）');
      }
      setTimeout(() => onSaved(), 200);
    } catch (err: any) {
      message.error(err?.response?.data?.message || '保存失败');
    } finally {
      setLoading(false);
    }
  };

  // ── Validate ──────────────────────────────────────────

  const handleValidate = async () => {
    if (!hasPkmData) { message.warning('该记录缺少原始数据，无法验证'); return; }
    const editSnapshot = buildEditRequest(pokemon);
    try {
      const res = await saveFileApi.validateById(bankId, editSnapshot);
      const report: LegalityReportDto = res.data;
      setLegality({
        status: report.status,
        report: report.report,
        judgements: report.judgements || [],
      });
      message.info('合法性验证完成');
    } catch {
      message.error('验证失败');
    }
  };

  // ── Move-to-save modal ────────────────────────────────

  const openMoveModal = async () => {
    try {
      const res = await saveFileApi.list();
      setMoveSaves(res.data || []);
      setMoveTargetSaveId(undefined);
      setMoveTargetBox(0);
      setMoveSaveDetail(null);
      setMoveTargetSlot(undefined);
      setMoveModalOpen(true);
    } catch {
      message.error('加载存档列表失败');
    }
  };

  const handleMoveSaveSelected = async (saveFileId: string) => {
    setMoveTargetSaveId(saveFileId);
    setMoveTargetBox(0);
    setMoveSaveDetail(null);
    try {
      const res = await saveFileApi.getDetail(saveFileId);
      setMoveSaveDetail(res.data);
    } catch {
      message.error('加载存档详情失败');
    }
  };

  const handleMoveToSave = async () => {
    if (!moveTargetSaveId) return;
    setMoveLoading(true);
    try {
      await bankApi.sendToSave(bankId, {
        saveFileId: moveTargetSaveId,
        targetBoxIndex: moveTargetBox,
        targetSlotIndex: moveTargetSlot,
      });
      message.success('已发送到存档！');
      setMoveModalOpen(false);
      onSaved();
      onClose();
    } catch (err: any) {
      message.error(err?.response?.data?.message || '移动失败');
    } finally {
      setMoveLoading(false);
    }
  };

  // ── Tab definitions ───────────────────────────────────

  const tabItems = [
    { key: 'main', label: '基本信息', children: <MainTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    { key: 'stats', label: '能力值', children: <StatsTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    { key: 'moves', label: '招式', children: <MovesTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    { key: 'met', label: '相遇信息', children: <MetTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    { key: 'otmisc', label: '训练家/杂项', children: <OTMiscTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    { key: 'cosmetic', label: '外观/装饰', children: <CosmeticTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    { key: 'genspecific', label: '世代专属', children: <GenSpecificTab pokemon={pokemon} generation={generation} onChange={notifyChange} /> },
    {
      key: 'legality',
      label: '合法性',
      children: legality === null ? (
        <div style={{ textAlign: 'center', padding: 48 }}>
          <Text type="secondary" style={{ fontSize: 16 }}>尚未验证合法性</Text>
          <br />
          <Button
            type="primary"
            icon={<ExperimentOutlined />}
            onClick={handleValidate}
            style={{ marginTop: 16 }}
            disabled={!hasPkmData}
          >
            验证合法性
          </Button>
        </div>
      ) : (
        <LegalityTab
          status={legality.status}
          report={legality.report}
          judgements={legality.judgements}
          onValidate={handleValidate}
          pkmDataBase64={pokemon.pkmDataBase64}
        />
      ),
    },
  ];

  const legalityChip =
    legality === null ? <Tag>未验证</Tag>
    : legality.status === 'Legal' ? <Tag color="success">✓ 合法</Tag>
    : legality.status === 'Fishy' ? <Tag color="warning">⚠ 可疑</Tag>
    : <Tag color="error">✗ 不合法</Tag>;

  // ── Render ────────────────────────────────────────────

  return (
    <>
      <Drawer
        title={
          <Space wrap>
            <img
              src={getPokeApiArtworkUrl(pokemon.species)}
              alt={pokemon.speciesName}
              style={{ width: 40, height: 40, objectFit: 'contain' }}
              onError={(e) => {
                const img = e.target as HTMLImageElement;
                artFallbackStage.current += 1;
                if (artFallbackStage.current === 1) {
                  img.src = getPokemonSpriteUrl(pokemon.species);
                } else if (artFallbackStage.current === 2) {
                  img.src = getPokeApiSpriteUrl(pokemon.species);
                } else {
                  img.onerror = null; // Stage 3+ — 终止回退，杜绝死循环
                  img.src = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><rect fill="%23f0f0f0" width="40" height="40"/><text x="20" y="20" text-anchor="middle" dy=".3em" fill="%23999" font-size="7">PK</text></svg>');
                }
              }}
            />
            <span style={{ fontWeight: 600 }}>
              {pokemon.nickname || pokemon.speciesName} Lv.{pokemon.level}
            </span>
            {pokemon.isShiny && <Tag color="gold">✨ 闪光</Tag>}
            {pokemon.isEgg && <Tag>🥚 蛋</Tag>}
            {generation > 0 && <Tag color="blue">{GENERATION_LABELS[generation] || `PK${generation}`}</Tag>}
            {legalityChip}
          </Space>
        }
        open={open}
        onClose={onClose}
        size="large"
        extra={
          <Space>
            <Tooltip title={!hasPkmData ? '数据不完整，无法操作' : undefined}>
              <Button icon={<SendOutlined />} onClick={openMoveModal} disabled={!hasPkmData}>发送到存档</Button>
            </Tooltip>
            <Button onClick={onClose}>取消</Button>
            <Tooltip title={!hasPkmData ? '数据不完整，无法编辑' : undefined}>
              <Button type="primary" icon={<SaveOutlined />} loading={loading} onClick={handleSave} disabled={!hasPkmData}>
                保存修改
              </Button>
            </Tooltip>
          </Space>
        }
      >
        {!hasPkmData && (
          <Alert
            type="warning"
            message="该记录缺少原始数据，仅可查看"
            style={{ marginBottom: 16 }}
            showIcon
          />
        )}
        {!hasPkmData ? (
          <Descriptions column={2} bordered size="small">
            <Descriptions.Item label="物种">{pokemon.speciesName}</Descriptions.Item>
            <Descriptions.Item label="等级">Lv.{pokemon.level}</Descriptions.Item>
            {pokemon.nickname && <Descriptions.Item label="昵称">{pokemon.nickname}</Descriptions.Item>}
            <Descriptions.Item label="性格">{pokemon.natureName || '-'}</Descriptions.Item>
            <Descriptions.Item label="特性">{pokemon.abilityName || '-'}</Descriptions.Item>
            <Descriptions.Item label="闪光">{pokemon.isShiny ? <Tag color="gold">✨ 是</Tag> : '否'}</Descriptions.Item>
            <Descriptions.Item label="蛋">{pokemon.isEgg ? '是 🥚' : '否'}</Descriptions.Item>
            {pokemon.heldItemName && <Descriptions.Item label="持有物">{pokemon.heldItemName}</Descriptions.Item>}
            {pokemon.ballName && <Descriptions.Item label="球种">{pokemon.ballName}</Descriptions.Item>}
            <Descriptions.Item label="来源游戏">{pokemon.originGameName || '-'}</Descriptions.Item>
            <Descriptions.Item label="初训家">{pokemon.originalTrainerName || '-'}</Descriptions.Item>
            <Descriptions.Item label="TID">{pokemon.tid}</Descriptions.Item>
            <Descriptions.Item label="SID">{pokemon.sid}</Descriptions.Item>
          </Descriptions>
        ) : (
          <Tabs items={tabItems} defaultActiveKey="main" size="small" />
        )}
      </Drawer>

      {/* Move-to-save Modal */}
      <Modal
        title="发送到存档"
        open={moveModalOpen}
        onOk={handleMoveToSave}
        onCancel={() => setMoveModalOpen(false)}
        okText="发送"
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
            onChange={handleMoveSaveSelected}
            showSearch
            optionFilterProp="label"
            options={moveSaves.map(s => ({
              value: s.saveFileId,
              label: `${s.filename} (${s.trainerName || '?'} · ${s.gameVersionName || `Gen${s.generation}`} · ${s.pokemonCount}只)`,
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
                  return (
                    <Col span={12} key={i}>
                      <Radio value={i}>
                        {box.boxName || `箱子 ${i + 1}`}
                        <Text type="secondary" style={{ marginLeft: 8 }}>({used}/{capacity})</Text>
                      </Radio>
                    </Col>
                  );
                })}
              </Row>
            </Radio.Group>
          </div>
        )}

        {moveSaveDetail && generation > 0 && moveSaveDetail.generation !== generation && (
          <Alert
            type="info"
            message={`目标存档世代（Gen${moveSaveDetail.generation}）与宝可梦世代（Gen${generation}）不同，将自动进行兼容转换`}
            style={{ marginBottom: 12 }}
            showIcon
          />
        )}

        <Text type="secondary">将 "{pokemon.nickname || pokemon.speciesName}" 发送到目标箱子（自动填充空位）</Text>
      </Modal>
    </>
  );
};

export default BankEditDrawer;
