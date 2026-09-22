# Redis MCP Server

> [中文版 README](README.zh.md) | [English README](#readme)

A **read-only** Redis query server built with **.NET 10** and the official **Model Context Protocol (MCP)** SDK. It exposes the Redis *query* capabilities of [RedisDesktopManager](https://github.com/redis/RedisDesktopManager) as MCP tools over HTTP, plus a full REST API with Swagger UI for front-end integration.

- 🔍 **59 read-only tools** – keyspace browsing (`scan_keys`, `get_key`), all data types (string/hash/list/set/zset/stream/bitmap/HyperLogLog/Geo), server inspection (INFO/CONFIG/CLIENT LIST/SLOWLOG/LATENCY/MEMORY), cluster and pub/sub inspection.
- 🚫 **By design read-only** – no tool creates, modifies or deletes data. The architecture reserves explicit extension points for future write/delete tools (see [Architecture](#architecture)).
- 🌐 **Two transports from one process** – MCP over HTTP (streamable HTTP **and** legacy SSE on `/mcp`) and the same tools as a REST API on `/api/...` with Swagger UI at `/swagger` (available in **every** environment).
- 🏷️ **Multi-environment** – any number of Redis instances can be configured in `appsettings.json`; each environment's `ToolDescription` is injected into **every tool description** and into the global MCP `instructions`.
- 🖥️ **Explorer API for front-end UIs** – `GET /api/browse/databases|types|query` backs a visual Redis query page (see [Redis Explorer REST API](#redis-explorer-rest-api-for-front-end-uis)).
- ⚡ **Async & concurrent** – one multiplexed connection per environment (StackExchange.Redis), fully `async`, `CancellationToken`-aware, no `.Result` blocking.

---

## Quick start

```bash
dotnet restore
dotnet build RedisMcpServer.slnx
dotnet test RedisMcpServer.slnx
dotnet run --project src/RedisMcpServer.Api --no-build   # http://localhost:8081
```

Then open:

| URL | What |
|---|---|
| `http://localhost:8081/swagger` | Swagger UI for the REST API |
| `http://localhost:8081/api/tools` | Full tool catalog with parameters & JSON schemas |
| `http://localhost:8081/api/browse/databases` | Explorer API: databases + key counts, per-type counts, paginated query |
| `http://localhost:8081/api/environments` | Configured environments (no secrets) |
| `http://localhost:8081/mcp` | MCP endpoint – streamable HTTP + legacy SSE |
| `http://localhost:8081/api/health` | Connectivity status per environment |

The listener address comes from `"urls"` in `appsettings.json` (`http://localhost:8081` by default). Requires .NET SDK **10.0.x** (solution format `RedisMcpServer.slnx`).

---

## Configuration

Environments live in the `"Redis"` array of `appsettings.json`:

```json
{
  "urls": "http://localhost:8081",
  "Redis": [
    {
      "ToolDescription": "Stage环境的Name参数值为Stage",
      "Name": "Stage",
      "Connection": "xxx.yyy.local:6379,allowAdmin=false,defaultDatabase=6,password=xxx"
    }
  ]
}
```

- `Name` – environment identifier used as the `environment` tool argument (case-insensitive). The **first** entry is the default.
- `Connection` – a StackExchange.Redis connection string. Never exposed through any API; only host/port/hasPassword/defaultDatabase are shown.
- `ToolDescription` – free text that is automatically injected:
  1. into the `description` of **every** tool (as an "Environments" block), and
  2. into the global **MCP `instructions`** (shown by MCP clients during `initialize`).

`appsettings.Development.json` contains **no Redis configuration** (logging only) – all environments are configured in `appsettings.json` (or via User Secrets / environment variables, e.g. `Redis__0__Name`).

---

## Tool catalog (query-only)

Every tool takes an optional `environment` argument (the first configured environment is the default).

| Group | Tools |
|---|---|
| **Environment** | `list_environments`, `ping` |
| **Keys** | `dbsize`, `scan_keys`, `get_key`, `key_type`, `key_ttl`, `key_exists`, `key_random`, `key_memory`, `key_object` |
| **Strings** | `string_get`, `string_mget`, `string_strlen`, `string_getrange` |
| **Hashes** | `hash_getall`, `hash_get`, `hash_mget`, `hash_keys`, `hash_values`, `hash_len`, `hash_exists` |
| **Lists** | `list_range`, `list_len`, `list_index` |
| **Sets** | `set_members`, `set_card`, `set_ismember`, `set_randommember` |
| **Sorted Sets** | `zset_range`, `zset_rangebyscore`, `zset_rangebylex`, `zset_card`, `zset_score`, `zset_count` |
| **Streams** | `stream_len`, `stream_range`, `stream_read`, `stream_info` |
| **Bitmaps** | `bitmap_getbit`, `bitmap_count`, `bitmap_pos` |
| **HyperLogLog** | `hll_count` |
| **Geo** | `geo_pos`, `geo_dist`, `geo_hash`, `geo_search` |
| **Server** | `server_info`, `server_config`, `server_client_list`, `server_time`, `server_lastsave`, `server_slowlog`, `server_latency`, `server_memory` |
| **Cluster** | `cluster_info`, `cluster_nodes` |
| **Pub/Sub** | `pubsub_channels`, `pubsub_numsub` |

Result conventions:

- Every tool returns a JSON payload; errors are surfaced with `isError: true` and preserve the real Redis message (e.g. `WRONGTYPE`, `WRONGPASS`, timeouts).
- Key enumeration uses non-blocking **SCAN** (`scan_keys` with cursor paging) – never `KEYS`.
- Collection values (hash/list/set/zset/stream) are capped (`limit`, default 500 entries) and long strings truncated (`maxValueChars`, default 10 000) to keep responses fast.
- Binary values that are not valid UTF-8 come back as `"base64:..."`.

---

## REST API

`GET /api/tools` returns the whole catalog (group, enriched description, parameters, input schema). `POST /api/tools/{name}/invoke` runs a tool – the same single execution path as the MCP handler:

```bash
curl -X POST http://localhost:8081/api/tools/scan_keys/invoke \
  -H 'Content-Type: application/json' \
  -d '{"environment":"Local","arguments":{"pattern":"user:*","count":100}}'
```

Response mirrors the MCP `tools/call` shape:

```json
{
  "isError": false,
  "data": { "cursor": 0, "keys": ["user:1", "user:2"], "count": 100, "hasMore": false },
  "text": "{ ... }",        // human-readable JSON text (what MCP clients receive)
  "durationMs": 12,
  "error": null
}
```

| Endpoint | Description |
|---|---|
| `GET /api/health` | Server health + per-environment connectivity |
| `GET /api/environments` | Configured environments (no passwords/connection strings) |
| `GET /api/tools` | Full catalog with schemas |
| `GET /api/tools/{name}` | One tool's documentation (404 if unknown) |
| `POST /api/tools/{name}/invoke` | Invoke one tool |

---

## Redis Explorer REST API (for front-end UIs)

Three dedicated endpoints power a visual Redis query UI. They are protocol-neutral (served by `IRedisBrowseService` in `RedisMcpServer.Core`) and fully async. Every result is JSON (camelCase).

### 1. All databases + key counts

```
GET /api/browse/databases?environment=Local
```

```json
[
  { "db": 0, "keys": 1280, "expires": 42 },
  { "db": 1, "keys": 0,    "expires": 0 },
  ...
]
```

`keys` = DBSIZE of that logical database, `expires` = keys with a TTL (from `INFO keyspace`). The database count comes from `CONFIG GET databases` (default 16).

### 2. Key counts per data type in one database

```
GET /api/browse/types?environment=Local&db=3
```

```json
{ "db": 3, "total": 25, "types": [ { "type": "hash", "count": 23 }, { "type": "string", "count": 1 } ] }
```

Based on a full non-blocking SCAN + `TYPE`; counts are exact for the scanned keyspace.

### 3. Paginated key/value Query API

```
GET /api/browse/query?environment=Local&db=3&type=hash&key=claim&value=vip&order=desc&pageSize=50&pageNumber=1
```

All query parameters are **optional**:

| Parameter | Meaning | Default |
|---|---|---|
| `environment` | Environment name | default environment |
| `db` | Logical database (0..N) | environment's default db |
| `type` | Exact data type (string/hash/list/set/zset/stream/...) | all types |
| `key` | Case-insensitive substring of the key | all keys |
| `value` | Case-insensitive substring of the rendered value | all values |
| `order` | Time sort: `asc` or `desc` | `desc` (newest first) |
| `pageSize` | Page size (1..500) | `50` |
| `pageNumber` | 1-based page | `1` |

Response:

```json
{
  "pageNumber": 1,
  "pageSize": 50,
  "total": 23,
  "totalPages": 1,
  "environment": "Local",
  "db": 3,
  "items": [
    {
      "createdAt": "2026-03-19T06:03:23.0000000Z",
      "type": "hash",
      "key": "ClaimDiscountBonus:Progresses:1995:3988528",
      "value": "{\"ed2b106a...\": \"2026-03-19 14:03:23\", ...}",
      "ttlSeconds": null,
      "idleSeconds": 3600,
      "lastAccessAt": "2026-09-21T14:11:26.0000000Z",
      "accessFrequency": 5
    }
  ]
}
```

Per-row fields:

| Field | Meaning |
|---|---|
| `createdAt` | **Estimate** of the creation time (`now − OBJECT IDLETIME`). Redis stores **no** real creation timestamp, and on Redis 6.0 servers with an LFU `maxmemory-policy` the idle time is unavailable or minute-coarse, so this is **frequently `null`** for hot keys. |
| `type` / `key` / `value` | Data type, key name, bounded rendered value (first 20 elements, elements ≤200 chars, strings ≤2000). |
| `ttlSeconds` | Remaining lifetime in whole seconds; **`null` = no expiration** (TTL −1). |
| `idleSeconds` | Raw `OBJECT IDLETIME`; `null` when the server (e.g. Redis 6.0 + LFU) does not provide it. |
| `lastAccessAt` | Approximate last access (`now − idleSeconds`); `null` when idle time is unavailable. |
| `accessFrequency` | Raw `OBJECT FREQ` (LFU access-frequency counter); `null` on LRU-policy servers. |

> **`createdAt` / `ttlSeconds` are commonly `null` and that is expected**: creation time does not exist in Redis (the estimate degrades gracefully), and TTL is only present for keys that actually expire. Rows without a `createdAt` sort last in both orders. For lossless binary rendering (base64), use the `get_key` tool; `key_object` exposes the same OBJECT metadata per key.

> **Cost note.** Filtering and sorting are done in-memory over a full non-blocking SCAN of the selected database (with an optional `TYPE` filter), so runtimes scale with keyspace size – appropriate for explorer UIs; huge keyspaces should be narrowed with `type`/`key` filters.

Errors come back as HTTP **400** with `application/problem+json` (`{"title": "...", "detail": "..."}`) carrying the real Redis message (WRONGTYPE, timeouts, unknown environment, invalid db range, ...).

---

## MCP over HTTP

The endpoint `/mcp` speaks both:

- **Streamable HTTP** – `POST /mcp` with `Accept: application/json, text/event-stream` (sessions via the `Mcp-Session-Id` header).
- **Legacy SSE** – `GET /mcp` (SSE stream) + `POST /mcp` for messages, for clients built against the 2024-11-05 transport.

Quick protocol smoke test:

```bash
curl -X POST http://localhost:8081/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}'
```

The `initialize` result contains `instructions` (the global guide with every environment's `ToolDescription`) and the `tools` capability; `tools/list` returns the 59 tools with parameterized JSON schemas; `tools/call` executes through the same `IToolInvoker` used by the REST API, so errors are never swallowed into generic text.

---

## Architecture

```
┌────────────────────────────────────────────────────────────────┐
│ src/RedisMcpServer.Api   ASP.NET Core host                     │
│   Program.cs             DI wiring, /mcp (MCP HTTP transport), │
│                          /api + Swagger                        │
│   Mcp/RedisMcpGateway    tools/list + tools/call handlers      │
│   Api/ToolApiEndpoints   REST endpoint definitions             │
│   Api/BrowseApiEndpoints explorer endpoints (browse APIs)      │
├────────────────────────────────────────────────────────────────┤
│ src/RedisMcpServer.Tools   Tool implementations               │
│   [McpToolGroup] classes with [McpTool] methods                │
│   (EnvironmentTools, KeyTools, SimpleTypeTools,                │
│    ComplexTypeTools, ServerTools, ClusterPubSubTools)          │
├────────────────────────────────────────────────────────────────┤
│ src/RedisMcpServer.Core    Protocol-neutral framework           │
│   Tools/   ITool, ToolDiscovery (reflection scan),             │
│            ToolRegistry, ToolInvoker (single execution path),  │
│            ToolSchemaBuilder, InstructionsBuilder              │
│   Browse/  IRedisBrowseService, RedisBrowseService, KeyQuery   │
│   Redis/   RedisConnectionProvider (one multiplexer per env,   │
│            lazy + thread-safe, failure eviction),              │
│            RedisToolContext, RedisReadExtensions               │
│   Environment/ RedisEnvironmentResolver (validate + resolve)   │
│   Json/    JSON helpers for Redis results (binary → base64)    │
└────────────────────────────────────────────────────────────────┘
```

Key design decisions:

- **One execution path.** `IToolInvoker` is the *only* way tools run: the MCP `tools/call` handler and `POST /api/tools/{name}/invoke` both call it. Behavior is identical and testable without a server.
- **Custom MCP handlers.** `ListToolsHandler`/`CallToolHandler` are fully controlled (instead of the SDK's `McpServerTool` machinery) so real Redis error text flows back to the client (`isError: true`) instead of a generic invocation message.
- **Auto-discovery.** Reflection over `[McpToolGroup]`/`[McpTool]` attributes means a new tool is just a new method (or class). The registry rejects duplicate names at startup.
- **Per-environment multiplexer.** One `ConnectionMultiplexer` per environment, created lazily and shared by all concurrent requests (`ConcurrentDictionary` + `Lazy` with `ExecutionAndPublication`); failed creation is evicted and retried. Connections enable the client-side `AllowAdmin` bit so RDM-style inspections (`INFO`, `CONFIG GET`, `CLIENT LIST`, `SLOWLOG`, `CLUSTER *`) can be queried – Redis itself allows these for any client; the tool layer still only ever issues read commands, never admin/write commands (no `FLUSH*`, no `SHUTDOWN`, no `CONFIG SET`...).
- **Extension points for write tools.** The tool abstraction, discovery, registry, schema builder and invocation path need **no** structural change to add creating/modifying/deleting tools: implement a new `[McpTool]` method (or class), and it automatically appears in MCP, REST and the instructions. A future release can also introduce a per-tool "read/write" capability flag.
- **Performance.** Everything is `async` throughout (no `.Result`/`.Wait()`), handlers observe `CancellationToken`, collection rendering is bounded, and SCAN-based enumeration never blocks the server.

---

## Security notes

- Read-only by design – the only commands issued are queries; the connection string also sets `allowAdmin=false` by default (the client-side `AllowAdmin` bit is enabled internally only so `INFO`/`CONFIG GET`/`CLIENT LIST`/`SLOWLOG`/`CLUSTER *` reads work like RDM – no admin or write command is ever sent).
- Connection strings and passwords are **never** exposed through MCP or REST (only host/port/defaultDatabase/hasPassword are shown).
- Endpoints are plain HTTP by default; put the server behind a TLS reverse proxy in production and restrict access to trusted clients.

## License

MIT – see [LICENSE](LICENSE).
