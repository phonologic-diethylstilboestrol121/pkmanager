import React, { useEffect, useState } from 'react';
import {
  Drawer, Tabs, Button, App, Space,
} from 'antd';
import { SaveOutlined, BankOutlined, CopyOutlined, ImportOutlined } from '@ant-design/icons';
import type { PokemonDto, LegalityStatus, JudgementDto, EditResultDto, LegalityReportDto, AutoFixResultDto } from '../../api/saveFile';
import { saveFileApi } from '../../api/saveFile';
import { useResourceStore } from '../../stores/resourceStore';
import { buildEditRequest, validateFields } from './editHelpers';
import ShowdownImportModal from './ShowdownImportModal';
import MainTab from './MainTab';
import MetTab from './MetTab';
import StatsTab from './StatsTab';
import MovesTab from './MovesTab';
import LegalityTab from './LegalityTab';
import OTMiscTab from './OTMiscTab';
import CosmeticTab from './CosmeticTab';
import GenSpecificTab from './GenSpecificTab';

interface Props {
  open: boolean;
  pokemon: PokemonDto | null;
  generation: number;
  saveFileId?: string;
  boxIndex?: number;
  slotIndex?: number;
  isParty?: boolean;
  onClose: () => void;
  onSaved: () => void;
}

const EditPanel: React.FC<Props> = ({ open, pokemon, generation, saveFileId, boxIndex, slotIndex, isParty, onClose, onSaved }) => {
  const [loading, setLoading] = useState(false);
  const [legality, setLegality] = useState<{
    status: LegalityStatus;
    report?: string;
    judgements: JudgementDto[];
  } | null>(null);

  // Counter for regular field edits (triggers re-render only)
  const [, forceUpdate] = useState(0);
  const notifyChange = () => forceUpdate(n => n + 1);
  const [saveKey, setSaveKey] = useState(0);
  const [showImport, setShowImport] = useState(false);

  const { loadAll } = useResourceStore();
  const { message } = App.useApp();

  useEffect(() => {
    if (open) loadAll();
  }, [open, loadAll]);

  useEffect(() => {
    if (open && pokemon) {
      setLegality(null);
    }
  }, [open, pokemon]);

  if (!pokemon) return null;

  const handleSave = async () => {
    const b64 = pokemon.pkmDataBase64;
    if (!b64) { message.error('无法识别宝可梦数据'); return; }
    if (!saveFileId) { message.error('缺少存档ID'); return; }

    const errors = validateFields(pokemon);
    if (errors.length > 0) { message.error(`字段校验失败: ${errors.join('; ')}`); return; }

    const editSnapshot = buildEditRequest(pokemon);
    setLoading(true);
    try {
      const res = await saveFileApi.updateSaveSlot(
        b64, saveFileId, boxIndex ?? 0, slotIndex ?? 0, isParty ?? false, editSnapshot);
      const result: EditResultDto = res.data;

      // Always update pokemon with backend response (includes recalculated stats)
      const updated = result.updatedPokemon;

      setLegality({
        status: result.status,
        report: result.report,
        judgements: result.judgements,
      });

      // Update pokemon with backend-verified data immediately
      if (updated) {
        for (const key of Object.keys(updated)) {
          (pokemon as any)[key] = (updated as any)[key];
        }
      }
      notifyChange();
      if (result.isValid) {
        message.success('修改已保存！');
      } else {
        message.warning('已保存（⚠️ 宝可梦不合法）');
      }
      // Let parent reload in background (won't affect current display)
      setTimeout(() => onSaved(), 200);
      setSaveKey(k => k + 1);  // force remount Tabs to clean stale internal state
    } catch (err: any) {
      message.error(err?.response?.data?.message || '保存失败');
    } finally {
      setLoading(false);
    }
  };

  const handleFix = async (fixAction: string) => {
    const b64 = pokemon.pkmDataBase64;
    if (!b64) { message.error('缺少宝可梦数据'); return; }
    const editSnapshot = buildEditRequest(pokemon);
    setLoading(true);
    try {
      const res = await saveFileApi.autoFix({
        pkmDataBase64: b64,
        editSnapshot,
        fixActions: [fixAction],
        trainerSaveFileId: saveFileId ?? undefined,
      });
      const result: AutoFixResultDto = res.data;
      if (result.fixed && result.updatedPokemon) {
        Object.assign(pokemon, result.updatedPokemon);
        setLegality({
          status: result.status,
          report: result.report,
          judgements: result.judgements,
        });
        notifyChange();
        message.success(`修复完成: ${result.appliedFixes.join(', ')}`);
        if (result.failedFixes.length > 0) {
          message.warning(`部分修复失败: ${result.failedFixes.join(', ')}`);
        }
      } else {
        message.warning(result.failedFixes.length > 0
          ? `修复失败: ${result.failedFixes.join(', ')}`
          : '修复不适用');
      }
    } catch (err: any) {
      message.error(err?.response?.data?.message || '修复失败');
    } finally {
      setLoading(false);
    }
  };

  const handleValidate = async () => {
    const b64 = pokemon.pkmDataBase64;
    if (!b64) return;
    const editSnapshot = buildEditRequest(pokemon);
    try {
      const res = await saveFileApi.validatePokemon(b64, editSnapshot);
      const report: LegalityReportDto = res.data;
      setLegality({
        status: report.status,
        report: report.report,
        judgements: report.judgements,
      });
      message.info('合法性验证完成');
    } catch {
      message.error('验证失败');
    }
  };

  const tabItems = [
    {
      key: 'main',
      label: '基本信息',
      children: <MainTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'met',
      label: '相遇信息',
      children: <MetTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'stats',
      label: '能力值',
      children: <StatsTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'moves',
      label: '招式',
      children: <MovesTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'otmisc',
      label: '训练家/杂项',
      children: <OTMiscTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'cosmetic',
      label: '外观/装饰',
      children: <CosmeticTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'genspecific',
      label: '世代专属',
      children: <GenSpecificTab pokemon={pokemon} generation={generation} onChange={notifyChange} />,
    },
    {
      key: 'legality',
      label: '合法性',
      children: (
        <LegalityTab
          status={legality?.status || 'Legal'}
          report={legality?.report}
          judgements={legality?.judgements || []}
          onFix={legality ? handleFix : undefined}
          onValidate={handleValidate}
          pkmDataBase64={pokemon.pkmDataBase64}
        />
      ),
    },
  ];

  return (
    <Drawer
      title={
        <Space>
          <span style={{ fontWeight: 600 }}>
            {pokemon.nickname || pokemon.speciesName} Lv.{pokemon.level}
          </span>
          {pokemon.isShiny && <span style={{ color: '#faad14' }}>✨</span>}
          {pokemon.isEgg && <span>🥚</span>}
          <span style={{ fontSize: 10, color: '#bbb' }}>
            {!pokemon.id ? `随行#${slotIndex ?? '?'} 存档:${(saveFileId || '?').substring(0,8)}` : ''}
          </span>
        </Space>
      }
      open={open}
      onClose={onClose}
      size="large"
      extra={
        <Space>
          <Button icon={<CopyOutlined />} size="small">复制</Button>
          <Button icon={<BankOutlined />} size="small">存入银行</Button>
          <Button icon={<ImportOutlined />} size="small"
            onClick={() => setShowImport(true)}>Showdown</Button>
          <Button onClick={onClose}>取消</Button>
          <Button type="primary" icon={<SaveOutlined />} loading={loading} onClick={handleSave}>
            保存修改
          </Button>
        </Space>
      }
    >
      <Tabs key={`tabs-${saveKey}`} items={tabItems} defaultActiveKey="main" size="small" />
      <ShowdownImportModal
        open={showImport}
        saveFileId={saveFileId}
        onClose={() => setShowImport(false)}
        onImported={(imported) => {
          Object.assign(pokemon, imported);
          notifyChange();
          setLegality(null);
          setShowImport(false);
          message.success('Showdown 配置已导入，请检查并保存');
        }}
      />
    </Drawer>
  );
};

export default EditPanel;
