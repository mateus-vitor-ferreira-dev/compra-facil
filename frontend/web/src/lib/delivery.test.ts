import {
  addressLines,
  checkDelivery,
  EMPTY_DELIVERY,
  formatCpf,
  formatPhone,
  formatZipCode,
  fromOrder,
  isValidCpf,
  toOrderDelivery,
} from "./delivery";

const good = {
  name: "Ana  Souza",
  cpf: "529.982.247-25",
  phone: "(11) 98765-4321",
  zip_code: "01310-100",
  street: "Avenida Paulista",
  number: "1000",
  complement: "",
  district: "Bela Vista",
  city: "São Paulo",
  state: "sp",
};

describe("máscaras", () => {
  it("CPF, CEP e telefone como se escrevem", () => {
    expect(formatCpf("52998224725")).toBe("529.982.247-25");
    expect(formatCpf("5299")).toBe("529.9");
    expect(formatZipCode("01310100")).toBe("01310-100");
    expect(formatPhone("11987654321")).toBe("(11) 98765-4321");
    expect(formatPhone("1134567890")).toBe("(11) 3456-7890");
    expect(formatPhone("1")).toBe("(1");
  });
});

describe("checkDelivery", () => {
  it("dados certos passam, e o pedido leva só os dígitos", () => {
    expect(checkDelivery(good)).toEqual({});
    expect(toOrderDelivery(good)).toEqual({
      recipient: { name: "Ana Souza", cpf: "52998224725", phone: "11987654321" },
      shipping_address: {
        zip_code: "01310100",
        street: "Avenida Paulista",
        number: "1000",
        complement: null,
        district: "Bela Vista",
        city: "São Paulo",
        state: "SP",
      },
    });
  });

  it("cada erro no seu campo", () => {
    const errors = checkDelivery({ ...EMPTY_DELIVERY, name: "Ana", cpf: "529.982.247-24" });
    expect(errors.name).toContain("sobrenome");
    expect(errors.cpf).toContain("não existe");
    expect(Object.keys(errors).sort()).toEqual(
      ["city", "cpf", "district", "name", "number", "phone", "state", "street", "zip_code"].sort(),
    );
    expect(checkDelivery({ ...good, cpf: "529" }).cpf).toContain("11 dígitos");
  });

  it("CPF pelos dígitos verificadores", () => {
    expect(isValidCpf("52998224725")).toBe(true);
    expect(isValidCpf("11111111111")).toBe(false);
  });
});

it("o pedido anterior preenche o formulário, e o endereço sai em linhas", () => {
  const order = {
    recipient: { name: "Ana Souza", cpf: "52998224725", phone: "11987654321" },
    shipping_address: {
      zip_code: "01310100",
      street: "Avenida Paulista",
      number: "1000",
      complement: "Apto 12",
      district: "Bela Vista",
      city: "São Paulo",
      state: "SP",
    },
  };
  expect(fromOrder(order)).toMatchObject({
    cpf: "529.982.247-25",
    zip_code: "01310-100",
    complement: "Apto 12",
  });
  expect(fromOrder({ recipient: null, shipping_address: null })).toBeUndefined();
  expect(addressLines(order.shipping_address)).toEqual([
    "Avenida Paulista, 1000, Apto 12",
    "Bela Vista, São Paulo - SP",
    "CEP 01310-100",
  ]);
});
