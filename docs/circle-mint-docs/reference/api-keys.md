# API keys

> Learn about the different types of API keys used to authenticate requests to Circle's platform.

**Confirmed live 2026-07-07** at `https://developers.circle.com/api-reference/keys`
(moved out of `circle-mint/*` entirely — product-agnostic page, slug is
`keys` not `api-keys`). Content matches: three key types — API keys
(server-side, privileged ops), client keys (frontend, domain/app-bound),
kit keys (single credential across testnet+mainnet). Content below
unchanged.

Certain products use API keys to authenticate requests. Circle provides three
types of keys for different use cases: [API keys](#api-keys) for server-side
access, [client keys](#client-keys) for frontend applications, and
[kit keys](#kit-keys) for SDK integrations.

<Note>
  Permissionless products like CCTP and Gateway do not require an API key.
</Note>

<CardGroup cols={3}>
  <Card title="API key" icon="server">
    Authenticate server-side requests to Circle's RESTful APIs.
  </Card>

  <Card title="Client key" icon="browser">
    Authenticate client applications with domain or app binding. Required for
    frontend SDKs.
  </Card>

  <Card title="Kit key" icon="cube">
    Authenticate kit access with a single key that works on both testnet and
    mainnet.
  </Card>
</CardGroup>

## API keys

An API key is a unique string used to authenticate and enable access to
privileged operations on Circle's APIs. It's required for any RESTful API
requests to Circle services. Without it, requests will fail.

### Keep your API keys safe

API keys allow access to sensitive operations, so you must secure them.

* **Avoid public exposure**: Never share API keys or include them in client-side
  code, public repositories, or other public mediums.
* **Manage securely**: Use the Circle Console to generate and manage API keys.
  When generating a key, copy it exactly as displayed.

<Warning>Losing control of your API key can result in financial loss.</Warning>

### API key authentication

Use the headers below to authenticate requests on testnet or mainnet.

#### Testnet authorization header example

```text theme={null}
authorization: Bearer TEST_API_KEY:ebb3ad72232624921abc4b162148bb84:019ef3358ef9cd6d08fc32csfe89a68d
```

#### Mainnet authorization header example

```text theme={null}
authorization: Bearer LIVE_API_KEY:ebb3ad72232624921abc4b162148bb84:019ef3358ef9cd6d08fc32csfe89a68d
```

### Test authentication

To verify your API key setup, use the following `curl` command to retrieve
wallets:

```bash theme={null}
curl --request GET \
     --url https://api.circle.com/v1/w3s/wallets \
     --header 'accept: application/json' \
     --header 'authorization: Bearer <API_KEY>'
```

A successful response looks like this:

```json theme={null}
{
  "data": {
    "wallets": []
  }
}
```

An error response looks like this:

```json theme={null}
{
  "code": 401,
  "message": "Malformed authorization. Are the credentials properly encoded?"
}
```

***

## Client keys

A client key is a unique string used to authenticate and authorize API access
for apps using Circle's SDKs. A client key is linked to either a specific host
domain (websites), bundle ID (iOS), or package name (Android). This restricts
access to pre-configured apps.

<Note>
  A client key must be included in the headers of all modular wallets SDK API
  calls.
</Note>

### Best practices for client keys

Client keys enable access to sensitive application operations, so protecting
them is critical. Follow these best practices:

1. **Use separate keys for each application**: Create separate keys for web and
   mobile apps (iOS, Android) to prevent shared vulnerabilities.
2. **Monitor for misuse**: Set up alerts for unusual activity, such as
   unexpected spikes in API calls, and use monitoring tools to detect anomalies.
3. **Rotate keys regularly**: Regenerate client keys periodically and update
   them in your apps to reduce risk if a key is compromised.
4. **Store keys securely**: Use secure storage options like Local Storage or
   Secure Storage for mobile apps, and avoid unnecessary exposure.
5. **Restrict access**: Limit the scope of client keys by associating them with
   specific apps or domains to minimize potential misuse.

***

## Kit keys

A kit key is a unique string used to authenticate access for Circle's developer
kits. Kit keys simplify integration by providing a single credential that works
across both testnet and mainnet environments, reducing configuration overhead
when building. Kit keys are free to create and do not require KYC.

<Note>
  **Testnet and mainnet compatibility**

  Unlike API keys and client keys, kit keys work on both testnet and mainnet. You
  can use the same key during development and in production.
</Note>

### Keep your kit keys safe

Kit keys enable access to SDK features, so protecting them is essential.

* **Avoid public exposure**: Never share kit keys or include them in client-side
  code, public repositories, or other public mediums.
* **Manage securely**: Use your
  [Circle Developer account](https://console.circle.com/api-keys) to generate
  and manage kit keys. When generating a key, copy it exactly as displayed.

<Warning>
  Losing control of your kit key can result in unauthorized access to SDK
  capabilities.
</Warning>
