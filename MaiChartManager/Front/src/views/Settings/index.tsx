import { defineComponent } from "vue";
import AppearanceSection from "./AppearanceSection";
import GameDirectorySection from "./GameDirectorySection";
import ImportOptionsSection from "./ImportOptionsSection";
import AquaMaiSection from "./AquaMaiSection";

export default defineComponent({
  setup() {
    return () => (
      <div class="p-xy h-full of-y-auto cst">
        <AppearanceSection />
        <GameDirectorySection />
        <ImportOptionsSection />
        <AquaMaiSection />
      </div>
    );
  },
});
