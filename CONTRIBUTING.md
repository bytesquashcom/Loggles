# Contributing to Loggles

Thanks for your interest in contributing. This document covers how to set up a local development environment, run tests, and submit changes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/) (for PostgreSQL integration tests)
- Git

## Development setup

```bash
git clone https://github.com/bytesquashcom/Loggles.git
cd Loggles/src/Loggles.Api
dotnet run
# Listening on http://localhost:5000
```

## Running tests

```bash
cd Loggles
dotnet test src/Loggles.Tests/Loggles.Tests.csproj
```

PostgreSQL integration tests require Docker. Testcontainers will spin up a container automatically. If Docker is unavailable, those tests are skipped.

## Project structure

```
src/
  Loggles.Api/           Controllers, MCP tools, auth, startup
  Loggles.Core/          Domain models, DTOs, interfaces
  Loggles.Infrastructure/ SQLite/PostgreSQL stores, background services
  Loggles.Tests/         xUnit integration + unit tests
  Loggles.Demo.Otel/     Example OTLP client app
```

## Commit messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add Redis storage backend
fix: tail_logs ignoring from/to parameters
docs: add PHP integration example
perf: reduce MCP response token size
```

## Submitting changes

1. Fork the repository and create a branch from `main`
2. Make your changes with tests
3. Ensure `dotnet test` passes
4. Open a pull request against `main`

## Reporting bugs

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md) when opening an issue.

## Code style

- Nullable reference types enabled — no `!` suppressions without a comment
- Async all the way down in I/O paths
- XML doc comments on public API surface
- No top-level statements; use Controllers over Minimal API
