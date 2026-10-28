using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Configuration;
using System.Net.Mail;
using MailKit.Net.Smtp;
namespace WebApplication1.Services
{
    public class EmailServices
    {
        public static void SendEmailVerification(string toEmail, string code)
        {
            // Implement email sending logic here using SMTP or an email service provider
            // For example, you can use System.Net.Mail.SmtpClient to send emails
            // Make sure to handle exceptions and configure your SMTP settings properly

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Green Residences", ConfigurationManager.AppSettings["SmtpUser"]));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "Your Verification Code";

            message.Body = new TextPart("html")
            {
                Text = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <!-- Header Image -->
    <div style='border-radius: 12px 12px 0 0; overflow: hidden; height: 200px;'>
    <img src='https://i.imgur.com/FM1eJsr.jpeg' 
         width='600' 
         style='display:block; width:100%; height:200px; object-fit:cover; object-position:center;' />
    </div>
    <!-- Title bar -->
    <div style='background-color: #4F46E5; padding: 16px 24px;'>
        <h1 style='color: white; margin: 0; font-size: 22px;'>Email Verification 📧</h1>
    </div>
    <div style='background-color: #f8fafc; padding: 24px; border-radius: 0 0 12px 12px; border: 1px solid #e2e8f0;'>
        <p style='color: #334155; font-size: 15px;'>Your verification code is:</p>
        <div style='background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 24px; margin: 20px 0; text-align: center;'>
            <h1 style='letter-spacing: 12px; color: #4F46E5; margin: 0; font-size: 36px;'>{code}</h1>
        </div>
        <p style='color: #334155; font-size: 14px;'>This code expires in <strong>5 minutes</strong>.</p>
        <p style='color: #94a3b8; font-size: 13px; margin-top: 24px;'>— Green Residences Team</p>
    </div>
</div>"

            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                client.Connect(
                    ConfigurationManager.AppSettings["SmtpHost"],
                    int.Parse(ConfigurationManager.AppSettings["SmtpPort"]),
                    MailKit.Security.SecureSocketOptions.StartTls
                );
                client.Authenticate(
                    ConfigurationManager.AppSettings["SmtpUser"],
                    ConfigurationManager.AppSettings["SmtpPass"]
                );
                client.Send(message);
                client.Disconnect(true);
            }
        }
        public static void SendBookingConfirmed(string toEmail, string guestName, string unitName, DateTime bookingDatetime)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Green Residences", ConfigurationManager.AppSettings["SmtpUser"]));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "Your Viewing Schedule is Confirmed!";
            message.Body = new TextPart("html")
            {
                Text = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <!-- Header Image -->
    <div style='border-radius: 12px 12px 0 0; overflow: hidden; height: 200px;'>
    <img src='https://i.imgur.com/FM1eJsr.jpeg' 
         width='600' 
         style='display:block; width:100%; height:200px; object-fit:cover; object-position:center;' />
    </div>
    <!-- Green title bar -->
    <div style='background-color: #16a34a; padding: 16px 24px;'>
        <h1 style='color: white; margin: 0; font-size: 22px;'>Booking Confirmed ✅</h1>
    </div>
    <div style='background-color: #f8fafc; padding: 24px; border-radius: 0 0 12px 12px; border: 1px solid #e2e8f0;'>
        <p style='color: #334155; font-size: 15px;'>Hi <strong>{guestName}</strong>,</p>
        <p style='color: #334155; font-size: 15px;'>
            Your viewing schedule has been <strong style='color: #16a34a;'>confirmed!</strong>
        </p>
        <div style='background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 16px; margin: 20px 0;'>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>UNIT</p>
            <p style='margin: 0 0 16px; color: #0f172a; font-size: 15px; font-weight: bold;'>{unitName}</p>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>SCHEDULE</p>
            <p style='margin: 0; color: #0f172a; font-size: 15px; font-weight: bold;'>{bookingDatetime:MMMM dd, yyyy hh:mm tt}</p>
        </div>
        <p style='color: #334155; font-size: 14px;'>Please be on time. If you need to reschedule, contact us as soon as possible.</p>
        <p style='color: #94a3b8; font-size: 13px; margin-top: 24px;'>— Green Residences Team</p>
    </div>
</div>"
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                client.Connect(
                    ConfigurationManager.AppSettings["SmtpHost"],
                    int.Parse(ConfigurationManager.AppSettings["SmtpPort"]),
                    MailKit.Security.SecureSocketOptions.StartTls
                );
                client.Authenticate(
                    ConfigurationManager.AppSettings["SmtpUser"],
                    ConfigurationManager.AppSettings["SmtpPass"]
                );
                client.Send(message);
                client.Disconnect(true);
            }
        }

        public static void SendBookingDeclined(string toEmail, string guestName, string unitName, DateTime bookingDatetime, string reason)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Green Residences", ConfigurationManager.AppSettings["SmtpUser"]));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "Update on Your Viewing Schedule";
            message.Body = new TextPart("html")
            {
                Text = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <!-- Header Image -->
    <div style='border-radius: 12px 12px 0 0; overflow: hidden; height: 200px;'>
    <img src='https://i.imgur.com/FM1eJsr.jpeg' 
         width='600' 
         style='display:block; width:100%; height:200px; object-fit:cover; object-position:center;' />
    </div>
    <!-- Red title bar -->
    <div style='background-color: #dc2626; padding: 16px 24px;'>
        <h1 style='color: white; margin: 0; font-size: 22px;'>Booking Declined ❌</h1>
    </div>
    <div style='background-color: #f8fafc; padding: 24px; border-radius: 0 0 12px 12px; border: 1px solid #e2e8f0;'>
        <p style='color: #334155; font-size: 15px;'>Hi <strong>{guestName}</strong>,</p>
        <p style='color: #334155; font-size: 15px;'>
            Unfortunately your viewing schedule has been <strong style='color: #dc2626;'>declined.</strong>
        </p>
        <div style='background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 16px; margin: 20px 0;'>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>UNIT</p>
            <p style='margin: 0 0 16px; color: #0f172a; font-size: 15px; font-weight: bold;'>{unitName}</p>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>SCHEDULE</p>
            <p style='margin: 0 0 16px; color: #0f172a; font-size: 15px; font-weight: bold;'>{bookingDatetime:MMMM dd, yyyy hh:mm tt}</p>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>REASON</p>
            <p style='margin: 0; color: #dc2626; font-size: 15px;'>{reason}</p>
        </div>
        <p style='color: #334155; font-size: 14px;'>You may book another schedule at your convenience. We apologize for the inconvenience.</p>
        <p style='color: #94a3b8; font-size: 13px; margin-top: 24px;'>— Green Residences Team</p>
    </div>
</div>"
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                client.Connect(
                    ConfigurationManager.AppSettings["SmtpHost"],
                    int.Parse(ConfigurationManager.AppSettings["SmtpPort"]),
                    MailKit.Security.SecureSocketOptions.StartTls
                );
                client.Authenticate(
                    ConfigurationManager.AppSettings["SmtpUser"],
                    ConfigurationManager.AppSettings["SmtpPass"]
                );
                client.Send(message);
                client.Disconnect(true);
            }
        }

        public static void SendBookingReceipt(string toEmail, string guestName, string unitName, DateTime bookingDatetime, string notes)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Green Residences", ConfigurationManager.AppSettings["SmtpUser"]));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "We received your viewing request";

            string notesBlock = string.IsNullOrWhiteSpace(notes) ? "" : $@"
            <p style='margin: 16px 0 8px; color: #64748b; font-size: 13px;'>YOUR NOTES</p>
            <p style='margin: 0; color: #0f172a; font-size: 15px; font-style: italic;'>{notes}</p>";

            message.Body = new TextPart("html")
            {
                Text = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <div style='border-radius: 12px 12px 0 0; overflow: hidden; height: 200px;'>
    <img src='https://i.imgur.com/FM1eJsr.jpeg' 
         width='600' 
         style='display:block; width:100%; height:200px; object-fit:cover; object-position:center;' />
    </div>
    <div style='background-color: #16a34a; padding: 16px 24px;'>
        <h1 style='color: white; margin: 0; font-size: 22px;'>Booking Received 📅</h1>
    </div>
    <div style='background-color: #f8fafc; padding: 24px; border-radius: 0 0 12px 12px; border: 1px solid #e2e8f0;'>
        <p style='color: #334155; font-size: 15px;'>Hi <strong>{guestName}</strong>,</p>
        <p style='color: #334155; font-size: 15px;'>
            Thanks! We've received your viewing request. It is currently
            <strong style='color: #d97706;'>pending admin review</strong> — we'll email you again once it's confirmed.
        </p>
        <div style='background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 16px; margin: 20px 0;'>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>UNIT</p>
            <p style='margin: 0 0 16px; color: #0f172a; font-size: 15px; font-weight: bold;'>{unitName}</p>
            <p style='margin: 0 0 8px; color: #64748b; font-size: 13px;'>REQUESTED SCHEDULE</p>
            <p style='margin: 0; color: #0f172a; font-size: 15px; font-weight: bold;'>{bookingDatetime:MMMM dd, yyyy hh:mm tt}</p>{notesBlock}
        </div>
        <p style='color: #334155; font-size: 14px;'>No payment is required to book a viewing. Site inspection is free.</p>
        <p style='color: #94a3b8; font-size: 13px; margin-top: 24px;'>— Green Residences Team</p>
    </div>
</div>"
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                client.Connect(
                    ConfigurationManager.AppSettings["SmtpHost"],
                    int.Parse(ConfigurationManager.AppSettings["SmtpPort"]),
                    MailKit.Security.SecureSocketOptions.StartTls
                );
                client.Authenticate(
                    ConfigurationManager.AppSettings["SmtpUser"],
                    ConfigurationManager.AppSettings["SmtpPass"]
                );
                client.Send(message);
                client.Disconnect(true);
            }
        }
    }
}