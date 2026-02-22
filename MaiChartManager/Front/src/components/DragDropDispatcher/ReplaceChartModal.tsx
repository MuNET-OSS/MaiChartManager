import { t } from '@/locales';
import { globalCapture, selectedADir, selectedLevel, selectedMusic, selectMusicId, updateMusicList } from '@/store/refs';
import { NButton, NFlex, NModal, useDialog, useMessage } from 'naive-ui';
import { computed, defineComponent, ref, shallowRef } from 'vue';
import JacketBox from '../JacketBox';
import { DIFFICULTY } from '@/consts';
import api from '@/client/api';
import CheckingModal from "@/components/ImportCreateChartButton/ImportChartButton/CheckingModal";
import LevelTagsDisplay from "@/components/LevelTagsDisplay";
import { Chart, ImportChartCheckResult, ImportChartResult, ShiftMethod } from "@/client/apiGen";
import ImportAlert from "@/components/ImportCreateChartButton/ImportChartButton/ImportAlert";
import { defaultTempOptions, ImportChartMessageEx, TempOptions } from "@/components/ImportCreateChartButton/ImportChartButton/types";
import ShiftModeSelector from "@/components/ImportCreateChartButton/ImportChartButton/ShiftModeSelector";

// noinspection JSUnusedLocalSymbols
export let prepareReplaceChart = async (fileHandle?: FileSystemFileHandle) => {
}

export default defineComponent({
  setup() {
    const dialog = useDialog();

    const checking = ref(false);
    const fileHandle = shallowRef<FileSystemFileHandle | null>(null);
    const show = ref<"" | "ma2" | "maidata" | "failed">("");

    const apiResp = ref<ImportChartCheckResult | ImportChartResult | null>(null)
    const checkErrors = computed<ImportChartMessageEx[]>(()=>{
      return apiResp.value?.errors?.map(it=>({...it, name: selectedMusic.value!.name!})) ?? []
    })
    const tempOption = ref<TempOptions>({...defaultTempOptions})
    const showModal = computed({
      get: () => !!show.value,
      set: (val: boolean) => { if (!val) show.value = "" }
    })

    // 注：本功能的逻辑是，如果选择的是ma2文件，则只替换指定难度的谱面；如果选择的是maidata，则替换整首歌的所有难度。
    prepareReplaceChart = async (fHandle?: FileSystemFileHandle) => {
      if (!fHandle) {
        [fHandle] = await window.showOpenFilePicker({
          id: 'chart',
          startIn: 'downloads',
          types: [
            {
              description: t('music.edit.supportedFileTypes'),
              accept: {
                "application/x-supported": [".ma2", ".txt"], // 没办法限定只匹配maidata.txt，就只好先把一切txt都作为匹配
              },
            },
          ],
        });
      }
      if (!fHandle) return; // 用户未选择文件
      fileHandle.value = fHandle

      const name = fHandle.name;
      // 对maidata.txt和ma2分类讨论，前者执行ImportCheck
      if (name === "maidata.txt") {
        try {
          checking.value = true;
          const file = await fHandle.getFile();
          const r = (await api.ImportChartCheck({file, isReplacement: true})).data;
          if (!checking.value) return; // 说明检查期间用户点击了关闭按钮、取消了操作。则不再执行后续流程。

          apiResp.value = r;
          if (selectedMusic.value?.shiftMethod) { // 说明是新版导入的谱面、ShiftMethod已经被写入XML了。此时锁定ShiftMethod选项，不准用户自己选择
            tempOption.value = {shift: selectedMusic.value.shiftMethod as ShiftMethod, shiftLocked: true};
          }
          show.value = "maidata";
        } finally {
          checking.value = false;
        }
      } else if (name.endsWith(".ma2")) {
        show.value = "ma2"
      } else {
        dialog.error({title: t('error.unsupportedFileType'), content: t('music.edit.notValidChartFile')})
      }
    }

    const replaceChart = async () => {
      if (!fileHandle.value) return;
      try {
        const file = await fileHandle.value.getFile();
        fileHandle.value = null;
        const level = show.value === "maidata" ? -1 : selectedLevel.value;
        show.value = "";
        const result = (await api.ReplaceChart(selectMusicId.value, level, selectedADir.value, { file, shift: tempOption.value.shift })).data;
        if (!result.fatal) {
          dialog.success({title: t('music.edit.replaceChartSuccess')});
        } else {
          apiResp.value = result; // 用于在失败时显示错误信息
          show.value = "failed";
        }
        await updateMusicList(true);
      } catch (error) {
        globalCapture(error, t('music.edit.replaceChartFailed'));
        console.error(error);
      }
    }

    const chartsForDisplayLevel = computed(()=>{
      const result: (Chart | undefined)[] = [...selectedMusic.value!.charts!]
      if (show.value === "ma2") { // 此时只显示所选择的难度，其他难度不要显示
        for (let i=0; i<result.length; i++) {
          if (i !== selectedLevel.value) result[i] = undefined
        }
      }
      return result
    })

    return () => <div>
        <NModal
        preset="card"
        class="w-[min(90vw,50em)]"
        title={show.value !== "failed" ? t('music.edit.replaceChart') : t('music.edit.replaceChartFailed')}
        v-model:show={showModal.value}
      >{{
        default: () => <div class="flex flex-col gap-2">
          {show.value === "ma2" && <>
            {t('music.edit.replaceChartConfirm', { level: DIFFICULTY[selectedLevel.value!] })}
            <div class="text-4.5 text-center">{fileHandle.value?.name}</div>
            <div class="text-6 text-center">↓</div>
          </>}
          {(show.value === "maidata" || show.value === "failed") && <ImportAlert errors={checkErrors.value} tempOptions={tempOption.value}></ImportAlert>}
          {show.value !== "failed" && <div class="flex justify-center gap-2">
            <JacketBox info={selectedMusic.value!} class="h-8em w-8em" upload={false} />
            <div class="flex flex-col gap-1 max-w-24em justify-end">
              <div class="text-3.5 op-70">#{selectMusicId.value}</div>
              <div class="text-2xl overflow-hidden text-ellipsis whitespace-nowrap">{selectedMusic.value!.name}</div>
              <LevelTagsDisplay charts={chartsForDisplayLevel.value}></LevelTagsDisplay>
            </div>
          </div>}
          {show.value === "maidata" && <div>
            <ShiftModeSelector tempOptions={tempOption.value}></ShiftModeSelector>
            <div>{t('music.edit.replaceChartShiftModeHint')}</div>
          </div>}
        </div>,
        footer: () => <NFlex justify="end">
          <NButton onClick={() => show.value = ""}>{show.value !== "failed" ? t('common.cancel') : t('common.close')}</NButton>
          {show.value !== "failed" && <NButton onClick={replaceChart} type="primary">{t('common.confirm')}</NButton>}
        </NFlex>
      }}</NModal>
      <CheckingModal title={t('chart.import.checkingTitle')} show={checking.value} closeModal={()=>checking.value=false} />
    </div>;
  },
});
