import { MessageLevel } from "@/client/apiGen";
import { parseStructuredError } from "@/utils/structuredError";
import type { ImportChartMessageEx } from "./types";

export const createVideoConvertWarning = (
  errorValue: unknown,
  musicName: string,
  fallbackMessage: string,
  unknownMessage: string,
): ImportChartMessageEx => {
  const error = parseStructuredError(errorValue);
  return {
    level: MessageLevel.Warning,
    message: `${fallbackMessage}: ${error.message || unknownMessage}`,
    detail: error.detail,
    name: musicName,
  };
};

export const createImportFatal = (errorValue: unknown, musicName: string): ImportChartMessageEx => {
  const error = parseStructuredError(errorValue);
  return {
    level: MessageLevel.Fatal,
    message: error.message,
    detail: error.detail,
    name: musicName,
  };
};

export const getCaptureTarget = (errorValue: unknown): unknown => {
  if (typeof errorValue !== "object" || errorValue === null) return errorValue;
  if (!("error" in errorValue)) return errorValue;
  return errorValue.error;
};

export const isAbortError = (errorValue: any): boolean => errorValue?.name === "AbortError"
