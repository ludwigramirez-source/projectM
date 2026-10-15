using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace ProjectM.IntegrationTests.Infrastructure;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage res)
    {
        var body = await res.Content.ReadFromJsonAsync<T>(Options);
        return body!;
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>(Options));

    /// <summary>Verifica el status y, si es ProblemDetails, el código de error estable.</summary>
    public static async Task ShouldBeAsync(this HttpResponseMessage res, int status, string? code = null)
    {
        var text = await res.Content.ReadAsStringAsync();
        ((int)res.StatusCode).Should().Be(status, "respuesta: {0}", text);
        if (code is not null)
        {
            var json = JsonDocument.Parse(text).RootElement;
            json.GetProperty("code").GetString().Should().Be(code);
        }
    }
}
