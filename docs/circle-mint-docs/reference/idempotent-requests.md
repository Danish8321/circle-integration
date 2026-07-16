# Idempotent requests

> Idempotency keys let you safely retry Circle API calls.

Verified live 2026-07-07 at https://developers.circle.com/api-reference/idempotent-requests
(moved from `circle-mint/references/idempotent-requests` to `api-reference/`,
now product-agnostic) — content below still accurate; live page adds a
Node.js `crypto.randomUUID()` example for generating keys.

Circle APIs support
[idempotent requests](https://en.wikipedia.org/wiki/Idempotence), so making the
same request multiple times produces the same result. This lets you safely retry
API calls if something goes wrong.

## Idempotency keys

Certain endpoints require you to generate an idempotency key to identify the
request. For endpoints that require an idempotency key, each request must have a
unique key.

The server uses this key to identify a specific request. When a request is made
with the same idempotency key, the server returns the original response instead
of executing the operation again.

For endpoints that require it, the idempotency key must be in
[UUID version 4](https://en.wikipedia.org/wiki/Universally_unique_identifier)
format.

The following example demonstrates how to generate an idempotency key in
Node.js:

```typescript theme={null}
import crypto from "crypto";

function generateIdempotencyKey(): string {
  return crypto.randomUUID();
}

const idempotencyKey: string = generateIdempotencyKey();
console.log(idempotencyKey); // e.g. "f47ac10b-58cc-4372-a567-0e02b2c3d479"
```
