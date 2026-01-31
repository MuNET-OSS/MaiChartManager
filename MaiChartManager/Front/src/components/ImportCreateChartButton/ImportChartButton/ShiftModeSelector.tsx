import { defineComponent, PropType } from "vue";
import { NFlex, NFormItem, NRadioGroup, NPopover, NRadio } from "naive-ui";
import { ShiftMethod } from "@/client/apiGen";
import { TempOptions } from "./types";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    tempOptions: {type: Object as PropType<TempOptions>, required: true},
  },
  setup(props) {
    const {t} = useI18n();

    return () => <NFormItem label={t('chart.import.option.shiftMode')} labelPlacement="left" showFeedback={false}>
      <NFlex vertical class="w-full">
        <NFlex class="h-34px" align="center">
          <NRadioGroup v-model:value={props.tempOptions.shift} disabled={props.tempOptions.shiftLocked}>
            <NPopover trigger="hover">
              {{
                trigger: () => <NRadio value={ShiftMethod.Bar} label={t('chart.import.option.shiftByBar')}/>,
                default: () => <div>
                  {props.tempOptions.shiftLocked ? t('chart.import.option.shiftModeLocked') : t('chart.import.option.shiftByBarDesc')}
                </div>
              }}
            </NPopover>
            <NPopover trigger="hover">
              {{
                trigger: () => <NRadio value={ShiftMethod.Legacy} label={t('chart.import.option.shiftLegacy')}/>,
                default: () => <div>
                  {props.tempOptions.shiftLocked ? t('chart.import.option.shiftModeLocked') : t('chart.import.option.shiftLegacyDesc')}
                </div>
              }}
            </NPopover>
            <NPopover trigger="hover">
              {{
                trigger: () => <NRadio value={ShiftMethod.NoShift} label={t('chart.import.option.shiftNoMove')}/>,
                default: () => <div>
                  {props.tempOptions.shiftLocked ? t('chart.import.option.shiftModeLocked') : t('chart.import.option.shiftNoMoveDesc')}
                </div>
              }}
            </NPopover>
          </NRadioGroup>
        </NFlex>
      </NFlex>
    </NFormItem>
  }
})
