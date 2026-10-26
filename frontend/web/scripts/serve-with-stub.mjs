// Sobe a API falsa (e2e/stub-api.mjs) e o build de produção apontando para ela: o Lighthouse CI mede a
// página como ela sai do servidor, sem a stack inteira.
import { spawn } from "node:child_process";

const stub = spawn(process.execPath, ["e2e/stub-api.mjs"], { stdio: "inherit" });
const app = spawn("npm", ["run", "start"], {
  stdio: "inherit",
  env: { ...process.env, API_URL: "http://localhost:4010" },
});
const stop = () => {
  stub.kill();
  app.kill();
};
process.on("SIGINT", stop);
process.on("SIGTERM", stop);
app.on("exit", (code) => {
  stub.kill();
  process.exit(code ?? 0);
});
