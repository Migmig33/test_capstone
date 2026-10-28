using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using WebApplication1.Filter;
using WebApplication1.Helper;
using WebApplication1.Models;
using WebApplication1.Models.Context;
using WebApplication1.Models.Tables;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    [RateLimit(sec = 10, requests = 5)]

    public class AuthController : Controller
    {
        // GET: Auth
        private bool VerifyPassword(string entered, string stored, out bool needsUpgrade)
        {
            needsUpgrade = false;
            if (string.IsNullOrEmpty(stored)) return false;

            if (stored.StartsWith("$2"))   // BCrypt hashes start with $2a$/$2b$/$2y$
                return BCrypt.Net.BCrypt.Verify(entered, stored);

            // Legacy plaintext row
            if (entered == stored) { needsUpgrade = true; return true; }
            return false;
        }
        public JsonResult Login(LoginModelDTO data)
        {
            string emailError = data == null ? "Email is required." : InputValidator.Email(data.email, "Email");
            if (emailError != null)
                return Json(new { success = false, message = emailError });

            try
            {
                using (var connect = new DB_Context())
                {
                    // ---- Admin / Property Manager (both in admin table) ----
                    var admin = connect.admin.FirstOrDefault(x => x.email == data.email);
                    if (admin != null && (admin.role == 1 || admin.role == 2))
                    {
                        bool upgrade;
                        if (VerifyPassword(data.password, admin.password, out upgrade))
                        {
                            if (upgrade)
                            {
                                admin.password = BCrypt.Net.BCrypt.HashPassword(data.password);
                                connect.SaveChanges();
                            }

                            string code = new Random().Next(100000, 999999).ToString();
                            Session["VerificationCode"] = code;
                            Session["VerifyEmail"] = data.email;
                            Session["VerifyExpiry"] = DateTime.Now.AddMinutes(5);
                            Session["UserRole"] = admin.role;   // 1 or 2
                            EmailServices.SendEmailVerification(data.email, code);
                            TempData["Email"] = data.email;
                            return Json(new { success = true, role = admin.role, message = "Verification code sent to " + data.email }, JsonRequestBehavior.AllowGet);
                        }
                    }

                    // ---- Tenant ----
                    var tenant = connect.tenant.FirstOrDefault(x => x.email == data.email);
                    if (tenant != null)
                    {
                        bool upgrade;
                        if (VerifyPassword(data.password, tenant.passwordHash, out upgrade))
                        {
                            if (upgrade)
                            {
                                tenant.passwordHash = BCrypt.Net.BCrypt.HashPassword(data.password);
                                connect.SaveChanges();
                            }

                            // Account-specific exception. The password was verified above, so only the OTP step
                            // is skipped for this one account. The sign-in itself goes through CompleteSignIn.
                            if (IsOtpExemptTenant(tenant.email))
                            {
                                Session.Remove("VerificationCode");
                                Session.Remove("VerifyExpiry");
                                Session["VerifyEmail"] = tenant.email;
                                Session["UserRole"] = 3; // Tenant
                                CompleteSignIn();
                                TempData["Email"] = tenant.email;
                                AuditLogger.Log("login", "info", "OTP step skipped for exempt tenant account", tenant.name);
                                return Json(new { success = true, role = 3, otpRequired = false, message = "Signed in." }, JsonRequestBehavior.AllowGet);
                            }

                            string code = new Random().Next(100000, 999999).ToString();
                            Session["VerificationCode"] = code;
                            Session["VerifyEmail"] = data.email;
                            Session["VerifyExpiry"] = DateTime.Now.AddMinutes(5);
                            Session["UserRole"] = 3; // Tenant
                            EmailServices.SendEmailVerification(data.email, code);
                            TempData["Email"] = data.email;
                            return Json(new { success = true, role = 3, message = "Verification code sent to " + data.email }, JsonRequestBehavior.AllowGet);
                        }
                    }

                    AuditLogger.Log("security", "warning", "Failed login attempt for " + data.email, data.email);
                    return Json(new { success = false, message = "User not found." }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        [HttpPost]
        public JsonResult Logout()
        {
            Session.Clear();
            Session.Abandon();
            return Json(new { success = true });
        }
        // The one tenant account that skips the OTP step. Its password is still verified like every other login.
        private const string OtpExemptTenantEmail = "tenant4cc@gmail.com";

        private static bool IsOtpExemptTenant(string email)
        {
            return !string.IsNullOrWhiteSpace(email)
                && string.Equals(email.Trim(), OtpExemptTenantEmail, StringComparison.OrdinalIgnoreCase);
        }

        // Completes a sign-in: marks the session authenticated, stamps lastActive, sets LoggedInTid
        // and writes the audit entry. Used by VerifyCode and by the OTP-exempt tenant sign-in.
        // Expects Session["VerifyEmail"] and Session["UserRole"] to be set already.
        private void CompleteSignIn()
        {
            Session["IsAuthenticated"] = true;

            int userRole = Convert.ToInt32(Session["UserRole"]);

            // + lastActive: stamp on successful sign-in and grab the display name for the audit log
            string verifyEmail = Session["VerifyEmail"] as string;
            string actorName = verifyEmail;
            using (var connect = new DB_Context())
            {
                if (userRole == 1 || userRole == 2) // admin / PM (admin table)
                {
                    var loginUser = connect.admin.FirstOrDefault(x => x.email == verifyEmail);
                    if (loginUser != null)
                    {
                        loginUser.lastActive = DateTime.Now;
                        actorName = loginUser.name;
                        connect.SaveChanges();
                    }
                }
                else if (userRole == 3) // tenant
                {
                    var loginTenant = connect.tenant.FirstOrDefault(x => x.email == verifyEmail);
                    if (loginTenant != null)
                    {
                        loginTenant.lastActive = DateTime.Now;
                        actorName = loginTenant.name;
                        connect.SaveChanges();
                    }
                }
            }

            // ✅ Set LoggedInTid so TenantPortal can identify the user
            if (userRole == 3)
            {
                string email = Session["VerifyEmail"] as string;
                using (var connect = new DB_Context())
                {
                    var tenant = connect.tenant.FirstOrDefault(x => x.email == email);

                    // If tenant is found, use their Tid. Otherwise, default to 1.
                    if (tenant != null)
                    {
                        Session["LoggedInTid"] = tenant.Tid;
                    }
                    else
                    {
                        Session["LoggedInTid"] = 1;
                    }
                }
            }

            AuditLogger.Log("login", "info", "Signed in", actorName); // + AUDIT
        }

        public JsonResult VerifyCode(verifycode data)
        {
            string codeError = InputValidator.Otp(data == null ? null : data.code);
            if (codeError != null)
                return Json(new { success = false, message = codeError });

            try
            {
                string savedCode = Session["VerificationCode"] as string;
                DateTime? expiry = Session["VerifyExpiry"] as DateTime?;

                if (savedCode == null || expiry == null)
                    return Json(new { success = false, message = "Session Expired" }, JsonRequestBehavior.AllowGet);

                if (DateTime.Now > expiry)
                    return Json(new { success = false, message = "Code Has Expired" }, JsonRequestBehavior.AllowGet);

                if (data.code != savedCode)
                    return Json(new { success = false, message = "Invalid Code" }, JsonRequestBehavior.AllowGet);

                CompleteSignIn();

                int userRole = Convert.ToInt32(Session["UserRole"]);
                return Json(new { success = true, role = userRole }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) });
            }
        }


        // ============================================================
        // 1. SEND OTP
        // ============================================================
        [HttpPost]
        public JsonResult SendVisitorOtp(VisitorAuthDTO data)
        {
            try
            {
                if (data == null)
                    return Json(new { success = false, message = "Visitor details are missing." });

                string visitorError = InputValidator.FirstError(
                    InputValidator.Name(data.name, "Full name"),
                    InputValidator.Email(data.email, "Email"),
                    InputValidator.Phone(data.phone, "Phone number", true));
                if (visitorError != null)
                    return Json(new { success = false, message = visitorError });

                string code = new Random().Next(100000, 999999).ToString();

                // Held separately from the verified keys until the code checks out
                Session["VisitorCode"] = code;
                Session["VisitorExpiry"] = DateTime.Now.AddMinutes(5);
                Session["PendingVisitorName"] = data.name.Trim();
                Session["PendingVisitorEmail"] = data.email.Trim();
                Session["PendingVisitorPhone"] = data.phone.Trim();

                EmailServices.SendEmailVerification(data.email.Trim(), code);

                return Json(new { success = true, message = "Verification code sent to " + data.email });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ============================================================
        // 2. VERIFY OTP
        // ============================================================
        [HttpPost]
        public JsonResult VerifyVisitorOtp(string code)
        {
            string codeError = InputValidator.Otp(code);
            if (codeError != null)
                return Json(new { success = false, message = codeError });

            try
            {
                string savedCode = Session["VisitorCode"] as string;
                DateTime? expiry = Session["VisitorExpiry"] as DateTime?;

                if (savedCode == null || expiry == null)
                    return Json(new { success = false, message = "Session expired. Please start again." });

                if (DateTime.Now > expiry)
                    return Json(new { success = false, message = "Code has expired. Please resend." });

                if (code != savedCode)
                    return Json(new { success = false, message = "Incorrect code. Please try again." });

                // Promote pending details to verified
                Session["VisitorName"] = Session["PendingVisitorName"];
                Session["VisitorEmail"] = Session["PendingVisitorEmail"];
                Session["VisitorPhone"] = Session["PendingVisitorPhone"];
                Session["VisitorVerified"] = true;

                Session["VisitorCode"] = null;
                Session["VisitorExpiry"] = null;

                return Json(new
                {
                    success = true,
                    name = Session["VisitorName"],
                    email = Session["VisitorEmail"],
                    phone = Session["VisitorPhone"]
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ============================================================
        // 3. CURRENT VISITOR  (so the modal can skip straight to the calendar)
        // ============================================================
        [HttpGet]
        public JsonResult GetVisitorSession()
        {
            bool verified = Session["VisitorVerified"] != null && (bool)Session["VisitorVerified"];

            return Json(new
            {
                success = true,
                verified = verified,
                name = verified ? Session["VisitorName"] : null,
                email = verified ? Session["VisitorEmail"] : null,
                phone = verified ? Session["VisitorPhone"] : null
            }, JsonRequestBehavior.AllowGet);
        }


        // ============================================================
        // 4. SWITCH ACCOUNT
        // ============================================================
        [HttpPost]
        public JsonResult ClearVisitorSession()
        {
            Session["VisitorVerified"] = null;
            Session["VisitorName"] = null;
            Session["VisitorEmail"] = null;
            Session["VisitorPhone"] = null;
            Session["VisitorCode"] = null;
            Session["VisitorExpiry"] = null;

            return Json(new { success = true });
        }


        // ============================================================
        // 5. AVAILABILITY  (busy days + taken slots, no guest details exposed)
        // ============================================================
        [HttpGet]
        public JsonResult GetBookingAvailability()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var busyDates = connect.busy_schedule
                        .ToList()
                        .Select(b => b.busyDate.ToString("yyyy-MM-dd"))
                        .Distinct()
                        .ToList();

                    // Only what the calendar needs. No names, emails or phones.
                    var taken = connect.booking
                        .Where(b => b.status == "Pending" || b.status == "Confirmed")
                        .ToList()
                        .Select(b => new
                        {
                            date = b.bookingDatetime.ToString("yyyy-MM-dd"),
                            hour = b.bookingDatetime.Hour
                        })
                        .ToList();

                    return Json(new
                    {
                        success = true,
                        busyDates = busyDates,
                        taken = taken
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message },
                            JsonRequestBehavior.AllowGet);
            }
        }


        // ============================================================
        // 6. SUBMIT BOOKING
        // ============================================================
        [HttpPost]
        public JsonResult SubmitVisitorBooking(VisitorBookingDTO data)
        {
            try
            {
                bool verified = Session["VisitorVerified"] != null && (bool)Session["VisitorVerified"];
                if (!verified)
                    return Json(new { success = false, message = "Please verify your email first." });

                int selectedUid;
                var rawUid = Session["SelectedUid"];
                if (rawUid == null || !int.TryParse(rawUid.ToString(), out selectedUid))
                    return Json(new { success = false, message = "No unit selected." });

                if (data == null || string.IsNullOrWhiteSpace(data.date) || string.IsNullOrWhiteSpace(data.slot))
                    return Json(new { success = false, message = "Please pick a date and time slot." });

                string notesError = InputValidator.Description(data.notes, "Notes", false, 500);
                if (notesError != null)
                    return Json(new { success = false, message = notesError });
                data.notes = InputValidator.CleanDescription(data.notes);

                // ---- Build the datetime from "yyyy-MM-dd" + "08:00 AM" ----
                DateTime bookingDate;
                if (!DateTime.TryParseExact(data.date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out bookingDate))
                    return Json(new { success = false, message = "Invalid date." });
                if (bookingDate.Date < DateTime.Today)
                    return Json(new { success = false, message = "Please pick today or a later date." });

                int hour = ParseSlotHour(data.slot);
                var bookingDatetime = bookingDate.Date.AddHours(hour);

                if (bookingDatetime < DateTime.Now)
                    return Json(new { success = false, message = "That time slot is already in the past." });

                string name = Session["VisitorName"] as string;
                string email = Session["VisitorEmail"] as string;
                string phone = Session["VisitorPhone"] as string;

                using (var connect = new DB_Context())
                {
                    var unit = connect.unit.FirstOrDefault(u => u.Uid == selectedUid);
                    if (unit == null)
                        return Json(new { success = false, message = "Unit not found." });

                    if (unit.status != "active")
                        return Json(new { success = false, message = "This unit is no longer available for booking." });

                    var today = DateTime.Today;
                    var currentTenant = connect.tenant
                        .Where(t => t.unitId == selectedUid && t.isTerminated != 1 && t.leaseEnd >= today)
                        .OrderByDescending(t => t.leaseEnd)
                        .FirstOrDefault();

                    if (currentTenant != null)
                    {
                        int totalOccupants = 1 + connect.co_occupant.Count(c => c.Tid == currentTenant.Tid);
                        bool hasOpenBedspace = UnitAvailabilityHelper.HasOpenBedspace(
                            currentTenant.occupancyTypeId, unit.maxOccupants, totalOccupants);

                        if (!hasOpenBedspace)
                            return Json(new { success = false, message = "This unit is currently occupied and has no available bedspace slots." });

                        if (!data.bedspaceConfirmed)
                            return Json(new { success = false, message = "Please confirm that you understand this booking is for a bedspace in a shared unit." });
                    }

                    // ---- Admin busy day ----
                    var dayStart = bookingDatetime.Date;
                    var dayEnd = dayStart.AddDays(1);

                    bool isBusy = connect.busy_schedule
                        .Any(b => b.busyDate >= dayStart && b.busyDate < dayEnd);

                    if (isBusy)
                        return Json(new { success = false, message = "That day is unavailable for viewings." });

                    // ---- Monthly limit: 2 per email per calendar month ----
                    int monthCount = connect.booking
                        .Count(b => b.guestEmail == email
                                    && b.bookingDatetime.Year == bookingDatetime.Year
                                    && b.bookingDatetime.Month == bookingDatetime.Month);

                    if (monthCount >= 2)
                        return Json(new { success = false, message = "You have reached the maximum of 2 bookings this month." });

                    // ---- Conflict: any live booking within 60 minutes, any unit ----
                    var windowStart = bookingDatetime.AddMinutes(-59);
                    var windowEnd = bookingDatetime.AddMinutes(59);

                    bool conflict = connect.booking
                        .Any(b => (b.status == "Pending" || b.status == "Confirmed")
                                  && b.bookingDatetime >= windowStart
                                  && b.bookingDatetime <= windowEnd);

                    if (conflict)
                        return Json(new { success = false, message = "That time slot is no longer available. Please choose another." });

                    // ---- Save ----
                    var booking = new booking
                    {
                        Uid = selectedUid,
                        guestName = name,
                        guestEmail = email,
                        guestPhone = phone,
                        bookingDatetime = bookingDatetime,
                        status = "Pending",
                        notes = data.notes
                    };

                    connect.booking.Add(booking);
                    connect.SaveChanges();

                    // Receipt email. Don't fail the booking if SMTP is down.
                    try
                    {
                        EmailServices.SendBookingReceipt(email, name, unit.unitName, bookingDatetime, data.notes);
                    }
                    catch { }

                    return Json(new
                    {
                        success = true,
                        message = "Booking submitted successfully!",
                        unitName = unit.unitName,
                        datetime = bookingDatetime.ToString("MMMM dd, yyyy hh:mm tt")
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // Turns "08:00 AM" / "01:00 PM" into a 24-hour integer
        private int ParseSlotHour(string slot)
        {
            var parts = slot.Trim().Split(' ');
            int hour = int.Parse(parts[0].Split(':')[0]);

            if (parts.Length > 1 && parts[1].ToUpper() == "PM" && hour != 12) hour += 12;
            if (parts.Length > 1 && parts[1].ToUpper() == "AM" && hour == 12) hour = 0;

            return hour;
        }


        [HttpPost]
        public JsonResult SendResetCode(string email)
        {
            try
            {
                string emailError = InputValidator.Email(email, "Email");
                if (emailError != null)
                    return Json(new { success = false, message = emailError }, JsonRequestBehavior.AllowGet);

                email = email.Trim();

                using (var connect = new DB_Context())
                {
                    int role = 0;

                    var admin = connect.admin.FirstOrDefault(x => x.email == email);
                    if (admin != null)
                    {
                        role = 1;
                    }
                    else
                    {
                        var tenant = connect.tenant.FirstOrDefault(x => x.email == email);
                        if (tenant != null) role = 2;
                    }

                    if (role == 0)
                        return Json(new { success = false, message = "No account found with this email address." },
                                    JsonRequestBehavior.AllowGet);

                    string code = new Random().Next(100000, 999999).ToString();

                    Session["ResetCode"] = code;
                    Session["ResetExpiry"] = DateTime.Now.AddMinutes(5);
                    Session["ResetEmail"] = email;
                    Session["ResetRole"] = role;
                    Session["ResetVerified"] = false;

                    EmailServices.SendEmailVerification(email, code);

                    return Json(new { success = true, message = "Reset code sent to " + email },
                                JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) },
                            JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost]
        public JsonResult VerifyResetCode(string code)
        {
            string codeError = InputValidator.Otp(code);
            if (codeError != null)
                return Json(new { success = false, message = codeError }, JsonRequestBehavior.AllowGet);

            try
            {
                string savedCode = Session["ResetCode"] as string;
                DateTime? expiry = Session["ResetExpiry"] as DateTime?;

                if (savedCode == null || expiry == null)
                    return Json(new { success = false, message = "Session Expired" },
                                JsonRequestBehavior.AllowGet);

                if (DateTime.Now > expiry)
                    return Json(new { success = false, message = "Code Has Expired" },
                                JsonRequestBehavior.AllowGet);

                if (code != savedCode)
                    return Json(new { success = false, message = "Incorrect code. Please try again." },
                                JsonRequestBehavior.AllowGet);

                // Passing the code is what unlocks ResetPassword
                Session["ResetVerified"] = true;
                Session["ResetCode"] = null;

                return Json(new { success = true }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) },
                            JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost]
        public JsonResult ResetPassword(string newPassword)
        {
            string passwordError = InputValidator.Password(newPassword, "New password", true, 8, 64);
            if (passwordError != null)
                return Json(new { success = false, message = passwordError }, JsonRequestBehavior.AllowGet);

            try
            {
                bool verified = Session["ResetVerified"] != null && (bool)Session["ResetVerified"];
                if (!verified)
                    return Json(new { success = false, message = "Please verify your code first." }, JsonRequestBehavior.AllowGet);

                DateTime? expiry = Session["ResetExpiry"] as DateTime?;
                if (expiry == null || DateTime.Now > expiry)
                    return Json(new { success = false, message = "Reset session expired. Please start again." }, JsonRequestBehavior.AllowGet);

                if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
                    return Json(new { success = false, message = "Password must be at least 8 characters." }, JsonRequestBehavior.AllowGet);

                string email = Session["ResetEmail"] as string;
                int role = Convert.ToInt32(Session["ResetRole"]);

                using (var connect = new DB_Context())
                {
                    if (role == 1)
                    {
                        var admin = connect.admin.FirstOrDefault(x => x.email == email);
                        if (admin == null)
                            return Json(new { success = false, message = "Account not found." }, JsonRequestBehavior.AllowGet);

                        admin.password = BCrypt.Net.BCrypt.HashPassword(newPassword);   // hashed
                    }
                    else
                    {
                        var tenant = connect.tenant.FirstOrDefault(x => x.email == email);
                        if (tenant == null)
                            return Json(new { success = false, message = "Account not found." }, JsonRequestBehavior.AllowGet);

                        tenant.passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);   // hashed
                    }

                    connect.SaveChanges();
                }

                AuditLogger.Log("security", "info", "Password reset", email);

                Session["ResetVerified"] = null;
                Session["ResetEmail"] = null;
                Session["ResetRole"] = null;
                Session["ResetExpiry"] = null;

                return Json(new { success = true, message = "Password updated. You can now sign in." }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        // ============================================================
        // 7. CHECK ACTIVE USER & UPDATE HEARTBEAT
        // ============================================================
        [HttpGet]
        public JsonResult CheckActiveUser()
        {
            try
            {
                // 1. Check if they have a valid authenticated session
                if (Session["IsAuthenticated"] == null || !(bool)Session["IsAuthenticated"])
                {
                    return Json(new { isAuthenticated = false, message = "No active session." }, JsonRequestBehavior.AllowGet);
                }

                int role = Convert.ToInt32(Session["UserRole"]);
                string email = Session["VerifyEmail"] as string;

                if (string.IsNullOrEmpty(email))
                {
                    Session.Clear();
                    return Json(new { isAuthenticated = false, message = "Session invalid." }, JsonRequestBehavior.AllowGet);
                }

                using (var connect = new DB_Context())
                {
                    if (role == 1 || role == 2) // Admin
                    {
                        var admin = connect.admin.FirstOrDefault(x => x.email == email);

                        // Check if account exists (status check removed)
                        if (admin == null)
                        {
                            Session.Clear();
                            Session.Abandon();
                            return Json(new { isAuthenticated = false, message = "Account not found." }, JsonRequestBehavior.AllowGet);
                        }

                        // Update the heartbeat timestamp
                        admin.lastActive = DateTime.Now;
                        connect.SaveChanges();

                        return Json(new
                        {
                            isAuthenticated = true,
                            role = role,
                            user = new { name = admin.name, email = admin.email }
                        }, JsonRequestBehavior.AllowGet);
                    }
                    else if (role == 3) // Tenant
                    {
                        var tenant = connect.tenant.FirstOrDefault(x => x.email == email);

                        // Check if account exists (status check removed)
                        if (tenant == null)
                        {
                            Session.Clear();
                            Session.Abandon();
                            return Json(new { isAuthenticated = false, message = "Account not found." }, JsonRequestBehavior.AllowGet);
                        }

                        // Update the heartbeat timestamp
                        tenant.lastActive = DateTime.Now;
                        connect.SaveChanges();

                        return Json(new
                        {
                            isAuthenticated = true,
                            role = role,
                            user = new { name = tenant.name, email = tenant.email, tid = tenant.Tid }
                        }, JsonRequestBehavior.AllowGet);
                    }

                    return Json(new { isAuthenticated = false, message = "Unknown role." }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { isAuthenticated = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        public string ErrorHandling(Exception ex)
        {
            var errorMessage = $@"
            ===== ERROR DETAILS =====
            Message        : {ex.Message}
            Type           : {ex.GetType().FullName}
            Source         : {ex.Source}
            Stack Trace    : {ex.StackTrace}
            Inner Exception: {(ex.InnerException != null ? ex.InnerException.Message : "None")}
            Timestamp      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            =========================";

            return errorMessage;
        }
    }
}
