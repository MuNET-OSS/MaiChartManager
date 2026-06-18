import { computed, defineComponent, ref } from "vue";
import api, { getUrl, isLocalHost } from "@/client/api";
import { globalCapture, selectedADir, selectedMusic, selectMusicId, showNeedPurchaseDialog, version } from "@/store/refs";
import { DropMenu, addToast } from "@munet/ui";
import { BlobWriter, ZipReader } from "@zip.js/zip.js";
import ChangeIdDialog from "./ChangeIdDialog";
import getSubDirFile from "@/utils/getSubDirFile";
import { useI18n } from 'vue-i18n';

enum CopyType {
  export,
  exportMaidata,
  exportMaidataIgnoreVideo,
}

export default defineComponent({
  setup() {
    const wait = ref(false);
    const showChangeId = ref(false);
    const { t } = useI18n();

    const triggerDownload = (url: string, filename: string) => {
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      a.click();
    };

    const copy = async (type: CopyType) => {
      wait.value = true;
      if (!isLocalHost || type === CopyType.exportMaidata || type === CopyType.exportMaidataIgnoreVideo) {
        // 浏览器模式，使用 zip.js 获取并解压
        let folderHandle: FileSystemDirectoryHandle;
        try {
          folderHandle = await window.showDirectoryPicker({
            id: 'copyToSaveDir',
            mode: 'readwrite'
          });
        } catch (e) {
          wait.value = false;
          console.log(e)
          return;
        }
        try {
          let url = getUrl(`${type === CopyType.export ? 'ExportOptApi' : 'ExportAsMaidataApi'}/${selectedADir.value}/${selectMusicId.value}`);
          if (type === CopyType.exportMaidataIgnoreVideo) {
            url += '?ignoreVideo=true';
          }
          const zip = await fetch(url)
          const zipReader = new ZipReader(zip.body!);
          try {
            const entries = zipReader.getEntriesGenerator();
            for await (const entry of entries) {
              console.log(entry.filename);
              if (entry.filename.endsWith('/')) {
                continue;
              }
              if (!('getData' in entry)) {
                continue;
              }
              const fileHandle = await getSubDirFile(folderHandle, entry.filename);
              const writable = await fileHandle.createWritable();
              try {
                const blob = await entry.getData(new BlobWriter());
                await writable.write(blob);
              } finally {
                await writable.close();
              }
            }
            addToast({message: t('message.exportSuccess'), type: 'success'});
          } finally {
            await zipReader.close();
          }
        } catch (e) {
          globalCapture(e, t('copy.exportError'))
        } finally {
          wait.value = false;
        }
        return;
      }
      try {
        // 本地 webview 打开，使用本地模式
        await api.RequestCopyTo({
          music: [{id: selectMusicId.value, assetDir: selectedADir.value}],
          removeEvents: false,
        });
      } finally {
        wait.value = false;
      }
    }

    const options = computed(() => [
      {
        label: t('copy.title'),
        action: () => copy(CopyType.export),
      },
      {
        label: t('copy.exportZip'),
        action: () => triggerDownload(
          getUrl(`ExportOptApi/${selectedADir.value}/${selectMusicId.value}`),
          `${selectMusicId.value} - ${selectedMusic.value?.name}.zip`,
        ),
      },
      {
        label: t('copy.exportMaidata'),
        action: () => copy(CopyType.exportMaidata),
      },
      {
        label: t('copy.exportMaidataZip'),
        action: () => triggerDownload(
          getUrl(`ExportAsMaidataApi/${selectedADir.value}/${selectMusicId.value}`),
          `${selectMusicId.value} - ${selectedMusic.value?.name} - Maidata.zip`,
        ),
      },
      {
        label: t('copy.exportMaidataNoVideo'),
        action: () => copy(CopyType.exportMaidataIgnoreVideo),
      },
      {
        label: t('copy.exportMaidataZipNoVideo'),
        action: () => triggerDownload(
          getUrl(`ExportAsMaidataApi/${selectedADir.value}/${selectMusicId.value}?ignoreVideo=true`),
          `${selectMusicId.value} - ${selectedMusic.value?.name} - Maidata.zip`,
        ),
      },
      ...(selectedADir.value === 'A000' ? [] : [{
        label: t('copy.changeId'),
        action: () => {
          if (version.value?.license !== 'Active') {
            showNeedPurchaseDialog.value = true
            return
          }
          showChangeId.value = true;
        },
      }]),
      {
        label: t('copy.showInExplorer'),
        action: () => api.RequestOpenExplorer(selectMusicId.value, selectedADir.value),
      },
      {
        label: t('copy.editXml'),
        action: () => api.RequestOpenXml(selectMusicId.value, selectedADir.value),
      },
    ]);

    return () =>
      <div class="flex">
        <DropMenu options={options.value} buttonText={t('copy.copyAndExport')}>
        </DropMenu>
        <ChangeIdDialog v-model:show={showChangeId.value}/>
      </div>
  }
})
