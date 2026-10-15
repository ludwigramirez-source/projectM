namespace ProjectM.Api.Infrastructure.Errors;

/// <summary>Error de aplicación que se traduce a ProblemDetails con un código estable.</summary>
public class AppException(int status, string code, string message, IDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static AppException NotFound(string what = "El recurso") =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"{what} no existe o no tienes acceso.");

    public static AppException Forbidden(string message = "No tienes permiso para realizar esta acción.") =>
        new(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, message);

    public static AppException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);

    public static AppException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    public static AppException Validation(IDictionary<string, string[]> errors) =>
        new(StatusCodes.Status400BadRequest, ErrorCodes.Validation, "Uno o más campos no son válidos.", errors);

    public static AppException Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });
}

public static class ErrorCodes
{
    public const string Validation = "VALIDACION";
    public const string NotFound = "NO_ENCONTRADO";
    public const string Forbidden = "PROHIBIDO";
    public const string Unauthorized = "NO_AUTENTICADO";
    public const string Concurrency = "CONFLICTO_CONCURRENCIA";
    public const string ProjectClosed = "PROYECTO_CERRADO";
    public const string OpenTasks = "TAREAS_ABIERTAS";
    public const string InvalidTransition = "TRANSICION_INVALIDA";
    public const string RateLimited = "DEMASIADAS_SOLICITUDES";
    public const string InvalidCredentials = "CREDENCIALES_INVALIDAS";
    public const string Internal = "ERROR_INTERNO";
}
