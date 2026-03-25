# Loggles

[![License: ELv2](https://img.shields.io/badge/License-Elastic_v2-blue.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/bytesquashcom/Loggles)](https://github.com/bytesquashcom/Loggles/releases)
[![GHCR](https://img.shields.io/badge/ghcr.io-bytesquashcom%2Floggles-blue)](https://github.com/bytesquashcom/Loggles/pkgs/container/loggles)

A local-first log sink that turns your coding agent into a runtime companion.

Instead of copy-pasting log output into Claude or Cursor, point your app at Loggles. Your agent
queries exactly what it needs — filtered by service, level, trace ID, or time window — and gets
back structured data it can reason over. No token waste, no manual copy-paste.

```
Your App  ──OTLP──▶  Loggles  ──MCP──▶  Claude Code / Cursor
```

Two use cases, same setup:

- **Investigating a bug** — Claude reads your source, traces the failure through logs, and points
  to the line. You describe the problem; it does the digging.
- **Watching runtime behaviour** — Ask "what did my app do when I hit that endpoint?" and Claude
  tails the logs, traces the request, and narrates what happened. Like a live debugger, without
  attaching one.

---

## Quick start

### Docker

```bash
docker run -d \
  -p 5000:5000 \
  -v loggles-data:/data \
  ghcr.io/bytesquashcom/loggles:latest
```

Add `-e Auth__ApiKey=your-secret-key` to enable API key protection (see [Authentication](#authentication)).

### dotnet run

```bash
git clone https://github.com/bytesquashcom/Loggles.git
cd Loggles/src/Loggles.Api
dotnet run
# Listening on http://localhost:5000
```

---

## Sending logs to Loggles

Point your OTLP exporter at `http://localhost:5000/v1/logs` using HTTP/protobuf.

### .NET

```csharp
// dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
builder.Services.AddOpenTelemetry()
    .WithLogging(logging =>
    {
        logging.AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri("http://localhost:5000/v1/logs");
            otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
        });
    });
```

### Node.js

```bash
npm install @opentelemetry/sdk-node @opentelemetry/exporter-logs-otlp-http @opentelemetry/sdk-logs
```

```js
const { LoggerProvider, SimpleLogRecordProcessor } = require('@opentelemetry/sdk-logs');
const { OTLPLogExporter } = require('@opentelemetry/exporter-logs-otlp-http');

const provider = new LoggerProvider();
provider.addLogRecordProcessor(
  new SimpleLogRecordProcessor(
    new OTLPLogExporter({ url: 'http://localhost:5000/v1/logs' })
  )
);
provider.register();
```

### Python

```bash
pip install opentelemetry-exporter-otlp-proto-http opentelemetry-sdk
```

```python
from opentelemetry import _logs
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter

provider = LoggerProvider()
provider.add_log_record_processor(
    BatchLogRecordProcessor(OTLPLogExporter(endpoint="http://localhost:5000/v1/logs"))
)
_logs.set_logger_provider(provider)
```

### Go

```bash
go get go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploghttp
```

```go
exporter, _ := otlploghttp.New(context.Background(),
    otlploghttp.WithEndpointURL("http://localhost:5000/v1/logs"),
    otlploghttp.WithInsecure(),
)
provider := log.NewLoggerProvider(
    log.WithProcessor(log.NewBatchProcessor(exporter)),
)
global.SetLoggerProvider(provider)
```

### Java

```xml
<!-- pom.xml -->
<dependency>
  <groupId>io.opentelemetry</groupId>
  <artifactId>opentelemetry-exporter-otlp</artifactId>
</dependency>
```

```java
OtlpHttpLogRecordExporter exporter = OtlpHttpLogRecordExporter.builder()
    .setEndpoint("http://localhost:5000/v1/logs")
    .build();

SdkLoggerProvider provider = SdkLoggerProvider.builder()
    .addLogRecordProcessor(BatchLogRecordProcessor.builder(exporter).build())
    .build();

OpenTelemetrySdk.builder().setLoggerProvider(provider).buildAndRegisterGlobal();
```

### Ruby

```bash
gem install opentelemetry-exporter-otlp opentelemetry-sdk
```

```ruby
require 'opentelemetry/sdk'
require 'opentelemetry/exporter/otlp'

OpenTelemetry::SDK.configure do |c|
  c.add_span_processor(
    OpenTelemetry::SDK::Logs::Export::BatchLogRecordProcessor.new(
      OpenTelemetry::Exporter::OTLP::LogsExporter.new(
        endpoint: 'http://localhost:5000/v1/logs'
      )
    )
  )
end
```

### PHP

```bash
composer require open-telemetry/exporter-otlp open-telemetry/sdk
```

```php
$exporter = (new \OpenTelemetry\Contrib\Otlp\LogsExporter(
    \OpenTelemetry\Contrib\Otlp\OtlpUtil::createTransport('http://localhost:5000/v1/logs')
));

$provider = new \OpenTelemetry\SDK\Logs\LoggerProvider(
    new \OpenTelemetry\SDK\Logs\Processor\BatchLogRecordProcessor($exporter)
);

\OpenTelemetry\API\Logs\NoopLogger::setLoggerProvider($provider);
```

### Any other language / collector

Set your OTLP exporter endpoint to `http://localhost:5000/v1/logs` using HTTP/protobuf transport.
HTTP/JSON is also supported with `Content-Type: application/json`.

---

## Connecting your coding agent

### Claude Code

```bash
claude mcp add loggles --transport http http://localhost:5000/mcp
```

If an API key is configured, Claude Code negotiates authentication automatically via the OAuth
PKCE flow — no manual token setup required.

#### Debug skill

This repository ships a Claude Code skill (`.claude/skills/loggles-debug/`) that activates
automatically when you describe a bug, ask Claude to investigate an error, or want to observe
your app's runtime behaviour. It puts Claude in one of two modes:

**Investigative** (something is broken):
- Reads your source code first to understand the relevant service, map its log output, and
  identify error branches — then queries logs with that context
- Cross-references log evidence against source to confirm a root cause and point to a specific
  file and line
- Calls out missing instrumentation (`correlation_id` not propagated, IDs embedded in message
  strings) and explains the fix

**Exploratory** (observing runtime behaviour):
- Starts from the logs directly — no upfront source reading
- Tails live streams, traces requests by ID, and narrates what the service is actually doing
- Useful during development and testing: "what did my app do when I hit that endpoint?"

The skill is loaded automatically when you open this project in Claude Code. To use it in
**your own application's repository**, copy it:

```bash
mkdir -p /your-project/.claude/skills
cp -r /path/to/loggles/.claude/skills/loggles-debug /your-project/.claude/skills/
```

### Cursor

Add to `.cursor/mcp.json` in your project root:

```json
{
  "mcpServers": {
    "loggles": {
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

If an API key is configured, add an `Authorization: Bearer <key>` header in your MCP client
configuration.

---

## MCP tools

| Tool | Description |
|------|-------------|
| `search_logs` | Search with filters: time range, level, service, message text, structured properties. Supports pagination. |
| `get_log_by_id` | Retrieve a single log event by ID |
| `get_services` | List all source/service names that have emitted logs |
| `get_log_levels` | List distinct log levels present |
| `get_properties` | List distinct structured property keys |
| `get_property_values` | List distinct values for a property key within a time window |
| `get_log_stats` | Log counts grouped by level and service |
| `get_logs_by_trace_id` | Retrieve all logs sharing a trace/correlation ID |
| `get_related_logs` | Context window of logs around a specific event |
| `get_recent_errors` | Last N error/critical log events, optionally filtered by service |
| `tail_logs` | Most recent N log events |
| `get_log_rate` | Bucketed log counts over time — observe traffic volume and rhythm |
| `get_message_templates` | List distinct message templates, optionally filtered by service |
| `find_log_patterns` | Cluster messages by recurring pattern |
| `get_error_spikes` | Detect time buckets where error count exceeded a threshold |
| `audit_log_quality` | Report instrumentation coverage: missing templates, unstructured messages, broken down by service |
| `clear_logs` | Delete all log events from the store |

---

## Authentication

By default, with no key configured, all endpoints are open — zero friction for localhost use.

### No authentication (default)

Leave `Auth__ApiKey` unset. All endpoints are publicly accessible. Suitable for localhost.

### API key

```bash
# Docker
docker run ... -e Auth__ApiKey=your-secret-key ...

# dotnet run
export Auth__ApiKey=your-secret-key
dotnet run
```

Once set, all ingest and query endpoints require:

```
Authorization: Bearer your-secret-key
```

### MCP clients (Claude Code, Cursor, etc.)

MCP clients that support OAuth (such as Claude Code) negotiate authentication automatically.
When an API key is configured, Loggles exposes a local OAuth 2.0 + PKCE flow (RFC 6749 /
RFC 7636) that issues your API key as the access token. The client handles this handshake
transparently.

Discovery endpoints used by OAuth-aware clients:

| Endpoint | Description |
|----------|-------------|
| `GET /.well-known/oauth-authorization-server` | OAuth server metadata (RFC 8414) |
| `GET /.well-known/oauth-protected-resource` | Protected resource metadata |
| `POST /oauth/register` | Dynamic client registration (RFC 7591) |
| `GET /oauth/authorize` | Authorization endpoint |
| `POST /oauth/token` | Token endpoint — returns your configured API key |

The OAuth flow can be toggled with `Mcp__OAuthEnabled` (default: `true`). Set to `false` to
suppress discovery endpoints if your client does not support OAuth.

---

## Configuration

All settings can be overridden with environment variables using `__` as separator
(e.g. `Retention__Hours=24`).

| Setting | Default | Description |
|---------|---------|-------------|
| `Storage__Provider` | `sqlite` | Storage backend: `sqlite` or `postgres` |
| `Storage__ConnectionString` | `Data Source=logs.db` | SQLite connection string, or PostgreSQL connection string when using `postgres` provider |
| `Retention__Hours` | `48` | How long to keep logs |
| `Retention__PurgeIntervalMinutes` | `15` | How often to run cleanup |
| `Mcp__Enabled` | `true` | Enable/disable the MCP endpoint |
| `Mcp__OAuthEnabled` | `true` | Expose OAuth discovery and token endpoints for MCP clients |
| `Auth__ApiKey` | _(empty)_ | Static API key for Bearer token auth. If unset, auth is disabled |
| `SelfDiagnostics__Enabled` | `true` | Send Loggles' own logs back to itself via OTLP |
| `SelfDiagnostics__OtlpEndpoint` | `http://localhost:5000` | OTLP endpoint for self-diagnostics |

### PostgreSQL

```bash
docker run -d \
  -p 5000:5000 \
  -e Storage__Provider=postgres \
  -e Storage__ConnectionString="Host=your-host;Database=loggles;Username=loggles;Password=secret" \
  ghcr.io/bytesquashcom/loggles:latest
```

---

## REST API

Beyond MCP, a REST API is available for scripting or manual queries.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/v1/logs` | Ingest OTLP/HTTP logs (protobuf or JSON) |
| `POST` | `/search` | Search logs with filters |
| `GET` | `/logs/{id}` | Get log by ID |
| `GET` | `/meta/properties` | List distinct property keys |
| `GET` | `/stats/levels` | Log counts by level |

---

## License

[Elastic License 2.0](LICENSE) — free to use, self-host, and modify. You may not offer Loggles as a hosted or managed service to third parties.
