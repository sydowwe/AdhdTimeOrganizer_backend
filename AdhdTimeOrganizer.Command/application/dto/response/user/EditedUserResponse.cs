namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class EditedUserResponse : UserResponse
{
    public EditedUserResponse(UserResponse userResponse)
    {
        Email = userResponse.Email;
        TwoFactorEnabled = userResponse.TwoFactorEnabled;
    }

    public IEnumerable<string>? ScratchCodes { get; set; }
    public byte[]? QrCode { get; set; }
}