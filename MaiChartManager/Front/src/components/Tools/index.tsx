import api from '@/client/api';
import { NDropdown, NButton, useMessage } from 'naive-ui';
import { defineComponent, PropType, ref, computed, watch } from 'vue';

enum DROPDOWN_OPTIONS {
  AudioConvert,
}

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {
    const message = useMessage();
    const options = [
      { label: "音频转换", key: DROPDOWN_OPTIONS.AudioConvert },
    ]

    const handleOptionClick = async (key: DROPDOWN_OPTIONS) => {
      switch (key) {
        case DROPDOWN_OPTIONS.AudioConvert: {
          const res = await api.AudioConvertTool();
          if (res.status === 200) {
            message.success("转换成功");
          } else {
            message.error("转换失败");
          }
          break;
        }
      }
    }

    return () => (location.hostname === 'mcm.invalid' || import.meta.env.DEV) && <NDropdown options={options} trigger="click" onSelect={handleOptionClick}>
      <NButton secondary>
        工具
      </NButton>
    </NDropdown>;
  },
});
