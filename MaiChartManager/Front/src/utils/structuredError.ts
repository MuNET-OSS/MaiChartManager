export type StructuredError = {
  readonly message: string,
  readonly detail?: string,
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null;

export const parseStructuredError = (value: unknown): StructuredError => {
  if (value instanceof Error) {
    const parsed = parseStructuredErrorText(value.message);
    return parsed ?? { message: value.message };
  }

  if (isRecord(value)) {
    const errorValue = value["error"];
    if (typeof errorValue === 'string') {
      const parsed = parseStructuredErrorText(errorValue);
      if (parsed) return parsed;
    }

    const messageValue = value["message"];
    const detailValue = value["detail"];
    if (typeof messageValue === 'string') {
      return {
        message: messageValue,
        detail: typeof detailValue === 'string' ? detailValue : undefined,
      };
    }
  }

  return { message: String(value) };
};

export const parseStructuredErrorText = (value: string): StructuredError | undefined => {
  try {
    const parsed: unknown = JSON.parse(value);
    if (!isRecord(parsed)) return undefined;

    const message = parsed["message"];
    if (typeof message !== 'string') return undefined;

    const detail = parsed["detail"];
    return {
      message,
      detail: typeof detail === 'string' ? detail : undefined,
    };
  } catch {
    return undefined;
  }
};
