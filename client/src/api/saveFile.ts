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
  // General
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

  listBackups: (saveFileId: string) =>
    apiClient.get<SaveBackupDto[]>(`/SaveFile/${saveFileId}/backups`),

  restoreBackup: (saveFileId: string, backupId: string) =>
    apiClient.post(`/SaveFile/${saveFileId}/backups/${backupId}/restore`),

  newGame: (gameId: string) =>
    apiClient.post<SaveFileDetail>('/SaveFile/new-game', { gameId }),
};

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
