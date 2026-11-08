import { computed, defineComponent } from "vue";

import ConfigEditor from "@/views/ModManager/ConfigEditor";
import { modInfo } from "@/store/refs";
import { shouldShowUpdate } from "@/views/ModManager/shouldShowUpdateController";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup(props) {
    const { t } = useI18n();

    const badgeType = computed(() => {
      if (!modInfo.value) return
      if (!modInfo.value.aquaMaiInstalled || !modInfo.value.melonLoaderInstalled) return 'error'
      if (shouldShowUpdate.value) return 'warning'
    })

    return () => <div class="p-xy h-full flex flex-col of-hidden">
      {modInfo.value && <ConfigEditor />}
    </div>;
  }
})
