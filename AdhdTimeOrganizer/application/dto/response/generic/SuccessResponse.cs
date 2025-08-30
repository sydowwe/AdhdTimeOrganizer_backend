namespace AdhdTimeOrganizer.application.dto.response.generic;

public record SuccessResponse(string Message)
{
    public static string Status => "success";
}