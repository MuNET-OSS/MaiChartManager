import { defineComponent, PropType, ref, computed, watch } from 'vue';
import { Modal } from '@munet/ui';
import useAsync from "@/hooks/useAsync";
import api from "@/client/api";
import { ensureBackendUrl } from '@/utils/ensureBackendUrl';
import { useI18n } from 'vue-i18n';

export default defineComponent({
  // props: {
  // },
  setup(props, { emit }) {
    const errors = useAsync(async () => {
      await ensureBackendUrl();
      return api.GetAppStartupErrors();
    })
    const show = ref(false)
    const { t } = useI18n();

    watch(() => errors.data.value, value => {
      if (!value) return
      if (value.data?.length) {
        show.value = true
      }
    })

    return () => <Modal
      width="min(50vw,60em)"
      title={t('startup.error')}
      v-model:show={show.value}
      class="bg-#FCEEEE!"
    >
      <div class="flex flex-col gap-2">
        <div class="flex flex-col gap-1">
          {errors.data.value?.data?.map((error) => {
            return <div>
              <div class="text-0.9em">
                {error}
              </div>
            </div>
          })}
        </div>
        {t('startup.fixPrompt')}
      </div>
    </Modal>;
  },
});
