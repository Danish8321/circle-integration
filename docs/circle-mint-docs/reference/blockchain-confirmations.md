# Blockchain confirmations

> Confirmation requirements and approximate times for each blockchain supported by Circle Mint.

Verified live against https://developers.circle.com/circle-mint/references/blockchain-confirmations on 2026-07-07 — content below unchanged.

## What are blockchain confirmations?

When you submit a transaction to a blockchain, it starts in a pending state. The
network must include it in a block and validate it before it counts as
confirmed. Each new block added after that makes the transaction harder to
reverse.

A **confirmation number** is the number of blocks that must follow a
transaction's block before it is final. Once the confirmation number is reached,
the transaction can't be reversed.

## Why confirmations matter

Without enough confirmations, transactions are at risk of reorganizations
(reorgs). A reorg happens when validators discard recent blocks and replace them
with new ones, rewriting part of the blockchain's history. This can reverse
transactions that appeared settled.

Each extra confirmation makes a reorg less likely. Because blockchains differ in
design, block times, and consensus, the number of confirmations needed varies by
blockchain.

## Confirmation numbers

Confirmations show how safe a transaction is from reorg. The confirmation number
is how many blocks must be added until a block is considered permanent.

Each blockchain's confirmation number is different, and determined by Circle.
Confirmation numbers are based on a variety of factors, including the
blockchain's history, potential for reorg, and overall network architecture.

For layer-2 (L2) blockchains that settle transactions on a separate base layer,
Circle waits for blocks on the layer-1 (L1) base blockchain. This happens after
the L2 block gets included in an L1 block.

| Chain              | Confirmations     | Approximate time   |
| ------------------ | ----------------- | ------------------ |
| Algorand           | **1**             | \~3 seconds        |
| Aptos              | **1**             | \~500 milliseconds |
| Arbitrum           | **12 ETH Blocks** | \~4 to 6 minutes   |
| Arc                | **1**             | under 1 second     |
| Avalanche C-Chain  | **1**             | \~2 seconds        |
| Base               | **12 ETH Blocks** | \~3 to 9 minutes   |
| Celo               | **12 ETH Blocks** | \~3 to 9 minutes   |
| Codex              | **12 ETH Blocks** | \~3 to 9 minutes   |
| Cronos             | **1**             | under 1 second     |
| EDGE               | **12 ETH Blocks** | \~3 to 5 minutes   |
| Ethereum           | **12**            | \~3 minutes        |
| Hedera             | N/A               | \~3 seconds        |
| HyperEVM           | **1**             | under 1 second     |
| Injective          | **1**             | under 1 second     |
| Ink                | **12 ETH Blocks** | \~3 to 9 minutes   |
| Linea              | **65 ETH Blocks** | \~6 to 32 hours    |
| Monad              | **4**             | \~1.6 seconds      |
| Morph              | **64**            | \~20 to 30 minutes |
| NEAR               | **1**             | \~2 seconds        |
| Noble              | **1**             | \~1.53 seconds     |
| OP Mainnet         | **12 ETH Blocks** | \~3 to 9 minutes   |
| Pharos             | **1**             | \~20 seconds       |
| Plume              | **12 ETH Blocks** | \~3 to 9 minutes   |
| Polkadot Asset Hub | **1**             | \~12 seconds       |
| Polygon PoS        | **2-3**           | \~8 seconds        |
| Sei                | **1**             | \~400 milliseconds |
| Solana             | **1**             | \~400 milliseconds |
| Sonic              | **1**             | \~1 second         |
| Starknet           | **65 ETH Blocks** | \~4 to 8 hours     |
| Stellar            | **1**             | \~5 seconds        |
| Sui                | **1**             | \~500 milliseconds |
| Unichain           | **12 ETH Blocks** | \~3 to 9 minutes   |
| World Chain        | **12 ETH Blocks** | \~3 to 9 minutes   |
| XDC                | **3**             | \~6 seconds        |
| XRPL               | **1**             | \~5 seconds        |
| ZKsync Era         | **65 ETH Blocks** | \~5 to 7 hours     |

<Note>
  **Hedera**: Hedera is built on a hashgraph, not a blockchain. As such, there
  isn't a count of confirmations before Circle considers a transfer valid. This
  determination is performed on Hedera directly and is then shared back to Circle.
  See [Hedera consensus](https://hedera.com/how-it-works) to learn more.

  **Linea**: Linea requires 65 ETH block confirmations. However, Linea only posts
  batches to Ethereum every 6-32 hours, so the approximate confirmation time
  reflects this batch posting interval rather than the ETH block time alone.
</Note>

## Transfer status

When an incoming transfer is included in a block, the API makes it available for
you. However, the transfer remains in the `running` status. It won't credit the
balance of the associated wallet with the transfer amount until the required
number of confirmations has been reached.

Once the confirmation number is reached, the transfer status changes to
`completed`. If you subscribed to webhook notifications, you receive a message
about this change.

You can use transfers in your processes before they reach `completed` status.
This comes with risk. A blockchain reorganization (reorg) occurs when validators
discard recent blocks and replace them with new ones, reversing transactions
that appeared settled. If a reorg reverses a transfer you already acted on, the
credited balance is rolled back. Reorgs are rare, but if you use transfers
before they get enough confirmations, you take on this risk.

<Note>
  Waiting for confirmations only applies to onchain transfers. These are transfers
  where the `source` is type `blockchain`. Transfers between hosted wallets don't
  need to wait. These transfers have a `source` type of `wallet` and happen
  instantly.
