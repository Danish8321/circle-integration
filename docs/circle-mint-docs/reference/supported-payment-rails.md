# Supported payment rails

> Reference for the fiat payment rails Circle Mint supports—Fedwire, SWIFT, RTP, SEPA, SPEI, CHATS, PIX, CUBIX, and book transfers—including currencies, regions, settlement timing, and API endpoints.

Verified live against https://developers.circle.com/circle-mint/references/supported-payment-rails on 2026-07-07 — content below unchanged.

{/* vale Google.Headings = NO */} {/* vale Vale.Spelling = NO */}

Circle Mint supports a range of fiat payment rails for funding deposits and
sending payouts. Use this reference to decide how a counterparty should fund a
deposit or receive a payout based on currency, region, and the settlement speed
you need. Circle auto-routes deposits and payouts submitted to
`/v1/businessAccount/banks/wires` to the appropriate rail based on the linked
bank's capabilities, while PIX and CUBIX have dedicated endpoints. Every rail
below supports both deposits and payouts.

## Rail comparison

| Rail          | Currencies | Region                   | Settlement time                                             | API endpoint                      |
| ------------- | ---------- | ------------------------ | ----------------------------------------------------------- | --------------------------------- |
| Fedwire       | USD        | United States (domestic) | 1–3 business days; same business day if before daily cutoff | `/v1/businessAccount/banks/wires` |
| SWIFT         | USD, EUR   | Cross-border             | 1–3 business days; longer with intermediary banks           | `/v1/businessAccount/banks/wires` |
| RTP           | USD        | United States (domestic) | Near-instant, 24/7                                          | `/v1/businessAccount/banks/wires` |
| SEPA          | EUR        | SEPA zone (domestic)     | Same or next business day                                   | `/v1/businessAccount/banks/wires` |
| SEPA Instant  | EUR        | SEPA zone (domestic)     | Near-instant, 24/7                                          | `/v1/businessAccount/banks/wires` |
| SPEI          | MXN        | Mexico (domestic)        | Near-instant                                                | `/v1/businessAccount/banks/wires` |
| CHATS         | HKD        | Hong Kong (domestic)     | Under 5 minutes                                             | `/v1/businessAccount/banks/wires` |
| Book transfer | USD, EUR   | Domestic same-bank       | Near-instant, banking hours                                 | `/v1/businessAccount/banks/wires` |
| PIX           | BRL        | Brazil (domestic)        | Near-instant, 24/7                                          | `/v1/businessAccount/banks/pix`   |
| CUBIX         | USD        | Contact Circle           | Contact Circle                                              | `/v1/businessAccount/banks/cubix` |

## USD rails

### Fedwire

Fedwire is the domestic US wire rail that runs over the Federal Reserve's
wholesale wire system. Wires settle same business day when submitted before the
daily Fedwire cutoff; otherwise they settle the next business day. The linked
bank must have a US ABA routing number. Use `/v1/businessAccount/banks/wires` to
link the account and to submit payouts.

### SWIFT

SWIFT is the international wire rail and supports both USD and EUR. The linked
bank must have a SWIFT/BIC, and for accounts in SEPA-zone countries an IBAN is
also required. Settlement typically takes 1–3 business days but can take longer
when intermediary banks are involved. Use `/v1/businessAccount/banks/wires` to
link the account and to submit payouts.

### RTP

RTP is the Real-Time Payments network in the United States. It is domestic-only
and settles near-instantly, 24/7. There is no separate endpoint: Circle
auto-routes deposits and payouts submitted to `/v1/businessAccount/banks/wires`
over RTP when the linked US bank participates in the network. Per-rail
transaction limits are subject to Circle's banking partner configuration;
contact Circle to confirm the limit.

### CUBIX

CUBIX is a USD rail exposed through a dedicated endpoint,
`/v1/businessAccount/banks/cubix`. Use this endpoint to link a CUBIX-capable
account and to submit deposits and payouts. Contact Circle to confirm the
geographic scope, settlement timing, and account prerequisites for CUBIX before
integrating.

### Book transfer

When the linked bank account and a Circle account are held at the same banking
partner, Mint can route deposits and payouts as a book-to-book transfer in that
bank. These transfers are typically near-instant during banking hours and
support USD and EUR. There is no separate endpoint: Mint chooses this path
automatically when the linked bank matches.

## EUR rails

### SEPA

SEPA is the Single Euro Payments Area credit transfer rail for accounts in
SEPA-zone countries. Settlement is same or next business day. The linked bank
must be in a SEPA country and provide an IBAN. Use
`/v1/businessAccount/banks/wires` to link the account and to submit payouts.

### SEPA Instant

SEPA Instant settles near-instantly, 24/7, subject to the linked bank's
participation in the SEPA Instant scheme. Use `/v1/businessAccount/banks/wires`
to link the account and to submit payouts; Circle auto-routes through SEPA
Instant when the linked bank supports it. Per-rail transaction limits are
subject to scheme and banking partner configuration; contact Circle to confirm
the limit.

## MXN rails

### SPEI

SPEI (Sistema de Pagos Electrónicos Interbancarios) is Mexico's domestic
real-time payment system. Settlement is near-instant. The linked bank must be
configured for SPEI. Use `/v1/businessAccount/banks/wires` to link the account
and to submit payouts.

## HKD rails

### CHATS

CHATS (Clearing House Automated Transfer System) is Hong Kong's real-time gross
settlement system. Settlement is under 5 minutes. The linked bank must
participate in CHATS. Use `/v1/businessAccount/banks/wires` to link the account
and to submit payouts. Per-rail transaction limits are subject to scheme and
banking partner configuration; contact Circle to confirm the limit.

## BRL rails

### PIX

PIX is Brazil's instant payment system, operated by the Banco Central do Brasil.
Settlement is near-instant, 24/7. The linked Brazil bank must be enrolled for
PIX. PIX uses a dedicated endpoint surface: `/v1/businessAccount/banks/pix` to
link an account and to submit payouts, and
`/v1/businessAccount/banks/pix/{id}/instructions` to retrieve deposit
instructions for a linked PIX account.

<Note>
  Once fiat is received and credited to your Circle Mint account, Circle
  typically completes onchain minting in 15 minutes. For the full lifecycle from
  fiat receipt to onchain credit, see [How minting
  works](/circle-mint/concepts/how-minting-works).
</Note>

{/* vale Google.Headings = YES */} {/* vale Vale.Spelling = YES */}
