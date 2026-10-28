using Mysqlx;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebApplication1.Filter;
using WebApplication1.Helper;
using WebApplication1.Models.Context;
using WebApplication1.Models.Tables;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    [RateLimit(sec = 10, requests = 5)]

    public class TenantPortalController : Controller
    {
            [HttpGet]
            public JsonResult GetCurrentTenantData()
            {
                // 1. Get logged-in Tenant ID (e.g., from Session)
                int currentTid = Convert.ToInt32(Session["LoggedInTid"]);


                if (currentTid == 0)
                {
                    return Json(new { success = false, message = "Not authenticated" }, JsonRequestBehavior.AllowGet);
                }

            // 2. Fetch data from DB inside a using block
            using (var connect = new DB_Context())
            {
                var tenantData = connect.tenant.FirstOrDefault(t => t.Tid == currentTid);
                if (tenantData == null)
                    return Json(new { success = false }, JsonRequestBehavior.AllowGet);

                var unitData = connect.unit.FirstOrDefault(u => u.Uid == tenantData.unitId);
                var coOccupants = connect.co_occupant.Where(c => c.Tid == currentTid).ToList();

                return Json(new
                {
                    success = true,
                    tenant = new
                    {
                        tenantData.Tid,
                        tenantData.tenantNumber,
                        tenantData.unitId,
                        tenantData.name,
                        tenantData.email,
                        tenantData.phone,
                        tenantData.address,
                        tenantData.occupation,
                        tenantData.occupancyTypeId,
                        tenantData.isTerminated,
                        leaseStart = tenantData.leaseStart.ToString("MMM dd, yyyy"),
                        leaseEnd = tenantData.leaseEnd.ToString("MMM dd, yyyy")
                    },
                    unitName = unitData?.unitName ?? "Unassigned",
                    coOccupants = coOccupants
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]

        public JsonResult GetMaintenanceRequests(int tid)
        {
            // Make sure the requested tid matches the logged-in user for security
            int currentTid = Convert.ToInt32(Session["LoggedInTid"]);
            if (tid != currentTid)
            {
                return Json(null, JsonRequestBehavior.AllowGet);
            }

            using (var connect = new DB_Context())
            {
                var requests = connect.maintenance_request
                                  .Where(m => m.Tid == tid)
                                  .OrderByDescending(m => m.reportedDate)
                                  .ToList() // 1. Execute query and pull to memory
                                  .Select(m => new
                                  {
                                      // 2. Map properties
                                      id = m.id,
                                      Tid = m.Tid,
                                      Uid = m.Uid,
                                      category = m.category,
                                      description = m.description,
                                      status = m.status,

                                      // 3. Convert Dates to standard readable strings!
                                      // "yyyy-MM-dd" gives you "2023-10-25"
                                      // Use "yyyy-MM-ddTHH:mm:ss" if you also need the exact time
                                      reportedDate = m.reportedDate.ToString("yyyy-MM-dd"),
                                      resolvedDate = m.resolvedDate.HasValue
                                                     ? m.resolvedDate.Value.ToString("yyyy-MM-dd")
                                                     : null,
                                     scheduledDate = m.scheduledDate.HasValue ? m.scheduledDate.Value.ToString("yyyy-MM-dd") : null
                                  });

                return Json(requests, JsonRequestBehavior.AllowGet);
            }
        }

            [HttpPost]
            public JsonResult SubmitMaintenanceRequest(maintenance_request req)
            {
                string inputError = InputValidator.FirstError(
                    InputValidator.Description(req.description, "Description", true, 100),
                    InputValidator.Code(req.category, "Category", true, 100));
                if (inputError != null)
                    return Json(new { success = false, message = inputError });
                req.description = InputValidator.CleanDescription(req.description);

                try
                {
                    // Validate security
                    int currentTid = Convert.ToInt32(Session["LoggedInTid"]);
                    if (req.Tid != currentTid)
                    {
                        return Json(new { success = false, message = "Unauthorized." });
                    }

                    // Force server-side dates/statuses to prevent client manipulation
                    req.reportedDate = DateTime.Now;
                    req.status = "Pending";

                    using (var connect = new DB_Context())
                    {
                        connect.maintenance_request.Add(req);
                        connect.SaveChanges();
                    }

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = ex.Message });
                }
            }

        public JsonResult GetPayments(int tid)
        {
            int currentTid = Convert.ToInt32(Session["LoggedInTid"]);
            if (tid != currentTid)
                return Json(null, JsonRequestBehavior.AllowGet);

            using (var connect = new DB_Context())
            {
                var payments = connect.payment
                    .Where(p => p.Tid == tid)
                    .OrderBy(p => p.dueDate)
                    .ToList()
                    .Select(p => new
                    {
                        p.id,
                        p.Tid,
                        p.Uid,
                        p.amount,
                        p.billingPeriod,
                        dueDate = p.dueDate.ToString("MMM dd, yyyy"),
                        paidDate = p.paidDate.HasValue ? p.paidDate.Value.ToString("MMM dd, yyyy") : null,
                        status = p.status,  // computed from getter
                        month = p.billingPeriod // what the frontend shows as the row title
                    })
                    .ToList();

                return Json(payments, JsonRequestBehavior.AllowGet);
            }
        }
        public JsonResult SaveTenantProfileChangeRequest(tenant_profile_change_request data)
        {
            string inputError = InputValidator.FirstError(
                InputValidator.Email(data.requestedEmail, "Email"),
                InputValidator.Phone(data.requestedPhone, "Phone number", true),
                InputValidator.Occupation(data.requestedOccupation, "Occupation", true, 25),
                InputValidator.Address(data.requestedAddress, "Address", true, 150),
                InputValidator.Description(data.reason, "Reason", true, 1000));
            if (inputError != null)
                return Json(new { success = false, message = inputError });
            data.requestedEmail = data.requestedEmail.Trim();
            data.requestedPhone = InputValidator.CleanText(data.requestedPhone);
            data.requestedOccupation = InputValidator.CleanText(data.requestedOccupation);
            data.requestedAddress = InputValidator.CleanText(data.requestedAddress);
            data.reason = InputValidator.CleanDescription(data.reason);

            try
            {
                using(var connect = new DB_Context())
                {
                    int currentTenantId = Convert.ToInt32(Session["LoggedInTid"]);
                    var currentTenant = connect.tenant.FirstOrDefault(t => t.Tid == currentTenantId);
                    var existingTenantRequest = connect.tenant_profile_change_request.FirstOrDefault(x => x.Tid == currentTenantId);
                    if(existingTenantRequest == null)
                    {
                        //Insert 
                        var tenantRequest = new tenant_profile_change_request
                        {
                            Tid = currentTenantId,
                            requestedEmail = data.requestedEmail,
                            requestedPhone = data.requestedPhone,
                            requestedOccupation = data.requestedOccupation,
                            requestedAddress = data.requestedAddress,
                            reason = data.reason,
                            status = "Pending",
                            requestedAt = DateTime.Now,
                            
                        };
                        connect.tenant_profile_change_request.Add(tenantRequest);
                        connect.SaveChanges();
                        AuditLogger.Log("user", "info", "Change Profile Request " + currentTenant.name, CurrentTenantName());
                        bool notifStatus = true;
                        var existingPropertyManager = connect.admin.Where(a => a.role == 2).ToList();
                        foreach(var manager in existingPropertyManager)
                        {
                            string message =
                                $"Hello {manager.name},\n\n" +
                                $"{currentTenant.name} has submitted a request to change their profile information.\n\n" +
                                $"Reason for the request: {data.reason}\n\n" +
                                "Please review the request and approve or decline it when you have a moment.\n\n" +
                                "Thank you!";
                            var phone = SmsServices.NormalizePhone(manager.phone);
                            string error = SmsServices.SendSms(phone, message);
                            if (error != null)
                            {
                                notifStatus = false;
                            }
                        }

                        return Json(new
                        {
                            success = true,
                            message = notifStatus
                                ? "Request profile change successfully added."
                                : "Request was added, but some notifications could not be sent."
                        }, JsonRequestBehavior.AllowGet);


                    }
                    else
                    {
                        //update
                        existingTenantRequest.requestedPhone = data.requestedPhone;
                        existingTenantRequest.requestedOccupation = data.requestedOccupation;
                        existingTenantRequest.requestedEmail = data.requestedEmail;
                        existingTenantRequest.requestedAddress = data.requestedAddress;
                        existingTenantRequest.status = data.status;
                        existingTenantRequest.reason = data.reason;
                        
                        connect.SaveChanges();




                            return Json(new { success = true, message = "Request profile change successfully modified" }, JsonRequestBehavior.AllowGet);


                    }
                }

                
            }catch(Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        //Helper
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
        private string CurrentTenantName()
        {
            string email = Session["VerifyEmail"] as string;
            if (string.IsNullOrEmpty(email)) return "System";
            using (var connect = new DB_Context())
            {
                var a = connect.tenant.FirstOrDefault(x => x.email == email);
                return a != null ? a.name : email;
            }
        }
        private string CurrentManagerName()
        {
            string email = Session["VerifyEmail"] as string;
            if (string.IsNullOrEmpty(email)) return "System";
            using (var connect = new DB_Context())
            {
                var a = connect.admin.FirstOrDefault(x => x.email == email);
                return a != null ? a.name : email;
            }
        }
    }
}