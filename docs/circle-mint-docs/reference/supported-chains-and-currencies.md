# Supported chains and currencies

> Which blockchains Circle Mint and the Circle APIs support for USDC and EURC, and where sending the wrong asset/chain combination loses funds permanently.

Source: https://developers.circle.com/circle-mint/supported-chains-and-currencies

Circle Mint supports USDC across 34 chains and EURC across 8 chains. Circle
Mint and Circle APIs only support USDC and EURC on the indicated blockchains —
sending an unsupported asset or using an unsupported chain can permanently
lose funds.

## Chain-specific warnings

- **Cosmos appchains** — only Noble's USDC is supported. Transfers from other
  appchains must route back through Noble first.
- **Polkadot parachains** — only Polkadot Asset Hub's USDC is accepted.
  XCM-transferred assets from other parachains risk permanent loss.
- **Injective** — only EVM-layer deposits are supported; Cosmos-layer deposits
  get stuck.

## API usage

Specify currency-chain pairs when calling transfer/address endpoints, e.g.
`USD` currency on `ETH` chain for Ethereum USDC. Balance inquiries reference
currency alone — Circle-hosted asset values are chain-independent.

Check `https://developers.circle.com/llms.txt` for the current full chain
list before shipping a new chain integration; this file is a summary, not the
authoritative table.
