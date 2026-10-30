namespace MediQueue.Models;

public enum TokenStatus { Waiting, Called, InConsultation, Completed, Skipped, Cancelled }

/// <summary>Emergency always goes first. Senior / Pregnant / Disabled get a small head start.</summary>
public enum PriorityLevel { Normal, Senior, Pregnant, Disabled, Emergency }

public static class Roles
{
    public const string Admin = "Admin";
    public const string Reception = "Reception";
    public const string Doctor = "Doctor";
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Room { get; set; } = "";
    public int AvgMinutes { get; set; } = 8;
    public bool IsActive { get; set; } = true;
    public bool AcceptingTokens { get; set; } = true;
    public string Icon { get; set; } = "🩺";
}

public class StaffUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.Doctor;
    public int? DepartmentId { get; set; }
    public string Room { get; set; } = "";
    public bool OnDuty { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string PasswordHash { get; set; } = "";
    public bool IsDemoPassword { get; set; }
}

public class TokenRecord
{
    public int Id { get; set; }
    public string PublicId { get; set; } = "";
    public int DepartmentId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public string Reason { get; set; } = "";
    public PriorityLevel Priority { get; set; }
    public TokenStatus Status { get; set; }
    public string Source { get; set; } = "Online";
    public DateTime CreatedAt { get; set; }
    public DateTime? CalledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? ServedByStaffId { get; set; }
    public string Room { get; set; } = "";
    public int RecallCount { get; set; }
}

public class StoreData
{
    public List<Department> Departments { get; set; } = new();
    public List<StaffUser> Staff { get; set; } = new();
    public List<TokenRecord> Tokens { get; set; } = new();
    public int NextDepartmentId { get; set; } = 1;
    public int NextStaffId { get; set; } = 1;
    public int NextTokenId { get; set; } = 1;
}
