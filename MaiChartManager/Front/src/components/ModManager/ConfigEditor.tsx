import { computed, defineComponent, onMounted, ref, watch } from "vue";
import { NButton, NCheckbox, NFlex, NModal, NSwitch, useDialog, useMessage } from "naive-ui";
import api from "@/client/api";
import { globalCapture, modInfo, updateModInfo, updateMusicList, aquaMaiConfig as config, modUpdateInfo } from "@/store/refs";
import AquaMaiConfigurator from "./AquaMaiConfigurator";
import { compareVersions, latestVersion, shouldShowUpdate } from "./shouldShowUpdateController";
import { useStorage } from "@vueuse/core";
import _ from "lodash";
import ModInstallDropdown from "@/components/ModManager/ModInstallDropdown";

export default defineComponent({
  props: {
    show: Boolean,
    badgeType: String,
  },
  setup(props, { emit }) {
    const show = computed({
      get: () => props.show,
      set: (val) => emit('update:show', val)
    })

    const configReadErr = ref('')
    const configReadErrTitle = ref('')
    const dialog = useDialog()
    const installingMelonLoader = ref(false)
    const useNewSort = useStorage('useNewSort', true)
    const message = useMessage();

    const updateAquaMaiConfig = async () => {
      try {
        configReadErr.value = ''
        configReadErrTitle.value = ''
        config.value = (await api.GetAquaMaiConfig()).data;
      } catch (err: any) {
        if (err instanceof Response && !err.bodyUsed) {
            const text = await err.text();
            try {
              const json = JSON.parse(text);
              if (json.detail) {
                configReadErr.value = json.detail;
              }
              if (json.title) {
                configReadErrTitle.value = json.title;
              }
              if(configReadErrTitle.value === 'System.Reflection.TargetInvocationException' && compareVersions(modInfo.value?.aquaMaiVersion || '0.0.0', '1.6.0') < 0) {
                configReadErr.value = 'AquaMai 版本过低，请更新到 1.6.0 以上';
              }
              return
            } catch {
            }
            configReadErr.value = text.split('\n')[0];
        }
        if (err.error instanceof Error) {
          configReadErr.value = err.error.message.split('\n')[0];
        }
        else if (err.error) {
          configReadErr.value = err.error.toString().split('\n')[0];
        }
        configReadErr.value = err.toString().split('\n')[0];
      }
    }

    onMounted(updateAquaMaiConfig)

    const installMelonLoader = async () => {
      try {
        installingMelonLoader.value = true
        await api.InstallMelonLoader()
        await updateModInfo()
      } catch (e: any) {
        globalCapture(e, "安装 MelonLoader 失败")
      } finally {
        installingMelonLoader.value = false
      }
    }

    const saveImpl = async () => {
      if (!config.value) return;
      try {
        await api.SetAquaMaiConfig(config.value)
        await updateMusicList()
        message.success("保存配置文件成功")
      } catch (e) {
        globalCapture(e, "保存 AquaMai 配置失败")
      }
    }
    const save = _.debounce(saveImpl, 2000);

    watch(() => config.value, async (val) => {
      if (configReadErr.value) return
      if (!show.value) return
      if (val) {
        console.log('配置变动')
        save()
      }
    }, { deep: true })


    return () => <NModal
      preset="card"
      class="w-[min(90vw,100em)]"
      title="Mod 管理"
      v-model:show={show.value}
    >
      {!!modInfo.value && <NFlex vertical>
        <NFlex align="center">
          MelonLoader:
          {modInfo.value.melonLoaderInstalled ? <span class="c-green-6">已安装</span> : <span class="c-red-6">未安装</span>}
          {!modInfo.value.melonLoaderInstalled && <NButton secondary loading={installingMelonLoader.value} onClick={installMelonLoader}>安装</NButton>}
          <div class="w-8"/>
          AquaMai:
          {modInfo.value.aquaMaiInstalled ?
            !shouldShowUpdate.value ? <span class="c-green-6">已安装</span> : <span class="c-orange">可更新</span> :
            <span class="c-red-6">未安装</span>}
          <ModInstallDropdown updateAquaMaiConfig={updateAquaMaiConfig}/>
          已安装:
          <span>v{modInfo.value.aquaMaiVersion}</span>
          可安装:
          <span class={shouldShowUpdate.value ? "c-orange" : ""}>{latestVersion.value.version}</span>
          <NSwitch v-model:value={useNewSort.value} class="m-l"/>
          使用新的排序方式
        </NFlex>
        {configReadErr.value ? <NFlex vertical justify="center" align="center" class="min-h-100">
          <div class="text-8">AquaMai 未安装或需要更新</div>
          <div class="c-gray-5 text-lg">{configReadErr.value}</div>
          <div class="c-gray-4 text-sm">{configReadErrTitle.value}</div>
        </NFlex> : <AquaMaiConfigurator config={config.value!} useNewSort={useNewSort.value}/>}
      </NFlex>}
    </NModal>;
  }
})
