using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Infrastructure.Idempotency;

/// <summary>
/// Enforces the Idempotency-Key contract from section 9: apply to every POST action that
/// creates or posts something. Same key + same request body replays the stored response;
/// same key + a different body is rejected with IDEMPOTENCY_KEY_REUSED. Records are kept
/// for 7 days (platform.IdempotencyRecord, cleaned up by the Worker's daily job).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var keyHeader) ||
            string.IsNullOrWhiteSpace(keyHeader))
        {
            context.Result = ProblemResult(400, ErrorCodes.ValidationFailed, $"The '{HeaderName}' header is required.");
            return;
        }

        var key = keyHeader.ToString();
        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
        var unitOfWork = httpContext.RequestServices.GetRequiredService<IUnitOfWork>();
        var clock = httpContext.RequestServices.GetRequiredService<IClock>();

        httpContext.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
            httpContext.Request.Body.Position = 0;
        }

        var requestHash = Sha256Hex($"{httpContext.Request.Method}:{httpContext.Request.Path}:{body}");

        var existing = await FindExistingAsync(unitOfWork, currentUser.TenantId, key, httpContext.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                context.Result = ProblemResult(422, ErrorCodes.IdempotencyKeyReused, "This Idempotency-Key was already used with a different request.");
                return;
            }

            context.Result = new ContentResult
            {
                StatusCode = existing.ResponseStatus,
                Content = existing.ResponseBody,
                ContentType = "application/json",
            };
            return;
        }

        var executedContext = await next();

        if (executedContext.Result is ObjectResult { Value: not null } objectResult)
        {
            var statusCode = objectResult.StatusCode ?? 200;
            var responseBody = JsonSerializer.Serialize(objectResult.Value);
            await InsertRecordAsync(
                unitOfWork, currentUser.TenantId, currentUser.UserId, key,
                httpContext.Request.Path, requestHash, statusCode, responseBody,
                clock.UtcNow, httpContext.RequestAborted);
        }
    }

    private static async Task<ExistingRecord?> FindExistingAsync(
        IUnitOfWork unitOfWork, Guid tenantId, string key, CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = """
            SELECT RequestHash, ResponseStatus, ResponseBody
            FROM platform.IdempotencyRecord
            WHERE TenantId = @tenantId AND IdempotencyKey = @key;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@key", key));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ExistingRecord(
            reader.GetString(0),
            reader.IsDBNull(1) ? 200 : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task InsertRecordAsync(
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid userId,
        string key,
        string requestPath,
        string requestHash,
        int responseStatus,
        string responseBody,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = """
            INSERT platform.IdempotencyRecord
                (TenantId, IdempotencyKey, UserId, RequestPath, RequestHash, ResponseStatus, ResponseBody, ExpiresAtUtc)
            VALUES
                (@tenantId, @key, @userId, @requestPath, @requestHash, @responseStatus, @responseBody, @expiresAtUtc);
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@key", key));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        command.Parameters.Add(new SqlParameter("@requestPath", requestPath));
        command.Parameters.Add(new SqlParameter("@requestHash", requestHash));
        command.Parameters.Add(new SqlParameter("@responseStatus", responseStatus));
        command.Parameters.Add(new SqlParameter("@responseBody", responseBody));
        command.Parameters.Add(new SqlParameter("@expiresAtUtc", nowUtc.AddDays(7)));

        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ObjectResult ProblemResult(int status, string code, string detail) => new(new ProblemDetails
    {
        Type = $"https://errors.stockorder.app/{code}",
        Title = code,
        Status = status,
        Detail = detail,
        Extensions = { ["code"] = code },
    })
    { StatusCode = status };

    private sealed record ExistingRecord(string RequestHash, int ResponseStatus, string? ResponseBody);
}
