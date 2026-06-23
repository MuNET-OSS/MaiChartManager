import { defineComponent, ref } from "vue";
import { Button, Modal, Progress, addToast } from "@munet/ui";
import api, { getUrl, isLocalHost } from "@/client/api";
import { updateAssetDirs } from "@/store/refs";
import axios from "axios";
import { UploadAssetDirResult } from "@/client/apiGen";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup(props) {
    const importWait = ref(false);
    const showProgress = ref(false);
    const progress = ref(0);
    const { t } = useI18n();

    const importLocal = async () => {
      importWait.value = true;
      if (!isLocalHost) {
        // 浏览器模式
        let folderHandle: FileSystemDirectoryHandle;
        try {
          folderHandle = await window.showDirectoryPicker({
            id: 'uploadOptDir',
          });
        } catch (e) {
          importWait.value = false;
          console.log(e)
          return;
        }
        try {
          const data = new FormData();

          const processSubDir = async (dirName: string, folder: FileSystemDirectoryHandle) => {
            for await (const fileHandle of folder.values()) {
              if (fileHandle.kind === 'directory') {
                await processSubDir(`${dirName}/${fileHandle.name}`, fileHandle);
              } else {
                const file = await fileHandle.getFile();
                data.append('files[]', new File([file], `${dirName}/${fileHandle.name}`));
              }
            }
          }

          await processSubDir('', folderHandle);

          progress.value = 0;
          showProgress.value = true;
          const res = await axios.post<UploadAssetDirResult>(getUrl(`UploadAssetDirApi/${folderHandle.name}`), data, {
            onUploadProgress: data => {
              progress.value = Math.floor((data.progress || 0) * 100);
            },
            responseType: 'json'
          })
          showProgress.value = false;
          addToast({message: t('message.importSuccess') + ` ${res.data.dirName}`, type: 'success'});
          updateAssetDirs();
        } catch (e) {
          console.log(e)
        } finally {
          importWait.value = false;
          showProgress.value = false;
        }
        return;
      }
      try {
        // 本地 webview 打开，使用本地模式
        await api.RequestLocalImportDir();
      } finally {
        importWait.value = false;
        updateAssetDirs();
      }
    }

    return () => <Button onClick={importLocal} ing={importWait.value}>
      {t('common.import')}
      <Modal
        width="min(60vw,80em)"
        title={t('music.batch.progress')}
        show={showProgress.value}
        esc={false}
      >
        <Progress
          percentage={progress.value}
          status="success"
          showIndicator
        />
      </Modal>
    </Button>;
  }
})
