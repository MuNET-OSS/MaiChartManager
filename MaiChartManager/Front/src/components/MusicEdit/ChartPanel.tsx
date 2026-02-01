import { computed, defineComponent, PropType, watch } from "vue";
import { Chart } from "@/client/apiGen";
import { NButton, NFlex, NForm, NFormItem, NInput, NInputNumber, NSelect, NSwitch } from "naive-ui";
import api from "@/client/api";
import { disableSync, selectedADir, selectedMusic } from "@/store/refs";
import { LEVELS } from "@/consts";
import ProblemsDisplay from "@/components/ProblemsDisplay";
import PreviewChartButton from "@/components/MusicEdit/PreviewChartButton";
import { useI18n } from 'vue-i18n';
import { prepareReplaceChart } from "@/components/DragDropDispatcher/ReplaceChartModal";

const LEVELS_OPTIONS = LEVELS.map((level, index) => ({label: level, value: index}));

export default defineComponent({
  props: {
    songId: {type: Number, required: true},
    chartIndex: {type: Number, required: true},
    chart: {type: Object as PropType<Chart>, required: true},
  },
  setup(props) {
    const { t } = useI18n();
    const levelValue = computed({
      get: () => props.chart.level! + props.chart.levelDecimal! / 10,
      set: (value: number) => {
        props.chart.level = Math.floor(value);
        props.chart.levelDecimal = Math.round(value * 10 - props.chart.level * 10);
      }
    })

    const sync = (key: keyof Chart, method: Function) => async () => {
      if (disableSync.value || !props.chart) return;
      selectedMusic.value!.modified = true;
      await method(props.songId, props.chartIndex, selectedADir.value, props.chart[key]!);
    }

    watch(() => props.chart.designer, sync('designer', api.EditChartDesigner));
    watch(() => props.chart.level, sync('level', api.EditChartLevel));
    watch(() => props.chart.levelDecimal, sync('levelDecimal', api.EditChartLevelDecimal));
    watch(() => props.chart.maxNotes, sync('maxNotes', api.EditChartNoteCount));
    watch(() => props.chart.enable, sync('enable', api.EditChartEnable));
    watch(() => props.chart.levelId, sync('levelId', api.EditChartLevelDisplay));

    return () => <NForm showFeedback={false} labelPlacement="top" disabled={selectedADir.value === 'A000'}>
      <NFlex vertical>
        <NFlex align="center" class="absolute right-0 top-0 m-xy mt-2 z-2">
          <PreviewChartButton songId={props.songId} level={props.chartIndex}/>
          <NButton secondary onClick={() => prepareReplaceChart()}>
            {t('music.edit.replaceChart')}
          </NButton>
        </NFlex>
        <NFormItem label={t('music.edit.chartEnable')} labelPlacement="left" class="ml-2px">
          <NFlex align="center">
            <NSwitch v-model:value={props.chart.enable}/>
            <ProblemsDisplay problems={props.chart.problems!}/>
          </NFlex>
        </NFormItem>
        <NFormItem label={t('music.edit.chartAuthor')}>
          <NInput v-model:value={props.chart.designer} placeholder=""/>
        </NFormItem>
        <NFormItem label={t('music.edit.chartLevel')}>
          <NSelect options={LEVELS_OPTIONS as any} v-model:value={props.chart.levelId}/>
        </NFormItem>
        <NFormItem label={t('music.edit.chartConstant')}>
          <NInputNumber showButton={false} class="w-full" precision={1} v-model:value={levelValue.value} min={0}/>
        </NFormItem>
        <NFormItem label={t('music.edit.chartNoteCount')}>
          <NInputNumber showButton={false} class="w-full" precision={0} v-model:value={props.chart.maxNotes} min={0}/>
        </NFormItem>
      </NFlex>
    </NForm>;
  },
});
