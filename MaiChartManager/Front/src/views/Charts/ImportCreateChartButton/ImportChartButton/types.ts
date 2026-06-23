import { ImportChartCheckResult, ImportChartMessage, SetAudioPreviewRequest, ShiftMethod } from "@/client/apiGen";

export enum STEP {
  none,
  selectFile,
  checking,
  showWarning,
  importing,
  showResultError
}

export enum IMPORT_STEP {
  start,
  create,
  chart,
  music,
  movie,
  jacket,
  finish
}

export type ImportMeta = {
  id: number,
  importStep: IMPORT_STEP,
  maidata?: File,
  track?: File,
  bg?: File,
  movie?: File,
  name: string,
  chartPaddings: ImportChartCheckResult['chartPaddings'],
  first: number,
  isDx: boolean,
  previewTime?: SetAudioPreviewRequest
}

export type FirstPaddingMessage = { first: number, chartPaddings: ImportChartCheckResult['chartPaddings']}
export type ImportChartMessageEx = (ImportChartMessage | FirstPaddingMessage) & { name: string, isPaid?: boolean, detail?: string }

export const dummyMeta = {name: '', importStep: IMPORT_STEP.start} as ImportMeta

export const defaultTempOptions: TempOptions = {
  shift: ShiftMethod.Bar,
}

export const defaultSavedOptions = {
  addVersionId: 0,
  genreId: 1,
  // 大家都喜欢写 22001，甚至不理解这个选项是干什么的
  version: 22001,
}

export type TempOptions = {
  shift: ShiftMethod,
  shiftLocked?: boolean,
  ignoreLevel?: boolean,
  disableBga?: boolean,
  ignoreGapless?: boolean,
};
export type SavedOptions = typeof defaultSavedOptions;
