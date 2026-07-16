import { computed, defineComponent, PropType, ref } from "vue";
import { HttpResponse, MusicXmlWithABJacket } from "@/client/apiGen";
import { Button, Modal, NumberInput, Popover, showTransactionalDialog } from "@munet/ui";
import { globalCapture, selectedADir } from "@/store/refs";
import FileTypeIcon from "@/components/FileTypeIcon";
import BottomOverlay from "@/components/BottomOverlay";
import api, { getUrl } from "@/client/api";
import AudioPreviewEditorButton from "@/views/Charts/MusicEdit/AudioPreviewEditorButton";
import SetMovieButton from "@/views/Charts/MusicEdit/SetMovieButton";
import { t } from "@/locales";
import { pickFile } from "@/utils/pickFile";


export let uploadFlow = async (fileHandle?: FileSystemFileHandle) => {
}

export default defineComponent({
  props: {
    song: { type: Object as PropType<MusicXmlWithABJacket>, required: true },
  },
  setup(props) {
    const updateTime = ref(0)
    const url = computed(() => getUrl(`GetMusicWavApi/${selectedADir.value}/${props.song.cueId}?${updateTime.value}`))
    const tipShow = ref(false)
    const tipSelectAwbShow = ref(false)
    const setOffsetShow = ref(false)
    const offset = ref(0)
    const load = ref(false)
    const okResolve = ref<Function>(() => {
    })

    const cueIdNotMatch = computed(() => props.song.nonDxId !== props.song.cueId)
    const movieIdNotMatch = computed(() => props.song.nonDxId !== props.song.movieId)

    uploadFlow = async (fileHandle?: FileSystemFileHandle) => {
      tipShow.value = true
      try {
        let file: File;
        if (!fileHandle) {
          // 音频文件，使用通用单文件选择（兼容 WebKitGTK）
          const picked = await pickFile('.mp3,.wav,.ogg,.acb');
          tipShow.value = false;
          if (!picked) return;
          file = picked;
        } else {
          tipShow.value = false;
          file = await fileHandle.getFile() as File;
        }

        let res: HttpResponse<any>;
        if (file.name.endsWith('.acb')) {
          tipSelectAwbShow.value = true;
          // 对应的 awb 文件，使用通用单文件选择（兼容 WebKitGTK）
          const awb = await pickFile('.awb');
          tipSelectAwbShow.value = false;
          if (!awb) return;

          load.value = true;
          res = await api.SetAudio(props.song.id!, selectedADir.value, { file, awb, padding: 0 });
        } else {
          offset.value = 0;
          setOffsetShow.value = true;
          await new Promise((resolve) => {
            okResolve.value = resolve;
          });
          load.value = true;
          setOffsetShow.value = false;
          res = await api.SetAudio(props.song.id!, selectedADir.value, { file, padding: offset.value });
        }
        if (res.error) {
          const error = res.error as any;
          showTransactionalDialog(t('jacket.setFailed'), error.message || error, undefined, true);
          return;
        }
        updateTime.value = Date.now()
        props.song.isAcbAwbExist = true;
      } catch (e: any) {
        if (e.name === 'AbortError') return
        console.log(e)
        globalCapture(e, t('music.edit.audioImportError'))
      } finally {
        tipShow.value = false;
        tipSelectAwbShow.value = false;
        setOffsetShow.value = false;
        load.value = false;
      }
    }

    return () => <div class="flex flex-wrap items-center gap-2 mt-2">
      {props.song.isAcbAwbExist && <audio controls src={url.value} class="w-full min-[640px]:w-0 min-[640px]:grow"/>}
      {selectedADir.value !== 'A000' && <div class="shrink-0">{cueIdNotMatch.value
        ? <Popover trigger="hover">{{
            trigger: () => <Button variant="secondary" class={`${!props.song.isAcbAwbExist && "w-full"}`} onClick={() => uploadFlow()} ing={load.value} disabled>{props.song.isAcbAwbExist ? t('music.edit.replaceAudio') : t('music.edit.setAudio')}</Button>,
            default: () => t('music.edit.cueIdNotMatch')
          }}</Popover>
        : <Button variant="secondary" class={`${!props.song.isAcbAwbExist && "w-full"}`} onClick={() => uploadFlow()} ing={load.value}>{props.song.isAcbAwbExist ? t('music.edit.replaceAudio') : t('music.edit.setAudio')}</Button>
      }</div>}
      {selectedADir.value !== 'A000' && props.song.isAcbAwbExist && <div class="shrink-0">{cueIdNotMatch.value
        ? <Popover trigger="hover">{{
            trigger: () => <AudioPreviewEditorButton disabled/>,
            default: () => t('music.edit.cueIdNotMatchPreview')
          }}</Popover>
        : <AudioPreviewEditorButton/>
      }</div>}
      {selectedADir.value !== 'A000' && props.song.isAcbAwbExist && <div class="shrink-0">{movieIdNotMatch.value
        ? <Popover trigger="hover">{{
            trigger: () => <SetMovieButton song={props.song} disabled/>,
            default: () => t('music.edit.movieIdNotMatch')
          }}</Popover>
        : <SetMovieButton song={props.song}/>
      }</div>}

      {/* 打开文件对话框一般在左上角，所以在下边显示一个 Drawer */}
      <BottomOverlay title={t('music.edit.selectFileTypes')} show={tipShow.value}>
        <div class="grid cols-4 justify-items-center text-8em gap-10">
          <FileTypeIcon type="WAV"/>
          <FileTypeIcon type="MP3"/>
          <FileTypeIcon type="OGG"/>
          <FileTypeIcon type="ACB"/>
        </div>
      </BottomOverlay>
      <BottomOverlay title={t('music.edit.selectAwb')} show={tipSelectAwbShow.value}/>
      <Modal
        width="min(30vw,25em)"
        title={t('music.edit.setOffsetSeconds')}
        v-model:show={setOffsetShow.value}
      >{{
        default: () => <div class="flex flex-col gap-3">
          <div>{t('music.edit.audioOffsetHint')}</div>
          <NumberInput v-model:value={offset.value} class="w-full" step={0.0001} decimal={4}/>
        </div>,
        actions: () => <button class="w-0 grow" onClick={okResolve.value as any}>{t('common.confirm')}</button>
      }}</Modal>
    </div>
  }
})
