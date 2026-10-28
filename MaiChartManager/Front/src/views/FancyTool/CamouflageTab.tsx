import { computed, defineComponent, ref, watch } from 'vue';
import { Button, CheckBox, NumberInput, TextInput, addToast, showTransactionalDialog } from '@munet/ui';
import { useI18n } from 'vue-i18n';
import api, { getUrl } from '@/client/api';
import type { CamouflageInfoDto } from '@/client/apiGen';
import { globalCapture } from '@/store/refs';
import { pickFile } from '@/utils/pickFile';
import { fileToken, refreshEntries, selectedId } from './refs';
import { showFancyError } from './errors';

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
  setup() {
    const { t } = useI18n();

    const busy = ref(false);
    const camouflage = ref(emptyCamouflage());

    const load = async () => {
      const id = selectedId.value;
      if (!id) {
        camouflage.value = emptyCamouflage();
        return;
      }
      const data: CamouflageInfoDto = (await api.GetCamouflage({ id })).data;
      // 切曲目后旧请求可能才回来，丢弃过期响应
      if (id !== selectedId.value) return;

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

    watch(selectedId, () => {
      if (!selectedId.value) return;
      load().catch(e => globalCapture(e, t('tools.fancy.errorLoad')));
    }, { immediate: true });

    // 游戏侧曲名和曲师都是必填的（缺了会显示 ???），所以空着就不让保存
    const textMissing = computed(() => !camouflage.value.name.trim() || !camouflage.value.artist.trim());

    const jacketPreviewUrl = computed(() => selectedId.value && camouflage.value.hasJacket && !busy.value
      ? getUrl(`GetCamouflageJacketApi?id=${selectedId.value}&t=${fileToken.value}`)
      : '');

    // 只刷新封面状态，保留用户还在编辑、尚未保存的表单内容
    const refreshJacket = async () => {
      const id = selectedId.value;
      if (!id) return;
      const data: CamouflageInfoDto = (await api.GetCamouflage({ id })).data;
      if (id !== selectedId.value) return;

      camouflage.value.hasJacket = !!data.hasJacket;
      camouflage.value.jacketPath = data.jacketPath ?? '';
      fileToken.value = Date.now();
    };

    const save = async () => {
      const id = selectedId.value;
      if (!id) return;

      busy.value = true;
      try {
        const result = (await api.SetCamouflage({
          id,
          name: camouflage.value.name,
          artist: camouflage.value.artist,
          noteDesigner: camouflage.value.noteDesigner,
          hideNoteDesigner: camouflage.value.hideNoteDesigner,
          unlockScore: camouflage.value.unlockScore,
        })).data;

        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }

        addToast({ message: t('tools.fancy.saved'), type: 'success' });
        await Promise.all([load(), refreshEntries()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const uploadJacket = async (file: File) => {
      const id = selectedId.value;
      if (!id) return;

      busy.value = true;
      try {
        const result = (await api.SetCamouflageJacket({ id, file })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }

        addToast({ message: t('tools.fancy.saved'), type: 'success' });
        // 不要用 load：它会把服务端数据写回表单，冲掉还没保存的填写内容
        await Promise.all([refreshJacket(), refreshEntries()]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const chooseJacket = async () => {
      const file = await pickFile('image/png,image/jpeg');
      if (file) await uploadJacket(file);
    };

    const confirmThen = async (message: string, action: () => Promise<void>) => {
      const confirmed = await showTransactionalDialog(
        t('tools.fancy.camouflageTab'),
        message,
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) return;

      busy.value = true;
      try {
        await action();
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const removeJacket = () => {
      const id = selectedId.value;
      if (!id) return;
      return confirmThen(t('tools.fancy.camoJacketRemoveConfirm', { id }), async () => {
        const result = (await api.DeleteCamouflageJacket({ id })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }
        await Promise.all([refreshJacket(), refreshEntries()]);
      });
    };

    const removeAll = () => {
      const id = selectedId.value;
      if (!id) return;
      return confirmThen(t('tools.fancy.camoRemoveConfirm', { id }), async () => {
        const result = (await api.DeleteCamouflage({ id })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }
        await Promise.all([load(), refreshEntries()]);
        fileToken.value = Date.now();
      });
    };

    const hasAny = computed(() => !!camouflage.value.name || camouflage.value.hasJacket);

    return () => <div class="flex flex-col gap-3 py">
      <div class="text-sm op-70 ws-normal">{t('tools.fancy.camouflageHint')}</div>

      <div class="flex gap-4">
        <div class="flex flex-col items-center gap-2 shrink-0">
          {camouflage.value.hasJacket
            ? <img key={jacketPreviewUrl.value} src={jacketPreviewUrl.value} alt=""
                   class="h-40 w-40 rounded object-contain bg-black/5"/>
            : <div class="h-40 w-40 rounded bg-black/5 flex items-center justify-center text-sm op-50">
                {t('tools.fancy.noPreview')}
              </div>}
          <div class="flex gap-2">
            <Button class="px-2! py-1! text-xs" disabled={busy.value} ing={busy.value} onClick={chooseJacket}>
              {camouflage.value.hasJacket ? t('tools.fancy.camoJacketReplace') : t('tools.fancy.camoJacketUpload')}
            </Button>
            {camouflage.value.hasJacket &&
              <Button class="px-2! py-1! text-xs" variant="secondary" disabled={busy.value} onClick={removeJacket}>
                {t('tools.fancy.camoJacketRemove')}
              </Button>}
          </div>
          {!!camouflage.value.jacketPath &&
            <div class="text-xs op-50 select-text break-all max-w-40 text-center">{camouflage.value.jacketPath}</div>}
        </div>

        <div class="grow-1 flex flex-col gap-3 min-w-0">
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoName')}</div>
            <TextInput v-model:value={camouflage.value.name} disabled={busy.value}/>
          </div>
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoArtist')}</div>
            <TextInput v-model:value={camouflage.value.artist} disabled={busy.value}/>
          </div>
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoNoteDesigner')}</div>
            <TextInput v-model:value={camouflage.value.noteDesigner} disabled={busy.value}/>
          </div>
          <CheckBox v-model:value={camouflage.value.hideNoteDesigner} class="m-1" disabled={busy.value}>
            {t('tools.fancy.camoHideNoteDesigner')}
          </CheckBox>
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoUnlockScore')}</div>
            <NumberInput class="w-full" step={0.1} decimal={1} min={0} max={102}
                         v-model:value={camouflage.value.unlockScore} disabled={busy.value}/>
            <div class="ml-1 text-xs op-60 ws-normal">{t('tools.fancy.camoUnlockScoreHint')}</div>
          </div>
        </div>
      </div>

      <div class="text-xs op-60 ws-normal">{t('tools.fancy.camoTextHint')}</div>

      <div class="flex gap-2">
        <Button class="w-0 grow" disabled={busy.value || textMissing.value} ing={busy.value} onClick={save}>
          {t('tools.fancy.save')}
        </Button>
        <Button class="w-0 grow" variant="secondary" disabled={busy.value || !hasAny.value} onClick={removeAll}>
          {t('tools.fancy.camoRemove')}
        </Button>
      </div>
    </div>;
  },
});
