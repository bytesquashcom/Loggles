# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.x     | Yes       |

## Reporting a vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please report security issues by emailing the maintainer directly or using [GitHub's private vulnerability reporting](https://github.com/bytesquashcom/Loggles/security/advisories/new).

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

You will receive a response within 72 hours. Once confirmed, a fix will be released and the issue publicly disclosed after a reasonable remediation period.

## Security model

Loggles is designed for local or trusted-network use. Key points:

- **No authentication by default** — suitable for localhost only. Set `Auth__ApiKey` before exposing to a network.
- **API key auth** — all endpoints require `Authorization: Bearer <key>` when configured.
- **OAuth 2.0 + PKCE** — MCP clients negotiate auth automatically; access tokens are scoped to the configured API key.
- **No multi-tenancy** — a single Loggles instance serves one user/team. Do not expose it as a shared public service.
- **Log data is sensitive** — logs may contain PII, stack traces, or internal identifiers. Treat the `/data` volume accordingly.
