# CAMT.053 daily statements

> Integrate ISO 20022 CAMT.053 daily statement XML from Circle Mint for treasury and reconciliation workflows.

Confirmed live 2026-07-07 at https://developers.circle.com/circle-mint/references/camt053-statements
(real slug is `camt053-statements`, not `camt053-daily-statements` — my
earlier 4 path guesses were all wrong, not deleted content) — content below
unchanged.

[ISO 20022](https://www.iso20022.org/) CAMT.053 is a standard bank-to-customer
statement format used for treasury reconciliation. Circle Mint generates one
CAMT.053.001.13 XML file per calendar day (UTC), covering all USDC and EURC
activity on your account. You
[retrieve the file](/circle-mint/references/camt053-statements#retrieve-the-report)
through the Circle Mint API and parse it to reconcile balances, match
transactions, and integrate with your treasury systems.

## Availability and SLA

* **Coverage**: Each report covers the prior calendar day from 00:00 through
  24:00 UTC.
* **When it is ready**: Circle delivers the report by 06:00 UTC the next day,
  typically around 02:00 UTC.
* **One file, multiple currencies**: USDC and EURC statements are delivered in a
  single XML file as separate `Stmt` blocks.

<Tip>
  Subscribe to the [Circle status page](https://status.circle.com/) to receive
  updates on CAMT.053 outages.
</Tip>

## Retrieve the report

Retrieve the CAMT.053 report by first calling
[Request a report](/api-reference/circle-mint/account/request-report) with
`reportType: camt053` and `date` in `YYYY-MM-DD` format (the UTC calendar date
the statement covers), then use the returned `id` with one of the following
endpoints:

* [Get report by ID](/api-reference/circle-mint/account/get-report-by-id):
  Returns JSON with a `data` object that includes `downloadUrl` and `expiresAt`.
  Download the XML from `downloadUrl` before the time indicated by `expiresAt`.
* [Get report content](/api-reference/circle-mint/account/get-report-content):
  Returns the raw XML as `application/xml`. The response uses a
  `Content-Disposition` attachment filename such as
  `camt053_YYYY-MM-DD_report.xml`.

## Read and reconcile the report

### File format at a glance

The report uses ISO 20022 CAMT.053.001.13 XML structure. See the
[ISO 20022 message definitions catalog](https://www.iso20022.org/iso-20022-message-definitions)
for official message definitions.

* The file uses namespace `urn:iso:std:iso:20022:tech:xsd:camt.053.001.13`.
* The root is `Document` → `BkToCstmrStmt` → one or more `Stmt` elements.
* Each `Stmt` is one account and currency.
* Inside a statement you will see opening and closing balances and `Ntry`
  (entry) elements. There is one `Ntry` per movement that affects the balance.

### Balances

Each `Stmt` includes opening and closing balances for the report date:

* `OPBD`: Opening booked balance for the report date.
* `CLBD`: Closing booked balance for the report date.

Each balance has an amount with `Ccy` (currency), `CdtDbtInd` (credit or debit
side of the balance), and a timestamp. For reconciliation, a common check is:
opening balance plus the sum of entries (respecting credit and debit) aligns
with closing balance for that statement.

### Currency vs token

Standard ISO 20022 fields follow ISO 4217. They use three-letter currency codes
on `Amt` and related standard elements:

* `USD` for USDC transactions (for example `<Amt Ccy="USD">`).
* `EUR` for EURC transactions (for example `<Amt Ccy="EUR">`).

The full token identifier (`USDC` or `EURC`) is preserved on each transaction
line. At the transaction level it follows the path `Ntry` → `NtryDtls` →
`TxDtls` → `SplmtryData` →
[`CircleTxn`](/circle-mint/references/camt053-statements#circle-transaction-details-circletxn)
→ `Token`.

Use both when you need to match fiat-style accounting codes and token
identifiers.

### Transaction entries (`<Ntry>`)

Each `Ntry` is one transaction line. Important children include:

* `Amt`: Amount and ISO currency on the amount (`USD` or `EUR`).
* `CdtDbtInd`: `CRDT` (credit) or `DBIT` (debit).
* `Sts`: Booking status in `Cd`, such as:
  * `BOOK`: Booked
  * `PDNG`: Pending
  * `RJCT`: Rejected
  * `FAIL`: Failed
* `BookgDt`: Booking time in `DtTm`, UTC (ISO 8601).
* `BkTxCd`: Circle's label for the transaction type, found under `Prtry` / `Cd`.
  Unmapped types appear as `UNKNOWN`.

### Circle transaction details (`CircleTxn`)

Circle adds detail under `Ntry` → `NtryDtls` → `TxDtls` → `SplmtryData` where
`PlcAndNm` is `CircleTransactionData`. Inside the envelope, `CircleTxn` uses
namespace `urn:circle:camt053:transaction`.

The following child elements may appear on a transaction line. This list is not
exhaustive. Your parser must tolerate additional elements, and any field below
may be absent on a given line.

* `TransactionId`: Identifier for the movement.
* `JobId`: Related job identifier.
* `Token`: Full token identifier (`USDC` or `EURC`).
* `CustomReferenceId`: Customer-provided reference when supplied.
* `ExternalReferenceId`: EFT-style reference when supplied (for example `IMAD`
  or `UETR`).
* `Blockchain`: Blockchain identifier when the movement is onchain.
* `TransactionHash`: Onchain transaction hash when present.
* `Source`: Originating party or address when populated.
* `SourceType`: Type of source (for example fiat account or blockchain address)
  when populated.
* `Destination`: Receiving party or address when populated.
* `DestinationType`: Type of destination when populated.
* `CustomerId`: Customer association when present.

Use these fields to tie a line back to APIs, onchain activity, or internal
references.

## Example truncated statement

The example below shows the header, one statement with opening and closing
balances, and one entry (abbreviated). It is only for orientation. Your real
files can contain many entries and a second statement for the other currency.

```xml Example CAMT.053 fragment theme={null}
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.13">
  <BkToCstmrStmt>
    <GrpHdr>
      <MsgId>CAMT053_0455_20251006</MsgId>
      <CreDtTm>2025-10-07T14:30:45Z</CreDtTm>
      <MsgRcpt>
        <Id>
          <OrgId>
            <Othr>
              <Id>e0549c6e-c80e-4e5f-95ee-c66f7d1be455</Id>
              <SchmeNm>
                <Prtry>EntityId</Prtry>
              </SchmeNm>
            </Othr>
          </OrgId>
        </Id>
      </MsgRcpt>
    </GrpHdr>
    <Stmt>
      <Id>0455_1000123456_USD_20251006</Id>
      <Acct>
        <Id>
          <Othr>
            <Id>1000123456</Id>
          </Othr>
        </Id>
        <Ccy>USD</Ccy>
      </Acct>
      <Bal>
        <Tp>
          <CdOrPrtry>
            <Cd>OPBD</Cd>
          </CdOrPrtry>
        </Tp>
        <Amt Ccy="USD">1750000.00</Amt>
        <CdtDbtInd>CRDT</CdtDbtInd>
        <Dt>
          <DtTm>2025-10-06T00:00:00Z</DtTm>
        </Dt>
      </Bal>
      <Bal>
        <Tp>
          <CdOrPrtry>
            <Cd>CLBD</Cd>
          </CdOrPrtry>
        </Tp>
        <Amt Ccy="USD">1775000.00</Amt>
        <CdtDbtInd>CRDT</CdtDbtInd>
        <Dt>
          <DtTm>2025-10-06T23:59:59Z</DtTm>
        </Dt>
      </Bal>
      <Ntry>
        <Amt Ccy="USD">25000.00</Amt>
        <CdtDbtInd>CRDT</CdtDbtInd>
        <Sts>
          <Cd>BOOK</Cd>
        </Sts>
        <BookgDt>
          <DtTm>2025-10-06T08:15:23Z</DtTm>
        </BookgDt>
        <BkTxCd>
          <Prtry>
            <Cd>Mint</Cd>
          </Prtry>
        </BkTxCd>
        <NtryDtls>
          <TxDtls>
            <LclInstrm>
              <Prtry>wire</Prtry>
            </LclInstrm>
            <SplmtryData>
              <PlcAndNm>CircleTransactionData</PlcAndNm>
              <Envlp>
                <CircleTxn xmlns="urn:circle:camt053:transaction">
                  <TransactionId>550e8400-e29b-41d4-a716-446655440000</TransactionId>
                  <JobId>660e9511-f3ac-52e5-b827-557766551111</JobId>
                  <Token>USDC</Token>
                </CircleTxn>
              </Envlp>
            </SplmtryData>
          </TxDtls>
        </NtryDtls>
      </Ntry>
    </Stmt>
  </BkToCstmrStmt>
</Document>
```

## See also

* [Webhook notifications](/circle-mint/references/webhook-notifications):
  Subscribe to event notifications for transactions and account activity.
* [Supported payment rails](/circle-mint/references/supported-payment-rails):
  Review the bank rails and blockchains available for funding and withdrawals.
* [Error codes](/circle-mint/references/error-codes): Look up API error codes
  returned by Circle Mint endpoints.
