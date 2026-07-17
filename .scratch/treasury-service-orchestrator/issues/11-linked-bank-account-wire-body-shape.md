Status: open

Source: `docs/features/08-banking-and-wire-instructions.md` §7, §10 item 5; `docs/README.md` §7.
Blocked by: 07-redemption-and-linked-bank-account.

## Scope

Real `POST /v1/businessAccount/banks/wires` body is **nested** and bank-location-dependent
(three schemas: US, IBAN, non-IBAN-non-US) — `billingDetails{name,city,country,line1,
postalCode,line2?,district?}`, `bankAddress{country,bankName?,city?,line1?,line2?,district?}`.
Phase 1's `LinkedBankAccount` domain entity / `CreateLinkedBankAccountGatewayRequest` carry only
four **flat** fields (`BeneficiaryName`, `AccountNumber`, `RoutingNumber`, `BankName`) — a subset
of even the US schema's required fields. `CircleMintGateway.CreateLinkedBankAccountAsync` cannot
build a well-formed US wire-creation request from the Phase 1 domain shape alone as it stands.

Not a blocker for mock mode (§6 invents no such fields) or any Application-tier concern — only
hits at the Infrastructure/HTTP boundary once Phase 3's real gateway is built.

## Decision needed (not picked by the doc pass)

Two resolutions, pick one before Phase 3 Task 3 implements `CreateLinkedBankAccountAsync` for
real:

(a) Widen `LinkedBankAccount`/`CreateLinkedBankAccountCommand` to carry the full nested
    US-schema fields (`billingDetails.*`, `bankAddress.country`) — entity-shape change, needs a
    migration.
(b) Keep the domain shape flat; source the missing billing/bank-address fields from static
    Distributor-level configuration (plausible only if the Distributor always links its own bank
    accounts under one fixed billing identity — unconfirmed assumption, needs product sign-off).

IBAN and non-IBAN-non-US schemas are explicitly out of scope for Phase 1 either way (US-only
today).

## Definition of done

- Decision (a) or (b) recorded here.
- `LinkedBankAccount`/`CreateLinkedBankAccountGatewayRequest`/`CircleMintGateway.
  CreateLinkedBankAccountAsync` updated to match; migration reviewed by hand if (a).
- `CircleMintGatewayTests.cs` fixture asserts the full US wire-creation body shape per
  `docs/features/08-banking-and-wire-instructions.md` §5/§7.

## Comments
