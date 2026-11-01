export type CompanyDataProps = {
  name?: string;
  cnpj?: string;
  address?: string;
  email?: string;
  phone?: string;
};

/** Quem vende (Decreto 7.962/2013, art. 2º): só o que foi configurado, sem nada inventado. */
export function CompanyData({ name, cnpj, address, email, phone }: CompanyDataProps) {
  if (!name && !cnpj && !address && !email && !phone) return null;
  return (
    <address className="text-ink-soft space-y-1 text-sm not-italic">
      {name && <p>{name}</p>}
      {cnpj && <p>CNPJ {cnpj}</p>}
      {address && <p>{address}</p>}
      {email && (
        <p>
          <a href={`mailto:${email}`} className="inline-flex min-h-11 items-center underline sm:min-h-0">
            {email}
          </a>
        </p>
      )}
      {phone && <p>{phone}</p>}
    </address>
  );
}
