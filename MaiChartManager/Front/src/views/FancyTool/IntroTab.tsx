import { computed, defineComponent, ref, watch } from 'vue';
import { Button, CheckBox, addToast, showTransactionalDialog } from '@munet/ui';
import { useI18n } from 'vue-i18n';
import api, { getUrl } from '@/client/api';
import type { IntroMovieInfo } from '@/client/apiGen';
import { globalCapture } from '@/store/refs';
import { pickFile } from '@/utils/pickFile';
import { fileToken, refreshEntries, selectedId } from './refs';
import { showFancyError } from './errors';

type Side = '' | 'L' | 'R';

export default defineComponent({
  setup() {
    const { t } = useI18n();

    const busy = ref(false);
    const introInfo = ref<IntroMovieInfo | null>(null);
    // 左右屏共用一个视频 / 各用一个。切换开关本身不动文件，替换视频时才写入
    const introSame = ref(true);
    // 视频实际分辨率：开场视频大多是 1:1，显示出来方便确认没有被压扁
    const sameMeta = ref('');
    const leftMeta = ref('');
    const rightMeta = ref('');

    // 正在上传/删除时先不渲染预览：播放器还读着文件的话，Windows 会因占用删不掉
    const suppressPreview = computed(() => busy.value);

    const load = async () => {
      const id = selectedId.value;
      if (!id) {
        introInfo.value = null;
        return;
      }
      const data = (await api.GetIntroMovie({ id })).data;
      // 切曲目后旧请求可能才回来，丢弃过期响应
      if (id !== selectedId.value) return;

      introInfo.value = data;
      sameMeta.value = '';
      leftMeta.value = '';
      rightMeta.value = '';
      // 单文件优先；只有左右各一个文件时才进分屏模式
      introSame.value = !data || !!data.hasSingle || !(data.hasLeft && data.hasRight);
    };

    watch(selectedId, () => {
      if (!selectedId.value) return;
      load().catch(e => globalCapture(e, t('tools.fancy.errorLoad')));
    }, { immediate: true });

    const hasIntroMovie = computed(() =>
      !!(introInfo.value?.hasSingle || introInfo.value?.hasLeft || introInfo.value?.hasRight));

    const url = (side?: 'L' | 'R') => suppressPreview.value
      ? ''
      : getUrl(`GetIntroMovieFileApi?id=${selectedId.value}${side ? `&side=${side}` : ''}&t=${fileToken.value}`);

    const samePreviewSrc = computed(() => {
      const info = introInfo.value;
      if (!selectedId.value || !info) return '';
      if (info.hasSingle) return url();
      if (info.hasLeft) return url('L');
      if (info.hasRight) return url('R');
      return '';
    });

    // 左右分开时，某一侧还没有自己的文件就先用单文件顶替，方便对比
    const leftPreviewSrc = computed(() => {
      const info = introInfo.value;
      if (!selectedId.value || !info) return '';
      return info.hasLeft ? url('L') : (info.hasSingle ? url() : '');
    });

    const rightPreviewSrc = computed(() => {
      const info = introInfo.value;
      if (!selectedId.value || !info) return '';
      return info.hasRight ? url('R') : (info.hasSingle ? url() : '');
    });

    const samePreviewPath = computed(() => {
      const info = introInfo.value;
      return info?.singlePath ?? info?.leftPath ?? info?.rightPath ?? '';
    });

    const leftPreviewPath = computed(() => {
      const info = introInfo.value;
      return info?.leftPath ?? info?.singlePath ?? '';
    });

    const rightPreviewPath = computed(() => {
      const info = introInfo.value;
      return info?.rightPath ?? info?.singlePath ?? '';
    });

    // 视频一律静音播放，声音单独播一份 acb 解码后的 wav（左右分屏也只有这一路声音，不会重声）
    const audioSrc = computed(() => selectedId.value && introInfo.value?.hasAudio && !suppressPreview.value
      ? getUrl(`GetIntroAudioFileApi?id=${selectedId.value}&t=${fileToken.value}`)
      : '');

    let sameVideoEl: HTMLVideoElement | null = null;
    let leftVideoEl: HTMLVideoElement | null = null;
    let rightVideoEl: HTMLVideoElement | null = null;
    let audioEl: HTMLAudioElement | null = null;

    const readMeta = (e: Event) => {
      const video = e.target as HTMLVideoElement;
      return video.videoWidth && video.videoHeight ? `${video.videoWidth} × ${video.videoHeight}` : '';
    };

    const playAudio = (playing: boolean) => {
      if (!audioEl) return;
      if (playing) audioEl.play().catch(() => {});
      else audioEl.pause();
    };

    const syncAudioSeek = (from: HTMLVideoElement | null) => {
      if (!audioEl || !from || !Number.isFinite(from.currentTime)) return;
      if (Math.abs(audioEl.currentTime - from.currentTime) > 0.15) audioEl.currentTime = from.currentTime;
    };

    // 分屏预览时同步两侧播放状态，方便看出游戏里两边一起播的效果
    const syncPlay = (from: HTMLVideoElement | null, to: HTMLVideoElement | null) => {
      if (!from) return;
      const playing = !from.paused;
      if (to) {
        if (playing) to.play().catch(() => {});
        else to.pause();
      }
      playAudio(playing);
    };

    // 手动拖动进度条时把另一侧也拖到同一时间点；差值小于阈值就不动，避免两侧互相触发死循环
    const syncSeek = (from: HTMLVideoElement | null, to: HTMLVideoElement | null) => {
      syncAudioSeek(from);
      if (!from || !to) return;
      if (!Number.isFinite(from.currentTime)) return;
      if (Math.abs(to.currentTime - from.currentTime) <= 0.15) return;
      const target = Number.isFinite(to.duration) ? Math.min(from.currentTime, to.duration) : from.currentTime;
      to.currentTime = Math.max(0, target);
    };

    // 主动停掉并卸载预览，让文件句柄尽快释放（上传/删除前调用，避免 Windows 因占用删不掉）
    const releaseMedia = () => {
      for (const el of [sameVideoEl, leftVideoEl, rightVideoEl]) {
        if (!el) continue;
        try {
          el.pause();
          el.removeAttribute('src');
          el.load();
        } catch {
          // 忽略
        }
      }
      try {
        audioEl?.pause();
      } catch {
        // 忽略
      }
    };

    const afterChange = async () => {
      fileToken.value = Date.now();
      await Promise.all([load(), refreshEntries()]);
    };

    const uploadMovie = async (file: File, side: Side) => {
      if (!selectedId.value) return;
      busy.value = true;
      releaseMedia();
      try {
        const result = (await api.SetIntroMovie({
          id: selectedId.value,
          // 注意：生成的 FormData 序列化会把 undefined 变成字符串 "undefined"，
          // 所以这里必须传空字符串（后端会当成「单文件」）而不是 undefined。
          side,
          file,
        })).data;

        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }

        await afterChange();
        // mp4 是静音的，声音来自自动生成的 acb/awb
        addToast({
          message: introInfo.value?.hasAudio
            ? t('tools.fancy.introAudioGenerated')
            : t('tools.fancy.introAudioSilent'),
          type: 'success',
        });
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorConvert'));
      } finally {
        busy.value = false;
      }
    };

    const chooseMovie = async (side: Side = '') => {
      const file = await pickFile('video/*');
      if (file) await uploadMovie(file, side);
    };

    // 左右屏共用同一套音频（Enter_<id>.acb / .awb），所以不区分 side
    const uploadAudio = async (file: File) => {
      if (!selectedId.value) return;
      busy.value = true;
      try {
        const result = (await api.SetIntroAudio({ id: selectedId.value, file })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }

        // 音频换了 URL 必须跟着变，否则 <audio> 可能还播浏览器缓存里的旧音频
        await afterChange();
        addToast({ message: t('tools.fancy.introAudioGenerated'), type: 'success' });
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorConvert'));
      } finally {
        busy.value = false;
      }
    };

    const chooseAudio = async () => {
      const file = await pickFile('audio/*,.wav,.mp3,.ogg,.aac,.m4a,.flac');
      if (file) await uploadAudio(file);
    };

    const confirmThen = async (message: string, action: () => Promise<void>) => {
      const confirmed = await showTransactionalDialog(
        t('tools.fancy.introTab'),
        message,
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) return;

      busy.value = true;
      releaseMedia();
      try {
        await action();
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const removeMovie = () => {
      const id = selectedId.value;
      if (!id) return;
      return confirmThen(t('tools.fancy.introRemoveConfirm', { id }), async () => {
        const result = (await api.DeleteIntroMovie({ id })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }
        await afterChange();
      });
    };

    const removeAudio = () => {
      const id = selectedId.value;
      if (!id) return;
      return confirmThen(t('tools.fancy.introAudioRemoveConfirm', { id }), async () => {
        const result = (await api.DeleteIntroAudio({ id })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }
        await afterChange();
      });
    };

    const renderPreview = () => {
      if (!hasIntroMovie.value) {
        return <div class="text-sm op-50 px-2 py-4">{t('tools.fancy.noPreview')}</div>;
      }

      if (introSame.value) {
        // 高度驱动尺寸：开场视频本身是 1:1，写死宽度会把画面压成中间一小块
        return <div class="flex flex-col items-center gap-1">
          <video key={samePreviewSrc.value} src={samePreviewSrc.value} controls muted
                 ref={(el) => { sameVideoEl = el as HTMLVideoElement | null; }}
                 class="max-h-80 max-w-full rounded bg-black object-contain"
                 onLoadedmetadata={(e: Event) => { sameMeta.value = readMeta(e); }}
                 onPlay={() => syncPlay(sameVideoEl, null)}
                 onPause={() => syncPlay(sameVideoEl, null)}
                 onSeeked={() => syncSeek(sameVideoEl, null)}/>
          <div class="text-xs op-50 tabular-nums break-all">
            {[sameMeta.value, samePreviewPath.value].filter(Boolean).join(' · ')}
          </div>
        </div>;
      }

      // 左右分开：1:2 的并排预览框，两个视频同时播放
      return <div class="flex flex-col items-center gap-1">
        <div class="flex gap-1 h-80 max-w-full" style={{ aspectRatio: '2 / 1' }}>
          <video key={`L-${leftPreviewSrc.value}`} src={leftPreviewSrc.value} controls muted
                 ref={(el) => { leftVideoEl = el as HTMLVideoElement | null; }}
                 class="h-full w-1/2 rounded bg-black object-contain"
                 onLoadedmetadata={(e: Event) => { leftMeta.value = readMeta(e); }}
                 onPlay={() => syncPlay(leftVideoEl, rightVideoEl)}
                 onPause={() => syncPlay(leftVideoEl, rightVideoEl)}
                 onSeeked={() => syncSeek(leftVideoEl, rightVideoEl)}/>
          <video key={`R-${rightPreviewSrc.value}`} src={rightPreviewSrc.value} controls muted
                 ref={(el) => { rightVideoEl = el as HTMLVideoElement | null; }}
                 class="h-full w-1/2 rounded bg-black object-contain"
                 onLoadedmetadata={(e: Event) => { rightMeta.value = readMeta(e); }}
                 onPlay={() => syncPlay(rightVideoEl, leftVideoEl)}
                 onPause={() => syncPlay(rightVideoEl, leftVideoEl)}
                 onSeeked={() => syncSeek(rightVideoEl, leftVideoEl)}/>
        </div>
        <div class="text-xs op-50 tabular-nums">{`L ${leftMeta.value || '-'}   R ${rightMeta.value || '-'}`}</div>
        <div class="text-xs op-50 break-all text-center flex flex-col">
          <span>L: {leftPreviewPath.value || t('tools.fancy.none')}</span>
          <span>R: {rightPreviewPath.value || t('tools.fancy.none')}</span>
        </div>
      </div>;
    };

    return () => <div class="flex flex-col gap-3 py">
      <div class="text-sm op-70 ws-normal">{t('tools.fancy.introHint')}</div>

      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.preview')}</div>
        {renderPreview()}
      </div>

      {!!audioSrc.value &&
        <audio key={audioSrc.value} src={audioSrc.value} class="hidden"
               ref={(el) => { audioEl = el as HTMLAudioElement | null; }}/>}
      {(hasIntroMovie.value || introInfo.value?.hasAudio) &&
        <div class="text-xs op-50 px-1 text-center break-all">
          {introInfo.value?.hasAudio
            ? `${t('tools.fancy.introAudio')}${introSame.value ? '' : ` (${t('tools.fancy.introAudioShared')})`}: Enter_${selectedId.value}.acb + .awb`
            : `${t('tools.fancy.introAudio')}: ${t('tools.fancy.none')}`}
        </div>}

      <CheckBox v-model:value={introSame.value} class="px-1 text-sm" disabled={busy.value}>
        {t('tools.fancy.introSame')}
      </CheckBox>
      {!introSame.value &&
        <div class="text-xs op-60 px-1 ws-normal">{t('tools.fancy.introSameHint')}</div>}

      <div class="flex gap-2">
        {introSame.value
          ? <Button class="w-0 grow" disabled={busy.value} ing={busy.value} onClick={() => chooseMovie()}>
              {hasIntroMovie.value ? t('tools.fancy.introReplace') : t('tools.fancy.introUpload')}
            </Button>
          : <>
              <Button class="w-0 grow" disabled={busy.value} ing={busy.value} onClick={() => chooseMovie('L')}>
                {t('tools.fancy.introLeft')}
              </Button>
              <Button class="w-0 grow" disabled={busy.value} ing={busy.value} onClick={() => chooseMovie('R')}>
                {t('tools.fancy.introRight')}
              </Button>
            </>}
        <Button class="w-0 grow" disabled={busy.value || !hasIntroMovie.value} onClick={removeMovie}>
          {t('tools.fancy.introRemove')}
        </Button>
      </div>

      <div class="flex gap-2">
        <Button class="w-0 grow" disabled={busy.value} ing={busy.value} onClick={chooseAudio}>
          {introInfo.value?.hasAudio ? t('tools.fancy.introAudioReplace') : t('tools.fancy.introAudioAdd')}
        </Button>
        <Button class="w-0 grow" disabled={busy.value || !introInfo.value?.hasAudio} onClick={removeAudio}>
          {t('tools.fancy.introAudioRemove')}
        </Button>
      </div>
    </div>;
  },
});
