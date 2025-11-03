using System.Text.Json.Serialization;

namespace VrcAvatarGallery
{
    public class AvatarInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("imageUrl")] public string ImageUrl { get; set; } = "";
        [JsonPropertyName("authorName")] public string AuthorName { get; set; } = "";
    }
}