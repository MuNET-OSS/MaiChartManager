import { defineComponent, PropType } from "vue";
import { Popover } from "@munet/ui";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import OverrideUpIcon from '@/icons/override-up.svg?component'
import OverrideDownIcon from '@/icons/override-down.svg?component'
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    conflicts: {type: Array as PropType<MusicXmlWithABJacket[]>, required: true},
    type: {type: String, required: true},
  },
  setup(props) {
    const { t } = useI18n();
    
    return () => !!props.conflicts.length && <Popover trigger="hover">
      {{
        trigger: () => props.type === 'up' ?
          <OverrideUpIcon class="c-blue text-2em"/> : <OverrideDownIcon class="c-indigo text-2em"/>,
        default: () => <div class="flex flex-col gap-2">
          {props.type === 'up' ? t('assetDir.conflictOverrides') : t('assetDir.conflictOverriddenBy')}
          {props.conflicts!.map((p, index) => <div key={index}>{p.assetDir}</div>)}
        </div>
      }}
    </Popover>;
  }
})
