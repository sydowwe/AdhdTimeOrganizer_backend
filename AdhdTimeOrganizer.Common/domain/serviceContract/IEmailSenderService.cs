﻿namespace AdhdTimeOrganizer.Common.domain.serviceContract;

public interface IEmailSenderService
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
}