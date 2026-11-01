import { Button, DropMenu } from "@munet/ui";
import { computed, defineComponent, ref } from "vue";
import ImportChartButton from "./ImportChartButton";
import CreateMusicModal from "@/views/Charts/ImportCreateChartButton/CreateMusicModal";
import { isReadOnlyDir } from "@/store/refs";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup() {
    const showCreate = ref(false);
    const { t } = useI18n();
    const options = computed(() => [
      { label: t('chart.import.create'), action: () => { showCreate.value = true; }, disabled: isReadOnlyDir.value },
    ]);

    return () => <div class="flex relative">
      <ImportChartButton />
      <DropMenu options={options.value} buttonText="" align="right">
        {{
          trigger: (onClick: any) => <Button variant="secondary" disabled={isReadOnlyDir.value} class="px-.5 b-l b-l-solid b-l-[rgba(255,255,255,0.5)]" onClick={onClick}>
            <span class="i-mdi-arrow-down-drop text-6 translate-y-.25" />
          </Button>
        }}
      </DropMenu>
      <CreateMusicModal show={showCreate.value} closeModal={() => showCreate.value = false} />
    </div>;
  }
})
