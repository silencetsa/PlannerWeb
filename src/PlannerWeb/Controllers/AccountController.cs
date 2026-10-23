using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Planer.Data;
using PlannerWeb.Models;
using PlannerWeb.Services;

namespace PlannerWeb.Controllers;

public sealed class AccountController(AccountService accounts) : Controller
{
    [HttpGet] public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Planner") : View(new RegisterModel());
    [HttpPost, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try { await SignIn(await accounts.Register(model.Nickname, model.Email, model.Password)); return RedirectToAction("Index", "Planner"); }
        catch (ArgumentException e) { ModelState.AddModelError("", e.Message); return View(model); }
    }
    [HttpGet] public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Planner") : View(new LoginModel { ReturnUrl = returnUrl });
    [HttpPost, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await accounts.Login(model.Login, model.Password);
        if (user is null) { ModelState.AddModelError("", "Неверная почта, никнейм или пароль. После нескольких попыток попробуйте через 15 минут."); return View(model); }
        await SignIn(user);
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Planner");
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return RedirectToAction(nameof(Login)); }
    private Task SignIn(WebUser u) => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, u.Id), new(ClaimTypes.Name, u.Nickname)], CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
}
