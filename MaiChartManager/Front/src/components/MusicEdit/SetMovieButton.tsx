import { defineComponent, PropType, ref } from "vue";
import { NButton, NCheckbox, NDrawer, NDrawerContent, NFlex, NFormItem, NInputNumber, NModal, NProgress, NSelect, useDialog, useMessage } from "naive-ui";
import FileTypeIcon from "@/components/FileTypeIcon";
import { LicenseStatus, MusicXmlWithABJacket } from "@/client/apiGen";
import api, { getUrl } from "@/client/api";
import { aquaMaiConfig, globalCapture, selectedADir, showNeedPurchaseDialog, version } from "@/store/refs";
import { fetchEventSource } from "@microsoft/fetch-event-source";
import { defaultSavedOptions, MOVIE_CODEC } from "@/components/ImportCreateChartButton/ImportChartButton/types";
import { useStorage } from "@vueuse/core";

enum STEP {
  None,
  Select,
  Offset,
  Progress,
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
    const dialog = useDialog();
    const step = ref(STEP.None)
    const progress = ref(0)
    const message = useMessage();
    const noScale = ref(false)
    const savedOptions = useStorage('importMusicOptions', defaultSavedOptions, undefined, { mergeDefaults: true });

    const shouldUseH264 = () => {
      if (savedOptions.value.movieCodec === MOVIE_CODEC.ForceH264) return true;
      if (savedOptions.value.movieCodec === MOVIE_CODEC.ForceVP9) return false;
      return (aquaMaiConfig.value?.sectionStates?.['GameSystem.Assets.MovieLoader']?.enabled && aquaMaiConfig.value?.entryStates?.['GameSystem.Assets.MovieLoader.LoadMp4Movie']?.value) || false;
    }

    const uploadMovie = (id: number, movie: File, offset: number) => new Promise<void>((resolve, reject) => {
      progress.value = 0;
      const body = new FormData();
      const h264 = shouldUseH264();
      console.log('use h264', h264);
      body.append('h264', h264.toString());
      body.append('padding', offset.toString());
      body.append('noScale', noScale.value.toString());
      body.append('yuv420p', savedOptions.value.yuv420p.toString());
      body.append('file', movie);
      const controller = new AbortController();
      fetchEventSource(getUrl(`SetMovieApi/${selectedADir.value}/${id}`), {
        signal: controller.signal,
        method: 'PUT',
        body,
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

    const uploadFlow = async () => {
      step.value = STEP.Select
      try {
        const [fileHandle] = await window.showOpenFilePicker({
          id: 'movie',
          startIn: 'downloads',
          types: [
            {
              description: "支持的文件类型",
              accept: {
                "video/*": [".dat"],
                "image/*": [],
              },
            },
          ],
        });
        step.value = STEP.None
        if (!fileHandle) return;
        const file = await fileHandle.getFile() as File;

        if (file.name.endsWith('.dat')) {
          load.value = true;
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
          message.success("保存成功")
        }
      } catch (e: any) {
        if (e?.name === 'AbortError') return
        console.log(e)
        globalCapture(e, "导入 PV 出错")
      } finally {
        step.value = STEP.None
        load.value = false;
      }
    }

    return () => <NButton secondary onClick={uploadFlow} loading={load.value} disabled={props.disabled}>
      设置 PV

      <NDrawer show={step.value === STEP.Select} height={250} placement="bottom">
        <NDrawerContent title="可以选择的文件类型">
          <NFlex vertical>
            任何 FFmpeg 支持的视频格式或单张图片（赞助版功能），或者已经自行转换好的 DAT 文件
            <div class="grid cols-4 justify-items-center text-8em gap-10">
              <FileTypeIcon type="MP4"/>
              <FileTypeIcon type="JPG"/>
              <FileTypeIcon type="DAT"/>
            </div>
          </NFlex>
        </NDrawerContent>
      </NDrawer>
      <NModal
        preset="card"
        class="w-[min(30vw,25em)]"
        title="设置偏移（秒）"
        show={step.value === STEP.Offset}
        onUpdateShow={() => step.value = STEP.None}
      >{{
        default: () => <NFlex vertical size="large">
          <div>设为正数可以在视频前面添加黑场空白，设为负数则裁掉视频前面的一部分</div>
          <NInputNumber v-model:value={offset.value} class="w-full" step={0.01}/>
          <NCheckbox v-model:checked={noScale.value}>
            不要缩放 BGA 到 1080 宽度
          </NCheckbox>
          <NFormItem label="PV 编码" labelPlacement="left" showFeedback={false}>
            <NFlex vertical class="w-full">
              <NFlex class="h-34px" align="center">
                <NSelect v-model:value={savedOptions.value.movieCodec} options={[
                  { label: '优先 H264', value: MOVIE_CODEC.PreferH264 },
                  { label: '强制 H264', value: MOVIE_CODEC.ForceH264 },
                  { label: '强制 VP9 USM', value: MOVIE_CODEC.ForceVP9 },
                ]}/>
              </NFlex>
            </NFlex>
          </NFormItem>
          <NCheckbox v-model:checked={savedOptions.value.yuv420p}>
            转换 USM 时使用 YUV420P 颜色空间
          </NCheckbox>
        </NFlex>,
        footer: () => <NFlex justify="end">
          <NButton onClick={okResolve.value as any}>确定</NButton>
        </NFlex>
      }}</NModal>
      <NModal
        preset="card"
        class="w-[min(40vw,40em)]"
        title="正在转换…"
        show={step.value === STEP.Progress}
        closable={false}
        maskClosable={false}
        closeOnEsc={false}
      >
        <NProgress
          type="line"
          status="success"
          percentage={progress.value}
          indicator-placement="inside"
          processing
        >
          {progress.value === 100 ? '还在处理，别急…' : `${progress.value}%`}
        </NProgress>
      </NModal>
    </NButton>;
  }
})
