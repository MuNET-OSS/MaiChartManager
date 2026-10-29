import api from '@/client/api';
import type { ConfigDto, FancyEntry } from '@/client/apiGen';
import { addVersionList, genreList, musicListAll, updateAddVersionList, updateGenreList, updateMusicList } from '@/store/refs';
import { computed, ref } from 'vue';

export const FancyTab = {
  Intro: 'intro',
  Camouflage: 'camouflage',
} as const;
export type FancyTab = typeof FancyTab[keyof typeof FancyTab];

/** 已做过开场视频 / 曲目伪装的曲目（后端返回的并集） */
export const fancyEntries = ref<FancyEntry[]>([]);
export const selectedId = ref<number | null>(null);
/** 右侧当前显示编辑界面还是曲目搜索 */
export const showSearch = ref(true);
export const activeTab = ref<FancyTab>(FancyTab.Intro);
/** 列表 / 封面等文件状态变化后递增，用来让 <video>/<img> 换 URL，避免浏览器用缓存 */
export const fileToken = ref(Date.now());
/**
 * AquaMai 里对应功能是否开启。null 表示读不到配置（没装 AquaMai / 签名校验失败），
 * 这种情况不提示，免得把「读不到」误报成「没开启」。
 */
export const modFlags = ref<{ intro: boolean | null; camouflage: boolean | null }>({
  intro: null,
  camouflage: null,
});

export const selectedEntry = computed(() =>
  fancyEntries.value.find(entry => entry.id === selectedId.value) ?? null);

/** 曲目真实名称（伪装后的名字在 camouflageName 里，两者要能对照着看） */
export const musicNameMap = computed(() => {
  const map = new Map<number, string>();
  for (const music of musicListAll.value) {
    if (music.id === undefined || !music.name || map.has(music.id)) continue;
    map.set(music.id, music.name);
  }
  return map;
});

export const musicName = (id: number | null | undefined) =>
  (id ? musicNameMap.value.get(id) : undefined) ?? '';

export const refreshEntries = async () => {
  fancyEntries.value = (await api.GetFancyList()).data ?? [];
};

/** 打开某个曲目：切回编辑界面，并优先落到已经有内容的那个 tab */
export const selectMusic = (id: number) => {
  selectedId.value = id;
  showSearch.value = false;

  const entry = fancyEntries.value.find(it => it.id === id);
  if (!entry) return;
  const hasIntro = entry.hasSingle || entry.hasLeft || entry.hasRight || entry.hasAudio;
  if (!hasIntro && entry.hasCamouflage) activeTab.value = FancyTab.Camouflage;
  if (hasIntro) activeTab.value = FancyTab.Intro;
};

export const openSearch = () => {
  showSearch.value = true;
};

const loadModFlags = async () => {
  try {
    const config = (await api.GetAquaMaiConfig()).data as ConfigDto;
    const enabled = (path: string) => config.sectionStates?.[path]?.enabled === true;
    modFlags.value = {
      intro: enabled('Fancy.CustomIntroCinematic'),
      camouflage: enabled('Fancy.TrackCamouflage'),
    };
  } catch {
    modFlags.value = { intro: null, camouflage: null };
  }
};

/**
 * 页面启动：拉曲目列表、流派和添加版本（独立窗口里 store 是空的，搜索页要显示版本/流派）。
 * 已经加载过的就不再请求，同一窗口里直接进这个页面时也不浪费。
 */
export const initFancyTool = async () => {
  await Promise.all([
    musicListAll.value.length ? Promise.resolve() : updateMusicList(),
    genreList.value.length ? Promise.resolve() : updateGenreList(),
    addVersionList.value.length ? Promise.resolve() : updateAddVersionList(),
    refreshEntries(),
  ]);

  // 解析 AquaMai.dll 比较慢，不挡首屏（函数内部已经吞掉异常）
  loadModFlags();

  // 有内容就直接进第一条的编辑界面，没有就停在搜索页
  const first = fancyEntries.value[0];
  if (first?.id) selectMusic(first.id);
};
