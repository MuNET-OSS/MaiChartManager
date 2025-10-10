import api from '@/client/api';
import { NDropdown, NButton, useMessage } from 'naive-ui';
import { defineComponent, ref } from 'vue';
import VideoConvertButton from './VideoConvertButton';

enum DROPDOWN_OPTIONS {
  AudioConvert,
  VideoConvert,
}

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {
    const message = useMessage();
    const videoConvertRef = ref<{ trigger: () => void }>();

    const options = [
      { label: "音频转换（ACB + AWB）", key: DROPDOWN_OPTIONS.AudioConvert },
      { label: "视频转换（DAT）", key: DROPDOWN_OPTIONS.VideoConvert },
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
        case DROPDOWN_OPTIONS.VideoConvert: {
          videoConvertRef.value?.trigger();
          break;
        }
      }
    }

    return () => (location.hostname === 'mcm.invalid' || import.meta.env.DEV) && <>
      <NDropdown options={options} trigger="click" onSelect={handleOptionClick}>
        <NButton secondary>
          工具
        </NButton>
      </NDropdown>
      <VideoConvertButton ref={videoConvertRef} />
    </>;
  },
});
