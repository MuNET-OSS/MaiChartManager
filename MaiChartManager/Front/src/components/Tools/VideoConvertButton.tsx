import { getUrl } from '@/client/api';
import { NButton, NCheckbox, NFlex, NModal, NProgress, useMessage } from 'naive-ui';
import { defineComponent, ref } from 'vue';
import { useStorage } from '@vueuse/core';
import { LicenseStatus } from '@/client/apiGen';
import { globalCapture, showNeedPurchaseDialog, version } from '@/store/refs';
import { fetchEventSource } from '@microsoft/fetch-event-source';

enum STEP {
  None,
  Options,
  Progress,
}

// 视频转换选项的默认值
const defaultVideoConvertOptions = {
  noScale: false,
  yuv420p: true,
}

export default defineComponent({
  setup(props, { expose }) {
    const message = useMessage();
    const step = ref(STEP.None);
    const progress = ref(0);
    const videoConvertOptions = useStorage('videoConvertOptions', defaultVideoConvertOptions, undefined, { mergeDefaults: true });

    const handleVideoConvert = async () => {
      step.value = STEP.Progress;
      progress.value = 0;

      const controller = new AbortController();

      try {
        await new Promise<void>((resolve, reject) => {
          fetchEventSource(getUrl(`VideoConvertToolApi?noScale=${videoConvertOptions.value.noScale}&yuv420p=${videoConvertOptions.value.yuv420p}`), {
            signal: controller.signal,
            method: 'POST',
            onerror(e) {
              reject(e);
              controller.abort();
              throw new Error("disable retry onerror");
            },
            onclose() {
              reject(new Error("EventSource Close"));
              controller.abort();
              throw new Error("disable retry onclose");
            },
            openWhenHidden: true,
            onmessage: (e) => {
              switch (e.event) {
                case 'Progress':
                  progress.value = parseInt(e.data);
                  break;
                case 'Success':
                  console.log("success", e.data);
                  controller.abort();
                  message.success("转换完成！");
                  resolve();
                  break;
                case 'Error':
                  controller.abort();
                  reject(new Error(e.data));
                  break;
              }
            }
          });
        });
      } catch (e: any) {
        if (e?.name === 'AbortError') return;
        console.log(e);
        globalCapture(e, "视频转换出错");
      } finally {
        step.value = STEP.None;
      }
    };

    const trigger = () => {
      // 检查是否为赞助版
      if (version.value?.license !== LicenseStatus.Active) {
        showNeedPurchaseDialog.value = true;
        return;
      }
      // 显示选项对话框
      step.value = STEP.Options;
    };

    // 暴露方法供外部调用
    expose({
      trigger,
    });

    return () => <>

      <NModal
        preset="card"
        class="w-[min(30vw,25em)]"
        title="视频转换选项"
        show={step.value === STEP.Options}
        onUpdateShow={() => step.value = STEP.None}
      >{{
        default: () => <NFlex vertical size="large">
          <div>将 MP4 视频转换为 DAT 格式（USM 容器）</div>
          <NCheckbox v-model:checked={videoConvertOptions.value.noScale}>
            不要缩放视频到 1080 宽度
          </NCheckbox>
          <NCheckbox v-model:checked={videoConvertOptions.value.yuv420p}>
            使用 YUV420P 颜色空间
          </NCheckbox>
        </NFlex>,
        footer: () => <NFlex justify="end">
          <NButton onClick={() => step.value = STEP.None}>取消</NButton>
          <NButton type="primary" onClick={handleVideoConvert}>确定</NButton>
        </NFlex>
      }}</NModal>

      <NModal
        preset="card"
        class="w-[min(40vw,40em)]"
        title="正在转换…"
        show={step.value === STEP.Progress}
        closable={false}
        maskClosable={false}
        closeOnEsc={false}
      >
        <NProgress
          type="line"
          status="success"
          percentage={progress.value}
          indicator-placement="inside"
          processing
        >
          {progress.value === 100 ? '还在处理，别急…' : `${progress.value}%`}
        </NProgress>
      </NModal>
    </>;
  },
});

