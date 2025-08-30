using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.dto;

public record AttachmentDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public required AttachmentTypeEnum Type { get; set; }


    public static AttachmentTypeEnum GetType(string type)
    {
        return type.ToLower() switch
        {
            // Image types
            "image/jpeg" or
                "image/jpg" or
                "image/png" or
                "image/gif" or
                "image/bmp" or
                "image/webp" or
                "image/svg+xml" or
                "image/tiff" => AttachmentTypeEnum.Image,
            // PDF
            "application/pdf" => AttachmentTypeEnum.Pdf,
            // Document types
            "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/vnd.ms-excel"
                or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or "text/plain" => AttachmentTypeEnum.Document,
            // Video types
            "video/mp4" or "video/mpeg" or "video/quicktime" or "video/x-msvideo" or "video/webm" => AttachmentTypeEnum.Video,
            // Compressed/Archive types
            "application/zip" or "application/x-rar-compressed" or "application/x-7z-compressed" or "application/gzip" => AttachmentTypeEnum.Compressed,
            _ => AttachmentTypeEnum.Other
        };
    }
}