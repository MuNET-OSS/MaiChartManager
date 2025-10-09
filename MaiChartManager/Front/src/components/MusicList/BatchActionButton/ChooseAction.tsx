import { defineComponent, PropType, ref } from "vue";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import { NButton, NFlex, NPopover, NRadio, NRadioGroup, NSelect, useMessage, useNotification } from "naive-ui";
import { STEP } from "@/components/MusicList/BatchActionButton/index";
import api from "@/client/api";
import { showNeedPurchaseDialog, updateMusicList, version } from "@/store/refs";
import remoteExport from "@/components/MusicList/BatchActionButton/remoteExport";
import TransitionVertical from "@/components/TransitionVertical.vue";
import { useStorage } from "@vueuse/core";

export enum OPTIONS {
  None,
  EditProps,
  Delete,
  CreateNewOpt,
  CreateNewOptCompatible,
  ConvertToMaidata,
  ConvertToMaidataIgnoreVideo,
  CreateNewOptMa2_103,
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
    // 导出为 Maidata 的子目录选项
    const selectedMaidataSubdir = useStorage('selectedMaidataSubdir', MAIDATA_SUBDIR.None);
    const load = ref(false);
    const notify = useNotification();

    const proceed = async () => {
      switch (selectedOption.value) {
        case OPTIONS.EditProps:
          props.continue(STEP.EditProps);
          break;
        case OPTIONS.Delete:
          load.value = true;
          await api.BatchDeleteMusic(props.selectedMusic!);
          await updateMusicList();
          props.continue(STEP.None);
          break;
        case OPTIONS.CreateNewOpt:
        case OPTIONS.CreateNewOptCompatible:
          if (location.hostname === 'mcm.invalid') {
            props.continue(STEP.None);
            await api.RequestCopyTo({music: props.selectedMusic, removeEvents: selectedOption.value === OPTIONS.CreateNewOptCompatible, legacyFormat: false});
            break;
          }
        case OPTIONS.CreateNewOptMa2_103:
          if (location.hostname === 'mcm.invalid') {
            props.continue(STEP.None);
            await api.RequestCopyTo({music: props.selectedMusic, removeEvents: true, legacyFormat: true});
            break;
          }
        case OPTIONS.ConvertToMaidata:
        case OPTIONS.ConvertToMaidataIgnoreVideo:
          if (version.value?.license !== 'Active') {
            showNeedPurchaseDialog.value = true
            break;
          }
          remoteExport(props.continue as any, props.selectedMusic!, selectedOption.value, notify, selectedMaidataSubdir.value);
          break;
      }
    }

    return () => <NFlex vertical>
      <NRadioGroup v-model:value={selectedOption.value} disabled={load.value}>
        <NFlex vertical>
          {
            props.selectedMusic?.some(it => it.assetDir === 'A000') ?
              <>
                <NPopover trigger="hover" placement="top-start">{{
                  trigger: () =>
                    <NRadio disabled>
                      编辑属性
                    </NRadio>,
                  default: () => '你选择了 A000 目录中的歌曲'
                }}</NPopover>
                <NPopover trigger="hover" placement="top-start">{{
                  trigger: () =>
                    <NRadio disabled>
                      删除
                    </NRadio>,
                  default: () => '你选择了 A000 目录中的歌曲'
                }}</NPopover>
              </> :
              <>
                <NRadio value={OPTIONS.EditProps}>
                  编辑属性
                </NRadio>
                <NRadio value={OPTIONS.Delete}>
                  删除
                </NRadio>
              </>
          }
          <NRadio value={OPTIONS.CreateNewOpt}>
            导出为 Opt（原始）
          </NRadio>
          <NRadio value={OPTIONS.CreateNewOptCompatible}>
            导出为 Opt（保持谱面格式，移除 Event 等）
          </NRadio>
          <NRadio value={OPTIONS.CreateNewOptMa2_103}>
            导出为 Opt（Ma2 103 格式，移除 Event 等）
          </NRadio>
          <NRadio value={OPTIONS.ConvertToMaidata}>
            转换为 Maidata
          </NRadio>
          <NRadio value={OPTIONS.ConvertToMaidataIgnoreVideo}>
            转换为 Maidata（无 BGA）
          </NRadio>

          <TransitionVertical>
            {(selectedOption.value === OPTIONS.ConvertToMaidata || selectedOption.value === OPTIONS.ConvertToMaidataIgnoreVideo) &&
              <NSelect v-model:value={selectedMaidataSubdir.value} options={[{label: '平铺文件夹', value: MAIDATA_SUBDIR.None}, {label: '按流派分组', value: MAIDATA_SUBDIR.Genre}, {label: '按版本分组', value: MAIDATA_SUBDIR.Version}]}/>}
          </TransitionVertical>
        </NFlex>
      </NRadioGroup>
      <NFlex justify="end">
        <NButton onClick={() => props.continue(STEP.Select)} disabled={load.value}>上一步</NButton>
        <NButton onClick={proceed} loading={load.value} disabled={selectedOption.value === OPTIONS.None}>继续</NButton>
      </NFlex>
    </NFlex>;
  }
})
