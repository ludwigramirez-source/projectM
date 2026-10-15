using FluentValidation;

namespace ProjectM.Api.Infrastructure.Validation;

/// <summary>Valida con FluentValidation el primer argumento de tipo <typeparamref name="T"/> del endpoint.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (validator is not null)
        {
            if (arg is null)
                throw new Errors.AppException(400, Errors.ErrorCodes.Validation, "El cuerpo de la solicitud es obligatorio.");
            await validator.ValidateAndThrowAsync(arg, ctx.HttpContext.RequestAborted);
        }
        return await next(ctx);
    }
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();
}
