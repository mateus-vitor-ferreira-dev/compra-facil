"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { RECOVERY_CODES_COOKIE, RECOVERY_CODES_PATH, readRecoveryCodes } from "@/lib/auth/recovery-codes";
import { safeReturnTo } from "@/lib/auth/return-to";

/** Os códigos foram guardados: some o cookie deles e a pessoa segue para onde ia (o painel). */
export async function continueAfterCodes(): Promise<void> {
  const store = await cookies();
  const saved = readRecoveryCodes(store.get(RECOVERY_CODES_COOKIE)?.value);
  store.delete({ name: RECOVERY_CODES_COOKIE, path: RECOVERY_CODES_PATH });
  redirect(safeReturnTo(saved?.back, "/admin"));
}
