import React, { useEffect, useState } from 'react';
import {
  Drawer, Tabs, Button, App, Space,
} from 'antd';
import { SaveOutlined, BankOutlined, CopyOutlined } from '@ant-design/icons';
import type { PokemonDto, LegalityStatus, JudgementDto, EditResultDto, LegalityReportDto } from '../../api/saveFile';
import { saveFileApi } from '../../api/saveFile';
import { useResourceStore } from '../../stores/resourceStore';
import MainTab from './MainTab';
import MetTab from './MetTab';
import StatsTab from './StatsTab';
import MovesTab from './MovesTab';
import LegalityTab from './LegalityTab';
import OTMiscTab from './OTMiscTab';
import CosmeticTab from './CosmeticTab';

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
  // Separate counter for post-save remount (forces clean state)
  const [saveKey, setSaveKey] = useState(0);

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

  const handleFix = (fixAction: string) => {
    message.info(`修复功能将在下一版本实现: ${fixAction}`);
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
          <Button onClick={onClose}>取消</Button>
          <Button type="primary" icon={<SaveOutlined />} loading={loading} onClick={handleSave}>
            保存修改
          </Button>
        </Space>
      }
    >
      <Tabs key={`tabs-${saveKey}`} items={tabItems} defaultActiveKey="main" size="small" />
    </Drawer>
  );
};

/** Build a PokemonEditRequest from PokemonDto state */
/** Validate basic field ranges before save */
function validateFields(p: PokemonDto): string[] {
  const errs: string[] = [];
  if (p.species < 1 || p.species > 1025) errs.push('物种ID无效');
  if (p.level < 1 || p.level > 100) errs.push('等级需在1-100之间');
  if (p.gender < 0 || p.gender > 2) errs.push('性别无效');
  if (p.nature < 0 || p.nature > 24) errs.push('性格无效');
  if (p.form < 0 || p.form > 63) errs.push('形态无效');
  if ((p.nickname || '').length > 12) errs.push('昵称最多12个字符');
  if ((p.originalTrainerName || '').length > 12) errs.push('初训家名称最多12个字符');
  if ((p.handlingTrainerName || '').length > 12) errs.push('现持有人名称最多12个字符');

  const ivs = p.ivs || [];
  for (let i = 0; i < 6; i++) {
    if (ivs[i] < 0 || ivs[i] > 31) { errs.push(`个体值[${['HP','攻击','防御','特攻','特防','速度'][i]}]需在0-31范围`); break; }
  }

  const evs = p.evs || [];
  let evSum = 0;
  for (let i = 0; i < 6; i++) {
    if (evs[i] < 0 || evs[i] > 252) { errs.push('努力值需在0-252范围'); break; }
    evSum += evs[i];
  }
  if (evSum > 510) errs.push('努力值总和不可超过510');

  if (p.tid < 0 || p.tid > 65535) errs.push('表ID需在0-65535范围');
  if (p.sid < 0 || p.sid > 65535) errs.push('里ID需在0-65535范围');
  if (p.originalTrainerFriendship < 0 || p.originalTrainerFriendship > 255) errs.push('亲密度需在0-255范围');

  const moveIds = (p.moves || []).map(m => m.moveId).filter(id => id > 0);
  if (new Set(moveIds).size < moveIds.length) errs.push('招式不能重复');

  return errs;
}

function buildEditRequest(pokemon: PokemonDto): Record<string, unknown> {
  return {
    species: pokemon.species,
    nickname: pokemon.nickname || null,
    isNicknamed: pokemon.isNicknamed,
    gender: pokemon.gender,
    level: pokemon.level,
    nature: pokemon.nature,
    ability: pokemon.ability,
    heldItem: pokemon.heldItem,
    ball: pokemon.ball,
    isShiny: pokemon.isShiny,
    isEgg: pokemon.isEgg,
    form: pokemon.form,
    formArgument: pokemon.formArgument,
    language: pokemon.language,
    exp: pokemon.exp,
    friendship: pokemon.originalTrainerFriendship,
    handlingTrainerFriendship: pokemon.handlingTrainerFriendship,
    pokerusStrain: pokemon.pokerusStrain,
    pokerusDays: pokemon.pokerusDays,
    fatefulEncounter: pokemon.fatefulEncounter,
    heightScalar: pokemon.heightScalar,
    weightScalar: pokemon.weightScalar,
    scale: pokemon.scale,
    ivs: pokemon.ivs || [0,0,0,0,0,0],
    evs: pokemon.evs || [0,0,0,0,0,0],
    avs: pokemon.avs,
    gvs: pokemon.gvs,
    dynamaxLevel: pokemon.dynamaxLevel,
    canGigantamax: pokemon.canGigantamax,
    teraTypeOriginal: pokemon.teraTypeOriginal,
    teraTypeOverride: pokemon.teraTypeOverride,
    isAlpha: pokemon.isAlpha,
    isNoble: pokemon.isNoble,
    statNature: pokemon.statNature,
    moves: (pokemon.moves || []).map(m => m.moveId),
    movePPs: pokemon.movePPs || [0,0,0,0],
    movePPUps: pokemon.movePPUps || [0,0,0,0],
    relearnMoves: pokemon.relearnMoves,
    metLocation: pokemon.metLocation,
    metLevel: pokemon.metLevel,
    originGame: pokemon.originGame,
    metDate: pokemon.metDate,
    eggLocation: pokemon.eggLocation,
    eggDate: pokemon.eggDate,
    metTimeOfDay: pokemon.metTimeOfDay,
    groundTile: pokemon.groundTile,
    battleVersion: pokemon.battleVersion,
    obedienceLevel: pokemon.obedienceLevel,
    originalTrainerName: pokemon.originalTrainerName,
    originalTrainerGender: pokemon.originalTrainerGender,
    tid16: pokemon.tid,
    sid16: pokemon.sid,
    handlingTrainerName: pokemon.handlingTrainerName,
    handlingTrainerGender: pokemon.handlingTrainerGender,
    handlingTrainerLanguage: pokemon.handlingTrainerLanguage,
    affection: pokemon.affection,
    homeTracker: pokemon.homeTracker ? parseInt(pokemon.homeTracker, 16) : null,
    isFavorite: pokemon.isFavorite,
    country: pokemon.country,
    subRegion: pokemon.subRegion,
    consoleRegion: pokemon.consoleRegion,
    affixedRibbon: pokemon.affixedRibbon,
    markings: pokemon.markings,
    contestCool: pokemon.contestCool,
    contestBeauty: pokemon.contestBeauty,
    contestCute: pokemon.contestCute,
    contestSmart: pokemon.contestSmart,
    contestTough: pokemon.contestTough,
    contestSheen: pokemon.contestSheen,
  };
}

export default EditPanel;
