namespace AdhdTimeOrganizer.Common.application.dto.response.generic;

public record SuccessResponse(string Message)
{
    public static string Status => "success";
}