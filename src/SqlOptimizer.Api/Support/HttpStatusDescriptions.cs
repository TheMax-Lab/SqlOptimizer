namespace SqlOptimizer.Api.Support;

/// <summary>
/// Standard HTTP status descriptions used in ProblemDetails titles (the
/// framework helper is not available on the .NET 8 shared framework).
/// </summary>
public static class HttpStatusDescriptions
{
    /// <summary>Returns the standard reason phrase for the given status code.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    public static string Status(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status408RequestTimeout => "Request Timeout",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "Error"
    };
}
