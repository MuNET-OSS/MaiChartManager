import { defineComponent } from "vue";
import { selectedADir } from "@/store/refs";
import { t } from "@/locales";
import { isPhotino } from "@/client/api";

export default defineComponent({
  props: {
    songId: {type: Number, required: true},
    level: {type: Number, required: true},
  },
  setup(props) {
    const openPreview = () => {
      const params = new URLSearchParams({
        assetDir: selectedADir.value!,
        songId: String(props.songId),
        level: String(props.level),
      });
      const width = 960;
      const height = 640;
      const left = (screen.width - width) / 2;
      const top = (screen.height - height) / 2;
      const url = new URL(location.href);
      url.hash = `/chart-preview?${params}`;

      if (isPhotino) {
        // WebKitGTK 不支持 window.open 弹新窗口。改为通知 Photino 宿主开一个内置 webview 子窗口加载预览页。
        (window as any).external.sendMessage(JSON.stringify({
          type: 'open-window',
          url: url.toString(),
          title: t('music.edit.previewChart'),
          width,
          height,
        }));
        return;
      }

      window.open(url, '_blank', `width=${width},height=${height},left=${left},top=${top},menubar=no,toolbar=no,location=no,status=no`);
    };

    return () => (
      <button onClick={openPreview}>
        {t('music.edit.previewChart')}
      </button>
    );
  },
});
