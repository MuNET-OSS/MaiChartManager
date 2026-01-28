import { t } from '@/locales';
import { globalCapture, selectedADir, selectedLevel, selectedMusic, selectMusicId, updateMusicList } from '@/store/refs';
import { NButton, NFlex, NModal, useDialog, useMessage } from 'naive-ui';
import { defineComponent, ref, shallowRef } from 'vue';
import JacketBox from '../JacketBox';
import { DIFFICULTY, LEVEL_COLOR } from '@/consts';
import api from '@/client/api';
import CheckingModal from "@/components/ImportCreateChartButton/ImportChartButton/CheckingModal";

export let prepareReplaceChart = async (fileHandle?: FileSystemFileHandle) => {
}

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {
    const message = useMessage();
    const dialog = useDialog();

    const checking = ref(false);
    const ma2Handle = shallowRef<FileSystemFileHandle | null>(null);

    prepareReplaceChart = async (fileHandle?: FileSystemFileHandle) => {
      if (!fileHandle) {
        [fileHandle] = await window.showOpenFilePicker({
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
      if (!fileHandle) return; // 用户未选择文件

      const name = fileHandle.name;
      // 对maidata.txt和ma2分类讨论，前者执行ImportCheck
      if (name == "maidata.txt") {
        try {
          checking.value = true;
          const file = await fileHandle.getFile();
          const checkRet = (await api.ImportChartCheck({file, isReplacement: true})).data;
          if (!checking.value) return; // 说明检查期间用户点击了关闭按钮、取消了操作。则不再执行后续流程。
          // TODO 显示导入界面（类似ErrorDisplayIdInput）、完成导入流程
          console.log(checkRet)
          dialog.error({title: "NotImplemented"})
        } finally {
          checking.value = false;
        }
      } else if (name.endsWith(".ma2")) {
        ma2Handle.value = fileHandle
      } else {
        dialog.error({title: t('error.unsupportedFileType'), content: t('music.edit.notValidChartFile')})
      }
    }

    const replaceMa2 = async () => {
      if (!ma2Handle.value) return;
      try {
        const file = await ma2Handle.value.getFile();
        ma2Handle.value = null;
        await api.ReplaceChart(selectMusicId.value, selectedLevel.value, selectedADir.value, { file });
        message.success(t('music.edit.replaceChartSuccess'));
        await updateMusicList();
      } catch (error) {
        globalCapture(error, t('music.edit.replaceChartFailed'));
        console.error(error);
      }
    }

    return () => <div>
        <NModal
        preset="card"
        class="w-[min(90vw,50em)]"
        title={t('music.edit.replaceChart')}
        show={ma2Handle.value !== null}
        onUpdateShow={() => ma2Handle.value = null}
      >{{
        default: () => <div class="flex flex-col gap-2">
          {t('music.edit.replaceChartConfirm', { level: DIFFICULTY[selectedLevel.value!] })}
          <div class="text-4.5 text-center">{ma2Handle.value?.name}</div>
          <div class="text-6 text-center">↓</div>
          <div class="flex justify-center gap-2">
            <JacketBox info={selectedMusic.value!} class="h-8em w-8em" upload={false} />
            <div class="flex flex-col gap-1 max-w-24em justify-end">
              <div class="text-3.5 op-70">#{selectMusicId.value}</div>
              <div class="text-2xl overflow-hidden text-ellipsis whitespace-nowrap">{selectedMusic.value!.name}</div>
              <div class="flex">
                <div class="c-white rounded-full px-2" style={{ backgroundColor: LEVEL_COLOR[selectedLevel.value!] }}>
                  {selectedMusic.value!.charts![selectedLevel.value!]?.level}.{selectedMusic.value!.charts![selectedLevel.value!]?.levelDecimal}
                </div>
              </div>
            </div>
          </div>
        </div>,
        footer: () => <NFlex justify="end">
          <NButton onClick={() => ma2Handle.value = null}>{t('common.cancel')}</NButton>
          <NButton onClick={replaceMa2} type="primary">{t('common.confirm')}</NButton>
        </NFlex>
      }}</NModal>
      <CheckingModal title={t('chart.import.checkingTitle')} show={checking.value} closeModal={()=>checking.value=false} />
    </div>;
  },
});
