import { defineComponent, PropType, ref } from "vue";
import { Button, Modal, NumberInput, Progress, addToast, showTransactionalDialog } from "@munet/ui";
import FileTypeIcon from "@/components/FileTypeIcon";
import BottomOverlay from "@/components/BottomOverlay";
import { LicenseStatus, MusicXmlWithABJacket } from "@/client/apiGen";
import api, { getUrl } from "@/client/api";
import { globalCapture, selectedADir, showNeedPurchaseDialog, version } from "@/store/refs";
import { fetchEventSource } from "@microsoft/fetch-event-source";
import { handleSseOpen } from "@/utils/sseOpen";
import { t } from "@/locales";
import { pickFile } from "@/utils/pickFile";

enum STEP {
  None,
  Select,
  Offset,
  Progress,
}

export let uploadFlow = async (fileHandle?: FileSystemFileHandle) => {
}

export default defineComponent({
  props: {
    disabled: Boolean,
    song: { type: Object as PropType<MusicXmlWithABJacket>, required: true },
  },
  setup(props) {
    const offset = ref(0)
    const load = ref(false)
    const okResolve = ref<Function>(() => {
    })

    const step = ref(STEP.None)
    const progress = ref(0)



    const uploadMovie = (id: number, movie: File, offset: number) => new Promise<void>((resolve, reject) => {
      progress.value = 0;
      const body = new FormData();
      body.append('padding', offset.toString());
      body.append('file', movie);
      body.append('file', movie);
      const controller = new AbortController();
      fetchEventSource(getUrl(`SetMovieApi/${selectedADir.value}/${id}`), {
        signal: controller.signal,
        method: 'PUT',
        body,
        onopen: handleSseOpen,
        onerror(e) {
          reject(e);
          controller.abort();
          throw new Error("disable retry onerror");
        },
        onclose() {
          reject(new Error("EventSource Close"));
          controller.abort();
          throw new Error("disable retry onclose");
        },
        openWhenHidden: true,
        onmessage: (e) => {
          switch (e.event) {
            case 'Progress':
              progress.value = parseInt(e.data);
              break;
            case 'Success':
              console.log("success")
              controller.abort();
              resolve();
              break;
            case 'Error':
              controller.abort();
              reject(new Error(e.data));
              break;
          }
        }
      });
    })

    uploadFlow = async (fileHandle?: FileSystemFileHandle) => {
      step.value = STEP.Select
      try {
        let file: File;
        if (!fileHandle) {
          // 视频/图片文件，使用通用单文件选择（兼容 WebKitGTK）
          const picked = await pickFile('video/*,image/*,.dat');
          step.value = STEP.None
          if (!picked) return;
          file = picked;
        } else {
          step.value = STEP.None
          file = await fileHandle.getFile() as File;
        }

        if (file.name.endsWith('.dat')) {
          load.value = true;
          const confirmed = await showTransactionalDialog(
            t('common.confirm'),
            t('music.edit.confirmSetMovie', { filename: file.name }),
            [{ text: t('common.confirm'), action: true }, { text: t('common.cancel'), action: false }]
          );
          if (!confirmed) throw new DOMException('', 'AbortError');
          await api.SetMovie(props.song.id!, selectedADir.value, { file, padding: 0 });
        } else if (version.value?.license !== LicenseStatus.Active) {
          showNeedPurchaseDialog.value = true;
        } else {
          offset.value = 0;
          if (file.type.startsWith("video/")) {
            step.value = STEP.Offset
            await new Promise((resolve) => {
              okResolve.value = resolve;
            });
          }
          load.value = true;
          progress.value = 0;
          step.value = STEP.Progress
          await uploadMovie(props.song.id!, file, offset.value);
          console.log("upload movie success")
          addToast({message: t('message.saveSuccess'), type: 'success'})
        }
      } catch (e: any) {
        if (e?.name === 'AbortError') return
        console.log(e)
        globalCapture(e, t('music.edit.importPvError'))
      } finally {
        step.value = STEP.None
        load.value = false;
      }
    }

    return () => <Button variant="secondary" onClick={() => uploadFlow()} ing={load.value} disabled={props.disabled}>
      {t('music.edit.setPv')}

      <BottomOverlay title={t('music.edit.selectFileTypes')} show={step.value === STEP.Select}>
        <div class="flex flex-col gap-2 items-center">
          <div>{t('music.edit.pvFileHint')}</div>
          <div class="grid cols-4 justify-items-center text-8em gap-10">
            <FileTypeIcon type="MP4"/>
            <FileTypeIcon type="JPG"/>
            <FileTypeIcon type="DAT"/>
          </div>
        </div>
      </BottomOverlay>
      <Modal
        width="min(30vw,30em)"
        title={t('music.edit.setOffsetSeconds')}
        show={step.value === STEP.Offset}
        onUpdateShow={() => step.value = STEP.None}
      >{{
        default: () => <div class="flex flex-col gap-3">
          <div>{t('music.edit.offsetHint')}</div>
          <NumberInput v-model:value={offset.value} class="w-full" step={0.0001} decimal={4}/>
        </div>,
        actions: () => <button class="w-0 grow" onClick={okResolve.value as any}>{t('common.confirm')}</button>
      }}</Modal>
      <Modal
        width="min(40vw,40em)"
        title={t('tools.converting')}
        show={step.value === STEP.Progress}
        esc={false}
      >
        <div class="flex flex-col gap-2">
          <Progress status="success" percentage={progress.value} showIndicator/>
          <div class="text-center text-sm">{progress.value === 100 ? t('tools.videoOptions.processing') : `${progress.value}%`}</div>
        </div>
      </Modal>
    </Button>;
  }
})
