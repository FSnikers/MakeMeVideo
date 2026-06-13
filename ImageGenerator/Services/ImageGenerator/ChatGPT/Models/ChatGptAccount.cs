using System.Text.Json.Serialization;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

/// <summary>
/// Модель учетной записи ChatGPT
/// </summary>
public class ChatGptAccount
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
    
    [JsonPropertyName("cookies")]
    public List<CookieData>? Cookies { get; set; }
    
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
    
    [JsonPropertyName("last_used")]
    public DateTime? LastUsed { get; set; }
    
    [JsonPropertyName("generation_count_today")]
    public int GenerationCountToday { get; set; }
    
    [JsonPropertyName("last_reset_date")]
    public DateTime? LastResetDate { get; set; }
}

/// <summary>
/// Данные cookie для сохранения сессии
/// </summary>
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
