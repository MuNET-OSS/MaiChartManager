import { defineComponent } from "vue";
import { Button, Modal, Popover, Qrcode } from "@munet/ui";
import { showNeedPurchaseDialog } from "@/store/refs";
import StorePurchaseButton from "@/components/StorePurchaseButton";
import AfdianIcon from "@/icons/afdian.svg";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup(props) {
    const { t } = useI18n();

    return () => <Modal
      width="min(50vw,60em)"
      title={t('purchase.title')}
      v-model:show={showNeedPurchaseDialog.value}
    >
      <div class="flex flex-col gap-2">
        <div>
          {t('purchase.description')}
        </div>
        <div>
          {t('purchase.featuresTitle')}
        </div>
        <ul>
          <li>{t('purchase.feature.convertPv')}</li>
          <li>{t('purchase.feature.audioPreview')}</li>
          <li>{t('purchase.feature.changeId')}</li>
          <li>{t('purchase.feature.batchExport')}</li>
          <li>{t('purchase.feature.batchMaidata')}</li>
        </ul>
        <div>{t('purchase.moreComingSoon')}</div>
        <div class="flex gap-2 items-center">
          {t('purchase.supportDev')}
          <StorePurchaseButton/>
          <Button variant="secondary" onClick={() => window.open("https://pay.ldxp.cn/item/lymueu")}>
            {t('purchase.ldxp')}
          </Button>
          <Button variant="secondary" onClick={() => window.open("https://qm.qq.com/q/U3gT7CDuy6")}>
            {t('purchase.qqGroup')}
          </Button>
        </div>
      </div>
    </Modal>;
  }
})
