// Sobe a API falsa (e2e/stub-api.mjs) e o build de produção apontando para ela: o Lighthouse CI mede a
// página como ela sai do servidor, sem a stack inteira.
import { spawn } from "node:child_process";

// as portas vêm de E2E_FRONT_PORT e E2E_STUB_PORT, que o CI sorteia no runner próprio (#415)
const frontPort = process.env.E2E_FRONT_PORT ?? "3000";
const stubPort = process.env.E2E_STUB_PORT ?? "4010";
const stub = spawn(process.execPath, ["e2e/stub-api.mjs"], {
  stdio: "inherit",
  env: { ...process.env, STUB_PORT: stubPort },
});
const app = spawn("npm", ["run", "start"], {
  stdio: "inherit",
  env: {
    ...process.env,
    PORT: frontPort,
    API_URL: `http://localhost:${stubPort}`,
    SITE_URL: `http://localhost:${frontPort}`,
  },
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
