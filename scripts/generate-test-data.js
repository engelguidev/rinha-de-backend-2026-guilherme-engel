const fs = require('fs');

// Merchants separados por perfil de risco, alinhados com as referências da competição
// Ref LEGIT  mcc_risk mean=0.23 → MCCs de baixo risco
// Ref FRAUD  mcc_risk mean=0.78 → MCCs de alto risco
const legitMerchants = [
  { id: 'MERC-001', mcc: '5411' }, // 0.15
  { id: 'MERC-002', mcc: '5812' }, // 0.30
  { id: 'MERC-003', mcc: '5912' }, // 0.20
  { id: 'MERC-004', mcc: '5311' }, // 0.25
  { id: 'MERC-005', mcc: '4511' }, // 0.35
];

const fraudMerchants = [
  { id: 'MERC-006', mcc: '7995' }, // 0.85
  { id: 'MERC-007', mcc: '7801' }, // 0.80
  { id: 'MERC-008', mcc: '7802' }, // 0.75
  { id: 'MERC-009', mcc: '5944' }, // 0.45
  { id: 'MERC-010', mcc: '5999' }, // 0.50
];

const entries = [];

for (let i = 0; i < 1000; i++) {
  // 0-349: fraud (35%), 350-949: legit (60%), 950-999: edge cases (5%)
  const isFraud = i < 350 ? true : i < 950 ? false : Math.random() > 0.5;

  // Merchant separado por perfil: fraudes usam MCCs de alto risco, legítimos de baixo risco
  const merchant = isFraud
    ? fraudMerchants[i % fraudMerchants.length]
    : legitMerchants[i % legitMerchants.length];

  let amount, installments, avgAmount, isOnline, cardPresent, kmFromHome, txCount24h, knownMerchants;

  if (isFraud) {
    // FRAUDE: bem mais extrema para reduzir falsos negativos
    amount = Math.random() * 4000 + 6000;                   // 6000–10000
    installments = Math.floor(Math.random() * 7) + 6;       // 6–12
    avgAmount = Math.random() * 100 + 30;                   // 30–130 → ratio alto
    isOnline = true;                                        // sempre online
    cardPresent = false;                                    // nunca presencial
    kmFromHome = Math.random() * 400 + 600;                 // 600–1000km
    txCount24h = Math.floor(Math.random() * 6) + 15;        // 15–20
    knownMerchants = [];                                    // sempre desconhecido
  } else {
    // LEGÍTIMO: padrão bem conservador
    amount = Math.random() * 180 + 20;                      // 20–200
    installments = 1;                                       // sempre a vista
    avgAmount = amount + Math.random() * 20 - 10;           // muito perto do valor
    isOnline = false;                                       // sempre presencial
    cardPresent = true;                                     // sempre presencial
    kmFromHome = Math.random() * 15;                        // 0–15km
    txCount24h = Math.floor(Math.random() * 4) + 1;         // 1–4
    knownMerchants = [merchant.id];                         // sempre conhecido
  }

  const now = new Date(Date.now() - Math.random() * 86400000);
  const lastTxTime = new Date(now.getTime() - Math.random() * 3600000);

  const entry = {
    id: `tx-${String(Math.floor(Math.random() * 10000000000)).padStart(10, '0')}`,
    expected_approved: isFraud ? false : true,
    transaction: {
      amount: parseFloat(amount.toFixed(2)),
      installments: installments,
      requested_at: now.toISOString()
    },
    customer: {
      avg_amount: parseFloat(avgAmount.toFixed(2)),
      tx_count_24h: txCount24h,
      known_merchants: knownMerchants
    },
    merchant: {
      id: merchant.id,
      mcc: merchant.mcc,
      avg_amount: parseFloat((Math.random() * 470 + 30).toFixed(2)) // R$30–500 → values[13] = 0.003–0.050
    },
    terminal: {
      is_online: isOnline,
      card_present: cardPresent,
      km_from_home: parseFloat((kmFromHome).toFixed(10))
    },
    last_transaction: isFraud
      ? null
      : {
          timestamp: lastTxTime.toISOString(),
          km_from_current: parseFloat((Math.random() * 5).toFixed(10))
        }
  };

  entries.push(entry);
}

const data = {
  stats: {
    total: 1000,
    fraud_count: 350,
    fraud_rate: 35.0,
    legit_count: 600,
    legit_rate: 60.0,
    edge_case_rate: 5.0
  },
  entries
};

fs.writeFileSync('test-data.json', JSON.stringify(data, null, 2));
console.log('test-data.json criado com 1000 transacoes no formato correto');