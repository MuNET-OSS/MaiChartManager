import { useDropZone } from '@vueuse/core';
import { defineComponent, PropType, ref, computed, watch, shallowRef } from 'vue';
import { startProcess as startProcessMusicImport } from '@/components/ImportCreateChartButton/ImportChartButton';
import { uploadFlow as uploadFlowMovie } from '@/components/MusicEdit/SetMovieButton';
import { uploadFlow as uploadFlowAcbAwb } from '@/components/MusicEdit/AcbAwb';
import { selectedADir, selectedMusic } from '@/store/refs';
import { upload as uploadJacket } from '@/components/JacketBox';
import ReplaceChartModal, { prepareReplaceChart } from './ReplaceChartModal';
import AquaMaiManualInstaller, { setManualInstallAquaMai } from './AquaMaiManualInstaller';

export const mainDivRef = shallowRef<HTMLDivElement>();

enum DropType {
  None,
  Charts,
  Video,
}

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {
    const onDrop = async (files: File[] | null, e: DragEvent) => {
      e.stopPropagation();
      console.log(files);
      const items = e.dataTransfer?.items;
      if (!items?.length) return;
      const firstType = items[0].type;
      console.log(firstType);
      const handles = await Promise.all(Array.from(items).map(item => item.getAsFileSystemHandle()));
      console.log(handles);
      if (handles.every(handle => handle instanceof FileSystemDirectoryHandle)) {
        startProcessMusicImport(handles.length === 1 ? handles[0] : handles);
      }
      else if (handles.length === 1 && handles[0] instanceof FileSystemFileHandle) {
        const file = handles[0] as FileSystemFileHandle;
        if (file.kind === 'file' && file.name.endsWith('.dll')) {
          setManualInstallAquaMai(file);
        }
        if (selectedADir.value === 'A000') return;
        if (!selectedMusic.value) return;
        if (file.kind === 'file' && (firstType.startsWith('video/') || ['dat', 'usm'].includes(file.name.toLowerCase().split('.').pop()!))) {
          uploadFlowMovie(file);
        }
        else if (file.kind === 'file' && (firstType.startsWith('audio/') || file.name.endsWith('.acb'))) {
          uploadFlowAcbAwb(file);
        }
        else if (file.kind === 'file' && (firstType.startsWith('image/') || file.name.endsWith('.jpeg') || file.name.endsWith('.jpg') || file.name.endsWith('.png'))) {
          uploadJacket(file);
        }
        else if (file.kind === 'file' && (file.name.endsWith('.ma2') || file.name === "maidata.txt")) {
          prepareReplaceChart(file);
        }
      }
    }
    const dropType = ref<DropType>(DropType.None);


    const { isOverDropZone } = useDropZone(mainDivRef, {
      onDrop,
      // specify the types of data to be received.
      // dataTypes: ['application/json', 'text/csv'],
      // control multi-file drop
      multiple: true,
      // whether to prevent default behavior for unhandled events
      preventDefaultForUnhandled: true,
      onOver(f, e) {
        dropType.value = DropType.None;
        e.stopPropagation();
        if (e.dataTransfer) {
          // 由于没法判断是不是文件夹也不知道扩展名，所以没法给出推理
          // const items = e.dataTransfer.items;
          // console.log(items[0].type);
          // if (items?.length) {
          //   const allDirectories = Array.from(items).every(item => item.kind === 'file' && item.type === '');
          //   if (allDirectories) {
          //     dropType.value = DropType.Charts;
          //   }
          // }
          // if (items.length === 1) {
          //   const item = items[0];
          //   if (item.type.startsWith('video/') && selectedMusic.value){
          //     dropType.value = DropType.Video;
          //   }
          // }

          // if (dropType.value === DropType.None) {
          //   e.dataTransfer.dropEffect = 'none';
          // }
          // else {
          e.dataTransfer.dropEffect = 'copy';
          // }
        }
      },
    });

    return () => <>
      {isOverDropZone.value && dropType.value !== DropType.None && <div class="absolute-full z-100 bg-white/50 backdrop-blur-sm flex justify-center items-center" >
        {/* {dropType.value === DropType.Charts && selectedADir.value !== 'A000' && <div class="text-2xl">
          松开鼠标导入谱面
        </div>} */}
      </div>}
      <ReplaceChartModal />
      <AquaMaiManualInstaller />
    </>;
  },
});
