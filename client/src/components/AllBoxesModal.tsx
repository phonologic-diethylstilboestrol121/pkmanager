import React, { useState } from 'react';
import { Modal, Button, Typography, App, Tag, Tooltip, Space } from 'antd';
import { SwapOutlined, StarFilled } from '@ant-design/icons';
import { type BoxDto, type LegalityStatus, saveFileApi } from '../api/saveFile';
import PokemonSprite from './PokemonSprite';
import type { SpriteStyle } from '../lib/spriteUrl';

const { Text } = Typography;

interface Props {
  open: boolean;
  onClose: () => void;
  boxes: BoxDto[];
  legalityMap: Record<string, LegalityStatus>;
  activeBox: number;
  saveFileId: string;
  onSelectBox: (boxIndex: number) => void;
  onSwapped: () => void; // refresh after swap
  spriteStyle?: SpriteStyle;
}

// ── Helper: legality color ──────────────────────────────────────────

const legalityDotColor = (status?: LegalityStatus): string | undefined => {
  if (status === 'Legal') return '#52c41a';
  if (status === 'Fishy') return '#faad14';
  if (status === 'Illegal') return '#ff4d4f';
  return undefined;
};

// ── Mini slot cell ──────────────────────────────────────────────────

const MiniSlot: React.FC<{
  isEmpty: boolean;
  species?: number;
  isShiny?: boolean;
  isAlpha?: boolean;
  canGigantamax?: boolean;
  legalityStatus?: LegalityStatus;
  spriteStyle?: SpriteStyle;
}> = ({ isEmpty, species, isShiny, isAlpha, canGigantamax, legalityStatus, spriteStyle }) => {
  const color = legalityDotColor(legalityStatus);

  if (isEmpty) {
    return (
      <div style={{
        aspectRatio: '1', border: '1px dashed #e8e8e8', borderRadius: 3,
        background: '#fafafa', display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}>
        <Text type="secondary" style={{ fontSize: 8 }}>·</Text>
      </div>
    );
  }

  return (
    <div style={{
      aspectRatio: '1', border: '1px solid #e8e8e8', borderRadius: 3,
      background: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center',
      position: 'relative',
    }}>
      <PokemonSprite
        speciesId={species!}
        variant={spriteStyle}
        width={22} height={22}
      />
      {isShiny && (
        <StarFilled style={{
          position: 'absolute', top: 0, right: 1,
          fontSize: 7, color: '#faad14',
        }} />
      )}
      {isAlpha && (
        <span style={{
          position: 'absolute', top: 0, left: 1,
          fontSize: 6, color: '#ff4d4f', fontWeight: 'bold', lineHeight: 1,
        }}>α</span>
      )}
      {canGigantamax && (
        <span style={{
          position: 'absolute', bottom: 0, right: 1,
          fontSize: 6, color: '#fa541c', fontWeight: 'bold', lineHeight: 1,
        }}>G</span>
      )}
      {color && (
        <span style={{
          position: 'absolute', bottom: 0, left: 1,
          width: 5, height: 5, borderRadius: '50%', background: color,
          border: '0.5px solid #fff',
        }} />
      )}
    </div>
  );
};

// ── AllBoxesModal ───────────────────────────────────────────────────

const AllBoxesModal: React.FC<Props> = ({
  open, onClose, boxes, legalityMap, activeBox,
  saveFileId, onSelectBox, onSwapped, spriteStyle,
}) => {
  const { message } = App.useApp();
  const [swappingBox, setSwappingBox] = useState<number | null>(null);

  const handleSwap = async (boxA: number) => {
    const boxB = boxA + 1;
    if (boxB >= boxes.length) return;

    setSwappingBox(boxA);
    try {
      await saveFileApi.swapBoxes(saveFileId, boxA, boxB);
      message.success(`Box ${boxA + 1} ⇄ Box ${boxB + 1} 已交换`);
      onSwapped();
    } catch {
      message.error('交换失败');
    } finally {
      setSwappingBox(null);
    }
  };

  return (
    <Modal
      title="全部箱子"
      open={open}
      onCancel={onClose}
      width="90%"
      style={{ top: 20, maxWidth: 1200 }}
      footer={null}
      destroyOnClose
    >
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
          gap: 12,
          maxHeight: '70vh',
          overflow: 'auto',
          padding: 4,
        }}
      >
        {boxes.map((box) => {
          const count = box.slots.filter((s) => !s.isEmpty).length;
          const isActive = box.boxIndex === activeBox;

          return (
            <div
              key={box.boxIndex}
              style={{
                background: isActive ? '#e6f4ff' : '#fff',
                border: isActive ? '2px solid #1677ff' : '1px solid #e8e8e8',
                borderRadius: 8,
                padding: 10,
                cursor: 'pointer',
                transition: 'border-color 0.2s',
              }}
              onClick={() => { onSelectBox(box.boxIndex); onClose(); }}
            >
              {/* Header */}
              <div style={{
                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                marginBottom: 8,
              }}>
                <div>
                  <Text strong style={{ fontSize: 13 }}>
                    Box {box.boxIndex + 1}: {box.boxName}
                  </Text>
                  <br />
                  <Text type="secondary" style={{ fontSize: 11 }}>
                    {count}/{box.capacity}
                  </Text>
                </div>
                <Space size={4}>
                  {count > 0 && (
                    <Tag color={isActive ? 'blue' : 'default'} style={{ margin: 0 }}>
                      {isActive ? '当前' : `${count}只`}
                    </Tag>
                  )}
                  <Tooltip title={`与 Box ${box.boxIndex + 2} 交换`}>
                    <Button
                      size="small"
                      icon={<SwapOutlined />}
                      disabled={box.boxIndex >= boxes.length - 1}
                      loading={swappingBox === box.boxIndex}
                      onClick={(e) => {
                        e.stopPropagation();
                        handleSwap(box.boxIndex);
                      }}
                    />
                  </Tooltip>
                </Space>
              </div>

              {/* Mini 6×5 grid */}
              <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(6, 1fr)',
                gap: 2,
              }}>
                {box.slots.map((slot) => {
                  const slotKey = `box-${box.boxIndex}-${slot.slotIndex}`;
                  return (
                    <MiniSlot
                      key={slot.slotIndex}
                      isEmpty={slot.isEmpty}
                      species={slot.pokemon?.species}
                      isShiny={slot.pokemon?.isShiny}
                      isAlpha={slot.pokemon?.isAlpha}
                      canGigantamax={slot.pokemon?.canGigantamax}
                      legalityStatus={legalityMap[slotKey]}
                      spriteStyle={spriteStyle}
                    />
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </Modal>
  );
};

export default AllBoxesModal;
