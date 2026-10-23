using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planer.Core;
using PlannerWeb.Models;
using PlannerWeb.Services;

namespace PlannerWeb.Controllers;

[Authorize]
public sealed class PlannerController(PlannerService service) : Controller
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Index(string screen = "week", string? date = null, int yearMode = 0, bool completed = false)
    {
        if (!new[] { "week", "month", "year", "day", "habits", "payments", "finances" }.Contains(screen)) return BadRequest();
        try
        {
            var selected = PlannerService.Date(date) ?? service.Today;
            ViewBag.Completed = completed;
            return View(await service.WithEngine(Owner, e =>
            {
                var page = new PlannerPage(e, screen, selected, Math.Clamp(yearMode, 0, 2));
                e.EnsureTaskRange(Dates.Monday(page.First), page.Last.AddDays(6)); return page;
            }));
        }
        catch (ArgumentException e) { return BadRequest(e.Message); }
    }
    [HttpPost, RequestSizeLimit(65536)]
    public async Task<IActionResult> Command([FromBody] PlannerCommand command)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Проверьте заполненные поля" });
        try { return Json(await service.WithEngine(Owner, e => PlannerService.Execute(e, command))); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return BadRequest(new { error = e is KeyNotFoundException || e.Message.Contains("Sequence contains") ? "Элемент не найден. Обновите страницу." : e.Message }); }
    }
    [HttpGet] public IActionResult Clock() => Json(new { today = PlannerPage.Iso(service.Today) });
    [HttpPost]
    public async Task<IActionResult> Pulse() => Json(await service.WithEngine(Owner, e =>
    {
        e.CheckNotificationDue(service.LocalNow);
        var notifications = e.PendingNotifications(service.LocalNow).ToArray();
        foreach (var notification in notifications) e.AcknowledgeNotification(notification.Key);
        return new { today = PlannerPage.Iso(e.Today), messages = notifications.Select(n => new { n.Body, screen = n.Destination switch { PlannerDestination.Today => "day", PlannerDestination.Habits => "habits", _ => "week" } }) };
    }));
    [AllowAnonymous, Route("/Error")] public IActionResult Error() { Response.StatusCode = 500; return View("Error"); }
}
