import { defineComponent, ref, Transition, watch } from 'vue';
import MusicList from './MusicList';
import MusicEdit from './MusicEdit';
import MusicSelectedTopRightToolbar from './MusicSelectedTopRightToolbar';
import AssetDirsManager from './AssetDirsManager';
import ImportCreateChartButton from './ImportCreateChartButton';
import CopyToButton from './CopyToButton';
import MemoBox from './AssetDirsManager/MemoBox';
import TransitionOpacity from '@/components/TransitionOpacity';
import { useI18n } from 'vue-i18n';
import { isReadOnlyDir, selectedMusic } from '@/store/refs';
import { leftPanel, musicDrawerOpen, rightPanel } from './refs';
import styles from './index.module.sass';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const isForward = ref(true);
    watch(leftPanel, (val) => {
      isForward.value = val === 'assetDirs';
    });

    // 抽屉布局用 max-[1439px]：抽屉与下面的 min-[1440px]:hidden（汉堡按钮）必须以 1440 为界、
    // 不能互相重叠。之前两边都写 1440，窗口宽度正好 1440（例如 1440 宽的屏幕最大化）时
    // 左侧既被推出屏幕、汉堡按钮又被隐藏，谱面管理页直接没法用。
    return () => <div class="grid cols-[40em_1fr] max-[1439px]:cols-1">
      <div class={[
        'h-100dvh',
        'max-[1439px]:absolute max-[1439px]:left-16 max-[1439px]:w-40em max-[1439px]:max-w-[calc(100dvw-64px)] max-[1439px]:z-10 transition-transform duration-300',
        'max-[1439px]:bg-white max-[1439px]:border-r-solid max-[1439px]:border-r-1 max-[1439px]:border-r-gray-200',
        'max-[767px]:left-0 max-[767px]:max-w-100dvw',
        musicDrawerOpen.value ? 'max-[1439px]:translate-x-0' : 'max-[1439px]:translate-x-[-100%]',
      ]}>
        <div class="relative h-full of-hidden">
          <Transition
            enterActiveClass={styles.enterActive}
            leaveActiveClass={styles.leaveActive}
            enterFromClass={isForward.value ? styles.forwardEnterFrom : styles.backwardEnterFrom}
            leaveToClass={isForward.value ? styles.forwardLeaveTo : styles.backwardLeaveTo}
          >
            {leftPanel.value === 'musicList'
              ? <MusicList toggleMenu={() => (musicDrawerOpen.value = false)} key="musicList" />
              : <AssetDirsManager key="assetDirs" />
            }
          </Transition>
        </div>
      </div>
      <TransitionOpacity>
        {musicDrawerOpen.value && (
          <div
            class="min-[1440px]:hidden absolute-full z-5 bg-white/70 backdrop-blur-sm"
            onClick={() => (musicDrawerOpen.value = false)}
          />
        )}
      </TransitionOpacity>
      <div
        class="flex flex-col h-100dvh of-hidden"
        style={{ background: 'linear-gradient(90deg, transparent 0%, rgba(255, 255, 255, 0.1) 16px, rgba(255, 255, 255, 0.1) calc(100% - 16px), transparent 100%)' }}
      >
        {rightPanel.value === 'memoEdit' ? (
          <MemoBox />
        ) : (
          <div class="flex flex-col gap-4 p-y h-full">
            <div class="flex flex-wrap items-center gap-2 shrink-0 px">
              <button onClick={() => (musicDrawerOpen.value = true)} class="min-[1440px]:hidden">
                <span class="i-ic-baseline-menu text-lg" />
              </button>
              <div class="grow-1" />
              <div class="flex flex-col items-end gap-1 max-[639px]:w-full">
                <div class="flex flex-wrap items-center justify-end gap-2 max-[639px]:w-full">
                  {!!selectedMusic.value && <div class="shrink-0"><CopyToButton /></div>}
                  {/* A000 是游戏本体目录，不允许编辑：按钮保留但禁用，下面用灰字说明原因 */}
                  <div class={['flex flex-wrap items-center justify-end gap-2', isReadOnlyDir.value && 'pointer-events-none op-50']}>
                    <div class="flex gap-2 shrink-0"><MusicSelectedTopRightToolbar /></div>
                    <div class="shrink-0"><ImportCreateChartButton /></div>
                  </div>
                </div>
                {isReadOnlyDir.value && <div class="text-xs c-gray-5">{t('assetDir.selectNonA000')}</div>}
              </div>
            </div>
            <div class="of-y-auto cst grow-1 px">
              {selectedMusic.value
                ? <MusicEdit />
                : <div class="h-full flex items-center justify-center text-2xl op-50 select-none">{t('music.edit.selectHint')}</div>}
            </div>
          </div>
        )}
      </div>
    </div>;
  },
});
