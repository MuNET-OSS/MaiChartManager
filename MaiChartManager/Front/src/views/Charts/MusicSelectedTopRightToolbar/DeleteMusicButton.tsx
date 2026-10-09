import { defineComponent, ref } from "vue";
import api from "@/client/api";
import { globalCapture, selectedADir, selectMusicId, updateMusicList } from "@/store/refs";
import { Button, showTransactionalDialog } from "@munet/ui";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup() {
    const deleteLoading = ref(false);
    const deleteConfirm = ref(false);
    const { t } = useI18n();

    const del = async () => {
      if (!deleteConfirm.value) {
        deleteConfirm.value = true;
        return;
      }
      deleteConfirm.value = false;
      deleteLoading.value = true;
      try {
        const res = await api.DeleteMusic(selectMusicId.value, selectedADir.value);
        if (res.error) {
          const error = res.error as any;
          await showTransactionalDialog(t('music.delete.deleteFailed'), error.message || error, undefined, true);
          return;
        }
      } catch (e) {
        globalCapture(e, t('music.delete.deleteError'))
      }
      selectMusicId.value = 0;
      updateMusicList();
    }


    return () => <Button onClick={del} ing={deleteLoading.value} class={deleteConfirm.value && 'bg-red-300!'}
                          onMouseleave={() => deleteConfirm.value = false}>
      {deleteConfirm.value ? t('music.delete.confirm') : t('common.delete')}
    </Button>;
  }
});
