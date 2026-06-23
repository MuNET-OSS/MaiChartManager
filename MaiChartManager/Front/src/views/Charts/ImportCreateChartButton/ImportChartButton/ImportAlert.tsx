import { computed, defineComponent, PropType, ref } from "vue";
import { MessageLevel, ShiftMethod } from "@/client/apiGen";
import { ImportChartMessageEx, TempOptions } from "./types";
import { showNeedPurchaseDialog } from "@/store/refs";
import { useI18n } from 'vue-i18n';
import { Button, Modal } from "@munet/ui";

export default defineComponent({
  props: {
    tempOptions: {type: Object as PropType<TempOptions>, required: true},
    errors: {type: Array as PropType<ImportChartMessageEx[]>, required: true}
  },
  setup(props) {
    const {t} = useI18n();
    const detail = ref<string>();

    const i18nPostfix = computed(() => {
      switch (props.tempOptions.shift) {
        case ShiftMethod.Legacy: return "Padding"
        case ShiftMethod.NoShift: return "First"
      }
    })

    return () => <>
      <div class="of-y-auto cst max-h-20vh">
        <div class="flex flex-col gap-2">
        {
          props.errors.map((error, i) => {
            if ('first' in error) {
              const padding = error.chartPaddings?.[props.tempOptions.shift]! - error.first
              if (props.tempOptions.shift === ShiftMethod.Bar) {
                if (error.chartPaddings?.[ShiftMethod.Bar]! > 0) { // 如果选择的是Bar模式且确实需要addBar，则也显示一条提示
                  return <div key={i} class="p-3 rounded border border-blue/30 bg-blue/10">
                    <div class="font-bold mb-1">{error.name}</div>
                    {t('chart.import.addBar')}
                  </div>
                }
              }
              else if (padding > 0){
                return <div key={i} class="p-3 rounded border border-blue/30 bg-blue/10">
                  <div class="font-bold mb-1">{error.name}</div>
                  {/* i18n 中的参数始终使用 padding，因为要动态选择 key */}
                  {t('chart.import.add' + i18nPostfix.value, {padding: padding.toFixed(3)})}
                </div>
              }
              else if (padding < 0) {
                return <div key={i} class="p-3 rounded border border-blue/30 bg-blue/10">
                  <div class="font-bold mb-1">{error.name}</div>
                  {t('chart.import.trim' + i18nPostfix.value, {padding: (-padding).toFixed(3)})}
                </div>
              }
              return <></>
            }
            let borderColor = 'border-gray/30';
            let bgColor = 'bg-gray/10';
            switch (error.level) {
              case MessageLevel.Info:
                borderColor = 'border-blue/30';
                bgColor = 'bg-blue/10';
                break;
              case MessageLevel.Warning:
                borderColor = 'border-yellow/30';
                bgColor = 'bg-yellow/10';
                break;
              case MessageLevel.Fatal:
                borderColor = 'border-red/30';
                bgColor = 'bg-red/10';
                break;
            }
            return <div key={i} class={`p-3 rounded border ${borderColor} ${bgColor} ${error.isPaid && 'cursor-pointer'}`}
                         onClick={() => {
                           if (error.isPaid) showNeedPurchaseDialog.value = true;
                         }}
            >
              <div class="flex items-center gap-2 mb-1">
                <div class="font-bold w-0 grow">{error.name}</div>
                {error.detail && <span onClick={(event: MouseEvent) => event.stopPropagation()}>
                  <Button size="small" onClick={() => {
                    detail.value = error.detail;
                  }}>{t('common.detail')}</Button>
                </span>}
              </div>
              <div class="whitespace-pre-wrap">
                {error.message}
              </div>
            </div>
          })
        }
        </div>
      </div>
      <Modal
        width="min(70vw,70em)"
        title={t('common.detail')}
        show={!!detail.value}
        onUpdateShow={() => detail.value = undefined}
      >
        <pre class="max-h-60vh of-auto cst whitespace-pre-wrap break-words text-xs font-mono">{detail.value}</pre>
      </Modal>
    </>
  }
})
