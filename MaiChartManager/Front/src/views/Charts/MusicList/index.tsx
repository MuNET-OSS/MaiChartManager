import { computed, defineComponent, nextTick, ref } from "vue";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import { Button, Select, TextInput, theme } from "@munet/ui";
import { VList } from 'virtua/vue';
import MusicEntry from "@/views/Charts/MusicList/MusicEntry";
import { assetDirs, musicList, musicSortMode, MusicSortMode, selectedADir, selectMusicId } from "@/store/refs";
import { leftPanel } from "@/views/Charts/refs";
import { matchMusicKeyword } from "@/utils/musicSearch";
import { useI18n } from 'vue-i18n';


export const musicListRef = ref<InstanceType<typeof VList> | null>(null);

// 左侧列表的搜索词。放在模块作用域，因为跳转逻辑（scrollToMusic）要在组件之外读它。
// 切 A-dir 时刻意不清空：列表上方的搜索框会把当前词显示出来，保留它可以比较不同目录下的同一首。
export const musicSearchKeyword = ref('');
// 过滤、空态、清空按钮都用 trim 后的值，免得只输入空格时三处口径不一致
const keyword = computed(() => musicSearchKeyword.value.trim());

const clearMusicSearch = () => (musicSearchKeyword.value = '');

// 实际渲染的曲目。搜索词为空时就是当前 A-dir 的全量。
// 不能直接过滤 musicList：selectedMusic 由它派生，把列表过滤掉会让右侧编辑面板跟着变空。
export const displayedMusic = computed(() =>
  keyword.value ? musicList.value.filter(m => matchMusicKeyword(m, keyword.value)) : musicList.value
);

// 连续跳转时只让最后一次生效，避免前一次的等待把列表滚到旧位置
let lastJump = 0;
// 用户手动切到目录管理时，之前那次跳转已经不作数了
const cancelPendingJump = () => { lastJump++; };

export const scrollToMusic = async (musicId: number) => {
  const jump = ++lastJump;

  // 目标必须在当前 A-dir 里。跨目录不是这里处理的：调用方（批量页跳转）会先把 selectedADir 切到目标所在目录
  if (!musicList.value.some(m => m.id === musicId)) return;

  // 跳转的含义是「回到曲目列表并看到这首歌」：左侧停在目录管理时也要切回列表
  if (leftPanel.value !== 'musicList') leftPanel.value = 'musicList';

  // 搜索词把目标挡住了就先清掉，否则 scrollToIndex 找不到它
  if (keyword.value && !displayedMusic.value.some(m => m.id === musicId)) {
    clearMusicSearch();
  }
  await nextTick();
  if (jump !== lastJump) return;

  const list = await waitForMusicList();
  if (!list || jump !== lastJump) return;

  // 等 VList 挂载的这段时间里列表可能已经变了，下标要按最新数据重算
  const idx = displayedMusic.value.findIndex(m => m.id === musicId);
  if (idx === -1) return;
  list.scrollToIndex(idx, { align: 'center', smooth: true });
};

// 从批量操作跳过来时这个页面还在做 0.3s 的过渡，VList 尚未挂载，而调用方只等 100ms。
// 用 setTimeout 轮询而不是 requestAnimationFrame：页面在后台时 rAF 不触发，循环会一直卡住。
// 注意：改 Index.module.sass 的 tab 过渡时长、或 Charts/index.tsx 的左侧面板切换方式时，要回来确认这个超时还够。
const waitForMusicList = async (timeout = 2000) => {
  const start = Date.now();
  while (!musicListRef.value && Date.now() - start < timeout) {
    await new Promise(resolve => setTimeout(resolve, 30));
  }
  return musicListRef.value;
};

export default defineComponent({
  props: {
    toggleMenu: { type: Function, required: true },
  },
  setup(props) {
    const { t } = useI18n();
    const sortOptions = computed(() => [
      { label: t('music.list.sortById'), value: 'id' as MusicSortMode },
      { label: t('music.list.sortByName'), value: 'name' as MusicSortMode },
      { label: t('music.list.sortByVersion'), value: 'version' as MusicSortMode },
    ]);
    const selectedDirLabel = () => {
      const dir = assetDirs.value.find(d => d.dirName === selectedADir.value);
      if (!dir) return selectedADir.value;
      return <>{dir.dirName}<span class="op-70">{dir.version ? ` (Ver.${dir.version})` : ''}</span></>;
    };

    const clearSearch = clearMusicSearch;

    return () => (
      <div class="flex flex-col gap-3 h-full p-xy">
        <div class="flex items-center gap-2">
          <Button variant="secondary" onClick={() => props.toggleMenu()} class="min-[1440px]:hidden">
            <span class="i-ic-baseline-menu text-lg" />
          </Button>
          <div
            class={["grow w-0 flex items-center gap-1 px-3 py-1.5 rounded-12px transition-colors text-left truncate cursor-pointer border-none h-48px", theme.value.listItem, theme.value.listItemHover]}
            onClick={() => { cancelPendingJump(); leftPanel.value = 'assetDirs'; }}
          >
            <span class="truncate">{selectedDirLabel()}</span>
          </div>
          <Select
            v-model:value={musicSortMode.value}
            options={sortOptions.value}
            class="w-40! shrink-0"
          />
        </div>
        <div
          class="relative"
          onKeydown={(e: KeyboardEvent) => {
            // 输入法组词时按 Esc 是取消候选，不该顺手清空搜索框
            if (e.isComposing) return;
            if (e.key === 'Escape') clearSearch();
          }}
        >
          <TextInput
            v-model:value={musicSearchKeyword.value}
            placeholder={t('music.list.searchPlaceholder')}
            innerClass="min-h-12 cst pr-10"
          />
          {keyword.value &&
            <button
              class="absolute right-2 top-1/2 -translate-y-1/2 w-7 h-7 p-0 flex items-center justify-center rounded-full border-none bg-transparent c-gray-4 hover:c-gray-6 cursor-pointer"
              title={t('music.list.clearSearch')}
              onClick={clearSearch}
            >
              <span class="i-mdi:close-circle text-lg" />
            </button>}
        </div>
        <div class="relative flex-1 min-h-0">
          <VList ref={musicListRef} class="h-full cst" data={displayedMusic.value}>
            {({item}: {item: MusicXmlWithABJacket}) => (
              <MusicEntry music={item} selected={selectMusicId.value === item.id} onClick={() => selectMusicId.value = item.id!} key={item.id} />
            )}
          </VList>
          {keyword.value && !displayedMusic.value.length &&
            <div class="absolute inset-0 flex items-center justify-center c-gray-5 pointer-events-none">{t('music.list.noMatch')}</div>}
        </div>
      </div>
    )
  }
})
