const ILLEGAL_FS_CHAR_MAP: Record<string, string> = {
  "/": "\uFF0F",
  "\\": "\uFF3C",
  ":": "\uFF1A",
  "*": "\uFF0A",
  "?": "\uFF1F",
  "\"": "\uFF02",
  "<": "\uFF1C",
  ">": "\uFF1E",
  "|": "\uFF5C"
};

const sanitizeRawSegment = (segment: string) => {
  return segment
    .normalize("NFC")
    .replace(/[\/\\:*?"<>|]/g, (char) => ILLEGAL_FS_CHAR_MAP[char] || char)
    .replace(/[\u0000-\u001F]/g, "")
    .replace(/[.\s]+$/g, "");
};

export const sanitizeFsSegment = (segment: string, fallback = "untitled") => {
  const sanitized = sanitizeRawSegment(segment || "");
  if (sanitized.length > 0) {
    return sanitized;
  }

  const fallbackSanitized = sanitizeRawSegment(fallback || "untitled");
  return fallbackSanitized.length > 0 ? fallbackSanitized : "untitled";
};
