using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.serviceContract;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AdhdTimeOrganizer.infrastructure.extService;

public class EmailSenderService(IConfiguration configuration) : IEmailSenderService
{

    private readonly string _fromEmail = Helper.GetEnvVar("MAIL_FROM_EMAIL");
    private readonly string _smtpPassword = Helper.GetEnvVar("MAIL_SMTP_PASSWORD");
    private readonly int _smtpPort = int.Parse(Helper.GetEnvVar("MAIL_SMTP_PORT"));
    private readonly string _smtpServer = Helper.GetEnvVar("MAIL_SMTP_SERVER");
    private readonly string _smtpUsername = Helper.GetEnvVar("MAIL_SMTP_USERNAME");

    protected readonly string appLogo = Helper.GetAppLogoUrl();

    protected readonly string appName = configuration["Application:Name"] ??
                                         throw new ArgumentNullException(nameof(configuration));
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_fromEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlMessage
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtpServer, _smtpPort, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }


}