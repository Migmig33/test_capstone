using Amazon;
using Hangfire.States;
using Microsoft.Ajax.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using WebApplication1;
using WebApplication1.Filter;
using WebApplication1.Helper;
using WebApplication1.Models;
using WebApplication1.Models.Context;
using WebApplication1.Models.Tables;
using WebApplication1.Services;
using static WebApplication1.Services.NotificationChecker;
namespace WebApplication1.Controllers
{
    [RateLimit(sec = 10, requests = 5)]

    public class SystemController : Controller

    {



        // GET: System
        public ActionResult Index()
        {
            return View();
        }
        //[CheckSession(AllowedRoles = new[] { 1 })]
        public ActionResult AdminDashboard()
        {
            return View("SuperAdmin/Dashboard");
        }
        [CheckSession(AllowedRoles = new[] { 1 })]
        public ActionResult AdminManagers()
        {
            return View("SuperAdmin/Managers");
        }
        [CheckSession(AllowedRoles = new[] { 1 })]
        public ActionResult AdminTables()
        {
            return View("SuperAdmin/Table");
        }
        [CheckSession(AllowedRoles = new[] { 1 })]
        public ActionResult AdminLogs()
        {
            return View("SuperAdmin/Log");
        }
        public ActionResult RentersBrowse()
        {
            return View("Renters/Browse");
        }
        public ActionResult RentersBookings()
        {
            return View("Renters/MyBookings");
        }
        public ActionResult RentersUnitDetails(int? id)
        {
            if (id.HasValue && id.Value > 0)
                Session["SelectedUid"] = id.Value;

            return View("Renters/UnitDetails");

        }
        [CheckSession(AllowedRoles = new[] { 2 })]
        public ActionResult Dashboard()
        {
            return View("Admin/Dashboard");
        }
        [CheckSession(AllowedRoles = new[] { 2 })]
        public ActionResult Units()
        {
            return View("Admin/Units");
        }
        //[CheckSession(AllowedRoles = new[] { 2 })]
        public ActionResult Tenants()
        {
            return View("Admin/Tenants");
        }
        [CheckSession(AllowedRoles = new[] { 2 })]
        public ActionResult Bookings()
        {
            return View("Admin/Bookings");
        }
        [CheckSession(AllowedRoles = new[] { 2 })]

        public ActionResult Maintenance()
        {
            return View("Admin/Maintenance");
        }
        [CheckSession(AllowedRoles = new[] { 2 })]

        public ActionResult Payments()
        {
            return View("Admin/Payments");
        }
        public ActionResult Auth()
        {
            return View();
        }
        [CheckSession(AllowedRoles = new[] { 3 })]

        public ActionResult TenantPortal()
        {
            return View();
        }
        public ActionResult TenantProfile()
        {
            return View();
        }
        // POST API
        [HttpPost]
        public JsonResult UploadUnitVideo(HttpPostedFileBase video)
        {
            try
            {
                if (video == null || video.ContentLength == 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No video file recieved"
                    });
                }
                var allowedTypes = new[]
                {
                    "video/mp4",
                    "video/webm",
                    "video/ogg"
                };
                if (!allowedTypes.Contains(video.ContentType))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid Video Type"
                    });
                }
                var extension = Path.GetExtension(video.FileName);

                var fileName =
                    Guid.NewGuid().ToString() + extension;

                var folderPath =
                    Server.MapPath("~/Uploads/Videos");

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                var filePath = Path.Combine(folderPath, fileName);
                video.SaveAs(filePath);

                var videoUrl =
                    Url.Content("~/Uploads/Videos/" + fileName);
                return Json(new
                {
                    success = true,
                    url = videoUrl,

                });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) },
                           JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult SaveTenant(tenant tenantData, List<co_occupant> coOccupants,
                              List<IdFileDto> idFiles, List<int> deletedDocumentIds, int? id,
                              List<HttpPostedFileBase> idUploads = null)
        {
            // Server-side input rules. They run before any file or database write.
            string inputError = CheckAndCleanTenant(tenantData, coOccupants);
            if (inputError != null)
                return Json(new { success = false, message = inputError }, JsonRequestBehavior.AllowGet);

            var uploadedPaths = new List<string>();
            bool committed = false;
            try
            {
                using (var connect = new DB_Context())
                using (var transaction = connect.Database.BeginTransaction())
                {
                    var existingTenant = connect.tenant.FirstOrDefault(x => x.Tid == id);
                    idFiles = idFiles ?? new List<IdFileDto>();
                    int retainedDocuments = 0;
                    if (existingTenant != null)
                    {
                        var documents = connect.tenant_document.Where(d => d.Tid == existingTenant.Tid);
                        if (deletedDocumentIds != null && deletedDocumentIds.Any())
                            documents = documents.Where(d => !deletedDocumentIds.Contains(d.id));
                        retainedDocuments = documents.Count();
                    }
                    if (retainedDocuments + idFiles.Count(f => f.id == 0) + (idUploads == null ? 0 : idUploads.Count) > 2)
                        throw new InvalidOperationException("Maximum of 2 ID documents allowed.");

                    // Store image files outside the database and outside the public web root.
                    foreach (var upload in idUploads ?? new List<HttpPostedFileBase>())
                    {
                        idFiles.Add(new IdFileDto
                        {
                            name = Path.GetFileName(upload.FileName),
                            url = SaveTenantIdImage(upload, uploadedPaths)
                        });
                    }
                    bool usesCoOcc = UsesCoOccupants(tenantData.occupancyTypeId);

                    // ---- Capacity guard: 1 main tenant + co-occupants <= maxOccupants ----
                    if (tenantData.unitId > 0)
                    {
                        var targetUnit = connect.unit.FirstOrDefault(u => u.Uid == tenantData.unitId);
                        if (targetUnit != null)
                        {
                            int incoming = 1 + (usesCoOcc && coOccupants != null
                                ? coOccupants.Count(c => !string.IsNullOrWhiteSpace(c.name))
                                : 0);

                            if (targetUnit.maxOccupants > 0 && incoming > targetUnit.maxOccupants)
                            {
                                return Json(new
                                {
                                    success = false,
                                    message = "This unit only holds " + targetUnit.maxOccupants + " occupant(s)."
                                }, JsonRequestBehavior.AllowGet);
                            }
                        }
                    }

                    // ================================================================
                    // INSERT
                    // ================================================================
                    if (existingTenant == null)
                    {
                        // Block a second main tenant on an already-occupied unit
                        if (tenantData.unitId > 0)
                        {
                            bool alreadyTaken = connect.tenant.Any(t =>
                                t.unitId == tenantData.unitId && t.isTerminated != 1);

                            if (alreadyTaken)
                            {
                                return Json(new
                                {
                                    success = false,
                                    message = "That unit already has a tenant. To add a bedspacer, edit the existing tenant and add them as an occupant."
                                }, JsonRequestBehavior.AllowGet);
                            }
                        }

                        string newTenantNumber = tenantData.tenantNumber;
                        if (string.IsNullOrEmpty(newTenantNumber))
                            newTenantNumber = "T-" + DateTime.Now.ToString("yyyyMMddHHmm");

                        var newTenant = new tenant
                        {
                            tenantNumber = newTenantNumber,
                            name = tenantData.name,
                            email = tenantData.email,
                            phone = tenantData.phone,
                            address = tenantData.address,
                            occupation = tenantData.occupation,
                            occupancyTypeId = tenantData.occupancyTypeId,
                            passwordHash = BCrypt.Net.BCrypt.HashPassword(tenantData.passwordHash),
                            unitId = tenantData.unitId,
                            isTerminated = 0,
                            leaseStart = tenantData.leaseStart,
                            leaseEnd = tenantData.leaseEnd
                        };

                        connect.tenant.Add(newTenant);
                        connect.SaveChanges();

                        // ---- Mark the unit occupied and create the first bill ----
                        if (newTenant.unitId > 0)
                        {
                            var assignedUnit = connect.unit.FirstOrDefault(u => u.Uid == newTenant.unitId);
                            if (assignedUnit != null)
                            {
                                assignedUnit.status = "active";

                                connect.payment.Add(new payment
                                {
                                    Tid = newTenant.Tid,
                                    Uid = assignedUnit.Uid,
                                    amount = assignedUnit.price,
                                    dueDate = DateTime.Now,
                                    paidDate = DateTime.Now,
                                    billingPeriod = DateTime.Now.ToString("MMMM yyyy")
                                });

                                connect.SaveChanges();
                            }
                        }

                        // ---- Co-occupants: Household (2) AND Bedspace (3) ----
                        if (usesCoOcc && coOccupants != null)
                        {
                            foreach (var occ in coOccupants)
                            {
                                if (string.IsNullOrWhiteSpace(occ.name)) continue;

                                connect.co_occupant.Add(new co_occupant
                                {
                                    Tid = newTenant.Tid,
                                    name = occ.name,
                                    phone = occ.phone,
                                    address = occ.address
                                });
                            }
                            connect.SaveChanges();
                        }

                        // ---- ID documents ----
                        if (idFiles != null)
                        {
                            foreach (var file in idFiles)
                            {
                                if (string.IsNullOrWhiteSpace(file.url)) continue;

                                connect.tenant_document.Add(new tenant_document
                                {
                                    Tid = newTenant.Tid,
                                    fileName = file.name,
                                    fileUrl = file.url,
                                    fileType = "ID Document"
                                });
                            }
                            connect.SaveChanges();
                        }

                        transaction.Commit();
                        committed = true;
                        CacheHelper.Remove("all-tenants");
                        CacheHelper.Remove("all-units");
                        CacheHelper.Remove("all-browse-units");
                        AuditLogger.Log("user", "info", "Created tenant: " + newTenant.name, CurrentUserName());
                        return Json(new { success = true, message = "Tenant Saved Successfully" },
                                    JsonRequestBehavior.AllowGet);
                    }

                    // ================================================================
                    // UPDATE
                    // ================================================================
                    int previousUnitId = existingTenant.unitId;

                    existingTenant.name = tenantData.name;
                    existingTenant.email = tenantData.email;
                    existingTenant.phone = tenantData.phone;
                    existingTenant.address = tenantData.address;
                    existingTenant.occupation = tenantData.occupation;
                    existingTenant.occupancyTypeId = tenantData.occupancyTypeId;
                    existingTenant.unitId = tenantData.unitId;
                    existingTenant.leaseStart = tenantData.leaseStart;
                    existingTenant.leaseEnd = tenantData.leaseEnd;

                    if (!string.IsNullOrEmpty(tenantData.passwordHash))
                        existingTenant.passwordHash = BCrypt.Net.BCrypt.HashPassword(tenantData.passwordHash);

                    connect.SaveChanges();

                    // ---- Co-occupant deletions ----
                    if (tenantData.deletedCoOccupants != null && tenantData.deletedCoOccupants.Any())
                    {
                        var toDelete = connect.co_occupant
                            .Where(c => tenantData.deletedCoOccupants.Contains(c.id))
                            .ToList();

                        connect.co_occupant.RemoveRange(toDelete);
                        connect.SaveChanges();
                    }

                    // ---- Co-occupant adds and edits: Household (2) AND Bedspace (3) ----
                    if (usesCoOcc && coOccupants != null)
                    {
                        foreach (var occ in coOccupants)
                        {
                            if (occ.id == 0)
                            {
                                if (string.IsNullOrWhiteSpace(occ.name)) continue;

                                connect.co_occupant.Add(new co_occupant
                                {
                                    Tid = existingTenant.Tid,
                                    name = occ.name,
                                    phone = occ.phone,
                                    address = occ.address
                                });
                            }
                            else
                            {
                                var existingOcc = connect.co_occupant.FirstOrDefault(c => c.id == occ.id);
                                if (existingOcc != null)
                                {
                                    existingOcc.name = occ.name;
                                    existingOcc.phone = occ.phone;
                                    existingOcc.address = occ.address;
                                }
                            }
                        }
                        connect.SaveChanges();
                    }
                    else if (tenantData.occupancyTypeId == 1)
                    {
                        // Switched to Single Occupant -> clear all co-occupants
                        var all = connect.co_occupant
                            .Where(c => c.Tid == existingTenant.Tid)
                            .ToList();

                        if (all.Any())
                        {
                            connect.co_occupant.RemoveRange(all);
                            connect.SaveChanges();
                        }
                    }

                    // ---- Document deletions ----
                    if (deletedDocumentIds != null && deletedDocumentIds.Any())
                    {
                        var docsToDelete = connect.tenant_document
                            .Where(d => d.Tid == existingTenant.Tid && deletedDocumentIds.Contains(d.id))
                            .ToList();

                        connect.tenant_document.RemoveRange(docsToDelete);
                        connect.SaveChanges();
                    }

                    // ---- New documents only ----
                    if (idFiles != null)
                    {
                        foreach (var file in idFiles)
                        {
                            if (file.id != 0) continue;
                            if (string.IsNullOrWhiteSpace(file.url)) continue;

                            connect.tenant_document.Add(new tenant_document
                            {
                                Tid = existingTenant.Tid,
                                fileName = file.name,
                                fileUrl = file.url,
                                fileType = "ID Document"
                            });
                        }
                        connect.SaveChanges();
                    }

                    // ---- Unit reassignment: free the old one ----
                    if (previousUnitId != existingTenant.unitId && previousUnitId > 0)
                    {
                        bool stillOccupied = connect.tenant
                            .Any(t => t.unitId == previousUnitId
                                      && t.Tid != existingTenant.Tid
                                      && t.isTerminated != 1);

                        if (!stillOccupied)
                        {
                            var oldUnit = connect.unit.FirstOrDefault(u => u.Uid == previousUnitId);
                            if (oldUnit != null)
                            {
                                oldUnit.status = "active";
                                connect.SaveChanges();
                            }
                        }
                    }

                    transaction.Commit();
                    committed = true;
                    CacheHelper.Remove("all-tenants");
                    CacheHelper.Remove("all-units");
                    CacheHelper.Remove("all-browse-units");
                    AuditLogger.Log("user", "info", "Updated tenant: " + existingTenant.name, CurrentUserName());
                    return Json(new { success = true, message = "Tenant Saved Successfully" },
                                JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) },
                            JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (!committed)
                {
                    foreach (var path in uploadedPaths)
                    {
                        try { System.IO.File.Delete(path); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }

        private string SaveTenantIdImage(HttpPostedFileBase upload, List<string> uploadedPaths)
        {
            if (upload == null || upload.ContentLength == 0 || upload.ContentLength > 10 * 1024 * 1024)
                throw new InvalidOperationException("Each ID image must be between 1 byte and 10 MB.");
            if (Path.GetFileName(upload.FileName).Length > 255)
                throw new InvalidOperationException("The ID image filename must be 255 characters or fewer.");

            string extension;
            try
            {
                using (var image = System.Drawing.Image.FromStream(upload.InputStream, false, true))
                {
                    var format = image.RawFormat;
                    if (format.Equals(System.Drawing.Imaging.ImageFormat.Jpeg)) extension = ".jpg";
                    else if (format.Equals(System.Drawing.Imaging.ImageFormat.Png)) extension = ".png";
                    else if (format.Equals(System.Drawing.Imaging.ImageFormat.Gif)) extension = ".gif";
                    else if (format.Equals(System.Drawing.Imaging.ImageFormat.Bmp)) extension = ".bmp";
                    else if (format.Equals(System.Drawing.Imaging.ImageFormat.Tiff)) extension = ".tif";
                    else throw new ArgumentException();
                }
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("Please upload a valid JPG, PNG, GIF, BMP, or TIFF ID image.");
            }

            string fileName = Guid.NewGuid().ToString("N") + extension;
            string folder = Server.MapPath("~/App_Data/TenantIds");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            uploadedPaths.Add(path);
            upload.SaveAs(path);
            return Url.Content("~/System/TenantIdImage") + "?name=" + fileName;
        }

        [HttpGet]
        [CheckSession(AllowedRoles = new[] { 1, 2 })]
        public ActionResult TenantIdImage(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name))
                return HttpNotFound();
            string path = Server.MapPath("~/App_Data/TenantIds/" + name);
            if (!System.IO.File.Exists(path)) return HttpNotFound();
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            return File(path, MimeMapping.GetMimeMapping(name));
        }

        [HttpPost]
        public JsonResult SaveUnit(unit data, List<string> imageUrls, string amenityIds, int? id)
        {
            string inputError = CheckAndCleanUnit(data);
            if (inputError != null)
                return Json(new { success = false, message = inputError }, JsonRequestBehavior.AllowGet);

            try
            {
                using (var connect = new DB_Context())
                {
                    // ---- Parse "1,3,5" into a list of ints ----
                    var parsedAmenityIds = new List<int>();
                    if (!string.IsNullOrEmpty(amenityIds))
                    {
                        parsedAmenityIds = amenityIds.Split(',')
                                                     .Where(x => !string.IsNullOrWhiteSpace(x))
                                                     .Select(int.Parse)
                                                     .ToList();
                    }

                    // ---- Cap images ----
                    var images = (imageUrls ?? new List<string>())
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Take(10)
                        .ToList();

                    var existingUnit = connect.unit.FirstOrDefault(x => x.Uid == id);
                    int unitId;

                    if (existingUnit == null)
                    {
                        // ================= INSERT =================
                        var unitData = new unit
                        {
                            unitName = data.unitName,
                            price = data.price,
                            beds = data.beds,
                            sqm = data.sqm,
                            floor = data.floor,
                            description = data.description,
                            videoUrl = data.videoUrl,
                            status = data.status,
                            address = data.address,
                            maxOccupants = data.maxOccupants
                        };

                        connect.unit.Add(unitData);
                        connect.SaveChanges();

                        unitId = unitData.Uid;
                    }
                    else
                    {
                        // ================= UPDATE =================
                        existingUnit.unitName = data.unitName;
                        existingUnit.price = data.price;
                        existingUnit.beds = data.beds;
                        existingUnit.sqm = data.sqm;
                        existingUnit.floor = data.floor;
                        existingUnit.description = data.description;
                        existingUnit.videoUrl = data.videoUrl;
                        existingUnit.status = data.status;
                        existingUnit.address = data.address;
                        existingUnit.maxOccupants = data.maxOccupants;

                        connect.SaveChanges();

                        unitId = existingUnit.Uid;
                    }

                    // ---- IMAGES: clear and re-insert, for both insert and update ----
                    var oldImages = connect.unit_image.Where(x => x.Uid == unitId).ToList();
                    if (oldImages.Any())
                    {
                        connect.unit_image.RemoveRange(oldImages);
                        connect.SaveChanges();
                    }

                    for (int i = 0; i < images.Count; i++)
                    {
                        connect.unit_image.Add(new unit_image
                        {
                            Uid = unitId,
                            imageUrl = images[i],
                            displayOrder = i
                        });
                    }

                    // ---- AMENITIES: same clear-and-reinsert ----
                    var oldAmenities = connect.unit_amenity.Where(x => x.Uid == unitId).ToList();
                    if (oldAmenities.Any())
                    {
                        connect.unit_amenity.RemoveRange(oldAmenities);
                        connect.SaveChanges();
                    }

                    foreach (var aId in parsedAmenityIds)
                    {
                        connect.unit_amenity.Add(new unit_amenity
                        {
                            Uid = unitId,
                            amenityId = aId
                        });
                    }

                    connect.SaveChanges();
                    CacheHelper.Remove("all-browse-units");
                    CacheHelper.Remove("all-units");
                    AuditLogger.Log("settings", "info",
                        (existingUnit == null ? "Created unit: " : "Updated unit: ") + data.unitName, CurrentUserName());
                    return Json(new
                    {
                        success = true,
                        message = "Unit Saved Successfully",
                        imageCount = images.Count
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) },
                            JsonRequestBehavior.AllowGet);
            }
        }



        [HttpPost]
        public JsonResult ConfirmBooking(int id)
        {
            using (var connect = new DB_Context())
            {
                var booking = connect.booking.FirstOrDefault(b => b.id == id);
                if (booking != null)
                {
                    booking.status = "Confirmed";
                    connect.SaveChanges();

                    AuditLogger.Log("settings", "info", "Confirmed booking: " + booking.guestName, CurrentUserName());

                    var unit = connect.unit.FirstOrDefault(u => u.Uid == booking.Uid);
                    var unitName = unit?.unitName ?? "the unit";

                    EmailServices.SendBookingConfirmed(
                        booking.guestEmail,
                        booking.guestName,
                        unitName,
                        booking.bookingDatetime
                    );

                    return Json(new { success = true, message = "Booking confirmed!" });
                }
                return Json(new { success = false, message = "Booking not found." });
            }
        }

        [HttpPost]
        public JsonResult DeclineBooking(int id, string reason)
        {
            string reasonError = InputValidator.Description(reason, "Reason", true, 300);
            if (reasonError != null)
                return Json(new { success = false, message = reasonError });
            reason = InputValidator.CleanDescription(reason);

            using (var connect = new DB_Context())
            {
                var booking = connect.booking.FirstOrDefault(b => b.id == id);
                if (booking != null)
                {
                    booking.status = "Declined";
                    booking.cancelReason = reason;
                    connect.SaveChanges();

                    AuditLogger.Log("settings", "warning", "Declined booking: " + booking.guestName, CurrentUserName());

                    var unit = connect.unit.FirstOrDefault(u => u.Uid == booking.Uid);
                    var unitName = unit?.unitName ?? "the unit";

                    EmailServices.SendBookingDeclined(
                        booking.guestEmail,
                        booking.guestName,
                        unitName,
                        booking.bookingDatetime,
                        reason
                    );

                    return Json(new { success = true, message = "Booking declined." });
                }
                return Json(new { success = false, message = "Booking not found." });
            }
        }

        [HttpPost]
        public JsonResult ToggleBusyDate(string dateStr)
        {
            try
            {
                using (var connect = new DB_Context()) // Use your actual DbContext
                {
                    // Convert the string "2026-05-12" into a proper C# DateTime
                    if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                    {
                        // Check if this exact date is already in the database
                        var existingDate = connect.busy_schedule.FirstOrDefault(b => b.busyDate == parsedDate);

                        if (existingDate != null)
                        {
                            // It exists! That means the admin clicked an already-busy day to free it up.
                            connect.busy_schedule.Remove(existingDate);
                            connect.SaveChanges();

                            AuditLogger.Log("settings", "warning", "Unblocked date: " + parsedDate.ToString("MMM d, yyyy"), CurrentUserName());
                            return Json(new { success = true, action = "removed" });
                        }
                        else
                        {
                            // It doesn't exist! The admin wants to block this day off.
                            var newBusy = new busy_schedule
                            {
                                busyDate = parsedDate
                            };

                            connect.busy_schedule.Add(newBusy);
                            connect.SaveChanges();

                            AuditLogger.Log("settings", "info", "Blocked date: " + parsedDate.ToString("MMM d, yyyy"), CurrentUserName());
                            return Json(new { success = true, action = "added" });
                        }
                    }

                    return Json(new { success = false, message = "Invalid date format." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        [HttpPost]
        public JsonResult RemoveBusyDate(string dateStr)
        {
            try
            {
                using (var connect = new DB_Context()) // Use your actual DB Context
                {
                    // CASE 1: Clear EVERYTHING
                    if (dateStr == "ALL")
                    {
                        var allDates = connect.busy_schedule.ToList();
                        connect.busy_schedule.RemoveRange(allDates);
                        connect.SaveChanges();

                        AuditLogger.Log("settings", "warning", "Cleared all busy dates", CurrentUserName());
                        return Json(new { success = true, message = "All busy dates cleared." });
                    }

                    // CASE 2: Delete just a SINGLE date
                    else if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                    {
                        var targetDate = connect.busy_schedule.FirstOrDefault(b => b.busyDate == parsedDate);

                        if (targetDate != null)
                        {
                            connect.busy_schedule.Remove(targetDate);
                            connect.SaveChanges();

                            AuditLogger.Log("settings", "warning", "Removed busy date: " + parsedDate.ToString("MMM d, yyyy"), CurrentUserName());
                        }

                        return Json(new { success = true, message = "Busy date removed." });
                    }

                    return Json(new { success = false, message = "Invalid command or date format." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Server error: " + ex.Message });
            }
        }
        [HttpPost]
        public JsonResult SaveMaintenanceRequest(maintenance_request requestData, int? id)
        {
            string inputError = CheckAndCleanMaintenance(requestData);
            if (inputError != null)
                return Json(new { success = false, message = inputError }, JsonRequestBehavior.AllowGet);

            try
            {
                using (var connect = new DB_Context())
                {
                    // 1. Intelligently find the correct Tenant ID based on the chosen Unit
                    var activeTenant = connect.tenant.FirstOrDefault(t =>
                        t.unitId == requestData.Uid);

                    int tid = activeTenant != null ? activeTenant.Tid : 0;

                    // 2. CHECK IF INSERT OR UPDATE
                    if (requestData.id == 0)
                    {
                        var newRequest = new maintenance_request
                        {
                            Uid = requestData.Uid,
                            Tid = requestData.Tid, // <-- Direct assignment!
                            category = requestData.category,
                            description = requestData.description,
                            status = "Pending",
                            reportedDate = DateTime.Now,
                        };

                        connect.maintenance_request.Add(newRequest);
                        connect.SaveChanges();

                        AuditLogger.Log("settings", "info", "Created maintenance request", CurrentUserName());
                        return Json(new { success = true, message = "Request saved successfully" }, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        // --- B. UPDATE EXISTING REQUEST ---
                        var existingRequest = connect.maintenance_request.FirstOrDefault(m => m.id == requestData.id);

                        if (existingRequest != null)
                        {

                            existingRequest.status = requestData.status;
                            existingRequest.scheduledDate = requestData.scheduledDate;

                            connect.SaveChanges();

                            AuditLogger.Log("settings", "info", "Updated maintenance status: " + existingRequest.status, CurrentUserName());
                            return Json(new { success = true, newId = existingRequest.id, message = "Request saved successfully" }, JsonRequestBehavior.AllowGet);
                        }
                        else
                        {
                            return Json(new { success = false, message = "Request not found" }, JsonRequestBehavior.AllowGet);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        [HttpPost]
        public JsonResult MarkPaymentsPaid(int Tid, int Uid, decimal amount, List<MonthItem> months)
        {
            if (amount <= 0 || amount > 99999999.99m || decimal.Round(amount, 2) != amount)
                return Json(new { success = false, message = "Amount must be a positive amount with at most 2 decimal places." });

            try
            {
                using (var db = new DB_Context())
                {
                    foreach (var m in months)
                    {
                        // Match by Tid, Uid, and dueDate month/year
                        var existing = db.payment.FirstOrDefault(p =>
                            p.Tid == Tid &&
                            p.Uid == Uid &&
                            p.dueDate.Month == m.Month &&
                            p.dueDate.Year == m.Year &&
                            p.paidDate == null
                        );

                        if (existing != null)
                        {
                            // UPDATE — record exists but unpaid
                            existing.paidDate = DateTime.Now;
                        }
                        else
                        {
                            // Check if already paid (skip) or truly missing (insert)
                            bool alreadyPaid = db.payment.Any(p =>
                                p.Tid == Tid &&
                                p.Uid == Uid &&
                                p.dueDate.Month == m.Month &&
                                p.dueDate.Year == m.Year &&
                                p.paidDate != null
                            );

                            if (!alreadyPaid)
                            {
                                // INSERT — no record at all for this month
                                db.payment.Add(new payment
                                {
                                    Tid = Tid,
                                    Uid = Uid,
                                    amount = amount,
                                    dueDate = new DateTime(m.Year, m.Month, 1),
                                    paidDate = DateTime.Now,
                                    billingPeriod = new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy")
                                });
                            }
                        }
                    }

                    db.SaveChanges();
                    AuditLogger.Log("settings", "info", "Marked payments paid for tenant #" + Tid, CurrentUserName());
                    return Json(new { success = true, message = "Payments marked as paid." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) });
            }
        }
        // Get API
        public JsonResult GetDashboardData()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var currentDate = DateTime.Now.Date;

                    // 1. Existing Top-Level Metrics
                    var requests = connect.maintenance_request
                         .Where(m => m.status == "Pending")
                         .Select(m => m.status).ToList();

                    var units = connect.unit
                        .Where(m => m.status == "active")
                        .Select(m => m.status).ToList();

                    var bookings = connect.booking
                        .Where(m => m.status == "Pending")
                        .Select(m => m.status).ToList();

                    var payments = connect.payment
                        .Where(m => m.paidDate != null)
                        .Sum(m => (decimal?)m.amount) ?? 0;

                    // 2. Recent Bookings List
                    var raw = (from b in connect.booking
                               join u in connect.unit on b.Uid equals u.Uid
                               orderby b.bookingDatetime descending
                               select new
                               {
                                   id = b.id,
                                   name = b.guestName,
                                   unitName = u.unitName,
                                   bookingDate = b.bookingDatetime,
                                   status = b.status
                               }).Take(5).ToList();

                    var recentBookings = raw.Select(b => new
                    {
                        id = b.id,
                        name = b.name,
                        unitName = b.unitName,
                        bookingDate = b.bookingDate.ToString("yyyy-MM-dd HH:mm:ss"),
                        status = b.status
                    }).ToList();

                    // 3. Monthly Revenue Logic
                    var monthlyRevenue = connect.payment
                         .Where(x => x.paidDate != null)
                         .Select(x => new
                         {
                             Year = x.paidDate.Value.Year,
                             Month = x.paidDate.Value.Month,
                             x.amount
                         })
                         .GroupBy(x => new { x.Year, x.Month })
                         .Select(p => new
                         {
                             Year = p.Key.Year,
                             Month = p.Key.Month,
                             Revenue = p.Sum(x => x.amount)
                         })
                         .OrderBy(x => x.Year)
                         .ThenBy(x => x.Month)
                         .ToList();

                    // I-load ang mga tables para sa occupancy computation
                    var allUnits = connect.unit.ToList();
                    var allTenants = connect.tenant.ToList();

                    // --------------------------------------------------------
                    // BAGONG LOGIC: CURRENT VACANT UNITS (Pang-Dashboard Card)
                    // --------------------------------------------------------
                    int totalActiveUnitsCount = allUnits.Count(u => u.status == "active");

                    var occupiedUnitIdsToday = allTenants
                    .Where(t =>
                        t.leaseStart.Date <= currentDate &&
                        t.leaseEnd.Date >= currentDate &&
                        t.isTerminated == 0       // 2. Sinigurado na hindi terminated ang tenant

                    )
                    .Select(t => t.unitId)
                    .Distinct()
                    .ToList();

                    int currentVacantUnits = Math.Max(0, totalActiveUnitsCount - occupiedUnitIdsToday.Count);


                    // --------------------------------------------------------
                    // BAGONG LOGIC: MONTHLY OCCUPIED VS VACANT UNITS (Pang-Chart)
                    // --------------------------------------------------------
                    var monthlyOccupant = allTenants
                        .Select(t => new
                        {
                            Year = t.leaseStart.Year,
                            Month = t.leaseStart.Month
                        })
                        .Distinct()
                        .OrderBy(x => x.Year)
                        .ThenBy(x => x.Month)
                        .Select(period =>
                        {
                            // Kunin lahat ng tenants na active sa buwang ito
                            var tenantThisMonth = allTenants
                                .Where(t =>
                                    (t.leaseStart.Year < period.Year ||
                                        (t.leaseStart.Year == period.Year && t.leaseStart.Month <= period.Month)) &&
                                    (t.leaseEnd.Year > period.Year ||
                                        (t.leaseEnd.Year == period.Year && t.leaseEnd.Month >= period.Month))
                                ).ToList();

                            // Bilangin kung ilang DISTINCT units ang occupied this month
                            int occupiedUnitsCount = tenantThisMonth
                                .Select(t => t.unitId)
                                .Distinct()
                                .Count();

                            // I-subtract sa total active units para makuha ang vacant units
                            int vacantUnitsCount = Math.Max(0, totalActiveUnitsCount - occupiedUnitsCount);

                            return new
                            {
                                Year = period.Year,
                                Month = period.Month,
                                OccupiedUnits = occupiedUnitsCount,
                                VacantUnits = vacantUnitsCount
                            };
                        })
                        .ToList();

                    // I-return ang lahat sa frontend
                    return Json(new
                    {
                        success = true,
                        activeUnitNum = units,
                        pendingRequestNum = requests,
                        pendingBookNum = bookings,
                        totalPayment = payments,
                        recentBookings = recentBookings,
                        monthlyRevenue = monthlyRevenue,
                        monthlyOccupant = monthlyOccupant,
                        currentVacantUnits = currentVacantUnits, // Idinagdag dito
                        message = "Get Data Success"
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [CheckSession(AllowedRoles = new[] { 1 })]
        public ActionResult GetDashboardDataAdmin()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var activeWindow = DateTime.Now.AddMinutes(-5);

                    // ---- 1. Active users (admin + tenant by lastActive) ----
                    int adminTotal = connect.admin.Count();
                    int tenantTotal = connect.tenant.Count();
                    int adminActive = connect.admin.Count(a => a.lastActive != null && a.lastActive >= activeWindow);
                    int tenantActive = connect.tenant.Count(t => t.lastActive != null && t.lastActive >= activeWindow);

                    int totalUsers = adminTotal + tenantTotal;
                    int activeUsers = adminActive + tenantActive;

                    // ---- 2. Accounts card (admin table) ----
                    var accounts = connect.admin
                        .OrderByDescending(a => a.lastActive)
                        .ToList()
                        .Select(a => new
                        {
                            name = a.name,   // ← swap for your real admin columns
                            email = a.email,
                            status = (a.lastActive != null && a.lastActive >= activeWindow) ? "active" : "inactive"
                        })
                        .ToList();

                    // ---- 3. Security alerts (from audit_log) ----
                    int criticalAlerts = connect.audit_log.Count(x => x.severity == "critical");
                    int warningAlerts = connect.audit_log.Count(x => x.severity == "warning");

                    // ---- 4. Recent activity ----
                    var logs = connect.audit_log
                        .OrderByDescending(x => x.createdAt)
                        .Take(8)
                        .ToList();

                    var recentActivity = logs.Select(x => new
                    {
                        type = x.type,
                        message = x.message,
                        actor = x.actor,
                        time = RelativeTime(x.createdAt)
                    }).ToList();

                    // ---- 5. Return (flat, camelCase) ----
                    return Json(new
                    {
                        activeManagers = activeUsers,
                        lockedManagers = totalUsers - activeUsers,
                        totalAccounts = totalUsers,
                        securityAlerts = criticalAlerts + warningAlerts,
                        criticalAlerts = criticalAlerts,
                        warningAlerts = warningAlerts,
                        propertyManagers = accounts,
                        recentActivity = recentActivity
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Relative time — uses DateTime.Now to match your local-time convention
        private static string RelativeTime(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + "m ago";
            if (span.TotalHours < 24) return (int)span.TotalHours + "h ago";
            if (span.TotalDays < 7) return (int)span.TotalDays + "d ago";
            return when.ToString("MMM d");
        }
        [HttpGet]
        public JsonResult GetAllAmenities()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var amenities = connect.amenity
                        .Select(a => new
                        {
                            id = a.id,
                            name = a.name
                        })
                        .ToList();

                    return Json(new { success = true, data = amenities }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST /System/AddAmenity
        [HttpPost]
        [CheckSession(AllowedRoles = new[] { 1 })]
        [ValidateAntiForgeryToken]
        public JsonResult AddAmenity(string name)
        {
            try
            {
                string nameError = InputValidator.Code(name, "Amenity name", true, 100);
                if (nameError != null)
                    return Json(new { success = false, message = nameError });

                name = InputValidator.CleanText(name);
                if (name.Length > 100)
                    return Json(new { success = false, message = "Amenity names cannot exceed 100 characters." });

                using (var connect = new DB_Context())
                {
                    if (connect.amenity.Any(a => a.name == name))
                        return Json(new { success = false, message = "That amenity already exists." });

                    var newAmenity = new amenity { name = name };
                    connect.amenity.Add(newAmenity);
                    connect.SaveChanges();
                    CacheHelper.Remove("all-units");
                    CacheHelper.Remove("all-browse-units");
                    CacheHelper.Remove("amenities"); // keep cache fresh (you cache GetAllAmenities)
                    AuditLogger.Log("settings", "info", "Added amenity: " + name, CurrentUserName());

                    return Json(new { success = true, id = newAmenity.id, name = name });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        // POST /System/DeleteAmenity
        [HttpPost]
        [CheckSession(AllowedRoles = new[] { 1 })]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteAmenity(int id)
        {
            try
            {
                if (id <= 0)
                    return Json(new { success = false, message = "Invalid amenity." });

                using (var connect = new DB_Context())
                {
                    var target = connect.amenity.FirstOrDefault(a => a.id == id);
                    if (target == null)
                        return Json(new { success = false, message = "Amenity not found." });

                    string name = target.name;

                    int assignedUnits = connect.unit_amenity.Count(a => a.amenityId == id);
                    if (assignedUnits > 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "This amenity is assigned to " + assignedUnits
                                + " unit(s). Unassign it from those units before deleting it."
                        });
                    }

                    connect.amenity.Remove(target);
                    connect.SaveChanges();


                    CacheHelper.Remove("amenities");
                    AuditLogger.Log("settings", "warning", "Removed amenity: " + name, CurrentUserName());

                    return Json(new { success = true, message = "Amenity Successfully Deleted." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpGet]
        public ActionResult GetAllUnit()
        {
            try
            {
                var payload = CacheHelper.GetOrAdd(
                    "all-units",
                    1,
                    () =>
                    {
                        using (var connect = new DB_Context())
                        {
                            var today = DateTime.Today;
                            var expiringThresHold = today.AddDays(45);

                            var units = connect.unit.ToList();
                            var tenants = connect.tenant.ToList();
                            var amenities = connect.amenity.ToList();
                            var unitAmenities = connect.unit_amenity.ToList();

                            // Cover image only. Pulling every image for every unit is what
                            // blew past the serializer limit — the grid shows one thumbnail,
                            // and the editor fetches the full set via GetUnitImages.
                            var coverImages = connect.unit_image
                                .GroupBy(i => i.Uid)
                                .Select(g => g.OrderBy(i => i.displayOrder).FirstOrDefault())
                                .ToList();

                            var result = units.Select(u =>
                            {
                                var imageCounts = connect.unit_image
                                    .GroupBy(i => i.Uid)
                                    .Select(g => new
                                    {
                                        Uid = g.Key,
                                        Count = g.Count()
                                    })
                                    .ToDictionary(x => x.Uid, x => x.Count);
                                var tenant = tenants.FirstOrDefault(t =>
                                    t.unitId == u.Uid
                                );

                                string occupancy;
                                if (tenant == null)
                                {
                                    occupancy = "Vacant";
                                }
                                else if (tenant.leaseEnd <= expiringThresHold)
                                {
                                    occupancy = "Expiring";
                                }
                                else
                                {
                                    occupancy = "Occupied";
                                }

                                var unitAmenityList = unitAmenities
                                    .Where(ua => ua.Uid == u.Uid)
                                    .Join(amenities,
                                        ua => ua.amenityId,
                                        a => a.id,
                                        (ua, a) => new
                                        {
                                            a.id,
                                            a.name
                                        })
                                    .ToList();

                                var cover = coverImages.FirstOrDefault(i => i != null && i.Uid == u.Uid);
                                return new
                                {
                                    u.Uid,
                                    u.unitName,
                                    u.price,
                                    u.beds,
                                    u.sqm,
                                    u.floor,
                                    u.description,
                                    u.videoUrl,
                                    u.status,
                                    u.address,
                                    u.maxOccupants,
                                    occupancy,

                                    // Same shape as before: [{ imageUrl: "..." }], just capped at one
                                    images = cover == null
                                        ? new List<object>()
                                        : new List<object> { new { cover.imageUrl } },

                                    // How many there really are, for the grid badge
                                    imageCount = imageCounts.ContainsKey(u.Uid)
                                        ? imageCounts[u.Uid]
                                        : 0,

                                    amenities = unitAmenityList
                                };
                            }).ToList();

                            return new { success = true, data = result };
                        }
                    }

                    );
                return LargeJson(payload);
            }
            catch (Exception ex)
            {
                return LargeJson(new { success = false, message = ErrorHandling(ex) });
            }
        }
        [HttpGet]
        public ActionResult GetUnitImages(int uid)
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var images = connect.unit_image
                        .Where(i => i.Uid == uid)
                        .OrderBy(i => i.displayOrder)
                        .Select(i => i.imageUrl)
                        .ToList();

                    return LargeJson(new { success = true, data = images });
                }
            }
            catch (Exception ex)
            {
                return LargeJson(new { success = false, message = ex.Message });
            }
        }

        public JsonResult GetAllTenant()
        {
            try
            {
                var payload = CacheHelper.GetOrAdd(
                    "all-tenants",
                    1,
                    () =>
                    {
                        using (var connect = new DB_Context())
                        {
                            var today = DateTime.Today;
                            var expiringThreshold = today.AddDays(45);

                            var tenants = connect.tenant.ToList();
                            var units = connect.unit.ToList();
                            var coOccupants = connect.co_occupant.ToList();
                            var tenantDocuments = connect.tenant_document.ToList();

                            var raw = (from t in tenants
                                       join u in units on t.unitId equals u.Uid into unitGroup
                                       from u in unitGroup.DefaultIfEmpty()
                                       select new
                                       {
                                           t.Tid,
                                           t.tenantNumber,
                                           t.name,
                                           t.email,
                                           t.phone,
                                           t.address,
                                           t.occupation,
                                           t.leaseStart,
                                           t.leaseEnd,
                                           t.isTerminated,
                                           t.unitId,
                                           t.occupancyTypeId,
                                           t.passwordHash,
                                           unitName = u != null ? u.unitName : null,
                                           maxOccupants = u != null ? u.maxOccupants : 0,
                                           coOccupantsList = coOccupants.Where(c => c.Tid == t.Tid).Select(c => new
                                           {
                                               c.id,
                                               c.Tid,
                                               c.name,
                                               c.phone,
                                               c.address
                                           }).ToList(),
                                           additionalOccupantsCount = coOccupants.Count(c => c.Tid == t.Tid),
                                           idFiles = tenantDocuments
                                               .Where(d => d.Tid == t.Tid)
                                               .Select(d => new
                                               {
                                                   id = d.id,
                                                   url = d.fileUrl,
                                                   fileName = d.fileName,
                                                   fileType = d.fileType
                                               })
                                               .ToList()
                                       }).ToList();

                            var data = raw.Select(t =>
                            {
                                // Occupancy label from the type id
                                string occupancyLabel;
                                switch (t.occupancyTypeId)
                                {
                                    case 1: occupancyLabel = "Single Occupant"; break;
                                    case 2: occupancyLabel = "Household"; break;
                                    case 3: occupancyLabel = "Bedspace"; break;
                                    default: occupancyLabel = "\u2014"; break;
                                }

                                // Headcount = main tenant + co-occupants
                                int totalOccupants = 1 + t.additionalOccupantsCount;
                                int slotsOpen = t.maxOccupants > 0
                                    ? Math.Max(0, t.maxOccupants - totalOccupants)
                                    : 0;

                                var daysLeft = (int?)(t.leaseEnd.Date - today).TotalDays;

                                string liveStatus;
                                if (t.isTerminated == 1) liveStatus = "Terminated";
                                else if (t.leaseEnd < today) liveStatus = "Expired";
                                else if (t.leaseEnd <= expiringThreshold) liveStatus = "Expiring";
                                else liveStatus = "Active";

                                var leaseDurationMonths = ((t.leaseEnd.Year - t.leaseStart.Year) * 12)
                                                          + t.leaseEnd.Month - t.leaseStart.Month;
                                string contractType;
                                if (leaseDurationMonths <= 3) contractType = "Short-term";
                                else if (leaseDurationMonths <= 6) contractType = "Mid-term";
                                else contractType = "Long-term";

                                return new
                                {
                                    Tid = t.Tid,
                                    tenantNumber = t.tenantNumber,
                                    name = t.name,
                                    email = t.email,
                                    phone = t.phone,
                                    address = t.address,
                                    occupation = t.occupation,
                                    status = liveStatus,
                                    coOccupants = t.coOccupantsList,
                                    maxOccupants = t.maxOccupants,

                                    leaseStart = t.leaseStart.ToString("yyyy-MM-dd"),
                                    leaseEnd = t.leaseEnd.ToString("yyyy-MM-dd"),

                                    isTerminated = t.isTerminated == 1,
                                    unit = t.unitName,
                                    unitId = t.unitId,

                                    occupancyTypeId = t.occupancyTypeId,
                                    occupancyLabel = occupancyLabel,
                                    additionalOccupantsCount = t.additionalOccupantsCount,
                                    totalOccupants = totalOccupants,
                                    slotsOpen = slotsOpen,
                                    isJoinable = (t.occupancyTypeId == 3 && slotsOpen > 0 && t.isTerminated != 1),

                                    daysLeft = daysLeft,
                                    liveStatus = liveStatus,
                                    contractType = contractType,
                                    latestPaymentStatus = "None",

                                    idFiles = t.idFiles,
                                    idFilesCount = t.idFiles.Count
                                };
                            }).ToList();

                            return new { success = true, data = data };

                        }
                    }
                    );
                return Json(payload, JsonRequestBehavior.AllowGet);

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }


        public JsonResult GetAllBooking()
        {
            try
            {
                using (var connect = new DB_Context()) // Use your actual DbContext name
                {
                    // 1. Fetch Bookings and join with Units to get the UnitName
                    var raw = (from b in connect.booking
                               join u in connect.unit on b.Uid equals u.Uid into unitJoin
                               from u in unitJoin.DefaultIfEmpty()
                               select new
                               {
                                   id = b.id,
                                   name = b.guestName,
                                   email = b.guestEmail,
                                   phone = b.guestPhone,
                                   datetime = b.bookingDatetime,
                                   status = b.status,
                                   unitName = u != null ? u.unitName : "Unknown Unit"
                               })
                                        .OrderByDescending(b => b.datetime)
                                        .ToList();
                    var bookingsData = raw.Select(x => new
                    {
                        id = x.id,
                        name = x.name,
                        email = x.email,
                        phone = x.phone,

                        // CHANGE THIS LINE: Add the 'T'
                        datetime = x.datetime.ToString("yyyy-MM-ddTHH:mm:ss"),

                        status = x.status,
                        unitName = x.unitName
                    });
                    // 2. Fetch Busy Dates
                    // NOTE: If you have a busy_schedule table, query it here. 
                    // If not, we just pass an empty list for now to prevent frontend errors.
                    var busyDatesList = connect.busy_schedule
                        .ToList() // This executes the query to grab them from SQL
                        .Select(b => b.busyDate.ToString("yyyy-MM-dd")) // Formats them for the calendar
                        .ToList();

                    return Json(new
                    {
                        success = true,
                        bookings = bookingsData,
                        busyDates = busyDatesList // Now contains your real database dates!
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        public JsonResult GetAllMaintenance()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    // 1. Fetch all tables into memory (matching your GetAllUnit pattern)
                    var maintenanceRequests = connect.maintenance_request.ToList();
                    var units = connect.unit.ToList();
                    var tenants = connect.tenant.ToList();

                    // 2. Map the maintenance requests
                    var requestsResult = maintenanceRequests.Select(m =>
                    {
                        // Find related unit and tenant
                        var unit = units.FirstOrDefault(u => u.Uid == m.Uid);
                        var tenant = tenants.FirstOrDefault(t => t.Tid == m.Tid);

                        return new
                        {
                            id = m.id,
                            category = m.category,
                            status = m.status,
                            description = m.description,

                            // Fallbacks in case the unit or tenant was deleted
                            unitName = unit != null ? unit.unitName : "Unknown Unit",
                            tenantName = tenant != null ? tenant.name : "Unknown Tenant",

                            reportedDate = m.reportedDate.ToString("MMM dd, yyyy hh:mm tt"),

                            resolvedDate = m.resolvedDate.HasValue ? m.resolvedDate.Value.ToString("MMM dd, yyyy hh:mm tt") : null,
                            // UI placeholders for fields that aren't in your DB model yet

                            scheduledDate = m.scheduledDate.HasValue ? m.scheduledDate.Value.ToString("MMM dd, yyyy hh:mm tt") : null
                        };
                    }).ToList();

                    // 3. Map the units for the "Add Request" dropdown
                    var allUnit = units.Select(u => new
                    {
                        u.Uid,
                        u.unitName
                    }).ToList();

                    var tenantsResult = tenants.Select(t => new
                    {
                        Tid = t.Tid,
                        Uid = t.unitId,
                        name = t.name
                    }).ToList();

                    var unitsResult = (
                        from u in allUnit
                        join t in tenantsResult
                            on u.Uid equals t.Uid
                        select new
                        {
                            u.Uid,
                            u.unitName
                        }
                        );

                    return Json(new
                    {
                        success = true,
                        requests = requestsResult,
                        units = unitsResult,
                        tenants = tenantsResult // <-- Send this to Angular!
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        [HttpGet]
        public JsonResult GetAllPayment()
        {
            try
            {
                BillingScheduler.Run(false);
                using (var connect = new DB_Context())
                {
                    // 1. Fetch tables into memory
                    // We pull these to memory to handle the computed 'status' property logic safely
                    var paymentsList = connect.payment.ToList();
                    var tenantsList = connect.tenant.ToList();
                    var unitsList = connect.unit.ToList();

                    // 2. Filter to show ONLY the latest bill per tenant
                    var latestPayments = paymentsList
                        .GroupBy(p => p.Tid) // Group all bills belonging to the same tenant
                        .Select(group => group
                            .OrderByDescending(p => p.dueDate) // Sort by newest date first
                            .FirstOrDefault() // Pick only the top one (the most recent)
                        )
                        .ToList();

                    // 3. Map the data for the frontend
                    var result = latestPayments.Select(p =>
                    {
                        // Find related tenant
                        var tenant = tenantsList.FirstOrDefault(t => t.Tid == p.Tid);

                        // Find unit (using payment Uid or tenant's current unitId)
                        var unitIdToFind = p.Uid > 0 ? p.Uid : (tenant != null ? tenant.unitId : 0);
                        var unit = unitsList.FirstOrDefault(u => u.Uid == unitIdToFind);

                        return new
                        {
                            id = p.id,
                            Tid = p.Tid,
                            Uid = p.Uid,

                            // Human readable names
                            tenantName = tenant != null ? tenant.name : "Unknown Tenant",
                            unitName = unit != null ? unit.unitName : "Unknown Unit",

                            month = p.billingPeriod,
                            amount = p.amount,
                            status = p.status, // C# property handles the "Unpaid/Paid/Overdue" logic

                            // Format dates
                            dueDate = p.dueDate.ToString("MMM dd, yyyy"),
                            paidDate = p.paidDate.HasValue ? p.paidDate.Value.ToString("MMM dd, yyyy") : null
                        };
                    }).ToList();

                    return Json(new { success = true, data = result }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                // Using your ErrorHandling helper
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }


        //Delete
        public JsonResult DeleteUnit(unit data)
        {
            System.Diagnostics.Debug.WriteLine("Data is " + data.Uid);
            try
            {
                using (var connect = new DB_Context())
                {
                    var deleteUnit = connect.unit.Where(u => u.Uid == data.Uid).FirstOrDefault();
                    var deleteUnitImage = connect.unit_image.Where(u => u.Uid == deleteUnit.Uid).ToList();
                    connect.unit.Remove(deleteUnit);
                    connect.unit_image.RemoveRange(deleteUnitImage);
                    connect.SaveChanges();
                    CacheHelper.Remove("all-browse-units");
                    CacheHelper.Remove("all-units");
                    AuditLogger.Log("settings", "warning", "Deleted unit: " + deleteUnit.unitName, CurrentUserName());
                }
                return Json(new { success = true, message = "Unit Successfully Deleted" });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) });
            }
        }
        public JsonResult DeleteTenant(tenant data)
        {
            System.Diagnostics.Debug.WriteLine("Data is " + data.Tid);
            try
            {
                using (var connect = new DB_Context())
                {
                    var deleteTenant = connect.tenant.Where(u => u.Tid == data.Tid).FirstOrDefault();
                    var deleteTenantDocu = connect.tenant_document.Where(u => u.Tid == deleteTenant.Tid).ToList();
                    connect.tenant.Remove(deleteTenant);
                    connect.tenant_document.RemoveRange(deleteTenantDocu);
                    connect.SaveChanges();

                    AuditLogger.Log("user", "warning", "Deleted tenant: " + deleteTenant.name, CurrentUserName());
                }
                return Json(new { success = true, message = "Tenant Successfully Deleted" });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) });
            }
        }
      

        // ============================================================


        private ContentResult LargeJson(object data)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            return new ContentResult
            {
                Content = serializer.Serialize(data),
                ContentType = "application/json",
                ContentEncoding = System.Text.Encoding.UTF8
            };
        }


        public ActionResult GetBrowseUnits()
        {
            try
            {
                var payload = CacheHelper.GetOrAdd(
                    "all-browse-units",
                    1,
                    () =>
                    {
                        using (var connect = new DB_Context())
                        {
                            var today = DateTime.Today;

                            var activeUnits = connect.unit.Where(u => u.status == "active").ToList();
                            var liveTenants = connect.tenant
                                .Where(t => t.isTerminated != 1 && t.leaseEnd >= today)
                                .ToList();
                            var coOccupants = connect.co_occupant.ToList();

                            // Build availability per unit
                            var availability = activeUnits.Select(u =>
                            {
                                var mainTenant = liveTenants.FirstOrDefault(t => t.unitId == u.Uid);

                                if (mainTenant == null)
                                {
                                    // Fully vacant
                                    return new
                                    {
                                        Uid = u.Uid,
                                        available = true,
                                        joinable = false,
                                        slotsOpen = u.maxOccupants,
                                        totalOccupants = 0
                                    };
                                }

                                int headcount = 1 + coOccupants.Count(c => c.Tid == mainTenant.Tid);
                                int open = Math.Max(0, u.maxOccupants - headcount);

                                // Only Bedspace (3) stays listable while it has open slots
                                bool joinable = UnitAvailabilityHelper.HasOpenBedspace(
                                    mainTenant.occupancyTypeId, u.maxOccupants, headcount);

                                return new
                                {
                                    Uid = u.Uid,
                                    available = joinable,
                                    joinable = joinable,
                                    slotsOpen = open,
                                    totalOccupants = headcount
                                };
                            }).ToList();

                            var availableIds = availability.Where(a => a.available).Select(a => a.Uid).ToList();

                            var units = activeUnits
                                .Where(u => availableIds.Contains(u.Uid))
                                .OrderBy(u => u.unitName)
                                .ToList();

                            var unitIds = units.Select(u => u.Uid).ToList();

                            var images = connect.unit_image
                                .Where(i => unitIds.Contains(i.Uid))
                                .OrderBy(i => i.displayOrder)
                                .Select(i => new { i.Uid, i.imageUrl })
                                .ToList();

                            var amenities = (from ua in connect.unit_amenity
                                             join a in connect.amenity on ua.amenityId equals a.id
                                             where unitIds.Contains(ua.Uid)
                                             select new { ua.Uid, a.name })
                                            .ToList();

                            var data = units.Select(u =>
                            {
                                var av = availability.First(a => a.Uid == u.Uid);

                                return new
                                {
                                    id = u.Uid,
                                    name = u.unitName,
                                    price = u.price,
                                    beds = u.beds,
                                    sqm = u.sqm,
                                    floor = u.floor,
                                    description = u.description ?? "",
                                    address = u.address ?? "",
                                    videoUrl = u.videoUrl,
                                    status = u.status,
                                    maxOccupants = u.maxOccupants,

                                    // Bedspace info for the renter-facing badge
                                    joinable = av.joinable,
                                    slotsOpen = av.slotsOpen,
                                    currentOccupants = av.totalOccupants,
                                    availabilityLabel = av.joinable
                                        ? ("Bedspace \u00b7 " + av.slotsOpen + " slot(s) open")
                                        : "Vacant",

                                    images = images
                                        .Where(i => i.Uid == u.Uid)
                                        .Select(i => i.imageUrl)
                                        .Take(1)
                                        .ToList(),

                                    amenities = amenities
                                        .Where(a => a.Uid == u.Uid)
                                        .Select(a => a.name)
                                        .ToList()
                                };
                            }).ToList();

                            var bedOptions = units
                                .Where(u => !string.IsNullOrEmpty(u.beds))
                                .Select(u => u.beds)
                                .Distinct()
                                .OrderBy(b => b)
                                .ToList();

                            var amenityOptions = connect.amenity
                                .OrderBy(a => a.name)
                                .Select(a => a.name)
                                .ToList();

                            var rentTypeOptions = data
                                .Select(u => u.joinable ? "Bedspace" : "Vacant")
                                .Distinct()
                                .OrderBy(x => x)
                                .ToList();

                            
                            return new
                            {
                                success = true,
                                data = data,
                                bedOptions = bedOptions,
                                amenityOptions = amenityOptions,
                                rentTypeOptions = rentTypeOptions,
                            };
                        }
                    }
                    );
                return LargeJson(payload);

            }
            catch (Exception ex)
            {
                return LargeJson(new { success = false, message = ErrorHandling(ex) });
            }
        }

        // ============================================================
        // Paste both actions into SystemController
        // ============================================================

        // Stores the clicked unit in Session so the detail URL stays clean.
        [HttpPost]
        public JsonResult SetSelectedUnit(int id)
        {
            Session["SelectedUid"] = id;
            return Json(new { success = true });
        }


        // Reads the unit chosen by SetSelectedUnit and returns everything the
        // detail page needs: the unit, its occupancy, and similar units.

        [HttpGet]
        public ActionResult GetUnitDetail()
        {
            try
            {
                int selectedUid;
                var raw = Session["SelectedUid"];

                if (raw == null || !int.TryParse(raw.ToString(), out selectedUid))
                    return LargeJson(new { success = false, message = "No unit selected." });

                using (var connect = new DB_Context())
                {
                    var today = DateTime.Today;

                    var u = connect.unit.FirstOrDefault(x => x.Uid == selectedUid);
                    if (u == null)
                        return LargeJson(new { success = false, message = "Unit not found." });

                    // ---- Occupancy ----
                    // Vacant   : no live tenant
                    // Expiring : live tenant whose lease ends within 45 days
                    // Occupied : live tenant
                    var tenant = connect.tenant
                        .Where(t => t.unitId == u.Uid && t.isTerminated != 1 && t.leaseEnd >= today)
                        .OrderByDescending(t => t.leaseEnd)
                        .FirstOrDefault();

                    int totalOccupants = 0;
                    int slotsOpen = Math.Max(0, u.maxOccupants);
                    bool joinable = false;
                    bool isAvailable = u.status == "active";
                    string occupancy = "Vacant";
                    if (tenant != null)
                    {
                        totalOccupants = 1 + connect.co_occupant.Count(c => c.Tid == tenant.Tid);
                        slotsOpen = Math.Max(0, u.maxOccupants - totalOccupants);
                        joinable = UnitAvailabilityHelper.HasOpenBedspace(
                            tenant.occupancyTypeId, u.maxOccupants, totalOccupants);
                        isAvailable = u.status == "active" && joinable;

                        if (joinable)
                            occupancy = "Bedspace \u00b7 " + slotsOpen + " slot(s) open";
                        else
                        {
                            var daysLeft = (tenant.leaseEnd - today).TotalDays;
                            occupancy = (daysLeft <= 45) ? "Expiring" : "Occupied";
                        }
                    }

                    // ---- Images and amenities ----
                    var images = connect.unit_image
                        .Where(i => i.Uid == u.Uid)
                        .OrderBy(i => i.displayOrder)
                        .Select(i => i.imageUrl)
                        .ToList();

                    var amenities = (from ua in connect.unit_amenity
                                     join a in connect.amenity on ua.amenityId equals a.id
                                     where ua.Uid == u.Uid
                                     select a.name)
                                    .ToList();
                    var colorCode = connect.unit.Select(x => x.status == "active" ? "#22C55E" : "#EF4444");

                    var unit = new
                    {
                        id = u.Uid,
                        name = u.unitName,
                        price = u.price,
                        beds = u.beds,
                        sqm = u.sqm,
                        floor = u.floor,
                        description = u.description ?? "",
                        address = u.address ?? "",
                        video = u.videoUrl,
                        colorCode,
                        maxOccupants = u.maxOccupants,
                        joinable = joinable,
                        slotsOpen = slotsOpen,
                        currentOccupants = totalOccupants,
                        images = images,
                        amenities = amenities
                    };

                    // ---- Similar units: same type, active, still vacant ----
                    var occupiedUnitIds = connect.tenant
                        .Where(t => t.isTerminated != 1 && t.leaseEnd >= today)
                        .Select(t => t.unitId)
                        .Distinct()
                        .ToList();

                    var related = connect.unit
                        .Where(x => x.Uid != u.Uid
                                    && x.status == "active"
                                    && x.beds == u.beds
                                    && !occupiedUnitIds.Contains(x.Uid))
                        .OrderBy(x => x.unitName)
                        .Take(3)
                        .ToList();

                    var relatedIds = related.Select(x => x.Uid).ToList();

                    var relatedImages = connect.unit_image
                        .Where(i => relatedIds.Contains(i.Uid))
                        .OrderBy(i => i.displayOrder)
                        .Select(i => new { i.Uid, i.imageUrl })
                        .ToList();

                    var relatedUnits = related.Select(x => new
                    {
                        id = x.Uid,
                        name = x.unitName,
                        price = x.price,
                        sqm = x.sqm,
                        floor = x.floor,
                        image = relatedImages
                            .Where(i => i.Uid == x.Uid)
                            .Select(i => i.imageUrl)
                            .FirstOrDefault()
                    }).ToList();

                    return LargeJson(new
                    {
                        success = true,
                        unit = unit,
                        occupancy = occupancy,
                        isAvailable = isAvailable,
                        relatedUnits = relatedUnits
                    });
                }
            }
            catch (Exception ex)
            {
                return LargeJson(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetMyBookings()
        {
            try
            {
                bool verified = Session["VisitorVerified"] != null && (bool)Session["VisitorVerified"];
                if (!verified)
                    return Json(new { success = true, verified = false, data = new List<object>() },
                                JsonRequestBehavior.AllowGet);

                string email = Session["VisitorEmail"] as string;

                using (var connect = new DB_Context())
                {
                    var rows = (from b in connect.booking
                                join u in connect.unit on b.Uid equals u.Uid
                                where b.guestEmail == email
                                select new { b, u.unitName })
                               .ToList();

                    var data = rows
                        .OrderByDescending(x => x.b.bookingDatetime)
                        .Select(x => new
                        {
                            id = x.b.id,
                            unitId = x.b.Uid,
                            unitName = x.unitName,
                            name = x.b.guestName,
                            email = x.b.guestEmail,
                            phone = x.b.guestPhone,
                            date = x.b.bookingDatetime.ToString("MMM dd, yyyy"),
                            time = x.b.bookingDatetime.ToString("h:mm tt"),
                            status = x.b.status,
                            notes = x.b.notes,
                            cancelReason = x.b.cancelReason
                        })
                        .ToList();

                    return Json(new
                    {
                        success = true,
                        verified = true,
                        name = Session["VisitorName"],
                        email = email,
                        data = data
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message },
                            JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost]
        public JsonResult CancelMyBooking(int id, string reason)
        {
            string reasonError = InputValidator.Description(reason, "Reason", true, 300);
            if (reasonError != null)
                return Json(new { success = false, message = reasonError });
            reason = InputValidator.CleanDescription(reason);

            try
            {
                bool verified = Session["VisitorVerified"] != null && (bool)Session["VisitorVerified"];
                if (!verified)
                    return Json(new { success = false, message = "Please sign in first." });

                if (string.IsNullOrWhiteSpace(reason))
                    return Json(new { success = false, message = "Please select or enter a reason for cancellation." });

                string email = Session["VisitorEmail"] as string;

                using (var connect = new DB_Context())
                {
                    // Match on the session email too, so nobody can cancel
                    // someone else's booking by guessing an id.
                    var booking = connect.booking
                        .FirstOrDefault(b => b.id == id && b.guestEmail == email);

                    if (booking == null)
                        return Json(new { success = false, message = "Booking not found." });

                    if (booking.status != "Pending" && booking.status != "Confirmed")
                        return Json(new { success = false, message = "This booking can no longer be cancelled." });

                    booking.status = "Cancelled";
                    booking.cancelReason = reason.Trim();
                    connect.SaveChanges();

                    AuditLogger.Log("settings", "warning", "Cancelled booking: " + booking.guestName,
                        (Session["VisitorName"] as string) ?? email);

                    return Json(new { success = true, message = "Booking cancelled successfully." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        ////// SMS
        ///
        [HttpPost]
        public JsonResult RunSmsNotifications(bool dryRun = true)
        {
            try
            {
                var run = NotificationScanner.Run(dryRun);

                return Json(new
                {
                    success = true,
                    dryRun = dryRun,
                    scanned = new
                    {
                        expiringLeases = run.expiringLeases,
                        duePayments = run.duePayments
                    },
                    results = run.results
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpGet]
        [CheckSession(AllowedRoles = new[] { 1 })]
        public JsonResult GetManagers()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var onlineWindow = DateTime.Now.AddMinutes(-5); // "online now" = seen in last 5 min

                    var list = connect.admin
                        .ToList() // pull to memory so we can format strings below
                        .Select(a => new
                        {
                            id = a.id,                                   // ← admin PK
                            name = a.name,
                            email = a.email,
                            role = a.role == 1 ? "admin"
                                 : a.role == 2 ? "property_manager"
                                 : "invalid",

                            // status is NOT stored — derived from lastActive
                            status = (a.role != 1 && a.role != 2)
                                   ? "invalid"
                                   : (a.lastActive != null && a.lastActive >= onlineWindow) ? "active" : "inactive",

                            // HTML binds these; no created column, lastLogin = lastActive
                            createdAt = (string)null,
                            lastLogin = a.lastActive != null
                                        ? ((DateTime)a.lastActive).ToString("MMM d, yyyy h:mm tt")
                                        : null
                        })
                        .OrderByDescending(x => x.role == "admin") // admins on top
                        .ThenBy(x => x.role == "invalid")          // repairable legacy rows last
                        .ToList();

                    return Json(new { success = true, data = list }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }


        // ===================== CREATE =====================
        // POST /System/CreateManager
        [HttpPost]
        [CheckSession(AllowedRoles = new[] { 1 })]
        [ValidateJsonAntiForgeryToken]
        public JsonResult CreateManager(admin data)
        {
            try
            {
                if (data == null)
                    return Json(new { success = false, message = "Account data is missing." });
                string accountError = InputValidator.FirstError(
                    InputValidator.Name(data.name, "Full name"),
                    InputValidator.Email(data.email, "Email"),
                    InputValidator.Password(data.password, "Password", true, 8, 64));
                if (accountError != null)
                    return Json(new { success = false, message = accountError });
                data.name = InputValidator.CleanText(data.name);
                data.email = data.email.Trim();
                if (data.role != 1 && data.role != 2)
                    return Json(new { success = false, message = "Select a valid account role." });

                using (var connect = new DB_Context())
                {
                    string email = data.email.Trim().ToLower();

                    bool exists = connect.admin.Any(a => a.email == email)
                                  || connect.tenant.Any(t => t.email == email);
                    if (exists)
                        return Json(new { success = false, message = "That email is already in use." });

                    var newAdmin = new admin
                    {
                        name = data.name.Trim(),
                        email = email,
                        password = BCrypt.Net.BCrypt.HashPassword(data.password),   // hashed
                        role = data.role,
                        lastActive = null
                    };

                    connect.admin.Add(newAdmin);
                    connect.SaveChanges();

                    AuditLogger.Log("user", "info", "Created account: " + newAdmin.name, CurrentUserName());
                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ===================== UPDATE =====================
        // POST /System/UpdateManager
        [HttpPost]
        [CheckSession(AllowedRoles = new[] { 1 })]
        [ValidateJsonAntiForgeryToken]
        public JsonResult UpdateManager(admin data)
        {
            try
            {
                if (data == null || data.id <= 0)
                    return Json(new { success = false, message = "Invalid account." });
                string nameError = InputValidator.Name(data.name, "Full name");
                if (nameError != null)
                    return Json(new { success = false, message = nameError });

                using (var connect = new DB_Context())
                {
                    var target = connect.admin.FirstOrDefault(a => a.id == data.id); // ← PK
                    if (target == null)
                        return Json(new { success = false, message = "Account not found." });

                    target.name = InputValidator.CleanText(data.name);

                    // Protect the admin (role 1): lock email + role changes
                    if (target.role != 1)
                    {
                        string emailError = InputValidator.Email(data.email, "Email");
                        if (emailError != null)
                            return Json(new { success = false, message = emailError });
                        if (data.role != 1 && data.role != 2)
                            return Json(new { success = false, message = "Select a valid account role." });

                        string email = data.email.Trim().ToLower();
                        bool taken = connect.admin.Any(a => a.email == email && a.id != target.id)
                                     || connect.tenant.Any(t => t.email == email);
                        if (taken)
                            return Json(new { success = false, message = "That email is already in use." });

                        target.email = email;
                        target.role = data.role;
                    }

                    // Only change password if a new one was typed
                    if (!string.IsNullOrWhiteSpace(data.password))
                    {
                        string passwordError = InputValidator.Password(data.password, "Password", true, 8, 64);
                        if (passwordError != null)
                            return Json(new { success = false, message = passwordError });
                        target.password = BCrypt.Net.BCrypt.HashPassword(data.password);   // hashed
                    }

                    connect.SaveChanges();
                    AuditLogger.Log("user", "info", "Updated account: " + target.name, CurrentUserName());
                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ===================== DELETE =====================
        // POST /System/DeleteManager
        [HttpPost]
        [CheckSession(AllowedRoles = new[] { 1 })]
        [ValidateJsonAntiForgeryToken]
        public JsonResult DeleteManager(admin data)
        {
            try
            {
                if (data == null || data.id <= 0)
                    return Json(new { success = false, message = "Invalid account." });

                using (var connect = new DB_Context())
                {
                    var target = connect.admin.FirstOrDefault(a => a.id == data.id); // ← PK
                    if (target == null)
                        return Json(new { success = false, message = "Account not found." });

                    if (target.role == 1)
                        return Json(new { success = false, message = "The admin account cannot be deleted." });

                    string name = target.name;
                    connect.admin.Remove(target);
                    connect.SaveChanges();

                    AuditLogger.Log("user", "warning", "Deleted account: " + name, CurrentUserName());
                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ===================== HELPER =====================
        // Resolves the logged-in actor's name for audit logging
        private string CurrentUserName()
        {
            string email = Session["VerifyEmail"] as string;
            if (string.IsNullOrEmpty(email)) return "System";
            using (var connect = new DB_Context())
            {
                var a = connect.admin.FirstOrDefault(x => x.email == email);
                return a != null ? a.name : email;
            }
        }
        // Validates the unit form on the server and stores the cleaned values.
        private static string CheckAndCleanUnit(unit u)
        {
            if (u == null) return "Unit data is missing.";

            string error = InputValidator.FirstError(
                InputValidator.Code(u.unitName, "Unit name", true, 50),
                InputValidator.Code(u.floor, "Floor", true, 10),
                InputValidator.Address(u.address, "Address", false, 150),
                InputValidator.Description(u.description, "Description", false, 100));
            if (error != null) return error;

            if (u.price <= 0 || u.price > 999999.99m) return "Monthly rent must be greater than 0 and no more than 999999.99.";
            if (decimal.Round(u.price, 2) != u.price) return "Monthly rent can have at most 2 decimal places.";
            if (u.sqm < 1 || u.sqm > 9999) return "Size (sqm) must be between 1 and 9999.";
            if (u.maxOccupants < 1 || u.maxOccupants > 20) return "Max Occupants must be between 1 and 20.";
            if (!string.IsNullOrEmpty(u.status) && u.status != "active" && u.status != "inactive")
                return "Please select a valid status.";

            u.unitName = InputValidator.CleanText(u.unitName);
            u.floor = InputValidator.CleanText(u.floor);
            u.address = InputValidator.CleanText(u.address);
            u.description = InputValidator.CleanDescription(u.description);
            return null;
        }

        // Creation (id 0) needs a description and category. Status-only updates may leave them out.
        private static string CheckAndCleanMaintenance(maintenance_request r)
        {
            if (r == null) return "Request data is missing.";

            bool isNew = r.id == 0;
            string error = InputValidator.FirstError(
                InputValidator.Description(r.description, "Description", isNew, 1000),
                InputValidator.Code(r.category, "Category", isNew, 100));
            if (error != null) return error;

            r.description = InputValidator.CleanDescription(r.description);
            r.category = InputValidator.CleanText(r.category);
            return null;
        }

        private static bool UsesCoOccupants(int occupancyTypeId)
        {
            return occupancyTypeId == 2 || occupancyTypeId == 3;
        }

        // Validates the tenant form on the server and stores the cleaned values.
        // Returns the first message to show, or null when everything is valid.
        private static string CheckAndCleanTenant(tenant t, List<co_occupant> coOccupants)
        {
            if (t == null) return "Tenant data is missing.";

            string error = InputValidator.FirstError(
                InputValidator.Name(t.name, "Full name"),
                InputValidator.Email(t.email, "Email"),
                InputValidator.Phone(t.phone, "Phone number"),
                InputValidator.Address(t.address, "Address", false, 100),
                InputValidator.Occupation(t.occupation, "Occupation", false, 25),
                InputValidator.Password(t.passwordHash, "Portal password", false, 0, 20));
            if (error != null) return error;

            if (t.occupancyTypeId < 1 || t.occupancyTypeId > 3) return "Please select an Occupancy Type.";
            if (t.unitId <= 0) return "Please select a unit.";
            if (t.leaseStart == DateTime.MinValue) return "Lease Start date is required.";
            if (t.leaseEnd != DateTime.MinValue && t.leaseEnd <= t.leaseStart)
                return "Lease End date must be after the Lease Start date.";

            var occupants = UsesCoOccupants(t.occupancyTypeId) && coOccupants != null
                ? coOccupants
                : new List<co_occupant>();

            for (int i = 0; i < occupants.Count; i++)
            {
                var occupant = occupants[i];
                if (occupant == null) continue;

                // Rows left completely blank are skipped on save.
                if (string.IsNullOrWhiteSpace(occupant.name) && string.IsNullOrWhiteSpace(occupant.phone)
                    && string.IsNullOrWhiteSpace(occupant.address)) continue;

                string label = "Occupant #" + (i + 1);
                error = InputValidator.FirstError(
                    InputValidator.Name(occupant.name, label + " name"),
                    InputValidator.Phone(occupant.phone, label + " phone number", true),
                    InputValidator.Address(occupant.address, label + " address", false, 50));
                if (error != null) return error;
            }

            t.name = InputValidator.CleanText(t.name);
            t.email = t.email.Trim();
            t.phone = InputValidator.CleanText(t.phone);
            t.address = InputValidator.CleanText(t.address);
            t.occupation = InputValidator.CleanText(t.occupation);
            foreach (var occupant in occupants)
            {
                if (occupant == null) continue;
                occupant.name = InputValidator.CleanText(occupant.name);
                occupant.phone = InputValidator.CleanText(occupant.phone);
                occupant.address = InputValidator.CleanText(occupant.address);
            }
            return null;
        }
        [HttpGet]
        public JsonResult SchedulerStatus()
        {
            return Json(new
            {
                success = true,
                lastRunAt = SmsScheduler.LastRunAt.HasValue
                            ? SmsScheduler.LastRunAt.Value.ToString("MMM dd, yyyy h:mm:ss tt")
                            : "never",
                lastRunSummary = SmsScheduler.LastRunSummary ?? "-"
            }, JsonRequestBehavior.AllowGet);
        }





        [HttpGet]
        [CheckSession(AllowedRoles = new[] { 1 })]
        public JsonResult GetAuditLogs()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    // Newest first; cap so the page stays fast
                    var logs = connect.audit_log
                        .OrderByDescending(x => x.createdAt)
                        .Take(500)
                        .ToList()  // to memory so we can format dates below
                        .Select(x => new
                        {
                            id = x.logId,
                            type = x.type,
                            message = x.message,
                            actor = x.actor,
                            ip = (string)null,   // not stored; view hides it when empty
                            date = x.createdAt.ToString("MMM d, yyyy"),
                            time = x.createdAt.ToString("h:mm tt")
                        })
                        .ToList();

                    // Per-type totals for the 5 stat cards (whole table, not just the 500)
                    var counts = connect.audit_log
                        .GroupBy(x => x.type)
                        .Select(g => new { type = g.Key, count = g.Count() })
                        .ToList()
                        .ToDictionary(g => g.type, g => g.count);

                    return Json(new { success = true, data = logs, counts = counts },
                                JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message },
                            JsonRequestBehavior.AllowGet);
            }
        }

        // sms notif for failed pdc

        public JsonResult SendPaymentNotification(int? Tid)
        {
            System.Diagnostics.Debug.WriteLine("Received Tid: " + Tid);
            if (!Tid.HasValue) { return Json(new { success = false, message = "Invalid tenant ID." }, JsonRequestBehavior.AllowGet); }

            var tenant = new tenant();
            var payment = new payment();
            using (var db = new DB_Context())
            {
                tenant = db.tenant.FirstOrDefault(t => t.Tid == Tid);
                payment = db.payment.FirstOrDefault(p => p.Tid == Tid && p.paidDate == null);

            }
            try
            {
                // Compose the message
                string message = $"Hello {tenant.name},\n\n" +
                $"This is to inform you that your Post-Dated Check (PDC) payment of {payment.amount:C} due on {payment.dueDate} was unsuccessful.\n\n" +
                "Please arrange an alternative payment method and settle your outstanding balance as soon as possible.\n\n" +
                "Thank you.";
                // Send the notification (assuming NotificationService.SendEmail exists)
                var phone = SmsServices.NormalizePhone(tenant.phone);
                string error = SmsServices.SendSms(phone, message);
                if (error == null)
                {
                    return Json(new { success = true, message = "Notification sent successfully." });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Philsms Error: ", error);
                    return Json(new { success = false, message = "Failed to send notification." + error });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) });
            }
        }

        public ActionResult ExportAuditTrail()
        {
            using (var connect = new DB_Context())
            {
                var logs = connect.audit_log.OrderByDescending(a => a.createdAt).ToList();

                var csv = new System.Text.StringBuilder();

                csv.AppendLine("Date & Time,Type,Severity,Message,Actor");

                foreach (var log in logs)
                {
                    csv.AppendLine(string.Join(",", new[]
                    {
                        CsvEscape(log.createdAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        CsvEscape(log.type),
                        CsvEscape(log.severity),
                        CsvEscape(log.message),
                        CsvEscape(log.actor)
                    }));
                }
                return File(
                        new System.Text.UTF8Encoding(true).GetBytes(csv.ToString()),
                        "text/csv",
                        "AuditTrail_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv"
                    );
            }
        }
        public JsonResult UpdateStatusTenantProfileRequest(tenant_profile_change_request data)
        {
            try
            {
                if (data == null)
                {
                    return Json(new { success = false, message = "Invalid request data." });
                }

                if (string.IsNullOrWhiteSpace(data.status))
                {
                    return Json(new { success = false, message = "The request status cannot be empty." });
                }

                string reviewError = InputValidator.Description(data.reviewNotes, "Review note", true, 1000);
                if (reviewError != null)
                {
                    return Json(new { success = false, message = reviewError });
                }
                data.reviewNotes = InputValidator.CleanDescription(data.reviewNotes);

                var status = data.status.Trim().ToLowerInvariant();

                if (status != "accepted" && status != "declined")
                {
                    return Json(new { success = false, message = "Invalid request status." });
                }

                using (var connect = new DB_Context())
                {
                    var existingTenantRequest = connect.tenant_profile_change_request
                        .FirstOrDefault(r => r.id == data.id);

                    if (existingTenantRequest == null)
                    {
                        return Json(new { success = false, message = "There is no matching request ID." });
                    }

                    var thatTenant = connect.tenant
                        .FirstOrDefault(r => r.Tid == existingTenantRequest.Tid);

                    if (thatTenant == null)
                    {
                        return Json(new { success = false, message = "The associated tenant was not found." });
                    }

                    // Update the request review details.
                    existingTenantRequest.status = status;
                    existingTenantRequest.reviewNotes = data.reviewNotes;
                    existingTenantRequest.reviewedAt = DateTime.Now;

                    string message;

                    if (status == "accepted")
                    {
                        // Apply the requested changes to the tenant profile.
                        thatTenant.email = existingTenantRequest.requestedEmail;
                        thatTenant.phone = existingTenantRequest.requestedPhone;
                        thatTenant.occupation = existingTenantRequest.requestedOccupation;
                        thatTenant.address = existingTenantRequest.requestedAddress;

                        message = $"Hello {thatTenant.name},\n\n" +
                            "Your request to change your profile information has been approved by the manager, " +
                            "and the changes have been reflected in your tenant portal.\n\n" +
                            "Your approved profile information:\n" +
                            $"Email: {thatTenant.email}\n" +
                            $"Phone: {thatTenant.phone}\n" +
                            $"Occupation: {thatTenant.occupation}\n" +
                            $"Address: {thatTenant.address}\n\n" +
                            "Please log in to your tenant portal to review your updated information. " +
                            "If you notice any discrepancies, please contact the property manager.";

                    }
                    else
                    {
                        // Do not modify the tenant profile when the request is declined.
                        message = $"Hello {thatTenant.name},\n\n" +
                            "Your request to change your profile information has been reviewed and declined by the manager.\n\n" +
                            "Your requested changes were not applied, and your existing profile information remains unchanged.\n\n" +
                            "Requested changes:\n" +
                            $"Email: {existingTenantRequest.requestedEmail}\n" +
                            $"Phone: {existingTenantRequest.requestedPhone}\n" +
                            $"Occupation: {existingTenantRequest.requestedOccupation}\n" +
                            $"Address: {existingTenantRequest.requestedAddress}\n\n" +
                            $"Review Notes: {existingTenantRequest.reviewNotes ?? "No reason provided."}\n\n" +
                            "Please review the manager's notes and contact the property manager if you need clarification.";
                    }

                    // Persist the review before attempting SMS delivery.
                    connect.SaveChanges();

                    // Send notification and record whether delivery was successful.
                    bool notificationFailed = false;

                    try
                    {
                        var phone = SmsServices.NormalizePhone(thatTenant.phone);
                        var error = SmsServices.SendSms(phone, message);

                        notificationFailed = error != null;
                    }
                    catch
                    {
                        // The request was saved even if the SMS service throws an exception.
                        notificationFailed = true;
                    }

                    string action = status == "accepted" ? "accepted" : "declined";

                    AuditLogger.Log(
                        "user",
                        "info",
                        "Profile request by " + thatTenant.name + " was " + action,
                        CurrentUserName()
                    );

                    string responseMessage = notificationFailed
                        ? $"Tenant profile request successfully {action}, but the tenant could not be notified by SMS."
                        : $"Tenant profile request successfully {action}, and the SMS notification was sent.";

                    return Json(new
                    {
                        success = true,
                        notificationFailed,
                        message = responseMessage
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ErrorHandling(ex)
                });
            }
        }
        public JsonResult GetAllTenantProfileChangeRequest()
        {
            try
            {
                using (var connect = new DB_Context())
                {
                    var request = connect.tenant_profile_change_request.ToList();
                    var profile_requests = request.Select(x => new
                    {
                        x.id,
                        x.Tid,
                        x.requestedEmail,
                        x.requestedPhone,
                        x.requestedOccupation,
                        x.requestedAddress,
                        x.reason,
                        x.status,
                        reviewedAt = x.reviewedAt.HasValue ? x.reviewedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
                        requestedAt = x.requestedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        x.reviewNotes
                    }).ToList();
                    return Json(new { success = true, profile_requests = profile_requests }, JsonRequestBehavior.AllowGet);
                }
            }catch(Exception ex)
            {
                return Json(new { success = false, message = ErrorHandling(ex) }, JsonRequestBehavior.AllowGet);
            }
        }
        private string CsvEscape(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
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
