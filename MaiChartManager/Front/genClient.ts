import { generateApi } from "swagger-typescript-api";
import path from "node:path";
import { readFile, writeFile } from "node:fs/promises";

/* NOTE: all fields are optional expect one of `input`, `url`, `spec` */
const generate = (fileName: string, url: string) => generateApi({
  fileName,
  output: path.resolve("src/client"),
  url,
  templates: path.resolve("./api-templates"),
  httpClientType: "fetch", // or "fetch"
  defaultResponseType: "void",
  enumNamesAsValues: true,
})

// swagger-typescript-api 的 FILE_PREFIX 里硬编码了 // @ts-nocheck（没有开关可以关掉），
// 会让生成出来的几千行客户端整体绕过类型检查，所以生成后剥掉。
// 找不到就报错退出：模板或库改了要立刻发现，不能静默失效。
const stripTsNoCheck = async (fileName: string) => {
  const target = path.resolve("src/client", fileName);
  const source = await readFile(target, "utf8");
  const stripped = source.replace(/^\/\/ @ts-nocheck\r?\n/m, "");
  if (stripped === source) {
    throw new Error(`${fileName} 里没找到 // @ts-nocheck，swagger-typescript-api 的 FILE_PREFIX 可能变了`);
  }
  await writeFile(target, stripped);
};

await generate("apiGen.ts", "http://localhost:5181/swagger/v1/swagger.json");
await generate("aquaMaiVersionConfigApiGen.ts", "https://aquamai-version-config.init.ink/openapi.json");
await Promise.all([
  stripTsNoCheck("apiGen.ts"),
  stripTsNoCheck("aquaMaiVersionConfigApiGen.ts"),
]);
