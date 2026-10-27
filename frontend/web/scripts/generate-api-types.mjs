// Tipos TypeScript dos contratos REST (contracts/openapi/*.v1.yaml), com openapi-typescript.
// Os contratos são a fonte da verdade: mudou um, rode `npm run api:types`. Com --check (CI), falha
// se os tipos versionados estiverem desatualizados.
import { readFile, readdir, writeFile, mkdir } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";
import openapiTS, { astToString } from "openapi-typescript";

const root = fileURLToPath(new URL("..", import.meta.url));
const contracts = path.join(root, "..", "..", "contracts", "openapi");
const out = path.join(root, "src", "lib", "api", "schema");
const check = process.argv.includes("--check");

const specs = (await readdir(contracts)).filter((f) => f.endsWith(".v1.yaml")).sort();
await mkdir(out, { recursive: true });
let stale = [];
for (const spec of specs) {
  const service = spec.replace(".v1.yaml", "");
  const ast = await openapiTS(pathToFileURL(path.join(contracts, spec)));
  const code =
    `// Gerado de contracts/openapi/${spec} por scripts/generate-api-types.mjs. Não edite à mão.\n` +
    astToString(ast);
  const file = path.join(out, `${service}.ts`);
  const current = await readFile(file, "utf8").catch(() => null);
  if (current === code) continue;
  if (check) stale.push(path.relative(root, file));
  else await writeFile(file, code);
}
if (stale.length > 0) {
  console.error(`tipos desatualizados (rode npm run api:types): ${stale.join(", ")}`);
  process.exit(1);
}
console.log(
  check
    ? `tipos em dia (${specs.length} contratos)`
    : `tipos gerados de ${specs.length} contratos em ${path.relative(root, out)}`,
);
