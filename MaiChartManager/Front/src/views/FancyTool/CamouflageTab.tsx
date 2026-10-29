import { computed, defineComponent, ref, watch } from 'vue';
import { Button, CheckBox, NumberInput, TextInput, addToast, showTransactionalDialog } from '@munet/ui';
import { useI18n } from 'vue-i18n';
import api, { getUrl } from '@/client/api';
import type { CamouflageInfoDto } from '@/client/apiGen';
import { globalCapture } from '@/store/refs';
import { pickFile } from '@/utils/pickFile';
import { fileToken, modFlags, refreshEntries, selectedId } from './refs';
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
    const loading = ref(false);
    const camouflage = ref(emptyCamouflage());
    // 表单里现在装的是哪首曲目的数据：切曲目后、数据回来之前不能拿这份表单去保存
    const loadedId = ref(0);

    // 只在切换曲目时重置就绪状态：保存成功后重新读一遍不算「换了一首」，
    // 这里若也清零，一次读失败就会把表单锁死到下次切曲目
    const load = async () => {
      const id = selectedId.value;
      if (!id) {
        camouflage.value = emptyCamouflage();
        return;
      }

      loading.value = true;
      try {
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
        loadedId.value = id;
      } finally {
        if (id === selectedId.value) loading.value = false;
      }
    };

    // 表单内容确实属于当前曲目时才允许改动，否则切换曲目的一瞬间点保存会把上一首的内容写进这一首
    const ready = computed(() => !!selectedId.value && loadedId.value === selectedId.value);
    const locked = computed(() => busy.value || loading.value || !ready.value);

    watch(selectedId, () => {
      // 先清空，避免切走之后旧内容还留在表单里，也顺带把表单锁到读完为止
      camouflage.value = emptyCamouflage();
      loadedId.value = 0;
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
      if (!id || !ready.value) return;

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
        // 刷新失败只是没读到最新数据，文件其实已经写进去了，别报成「写入失败」
        await Promise.all([
          load().catch(e => globalCapture(e, t('tools.fancy.errorLoad'))),
          refreshEntries(),
        ]);
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorWrite'));
      } finally {
        busy.value = false;
      }
    };

    const uploadJacket = async (file: File) => {
      const id = selectedId.value;
      if (!id || !ready.value) return;

      busy.value = true;
      try {
        const result = (await api.SetCamouflageJacket({ id, file })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }

        addToast({ message: t('tools.fancy.saved'), type: 'success' });
        // 不要用 load：它会把服务端数据写回表单，冲掉还没保存的填写内容
        camouflage.value.hasJacket = true;
        await Promise.all([
          refreshJacket().catch(e => globalCapture(e, t('tools.fancy.errorLoad'))),
          refreshEntries(),
        ]);
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
      if (!id || !ready.value) return;
      return confirmThen(t('tools.fancy.camoJacketRemoveConfirm', { id }), async () => {
        const result = (await api.DeleteCamouflageJacket({ id })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }
        // 先就地改掉状态：万一接下来的读取失败，界面也不能还显示着一张已经删掉的封面
        camouflage.value.hasJacket = false;
        camouflage.value.jacketPath = '';
        await Promise.all([
          refreshJacket().catch(e => globalCapture(e, t('tools.fancy.errorLoad'))),
          refreshEntries(),
        ]);
      });
    };

    const removeAll = () => {
      const id = selectedId.value;
      if (!id || !ready.value) return;
      return confirmThen(t('tools.fancy.camoRemoveConfirm', { id }), async () => {
        const result = (await api.DeleteCamouflage({ id })).data;
        if (!result.success) {
          showFancyError(t, result.error);
          return;
        }
        // 删除成功立刻清空表单：万一接下来的读取失败，表单上留着的就是刚删掉的内容，
        // 用户一按保存又把伪装写回去了
        camouflage.value = emptyCamouflage();
        fileToken.value = Date.now();
        await Promise.all([
          load().catch(e => globalCapture(e, t('tools.fancy.errorLoad'))),
          refreshEntries(),
        ]);
      });
    };

    const hasAny = computed(() => !!camouflage.value.name || camouflage.value.hasJacket);

    const renderWarning = () => modFlags.value.camouflage === false
      ? <div class="flex items-start gap-2 rounded-md border-1 border-solid border-orange-500/30 bg-orange-500/10 px-3 py-2 text-sm text-orange-600">
        <span class="i-mdi-alert mt-0.5 shrink-0"/>
        <span class="ws-normal">{t('tools.fancy.camoModDisabled')}</span>
      </div>
      : null;

    return () => <div class="flex flex-col gap-3 py">
      {renderWarning()}
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
            <Button class="px-2! py-1! text-xs" disabled={locked.value} ing={busy.value} onClick={chooseJacket}>
              {camouflage.value.hasJacket ? t('tools.fancy.camoJacketReplace') : t('tools.fancy.camoJacketUpload')}
            </Button>
            {camouflage.value.hasJacket &&
              <Button class="px-2! py-1! text-xs" variant="secondary" disabled={locked.value} onClick={removeJacket}>
                {t('tools.fancy.camoJacketRemove')}
              </Button>}
          </div>
        </div>

        <div class="grow-1 flex flex-col gap-3 min-w-0">
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoName')}</div>
            <TextInput v-model:value={camouflage.value.name} disabled={locked.value}/>
          </div>
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoArtist')}</div>
            <TextInput v-model:value={camouflage.value.artist} disabled={locked.value}/>
          </div>
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoNoteDesigner')}</div>
            <TextInput v-model:value={camouflage.value.noteDesigner} disabled={locked.value}/>
          </div>
          <CheckBox v-model:value={camouflage.value.hideNoteDesigner} class="m-1" disabled={locked.value}>
            {t('tools.fancy.camoHideNoteDesigner')}
          </CheckBox>
          <div>
            <div class="ml-1 text-sm">{t('tools.fancy.camoUnlockScore')}</div>
            <NumberInput class="w-full" step={0.1} decimal={1} min={0} max={102}
                         v-model:value={camouflage.value.unlockScore} disabled={locked.value}/>
            <div class="ml-1 text-xs op-60 ws-normal">{t('tools.fancy.camoUnlockScoreHint')}</div>
          </div>
        </div>
      </div>

      <div class="flex gap-2">
        <Button class="w-0 grow" disabled={locked.value || textMissing.value} ing={busy.value} onClick={save}>
          {t('tools.fancy.save')}
        </Button>
        <Button class="w-0 grow" variant="secondary" disabled={locked.value || !hasAny.value} onClick={removeAll}>
          {t('tools.fancy.camoRemove')}
        </Button>
      </div>
    </div>;
  },
});
