using System.Collections.Concurrent;
using FluentValidation;
using Mediator;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Infrastructure.Validation;

/// <summary>
/// Runs every registered FluentValidation validator for the incoming message before the
/// handler sees it (section 20: "one command/query + handler + validator per use case").
/// When the handler's response is a <see cref="Result"/> or <see cref="Result{T}"/>, a
/// validation failure becomes a VALIDATION_FAILED result instead of a thrown exception, so
/// controllers can map it the same way as any other business-rule failure.
/// </summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> FailureFactories = new();

    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(message, cancellationToken);
        }

        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(message, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next(message, cancellationToken);
        }

        var errorsByField = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => (object?)g.Select(f => f.ErrorMessage).ToArray());

        // Section 9: field messages go in the ProblemDetails `errors` extension.
        var error = Error.Validation(
            "One or more fields failed validation.", new Dictionary<string, object?> { ["errors"] = errorsByField });

        if (!typeof(Result).IsAssignableFrom(typeof(TResponse)))
        {
            throw new ValidationException(failures);
        }

        var factory = FailureFactories.GetOrAdd(typeof(TResponse), BuildFailureFactory);
        return (TResponse)factory(error);
    }

    private static Func<Error, object> BuildFailureFactory(Type responseType)
    {
        if (responseType == typeof(Result))
        {
            return error => Result.Failure(error);
        }

        var valueType = responseType.GetGenericArguments()[0];
        var method = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
            .MakeGenericMethod(valueType);

        return error => method.Invoke(null, [error])!;
    }
}
