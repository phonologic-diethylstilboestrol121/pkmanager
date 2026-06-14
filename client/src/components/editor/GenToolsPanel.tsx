import React, { useState, useCallback, useEffect, useRef } from 'react';
import { Card, InputNumber, Button, App, Spin, Alert, Typography, Row, Col, Space, Checkbox, Tag, Divider, Progress, Statistic, List } from 'antd';
import { SaveOutlined, ClockCircleOutlined, ReloadOutlined, ThunderboltOutlined, AimOutlined, GiftOutlined, HomeOutlined, TrophyOutlined, BugOutlined, InfoCircleOutlined } from '@ant-design/icons';
import { saveFileApi, type GenToolsDto, type Rtc3EntryDto, type OPowerTypeEntryDto } from '../../api/saveFile';

const { Text, Title } = Typography;

interface Props {
  saveFileId: string;
}

const GenToolsPanel: React.FC<Props> = ({ saveFileId }) => {
  const [genTools, setGenTools] = useState<GenToolsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [saving, setSaving] = useState(false);
  const { message } = App.useApp();
  const mountedRef = useRef(true);
  const messageRef = useRef(message);
  const requestIdRef = useRef(0);

  useEffect(() => {
    mountedRef.current = true;
    messageRef.current = message;
    return () => {
      mountedRef.current = false;
    };
  }, [message]);

  const fetchGenTools = useCallback(async () => {
    const requestId = ++requestIdRef.current;
    setLoading(true);
    setError(false);
    try {
      const res = await saveFileApi.getGenTools(saveFileId);
      if (!mountedRef.current || requestId !== requestIdRef.current) return;
      setGenTools(res.data);
    } catch {
      if (!mountedRef.current || requestId !== requestIdRef.current) return;
      setError(true);
      messageRef.current.error('获取世代工具数据失败');
    } finally {
      if (mountedRef.current && requestId === requestIdRef.current) {
        setLoading(false);
      }
    }
  }, [saveFileId]);

  useEffect(() => {
    const timerId = window.setTimeout(() => {
      void fetchGenTools();
    }, 0);
    return () => window.clearTimeout(timerId);
  }, [fetchGenTools]);

  // ── RTC helpers ──────────────────────────────────────

  const handleRtcChange = (key: string, field: keyof Rtc3EntryDto, value: number | null) => {
    if (!genTools?.rtcEntries) return;
    setGenTools({
      ...genTools,
      rtcEntries: genTools.rtcEntries.map(e =>
        e.key === key ? { ...e, [field]: value ?? 0 } : e,
      ),
    });
  };

  // ── O-Power helpers ──────────────────────────────────

  const handleOPowerChange = (entryKey: string, field: keyof OPowerTypeEntryDto, value: unknown) => {
    if (!genTools?.opower?.entries) return;
    setGenTools({
      ...genTools,
      opower: {
        ...genTools.opower,
        entries: genTools.opower.entries.map(e =>
          e.key === entryKey ? { ...e, [field]: value } : e,
        ),
      },
    });
  };

  const handleOPowerTopChange = (field: 'points' | 'enableUnlocked' | 'fullRecoveryUnlocked', value: unknown) => {
    if (!genTools?.opower) return;
    setGenTools({
      ...genTools,
      opower: { ...genTools.opower, [field]: value },
    });
  };

  // ── Zygarde helpers ──────────────────────────────────

  const handleZygardeCellToggle = (index: number) => {
    if (!genTools?.zygarde?.cells) return;
    const newCells = genTools.zygarde.cells.map(c =>
      c.index === index ? { ...c, collected: !c.collected } : c
    );
    const newCollectedCount = newCells.filter(c => c.collected).length;
    setGenTools({
      ...genTools,
      zygarde: {
        ...genTools.zygarde,
        cells: newCells,
        collectedCount: newCollectedCount,
      },
    });
  };

  const handleZygardeMarkAll = () => {
    if (!genTools?.zygarde?.cells) return;
    setGenTools({
      ...genTools,
      zygarde: {
        ...genTools.zygarde,
        cells: genTools.zygarde.cells.map(c => ({ ...c, collected: true })),
        collectedCount: genTools.zygarde.cells.length,
      },
    });
  };

  const handleZygardeClearAll = () => {
    if (!genTools?.zygarde?.cells) return;
    setGenTools({
      ...genTools,
      zygarde: {
        ...genTools.zygarde,
        cells: genTools.zygarde.cells.map(c => ({ ...c, collected: false })),
        collectedCount: 0,
      },
    });
  };

  // ── Save ─────────────────────────────────────────────

  const handleSave = async () => {
    if (!genTools) return;
    setSaving(true);
    try {
      await saveFileApi.saveGenTools(saveFileId, genTools);
      message.success('专用工具设置已保存');
    } catch {
      message.error('保存失败');
    } finally {
      setSaving(false);
    }
  };

  // ── Render states ────────────────────────────────────

  if (loading) {
    return <div style={{ textAlign: 'center', padding: 60 }}><Spin size="large" /></div>;
  }

  if (error) {
    return (
      <div style={{ padding: 24 }}>
        <Alert
          type="error"
          message="加载失败"
          description="获取世代工具数据时发生错误，请检查网络连接后重试。"
          showIcon
          action={
            <Button size="small" icon={<ReloadOutlined />} onClick={fetchGenTools}>
              重试
            </Button>
          }
        />
      </div>
    );
  }

  const hasRtc = genTools?.capability.hasRtc ?? false;
  const hasOPowers = genTools?.capability.hasOPowers ?? false;
  const hasZygardeCells = genTools?.capability.hasZygardeCells ?? false;
  const hasEntreeForest = genTools?.capability.hasEntreeForest ?? false;
  const hasEntralink = genTools?.capability.hasEntralink ?? false;
  const hasCGearSkin = genTools?.capability.hasCGearSkin ?? false;

  if (!hasRtc && !hasOPowers && !hasZygardeCells && !hasEntreeForest && !hasEntralink && !hasCGearSkin) {
    return (
      <div style={{ padding: 24 }}>
        <Alert
          type="info"
          message="当前存档不支持专用工具功能"
          description="专用工具当前支持：Gen3 RTC、Gen5 Dream World / Entralink / C-Gear、Gen6 O-Power、Gen7 Zygarde Cell 及部分只读特色系统。"
          showIcon
        />
      </div>
    );
  }

  // ── RTC field defs ───────────────────────────────────

  const rtcFieldDefs: Array<{ field: keyof Rtc3EntryDto; label: string; min: number; max: number }> = [
    { field: 'day', label: '日', min: 0, max: 65535 },
    { field: 'hour', label: '时', min: 0, max: 23 },
    { field: 'minute', label: '分', min: 0, max: 59 },
    { field: 'second', label: '秒', min: 0, max: 59 },
  ];

  // ── O-Power grouped entries ──────────────────────────

  const oPower = genTools?.opower;
  const zygarde = genTools?.zygarde;
  const fieldEntries = oPower?.entries?.filter(e => e.category === 'field') ?? [];
  const battleEntries = oPower?.entries?.filter(e => e.category === 'battle') ?? [];

  const renderOPowerCard = (entry: OPowerTypeEntryDto) => (
    <Col xs={24} md={12} key={entry.key}>
      <Card
        size="small"
        title={
          <Space>
            <Text strong>{entry.name}</Text>
            <Tag color={entry.category === 'field' ? 'green' : 'red'}>
              {entry.category === 'field' ? 'Field' : 'Battle'}
            </Tag>
          </Space>
        }
        style={{ height: '100%' }}
      >
        {/* Level values */}
        <div style={{ display: 'flex', gap: 16, marginBottom: 10 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Text type="secondary" style={{ fontSize: 12, flexShrink: 0 }}>Lv.1</Text>
            <InputNumber
              value={entry.level1}
              onChange={v => handleOPowerChange(entry.key, 'level1', v ?? 0)}
              min={0} max={3} size="small" style={{ width: 60 }}
            />
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Text type="secondary" style={{ fontSize: 12, flexShrink: 0 }}>Lv.2</Text>
            <InputNumber
              value={entry.level2}
              onChange={v => handleOPowerChange(entry.key, 'level2', v ?? 0)}
              min={0} max={3} size="small" style={{ width: 60 }}
            />
          </div>
        </div>
        {/* Unlock flags */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
          <Checkbox
            checked={entry.level1Unlocked}
            onChange={e => handleOPowerChange(entry.key, 'level1Unlocked', e.target.checked)}
            style={{ fontSize: 12 }}
          >Lv.1</Checkbox>
          <Checkbox
            checked={entry.level2Unlocked}
            onChange={e => handleOPowerChange(entry.key, 'level2Unlocked', e.target.checked)}
            style={{ fontSize: 12 }}
          >Lv.2</Checkbox>
          <Checkbox
            checked={entry.level3Unlocked}
            onChange={e => handleOPowerChange(entry.key, 'level3Unlocked', e.target.checked)}
            style={{ fontSize: 12 }}
          >Lv.3</Checkbox>
          {entry.hasLevelS && (
            <Checkbox
              checked={entry.levelSUnlocked}
              onChange={e => handleOPowerChange(entry.key, 'levelSUnlocked', e.target.checked)}
              style={{ fontSize: 12 }}
            >S</Checkbox>
          )}
          {entry.hasLevelMax && (
            <Checkbox
              checked={entry.levelMaxUnlocked}
              onChange={e => handleOPowerChange(entry.key, 'levelMaxUnlocked', e.target.checked)}
              style={{ fontSize: 12 }}
            >MAX</Checkbox>
          )}
        </div>
      </Card>
    </Col>
  );

  return (
    <div>
      {/* ── RTC 时钟编辑器 ── */}
      {hasRtc && (
        <div style={{ marginBottom: 24 }}>
          <Title level={5} style={{ marginBottom: 12 }}>
            <ClockCircleOutlined style={{ marginRight: 6 }} />
            RTC 实时时钟
          </Title>
          <Row gutter={[16, 16]}>
            {genTools?.rtcEntries?.map(entry => (
              <Col xs={24} md={12} key={entry.key}>
                <Card
                  size="small"
                  title={<Text strong>{entry.label}</Text>}
                  style={{ height: '100%' }}
                >
                  <Space direction="vertical" style={{ width: '100%' }}>
                    {rtcFieldDefs.map(fd => (
                      <div key={fd.field} style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                        <Text type="secondary" style={{ width: 28, textAlign: 'right', flexShrink: 0 }}>
                          {fd.label}
                        </Text>
                        <InputNumber
                          value={entry[fd.field] as number}
                          onChange={v => handleRtcChange(entry.key, fd.field, v)}
                          min={fd.min}
                          max={fd.max}
                          style={{ flex: 1 }}
                          size="small"
                        />
                      </div>
                    ))}
                  </Space>
                </Card>
              </Col>
            ))}
          </Row>
          <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
            修改时钟可修复电池耗尽导致的树果不生长、潮汐洞穴不变化等问题。修改后建议在游戏中等待一天以触发时钟同步。
          </Text>
        </div>
      )}

      {/* ── O-Power 编辑器 ── */}
      {hasOPowers && oPower && (
        <div>
          {hasRtc && <Divider style={{ margin: '8px 0 20px' }} />}

          <Title level={5} style={{ marginBottom: 12 }}>
            <ThunderboltOutlined style={{ marginRight: 6 }} />
            O-Power
          </Title>

          {/* Top toolbar: Points + Enable + FullRecovery */}
          <Card size="small" style={{ marginBottom: 16 }}>
            <Row gutter={[24, 8]} align="middle">
              <Col>
                <Space>
                  <Text type="secondary">能量点数</Text>
                  <InputNumber
                    value={oPower.points}
                    onChange={v => handleOPowerTopChange('points', v ?? 0)}
                    min={0} max={255} size="small" style={{ width: 80 }}
                  />
                </Space>
              </Col>
              <Col>
                <Checkbox
                  checked={oPower.enableUnlocked}
                  onChange={e => handleOPowerTopChange('enableUnlocked', e.target.checked)}
                >
                  已启用
                </Checkbox>
              </Col>
              <Col>
                <Checkbox
                  checked={oPower.fullRecoveryUnlocked}
                  onChange={e => handleOPowerTopChange('fullRecoveryUnlocked', e.target.checked)}
                >
                  完全恢复已解锁
                </Checkbox>
              </Col>
            </Row>
          </Card>

          {/* Field O-Powers */}
          <Text strong style={{ display: 'block', marginBottom: 8, fontSize: 13 }}>
            <Tag color="green" style={{ marginRight: 6 }}>Field</Tag>
            野外人专用
          </Text>
          <Row gutter={[12, 12]} style={{ marginBottom: 20 }}>
            {fieldEntries.map(renderOPowerCard)}
          </Row>

          {/* Battle O-Powers */}
          <Text strong style={{ display: 'block', marginBottom: 8, fontSize: 13 }}>
            <Tag color="red" style={{ marginRight: 6 }}>Battle</Tag>
            对战用
          </Text>
          <Row gutter={[12, 12]}>
            {battleEntries.map(renderOPowerCard)}
          </Row>

          <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
            O-Power 是 Gen6 (X/Y/ΩR/αS) 的特色系统。修改等级和解锁标志后请在游戏中验证效果。
          </Text>
        </div>
      )}

      {/* ── Zygarde Cell 查看 ── */}
      {hasZygardeCells && zygarde && (
        <div>
          {(hasRtc || hasOPowers) && <Divider style={{ margin: '8px 0 20px' }} />}

          <Title level={5} style={{ marginBottom: 12 }}>
            <AimOutlined style={{ marginRight: 6 }} />
            Zygarde Cell / Core 收集进度
          </Title>

          {/* 进度概览卡片 */}
          <Card size="small" style={{ marginBottom: 16 }}>
            <Row gutter={[24, 8]} align="middle">
              <Col>
                <Text strong>收集进度: {zygarde.collectedCount} / {zygarde.totalCount}</Text>
              </Col>
              <Col flex="auto">
                <Progress
                  percent={Math.round((zygarde.collectedCount / zygarde.totalCount) * 100)}
                  size="small"
                  status={zygarde.collectedCount === zygarde.totalCount ? 'success' : 'active'}
                />
              </Col>
              <Col>
                <Space>
                  <Button size="small" onClick={handleZygardeMarkAll}>全部标记</Button>
                  <Button size="small" onClick={handleZygardeClearAll}>全部清除</Button>
                </Space>
              </Col>
            </Row>
          </Card>

          {/* Cell 网格 — 固定 10 列，窄屏横向滚动 */}
          <Card size="small" title="Cell 详情" style={{ overflowX: 'auto' }}>
            <div style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(10, 1fr)',
              gap: 4,
              width: 480,
              minWidth: 480,
            }}>
              {zygarde.cells.map(cell => (
                <div
                  key={cell.index}
                  onClick={() => handleZygardeCellToggle(cell.index)}
                  title={`Cell #${cell.index + 1} - ${cell.collected ? '已收集' : '未收集'}`}
                  style={{
                    aspectRatio: '1',
                    borderRadius: 6,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    cursor: 'pointer',
                    fontSize: 11,
                    fontWeight: 600,
                    color: cell.collected ? '#fff' : 'var(--color-text-secondary, #888)',
                    background: cell.collected ? '#52c41a' : '#f0f0f0',
                    border: cell.collected ? '2px solid #389e0d' : '2px solid #d9d9d9',
                    transition: 'all 0.15s',
                    userSelect: 'none',
                  } as React.CSSProperties}
                >
                  {cell.index + 1}
                </div>
              ))}
            </div>
            <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
              点击格子切换收集状态。绿色 = 已收集，灰色 = 未收集。
              Zygarde Cell 是 Gen7 (太阳/月亮/究极之日/究极之月) 的特色系统，共 {zygarde.totalCount} 个。
            </Text>
          </Card>
        </div>
      )}

      {/* ── Entree Forest (Gen5) — 只读 ── */}
      {genTools?.entreeForest && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>Entree Forest</Title>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={8}>
              <Card size="small">
                <Statistic title="占用槽位" value={genTools.entreeForest.occupiedSlots} suffix={`/ ${genTools.entreeForest.totalSlots}`} />
              </Card>
            </Col>
            <Col xs={12} sm={8}>
              <Card size="small">
                <Statistic title="第九区域" value={genTools.entreeForest.unlock9thArea ? '已解锁' : '未解锁'} />
              </Card>
            </Col>
            <Col xs={12} sm={8}>
              <Card size="small">
                <Statistic title="38 区域值" value={genTools.entreeForest.unlock38Areas} />
              </Card>
            </Col>
          </Row>
          <Card size="small" title="DW 宝可梦槽位" style={{ marginTop: 16 }}>
            <List
              size="small"
              dataSource={genTools.entreeForest.slots.filter(slot => slot.isOccupied)}
              renderItem={(slot) => (
                <List.Item>
                  <Space wrap>
                    <Text strong>槽位 #{slot.index + 1}</Text>
                    <Tag>Species {slot.species}</Tag>
                    <Tag>Move {slot.move}</Tag>
                    <Tag>Gender {slot.gender}</Tag>
                    <Tag>Form {slot.form}</Tag>
                    <Tag>Area {slot.area}</Tag>
                    {slot.isInvisible && <Tag color="default">Invisible</Tag>}
                  </Space>
                </List.Item>
              )}
            />
          </Card>
        </div>
      )}

      {/* ── Entralink (Gen5) — 只读 ── */}
      {genTools?.entralink && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells || !!genTools?.entreeForest) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>Entralink</Title>
          <Row gutter={[16, 16]}>
            <Col xs={12} sm={8}>
              <Card size="small">
                <Statistic title="White Forest Lv." value={genTools.entralink.whiteForestLevel} />
              </Card>
            </Col>
            <Col xs={12} sm={8}>
              <Card size="small">
                <Statistic title="Black City Lv." value={genTools.entralink.blackCityLevel} />
              </Card>
            </Col>
            {genTools.entralink.missionsComplete != null && (
              <Col xs={24} sm={8}>
                <Card size="small">
                  <Statistic title="Missions Complete" value={genTools.entralink.missionsComplete} />
                </Card>
              </Col>
            )}
          </Row>
          {(genTools.entralink.passPower1 != null || genTools.entralink.passPower2 != null || genTools.entralink.passPower3 != null) && (
            <Card size="small" title="Pass Powers" style={{ marginTop: 16 }}>
              <Space wrap>
                {genTools.entralink.passPower1 != null && <Tag>PassPower1: {genTools.entralink.passPower1}</Tag>}
                {genTools.entralink.passPower2 != null && <Tag>PassPower2: {genTools.entralink.passPower2}</Tag>}
                {genTools.entralink.passPower3 != null && <Tag>PassPower3: {genTools.entralink.passPower3}</Tag>}
              </Space>
            </Card>
          )}
        </div>
      )}

      {/* ── C-Gear Skin (Gen5) — 只读 ── */}
      {genTools?.cGearSkin && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells || !!genTools?.entreeForest || !!genTools?.entralink) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>C-Gear Skin</Title>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={8}>
              <Card size="small">
                <Statistic title="皮肤状态" value={genTools.cGearSkin.hasCGearSkin ? '已安装' : '未安装'} />
              </Card>
            </Col>
            <Col xs={12} sm={8}>
              <Card size="small">
                <Statistic title="Checksum" value={genTools.cGearSkin.checksum} />
              </Card>
            </Col>
            <Col xs={12} sm={8}>
              <Card size="small">
                <Statistic title="数据大小" value={genTools.cGearSkin.dataSize} suffix="bytes" />
              </Card>
            </Col>
          </Row>
        </div>
      )}

      {/* ── Holo Caster (Gen6) — 只读 ── */}
      {genTools?.holoCaster && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>
            <InfoCircleOutlined style={{ marginRight: 6 }} />
            Holo Caster
          </Title>
          <Alert
            type="info"
            showIcon
            message={genTools.holoCaster.dataPresent ? 'Holo Caster 数据区块已写入' : 'Holo Caster 数据区块可用'}
            description={
              genTools.holoCaster.dataPresent
                ? 'PKHeX 当前不支持解析 Holo Caster 数据结构（1604 字节原始数据块）。如需查看或编辑具体内容，请使用 PKHeX 桌面版。'
                : '当前存档属于支持 Holo Caster 的 Gen6 游戏，但该区块暂未发现非零内容。PKHeX 当前也不提供该结构的解析视图。'
            }
          />
        </div>
      )}

      {/* ── Festa (Gen7) — 只读 ── */}
      {genTools?.festa && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells || !!genTools?.holoCaster) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>
            <GiftOutlined style={{ marginRight: 6 }} />
            Festival Plaza
          </Title>
          <Row gutter={[16, 16]}>
            <Col xs={8}>
              <Card size="small">
                <Statistic title="当前硬币" value={genTools.festa.festaCoins} suffix={`/ ${9_999_999}`} />
              </Card>
            </Col>
            <Col xs={8}>
              <Card size="small">
                <Statistic title="累计硬币" value={genTools.festa.totalFestaCoins} />
              </Card>
            </Col>
            <Col xs={8}>
              <Card size="small">
                <Statistic title="广场等级" value={genTools.festa.festaRank} />
              </Card>
            </Col>
          </Row>
        </div>
      )}

      {/* ── Poké Pelago (Gen7) — 只读 ── */}
      {genTools?.pelago && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells || !!genTools?.holoCaster || !!genTools?.festa) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>
            <HomeOutlined style={{ marginRight: 6 }} />
            Poké Pelago
          </Title>
          {(() => {
            const pelago = genTools.pelago;
            return (
              <>
                <Row gutter={[16, 16]}>
                  <Col xs={24} sm={8}>
                    <Card size="small">
                      <Text type="secondary">宝可梦槽位</Text>
                      <Progress
                        percent={Math.round((pelago.occupiedSlots / pelago.totalSlots) * 100)}
                        format={() => `${pelago.occupiedSlots} / ${pelago.totalSlots}`}
                        style={{ marginBottom: 0 }}
                      />
                    </Card>
                  </Col>
                  <Col xs={24} sm={8}>
                    <Card size="small">
                      <Statistic title="访问次数" value={pelago.visits} />
                    </Card>
                  </Col>
                  <Col xs={24} sm={8}>
                    <Card size="small">
                      <Statistic title="孵蛋数" value={pelago.eggsHatched} />
                    </Card>
                  </Col>
                </Row>
                <Card size="small" title="豆子统计" style={{ marginTop: 16 }}>
                  <Space wrap size={[4, 4]}>
                    {pelago.beanCounts.map((count, i) => {
                      const beanLabels = ['红', '蓝', '浅蓝', '绿', '黄', '紫', '橙',
                        '红花纹', '蓝花纹', '浅蓝花纹', '绿花纹', '黄花纹', '紫花纹', '橙花纹', '彩虹'];
                      return (
                        <Tag key={i} color={count > 0 ? 'blue' : 'default'}>
                          {beanLabels[i] ?? `Bean ${i}`}: {count}
                        </Tag>
                      );
                    })}
                  </Space>
                </Card>
                <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
                  <Col xs={24}>
                    <Card size="small">
                      <Statistic title="寻宝次数" value={pelago.treasureHunts} />
                    </Card>
                  </Col>
                </Row>
                <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
                  Poké Pelago 是 Gen7 (太阳/月亮/究极之日/究极之月) 的特色系统。此处为只读展示。
                </Text>
              </>
            );
          })()}
        </div>
      )}

      {/* ── Totem Stickers + Stamps (Gen7) — 只读 ── */}
      {genTools?.totemStamps && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells || !!genTools?.holoCaster || !!genTools?.festa || !!genTools?.pelago) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>
            <TrophyOutlined style={{ marginRight: 6 }} />
            贴纸 &amp; 护照印章
          </Title>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={8}>
              <Card size="small">
                <Statistic title="贴纸收集" value={genTools.totemStamps.stickersCollected} />
              </Card>
            </Col>
            <Col xs={24} sm={16}>
              <Card size="small" title="护照印章">
                <Space wrap size={[4, 4]}>
                  {genTools.totemStamps.stamps.map((s, i) => (
                    <Tag key={i} color={s.earned ? 'green' : 'default'}>{s.name}</Tag>
                  ))}
                </Space>
              </Card>
            </Col>
          </Row>
        </div>
      )}

      {/* ── Rotom Dex (Gen7 USUM) — 只读 ── */}
      {genTools?.rotomDex && (
        <div>
          {(hasRtc || hasOPowers || hasZygardeCells || !!genTools?.holoCaster || !!genTools?.festa || !!genTools?.pelago || !!genTools?.totemStamps) && <Divider style={{ margin: '8px 0 20px' }} />}
          <Title level={5} style={{ marginBottom: 12 }}>
            <BugOutlined style={{ marginRight: 6 }} />
            洛托姆图鉴
          </Title>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={8}>
              <Card size="small">
                <Statistic title="好感度" value={genTools.rotomDex.affection} suffix="/ 1000" />
              </Card>
            </Col>
            <Col xs={24} sm={8}>
              <Card size="small">
                <Text type="secondary">Roto Loto</Text>
                <div style={{ marginTop: 8 }}>
                  <Tag color={genTools.rotomDex.rotoLoto1 ? 'green' : 'default'}>Roto Loto 1</Tag>
                  <Tag color={genTools.rotomDex.rotoLoto2 ? 'green' : 'default'}>Roto Loto 2</Tag>
                </div>
              </Card>
            </Col>
            <Col xs={24} sm={8}>
              <Card size="small">
                <Text type="secondary">昵称</Text>
                <div style={{ marginTop: 8 }}>
                  <Text strong>{genTools.rotomDex.nickname || '（未设置）'}</Text>
                </div>
              </Card>
            </Col>
          </Row>
          <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
            洛托姆图鉴此处仅展示 Gen7《究极之日 / 究极之月》存档中的数据。
          </Text>
        </div>
      )}

      {/* ── 保存按钮（仅可编辑区域可见时显示）── */}
      {(hasRtc || hasOPowers || hasZygardeCells) && (
        <div style={{
          position: 'sticky', bottom: 0, background: 'var(--bg-surface, #fff)',
          padding: '12px 0', borderTop: '1px solid var(--border-color, #f0f0f0)',
          textAlign: 'right', marginTop: 24,
        }}>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            onClick={handleSave}
            loading={saving}
          >
            保存专用工具设置
          </Button>
        </div>
      )}
    </div>
  );
};

export default GenToolsPanel;
