// Quem recebe e onde (#221): o pedido exige os dois, como a nota fiscal e o fornecedor. Um CPF válido de
// exemplo e um endereço de São Paulo, iguais em todos os pedidos dos testes.
export const DELIVERY = {
  recipient: { name: 'Cliente de Teste', cpf: '52998224725', phone: '11987654321' },
  shipping_address: {
    zip_code: '01310100', street: 'Avenida Paulista', number: '1000', complement: null,
    district: 'Bela Vista', city: 'São Paulo', state: 'SP',
  },
  // o frete (#222): o orders recota no fulfillment, então basta o serviço
  shipping: { service: 'economico' },
};
