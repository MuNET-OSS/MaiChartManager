import { computed, defineComponent, ref, watch } from "vue";
import { Button, Modal, NumberInput, Radio } from "@munet/ui";
import { assetDirs, isReadOnlyDir, musicList, selectedADir, selectMusicId, updateMusicList } from "@/store/refs";
import dxIcon from "@/assets/dxIcon.png";
import stdIcon from "@/assets/stdIcon.png";
import api from "@/client/api";
import MusicIdConflictNotifier from "@/components/MusicIdConflictNotifier";
import getNextUnusedMusicId from "@/utils/getNextUnusedMusicId";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    show: Boolean,
    closeModal: {type: Function, required: true},
  },
  setup(props) {
    const { t } = useI18n();

    const show = computed({
      get: () => props.show,
      set: (val) => props.closeModal()
    })
    const id = ref(0);
    const isDx = computed({
      get: () => id.value >= 1e4,
      set: (val) => {
        const base = id.value % 1e4;
        id.value = val ? base + 1e4 : base;
      }
    });

    watch(() => show.value, (newValue, oldValue) => {
      if (!newValue) return;
      id.value = getNextUnusedMusicId();
    })

    const save = async () => {
      // 对话框打开期间目录可能被切到只读的 A000，保存前重新判一次
      if (isReadOnlyDir.value) return;
      show.value = false;
      await api.AddMusic(id.value, selectedADir.value);
      await updateMusicList();
      selectMusicId.value = id.value;
    }

    return () => (
      <Modal
        width="25em"
        title={t('chart.import.create')}
        v-model:show={show.value}
      >{{
        default: () => <div class="flex flex-col gap-3">
          <div class="flex gap-4 items-center">
            <div class="ml-1 text-sm">ID</div>
            <div class="flex gap-2 items-center">
              <NumberInput v-model:value={id.value} class="w-full" min={1} max={2e4 - 1}/>
              <MusicIdConflictNotifier id={id.value}/>
            </div>
          </div>
          <div class="flex flex-col gap-2">
            <div class="ml-1 text-sm">{t('chart.import.chartType')}</div>
            <div class="flex gap-2 ml-2">
              <Radio v-model:value={isDx.value} k={false}>
                <img src={stdIcon} class="h-1.5em mt--0.6"/>
              </Radio>
              <Radio v-model:value={isDx.value} k={true}>
                <img src={dxIcon} class="h-1.5em mt--0.6"/>
              </Radio>
            </div>
          </div>
        </div>,
        actions: () => <>
          <Button class="w-0 grow" onClick={save}>{t('common.confirm')}</Button>
        </>
      }}</Modal>
    );
  }
});
