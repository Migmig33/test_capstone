using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using WebApplication1.Filter;
using WebApplication1.Models.Context;
using WebApplication1.Models.Tables;
namespace WebApplication1.Controllers
{
    [RateLimit(sec = 10, requests = 5)]


    public class AIController : Controller
    {
        [HttpPost]
        [RateLimit]
        public async Task<ActionResult> Chat(string message, string conversationHistory)
        {
            try
            {
                var apiKey = ConfigurationManager.AppSettings["GeminiApiKey"];
                var client = new HttpClient();
                using(var connect = new DB_Context())
                {
                    var today = DateTime.Today;
                    var expiringThreshold = today.AddDays(30);

                    var allUnits = connect.unit.ToList();
                    var allTenants = connect.tenant.ToList();
                    var allBookings = connect.booking.ToList();
                    var allPayments = connect.payment.ToList();
                    var allMaintenance = connect.maintenance_request.ToList();
                    var allCoOccupants = connect.co_occupant.ToList();

                    var dbContext = $@"
                                === GREEN RESIDENCES LIVE DATA ===

                                UNITS ({allUnits.Count} total):
                                {string.Join("\n", allUnits.Select(u => {
                                                        var t = allTenants.FirstOrDefault(x => x.unitId == u.Uid);
                                                        var occupancy = t == null ? "Vacant" : t.leaseEnd <= expiringThreshold ? "Expiring" : "Occupied";
                                                        var coCount = t != null ? allCoOccupants.Count(c => c.Tid == t.Tid) : 0;
                                                        return $"- {u.unitName} | Floor {u.floor} | {u.beds} | {u.sqm}sqm | ₱{u.price:N0}/mo | Status: {u.status} | Occupancy: {occupancy} | Occupants: {(t != null ? 1 + coCount : 0)}/{u.maxOccupants} | Tenant: {(t != null ? t.name : "None")}";
                                                    }))}

                                TENANTS ({allTenants.Count} total):
                                {string.Join("\n", allTenants.Select(t => {
                                                        var u = allUnits.FirstOrDefault(x => x.Uid == t.unitId);
                                                        var coList = allCoOccupants.Where(c => c.Tid == t.Tid).ToList();
                                                        var leaseStatus = t.leaseEnd <= expiringThreshold ? "Expiring Soon" : t.leaseEnd < today ? "Expired" : "Active";
                                                        return $"- {t.name} | Unit: {(u != null ? u.unitName : t.unitId.ToString())} | Email: {t.email} | Phone: {t.phone} | Occupation: {t.occupation} | Lease: {t.leaseStart:MMM dd, yyyy} to {t.leaseEnd:MMM dd, yyyy} | Lease Status: {leaseStatus} | Co-Occupants ({coList.Count}): {(coList.Any() ? string.Join(", ", coList.Select(c => c.name)) : "None")}";
                                                    }))}

                                BOOKINGS ({allBookings.Count} total):
                                {string.Join("\n", allBookings.Select(b => {
                                                        var u = allUnits.FirstOrDefault(x => x.Uid == b.Uid);
                                                        return $"- {b.guestName} | Email: {b.guestEmail} | Phone: {b.guestPhone} | Unit Interested: {(u != null ? u.unitName : "N/A")} | Date: {b.bookingDatetime:MMM dd, yyyy hh:mm tt} | Status: {b.status} | Notes: {b.notes ?? "None"} | Cancel Reason: {b.cancelReason ?? "N/A"}";
                                                    }))}

                                PAYMENTS ({allPayments.Count} total):
                                {string.Join("\n", allPayments.Select(p => {
                                                        var t = allTenants.FirstOrDefault(x => x.Tid == p.Tid);
                                                        return $"- {(t != null ? t.name : "Unknown")} | Amount: ₱{p.amount:N0} | Period: {p.billingPeriod} | Due: {p.dueDate:MMM dd, yyyy} | Paid: {(p.paidDate.HasValue ? p.paidDate.Value.ToString("MMM dd, yyyy") : "Not yet paid")} | Status: {p.status}";
                                                    }))}
                                Payment Summary:
                                - Total Paid:    ₱{allPayments.Where(p => p.status == "Paid").Sum(p => p.amount):N0}
                                - Total Overdue: ₱{allPayments.Where(p => p.status == "Overdue").Sum(p => p.amount):N0}
                                - Total Unpaid:  ₱{allPayments.Where(p => p.status == "Unpaid").Sum(p => p.amount):N0}

                                MAINTENANCE REQUESTS ({allMaintenance.Count} total):
                                {string.Join("\n", allMaintenance.Select(m => {
                                                        var t = allTenants.FirstOrDefault(x => x.Tid == m.Tid);
                                                        var u = allUnits.FirstOrDefault(x => x.Uid == m.Uid);
                                                        return $"- [{m.status}] {m.category ?? "N/A"} | Tenant: {(t != null ? t.name : "Unknown")} | Unit: {(u != null ? u.unitName : "Unknown")} | Reported: {(m.reportedDate != null ? ((DateTime)m.reportedDate).ToString("MMM dd, yyyy") : "N/A")} | Resolved: {(m.resolvedDate != null ? ((DateTime)m.resolvedDate).ToString("MMM dd, yyyy") : "Not yet resolved")} | Description: {m.description ?? "N/A"}";
                                                    }))}
                                Maintenance Summary:
                                - Pending:     {allMaintenance.Count(m => m.status == "Pending")}
                                - In Progress: {allMaintenance.Count(m => m.status == "In Progress")}
                                - Resolved:    {allMaintenance.Count(m => m.status == "Resolved")}
";


                    // ===== FETCH DB DATA =====


                    // ===== PARSE HISTORY =====
                    var history = string.IsNullOrEmpty(conversationHistory)
                        ? new List<object>()
                        : Newtonsoft.Json.JsonConvert.DeserializeObject<List<object>>(conversationHistory);

                    history.Add(new
                    {
                        role = "user",
                        parts = new[] { new { text = message } }
                    });

                    var requestBody = new
                    {
                        systemInstruction = new
                        {
                            parts = new[] { new { text = $@"You are a helpful assistant for Green Residences. 
                                                    You help tenants and guests with inquiries about units, bookings, payments, and maintenance requests. 
                                                    Be friendly and concise.
                                                    IMPORTANT: Do not use any markdown formatting. 
                                                    No asterisks, no bullet points, no bold, no headers, no symbols. 
                                                    Plain text only.
                                                    Answer questions based on the live data below. If the data does not contain the answer, say so politely.

                                                    {dbContext}" } }
                        },
                        contents = history,
                        generationConfig = new
                        {
                            maxOutputTokens = 1024,
                            temperature = 0.7
                        }
                    };

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent?key={apiKey}";
                    var response = await client.PostAsync(url, content);
                    var result = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return Json(new
                        {
                            success = false,
                            error = $"Gemini API Error ({response.StatusCode}): {result}",
                            message = "Something Went Wrong Please Try Again Later"
                        }, JsonRequestBehavior.AllowGet);
                    }

                    dynamic parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(result);

                    if (parsed.candidates == null)
                    {
                        return Json(new
                        {
                            success = false,
                            error = $"Gemini returned no candidates: {result}",
                            message = "Something Went Wrong Please Try Again Later"

                        }, JsonRequestBehavior.AllowGet);
                    }

                    string reply = parsed.candidates[0].content.parts[0].text;

                    history.Add(new
                    {
                        role = "model",
                        parts = new[] { new { text = reply } }
                    });

                    return Json(new
                    {
                        success = true,
                        reply = reply,
                        history = history
                    }, JsonRequestBehavior.AllowGet);
                }
              
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ErrorHandling(ex)
                }, JsonRequestBehavior.AllowGet);
            }
        }
        [HttpPost]
        [RateLimit(sec = 10, requests = 5)]
        public async Task<ActionResult> GuestChat(string message, string conversationHistory)
        {
            try
            {
                var apiKey = ConfigurationManager.AppSettings["GeminiApiKey"];
                var client = new HttpClient();

                using (var connect = new DB_Context())
                {
                    var today = DateTime.Today;

                    // ===== VACANT UNITS ONLY =====
                    var occupiedUnitIds = connect.tenant
                        .Where(t => t.isTerminated != 1 && t.leaseEnd >= today)
                        .Select(t => t.unitId)
                        .Distinct()
                        .ToList();

                    var vacantUnits = connect.unit
                        .Where(u => u.status == "active" && !occupiedUnitIds.Contains(u.Uid))
                        .OrderBy(u => u.price)
                        .ToList();

                    var unitIds = vacantUnits.Select(u => u.Uid).ToList();

                    var unitAmenities = (from ua in connect.unit_amenity
                                         join a in connect.amenity on ua.amenityId equals a.id
                                         where unitIds.Contains(ua.Uid)
                                         select new { ua.Uid, a.name })
                                        .ToList();

                    var allAmenityNames = connect.amenity.Select(a => a.name).ToList();

                    var unitContext = $@"
                                === GREEN RESIDENCES — AVAILABLE UNITS ===
 
                                VACANT UNITS ({vacantUnits.Count} available):
                                {(vacantUnits.Any()
                                            ? string.Join("\n", vacantUnits.Select(u => {
                                                var amens = unitAmenities.Where(a => a.Uid == u.Uid).Select(a => a.name).ToList();
                                                return $"- {u.unitName} | Floor {u.floor} | {u.beds} | {u.sqm}sqm | PHP {u.price:N0}/month | Up to {u.maxOccupants} occupants | Amenities: {(amens.Any() ? string.Join(", ", amens) : "none listed")} | Description: {u.description ?? "N/A"}";
                                            }))
                                            : "(No units are currently available.)")}
 
                                BUILDING AMENITIES OFFERED:
                                {string.Join(", ", allAmenityNames)}
 
                                LOCATION:
                                Green Residences, Taft Avenue, Malate, Manila. Walking distance to DLSU and CSB.
 
                                MOVE-IN COSTS:
                                Security deposit is 2 months rent. Advance payment is 1 month rent.
 
                                BOOKING A VIEWING:
                                Free. No payment required. The renter clicks a unit on the Browse page,
                                then Book a Viewing. They verify their email with a code, pick a date and
                                time slot, and the admin reviews the request.
                                Maximum 2 bookings per person per month.
";

                    // ===== PARSE HISTORY =====
                    var history = string.IsNullOrEmpty(conversationHistory)
                        ? new List<object>()
                        : Newtonsoft.Json.JsonConvert.DeserializeObject<List<object>>(conversationHistory);

                    history.Add(new
                    {
                        role = "user",
                        parts = new[] { new { text = message } }
                    });

                    var requestBody = new
                    {
                        systemInstruction = new
                        {
                            parts = new[] { new { text = $@"You are GreenBot, a friendly leasing assistant for Green Residences.
                                                    You are talking to a prospective renter browsing the website.
                                                    Help them find a unit that fits their budget, unit type, and amenity needs.
 
                                                    IMPORTANT: Do not use any markdown formatting.
                                                    No asterisks, no bullet points, no bold, no headers, no symbols.
                                                    Plain text only.
 
                                                    RULES:
                                                    Only recommend units from the list below. Never invent a unit.
                                                    Always mention the unit name and monthly price when recommending.
                                                    Write prices like PHP 16,500.
                                                    If nothing matches what they want, say so plainly and suggest the closest option.
                                                    Keep replies under 100 words. Be warm but brief.
                                                    Tell them to click the unit on the Browse page to see photos and book a viewing.
 
                                                    PRIVACY: You know nothing about existing tenants, their payments, their
                                                    bookings, or maintenance requests. If asked about any of those, say that
                                                    information is not available and offer to help them find a unit instead.
 
                                                    {unitContext}" } }
                        },
                        contents = history,
                        generationConfig = new
                        {
                            maxOutputTokens = 1024,
                            temperature = 0.7
                        }
                    };

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent?key={apiKey}";
                    var response = await client.PostAsync(url, content);
                    var result = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return Json(new
                        {
                            success = false,
                            error = $"Gemini API Error ({response.StatusCode}): {result}",
                            message = "Something Went Wrong Please Try Again Later"
                        }, JsonRequestBehavior.AllowGet);
                    }

                    dynamic parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(result);

                    if (parsed.candidates == null)
                    {
                        return Json(new
                        {
                            success = false,
                            error = $"Gemini returned no candidates: {result}",
                            message = "Something Went Wrong Please Try Again Later"
                        }, JsonRequestBehavior.AllowGet);
                    }

                    string reply = parsed.candidates[0].content.parts[0].text;

                    history.Add(new
                    {
                        role = "model",
                        parts = new[] { new { text = reply } }
                    });

                    return Json(new
                    {
                        success = true,
                        reply = reply,
                        history = history
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ErrorHandling(ex)
                }, JsonRequestBehavior.AllowGet);
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