import { defineComponent } from "vue";
import UnityWebgl from "unity-webgl";
import dataUrl from './Charts/MusicEdit/majdata-wasm/Build.bin?url';
import frameworkUrl from './Charts/MusicEdit/majdata-wasm/Build.framework.js?url';
import codeUrl from './Charts/MusicEdit/majdata-wasm/Build.wasm?url';
import loaderUrl from './Charts/MusicEdit/majdata-wasm/Build.loader.js?url';
import UnityVue from 'unity-webgl/vue';
import { getUrl } from "@/client/api";
import { useRoute } from 'vue-router';
import { useTitle } from '@vueuse/core';
import { t } from '@/locales';

export default defineComponent({
  setup() {
    const route = useRoute();
    useTitle(t('music.edit.previewChart'));
    const assetDir = route.query.assetDir as string ?? '';
    const songId = route.query.songId as string ?? '';
    const level = route.query.level as string ?? '';
    const side = route.query.side === 'L' || route.query.side === 'R' ? route.query.side : undefined;

    const unityContext = new UnityWebgl({
      dataUrl,
      frameworkUrl,
      loaderUrl,
      codeUrl,
    });

    unityContext.on("mounted", () => {
      setTimeout(() => {
        const chartUrl = new URL(getUrl(`ChartPreviewApi/${assetDir}/${songId}/${level}`));
        if (side) chartUrl.searchParams.set('side', side);
        unityContext.send("HandleJSMessages", "ReceiveMessage", [
          chartUrl.toString(),
          getUrl(`GetMusicWavApi/${assetDir}/${songId}`),
          getUrl(`GetJacketApi/${assetDir}/${songId}`),
          '',
          'lv0'
        ].join('\n'));
      }, 500);
    });

    return () => (
      <div style={{ width: '100vw', height: '100vh', display: 'flex', flexDirection: 'column' }}>
        <UnityVue unity={unityContext} height="100%" width="100%" />
      </div>
    );
  },
});
