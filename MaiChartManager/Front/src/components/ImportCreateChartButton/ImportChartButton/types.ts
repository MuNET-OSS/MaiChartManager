import { ImportChartMessage, ShiftMethod } from "@/client/apiGen";

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
  musicPadding: number,
  first: number,
  bar: number,
  isDx: boolean,
}

export type FirstPaddingMessage = { first: number, padding: number }
export type ImportChartMessageEx = (ImportChartMessage | FirstPaddingMessage) & { name: string, isPaid?: boolean }

export const dummyMeta = {name: '', importStep: IMPORT_STEP.start} as ImportMeta

export const defaultTempOptions: TempOptions = {
  shift: ShiftMethod.Bar,
}

export enum MOVIE_CODEC {
  PreferH264,
  ForceH264,
  ForceVP9,
}

export const defaultSavedOptions = {
  ignoreLevel: false,
  addVersionId: 0,
  genreId: 1,
  // 大家都喜欢写 22001，甚至不理解这个选项是干什么的
  version: 22001,
  disableBga: false,
  noScale: false,
  movieCodec: MOVIE_CODEC.ForceVP9,
  yuv420p: true,
}

export type TempOptions = {
  shift: ShiftMethod,
  shiftLocked?: boolean
};
export type SavedOptions = typeof defaultSavedOptions;
