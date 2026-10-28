import type { MusicXmlWithABJacket } from '@/client/apiGen';

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
