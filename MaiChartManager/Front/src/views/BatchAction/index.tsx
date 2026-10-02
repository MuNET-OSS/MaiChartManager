import { defineComponent, onMounted, ref } from "vue";
import MusicSelector from "./MusicSelector";
import EditProps from "./EditProps";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import ChooseAction from "./ChooseAction";
import ProgressDisplay from "./ProgressDisplay";
import { useI18n } from 'vue-i18n';

export enum STEP {
  None,
  Select,
  ChooseAction,
  EditProps,
  ProgressDisplay
}

export default defineComponent({
  setup(props) {
    const step = ref(STEP.None);
    const selectedMusic = ref<MusicXmlWithABJacket[]>([]);
    const { t } = useI18n();

    onMounted(() => {
      step.value = STEP.Select;
      selectedMusic.value = [];
    });

    return () => <div class="flex flex-col h-100dvh pxy">
      {step.value === STEP.Select && <MusicSelector v-model:selectedMusicIds={selectedMusic.value} continue={() => step.value = STEP.ChooseAction} cancel={() => { step.value = STEP.Select; selectedMusic.value = []; }}/>}
      {step.value === STEP.ChooseAction && <ChooseAction selectedMusic={selectedMusic.value} continue={(action: STEP) => step.value = action}/>}
      {step.value === STEP.EditProps && <EditProps selectedMusicIds={selectedMusic.value} closeModal={() => { step.value = STEP.Select; selectedMusic.value = []; }}/>}
      {step.value === STEP.ProgressDisplay && <ProgressDisplay continue={() => { step.value = STEP.Select; selectedMusic.value = []; }}/>}
    </div>;
  }
})
