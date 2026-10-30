using System.ComponentModel.DataAnnotations;

namespace MediQueue.Models;

/// <summary>Read-only copy of a token that is safe to hand to the views.</summary>
public record TokenView(
    int Id, string PublicId, string Code, int DepartmentId, string DepartmentName, string DepartmentIcon,
    string Room, string PatientName, string Phone, int Age, string Gender, string Reason,
    PriorityLevel Priority, TokenStatus Status, string Source,
    DateTime CreatedAt, DateTime? CalledAt, DateTime? StartedAt, DateTime? CompletedAt,
    int? ServedByStaffId, string? ServedByName, int RecallCount);

public record ServingItem(string Code, string Room, int RecallCount, TokenStatus Status);

public record DeptBoard(
    int Id, string Name, string Code, string Icon, string Room, bool AcceptingTokens,
    List<ServingItem> Serving, List<string> Next, int WaitingCount, int ServersOnDuty,
    double AvgMinutes, int? EstWaitNew, string CallStamp);

public record TokenStatusInfo(
    TokenView Token, int? Position, int? Ahead, int? EtaMinutes, bool NoDoctor, int ServersOnDuty,
    double AvgMinutes, List<ServingItem> NowServing, int WaitingCount);

public record QueueRow(TokenView Token, int? Position, int? EtaMinutes);

public record DoctorPanel(
    int StaffId, string FullName, string Room, bool OnDuty, int DepartmentId, string DepartmentName,
    string DepartmentCode, string Icon, TokenView? Current, List<QueueRow> Waiting,
    int CompletedToday, int SkippedToday, double AvgMinutes, int OtherDoctorsOnDuty, List<TokenView> RecentDone);

public record DeptStat(string Name, string Code, string Icon, int Total, int Waiting, int Completed, int Skipped, int Doctors, double AvgMinutes);
public record DayCount(string Label, int Total, int Completed);

public record AdminStats(
    int TotalToday, int WaitingNow, int InProgressNow, int CompletedToday, int SkippedToday, int CancelledToday,
    double? AvgWaitMinutes, double? AvgConsultMinutes, List<DeptStat> Depts, int[] HourlyToday,
    List<DayCount> Last7Days, int DoctorsOnDuty, int DoctorsTotal, bool UsingDemoPasswords);

public record StaffView(int Id, string Username, string FullName, string Role, int? DepartmentId, string Room);

public record StaffRow(int Id, string Username, string FullName, string Role, string DepartmentName, string Room,
    bool OnDuty, bool IsActive, bool IsDemoPassword);

public record IssueRequest(int DepartmentId, string Name, string Phone, int? Age, string? Gender, string? Reason,
    PriorityLevel Priority, string Source, bool IgnoreAccepting);

public record IssueResult(TokenView? Token, bool Duplicate, string? Error);

public record OpResult(bool Ok, string Message, TokenView? Token = null);

// ------------------------------------------------------------------ forms
public class TokenForm
{
    [Required(ErrorMessage = "Please choose a department")]
    public int DepartmentId { get; set; }

    [Required(ErrorMessage = "Please enter the patient's name"), StringLength(60, MinimumLength = 2, ErrorMessage = "Name must be 2 to 60 letters")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Please enter a mobile number"), RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a 10 digit mobile number")]
    public string Phone { get; set; } = "";

    [Range(0, 120, ErrorMessage = "Age must be between 0 and 120")]
    public int? Age { get; set; }

    public string? Gender { get; set; }

    [StringLength(120, ErrorMessage = "Please keep the reason short (120 letters)")]
    public string? Reason { get; set; }

    public PriorityLevel Priority { get; set; } = PriorityLevel.Normal;
}

public class TrackForm
{
    [Required(ErrorMessage = "Enter your token number, for example GM-012")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Enter the mobile number you used"), RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a 10 digit mobile number")]
    public string Phone { get; set; } = "";
}

public class LoginForm
{
    [Required(ErrorMessage = "Enter your username")] public string Username { get; set; } = "";
    [Required(ErrorMessage = "Enter your password")] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public class DepartmentForm
{
    public int Id { get; set; }
    [Required, StringLength(40, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, RegularExpression(@"^[A-Za-z]{2,3}$", ErrorMessage = "Code must be 2 or 3 letters, e.g. GM")] public string Code { get; set; } = "";
    [Required, StringLength(20)] public string Room { get; set; } = "";
    [Range(2, 60, ErrorMessage = "Average time must be between 2 and 60 minutes")] public int AvgMinutes { get; set; } = 8;
    [StringLength(4)] public string Icon { get; set; } = "🩺";
    public bool IsActive { get; set; } = true;
    public bool AcceptingTokens { get; set; } = true;
}

public class StaffForm
{
    [Required, StringLength(60, MinimumLength = 2)] public string FullName { get; set; } = "";
    [Required, RegularExpression(@"^[a-z0-9._-]{3,30}$", ErrorMessage = "Username: 3 to 30 small letters, numbers, dot, dash or underscore")] public string Username { get; set; } = "";
    [Required] public string Role { get; set; } = Roles.Doctor;
    public int? DepartmentId { get; set; }
    [StringLength(20)] public string? Room { get; set; }
    [Required, StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")] public string Password { get; set; } = "";
}

public class ReceptionModel
{
    public List<QueueRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public int? Dept { get; set; }
    public string Status { get; set; } = "active";
}
