export const LEVELS = [0, 1, 2, 3, 4, 5, 6, 7, '7+', 8, '8+', 9, '9+', 10, '10+', 11, '11+', 12, '12+', 13, '13+', 14, '14+', 15, '15+'] as const;
export const LEVEL_COLOR = ['rgb(34, 187, 91)', 'rgb(251, 156, 45)', 'rgb(246, 72, 97)', 'rgb(158, 69, 226)',
  'rgb(228, 166, 255)'] as const;
export const LEVEL_HUE = [150, 64, 17.17, 306.5, 315] as const;
export const DIFFICULTY = ['Basic', 'Advanced', 'Expert', 'Master', 'Re:Master'] as const;
export const UTAGE_GENRE = 107;
// 宴会场 ID = 序号 * 10000 + 原曲 ID 后四位，序号从 10 开始，所以宴会场 ID 起步就是 10 万
export const UTAGE_ID_START = 1e5;
export const isUtageId = (id?: number) => (id ?? 0) >= UTAGE_ID_START;
