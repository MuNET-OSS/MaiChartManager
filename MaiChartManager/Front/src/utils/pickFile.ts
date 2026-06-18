// 通用单文件选择：用 <input type=file>，WebKitGTK / Chromium / 远程浏览器都支持，
// 替代 WebKitGTK 不支持的 window.showOpenFilePicker。
export function pickFile(accept?: string): Promise<File | null> {
  return new Promise(resolve => {
    const input = document.createElement('input');
    input.type = 'file';
    if (accept) input.accept = accept;
    input.style.display = 'none';
    document.body.appendChild(input);
    let settled = false;
    input.addEventListener('change', () => { settled = true; resolve(input.files?.[0] ?? null); input.remove(); });
    // 取消时多数内核不触发 change；用 window focus 兜底判定取消
    window.addEventListener('focus', () => setTimeout(() => { if (!settled) { resolve(null); input.remove(); } }, 500), { once: true });
    input.click();
  });
}
