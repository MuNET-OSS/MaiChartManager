import { defineComponent, PropType, ref } from "vue";
import api from "@/client/api";
import { selectedADir, selectMusicId, updateAssetDirs, updateMusicList } from "@/store/refs";
import { Button, addToast } from '@munet/ui';
import { GetAssetsDirsResult } from "@/client/apiGen";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    dir: {type: Object as PropType<GetAssetsDirsResult>, required: true}
  },
  setup(props) {
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
      const res = await api.DeleteAssetDir(props.dir.dirName!);
      if (res.error) {
        const error = res.error as any;
        addToast({ message: (error.message || error) as string, type: 'error' });
        return;
      }
      if (selectedADir.value === props.dir.dirName) {
        selectedADir.value = 'A000';
        selectMusicId.value = 0;
        await updateMusicList();
      }
      await updateAssetDirs();
    }


    return () => <Button variant="secondary" onClick={del} ing={deleteLoading.value} danger={deleteConfirm.value}
                          onMouseleave={() => deleteConfirm.value = false}>
      {deleteConfirm.value ? t('common.confirm') : t('common.delete')}
    </Button>;
  }
});
