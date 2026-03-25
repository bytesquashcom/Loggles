# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-03-22

### Added
- OTLP/HTTP log ingestion endpoint (`/v1/logs`) supporting protobuf and JSON
- SQLite storage backend with configurable retention and auto-purge
- PostgreSQL storage backend for persistent/multi-process deployments
- MCP server at `/mcp` (SSE transport) exposing 17 tools for log querying and analysis
- `search_logs` — full-text and structured property search with pagination
- `get_log_by_id`, `get_logs_by_trace_id`, `get_related_logs` — single-event and trace retrieval
- `get_services`, `get_log_levels`, `get_properties`, `get_property_values` — schema discovery
- `get_log_stats`, `get_log_rate`, `get_recent_errors` — aggregation and monitoring
- `get_message_templates`, `find_log_patterns`, `get_error_spikes` — pattern analysis
- `audit_log_quality` — instrumentation coverage report per service
- `tail_logs` — most recent N events with optional time window
- `clear_logs` — delete all stored events
- REST API for scripting: `/search`, `/logs/{id}`, `/meta/properties`, `/stats/levels`
- API key authentication via `Auth__ApiKey` environment variable
- OAuth 2.0 + PKCE flow for MCP clients (RFC 6749, RFC 7636, RFC 8414)
- Dynamic client registration (RFC 7591) for MCP Inspector and compatible clients
- Self-diagnostics: Loggles can send its own logs to itself via OTLP
- Elastic License 2.0 (ELv2)
- Docker image published to `ghcr.io/bytesquashcom/loggles`
- Multi-stage Alpine-based Dockerfile for minimal image size
- Claude Code debug skill (`loggles-debug`) for investigative and exploratory runtime analysis
- Support for .NET, Node.js, Python, Go, Java, Ruby, PHP OTLP exporters

### Fixed
- `audit_log_quality` null crash when service name is absent
- `tail_logs` `from`/`to` time window parameters not applied
- `WWW-Authenticate` header added to 401 responses for MCP Inspector OAuth discovery

[Unreleased]: https://github.com/bytesquashcom/Loggles/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/bytesquashcom/Loggles/releases/tag/v0.5.0
