import { computed, defineComponent, ref, watch } from 'vue';
import { Button, CheckBox, Modal, NumberInput, Select, TabPane, Tabs, TextInput, addToast, showTransactionalDialog } from '@munet/ui';
import api, { getUrl } from '@/client/api';
import type {
  CamouflageEntry,
  CamouflageInfoDto,
  IntroMovieEntry,
  IntroMovieInfo,
} from '@/client/apiGen';
import { globalCapture, musicListAll } from '@/store/refs';
import { useI18n } from 'vue-i18n';
import { pickFile } from '@/utils/pickFile';

// 后端返回的错误码 → 文案
const errorKeys: Record<string, string> = {
  'invalid-id': 'tools.fancy.errorInvalidId',
  'invalid-side': 'tools.fancy.errorInvalidSide',
  'invalid-text': 'tools.fancy.errorInvalidText',
  'invalid-image': 'tools.fancy.errorInvalidImage',
  'license-required': 'tools.fancy.errorLicense',
  'game-path-missing': 'tools.fancy.errorGamePath',
  'convert-failed': 'tools.fancy.errorConvert',
  'write-failed': 'tools.fancy.errorWrite',
  'delete-failed': 'tools.fancy.errorDeleteLocked',
  'invalid-audio': 'tools.fancy.errorInvalidAudio',
};

const emptyCamouflage = () => ({
  name: '',
  artist: '',
  noteDesigner: '',
  hideNoteDesigner: false,
  unlockScore: 0,
  hasJacket: false,
  jacketPath: '',
});

export default defineComponent({
  setup(props, { expose }) {
    const { t } = useI18n();

    const show = ref(false);
    const tab = ref('intro');
    const musicId = ref<number | null>(null);
    const busy = ref(false);
    // 用于给预览加时间戳，避免换文件后浏览器还用缓存
    const previewToken = ref(Date.now());
    // 正在上传/删除时先不渲染预览：播放器还读着文件的话，Windows 会因占用删不掉
    const previewSuppressed = computed(() => busy.value);
    // 视频实际分辨率：开场视频大多是 1:1，显示出来方便确认没有被压扁
    const videoMeta = ref('');
    const leftMeta = ref('');
    const rightMeta = ref('');

    const introSame = ref(true);
    const introInfo = ref<IntroMovieInfo | null>(null);
    const introEntries = ref<IntroMovieEntry[]>([]);

    const camouflage = ref(emptyCamouflage());
    const camouflageEntries = ref<CamouflageEntry[]>([]);

    // MuNET 的 Select 不带搜索，这里用输入框过滤选项
    const musicSearch = ref('');
    const matchesSearch = (music: { id?: number; name?: string | null }) => {
      const keyword = musicSearch.value.trim().toLowerCase();
      if (!keyword) return true;
      return `${music.id} ${music.name ?? ''}`.toLowerCase().includes(keyword);
    };

    // MuNET 的 Select 只收 string | number，用 undefined 表示「未选」
    const selectedMusic = computed({
      get: () => musicId.value ?? undefined,
      set: (value) => { musicId.value = (value as number | undefined) ?? null; },
    });

    const musicOptions = computed(() => {
      const seen = new Set<number>();
      const options: { label: string; value: number }[] = [];
      for (const music of musicListAll.value) {
        if (music.id === undefined || seen.has(music.id)) continue;
        if (!matchesSearch(music)) continue;
        seen.add(music.id);
        options.push({ label: `${music.id} · ${music.name ?? ''}`, value: music.id });
      }
      return options.sort((a, b) => a.value - b.value);
    });

    // 已有列表里按 id 回查真实曲名，查不到就只显示 id
    const musicNameMap = computed(() => {
      const map = new Map<number, string>();
      for (const music of musicListAll.value) {
        if (music.id === undefined || !music.name || map.has(music.id)) continue;
        map.set(music.id, music.name);
      }
      return map;
    });
    const songName = (id: number) => musicNameMap.value.get(id) ?? '';
    // 没选乐曲时不允许填写/操作（后端也是按乐曲 id 落盘的）
    const hasMusic = computed(() => !!musicId.value);

    const fail = (code?: string | null) => {
      const key = code ? errorKeys[code] : undefined;
      addToast({
        message: key ? t(key) : `${t('tools.fancy.errorUnknown')}${code ? ` (${code})` : ''}`,
        type: 'error',
      });
    };

    const loadIntroMovie = async () => {
      if (!musicId.value) {
        introInfo.value = null;
        return;
      }
      introInfo.value = (await api.GetIntroMovie({ id: musicId.value })).data;
      const info = introInfo.value;
      // 单文件优先；只有左右各一个文件时才进分屏模式
      introSame.value = !info || info.hasSingle || !(info.hasLeft && info.hasRight);
    };

    const loadCamouflage = async () => {
      if (!musicId.value) {
        camouflage.value = emptyCamouflage();
        return;
      }
      const data: CamouflageInfoDto = (await api.GetCamouflage({ id: musicId.value })).data;
      camouflage.value = {
        name: data.name ?? '',
        artist: data.artist ?? '',
        noteDesigner: data.noteDesigner ?? '',
        hideNoteDesigner: !!data.hideNoteDesigner,
        unlockScore: data.unlockScore ?? 0,
        hasJacket: !!data.hasJacket,
        jacketPath: data.jacketPath ?? '',
      };
    };

    const loadLists = async () => {
      introEntries.value = (await api.GetIntroMovieList()).data ?? [];
      camouflageEntries.value = (await api.GetCamouflageList()).data ?? [];
    };

    // 只刷新封面状态，保留用户还在编辑、尚未保存的表单内容
    const refreshCamouflageJacket = async () => {
      if (!musicId.value) return;
      const data: CamouflageInfoDto = (await api.GetCamouflage({ id: musicId.value })).data;
      camouflage.value.hasJacket = !!data.hasJacket;
      camouflage.value.jacketPath = data.jacketPath ?? '';
    };

    const reload = async () => {
      previewToken.value = Date.now();
      videoMeta.value = '';
      leftMeta.value = '';
      rightMeta.value = '';
      try {
        await Promise.all([loadIntroMovie(), loadCamouflage(), loadLists()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorLoad'));
      }
    };

    watch(show, (visible) => {
      if (visible) reload();
    });

    watch(musicId, async (id) => {
      if (!show.value) return;
      previewToken.value = Date.now();
      videoMeta.value = '';
      leftMeta.value = '';
      rightMeta.value = '';
      try {
        await Promise.all([loadIntroMovie(), loadCamouflage()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorLoad'));
      }
    });

    const selectMusic = (id: number) => {
      // 载入已有条目时先清掉搜索词，否则下拉框可能因为过滤而显示不出这首歌
      musicSearch.value = '';
      musicId.value = id;
    };

    // ---------- 开场视频 ----------

    const uploadIntroMovie = async (file: File, side: '' | 'L' | 'R' = '') => {
      if (!musicId.value) return;
      busy.value = true;
      releaseMedia();
      try {
        const result = (await api.SetIntroMovie({
          id: musicId.value,
          // 注意：生成的 FormData 序列化会把 undefined 变成字符串 "undefined"，
          // 所以这里必须传空字符串（后端会当成「单文件」）而不是 undefined。
          side,
          file,
        })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        previewToken.value = Date.now();
        await Promise.all([loadIntroMovie(), loadLists()]);
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

    const chooseIntroMovie = async (side: '' | 'L' | 'R' = '') => {
      const file = await pickFile('video/*');
      if (file) await uploadIntroMovie(file, side);
    };

    // 左右屏共用同一套音频（Enter_<id>.acb / .awb），所以不区分 side
    const uploadIntroAudio = async (file: File) => {
      if (!musicId.value) return;
      busy.value = true;
      try {
        const result = (await api.SetIntroAudio({ id: musicId.value, file })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        await Promise.all([loadIntroMovie(), loadLists()]);
        addToast({ message: t('tools.fancy.introAudioGenerated'), type: 'success' });
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorConvert'));
      } finally {
        busy.value = false;
      }
    };

    const chooseIntroAudio = async () => {
      const file = await pickFile('audio/*,.wav,.mp3,.ogg,.aac,.m4a,.flac');
      if (file) await uploadIntroAudio(file);
    };

    const removeIntroAudio = async (id: number) => {
      const confirmed = await showTransactionalDialog(
        t('tools.fancy.introTab'),
        t('tools.fancy.introAudioRemoveConfirm', { id }),
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) return;

      busy.value = true;
      try {
        const result = (await api.DeleteIntroAudio({ id })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        await Promise.all([loadIntroMovie(), loadLists()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const removeIntroMovie = async (id: number) => {
      const confirmed = await showTransactionalDialog(
        t('tools.fancy.introTab'),
        t('tools.fancy.introRemoveConfirm', { id }),
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) return;

      busy.value = true;
      releaseMedia();
      try {
        const result = (await api.DeleteIntroMovie({ id })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        if (id === musicId.value) previewToken.value = Date.now();
        await Promise.all([loadIntroMovie(), loadLists()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    // ---------- 曲目伪装 ----------

    const saveCamouflage = async () => {
      if (!musicId.value) return;
      busy.value = true;
      try {
        const result = (await api.SetCamouflage({
          id: musicId.value,
          name: camouflage.value.name,
          artist: camouflage.value.artist,
          noteDesigner: camouflage.value.noteDesigner,
          hideNoteDesigner: camouflage.value.hideNoteDesigner,
          unlockScore: camouflage.value.unlockScore,
        })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        addToast({ message: t('tools.fancy.saved'), type: 'success' });
        await Promise.all([loadCamouflage(), loadLists()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const uploadCamouflageJacket = async (file: File) => {
      if (!musicId.value) return;
      busy.value = true;
      try {
        const result = (await api.SetCamouflageJacket({ id: musicId.value, file })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        addToast({ message: t('tools.fancy.saved'), type: 'success' });
        previewToken.value = Date.now();
        // 不要用 loadCamouflage：它会把服务端数据写回表单，冲掉还没保存的填写内容
        await Promise.all([refreshCamouflageJacket(), loadLists()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const chooseCamouflageJacket = async () => {
      const file = await pickFile('image/png,image/jpeg');
      if (file) await uploadCamouflageJacket(file);
    };

    const removeCamouflage = async (id: number) => {
      const confirmed = await showTransactionalDialog(
        t('tools.fancy.camouflageTab'),
        t('tools.fancy.camoRemoveConfirm', { id }),
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) return;

      busy.value = true;
      try {
        const result = (await api.DeleteCamouflage({ id })).data;
        if (!result.success) {
          fail(result.error);
          return;
        }
        if (id === musicId.value) previewToken.value = Date.now();
        await Promise.all([loadCamouflage(), loadLists()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    // ---------- 渲染 ----------

    const hasIntroMovie = computed(() =>
      !!(introInfo.value?.hasSingle || introInfo.value?.hasLeft || introInfo.value?.hasRight));

    const introUrl = (side?: 'L' | 'R') => (previewSuppressed.value ? '' : getUrl(
      `GetIntroMovieFileApi?id=${musicId.value}${side ? `&side=${side}` : ''}&t=${previewToken.value}`));

    // 左右相同：优先单文件，退回任意一侧
    const samePreviewSrc = computed(() => {
      const info = introInfo.value;
      if (!musicId.value || !info) return '';
      if (info.hasSingle) return introUrl();
      if (info.hasLeft) return introUrl('L');
      if (info.hasRight) return introUrl('R');
      return '';
    });

    const samePreviewPath = computed(() => {
      const info = introInfo.value;
      return info?.singlePath ?? info?.leftPath ?? info?.rightPath ?? '';
    });

    // 左右分开：某一侧还没有自己的文件时先用单文件顶替，方便对比
    const leftPreviewSrc = computed(() => {
      const info = introInfo.value;
      if (!musicId.value || !info) return '';
      if (info.hasLeft) return introUrl('L');
      return info.hasSingle ? introUrl() : '';
    });

    const rightPreviewSrc = computed(() => {
      const info = introInfo.value;
      if (!musicId.value || !info) return '';
      if (info.hasRight) return introUrl('R');
      return info.hasSingle ? introUrl() : '';
    });

    const leftPreviewPath = computed(() => {
      const info = introInfo.value;
      if (!info) return '';
      return info.leftPath ?? info.singlePath ?? '';
    });

    const rightPreviewPath = computed(() => {
      const info = introInfo.value;
      if (!info) return '';
      return info.rightPath ?? info.singlePath ?? '';
    });

    const jacketPreviewUrl = computed(() => (musicId.value && camouflage.value.hasJacket && !previewSuppressed.value
      ? getUrl(`GetCamouflageJacketApi?id=${musicId.value}&t=${previewToken.value}`)
      : ''));

    // 视频一律静音播放，声音单独播一份 acb 解码后的 wav（左右分屏也只有这一路声音，不会重声）
    const introAudioSrc = computed(() => (musicId.value && introInfo.value?.hasAudio && !previewSuppressed.value
      ? getUrl(`GetIntroAudioFileApi?id=${musicId.value}&t=${previewToken.value}`)
      : ''));

    const entryRow = (id: number, title: string, desc: string, onDelete: (id: number) => void) => (
      <div key={id} class="flex items-center gap-2 px-2 py-1 rounded hover:bg-[oklch(0.97_0.01_var(--hue))]">
        <span class="text-sm font-500 tabular-nums w-16 shrink-0">#{id}</span>
        <div class="w-0 grow flex flex-col">
          {!!title && <span class="text-sm truncate">{title}</span>}
          {!!desc && <span class="text-xs op-60 truncate">{desc}</span>}
        </div>
        <Button class="px-2! py-1! text-xs" onClick={() => selectMusic(id)}>{t('tools.fancy.load')}</Button>
        <Button class="px-2! py-1! text-xs" disabled={busy.value} onClick={() => onDelete(id)}>
          {t('tools.fancy.delete')}
        </Button>
      </div>
    );

    // 分屏预览时同步两侧的播放/暂停，方便看出游戏里两边一起播的效果
    let sameVideoEl: HTMLVideoElement | null = null;
    let leftVideoEl: HTMLVideoElement | null = null;
    let rightVideoEl: HTMLVideoElement | null = null;
    let audioEl: HTMLAudioElement | null = null;

    const readMeta = (e: Event) => {
      const video = e.target as HTMLVideoElement;
      return video.videoWidth && video.videoHeight ? `${video.videoWidth} × ${video.videoHeight}` : '';
    };

    // 播放/暂停跟着视频走，声音只有 audioEl 这一路
    const playAudio = (playing: boolean) => {
      if (!audioEl) return;
      if (playing) audioEl.play().catch(() => {});
      else audioEl.pause();
    };

    const syncAudioSeek = (from: HTMLVideoElement | null) => {
      if (!audioEl || !from || !Number.isFinite(from.currentTime)) return;
      if (Math.abs(audioEl.currentTime - from.currentTime) > 0.15) audioEl.currentTime = from.currentTime;
    };

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

    const renderIntroPreview = () => {
      if (!hasIntroMovie.value) {
        return <div class="text-sm op-50 px-2 py-4">{t('tools.fancy.noPreview')}</div>;
      }

      if (introSame.value) {
        // 高度驱动尺寸：开场视频本身是 1:1，写死宽度会把画面压成中间一小块
        return <div class="flex flex-col items-center gap-1">
          <video key={samePreviewSrc.value} src={samePreviewSrc.value} controls muted
                 ref={(el) => { sameVideoEl = el as HTMLVideoElement | null; }}
                 class="max-h-72 max-w-full rounded bg-black object-contain"
                 onLoadedmetadata={(e: Event) => { videoMeta.value = readMeta(e); }}
                 onPlay={() => syncPlay(sameVideoEl, null)}
                 onPause={() => syncPlay(sameVideoEl, null)}
                 onSeeked={() => syncSeek(sameVideoEl, null)}/>
          <div class="text-xs op-50 tabular-nums break-all">
            {[videoMeta.value, samePreviewPath.value].filter(Boolean).join(' · ')}
          </div>
        </div>;
      }

      // 左右分开：1:2 的并排预览框，两个视频同时播放
      return <div class="flex flex-col items-center gap-1">
        <div class="flex gap-1 h-72 max-w-full" style={{ aspectRatio: '2 / 1' }}>
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

    const renderIntro = () => <div class="flex flex-col gap-3 pt-3">
      <div class="text-sm op-70 ws-normal">{t('tools.fancy.introHint')}</div>
      {!hasMusic.value &&
        <div class="text-sm op-60 px-1">{t('tools.fancy.pickMusicFirst')}</div>}

      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.preview')}</div>
        {renderIntroPreview()}
      </div>
      {!!introAudioSrc.value &&
        <audio key={introAudioSrc.value} src={introAudioSrc.value} class="hidden"
               ref={(el) => { audioEl = el as HTMLAudioElement | null; }}/>}
      {(hasIntroMovie.value || introInfo.value?.hasAudio) &&
        <div class="text-xs op-50 px-1 text-center break-all">
          {introInfo.value?.hasAudio
            ? `${t('tools.fancy.introAudio')}${introSame.value ? '' : ` (${t('tools.fancy.introAudioShared')})`}: Enter_${musicId.value}.acb + .awb`
            : `${t('tools.fancy.introAudio')}: ${t('tools.fancy.none')}`}
        </div>}

      <CheckBox v-model:value={introSame.value} class="px-1 text-sm" disabled={!hasMusic.value}>
        {t('tools.fancy.introSame')}
      </CheckBox>
      {!introSame.value &&
        <div class="text-xs op-60 px-1 ws-normal">{t('tools.fancy.introSameHint')}</div>}

      <div class="flex gap-2">
        {introSame.value
          ? <Button class="w-0 grow" disabled={busy.value || !hasMusic.value} ing={busy.value}
                    onClick={() => chooseIntroMovie()}>
              {hasIntroMovie.value ? t('tools.fancy.introReplace') : t('tools.fancy.introUpload')}
            </Button>
          : <>
              <Button class="w-0 grow" disabled={busy.value || !hasMusic.value} ing={busy.value}
                      onClick={() => chooseIntroMovie('L')}>
                {t('tools.fancy.introLeft')}
              </Button>
              <Button class="w-0 grow" disabled={busy.value || !hasMusic.value} ing={busy.value}
                      onClick={() => chooseIntroMovie('R')}>
                {t('tools.fancy.introRight')}
              </Button>
            </>}
        <Button class="w-0 grow" disabled={busy.value || !hasMusic.value || !hasIntroMovie.value}
                onClick={() => musicId.value && removeIntroMovie(musicId.value)}>
          {t('tools.fancy.introRemove')}
        </Button>
      </div>

      <div class="flex gap-2">
        <Button class="w-0 grow" disabled={busy.value || !hasMusic.value} ing={busy.value}
                onClick={chooseIntroAudio}>
          {introInfo.value?.hasAudio ? t('tools.fancy.introAudioReplace') : t('tools.fancy.introAudioAdd')}
        </Button>
        <Button class="w-0 grow" disabled={busy.value || !hasMusic.value || !introInfo.value?.hasAudio}
                onClick={() => musicId.value && removeIntroAudio(musicId.value)}>
          {t('tools.fancy.introAudioRemove')}
        </Button>
      </div>

      <div>
        <div class="ml-1 text-sm mb-1">{t('tools.fancy.existing')} ({introEntries.value.length})</div>
        <div class="max-h-40 overflow-auto border border-gray-200 rounded p-1">
          {introEntries.value.length === 0
            ? <div class="text-sm op-50 px-2 py-1">{t('tools.fancy.none')}</div>
            : introEntries.value.map(entry => entryRow(entry.id!, songName(entry.id!), [
              entry.hasSingle ? 'L=R' : '',
              entry.hasLeft ? 'L' : '',
              entry.hasRight ? 'R' : '',
            ].filter(Boolean).join(' / '), removeIntroMovie))}
        </div>
      </div>
    </div>;

    const renderCamouflage = () => <div class="flex flex-col gap-3 pt-3">
      <div class="text-sm op-70 ws-normal">{t('tools.fancy.camouflageHint')}</div>
      {!hasMusic.value &&
        <div class="text-sm op-60 px-1">{t('tools.fancy.pickMusicFirst')}</div>}

      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.preview')}</div>
        {camouflage.value.hasJacket
          ? <div class="flex justify-center">
              <img key={jacketPreviewUrl.value} src={jacketPreviewUrl.value} alt=""
                   class="max-h-48 max-w-full rounded object-contain bg-black/5"/>
            </div>
          : <div class="text-sm op-50 px-2 py-4">{t('tools.fancy.noPreview')}</div>}
        {!!camouflage.value.jacketPath &&
          <div class="ml-1 text-xs op-50 select-text break-all">
            {t('tools.fancy.path')}: {camouflage.value.jacketPath}
          </div>}
      </div>

      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.camoName')}</div>
        <TextInput v-model:value={camouflage.value.name} disabled={!hasMusic.value}/>
      </div>
      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.camoArtist')}</div>
        <TextInput v-model:value={camouflage.value.artist} disabled={!hasMusic.value}/>
      </div>
      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.camoNoteDesigner')}</div>
        <TextInput v-model:value={camouflage.value.noteDesigner} disabled={!hasMusic.value}/>
      </div>
      <CheckBox v-model:value={camouflage.value.hideNoteDesigner} class="m-1" disabled={!hasMusic.value}>
        {t('tools.fancy.camoHideNoteDesigner')}
      </CheckBox>
      <div>
        <div class="ml-1 text-sm">{t('tools.fancy.camoUnlockScore')}</div>
        <NumberInput class="w-full" step={0.1} decimal={1} min={0} max={102}
                     v-model:value={camouflage.value.unlockScore} disabled={!hasMusic.value}/>
        <div class="ml-1 text-xs op-60 ws-normal">{t('tools.fancy.camoUnlockScoreHint')}</div>
      </div>

      <div class="flex gap-2">
        <Button class="w-0 grow" disabled={busy.value || !hasMusic.value}
                onClick={chooseCamouflageJacket}>
          {camouflage.value.hasJacket ? t('tools.fancy.camoJacketReplace') : t('tools.fancy.camoJacketUpload')}
        </Button>
        <Button class="w-0 grow" disabled={busy.value || !hasMusic.value || !camouflage.value.name && !camouflage.value.hasJacket}
                onClick={() => musicId.value && removeCamouflage(musicId.value)}>
          {t('tools.fancy.camoRemove')}
        </Button>
      </div>
      <Button disabled={busy.value || !hasMusic.value} ing={busy.value} onClick={saveCamouflage}>
        {t('tools.fancy.save')}
      </Button>

      <div>
        <div class="ml-1 text-sm mb-1">{t('tools.fancy.existing')} ({camouflageEntries.value.length})</div>
        <div class="max-h-40 overflow-auto border border-gray-200 rounded p-1">
          {camouflageEntries.value.length === 0
            ? <div class="text-sm op-50 px-2 py-1">{t('tools.fancy.none')}</div>
            : camouflageEntries.value.map(entry => {
              const realName = songName(entry.id!);
              const camoName = entry.name ?? '';
              // 标题优先显示真实曲名；伪装名相同就不重复显示
              const title = realName || camoName;
              const desc = [
                realName && camoName && camoName !== realName ? camoName : '',
                entry.artist ?? '',
              ].filter(Boolean).join(' / ');
              return entryRow(entry.id!, title, desc, removeCamouflage);
            })}
        </div>
      </div>
    </div>;

    const trigger = () => {
      show.value = true;
    };

    expose({ trigger });

    return () => (
      <Modal width="min(56vw,48em)" title={t('tools.fancy.title')} v-model:show={show.value}>
        {{
          default: () => <div class="flex flex-col gap-3">
            <div>
              <div class="ml-1 text-sm">{t('tools.fancy.music')}</div>
              <div class="flex flex-col gap-2">
                <TextInput v-model:value={musicSearch.value} placeholder={t('tools.fancy.musicPlaceholder')}/>
                <Select
                  v-model:value={selectedMusic.value}
                  options={musicOptions.value}
                  placeholder={t('tools.fancy.musicPick')}
                />
              </div>
            </div>
            <Tabs v-model:value={tab.value}>
              <TabPane name="intro" tab={t('tools.fancy.introTab')}>{renderIntro()}</TabPane>
              <TabPane name="camouflage" tab={t('tools.fancy.camouflageTab')}>{renderCamouflage()}</TabPane>
            </Tabs>
          </div>,
          actions: () => <Button class="w-0 grow" onClick={() => show.value = false}>{t('common.close')}</Button>,
        }}
      </Modal>
    );
  },
});
