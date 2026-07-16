import { computed, defineComponent, ref, Transition, watch } from 'vue';
import MusicList from './MusicList';
import MusicEdit from './MusicEdit';
import MusicSelectedTopRightToolbar from './MusicSelectedTopRightToolbar';
import AssetDirsManager from './AssetDirsManager';
import ImportCreateChartButton from './ImportCreateChartButton';
import CopyToButton from './CopyToButton';
import MemoBox from './AssetDirsManager/MemoBox';
import TransitionOpacity from '@/components/TransitionOpacity';
import { useI18n } from 'vue-i18n';
import { selectedADir, selectedMusic } from '@/store/refs';
import { leftPanel, rightPanel } from './refs';
import styles from './index.module.sass';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const mobileShowMenu = ref(false);
    const isForward = ref(true);
    watch(leftPanel, (val) => {
      isForward.value = val === 'assetDirs';
    });

    return () => <div class="grid cols-[40em_1fr] max-[1440px]:cols-1">
      <div class={[
        'h-100dvh',
        'max-[1440px]:absolute max-[1440px]:left-16 max-[1440px]:w-40em max-[1440px]:max-w-[calc(100dvw-64px)] max-[1440px]:z-10 transition-transform duration-300',
        'max-[1440px]:bg-white max-[1440px]:border-r-solid max-[1440px]:border-r-1 max-[1440px]:border-r-gray-200',
        'max-[767px]:left-0 max-[767px]:max-w-100dvw',
        mobileShowMenu.value ? 'max-[1440px]:translate-x-0' : 'max-[1440px]:translate-x-[-100%]',
      ]}>
        <div class="relative h-full of-hidden">
          <Transition
            enterActiveClass={styles.enterActive}
            leaveActiveClass={styles.leaveActive}
            enterFromClass={isForward.value ? styles.forwardEnterFrom : styles.backwardEnterFrom}
            leaveToClass={isForward.value ? styles.forwardLeaveTo : styles.backwardLeaveTo}
          >
            {leftPanel.value === 'musicList'
              ? <MusicList toggleMenu={() => (mobileShowMenu.value = false)} key="musicList" />
              : <AssetDirsManager key="assetDirs" />
            }
          </Transition>
        </div>
      </div>
      <TransitionOpacity>
        {mobileShowMenu.value && (
          <div
            class="min-[1440px]:hidden absolute-full z-5 bg-white/70 backdrop-blur-sm"
            onClick={() => (mobileShowMenu.value = false)}
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
              <button onClick={() => (mobileShowMenu.value = true)} class="min-[1440px]:hidden">
                <span class="i-ic-baseline-menu text-lg" />
              </button>
              <div class="grow-1" />
              <div class="flex flex-wrap items-center justify-end gap-2 max-[639px]:w-full">
                {!!selectedMusic.value && <div class="shrink-0"><CopyToButton /></div>}
                {selectedADir.value === 'A000' ? (
                  t('assetDir.selectNonA000')
                ) : (
                  <>
                    <div class="flex gap-2 shrink-0"><MusicSelectedTopRightToolbar /></div>
                    <div class="shrink-0"><ImportCreateChartButton /></div>
                  </>
                )}
              </div>
            </div>
            <div class="of-y-auto cst grow-1 px">
              <MusicEdit />
            </div>
          </div>
        )}
      </div>
    </div>;
  },
});
