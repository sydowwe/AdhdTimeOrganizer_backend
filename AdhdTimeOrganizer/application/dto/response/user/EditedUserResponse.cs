namespace AdhdTimeOrganizer.application.dto.response.user;

public record EditedUserResponse : UserResponse
{
    public IEnumerable<string>? ScratchCodes { get; init; }
    public byte[]? QrCode { get; init; }
}