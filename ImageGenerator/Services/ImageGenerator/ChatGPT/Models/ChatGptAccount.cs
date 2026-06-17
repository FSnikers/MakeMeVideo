using System.Text.Json.Serialization;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

public class ChatGptAccount
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("cookies")]
    public List<CookieData>? Cookies { get; set; }

    [JsonPropertyName("last_used")]
    public DateTime? LastUsed { get; set; }
}

public class CookieData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = "/";

    [JsonPropertyName("expiry")]
    public long? Expiry { get; set; }

    [JsonPropertyName("secure")]
    public bool Secure { get; set; }

    [JsonPropertyName("http_only")]
    public bool HttpOnly { get; set; }
}
