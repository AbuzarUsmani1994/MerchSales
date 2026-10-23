using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Infrastructure.Api;

/// <summary>Maps a handler's <see cref="Result"/> to the RFC 9457 ProblemDetails shape from section 9.</summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : ToProblem(result.Error);

    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : ToProblem(result.Error);

    public static IActionResult ToCreatedResult<T>(this Result<T> result, string location) =>
        result.IsSuccess ? new CreatedResult(location, result.Value) : ToProblem(result.Error);

    private static ObjectResult ToProblem(Error error)
    {
        var status = ErrorCodeHttpStatus.For(error.Code);
        var problem = new ProblemDetails
        {
            Type = $"https://errors.stockorder.app/{error.Code}",
            Title = error.Code,
            Status = status,
            Detail = error.Message,
        };
        problem.Extensions["code"] = error.Code;

        if (error.Details is not null)
        {
            foreach (var (key, value) in error.Details)
            {
                problem.Extensions[key] = value;
            }
        }

        return new ObjectResult(problem) { StatusCode = status };
    }
}
