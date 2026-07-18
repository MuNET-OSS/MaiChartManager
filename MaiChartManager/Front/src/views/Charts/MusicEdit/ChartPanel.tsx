import { computed, defineComponent, PropType, watch } from "vue";
import type { Chart } from "@/client/apiGen";
import api from "@/client/api";
import { disableSync, selectedADir, selectedMusic } from "@/store/refs";
import { LEVELS } from "@/consts";
import ProblemsDisplay from "@/components/ProblemsDisplay";
import PreviewChartButton from "@/views/Charts/MusicEdit/PreviewChartButton";
import { useI18n } from 'vue-i18n';
import { Button, CheckBox, NumberInput, Select, TextInput } from "@munet/ui";
import { prepareReplaceChart } from "@/components/DragDropDispatcher/ReplaceChartModal";
import type { ChartSide } from "@/views/Charts/MusicEdit/PreviewChartButton";
import styles from './index.module.sass';

const LEVELS_OPTIONS = LEVELS.map((level, index) => ({label: level, value: index}));

export default defineComponent({
  props: {
    songId: {type: Number, required: true},
    chartIndex: {type: Number, required: true},
    chart: {type: Object as PropType<Chart>, required: true},
    doublePlayer: Boolean,
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

    const sideRow = (side: ChartSide, label: string) => <div class="flex flex-wrap items-center gap-2">
      <span class="text-sm font-500">{label}</span>
      <PreviewChartButton songId={props.songId} level={props.chartIndex} side={side}/>
      <Button onClick={() => prepareReplaceChart(undefined, side)}>
        {t('music.edit.replaceChart')}
      </Button>
    </div>;

    return () => <div class="flex flex-col gap-2">
        {!props.doublePlayer && <div class="absolute right-0 top-0 m-xy mt-2 z-2 flex gap-2">
          <PreviewChartButton songId={props.songId} level={props.chartIndex}/>
          <Button onClick={() => prepareReplaceChart()}>
            {t('music.edit.replaceChart')}
          </Button>
        </div>}
        {props.doublePlayer && <div class={["right-0 top-0 m-x mt-2 z-2 flex justify-end gap-8", styles.doublePlayerChartPanel]}>
          {sideRow('L', t('music.edit.leftChart'))}
          {sideRow('R', t('music.edit.rightChart'))}
        </div>}
        <div class="flex items-center gap-2">
          <CheckBox v-model:value={props.chart.enable} class="m-1">{t('music.edit.chartEnable')}</CheckBox>
          <ProblemsDisplay problems={props.chart.problems!}/>
        </div>
        <div class="ml-1 text-sm">{t('music.edit.chartAuthor')}</div>
        <TextInput v-model:value={props.chart.designer} placeholder=""/>
        <div class="ml-1 text-sm">{t('music.edit.chartLevel')}</div>
        <Select options={LEVELS_OPTIONS} v-model:value={props.chart.levelId}/>
        <div class="ml-1 text-sm">{t('music.edit.chartConstant')}</div>
        <NumberInput class="w-full" step={0.1} decimal={1} v-model:value={levelValue.value} min={0}/>
        <div class="ml-1 text-sm">{t('music.edit.chartNoteCount')}</div>
        <NumberInput class="w-full" v-model:value={props.chart.maxNotes} min={0}/>
    </div>;
  },
});
