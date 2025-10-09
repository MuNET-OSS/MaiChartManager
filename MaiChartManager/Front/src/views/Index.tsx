import { defineComponent, onMounted, ref } from 'vue';
import { NButton, NFlex, NScrollbar, useDialog, useNotification } from "naive-ui";
import MusicList from "@/components/MusicList";
import GenreVersionManager from "@/components/GenreVersionManager";
import { globalCapture, selectedADir, selectedMusic, updateAddVersionList, updateAll, updateAssetDirs, updateGenreList, updateMusicList, updateVersion, version } from "@/store/refs";
import MusicEdit from "@/components/MusicEdit";
import MusicSelectedTopRightToolbar from "@/components/MusicSelectedTopRightToolbar";
import ModManager from "@/components/ModManager";
import VersionInfo from "@/components/VersionInfo";
import { captureException } from "@sentry/vue";
import AssetDirsManager from "@/components/AssetDirsManager";
import ImportCreateChartButton from "@/components/ImportCreateChartButton";
import { HardwareAccelerationStatus, LicenseStatus } from "@/client/apiGen";
import CopyToButton from "@/components/CopyToButton";
import TransitionOpacity from '@/components/TransitionOpacity';
import Tools from '@/components/Tools';

export default defineComponent({
  setup() {
    const notification = useNotification();
    const dialog = useDialog();

    onMounted(async () => {
      document.title = `MaiChartManager (${location.host})`
      addEventListener("unhandledrejection", (event) => {
        console.log(event)
        captureException(event.reason?.error || event.reason, {
          tags: {
            context: 'unhandledrejection'
          }
        })
      });

      if (window.showDirectoryPicker === undefined) {
        const showError = () => {
          dialog.error({
            title: '警告：不支持的浏览器',
            content: '部分功能可能受到限制，请使用最新版电脑端的 Chrome 或 Edge 浏览器',
            positiveText: '知道了',
          })
        }
        window.showDirectoryPicker = () => {
          showError()
          throw new DOMException('不支持的浏览器', 'AbortError')
        }
        showError()
      }

      updateVersion().then(() => {
        if (version.value?.license === LicenseStatus.Pending || version.value?.hardwareAcceleration === HardwareAccelerationStatus.Pending) {
          setTimeout(updateVersion, 2000)
        }
      })
      try {
        await updateAll();
      } catch (err) {
        globalCapture(err, "初始化失败")
      }
    })

    const mobileShowMenu = ref(false);

    return () => <NFlex justify="center">
      <div class="grid cols-[40em_1fr] w-[min(100rem,100%)] max-[1440px]:cols-1">
        <div class={[
          "p-xy h-100dvh",
          "max-[1440px]:absolute max-[1440px]:left-0 max-[1440px]:w-40em max-[1440px]:max-w-100dvw  z-10 transition-transform duration-300",
          "max-[1440px]:bg-white max-[1440px]:border-r-solid max-[1440px]:border-r-1 max-[1440px]:border-r-gray-200",
          mobileShowMenu.value ? "max-[1440px]:translate-x-0" : "max-[1440px]:translate-x-[-100%]"
        ]}>
          <MusicList toggleMenu={() => mobileShowMenu.value = false} />
        </div>
        <TransitionOpacity>
          {mobileShowMenu.value && <div class={["min-[1440px]:hidden absolute-full z-5 bg-white/70 backdrop-blur-sm"]} onClick={() => mobileShowMenu.value = false} />}
        </TransitionOpacity>
        <NFlex vertical class="p-xy h-100dvh" size="large" style={{ background: 'linear-gradient(90deg, transparent 0%, rgba(255, 255, 255, 0.1) 16px, rgba(255, 255, 255, 0.1) calc(100% - 16px), transparent 100%)' }}>
          <NFlex class="shrink-0" align="center">
            <NButton secondary onClick={() => mobileShowMenu.value = true} class="min-[1440px]:hidden">
              <span class="i-ic-baseline-menu text-lg" />
            </NButton>
            <AssetDirsManager />
            {selectedADir.value !== 'A000' && <>
              <GenreVersionManager />
            </>}
            <ModManager />

            <div class="grow-1" />

            {!!selectedMusic.value && <CopyToButton />}
            {selectedADir.value === 'A000' ?
              '请选择一个 A000 以外的目录来编辑' :
              <>
                <MusicSelectedTopRightToolbar />
                <ImportCreateChartButton />
              </>}
            <Tools />
            <VersionInfo />
          </NFlex>
          <NScrollbar class="grow-1">
            <MusicEdit />
          </NScrollbar>
        </NFlex>
      </div>
    </NFlex>;
  },
});
