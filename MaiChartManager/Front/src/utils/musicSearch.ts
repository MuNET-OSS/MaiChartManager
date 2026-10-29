import type { MusicXmlWithABJacket } from '@/client/apiGen';
import { isUtageId } from '@/consts';

/** DX 曲目 id 的起点：标准曲目是 4 位，DX 是 5 位 */
const DX_ID_START = 1e4;

export const MusicIdKind = {
  Standard: 'standard',
  DX: 'dx',
  Utage: 'utage',
} as const;
export type MusicIdKind = typeof MusicIdKind[keyof typeof MusicIdKind];

/** 曲目 ID 分段，列表的「标准 / DX / 宴会场」筛选都走这里，免得两个表格各写一套阈值 */
export const musicIdKind = (id?: number): MusicIdKind =>
  isUtageId(id) ? MusicIdKind.Utage : (id ?? 0) >= DX_ID_START ? MusicIdKind.DX : MusicIdKind.Standard;

/**
 * 曲目搜索的统一判定：名称 / 曲师 / 谱师 / ID 任意命中即算匹配。
 * 左侧列表的过滤和搜索页的过滤都走这里，免得两处口径不一致。
 */
export const matchMusicKeyword = (music: MusicXmlWithABJacket, keyword: string) => {
  const value = keyword.trim().toLowerCase();
  if (!value) return true;

  return (music.name ?? '').toLowerCase().includes(value)
    || (music.artist ?? '').toLowerCase().includes(value)
    || (music.id ?? 0).toString().includes(value)
    || (music.charts ?? []).some(chart => chart.designer?.toLowerCase().includes(value));
};
