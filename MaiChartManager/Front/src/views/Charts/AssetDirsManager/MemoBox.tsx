import { defineComponent, ref, watch } from "vue";
import { Button, TextInput } from "@munet/ui";
import api from "@/client/api";
import { updateAssetDirs } from "@/store/refs";
import { useI18n } from 'vue-i18n';
import { rightPanel, editingMemoDir, editingMemoName } from "@/views/Charts/refs";

export default defineComponent({
  setup() {
    const content = ref('');
    const name = ref('');
    const load = ref(false);
    const { t } = useI18n();

    const deleteLoading = ref(false);
    const deleteConfirm = ref(false);

    // Watch for changes to editingMemoName to reload content
    watch([editingMemoName, editingMemoDir], async ([newName, newDir]) => {
      if (!newName || !newDir) return;
      load.value = true;
      content.value = '';
      name.value = '';
      if (newName !== 'add') {
        const req = await api.GetAssetDirTxtValue({ dirName: newDir.dirName, fileName: newName });
        content.value = req.data;
      }
      load.value = false;
    }, { immediate: true });

    const save = async () => {
      const dir = editingMemoDir.value;
      const memoName = editingMemoName.value;
      if (!dir) return;
      load.value = true;
      await api.PutAssetDirTxtValue({
        dirName: dir.dirName,
        fileName: memoName === 'add' ? name.value + '.txt' : memoName,
        content: content.value,
      });
      await updateAssetDirs();
      load.value = false;
      rightPanel.value = 'musicEdit';
    };

    const del = async () => {
      const dir = editingMemoDir.value;
      const memoName = editingMemoName.value;
      if (!dir) return;
      if (!deleteConfirm.value) {
        deleteConfirm.value = true;
        return;
      }
      deleteConfirm.value = false;
      deleteLoading.value = true;
      await api.DeleteAssetDirTxt({ dirName: dir.dirName, fileName: memoName });
      deleteLoading.value = false;
      rightPanel.value = 'musicEdit';
      updateAssetDirs();
    };

    return () => (
      <div class="flex flex-col gap-3 h-full p-xy">
        <div class="flex items-center gap-2">
          <Button variant="secondary" onClick={() => rightPanel.value = 'musicEdit'}>
            <span class="i-ic-baseline-arrow-back text-lg" />
          </Button>
          <div class="font-medium">
            {t('common.edit')} - {editingMemoDir.value?.dirName} / {editingMemoName.value === 'add' ? t('common.create') : editingMemoName.value}
          </div>
        </div>
        {editingMemoName.value === 'add' && (
          <div>
            <div class="ml-1 text-sm">{t('assetDir.memoName')}</div>
            <TextInput v-model:value={name.value} />
          </div>
        )}
        <TextInput textarea v-model:value={content.value} class="flex-1 min-h-40" disabled={load.value} />
        <div class="flex justify-between">
          {editingMemoName.value !== 'add' ? (
            <Button
              variant="secondary"
              onClick={del}
              ing={deleteLoading.value}
              danger={deleteConfirm.value}
              onMouseleave={() => deleteConfirm.value = false}
            >
              {t('common.delete')}
            </Button>
          ) : <div />}
          <Button variant="secondary" onClick={save}>{t('common.save')}</Button>
        </div>
      </div>
    );
  }
})
