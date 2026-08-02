import api from '@/client/api';
import { ResourceJunctionOverview, ResourceJunctionStatus } from '@/client/apiGen';
import { Button, Modal, addToast, showTransactionalDialog } from '@munet/ui';
import { computed, defineComponent, ref } from 'vue';
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup(_, { expose }) {
    const { t } = useI18n();
    const show = ref(false);
    const loading = ref(false);
    const overview = ref<ResourceJunctionOverview>();
    const items = computed(() => overview.value?.items ?? []);

    const canCreate = computed(() => items.value.some(item => (
      item.status === ResourceJunctionStatus.Ready || item.status === ResourceJunctionStatus.Removed
    )));
    const canRemove = computed(() => items.value.some(item => (
      item.status === ResourceJunctionStatus.AlreadyLinked || item.status === ResourceJunctionStatus.Created
    )));

    const request = async (action: 'auto' | 'status' | 'manual' | 'manualTarget' | 'create' | 'remove') => {
      loading.value = true;
      try {
        const writeParams = { headers: { 'X-MCM-Local-Action': 'resource-junction' } };
        const response = action === 'auto'
          ? await api.AutoSelectResourceJunctionSource(writeParams)
          : action === 'status'
            ? await api.GetResourceJunctionStatus()
            : action === 'manual'
              ? await api.SelectResourceJunctionSource(writeParams)
              : action === 'manualTarget'
                ? await api.SelectResourceJunctionTarget(writeParams)
              : action === 'create'
                ? await api.CreateResourceJunctions(writeParams)
                : await api.RemoveResourceJunctions(writeParams);
        overview.value = response.data;
      } catch (error) {
        console.error(error);
        addToast({ message: t('tools.resourceJunction.requestFailed'), type: 'error' });
      } finally {
        loading.value = false;
      }
    };

    const run = async (action: 'create' | 'remove') => {
      const removing = action === 'remove';
      const confirmed = await showTransactionalDialog(
        t('common.confirm'),
        t(removing ? 'tools.resourceJunction.removeConfirm' : 'tools.resourceJunction.createConfirm'),
        [
          { text: t('common.confirm'), action: true },
          { text: t('common.cancel'), action: false },
        ],
      );
      if (!confirmed) return;
      await request(action);
    };

    const trigger = () => {
      show.value = true;
      overview.value = undefined;
      request('auto');
    };
    expose({ trigger });

    const statusClass = (status?: ResourceJunctionStatus) => {
      if (
        status === ResourceJunctionStatus.Created
        || status === ResourceJunctionStatus.AlreadyLinked
        || status === ResourceJunctionStatus.Removed
      ) return 'text-green-700';
      if (status === ResourceJunctionStatus.Ready) return 'text-blue-700';
      return 'text-red-700';
    };

    return () => (
      <Modal
        width="min(92vw,52em)"
        title={t('tools.resourceJunction.title')}
        v-model:show={show.value}
      >
        <div class="flex flex-col gap-4">
          <div class="grid gap-3 text-sm">
            <div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1">
              <div class="min-w-0">
                <div class="flex items-center gap-2">
                  <div class="font-medium">{t('tools.resourceJunction.source')}</div>
                  {overview.value?.selectionMode && (
                    <span class="op-60">
                      {t(`tools.resourceJunction.selection.${overview.value.selectionMode}`)}
                    </span>
                  )}
                </div>
                <div class="break-all op-65">
                  {overview.value?.sourceRoot ?? t('tools.resourceJunction.noSource')}
                </div>
              </div>
              <Button disabled={loading.value} onClick={() => request('manual')}>
                <span class="i-mdi-folder-open-outline text-5" />
                {t('tools.resourceJunction.selectSource')}
              </Button>
            </div>
            <div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1">
              <div class="min-w-0">
                <div class="font-medium">{t('tools.resourceJunction.target')}</div>
                <div class="break-all op-65">{overview.value?.targetRoot}</div>
              </div>
              <Button disabled={loading.value} onClick={() => request('manualTarget')}>
                <span class="i-mdi-folder-open-outline text-5" />
                {t('tools.resourceJunction.selectTarget')}
              </Button>
            </div>
            {!!overview.value?.fileCounts?.length && (
              <div>
                <div class="font-medium">{t('tools.resourceJunction.fileCounts')}</div>
                <div class="flex flex-wrap gap-x-4 gap-y-1 op-65">
                  {overview.value.fileCounts.map(item => (
                    <span key={item.name}>{item.name}: {item.fileCount}</span>
                  ))}
                  <span>{t('tools.resourceJunction.total')}: {overview.value.totalFileCount}</span>
                </div>
              </div>
            )}
            {overview.value?.detail && <div class="text-red-700">{overview.value.detail}</div>}
          </div>

          <div class="border border-solid border-gray-200 rounded-md overflow-hidden">
            {items.value.map((item, index) => (
              <div
                key={item.name}
                class={[
                  'grid grid-cols-[minmax(0,1fr)_auto] gap-3 px-4 py-3 items-center',
                  index > 0 && 'border-t border-t-solid border-t-gray-200',
                ]}
              >
                <div class="min-w-0">
                  <div class="font-medium break-all">{item.name}</div>
                  {item.detail && <div class="text-xs op-60 break-all mt-1">{item.detail}</div>}
                </div>
                <div class={['text-sm font-medium whitespace-nowrap', statusClass(item.status)]}>
                  {t(`tools.resourceJunction.status.${item.status}`)}
                </div>
              </div>
            ))}
            {!items.value.length && (
              <div class="px-4 py-6 text-center op-60">{t('tools.resourceJunction.loading')}</div>
            )}
          </div>

          <div class="flex flex-wrap justify-end gap-2">
            <Button onClick={() => request('status')} ing={loading.value}>
              <span class="i-mdi-refresh text-5" />
              {t('tools.resourceJunction.refresh')}
            </Button>
            <Button
              danger
              variant="secondary"
              disabled={!canRemove.value || loading.value}
              onClick={() => run('remove')}
            >
              <span class="i-mdi-link-off text-5" />
              {t('tools.resourceJunction.remove')}
            </Button>
            <Button
              variant="primary"
              disabled={!canCreate.value || loading.value}
              onClick={() => run('create')}
            >
              <span class="i-mdi-link-variant-plus text-5" />
              {t('tools.resourceJunction.create')}
            </Button>
          </div>
        </div>
      </Modal>
    );
  },
});
