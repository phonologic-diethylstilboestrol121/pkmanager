import apiClient from './axios';

export interface SaveFileInfo {
  saveFileId: string;
  filename: string;
  generation: number;
  gameVersion: number;
  gameVersionName: string;
  trainerName: string;
  pokemonCount: number;
  isModified: boolean;
  boxCount: number;
  playTime: number;
  createdAt: string;
  updatedAt: string;
}

export type SaveBoxSortBy = 'species' | 'level' | 'shiny' | 'name';

export interface MoveDto {
  moveId: number;
  moveName: string;
  moveType: number;
  moveTypeName?: string;
  moveCategory: number;  // 0=Status, 1=Physical, 2=Special
  basePower?: number;
  accuracy?: number;
  basePP: number;
}

export interface PokemonDto {
  id?: string;
  // Main
  species: number;
  speciesName: string;
  nickname?: string;
  isNicknamed: boolean;
  gender: number;
  level: number;
  nature: number;
  natureName: string;
  ability: number;
  abilitySlot?: number;
  abilityName: string;
  isShiny: boolean;
  isEgg: boolean;
  heldItem: number;
  heldItemName?: string;
  ball: number;
  ballName?: string;
  form: number;
  formName?: string;
  formArgument: number;
  language: number;
  languageName?: string;
  exp: number;
  originalTrainerFriendship: number;
  handlingTrainerFriendship: number;
  pokerusStrain: number;
  pokerusDays: number;
  fatefulEncounter: boolean;
  heightScalar: number;
  weightScalar: number;
  scale: number;
  // Stats
  baseStats: number[];
  ivs: number[];
  evs: number[];
  calculatedStats: number[];
  hiddenPowerType: number;
  avs?: number[];
  gvs?: number[];
  dynamaxLevel?: number;
  canGigantamax: boolean;
  teraTypeOriginal?: number;
  teraTypeOverride?: number;
  isAlpha: boolean;
  isNoble: boolean;
  statNature: number;
  // Moves
  moves: MoveDto[];
  movePPs: number[];
  movePPUps: number[];
  relearnMoves?: number[];
  relearnMoveNames?: string[];
  // Met
  pid: number;
  ec: number;
  metLocation?: number;
  metLocationName?: string;
  metLevel?: number;
  metDate?: string;
  originGame?: number;
  originGameName?: string;
  eggLocation?: number;
  eggDate?: string;
  metTimeOfDay?: number;
  groundTile?: number;
  battleVersion?: number;
  obedienceLevel?: number;
  // OT/Misc
  tid: number;
  sid: number;
  originalTrainerName?: string;
  originalTrainerGender: number;
  handlingTrainerName?: string;
  handlingTrainerGender: number;
  handlingTrainerLanguage: number;
  affection?: number;
  homeTracker?: string;
  isFavorite: boolean;
  geoCountry?: number[];
  geoRegion?: number[];
  country?: number;
  countryName?: string;
  subRegion?: number;
  subRegionName?: string;
  consoleRegion?: number;
  consoleRegionName?: string;
  affixedRibbon?: number;
  // Cosmetic
  markings: number[];
  contestCool: number;
  contestBeauty: number;
  contestCute: number;
  contestSmart: number;
  contestTough: number;
  contestSheen: number;
  originMark?: number;
  // Gen-Specific Tab
  // Gen3 Colosseum/XD
  shadowId?: number;
  purification?: number;
  isShadow: boolean;
  // Gen4 HGSS Shiny Leaves (raw bitfield: bit0-4=leaves, bit5=crown)
  shinyLeaf?: number;
  // Gen5 NSparkle / PokeStar
  nSparkle?: boolean;
  pokeStarFame?: number;
  isPokeStar: boolean;
  // Gen6-7 Super Training
  superTrainingEnabled: boolean;
  secretSuperTrainingUnlocked?: boolean;
  superTrainSupremelyTrained: boolean;
  superTrainRegimenFlags?: boolean[];
  distSuperTrainFlags?: boolean[];
  // Gen6-7 Amie
  fullness?: number;
  enjoyment?: number;
  // Gen7 Hyper Training
  hyperTrainingEnabled: boolean;
  hyperTrainFlags?: boolean[];
  // Gen7 LGPE
  combatPower?: number;
  spirit?: number;
  mood?: number;
  // General
  format: number;      // PKM format (3=PK3/Gen3, 4=PK4/Gen4, ..., 7=PK7/Gen7)
  isValid: boolean;
  pkmDataBase64?: string;
}

export interface BoxSlotDto {
  slotIndex: number;
  isEmpty: boolean;
  pokemon?: PokemonDto;
}

export interface BoxDto {
  boxIndex: number;
  boxName: string;
  capacity: number;
  slots: BoxSlotDto[];
}

export interface SaveFileDetail {
  saveFileId: string;
  filename: string;
  generation: number;
  gameVersion: number;
  gameVersionName: string;
  trainerName: string;
  trainerId: number;
  secretId: number;
  playTime: number;
  isModified: boolean;
  boxes: BoxDto[];
  party: BoxSlotDto[];
}

// ── Legality types ───────────────────────────────────
export type LegalityStatus = 'Legal' | 'Fishy' | 'Illegal';

export interface JudgementDto {
  identifier: string;
  judgement: string;
  comment: string;
  issue: string;
  canFix: boolean;
  fixAction?: string;
}

export interface EditResultDto {
  isValid: boolean;
  status: LegalityStatus;
  report?: string;
  judgements: JudgementDto[];
  updatedPokemon: PokemonDto;
}

export interface LegalityReportDto {
  isValid: boolean;
  status: LegalityStatus;
  report?: string;
  judgements: JudgementDto[];
}

export interface SlotLegalityDto {
  slotId: string;
  boxIndex: number;
  slotIndex: number;
  isParty: boolean;
  species: number;
  speciesName: string;
  nickname?: string;
  level: number;
  isShiny: boolean;
  status: LegalityStatus;
  firstIssue?: string;
}

export interface BatchLegalityReportDto {
  total: number;
  legalCount: number;
  fishyCount: number;
  illegalCount: number;
  slots: SlotLegalityDto[];
}

// ── F.2 合法性引擎升级: 生成 + 修复 DTO ──────────────────

export interface LegalizationRequest {
  species: number;
  targetGameVersion: number;
  isShiny?: boolean;
  nature?: number;
  gender?: number;
  ability?: number;
  form?: number;
  level?: number;
  desiredMoves?: number[];
  preserveOT?: boolean;
  originalTrainerName?: string;
  trainerSaveFileId?: string;
}

export interface ShowdownImportRequest {
  showdownText: string;
  targetGameVersion: number;
  trainerSaveFileId?: string;
}

export interface AutoFixRequest {
  pkmDataBase64: string;
  editSnapshot: Record<string, unknown>;
  fixActions?: string[];
  trainerSaveFileId?: string;
  targetGameVersion?: number;
}

export interface ShowdownExportRequest {
  pkmDataBase64: string;
  editSnapshot?: Record<string, unknown>;
}

export interface LegalizationResultDto {
  success: boolean;
  error?: string;
  pokemon?: PokemonDto;
  pkmDataBase64?: string;
  changes: string[];
  encounterType?: string;
}

export interface AutoFixResultDto {
  fixed: boolean;
  appliedFixes: string[];
  failedFixes: string[];
  updatedPokemon?: PokemonDto;
  pkmDataBase64?: string;
  status: LegalityStatus;
  judgements: JudgementDto[];
  report?: string;
}

export interface ShowdownParseResultDto {
  success: boolean;
  error?: string;
  sets: ShowdownSetPreviewDto[];
}

export interface ShowdownSetPreviewDto {
  species: string;
  speciesId: number;
  nickname?: string;
  level: number;
  shiny: boolean;
  gender?: string;
  ability?: string;
  nature?: string;
  item?: string;
  moves: string[];
  form?: string;
  rawText: string;
}

export interface BankBatchLegalityReportDto {
  total: number;
  legalCount: number;
  fishyCount: number;
  illegalCount: number;
  slots: SlotLegalityDto[];
}

export const saveFileApi = {
  list: () =>
    apiClient.get<SaveFileInfo[]>('/SaveFile'),

  getDetail: (id: string) =>
    apiClient.get<SaveFileDetail>(`/SaveFile/${id}`),

  upload: (file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiClient.post<SaveFileDetail>('/SaveFile/upload', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },

  moveSlot: (saveFileId: string, data: {
    fromBoxIndex: number;
    fromSlotIndex: number;
    toBoxIndex: number;
    toSlotIndex: number;
  }) => apiClient.post(`/SaveFile/${saveFileId}/move-slot`, data),

  save: (saveFileId: string) =>
    apiClient.post(`/SaveFile/${saveFileId}/save`),

  updateSaveSlot: (pkmDataBase64: string, saveFileId: string, boxIndex: number, slotIndex: number, isParty: boolean, data: Record<string, unknown>) =>
    apiClient.put<EditResultDto>('/Pokemon/save-slot', { ...data, pkmDataBase64, saveFileId, boxIndex, slotIndex, isParty }),

  generateQR: (pkmDataBase64: string) =>
    apiClient.post<string>('/Pokemon/qr', { pkmDataBase64 }),

  validatePokemon: (pkmDataBase64: string, data: Record<string, unknown>) =>
    apiClient.post<LegalityReportDto>('/Pokemon/validate-party', { ...data, pkmDataBase64 }),

  /** 按 ID 验证宝可梦合法性（支持银行和存档宝可梦） */
  validateById: (id: string, data: Record<string, unknown>) =>
    apiClient.post<LegalityReportDto>(`/Pokemon/${id}/validate`, data),

  download: (saveFileId: string) =>
    apiClient.get(`/SaveFile/${saveFileId}/download`, {
      responseType: 'blob',
    }),

  delete: (saveFileId: string) =>
    apiClient.delete(`/SaveFile/${saveFileId}`),

  batchLegalityReport: (saveFileId: string) =>
    apiClient.post<BatchLegalityReportDto>(`/SaveFile/${saveFileId}/legality-report`),

  swapBoxes: (saveFileId: string, boxIndexA: number, boxIndexB: number) =>
    apiClient.post(`/SaveFile/${saveFileId}/swap-boxes`, { boxIndexA, boxIndexB }),

  sortBoxes: (saveFileId: string, sortBy: SaveBoxSortBy) =>
    apiClient.post(`/SaveFile/${saveFileId}/sortBoxes`, { sortBy }),

  sortBox: (saveFileId: string, boxIndex: number, sortBy: SaveBoxSortBy) =>
    apiClient.post(`/SaveFile/${saveFileId}/sortBox`, { boxIndex, sortBy }),

  listBackups: (saveFileId: string) =>
    apiClient.get<SaveBackupDto[]>(`/SaveFile/${saveFileId}/backups`),

  restoreBackup: (saveFileId: string, backupId: string) =>
    apiClient.post(`/SaveFile/${saveFileId}/backups/${backupId}/restore`),

  newGame: (gameId: string) =>
    apiClient.post<SaveFileDetail>('/SaveFile/new-game', { gameId }),

  // ── 背包（Bag Editor）──
  getBag: (saveFileId: string) =>
    apiClient.get<BagDto>(`/SaveFile/${saveFileId}/bag`),

  saveBag: (saveFileId: string, bag: BagDto) =>
    apiClient.put(`/SaveFile/${saveFileId}/bag`, bag),

  // ── 训练家信息（Trainer Info）──
  getTrainerInfo: (saveFileId: string) =>
    apiClient.get<TrainerInfoDto>(`/SaveFile/${saveFileId}/trainer`),

  saveTrainerInfo: (saveFileId: string, info: TrainerInfoDto) =>
    apiClient.put(`/SaveFile/${saveFileId}/trainer`, info),

  // ── 图鉴（Pokédex Editor）──
  getPokedex: (saveFileId: string) =>
    apiClient.get<PokedexDto>(`/SaveFile/${saveFileId}/pokedex`),

  savePokedex: (saveFileId: string, data: PokedexDto) =>
    apiClient.put(`/SaveFile/${saveFileId}/pokedex`, data),

  batchPokedex: (saveFileId: string, action: string) =>
    apiClient.post<PokedexDto>(`/SaveFile/${saveFileId}/pokedex/batch`, { action }),

  // ── F.2 合法性引擎升级: 生成 + 修复 ──
  legalize: (data: LegalizationRequest) =>
    apiClient.post<LegalizationResultDto>('/Pokemon/legalize', data),

  legalizeShowdown: (data: ShowdownImportRequest) =>
    apiClient.post<LegalizationResultDto>('/Pokemon/legalize-showdown', data),

  parseShowdown: (data: { showdownText: string }) =>
    apiClient.post<ShowdownParseResultDto>('/Pokemon/parse-showdown', data),

  // ── D.5 Showdown 导出 ──
  exportShowdown: (data: ShowdownExportRequest) =>
    apiClient.post<string>('/Pokemon/export-showdown', data),

  autoFix: (data: AutoFixRequest) =>
    apiClient.post<AutoFixResultDto>('/Pokemon/auto-fix', data),

  bankLegalityReport: (page?: number, pageSize?: number) =>
    apiClient.post<BankBatchLegalityReportDto>('/Bank/legality-report', null, {
      params: { page: page ?? 1, pageSize: pageSize ?? 100 }
    }),

  // ── 世代专属工具（Gen Tools）──
  getGenTools: (saveFileId: string) =>
    apiClient.get<GenToolsDto>(`/SaveFile/${saveFileId}/gen-tools`),

  saveGenTools: (saveFileId: string, data: GenToolsDto) =>
    apiClient.put(`/SaveFile/${saveFileId}/gen-tools`, data),

  // ── 高级搜索 ──
  searchSave: (saveFileId: string, request: PokemonSearchRequest) =>
    apiClient.post<PokemonSearchResultDto>(`/SaveFile/${saveFileId}/search`, request),
};

// ── GenTools types ────────────────────────────────────

export interface GenToolsCapability {
  hasRtc: boolean;
  hasOPowers: boolean;
  hasZygardeCells: boolean;
  hasEntreeForest: boolean;
  hasEntralink: boolean;
  hasCGearSkin: boolean;
  hasHoloCaster: boolean;
  hasFesta: boolean;
  hasPelago: boolean;
  hasTotemStamps: boolean;
  hasRotomDex: boolean;
}

export interface Rtc3EntryDto {
  key: string;
  label: string;
  day: number;
  hour: number;
  minute: number;
  second: number;
}

export interface OPowerTypeEntryDto {
  key: string;           // "hatching", "spAttack" ...
  name: string;          // "孵化", "特攻" ...
  category: string;      // "field" | "battle"
  level1: number;        // 0-3
  level2: number;        // 0-3
  level1Unlocked: boolean;
  level2Unlocked: boolean;
  level3Unlocked: boolean;
  hasLevelS: boolean;
  levelSUnlocked: boolean;
  hasLevelMax: boolean;
  levelMaxUnlocked: boolean;
}

export interface OPowerDto {
  points: number;
  enableUnlocked: boolean;
  fullRecoveryUnlocked: boolean;
  entries: OPowerTypeEntryDto[];
}

export interface ZygardeCellDto {
  index: number;       // 0-based (0~94 SM, 0~99 USUM)
  collected: boolean;  // 是否已收集
}

export interface ZygardeDto {
  collectedCount: number;
  totalCount: number;
  cells: ZygardeCellDto[];
}

export interface EntreeSlotDto {
  index: number;
  species: number;
  move: number;
  gender: number;
  form: number;
  isOccupied: boolean;
  isInvisible: boolean;
  area: number;
}

export interface EntreeForestDto {
  totalSlots: number;
  occupiedSlots: number;
  unlock9thArea: boolean;
  unlock38Areas: number;
  slots: EntreeSlotDto[];
}

export interface EntralinkDto {
  whiteForestLevel: number;
  blackCityLevel: number;
  missionsComplete?: number | null;
  passPower1?: number | null;
  passPower2?: number | null;
  passPower3?: number | null;
}

export interface CGearSkinDto {
  hasCGearSkin: boolean;
  checksum: number;
  dataSize: number;
}

// ── I.5 Gen6/Gen7 只读字段 ────────────────────────────

export interface HoloCasterDto {
  dataPresent: boolean;
}

export interface FestaDto {
  festaCoins: number;
  totalFestaCoins: number;
  festaRank: number;
}

export interface PelagoDto {
  occupiedSlots: number;
  totalSlots: number;
  beanCounts: number[];
  visits: number;
  eggsHatched: number;
  treasureHunts: number;
}

export interface TotemStampItem {
  name: string;
  earned: boolean;
}

export interface TotemStampsDto {
  stickersCollected: number;
  stamps: TotemStampItem[];
}

export interface RotomDexDto {
  affection: number;
  rotoLoto1: boolean;
  rotoLoto2: boolean;
  nickname: string | null;
}

export interface GenToolsDto {
  capability: GenToolsCapability;
  rtcEntries?: Rtc3EntryDto[];
  opower?: OPowerDto;
  zygarde?: ZygardeDto;
  entreeForest?: EntreeForestDto;
  entralink?: EntralinkDto;
  cGearSkin?: CGearSkinDto;
  holoCaster?: HoloCasterDto;
  festa?: FestaDto;
  pelago?: PelagoDto;
  totemStamps?: TotemStampsDto;
  rotomDex?: RotomDexDto;
}

// ── Bag types ─────────────────────────────────────────

export interface BagCapability {
  hasFavorite: boolean;
  hasNewFlag: boolean;
  hasFreeSpace: boolean;
  maxItemID: number;
}

export interface BagDto {
  capability: BagCapability;
  pouches: PouchDto[];
}

export interface PouchDto {
  type: string;
  maxCount: number;
  items: BagItemDto[];
}

export interface BagItemDto {
  index: number;
  count: number;
  isFavorite?: boolean;
  isNew?: boolean;
  isFreeSpace?: boolean;
}

// ── Trainer Info types ───────────────────────────────

export interface TrainerCapability {
  hasCoins: boolean;
  hasBP: boolean;
  hasLeaguePoints: boolean;
  hasBadges: boolean;
  badgeCount: number;
  badgeNames: string[];
  hasTrainerCard: boolean;
  hasCardNumber: boolean;
  hasGameSync: boolean;
  maxStringLengthTrainer: number;
  maxMoney: number;
  maxCoins?: number;
  trainerIDFormat: number;  // 0=None, 1=16BitSingle, 2=16Bit, 3=SixDigit
}

export interface TrainerInfoDto {
  capability: TrainerCapability;
  ot: string;
  tid16: number;
  sid16: number;
  displayTID: number;
  displaySID: number;
  gender: number;
  language: number;
  languageName?: string;
  playedHours: number;
  playedMinutes: number;
  playedSeconds: number;
  generation: number;
  gameVersionName?: string;
  money?: number;
  coins?: number;
  bp?: number;
  leaguePoints?: number;
  badges?: number;
  cardNumber?: string;
  gameSyncID?: string;
}

// ── Pokédex types ────────────────────────────────────

export interface PokedexEntryDto {
  species: number;
  seen: boolean;
  caught: boolean;
  seenGender?: number | null;
  displayFormValues?: number[] | null;
  spindaPID?: number | null;
  languageFlags?: number | null;
}

export interface PokedexDto {
  hasPokeDex: boolean;
  gameVersion?: number;
  generation: number;
  visibleSpeciesMax: number; // 0 = use totalSpecies
  isSupported: boolean;
  unsupportedReason?: string;
  totalSpecies: number;
  seenCount: number;
  caughtCount: number;
  percentSeen: number;
  percentCaught: number;
  entries: PokedexEntryDto[];
}

// ── 本地模拟器 API ──────────────────────────────────────

export interface CheckLocalRequest {
  generation: number;
  gameVersion?: number;
  gameId?: string;
}

export interface LaunchLocalResult {
  type: 'azahar' | 'desmume';
  generation: number;
  gameVersion: number;
  titleIdLow?: string | null;
  exePath: string;
  saveDir: string;
  romPath?: string;
  gameInstalled: boolean;
  emuSavePath?: string;
  launchArgs?: string;
  saveDataBase64: string;
  fileName: string;
  saveFileId: string;
  syncToken: string;
}

export const emulatorApi = {
  checkLocal: (data: CheckLocalRequest) =>
    apiClient.post<Record<string, unknown>>('/Emulator/check-local', data),

  launchLocal: (saveFileId: string) =>
    apiClient.post<LaunchLocalResult>(`/Emulator/launch-local/${saveFileId}`),

  /** 获取临时启动 token（用于协议处理器一键启动） */
  createLaunchToken: (saveFileId: string) =>
    apiClient.post<{ token: string; protocolUrl: string }>(`/Emulator/launch-token/${saveFileId}`),

  localStatus: (saveFileId: string) =>
    apiClient.get<{ running: boolean; pid?: number; stuck?: boolean }>(`/Emulator/local-status/${saveFileId}`),

  syncFromLocal: (saveFileId: string) =>
    apiClient.post<{ synced: boolean; restored: boolean }>(`/Emulator/sync-from-local/${saveFileId}`),

  emergencyRestore: (saveFileId: string) =>
    apiClient.post<{ restored: boolean }>(`/Emulator/emergency-restore/${saveFileId}`),
};

export interface SaveBackupDto {
  id: string;
  saveFileId: string;
  label?: string;
  createdAt: string;
  pokemonCount: number;
  trainerName: string;
  playTime: string;
  gameVersion: string;
  boxCount: number;
}

// ── 高级搜索 types ────────────────────────────────────

export interface PokemonSearchRequest {
  // 基础
  speciesId?: number;
  isShiny?: boolean;
  isEgg?: boolean;
  gender?: number;
  minLevel?: number;
  maxLevel?: number;
  // 性格/特性/道具/球种
  nature?: number;
  ability?: number;
  heldItem?: number;
  ball?: number;
  // 来源
  originGame?: number;
  language?: number;
  // IV
  minIV_HP?: number; maxIV_HP?: number;
  minIV_ATK?: number; maxIV_ATK?: number;
  minIV_DEF?: number; maxIV_DEF?: number;
  minIV_SPA?: number; maxIV_SPA?: number;
  minIV_SPD?: number; maxIV_SPD?: number;
  minIV_SPE?: number; maxIV_SPE?: number;
  minIVTotal?: number; maxIVTotal?: number;
  // EV
  minEV_HP?: number; maxEV_HP?: number;
  minEV_ATK?: number; maxEV_ATK?: number;
  minEV_DEF?: number; maxEV_DEF?: number;
  minEV_SPA?: number; maxEV_SPA?: number;
  minEV_SPD?: number; maxEV_SPD?: number;
  minEV_SPE?: number; maxEV_SPE?: number;
  minEVTotal?: number; maxEVTotal?: number;
  // 招式
  requiredMoves?: number[];
  anyMoves?: number[];
  // 训练家
  ot_Name?: string;
  tid?: number;
  // 合法性(仅银行)
  isLegal?: boolean;
  // 文本搜索
  searchText?: string;
  // 分页
  page: number;
  pageSize: number;
}

export interface PokemonSearchItemDto {
  speciesId: number;
  speciesName: string;
  nickname: string;
  level: number;
  nature: number;
  natureName: string;
  ability: number;
  abilityName: string;
  heldItem?: number;
  heldItemName?: string;
  isShiny: boolean;
  isEgg: boolean;
  isValid?: boolean;
  pkmDataBase64?: string;
  boxIndex?: number;
  slotIndex?: number;
  isParty: boolean;
  locationLabel?: string;
  bankId?: string;
}

export interface PokemonSearchResultDto {
  total: number;
  page: number;
  pageSize: number;
  items: PokemonSearchItemDto[];
}
