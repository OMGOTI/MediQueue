using System.Security.Claims;
using MediQueue.Models;
using MediQueue.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Controllers;

public static class ControllerHelpers
{
    public static int StaffId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>Puts the result of an operation into the green / red message bar.</summary>
    public static OpResult Flash(this Controller c, OpResult r)
    {
        c.TempData[r.Ok ? "Ok" : "Err"] = r.Message;
        return r;
    }
}

public class HomeController : Controller
{
    private readonly QueueService _q;
    public HomeController(QueueService q) { _q = q; }

    public IActionResult Index() => View(_q.GetBoard());

    public IActionResult Departments() => PartialView("_Departments", _q.GetBoard());

    [Route("Home/Error")]
    public IActionResult Error() => View();
}

public class TokenController : Controller
{
    private readonly QueueService _q;
    public TokenController(QueueService q) { _q = q; }

    [HttpGet]
    public IActionResult Create(int? dept)
    {
        ViewBag.Boards = _q.GetBoard();
        return View(new TokenForm { DepartmentId = dept ?? 0 });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Create(TokenForm f)
    {
        ViewBag.Boards = _q.GetBoard();
        if (f.Priority == PriorityLevel.Emergency) f.Priority = PriorityLevel.Normal;   // emergencies go to the reception desk
        if (!ModelState.IsValid) return View(f);

        var r = _q.IssueToken(new IssueRequest(f.DepartmentId, f.Name, f.Phone, f.Age, f.Gender, f.Reason, f.Priority, "Online", false));
        if (r.Token == null)
        {
            ModelState.AddModelError("", r.Error ?? "Could not create the token");
            return View(f);
        }
        if (r.Duplicate) TempData["Ok"] = "You already have an active token in this department. Here it is.";
        else TempData["Ok"] = "Your token is ready. Keep this page open or save the link.";
        return RedirectToRoute("status", new { publicId = r.Token.PublicId });
    }

    [HttpGet]
    public IActionResult Track() => View(new TrackForm());

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Track(TrackForm f)
    {
        if (!ModelState.IsValid) return View(f);
        var id = _q.FindPublicId(f.Code, f.Phone);
        if (id == null)
        {
            ModelState.AddModelError("", "No token found for today with this number and mobile. Please check and try again.");
            return View(f);
        }
        return RedirectToRoute("status", new { publicId = id });
    }

    public IActionResult Status(string publicId)
    {
        var info = _q.GetStatus(publicId);
        if (info == null) { Response.StatusCode = 404; return View("NotFound"); }
        return View(info);
    }

    public IActionResult StatusPartial(string id)
    {
        var info = _q.GetStatus(id);
        if (info == null) return NotFound();
        return PartialView("_Status", info);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cancel(string publicId)
    {
        this.Flash(_q.CancelByPatient(publicId));
        return RedirectToRoute("status", new { publicId });
    }
}

public class DisplayController : Controller
{
    private readonly QueueService _q;
    public DisplayController(QueueService q) { _q = q; }

    public IActionResult Index(int? dept)
    {
        ViewBag.Dept = dept;
        return View(_q.GetBoard(dept));
    }

    public IActionResult Partial(int? dept) => PartialView("_Board", _q.GetBoard(dept));
}

public class AccountController : Controller
{
    private readonly QueueService _q;
    public AccountController(QueueService q) { _q = q; }

    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Home));
        return View(new LoginForm { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm f)
    {
        if (!ModelState.IsValid) return View(f);
        var s = _q.Authenticate(f.Username.Trim().ToLowerInvariant(), f.Password);
        if (s == null)
        {
            ModelState.AddModelError("", "Wrong username or password");
            return View(f);
        }
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, s.Id.ToString()),
            new(ClaimTypes.Name, s.FullName),
            new(ClaimTypes.Role, s.Role),
        };
        var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));

        if (!string.IsNullOrEmpty(f.ReturnUrl) && Url.IsLocalUrl(f.ReturnUrl)) return LocalRedirect(f.ReturnUrl);
        return RedirectToAction(nameof(Home));
    }

    /// <summary>Sends every role to its own start page.</summary>
    [Authorize]
    public IActionResult Home()
    {
        if (User.IsInRole(Roles.Admin)) return RedirectToAction("Index", "Admin");
        if (User.IsInRole(Roles.Doctor)) return RedirectToAction("Index", "Doctor");
        return RedirectToAction("Index", "Reception");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();
}

[Authorize(Roles = Roles.Reception + "," + Roles.Admin)]
public class ReceptionController : Controller
{
    private readonly QueueService _q;
    public ReceptionController(QueueService q) { _q = q; }

    public IActionResult Index(int? dept, string? status) => View(_q.GetReception(dept, status));

    public IActionResult Partial(int? dept, string? status) => PartialView("_ReceptionTable", _q.GetReception(dept, status));

    [HttpGet]
    public IActionResult NewToken()
    {
        ViewBag.Departments = _q.GetDepartments();
        return View(new TokenForm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult NewToken(TokenForm f)
    {
        ViewBag.Departments = _q.GetDepartments();
        if (!ModelState.IsValid) return View(f);
        var r = _q.IssueToken(new IssueRequest(f.DepartmentId, f.Name, f.Phone, f.Age, f.Gender, f.Reason, f.Priority, "Reception", true));
        if (r.Token == null)
        {
            ModelState.AddModelError("", r.Error ?? "Could not create the token");
            return View(f);
        }
        TempData["Ok"] = r.Duplicate ? $"{r.Token.PatientName} already has token {r.Token.Code}." : $"Token {r.Token.Code} created for {r.Token.PatientName}.";
        return RedirectToRoute("status", new { publicId = r.Token.PublicId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cancel(int id, int? dept, string? status)
    {
        this.Flash(_q.CancelByStaff(id));
        return RedirectToAction(nameof(Index), new { dept, status });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Requeue(int id, int? dept, string? status)
    {
        this.Flash(_q.Requeue(id));
        return RedirectToAction(nameof(Index), new { dept, status });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Priority(int id, PriorityLevel level, int? dept, string? status)
    {
        this.Flash(_q.SetPriority(id, level));
        return RedirectToAction(nameof(Index), new { dept, status });
    }
}

[Authorize(Roles = Roles.Doctor)]
public class DoctorController : Controller
{
    private readonly QueueService _q;
    public DoctorController(QueueService q) { _q = q; }

    public IActionResult Index()
    {
        var p = _q.GetDoctorPanel(User.StaffId());
        if (p == null) return RedirectToAction("Login", "Account");
        return View(p);
    }

    public IActionResult Partial()
    {
        var p = _q.GetDoctorPanel(User.StaffId());
        if (p == null) return Unauthorized();
        return PartialView("_DoctorPanel", p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult CallNext() { this.Flash(_q.CallNext(User.StaffId())); return RedirectToAction(nameof(Index)); }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Start(int id) { this.Flash(_q.Start(id, User.StaffId())); return RedirectToAction(nameof(Index)); }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Complete(int id) { this.Flash(_q.Complete(id, User.StaffId())); return RedirectToAction(nameof(Index)); }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Skip(int id) { this.Flash(_q.Skip(id, User.StaffId())); return RedirectToAction(nameof(Index)); }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Recall(int id) { this.Flash(_q.Recall(id, User.StaffId())); return RedirectToAction(nameof(Index)); }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Duty() { this.Flash(_q.ToggleDuty(User.StaffId())); return RedirectToAction(nameof(Index)); }
}

[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly QueueService _q;
    public AdminController(QueueService q) { _q = q; }

    public IActionResult Index() => View(_q.GetAdminStats());

    public IActionResult Staff()
    {
        ViewBag.Departments = _q.GetDepartments(false);
        ViewBag.Form = new StaffForm();
        return View(_q.ListStaff());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult CreateStaff(StaffForm f)
    {
        if (!ModelState.IsValid)
        {
            TempData["Err"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Staff));
        }
        this.Flash(_q.CreateStaff(f));
        return RedirectToAction(nameof(Staff));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ResetPassword(int id, string newPassword)
    {
        this.Flash(_q.ResetPassword(id, newPassword ?? ""));
        return RedirectToAction(nameof(Staff));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SetActive(int id, bool active)
    {
        this.Flash(_q.SetStaffActive(id, active, User.StaffId()));
        return RedirectToAction(nameof(Staff));
    }

    public IActionResult Departments() => View(_q.GetDepartments(false));

    [HttpGet]
    public IActionResult Department(int? id)
    {
        var f = id.HasValue ? _q.GetDepartmentForm(id.Value) : new DepartmentForm();
        if (f == null) return NotFound();
        return View(f);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Department(DepartmentForm f)
    {
        if (!ModelState.IsValid) return View(f);
        var r = _q.SaveDepartment(f);
        if (!r.Ok) { ModelState.AddModelError("", r.Message); return View(f); }
        this.Flash(r);
        return RedirectToAction(nameof(Departments));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult DemoPatients()
    {
        var n = _q.AddDemoPatients(12);
        TempData["Ok"] = $"{n} demo patients added to today's queues.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult DemoHistory()
    {
        var n = _q.AddDemoHistory();
        TempData["Ok"] = $"{n} demo tokens added for the last 7 days (for the charts).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ClearToday()
    {
        var n = _q.ClearToday();
        TempData["Ok"] = $"{n} tokens of today were removed.";
        return RedirectToAction(nameof(Index));
    }
}
