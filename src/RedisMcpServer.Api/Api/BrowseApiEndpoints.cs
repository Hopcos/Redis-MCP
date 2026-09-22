using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RedisMcpServer.Core.Browse;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Api.Api;

/// <summary>
/// REST endpoints backing the front-end Redis explorer UI:
/// databases + key counts, per-type key counts, and a paginated key/value Query API.
/// </summary>
public static class BrowseApiEndpoints
{
    public static void MapBrowseApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/browse").WithTags("Browse");

        group.MapGet("/databases", async (
                IRedisBrowseService service,
                [FromQuery] string? environment,
                CancellationToken cancellationToken) =>
            await GuardAsync(() => service.ListDatabasesAsync(environment, cancellationToken)));

        group.MapGet("/types", async (
                IRedisBrowseService service,
                [FromQuery] string? environment,
                [FromQuery] int? db,
                CancellationToken cancellationToken) =>
            await GuardAsync(() => service.CountTypesAsync(environment, db, cancellationToken)));

        group.MapGet("/query", async (
                IRedisBrowseService service,
                [FromQuery] string? environment,
                [FromQuery] int? db,
                [FromQuery] string? type,
                [FromQuery] string? key,
                [FromQuery] string? value,
                [FromQuery] string order = "desc",
                [FromQuery] int pageSize = KeyQuery.DefaultPageSize,
                [FromQuery] int pageNumber = 1,
                CancellationToken cancellationToken = default) =>
            await GuardAsync(() => service.QueryAsync(new KeyQuery
            {
                Environment = environment,
                Db = db,
                Type = type,
                KeyPattern = key,
                ValuePattern = value,
                Order = order,
                PageSize = pageSize,
                PageNumber = pageNumber,
            }, cancellationToken)));
    }

    /// <summary>Runs a browse operation and converts tool/Redis errors into HTTP 400 problem details.</summary>
    private static async Task<IResult> GuardAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation().ConfigureAwait(false));
        }
        catch (ToolExecutionException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (StackExchange.Redis.RedisException ex)
        {
            // Commands issued after a lazy connect can fail with RedisConnectionException even when
            // the multiplexer itself was obtained successfully (AbortOnConnectFail=false).
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (TimeoutException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
    }
}
