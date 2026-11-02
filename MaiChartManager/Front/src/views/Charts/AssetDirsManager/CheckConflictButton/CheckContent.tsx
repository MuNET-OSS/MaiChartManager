import { defineComponent, onMounted, ref } from "vue";
import { CheckConflictEntry } from "@/client/apiGen";
import api from "@/client/api";
import { Button, CheckBox } from '@munet/ui';
import { globalCapture } from "@/store/refs";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    dir: {type: String, required: true}
  },
  setup(props) {
    const data = ref<(CheckConflictEntry & { key: number })[]>([]);
    const selectedIds = ref<number[]>([]);
    const load = ref(true);
    const { t } = useI18n();
    

    const update = async () => {
      selectedIds.value = [];
      try {
        const req = await api.CheckConflict(props.dir);
        data.value = req.data.map((it, idx) => ({...it, key: idx}));
        load.value = false;
      } catch (e) {
        globalCapture(e, t('assetDir.conflict.checkError'));
      }
    }

    onMounted(update)

    const requestDelete = async () => {
      load.value = true;
      try {
        const req = selectedIds.value.map(it => ({
          type: data.value[it].type,
          assetDir: data.value[it].upperDir,
          fileName: data.value[it].fileName,
        }));
        selectedIds.value = [];
        await api.DeleteAssets(req);
      } catch (e) {
        globalCapture(e, t('assetDir.conflict.deleteError'));
      }
      update();
    }

    const toggleRow = (key: number) => {
      const idx = selectedIds.value.indexOf(key);
      if (idx >= 0) selectedIds.value.splice(idx, 1);
      else selectedIds.value.push(key);
    };

    const toggleAll = () => {
      if (selectedIds.value.length === data.value.length) selectedIds.value = [];
      else selectedIds.value = data.value.map(it => it.key);
    };

    return () => <div class="flex flex-col gap-3">
      <Button onClick={requestDelete} disabled={!selectedIds.value.length}>{t('assetDir.conflict.deleteSelected')}</Button>
      {load.value ? <div class="c-neutral text-center p-4">Loading...</div> :
        data.value.length === 0 ? <div class="c-neutral text-center p-4">{t('assetDir.conflict.noConflict')}</div> :
        <div>
          <table class="w-full text-sm border-collapse">
            <thead>
              <tr class="border-b border-neutral/20">
                <th class="p-2 text-left"><CheckBox value={selectedIds.value.length === data.value.length && data.value.length > 0} onChange={toggleAll}/></th>
                <th class="p-2 text-left">{t('assetDir.conflict.musicId')}</th>
                <th class="p-2 text-left">{t('assetDir.conflict.musicName')}</th>
                <th class="p-2 text-left">{t('assetDir.conflict.lowerDir')}</th>
                <th class="p-2 text-left">{t('assetDir.conflict.upperDir')}</th>
                <th class="p-2 text-left">{t('assetDir.conflict.fileName')}</th>
              </tr>
            </thead>
            <tbody>
              {data.value.map(row => <tr key={row.key} class="border-b border-neutral/10">
                <td class="p-2"><CheckBox value={selectedIds.value.includes(row.key)} onChange={() => toggleRow(row.key)}/></td>
                <td class="p-2">{row.musicId}</td>
                <td class="p-2">{row.musicName}</td>
                <td class="p-2">{row.lowerDir}</td>
                <td class="p-2">{row.upperDir}</td>
                <td class="p-2">{row.fileName}</td>
              </tr>)}
            </tbody>
          </table>
        </div>
      }
    </div>;
  }
})
