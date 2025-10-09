import { NPopover, NFlex } from 'naive-ui';
import { defineComponent, PropType, ref, computed, watch } from 'vue';

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {

    return () => <NPopover trigger="hover">
    {{
      trigger: () => <div class="text-#5b79c4 i-mdi:arrow-left-right-bold text-2em" />,
      default: () => "LongMusic"
    }}
  </NPopover>;
  },
});
