using System.Text.Json.Serialization;

namespace Project2Prompt.Models;

/// <summary>トリミング後も設定JSONの型情報を保持するソース生成コンテキストです。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
