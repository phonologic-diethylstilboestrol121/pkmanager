-- ============================================================
-- F.1 静态数据缓存 — 资源名称表
-- 数据源: PKHeX.Core 内置文本资源（只读参考数据）
-- 种子脚本: scripts/seed-static-data.sh
-- ============================================================

-- ── 物种名称 (1025 条/语言) ─────────────────────────────
CREATE TABLE IF NOT EXISTS res_species (
    id   INT         NOT NULL,
    lang VARCHAR(10) NOT NULL DEFAULT 'zh-Hans',
    name VARCHAR(64) NOT NULL,
    PRIMARY KEY (id, lang)
);
COMMENT ON TABLE res_species IS '宝可梦物种名称 — 从 PKHeX text_Species 导入';

-- ── 招式名称 (920 条/语言) ─────────────────────────────
CREATE TABLE IF NOT EXISTS res_moves (
    id   INT         NOT NULL,
    lang VARCHAR(10) NOT NULL DEFAULT 'zh-Hans',
    name VARCHAR(64) NOT NULL,
    PRIMARY KEY (id, lang)
);
COMMENT ON TABLE res_moves IS '招式名称 — 从 PKHeX text_Moves 导入';

-- ── 特性名称 (310 条/语言) ─────────────────────────────
CREATE TABLE IF NOT EXISTS res_abilities (
    id   INT         NOT NULL,
    lang VARCHAR(10) NOT NULL DEFAULT 'zh-Hans',
    name VARCHAR(64) NOT NULL,
    PRIMARY KEY (id, lang)
);
COMMENT ON TABLE res_abilities IS '特性名称 — 从 PKHeX text_Abilities 导入';

-- ── 性格名称 (24 条/语言) ─────────────────────────────
CREATE TABLE IF NOT EXISTS res_natures (
    id   INT         NOT NULL,
    lang VARCHAR(10) NOT NULL DEFAULT 'zh-Hans',
    name VARCHAR(16) NOT NULL,
    PRIMARY KEY (id, lang)
);
COMMENT ON TABLE res_natures IS '性格名称 — 从 PKHeX text_Natures 导入';

-- ── 道具名称 (2684 条/语言) ─────────────────────────────
CREATE TABLE IF NOT EXISTS res_items (
    id   INT         NOT NULL,
    lang VARCHAR(10) NOT NULL DEFAULT 'zh-Hans',
    name VARCHAR(128) NOT NULL,
    PRIMARY KEY (id, lang)
);
COMMENT ON TABLE res_items IS '道具名称 — 从 PKHeX text_Items 导入。球种名称由此表按球种 ID 派生';
