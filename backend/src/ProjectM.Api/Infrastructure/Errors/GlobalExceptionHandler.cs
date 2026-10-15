using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectM.Domain.Common;

namespace ProjectM.Api.Infrastructure.Errors;

/// <summary>Traduce cualquier excepción a ProblemDetails (RFC 7807) con la extensión <c>code</c>.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, code, title, errors) = exception switch
        {
            AppException e => (e.Status, e.Code, e.Message, e.Errors),
            DomainException e => (e.Kind == DomainErrorKind.Validation ? 400 : 409, e.Code, e.Message, null),
            ValidationException e => (400, ErrorCodes.Validation, "Uno o más campos no son válidos.",
                (IDictionary<string, string[]>?)e.Errors
                    .GroupBy(x => ToCamel(x.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())),
            DbUpdateConcurrencyException => (409, ErrorCodes.Concurrency,
                "El registro fue modificado por otra persona. Recarga e intenta de nuevo.", null),
            BadHttpRequestException e => (e.StatusCode, ErrorCodes.Validation, "La solicitud no es válida.", null),
            _ => (500, ErrorCodes.Internal, "Ocurrió un error inesperado.", null)
        };

        if (status >= 500) logger.LogError(exception, "Error no controlado en {Path}", http.Request.Path);

        http.Response.StatusCode = status;
        var pd = new ProblemDetails { Status = status, Title = title };
        pd.Extensions["code"] = code;
        if (errors is not null) pd.Extensions["errors"] = errors;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = pd,
            Exception = exception
        });
    }

    private static string ToCamel(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
