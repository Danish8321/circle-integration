# Circle Mint

> Mint and redeem USDC and EURC directly from Circle.

<Note>
  Circle Mint is for institutional customers minting USDC or EURC. It is
  typically used by exchanges, institutional traders, wallet providers, banks,
  and consumer-app companies. [Contact
  Circle](https://www.circle.com/mint-contact) to learn more.
</Note>

Circle Mint lets you mint (convert fiat to stablecoins) and redeem (convert
stablecoins back to fiat) [USDC](/stablecoins/what-is-usdc) and EURC directly
from Circle, the sole issuer of both stablecoins. Every token redeems 1:1 for
its backing fiat currency: U.S. dollars for USDC, euros for EURC.

Manage your account through the [Mint Console](https://app.circle.com/) or the
Circle Mint API. Deposit fiat from a linked bank account, convert it to USDC or
EURC, and send stablecoins to blockchain wallets globally. To start integrating,
[set up your account and API key](/circle-mint/getting-started-with-the-circle-apis).

## What you can do

- [Mint and redeem stablecoins](quickstarts/mint-and-redeem-usdc.md) — convert
  fiat to USDC/EURC and redeem back through your Circle Mint account.
- [Transfer onchain](howtos/transfer-usdc-onchain.md) — send/receive USDC and
  EURC on supported blockchains.
- [Accept stablecoin payments](howtos/receive-stablecoin-payin.md) — use
  Circle's Payment APIs to accept USDC deposits from customers.
- [Exchange currencies](/circle-mint/howtos/exchange-currencies) — convert
  between local currencies and USDC using cross-currency APIs (not yet
  mirrored locally — fetch from official docs if needed).

## What you can do

<CardGroup cols={2}>
  <Card title="Mint and redeem stablecoins" icon="coins" href="/circle-mint/quickstarts/mint-and-redeem">
    Convert fiat to USDC or EURC and redeem stablecoins back to fiat through
    your Circle Mint account.
  </Card>

  <Card title="Transfer onchain" icon="arrow-right-arrow-left" href="/circle-mint/howtos/transfer-on-chain">
    Send and receive USDC and EURC on supported blockchains.
  </Card>

  <Card title="Accept stablecoin payments" icon="money-bill-wave" href="/cpn/stablecoin-payments/howtos/receive-stablecoin-payin">
    Use Circle's Payment APIs to accept USDC deposits from your customers.
  </Card>

  <Card title="Exchange currencies" icon="rotate" href="/circle-mint/howtos/exchange-currencies">
    Convert between local currencies and USDC using cross-currency APIs.
  </Card>
</CardGroup>
