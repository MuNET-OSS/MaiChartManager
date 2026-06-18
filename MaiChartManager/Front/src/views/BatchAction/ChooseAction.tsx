import { defineComponent, PropType, ref } from "vue";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import { Button, Radio, Select, Popover, addToast } from "@munet/ui";
import { STEP } from "@/views/BatchAction/index";
import api, { isLocalHost, requestExportMaidata } from "@/client/api";
import { showNeedPurchaseDialog, updateMusicList, version } from "@/store/refs";
import remoteExport from "@/views/BatchAction/remoteExport";
import TransitionVertical from "@/components/TransitionVertical.vue";
import { useStorage } from "@vueuse/core";
import { useI18n } from 'vue-i18n';

export enum OPTIONS {
  None,
  EditProps,
  Delete,
  CreateNewOpt,
  CreateNewOptCompatible,
  ConvertToMaidata,
  ConvertToMaidataIgnoreVideo,
  CreateNewOptMa2_103,
  ConvertToMaidataById,
}

export enum MAIDATA_SUBDIR {
  None,
  Genre,
  Version,
}

export default defineComponent({
  props: {
    selectedMusic: Array as PropType<MusicXmlWithABJacket[]>,
    continue: {type: Function, required: true},
  },
  setup(props) {
    const selectedOption = ref(OPTIONS.None);
    const selectedMaidataSubdir = useStorage('selectedMaidataSubdir', MAIDATA_SUBDIR.None);
    const load = ref(false);

    const { t } = useI18n();

    const proceed = async () => {
      switch (selectedOption.value) {
        case OPTIONS.EditProps:
          props.continue(STEP.EditProps);
          break;
        case OPTIONS.Delete:
          load.value = true;
          await api.BatchDeleteMusic(props.selectedMusic!);
          await updateMusicList();
          addToast({message: t('music.batch.deleteSuccess'), type: 'success'});
          props.continue(STEP.Select);
          break;
        case OPTIONS.CreateNewOpt:
        case OPTIONS.CreateNewOptCompatible:
          if (isLocalHost) {
            props.continue(STEP.Select);
            await api.RequestCopyTo({music: props.selectedMusic, removeEvents: selectedOption.value === OPTIONS.CreateNewOptCompatible, legacyFormat: false});
            addToast({message: t('music.batch.exportSuccess'), type: 'success'});
            break;
          }
        case OPTIONS.CreateNewOptMa2_103:
          if (isLocalHost) {
            props.continue(STEP.Select);
            await api.RequestCopyTo({music: props.selectedMusic, removeEvents: true, legacyFormat: true});
            addToast({message: t('music.batch.exportSuccess'), type: 'success'});
            break;
          }
        case OPTIONS.ConvertToMaidataById:
        case OPTIONS.ConvertToMaidata:
        case OPTIONS.ConvertToMaidataIgnoreVideo:
          if (version.value?.license !== 'Active') {
            showNeedPurchaseDialog.value = true
            break;
          }
          if (isLocalHost) {
            // 本地宿主（Photino/WebKitGTK、WebView2）：走后端 RequestExportMaidata，弹原生选目录对话框。
            // 注意：ConvertToMaidataById（按 ID 命名子目录）在本地路径下无法精确还原——
            // 后端目前按「歌名 + DX」命名子目录，因此本地路径下 ById 等同于普通 maidata 导出。
            // 远程路径（remoteExport）仍按 ID 命名，保持原样。
            load.value = true;
            try {
              await requestExportMaidata(
                props.selectedMusic!.map(it => ({id: it.id!, assetDir: it.assetDir!})),
                selectedOption.value === OPTIONS.ConvertToMaidataIgnoreVideo,
              );
              addToast({message: t('music.batch.exportSuccess'), type: 'success'});
            } finally {
              load.value = false;
            }
            props.continue(STEP.Select);
            break;
          }
          remoteExport(props.continue as any, props.selectedMusic!, selectedOption.value, selectedMaidataSubdir.value);
          break;
      }
    }

    return () => <div class="flex flex-col gap-2">
      <fieldset disabled={load.value}>
        <div class="flex flex-col gap-2">
          {
            props.selectedMusic?.some(it => it.assetDir === 'A000') ?
              <>
                <Popover trigger="hover">{{
                  trigger: () =>
                    <div class="flex gap-2 items-center opacity-50">
                      <input type="radio" disabled />
                      <label>{t('music.batch.editProperties')}</label>
                    </div>,
                  default: () => t('music.batch.selectedA000Warning')
                }}</Popover>
                <Popover trigger="hover">{{
                  trigger: () =>
                    <div class="flex gap-2 items-center opacity-50">
                      <input type="radio" disabled />
                      <label>{t('common.delete')}</label>
                    </div>,
                  default: () => t('music.batch.selectedA000Warning')
                }}</Popover>
              </> :
              <>
                <Radio k={OPTIONS.EditProps} v-model:value={selectedOption.value}>
                  {t('music.batch.editProperties')}
                </Radio>
                <Radio k={OPTIONS.Delete} v-model:value={selectedOption.value}>
                  {t('common.delete')}
                </Radio>
              </>
          }
          <Radio k={OPTIONS.CreateNewOpt} v-model:value={selectedOption.value}>
            {t('music.batch.exportOriginal')}
          </Radio>
          <Radio k={OPTIONS.CreateNewOptCompatible} v-model:value={selectedOption.value}>
            {t('music.batch.exportPreserveFormat')}
          </Radio>
          <Radio k={OPTIONS.CreateNewOptMa2_103} v-model:value={selectedOption.value}>
            {t('music.batch.exportMa2Format')}
          </Radio>
          <Radio k={OPTIONS.ConvertToMaidata} v-model:value={selectedOption.value}>
            {t('music.batch.convertToMaidata')}
          </Radio>
          <Radio k={OPTIONS.ConvertToMaidataById} v-model:value={selectedOption.value}>
            {t('music.batch.convertToMaidataById')}
          </Radio>
          <Radio k={OPTIONS.ConvertToMaidataIgnoreVideo} v-model:value={selectedOption.value}>
            {t('music.batch.convertToMaidataNoVideo')}
          </Radio>

          <TransitionVertical>
            {(selectedOption.value === OPTIONS.ConvertToMaidata || selectedOption.value === OPTIONS.ConvertToMaidataIgnoreVideo || selectedOption.value === OPTIONS.ConvertToMaidataById) &&
              <Select v-model:value={selectedMaidataSubdir.value} options={[{label: t('music.batch.subdirOption.none'), value: MAIDATA_SUBDIR.None}, {label: t('music.batch.subdirOption.genre'), value: MAIDATA_SUBDIR.Genre}, {label: t('music.batch.subdirOption.version'), value: MAIDATA_SUBDIR.Version}]}/>}
          </TransitionVertical>
        </div>
      </fieldset>
      <div class="flex justify-end gap-2">
        <Button onClick={() => props.continue(STEP.Select)} disabled={load.value}>{t('common.previous')}</Button>
        <Button onClick={proceed} ing={load.value} disabled={selectedOption.value === OPTIONS.None}>{t('purchase.continue')}</Button>
      </div>
    </div>;
  }
})
