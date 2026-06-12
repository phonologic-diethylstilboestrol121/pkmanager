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

  updateSaveSlot: (pkmDataBase64: string, saveFileId: string, boxIndex: number, slotIndex: number, isParty: boolean, data: any) =>
    apiClient.put<EditResultDto>('/Pokemon/save-slot', { ...data, pkmDataBase64, saveFileId, boxIndex, slotIndex, isParty }),

  generateQR: (pkmDataBase64: string) =>
    apiClient.post<string>('/Pokemon/qr', { pkmDataBase64 }),

  validatePokemon: (pkmDataBase64: string, data: any) =>
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

  autoFix: (data: AutoFixRequest) =>
    apiClient.post<AutoFixResultDto>('/Pokemon/auto-fix', data),

  bankLegalityReport: (page?: number, pageSize?: number) =>
    apiClient.post<BankBatchLegalityReportDto>('/Bank/legality-report', null, {
      params: { page: page ?? 1, pageSize: pageSize ?? 100 }
    }),
};

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
    apiClient.post<Record<string, any>>('/Emulator/check-local', data),

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
