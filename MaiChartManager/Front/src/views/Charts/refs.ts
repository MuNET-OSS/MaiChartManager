import { ref } from "vue";
import { GetAssetsDirsResult } from "@/client/apiGen";

export type LeftPanel = 'musicList' | 'assetDirs';
export type RightPanel = 'musicEdit' | 'memoEdit';

export const leftPanel = ref<LeftPanel>('musicList');
export const rightPanel = ref<RightPanel>('musicEdit');

// 窄屏下左侧列表是抽屉，默认关着。放在这里是因为「批量操作 → 跳转」要在组件之外把它拉开，
// 否则跳过去只是切了面板、抽屉还关着，用户看不到目标曲目。
export const musicDrawerOpen = ref(false);

// memo editing state
export const editingMemoDir = ref<GetAssetsDirsResult>();
export const editingMemoName = ref('');
