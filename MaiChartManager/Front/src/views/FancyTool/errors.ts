import { addToast } from '@munet/ui';

type Translate = (key: string, ...args: any[]) => string;

// 后端返回的错误码 → 文案
const errorKeys: Record<string, string> = {
  'invalid-id': 'tools.fancy.errorInvalidId',
  'invalid-side': 'tools.fancy.errorInvalidSide',
  'invalid-text': 'tools.fancy.errorInvalidText',
  'invalid-image': 'tools.fancy.errorInvalidImage',
  'invalid-audio': 'tools.fancy.errorInvalidAudio',
  'game-path-missing': 'tools.fancy.errorGamePath',
  'convert-failed': 'tools.fancy.errorConvert',
  'write-failed': 'tools.fancy.errorWrite',
  'file-locked': 'tools.fancy.errorFileLocked',
  'delete-failed': 'tools.fancy.errorFileLocked',
};

/** SSE 的 Error 事件把错误码当消息传过来，用它区分「后端返回的错误码」和「网络/其它异常」 */
export const isKnownErrorCode = (code?: string | null): boolean => !!code && code in errorKeys;

export const showFancyError = (t: Translate, code?: string | null) => {
  const key = code ? errorKeys[code] : undefined;
  addToast({
    message: key ? t(key) : `${t('tools.fancy.errorUnknown')}${code ? ` (${code})` : ''}`,
    type: 'error',
  });
};
