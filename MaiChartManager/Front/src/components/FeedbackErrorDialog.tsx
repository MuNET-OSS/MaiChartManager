import { computed, defineComponent, ref, watch } from "vue";
import { Modal, addToast } from "@munet/ui";
import { error, errorContext, errorId } from "@/store/refs";
import { captureFeedback } from "@sentry/vue";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup(props) {
    const { t } = useI18n();

    const message = computed(() => {
      let msg: string;
      if (!error.value) return "";
      if (error.value.error) {
        msg = error.value.error.message || error.value.error.detail || error.value.error.toString();
      } else if (error.value.message) {
        msg = error.value.message;
      } else {
        msg = error.value.toString();
      }
      msg = msg.split('\n')[0]
      return msg
    })

    const userInput = ref("");

    watch(() => error.value, (v, old) => {
      // 防止输入到一半出另一个错误清空输入
      if (old) return;
      userInput.value = "";
    });

    const report = () => {
      captureFeedback({
        associatedEventId: errorId.value,
        message: userInput.value || t('feedback.none'),
      })
      addToast({message: t('feedback.thanks'), type: 'success'});
      error.value = null;
    }

    return () => <Modal
      width="min(50vw,60em)"
      title={t('error.title')}
      show={!!error.value}
      onUpdateShow={() => error.value = null}
    >
      <div class="flex flex-col gap-3">
        <div class="text-lg">{errorContext.value}</div>
        {message.value}
      </div>
    </Modal>;
  }
})
