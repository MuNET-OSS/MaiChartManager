import { defineComponent, PropType } from "vue";
import { NAlert, NFlex, NScrollbar } from "naive-ui";
import { MessageLevel, ShiftMethod } from "@/client/apiGen";
import { ImportChartMessageEx, TempOptions } from "./types";
import { showNeedPurchaseDialog } from "@/store/refs";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    tempOptions: {type: Object as PropType<TempOptions>, required: true},
    errors: {type: Array as PropType<ImportChartMessageEx[]>, required: true}
  },
  setup(props, {emit}) {
    const {t} = useI18n();

    return () => <NScrollbar class="max-h-24vh">
      <NFlex vertical>
        {
          props.errors.map((error, i) => {
            if ('first' in error) {
              if (error.padding > 0 && props.tempOptions.shift === ShiftMethod.Legacy) {
                return <NAlert key={i} type="info"
                               title={error.name}>{t('chart.import.addPadding', {padding: error.padding.toFixed(3)})}</NAlert>
              }
              if (error.padding < 0 && props.tempOptions.shift === ShiftMethod.Legacy) {
                return <NAlert key={i} type="info"
                               title={error.name}>{t('chart.import.trimPadding', {padding: (-error.padding).toFixed(3)})}</NAlert>
              }
              if (error.first > 0 && props.tempOptions.shift === ShiftMethod.NoShift) {
                return <NAlert key={i} type="info"
                               title={error.name}>{t('chart.import.trimFirst', {first: error.first.toFixed(3)})}</NAlert>
              }
              if (error.first < 0 && props.tempOptions.shift === ShiftMethod.NoShift) {
                return <NAlert key={i} type="info"
                               title={error.name}>{t('chart.import.addFirst', {first: (-error.first).toFixed(3)})}</NAlert>
              }
              return <></>
            }
            let type: "default" | "info" | "success" | "warning" | "error" = "default";
            switch (error.level) {
              case MessageLevel.Info:
                type = 'info';
                break;
              case MessageLevel.Warning:
                type = 'warning';
                break;
              case MessageLevel.Fatal:
                type = 'error';
                break;
            }
            return <NAlert key={i} type={type} title={error.name} class={`${error.isPaid && 'cursor-pointer'}`}
              // @ts-ignore
                           onClick={() => error.isPaid && (showNeedPurchaseDialog.value = true)}
            >
              <div class="whitespace-pre-wrap">
                {error.message}
              </div>
            </NAlert>
          })
        }
      </NFlex>
    </NScrollbar>
  }
})
