import React, { useEffect, useState, useCallback } from 'react';
import { Card, Row, Col, Typography, Button, Modal, List, Tag, Spin, App } from 'antd';
import { useNavigate } from 'react-router-dom';
import { BankOutlined, SaveOutlined, PlusOutlined, SettingOutlined } from '@ant-design/icons';
import { saveFileApi, type SaveFileInfo } from '../api/saveFile';
import { useDiagnosticStore } from '../stores/diagnosticStore';
import { PLAYABLE_GAMES, GAME_META } from '../constants/games';
import { emulatorApi } from '../api/saveFile';
import GameCover from '../components/GameCover';
import { launchLocalSave } from '../lib/localLaunch';

const { Title, Text } = Typography;

const DashboardPage: React.FC = () => {
  const navigate = useNavigate();
  const { message } = App.useApp();

  // Modal state — selectedGame is null when closed
  const [selectedGame, setSelectedGame] = useState<{ gameId: string; displayName: string; generation: number } | null>(null);
  const [saves, setSaves] = useState<SaveFileInfo[]>([]);
  const [loadingSaves, setLoadingSaves] = useState(false);
  const fetchSaves = useCallback(async () => {
    setLoadingSaves(true);
    try {
      const res = await saveFileApi.list();
      setSaves(res.data || []);
    } catch (err: any) {
      useDiagnosticStore.getState().log({
        category: 'api',
        level: 'error',
        message: '加载存档列表失败',
        stack: err?.message,
      });
    } finally {
      setLoadingSaves(false);
    }
  }, []);

  useEffect(() => {
    if (selectedGame) fetchSaves();
  }, [selectedGame, fetchSaves]);

  const gameVersion = selectedGame ? GAME_META[selectedGame.gameId]?.gameVersion : undefined;
  const matchingSaves = saves.filter(s => s.gameVersion === gameVersion);
  const isNds = selectedGame ? (selectedGame.generation >= 4 && selectedGame.generation <= 5) : false;
  const is3ds = selectedGame ? selectedGame.generation >= 6 : false;
  const [checkState, setCheckState] = useState<{ loading: boolean; ready?: boolean; error?: string }>({ loading: false });

  // 3DS 游戏：打开 Modal 时预校验 Azahar
  useEffect(() => {
    if (!selectedGame || !is3ds) { setCheckState({ loading: false }); return; }
    setCheckState({ loading: true });
    emulatorApi.checkLocal({ generation: selectedGame.generation, gameVersion: gameVersion, gameId: selectedGame.gameId })
      .then((res: any) => {
        const d = res.data || res;
        const ready = d.azaharReady || d.desmumeReady;
        setCheckState({ loading: false, ready, error: ready ? undefined : (d.error || '模拟器未就绪') });
      })
      .catch((err: any) => {
        setCheckState({ loading: false, ready: false, error: err.response?.data?.message || err.message || '校验失败' });
      });
  }, [selectedGame?.gameId, is3ds]);

  const handleSelectSave = async (saveFileId: string, filename?: string) => {
    if (is3ds && !checkState.ready) {
      message.warning(checkState.error || '本地模拟器未就绪');
      return;
    }
    setSelectedGame(null);
    if (is3ds) {
      try {
        await launchLocalSave(saveFileId, message, filename);
      } catch (err: any) {
        message.error(err?.message || err?.response?.data?.message || '本机启动失败');
      }
    } else {
      window.open(`/play${isNds ? '-nds' : ''}/${saveFileId}`, '_blank');
    }
  };

  const handleNewGame = () => {
    if (!selectedGame) return;
    setSelectedGame(null);
    if (is3ds) {
      window.open(`/saves`, '_blank');
    } else {
      window.open(`/play${isNds ? '-nds' : ''}/new/${selectedGame.gameId}`, '_blank');
    }
  };

  return (
    <div style={{ padding: 32, maxWidth: 1200, margin: '0 auto' }}>
      <Title level={2}>工作台</Title>
      <Row gutter={[24, 24]} style={{ marginTop: 24 }}>
        {/* 功能入口 */}
        <Col xs={24} sm={12} md={8} lg={6}>
          <Card hoverable onClick={() => navigate('/saves')}
            style={{ textAlign: 'center', minHeight: 200 }}>
            <SaveOutlined style={{ fontSize: 48, color: '#1890ff', marginBottom: 16 }} />
            <Title level={4}>存档管理</Title>
            <p>上传和管理你的游戏存档</p>
            <Button type="primary">进入</Button>
          </Card>
        </Col>
        <Col xs={24} sm={12} md={8} lg={6}>
          <Card hoverable onClick={() => navigate('/bank')}
            style={{ textAlign: 'center', minHeight: 200 }}>
            <BankOutlined style={{ fontSize: 48, color: '#52c41a', marginBottom: 16 }} />
            <Title level={4}>我的银行</Title>
            <p>在线宝可梦收藏管理</p>
            <Button type="primary">进入</Button>
          </Card>
        </Col>
        <Col xs={24} sm={12} md={8} lg={6}>
          <Card hoverable onClick={() => navigate('/settings')}
            style={{ textAlign: 'center', minHeight: 200 }}>
            <SettingOutlined style={{ fontSize: 48, color: '#722ed1', marginBottom: 16 }} />
            <Title level={4}>设置</Title>
            <p>配置本地模拟器与协议启动</p>
            <Button type="primary">进入</Button>
          </Card>
        </Col>

        {/* 可玩游戏卡片 (Gen3 GBA + Gen4/5 NDS + Gen6/7 3DS) — 按发行日期排序 */}
        {PLAYABLE_GAMES.map(game => (
          <Col key={game.gameId} xs={24} sm={12} md={8} lg={6}>
            <Card hoverable onClick={() => setSelectedGame({ gameId: game.gameId, displayName: game.displayName, generation: game.generation })}
              style={{ textAlign: 'center', minHeight: 300, borderColor: game.color }}>
              <GameCover gameId={game.gameId} />
              <Title level={4} style={{ marginTop: 8 }}>游玩{game.shortName}</Title>
              <p>{game.displayName}</p>
              <Button type="primary" style={{ background: game.color, borderColor: game.color }}>
                开始游戏
              </Button>
            </Card>
          </Col>
        ))}
      </Row>

      {/* 游戏选择 Modal — 复用同一对话框，标题和 newGame 动态切换 */}
      <Modal
        title={selectedGame ? `游玩${selectedGame.displayName.replace('宝可梦 ', '')}` : ''}
        open={selectedGame !== null}
        onCancel={() => setSelectedGame(null)}
        footer={null}
        width={520}
      >
        {/* 3DS: 预校验状态 */}
        {is3ds && checkState.loading && (
          <div style={{ textAlign: 'center', padding: 16, background: '#e6f4ff', borderRadius: 6, marginBottom: 12 }}>
            <Spin size="small" /> 正在检查 Azahar 配置...
          </div>
        )}
        {is3ds && !checkState.loading && checkState.error && (
          <div style={{ padding: 12, background: '#fff2f0', borderRadius: 6, marginBottom: 12, border: '1px solid #ffccc7' }}>
            <Text type="danger">{checkState.error}</Text>
            <br />
            <Button type="link" size="small" onClick={() => { setSelectedGame(null); window.open('/settings', '_blank'); }}>
              前往设置页配置
            </Button>
          </div>
        )}

        {loadingSaves ? (
          <div style={{ textAlign: 'center', padding: 32 }}><Spin /></div>
        ) : matchingSaves.length > 0 ? (
          <>
            <Text type="secondary" style={{ marginBottom: 12, display: 'block' }}>
              {is3ds ? '选择已有存档后将直接本机游玩' : '选择已有存档继续游戏'}
            </Text>
            <List
              dataSource={matchingSaves}
              renderItem={(save) => (
                <List.Item
                  onClick={() => handleSelectSave(save.saveFileId, save.filename)}
                  style={{ cursor: 'pointer', padding: '10px 12px', borderRadius: 6 }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = '#f6ffed'; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = ''; }}
                >
                  <List.Item.Meta
                    title={save.filename}
                    description={
                      <span>
                        {save.trainerName && <><Text>{save.trainerName}</Text> &middot; </>}
                        <Tag color={save.generation >= 6 ? 'volcano' : save.generation >= 4 ? 'blue' : 'green'}>
                          Gen{save.generation} {save.generation >= 6 ? '3DS' : save.generation >= 4 ? 'NDS' : 'GBA'}
                        </Tag>
                        {save.pokemonCount > 0 && <Text type="secondary"> {save.pokemonCount} 只宝可梦</Text>}
                      </span>
                    }
                  />
                </List.Item>
              )}
              style={{ marginBottom: 16 }}
            />
            {!is3ds && (
              <div style={{ borderTop: '1px solid #f0f0f0', paddingTop: 16 }}>
                <Text type="secondary" style={{ marginBottom: 8, display: 'block' }}>
                  或者开始全新游戏
                </Text>
              </div>
            )}
          </>
        ) : (
          <div style={{ textAlign: 'center', padding: '16px 0' }}>
            <Text type="secondary">
              {is3ds
                ? `暂无${selectedGame?.displayName}存档，请先上传你本机已绑定的 3DS 存档`
                : `暂无${selectedGame?.displayName}存档`}
            </Text>
          </div>
        )}

        {!is3ds && (
          <Button
            type="dashed"
            block
            size="large"
            icon={<PlusOutlined />}
            onClick={handleNewGame}
            style={{ marginTop: matchingSaves.length > 0 ? 0 : 8, height: 48 }}
          >
            新游戏
          </Button>
        )}
      </Modal>
    </div>
  );
};

export default DashboardPage;
