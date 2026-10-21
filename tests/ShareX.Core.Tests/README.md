# Shared core regression tests

Standalone .NET 10 console project; deliberately excluded from the solution.

```bash
source scripts/linux/env.sh
unset DISPLAY WAYLAND_DISPLAY
dotnet run --project tests/ShareX.Core.Tests -c Release
```

An optional argument filters test names. `--findings` runs bounded reproductions of
unfixed directory-link, OAuth query, Unicode encoding/allocation, and unexpected
uploader response findings instead of the passing regression suite:

```bash
dotnet run --project tests/ShareX.Core.Tests -c Release -- --findings
```

HTTP tests replace the factory's cached clients with in-memory message handlers.
They run serially and restore the factory after each transport test. No sockets,
DNS requests, upload services, displays, or application startup are involved.
All files (including logs and SQLite databases) use a private temporary directory
removed when the runner finishes. History reads bypass the UI error wrapper.

The streaming allocation measurement excludes the fixture's preallocated 8 MiB
payload. It reports process-wide managed allocations during the operation, not
peak RSS, total application memory, or a network throughput benchmark. The test
asserts streaming behavior and exact file length, not an unstable allocation or
time threshold.
