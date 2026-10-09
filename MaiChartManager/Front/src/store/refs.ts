import { computed, ref, watch } from "vue";
import { AppVersionResult, ConfigDto, GameModInfo, GenreXml, GetAssetsDirsResult, MusicXmlWithABJacket, VersionXml } from "@/client/apiGen";
import api from "@/client/api";
import { captureException } from "@sentry/vue";
import posthog from "posthog-js";
import { useStorage, useWindowFocus, whenever } from "@vueuse/core";
import { locale } from "@/locales";
import { updateAquaMaiConfig } from "@/views/ModManager/refs";
import { updateSettings } from "@/store/settings";
import { SidebarItem } from "@/components/Sidebar";
import { eagerFetchChangelog, updateAppUpdateInfo, updateModUpdateInfo } from "@/store/appUpdate";

export const sidebarActive = ref<SidebarItem>('charts');

export const error = ref();
export const errorId = ref<string>();
export const errorContext = ref<string>();

export const globalCapture = async (err: any, context: string) => {
  console.log(err)
  // Response 上没有 error 字段，这里主动挂一个可读的错误信息，供下面上报时读取
  const response = err instanceof Response ? err as Response & { error?: string } : undefined;
  if (response && !response.error) {
    if (!response.bodyUsed) {
      const errText = response.error = await response.text();
      try {
        const json = JSON.parse(errText);
        if (json.exception.details && json.detail) {
          response.error = json.detail + '\n' + json.exception.details;
        }
      } catch {
      }
    }
  }
  error.value = err;
  errorContext.value = context;
  errorId.value = captureException(err.error || err, {
    tags: {
      context
    }
  })
  posthog.capture('error: ' + context, {
    err,
    errorId: errorId.value,
    message: error.value?.error?.message || error.value?.error?.toString() || error.value?.message || error.value?.toString(),
  })
}

export const showNeedPurchaseDialog = ref(false);

export const selectMusicId = ref(0)
export const genreList = ref<GenreXml[]>([]);
export const addVersionList = ref<VersionXml[]>([]);
export const selectedADir = useStorage<string>('selectedADir', 'A000');
export const musicListAll = ref<MusicXmlWithABJacket[]>([]);
export const assetDirs = ref<GetAssetsDirsResult[]>([]);
export const version = ref<AppVersionResult>();
export const modInfo = ref<GameModInfo>();

export type MusicSortMode = 'id' | 'name' | 'version';
export const musicSortMode = useStorage<MusicSortMode>('musicSortMode', 'id');

const sortByNonDxId = (a: MusicXmlWithABJacket, b: MusicXmlWithABJacket) =>
  (a.id! % 10000) - (b.id! % 10000);

const musicSortComparators: Record<MusicSortMode, (a: MusicXmlWithABJacket, b: MusicXmlWithABJacket) => number> = {
  id: sortByNonDxId,
  name: (a, b) => (a.sortName ?? '').localeCompare(b.sortName ?? ''),
  version: (a, b) => (a.version ?? 0) - (b.version ?? 0) || sortByNonDxId(a, b),
};

export const musicList = computed(() =>
  musicListAll.value
    .filter(m => m.assetDir === selectedADir.value)
    .sort(musicSortComparators[musicSortMode.value] ?? sortByNonDxId)
);
export const selectedMusic = computed(() => musicList.value.find(m => m.id === selectMusicId.value));
export const selectedLevel = ref(0);

// 如ReplaceChart等后端接口可能会涉及对MusicXml中的信息进行修改后保存。此时前端updateMusicList时会发现相关数据出现变更，触发了MusicEdit/ChartPanel中的watch，造成发送多余的edit请求、同时modified也被错误设置为true。
// 因此，对于涉及内部对xml进行修改后会自动保存的后端接口，可以在updateMusicList期间打开本选项，以阻止MusicEdit/ChartPanel中的sync动作。
export const disableSync = ref(false);

export { aquaMaiConfig } from "@/views/ModManager/refs";

export const saveMusicIfNeeded = async (id: number) => {
  if (!id) return;
  const music = musicListAll.value.find(m => m.id === id);
  if (!music?.modified) return;
  await api.SaveMusic(id, selectedADir.value);
  await updateMusicList();
}

const focused = useWindowFocus()

whenever(() => !focused.value, () => {
  saveMusicIfNeeded(selectMusicId.value);
})

watch(selectMusicId, async (n, o) => {
  if (n === o) return;
  if (!o) return;
  await saveMusicIfNeeded(o);
})

export const updateGenreList = async () => {
  const response = await api.GetAllGenres();
  genreList.value = response.data;
}

export const updateAddVersionList = async () => {
  const response = await api.GetAllAddVersions();
  addVersionList.value = response.data;
}

export const updateMusicList = async (disableAutoSync=false) => {
  const data = (await api.GetMusicList()).data;
  if (disableAutoSync) disableSync.value = true;
  musicListAll.value = data;
  setTimeout(()=>disableSync.value = false); // timeout=0表示在下一帧执行
}

export const updateAssetDirs = async () => {
  assetDirs.value = (await api.GetAssetsDirs()).data;
}

export const updateVersion = async () => {
  version.value = (await api.GetAppVersion()).data;
  locale.value = version.value?.locale || 'en';
  // 预加载当前版本的更新日志
  if (version.value?.version) {
    eagerFetchChangelog(version.value.version);
  }
}

export const updateModInfo = async () => {
  modInfo.value = (await api.GetGameModInfo()).data;
}

export const updateAll = async () => Promise.all([
  updateVersion(),
  updateGenreList(),
  updateAddVersionList(),
  updateAssetDirs(),
  updateMusicList(),
  updateModInfo(),
  updateModUpdateInfo(),
  updateAppUpdateInfo(),
  updateAquaMaiConfig(),
  updateSettings(),
])

export const gameVersion = computed(() => version.value?.gameVersion || 45)
// 从1.60起，b15算最近两个版本
export const b15ver = computed(() => 20000 + (gameVersion.value >= 60 ? gameVersion.value - 5 : gameVersion.value) * 100);
