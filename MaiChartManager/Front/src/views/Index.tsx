import { defineComponent, onMounted, ref } from 'vue';
import { showTransactionalDialog } from '@munet/ui';
import GenreVersionManager from './GenreVersionManager';
import { globalCapture, updateAll, updateVersion, version, assetDirs, selectedADir, sidebarActive } from '@/store/refs';
import ModManager from '@/views/ModManager';
import { captureException } from '@sentry/vue';
import { HardwareAccelerationStatus, LicenseStatus } from '@/client/apiGen';
import { useI18n } from 'vue-i18n';
import DragDropDispatcher, { mainDivRef } from '@/components/DragDropDispatcher';
import Sidebar, { SidebarItem } from '@/components/Sidebar';
import BatchActionButton from '@/views/BatchAction';
import Charts from './Charts';
import Tools from './Tools';
import Settings from './Settings';
import Splash from '@/components/Splash';
import { ensureBackendUrl } from '@/utils/ensureBackendUrl';
import ChangelogModal from '@/components/ChangelogModal';
import { isLocalHost } from '@/client/api';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const loaded = ref(false);

    onMounted(async () => {
      document.title = `MaiChartManager (${location.host})`;
      addEventListener('unhandledrejection', (event) => {
        console.log(event);
        captureException(event.reason?.error || event.reason, {
          tags: { context: 'unhandledrejection' },
        });
      });

      // 本地宿主（Windows WebView2 / Photino）下目录操作走后端原生对话框，不需要 File System Access API；
      // 仅当是远程浏览器时才检测并提示浏览器不支持
      if (!isLocalHost && window.showDirectoryPicker === undefined) {
        const showError = () => {
          showTransactionalDialog(
            t('error.browserUnsupported.title'),
            t('error.browserUnsupported.content'),
            [{ text: t('error.browserUnsupported.confirm'), action: true }],
          );
        };
        window.showDirectoryPicker = () => {
          showError();
          throw new DOMException(t('error.browserUnsupported.title'), 'AbortError');
        };
        showError();
      }

      await ensureBackendUrl();
      updateVersion().then(() => {
        if (version.value?.license === LicenseStatus.Pending || version.value?.hardwareAcceleration === HardwareAccelerationStatus.Pending) {
          setTimeout(updateVersion, 2000);
        }
      });
      try {
        await updateAll();
        if (assetDirs.value.length > 0) {
          const exists = assetDirs.value.some(d => d.dirName === selectedADir.value)
          if (!exists) {
            selectedADir.value = assetDirs.value[assetDirs.value.length - 1].dirName!
          }
        }
        loaded.value = true;
      } catch (err) {
        loaded.value = true;
        globalCapture(err, t('error.initFailed'));
      }
    });


    return () => (
      <div class="" ref={mainDivRef as any}>
        <DragDropDispatcher />
        <Splash show={!loaded.value} />
        <ChangelogModal ready={loaded.value} />
        <div class={['grid cols-1 pb-14 md:pb-0 md:cols-[auto_1fr]']}>
          <Sidebar v-model:active={sidebarActive.value} />
          {sidebarActive.value === 'charts' && <Charts />}
          {sidebarActive.value === 'mods' && <ModManager />}
          {sidebarActive.value === 'genres' && <GenreVersionManager />}
          {sidebarActive.value === 'batch' && <BatchActionButton />}
          {sidebarActive.value === 'tools' && <Tools />}
          {sidebarActive.value === 'settings' && <Settings />}
        </div>
      </div>
    );
  },
});
