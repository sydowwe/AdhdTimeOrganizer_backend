namespace AdhdTimeOrganizer.Common.application.dto.response.generic;

public class SuccessResponse
{
    public SuccessResponse(string message)
    {
        Message = message;
    }

    public string Message { get; set; }
    public string Status => "success";
}