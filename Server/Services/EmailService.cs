using MailKit.Net.Smtp;
using MimeKit;

namespace InvestmentTracker.Server.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string body)
        {
            var emailSettings = _config.GetSection("EmailSettings");
            var from = emailSettings["From"];
            var smtp = emailSettings["SmtpServer"];
            var port = int.Parse(emailSettings["SmtpPort"] ?? "465");
            var user = emailSettings["Username"];
            var pass = emailSettings["Password"];

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Investment Tracker", from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(smtp, port, true);
                await client.AuthenticateAsync(user, pass);
                await client.SendAsync(message);
                _logger.LogInformation("Email sent to {To}", to);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }
    }
}