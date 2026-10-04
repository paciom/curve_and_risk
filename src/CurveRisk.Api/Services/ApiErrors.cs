using CurveRisk.Ai.Tools;
using CurveRisk.Analytics.Curves;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CurveRisk.Api.Services;

/// <summary>A failure the caller can act on. Each kind maps to one HTTP status and one stable problem type.</summary>
public abstract class ApiException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }

    public abstract string ProblemType { get; }
}

public sealed class NotFoundException(string resource, string id) : ApiException($"{resource} '{id}' was not found.")
{
    public override int StatusCode => StatusCodes.Status404NotFound;

    public override string ProblemType => "not-found";
}

public sealed class ConflictException(string message) : ApiException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;

    public override string ProblemType => "conflict";
}

/// <summary>The request did not say which version it expects to change.</summary>
public sealed class PreconditionRequiredException() : ApiException("Send the If-Match header with the ETag of the version you are changing.")
{
    public override int StatusCode => StatusCodes.Status428PreconditionRequired;

    public override string ProblemType => "precondition-required";
}

/// <summary>The resource changed since the caller read it.</summary>
public sealed class PreconditionFailedException() : ApiException("The resource has changed since it was read. Fetch it again and retry.")
{
    public override int StatusCode => StatusCodes.Status412PreconditionFailed;

    public override string ProblemType => "precondition-failed";
}

/// <summary>The book has more trades than an operation over the whole book accepts.</summary>
public sealed class BookTooLargeException(int limit)
    : ApiException($"The book has more than {limit} trades, which is the most a risk brief covers.")
{
    public override int StatusCode => StatusCodes.Status422UnprocessableEntity;

    public override string ProblemType => "book-too-large";
}

/// <summary>A feature that needs configuration the deployment does not have.</summary>
public sealed class ServiceUnavailableException(string message) : ApiException(message)
{
    public override int StatusCode => StatusCodes.Status503ServiceUnavailable;

    public override string ProblemType => "unavailable";
}

/// <summary>The request was well formed but its fields are not acceptable. Carries one or more messages per field.</summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors) : ApiException("One or more fields are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public override int StatusCode => StatusCodes.Status400BadRequest;

    public override string ProblemType => "validation";
}

/// <summary>
/// Turns exceptions into RFC 9457 problem details. Caller mistakes get their own status and a stable
/// <c>type</c>; anything unexpected is a 500 with no internal detail in the body.
/// </summary>
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    private const string TypePrefix = "https://curverisk.example/problems/";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var details = ToProblem(exception);
        if (details is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = details.Status ?? StatusCodes.Status500InternalServerError;
        return await problems
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = details, Exception = exception })
            .ConfigureAwait(false);
    }

    private static ProblemDetails? ToProblem(Exception exception) => exception switch
    {
        RequestValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = validation.StatusCode,
            Type = TypePrefix + validation.ProblemType,
            Title = validation.Message,
        },
        ApiException api => Problem(api.StatusCode, api.ProblemType, api.Message),

        // The market or the trade cannot be priced as given: the request is understood, the content is not usable.
        CalibrationException or RiskEngineException or NotSupportedException =>
            Problem(StatusCodes.Status422UnprocessableEntity, "unprocessable", exception.Message),
        _ => null,
    };

    private static ProblemDetails Problem(int status, string type, string detail) => new()
    {
        Status = status,
        Type = TypePrefix + type,
        Title = type,
        Detail = detail,
    };
}
