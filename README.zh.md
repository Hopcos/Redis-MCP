# Redis MCP Server

> [English README](README.md) | [中文版 README](#readme)

基于 **.NET 10** 与官方 **Model Context Protocol (MCP) SDK** 实现的 **只读** Redis 查询服务器。把 [RedisDesktopManager](https://github.com/redis/RedisDesktopManager) 的 Redis **查询**能力以 MCP 工具的形式通过 HTTP 暴露，同时提供完整 REST API 与 Swagger UI 供前端集成。

- 🔍 **59 个只读工具**——键空间浏览（`scan_keys`、`get_key`）、全数据类型（string/hash/list/set/zset/stream/bitmap/HyperLogLog/Geo）、服务器巡检（INFO/CONFIG/CLIENT LIST/SLOWLOG/LATENCY/MEMORY）、集群与发布订阅巡检。
- 🚫 **设计上只读**——没有任何工具会创建、修改或删除数据。架构已为将来新增写/删工具预留扩展点（见 [架构](#架构)）。
- 🌐 **单进程双传输**——MCP over HTTP（streamable HTTP **和** legacy SSE，同一 `/mcp` 端点），同时把相同工具暴露为 `/api/...` REST API，并在 `/swagger` 提供 Swagger UI（**任意环境**都可用）。
- 🏷️ **多环境**——可在 `appsettings.json` 中配置任意数量的 Redis 实例；每个环境的 `ToolDescription` 会被注入到**每个工具的描述**以及全局 MCP `instructions` 中。
- 🖥️ **面向前端 UI 的浏览 API**——`GET /api/browse/databases|types|query` 支撑可视化 Redis 查询页面（见 [Redis 可视化浏览 REST API](#redis-可视化浏览-rest-api供前端-ui-集成)）。
- ⚡ **异步与并发**——每环境一条多路复用连接（StackExchange.Redis），全程 `async`、支持 `CancellationToken`，无 `.Result` 阻塞。

---

## 快速开始

```bash
dotnet restore
dotnet build RedisMcpServer.slnx
dotnet test RedisMcpServer.slnx
dotnet run --project src/RedisMcpServer.Api --no-build   # http://localhost:8081
```

然后访问：

| 地址 | 内容 |
|---|---|
| `http://localhost:8081/swagger` | REST API 的 Swagger UI |
| `http://localhost:8081/api/tools` | 全部工具目录（含参数与 JSON schema） |
| `http://localhost:8081/api/browse/databases` | 浏览 API：db 列表与数据量、类型统计、分页查询 |
| `http://localhost:8081/api/environments` | 已配置的环境（不暴露任何密钥） |
| `http://localhost:8081/mcp` | MCP 端点（streamable HTTP + legacy SSE） |
| `http://localhost:8081/api/health` | 各环境的连接状态 |

监听地址来自 `appsettings.json` 的 `"urls"`（默认 `http://localhost:8081`）。需要 .NET SDK **10.0.x**（解决方案为 `RedisMcpServer.slnx` 格式）。

---

## 配置

环境配置在 `appsettings.json` 的 `"Redis"` 数组中：

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

- `Name`——环境标识，作为工具的 `environment` 参数值（不区分大小写）。**第一个**条目为默认环境。
- `Connection`——StackExchange.Redis 连接串。任何 API 都不会泄露它，仅展示 host/port/hasPassword/defaultDatabase。
- `ToolDescription`——自由文本，会被自动注入：
  1. 每个工具的 `description`（以 "Environments" 区块形式），以及
  2. 全局 **MCP `instructions`**（MCP 客户端在 `initialize` 时可见）。

`appsettings.Development.json` **不包含任何 Redis 配置**（仅 Logging）——所有环境一律在 `appsettings.json` 中配置（也可用 User Secrets 或环境变量覆盖，如 `Redis__0__Name`）。

---

## 工具目录（仅查询）

每个工具都带可选的 `environment` 参数（缺省使用第一个配置的环境）。

| 分组 | 工具 |
|---|---|
| **Environment** | `list_environments`、`ping` |
| **Keys** | `dbsize`、`scan_keys`、`get_key`、`key_type`、`key_ttl`、`key_exists`、`key_random`、`key_memory`、`key_object` |
| **Strings** | `string_get`、`string_mget`、`string_strlen`、`string_getrange` |
| **Hashes** | `hash_getall`、`hash_get`、`hash_mget`、`hash_keys`、`hash_values`、`hash_len`、`hash_exists` |
| **Lists** | `list_range`、`list_len`、`list_index` |
| **Sets** | `set_members`、`set_card`、`set_ismember`、`set_randommember` |
| **Sorted Sets** | `zset_range`、`zset_rangebyscore`、`zset_rangebylex`、`zset_card`、`zset_score`、`zset_count` |
| **Streams** | `stream_len`、`stream_range`、`stream_read`、`stream_info` |
| **Bitmaps** | `bitmap_getbit`、`bitmap_count`、`bitmap_pos` |
| **HyperLogLog** | `hll_count` |
| **Geo** | `geo_pos`、`geo_dist`、`geo_hash`、`geo_search` |
| **Server** | `server_info`、`server_config`、`server_client_list`、`server_time`、`server_lastsave`、`server_slowlog`、`server_latency`、`server_memory` |
| **Cluster** | `cluster_info`、`cluster_nodes` |
| **Pub/Sub** | `pubsub_channels`、`pubsub_numsub` |

结果约定：

- 每个工具返回 JSON 负载；出错时以 `isError: true` 呈现，并保留 Redis 真实错误信息（如 `WRONGTYPE`、`WRONGPASS`、超时）。
- 键枚举使用非阻塞 **SCAN**（`scan_keys` 游标分页），绝不用 `KEYS`。
- 集合类值（hash/list/set/zset/stream）有上限（`limit`，默认 500 条），长字符串会被截断（`maxValueChars`，默认 10 000）。
- 非 UTF-8 的二进制值以 `"base64:..."` 形式返回。

---

## REST API

`GET /api/tools` 返回完整目录（分组、增强描述、参数、输入 schema）。`POST /api/tools/{name}/invoke` 执行工具——与 MCP handler 走同一条执行路径：

```bash
curl -X POST http://localhost:8081/api/tools/scan_keys/invoke \
  -H 'Content-Type: application/json' \
  -d '{"environment":"Local","arguments":{"pattern":"user:*","count":100}}'
```

响应结构与 MCP `tools/call` 一致：

```json
{
  "isError": false,
  "data": { "cursor": 0, "keys": ["user:1", "user:2"], "count": 100, "hasMore": false },
  "text": "{ ... }",        // 人类可读的 JSON 文本（即 MCP 客户端收到的内容）
  "durationMs": 12,
  "error": null
}
```

| 端点 | 说明 |
|---|---|
| `GET /api/health` | 服务健康状态 + 各环境连通性 |
| `GET /api/environments` | 已配置环境（不含密码/连接串） |
| `GET /api/tools` | 完整目录与 schema |
| `GET /api/tools/{name}` | 单个工具文档（未知返回 404） |
| `POST /api/tools/{name}/invoke` | 调用一个工具 |

---

## Redis 可视化浏览 REST API（供前端 UI 集成）

三个专用端点支撑可视化 Redis 查询界面。它们是协议无关的（由 `RedisMcpServer.Core` 中的 `IRedisBrowseService` 提供），全程异步，返回 camelCase JSON。

### 1. 获取所有 db 及数据量

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

`keys` 为该逻辑 db 的 DBSIZE，`expires` 为带 TTL 的键数（来自 `INFO keyspace`）。db 总数取自 `CONFIG GET databases`（默认 16）。

### 2. 获取 db 下不同类型的数据量

```
GET /api/browse/types?environment=Local&db=3
```

```json
{ "db": 3, "total": 25, "types": [ { "type": "hash", "count": 23 }, { "type": "string", "count": 1 } ] }
```

基于全量非阻塞 SCAN + `TYPE` 统计，所选键空间内计数精确。

### 3. 分页 key-value Query API

```
GET /api/browse/query?environment=Local&db=3&type=hash&key=claim&value=vip&order=desc&pageSize=50&pageNumber=1
```

所有查询条件**均为可选**：

| 参数 | 含义 | 默认 |
|---|---|---|
| `environment` | 环境名 | 默认环境 |
| `db` | 逻辑数据库（0..N） | 环境的默认 db |
| `type` | 数据类型（string/hash/list/set/zset/stream/...） | 全部类型 |
| `key` | key 模糊匹配（不区分大小写的子串） | 全部 key |
| `value` | value 模糊匹配（对渲染后的 value 做不区分大小写子串匹配） | 全部 value |
| `order` | 时间排序：`asc` 或 `desc` | `desc`（新的在前） |
| `pageSize` | 每页条数（1..500） | `50` |
| `pageNumber` | 页码（从 1 开始） | `1` |

响应：

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

行字段说明：

| 字段 | 含义 |
|---|---|
| `createdAt` | **创建时间的估算值**（`当前时间 − OBJECT IDLETIME`）。Redis **不保存**真实创建时间；且在 Redis 6.0 + LFU `maxmemory-policy` 的服务器上，空闲时间不可用或仅为分钟粒度，因此热键上该值**经常为 `null`**。 |
| `type` / `key` / `value` | 数据类型、键名、有界渲染的 value（前 20 个元素、元素 ≤200 字符、字符串 ≤2000）。 |
| `ttlSeconds` | 剩余存活秒数；**`null` 表示永不过期**（TTL 为 -1）。 |
| `idleSeconds` | 原始 `OBJECT IDLETIME` 值；服务器不支持时（如 Redis 6.0 + LFU）为 `null`。 |
| `lastAccessAt` | 近似最后访问时间（`当前时间 − idleSeconds`）；空闲时间不可用时为 `null`。 |
| `accessFrequency` | 原始 `OBJECT FREQ`（LFU 访问频率计数器）；LRU 策略服务器上为 `null`。 |

> **`createdAt` / `ttlSeconds` 为 `null` 属于正常现象**：Redis 没有创建时间（估算失败时优雅降级），TTL 只存在于真正设置了过期的键上。没有 `createdAt` 的行在两种排序下都排在最后。如需无损二进制渲染（base64），请使用 `get_key` 工具；`key_object` 工具可查看单键的同类 OBJECT 元数据。

> **成本说明。** 过滤与排序在内存中基于所选 db 的全量非阻塞 SCAN 完成（可用 `TYPE` 过滤），耗时随键空间规模增长——适合浏览器界面；超大键空间建议先用 `type`/`key` 缩小范围。

错误统一以 HTTP **400** + `application/problem+json` 返回（`{"title": "...", "detail": "..."}`），携带真实 Redis 消息（WRONGTYPE、超时、未知环境、db 越界等）。

---

## MCP over HTTP

`/mcp` 端点同时支持：

- **Streamable HTTP**——`POST /mcp`，`Accept: application/json, text/event-stream`（会话通过 `Mcp-Session-Id` 头）。
- **Legacy SSE**——`GET /mcp`（SSE 流）+ `POST /mcp` 提交消息，兼容 2024-11-05 传输协议的客户端。

协议冒烟测试：

```bash
curl -X POST http://localhost:8081/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}'
```

`initialize` 结果包含 `instructions`（包含每个环境 `ToolDescription` 的全局指南）与 `tools` 能力；`tools/list` 返回 59 个带参数化 JSON schema 的工具；`tools/call` 通过 REST 同一 `IToolInvoker` 执行，错误不会被吞成通用文案。

---

## 架构

```
┌────────────────────────────────────────────────────────────────┐
│ src/RedisMcpServer.Api   ASP.NET Core 宿主                      │
│   Program.cs             DI 装配、/mcp（MCP HTTP 传输）、        │
│                          /api + Swagger                        │
│   Mcp/RedisMcpGateway    tools/list + tools/call 处理器        │
│   Api/ToolApiEndpoints   REST 端点定义                         │
│   Api/BrowseApiEndpoints 浏览端点（browse API）                 │
├────────────────────────────────────────────────────────────────┤
│ src/RedisMcpServer.Tools   工具实现                            │
│   带 [McpToolGroup] 的类 + [McpTool] 方法                       │
│   (EnvironmentTools, KeyTools, SimpleTypeTools,                │
│    ComplexTypeTools, ServerTools, ClusterPubSubTools)          │
├────────────────────────────────────────────────────────────────┤
│ src/RedisMcpServer.Core   协议无关框架                         │
│   Tools/   ITool、ToolDiscovery（反射扫描）、                   │
│            ToolRegistry、ToolInvoker（单一执行路径）、           │
│            ToolSchemaBuilder、InstructionsBuilder              │
│   Browse/  IRedisBrowseService、RedisBrowseService、KeyQuery   │
│   Redis/   RedisConnectionProvider（每环境一个多路复用连接，    │
│            lazy + 线程安全、失败逐出重试）、                     │
│            RedisToolContext、RedisReadExtensions               │
│   Environment/ RedisEnvironmentResolver（校验 + 解析）          │
│   Json/    Redis 结果 JSON 助手（二进制 → base64）              │
└────────────────────────────────────────────────────────────────┘
```

关键设计决策：

- **单一执行路径。** `IToolInvoker` 是工具执行的唯一入口：MCP `tools/call` 处理器与 `POST /api/tools/{name}/invoke` 都调用它，行为完全一致且无需真实服务器即可测试。
- **自定义 MCP 处理器。** 完全接管 `ListToolsHandler`/`CallToolHandler`（而非 SDK 的 `McpServerTool` 机制），让真实 Redis 错误文本原样返回客户端（`isError: true`），而不是通用调用失败消息。
- **自动发现。** 通过反射扫描 `[McpToolGroup]`/`[McpTool]` 特性，新增一个工具 = 新增一个方法（或类）；注册表启动时拒绝重名。
- **每环境多路复用连接。** 每个环境一个 `ConnectionMultiplexer`，lazy 创建并共享给所有并发请求（`ConcurrentDictionary` + `Lazy`（`ExecutionAndPublication`））；创建失败会被逐出并重试。连接层会开启客户端 `AllowAdmin` 位，以便像 RDM 一样查询 `INFO`、`CONFIG GET`、`CLIENT LIST`、`SLOWLOG`、`CLUSTER *`——Redis 本身允许任意客户端执行这些查询；工具层依然只下发只读命令，绝不调用管理员/写命令（无 `FLUSH*`、无 `SHUTDOWN`、无 `CONFIG SET`……）。
- **写工具的扩展点。** 工具抽象、发现、注册表、schema 构建与调用路径**无需结构性改动**即可加入创建/修改/删除工具：实现新的 `[McpTool]` 方法（或类）后，它会自动出现在 MCP、REST 与 instructions 中；未来版本还可以为每个工具增加"读/写"能力标记。
- **性能。** 全程 `async`（无 `.Result`/`.Wait()`），处理器可观察 `CancellationToken`，集合渲染有界，SCAN 枚举绝不阻塞服务器。

---

## 安全说明

- 设计上只读——只会下发查询命令；连接串默认 `allowAdmin=false`（客户端 `AllowAdmin` 位仅在内部开启，以便像 RDM 一样执行 `INFO`/`CONFIG GET`/`CLIENT LIST`/`SLOWLOG`/`CLUSTER *` 读取——绝不发送任何管理员或写命令）。
- 连接串与密码**永不**通过 MCP 或 REST 暴露（只展示 host/port/defaultDatabase/hasPassword）。
- 默认是明文 HTTP；生产环境请放在 TLS 反向代理之后，并限制访问来源为可信客户端。

## License

MIT——见 [LICENSE](LICENSE)。
