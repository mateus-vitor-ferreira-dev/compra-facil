// As portas do front e da API falsa nos testes. Na máquina de desenvolvimento, 3000 e 4010; no runner próprio,
// o CI sorteia portas livres (E2E_FRONT_PORT e E2E_STUB_PORT): a máquina é dividida com outros projetos e com
// o outro runner, e uma porta fixa já em uso derrubava o job (#415).
export const FRONT_PORT = Number(process.env.E2E_FRONT_PORT ?? 3000);
export const STUB_PORT = Number(process.env.E2E_STUB_PORT ?? 4010);
export const FRONT = `http://localhost:${FRONT_PORT}`;
export const STUB = `http://localhost:${STUB_PORT}`;
