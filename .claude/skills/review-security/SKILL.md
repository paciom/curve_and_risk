---
name: review-security
description: Review code for security vulnerabilities - injection, broken authentication and authorisation, secrets, unsafe deserialisation, SSRF, path traversal, cryptography misuse, vulnerable dependencies, data exposure. Use when asked for a security review, and on any change that handles untrusted input, authentication, sessions, secrets, file or network access, or personal data.
---

# Security review

Review as an attacker would read the code: for every piece of data, ask where it came from and what it can reach. A vulnerability is a path from something an attacker controls (a source) to something that matters (a sink) with no effective check in between.

Report only what you can trace. A finding names the source, the sink, and the missing control.

## Method

1. **Map the attack surface.** List every entry point the change adds or touches: HTTP endpoints, message handlers, file and CLI inputs, webhooks, deserialised data, values read from a database that users can write, and text returned by other services or by an LLM.
2. **Trace each input to its sinks.** Follow the value through the code. Note every place it reaches a sink from the checklist below.
3. **Check the control at each sink**, not at the entry point alone. Validation far from the sink gets bypassed by the next caller.
4. **Check who is allowed.** For each entry point: is the caller authenticated, and is this caller authorised for this specific resource?
5. **Look at what leaves.** Responses, logs, error messages, telemetry.
6. **Confirm.** Build a concrete input that exploits the issue, or a test that demonstrates it. Mark anything unconfirmed as such.

## Checklist

**Injection** (untrusted data interpreted as code)
- SQL: parameterised queries or an ORM only. String concatenation or interpolation into SQL is a finding, including in `ORDER BY`, table names and raw-SQL escape hatches.
- OS command: no shell; pass arguments as an array; never build a command line from input.
- Path traversal: resolve to a full path and confirm it is under the intended root; reject `..`, absolute paths and alternate separators.
- HTML/JS (XSS): encode on output for the context (HTML, attribute, JS, URL); no raw-HTML helpers on untrusted data.
- Also: LDAP, XPath, regular expressions (ReDoS), template engines, log injection (newlines), header injection.
- LLM prompt injection: text from documents, tools or users is data. It must not be able to trigger a privileged action without a control outside the model.

**Authentication and sessions**
- Passwords hashed with a slow, salted algorithm (Argon2id, bcrypt, PBKDF2); never encrypted, never plain hashed.
- Tokens validated fully: signature, issuer, audience, expiry, algorithm pinned.
- Session identifiers random, rotated on login, invalidated on logout; cookies `Secure`, `HttpOnly`, `SameSite`.
- Rate limiting and lockout on credential endpoints. No user enumeration through differing responses.

**Authorisation**
- Every endpoint enforces it server-side; hiding a button is not access control.
- Object-level checks: the caller owns or may access *this* id, not merely "is logged in" (IDOR).
- Deny by default. Privileged actions re-checked at the point of action.
- Mass assignment: bind to a request type with only the fields a caller may set.

**Secrets and configuration**
- No secrets in source, config files, test fixtures, container images or logs. Search the diff for keys, tokens, connection strings and private keys.
- Secrets come from a secret store or environment at runtime; least privilege on each credential.
- Debug endpoints, verbose errors and default credentials off in production. CORS not `*` with credentials.

**Cryptography**
- Use the platform's vetted primitives; never write your own.
- No MD5, SHA-1, DES, ECB mode, or static IVs for security purposes. Authenticated encryption (AES-GCM, ChaCha20-Poly1305).
- Random values for security from a cryptographic generator, not `Random`.
- TLS verification never disabled. Constant-time comparison for secrets.

**Data handling**
- Deserialisation: no type-name handling or polymorphic deserialisation of untrusted data; no `BinaryFormatter`, `pickle`, or native serialisation from untrusted sources.
- XML: external entities disabled (XXE).
- File upload: size limits, content type verified by content, stored outside the web root under a generated name.
- SSRF: outbound requests to user-supplied URLs go through an allowlist; block internal ranges and metadata endpoints; do not follow redirects blindly.
- Open redirect: redirect targets validated against an allowlist.

**Exposure**
- Errors returned to callers carry no stack traces, SQL, paths or internal hostnames.
- Logs carry no passwords, tokens, full card numbers or unnecessary personal data.
- Responses return only the fields the caller is entitled to.

**Resource abuse**
- Limits on request size, collection size, pagination, recursion depth and regex input.
- Timeouts on every outbound call. Expensive operations rate-limited.

**Dependencies and supply chain**
- New dependencies: maintained, widely used, pinned. Check for known vulnerabilities (`dotnet list package --vulnerable`, `npm audit`, `pip-audit`).
- Lock files committed. No install scripts from untrusted sources. CI actions pinned.

**Concurrency**
- Time-of-check to time-of-use: a permission or balance checked, then acted on without holding the guarantee.

## Reporting

Order by severity (impact x likelihood). For each finding:

```
[High] SQL injection in order search
Location: src/Orders/OrderRepository.cs:57
Source:   `sort` query parameter (GET /orders)
Sink:     interpolated into ORDER BY in raw SQL
Exploit:  ?sort=id;DROP TABLE orders--
Fix:      map `sort` to an allowlist of column names; never interpolate
Status:   confirmed with a failing integration test
```

| Severity | Guide |
|---|---|
| Critical | Remote code execution, authentication bypass, bulk data exposure, exploitable without credentials |
| High | Injection, broken object-level authorisation, secret exposure, exploitable by any authenticated user |
| Medium | Requires unusual conditions or yields limited data; missing hardening on a sensitive path |
| Low | Defence-in-depth improvement; no direct exploit |

Also state the entry points you reviewed, what you found clean, and what was out of scope or could not be assessed (infrastructure, third-party services, anything not in the code you were given). Do not report theoretical issues with no reachable path; they bury the real ones.

## Fixing

Fix at the sink with the standard control for that sink (parameterise, encode, allowlist, authorise). Add a test that carries the exploit input and fails before the fix. If a secret has been committed, removing it from the code is not enough: it must be rotated, and the user must be told.
