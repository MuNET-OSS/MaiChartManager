import { t } from '@/locales';
import { globalCapture, selectedADir, selectedLevel, selectedMusic, selectMusicId, updateMusicList } from '@/store/refs';
import { NButton, NFlex, NModal, useDialog, useMessage } from 'naive-ui';
import { defineComponent, shallowRef } from 'vue';
import JacketBox from '../JacketBox';
import { DIFFICULTY, LEVEL_COLOR } from '@/consts';
import api from '@/client/api';

export let prepareReplaceChart = async (fileHandle?: FileSystemFileHandle) => {
}

export const replaceChartFileHandle = shallowRef<FileSystemFileHandle | null>(null);

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {
    const message = useMessage();
    const dialog = useDialog();

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
      if (!fileHandle) return // 用户未选择文件
      const name = fileHandle.name
      if (!(name.endsWith(".ma2") || name == "maidata.txt")) {
        dialog.error({title: t('error.unsupportedFileType'), content: t('music.edit.notValidChartFile')})
        return
      }
      // TODO：对ma2和maidata.txt分类讨论，后者执行ImportCheck
      console.log(fileHandle.name)
      replaceChartFileHandle.value = fileHandle
    }

    const replaceChart = async () => {
      if (!replaceChartFileHandle.value) return;
      try {
        const file = await replaceChartFileHandle.value.getFile();
        replaceChartFileHandle.value = null;
        await api.ReplaceChart(selectMusicId.value, selectedLevel.value, selectedADir.value, { file });
        message.success(t('music.edit.replaceChartSuccess'));
        await updateMusicList();
      } catch (error) {
        globalCapture(error, t('music.edit.replaceChartFailed'));
        console.error(error);
      }
    }

    return () => <NModal
      preset="card"
      class="w-[min(90vw,50em)]"
      title={t('music.edit.replaceChart')}
      show={replaceChartFileHandle.value !== null}
      onUpdateShow={() => replaceChartFileHandle.value = null}
    >{{
      default: () => <div class="flex flex-col gap-2">
        {t('music.edit.replaceChartConfirm', { level: DIFFICULTY[selectedLevel.value!] })}
        <div class="text-4.5 text-center">{replaceChartFileHandle.value?.name}</div>
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
        <NButton onClick={() => replaceChartFileHandle.value = null}>{t('common.cancel')}</NButton>
        <NButton onClick={replaceChart} type="primary">{t('common.confirm')}</NButton>
      </NFlex>
    }}</NModal>;
  },
});
