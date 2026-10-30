using MediQueue.Models;

namespace MediQueue.Services;

// Staff accounts, departments, statistics and demo data.
public partial class QueueService
{
    private static readonly string DummyHash = PasswordHelper.Hash("not-a-real-password");

    // ------------------------------------------------------------- login
    public StaffView? Authenticate(string username, string password)
    {
        var name = (username ?? "").Trim().ToLowerInvariant();
        StaffUser? s;
        lock (_gate) s = D.Staff.FirstOrDefault(x => x.IsActive && x.Username == name);

        // the slow password check runs outside the lock; it also runs for unknown users so timing does not give them away
        var ok = PasswordHelper.Verify(password ?? "", s?.PasswordHash ?? DummyHash);
        if (s == null || !ok) return null;
        return new StaffView(s.Id, s.Username, s.FullName, s.Role, s.DepartmentId, s.Room);
    }

    public StaffView? GetStaff(int id)
    {
        lock (_gate)
        {
            var s = D.Staff.FirstOrDefault(x => x.Id == id && x.IsActive);
            return s == null ? null : new StaffView(s.Id, s.Username, s.FullName, s.Role, s.DepartmentId, s.Room);
        }
    }

    // ------------------------------------------------------------- staff
    public List<StaffRow> ListStaff()
    {
        lock (_gate)
        {
            return D.Staff.OrderBy(s => s.Role == Roles.Admin ? 0 : s.Role == Roles.Reception ? 1 : 2).ThenBy(s => s.FullName)
                .Select(s => new StaffRow(s.Id, s.Username, s.FullName, s.Role,
                    s.DepartmentId == null ? "–" : D.Departments.FirstOrDefault(d => d.Id == s.DepartmentId)?.Name ?? "–",
                    s.Room, s.OnDuty, s.IsActive, s.IsDemoPassword)).ToList();
        }
    }

    public OpResult CreateStaff(StaffForm f)
    {
        var hash = PasswordHelper.Hash(f.Password);
        lock (_gate)
        {
            var user = f.Username.Trim().ToLowerInvariant();
            if (D.Staff.Any(s => s.Username == user)) return new OpResult(false, "That username is already used.");
            if (f.Role is not (Roles.Admin or Roles.Reception or Roles.Doctor)) return new OpResult(false, "Please choose a role.");

            int? deptId = null;
            var room = Clean(f.Room, 20);
            if (f.Role == Roles.Doctor)
            {
                var dept = D.Departments.FirstOrDefault(d => d.Id == f.DepartmentId);
                if (dept == null) return new OpResult(false, "A doctor needs a department.");
                deptId = dept.Id;
                if (room.Length == 0) room = dept.Room;
            }
            D.Staff.Add(new StaffUser
            {
                Id = D.NextStaffId++, Username = user, FullName = Clean(f.FullName, 60), Role = f.Role,
                DepartmentId = deptId, Room = room, OnDuty = f.Role == Roles.Doctor, PasswordHash = hash,
            });
            Persist(deptId);
            return new OpResult(true, $"Account \"{user}\" created.");
        }
    }

    public OpResult ResetPassword(int staffId, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6) return new OpResult(false, "Password must be at least 6 characters.");
        var hash = PasswordHelper.Hash(newPassword);
        lock (_gate)
        {
            var s = D.Staff.FirstOrDefault(x => x.Id == staffId);
            if (s == null) return new OpResult(false, "Account not found.");
            s.PasswordHash = hash;
            s.IsDemoPassword = false;
            _store.Save();
            return new OpResult(true, $"Password changed for {s.Username}.");
        }
    }

    public OpResult SetStaffActive(int staffId, bool active, int actingStaffId)
    {
        lock (_gate)
        {
            var s = D.Staff.FirstOrDefault(x => x.Id == staffId);
            if (s == null) return new OpResult(false, "Account not found.");
            if (s.Id == actingStaffId && !active) return new OpResult(false, "You cannot switch off your own account.");
            if (!active && D.Tokens.Any(t => t.ServedByStaffId == s.Id && t.Status is TokenStatus.Called or TokenStatus.InConsultation))
                return new OpResult(false, "This doctor is with a patient right now.");
            if (!active && s.Role == Roles.Admin && D.Staff.Count(x => x.Role == Roles.Admin && x.IsActive) <= 1)
                return new OpResult(false, "At least one admin account must stay active.");
            s.IsActive = active;
            Persist(s.DepartmentId);
            return new OpResult(true, active ? $"{s.Username} is active again." : $"{s.Username} was switched off.");
        }
    }

    // -------------------------------------------------------- departments
    public DepartmentForm? GetDepartmentForm(int id)
    {
        lock (_gate)
        {
            var d = D.Departments.FirstOrDefault(x => x.Id == id);
            return d == null ? null : new DepartmentForm
            {
                Id = d.Id, Name = d.Name, Code = d.Code, Room = d.Room, AvgMinutes = d.AvgMinutes,
                Icon = d.Icon, IsActive = d.IsActive, AcceptingTokens = d.AcceptingTokens,
            };
        }
    }

    public OpResult SaveDepartment(DepartmentForm f)
    {
        lock (_gate)
        {
            var code = f.Code.Trim().ToUpperInvariant();
            if (D.Departments.Any(d => d.Id != f.Id && d.Code == code)) return new OpResult(false, "That code is used by another department.");

            var d = f.Id == 0 ? null : D.Departments.FirstOrDefault(x => x.Id == f.Id);
            if (f.Id != 0 && d == null) return new OpResult(false, "Department not found.");
            if (d == null)
            {
                d = new Department { Id = D.NextDepartmentId++ };
                D.Departments.Add(d);
            }
            d.Name = Clean(f.Name, 40);
            d.Code = code;
            d.Room = Clean(f.Room, 20);
            d.AvgMinutes = f.AvgMinutes;
            d.Icon = string.IsNullOrWhiteSpace(f.Icon) ? "🩺" : f.Icon.Trim();
            d.IsActive = f.IsActive;
            d.AcceptingTokens = f.AcceptingTokens;
            Persist(d.Id);
            return new OpResult(true, $"{d.Name} saved.");
        }
    }

    // --------------------------------------------------------- statistics
    public AdminStats GetAdminStats()
    {
        lock (_gate)
        {
            var today = D.Tokens.Where(t => t.CreatedAt.Date == Now.Date).ToList();
            var waits = today.Where(t => t.CalledAt != null).Select(t => (t.CalledAt!.Value - t.CreatedAt).TotalMinutes).ToList();
            var consults = today.Where(t => t.Status == TokenStatus.Completed && t.StartedAt != null && t.CompletedAt != null)
                .Select(t => (t.CompletedAt!.Value - t.StartedAt!.Value).TotalMinutes).ToList();

            var depts = D.Departments.Where(d => d.IsActive).OrderBy(d => d.Id).Select(d =>
            {
                var list = today.Where(t => t.DepartmentId == d.Id).ToList();
                return new DeptStat(d.Name, d.Code, d.Icon, list.Count,
                    list.Count(t => t.Status == TokenStatus.Waiting),
                    list.Count(t => t.Status == TokenStatus.Completed),
                    list.Count(t => t.Status == TokenStatus.Skipped),
                    Servers(d.Id), AvgMinutes(d));
            }).ToList();

            var hourly = new int[24];
            foreach (var t in today) hourly[t.CreatedAt.Hour]++;

            var days = new List<DayCount>();
            for (var i = 6; i >= 0; i--)
            {
                var day = Now.Date.AddDays(-i);
                var list = D.Tokens.Where(t => t.CreatedAt.Date == day).ToList();
                days.Add(new DayCount(day.ToString("dd MMM", System.Globalization.CultureInfo.InvariantCulture), list.Count,
                    list.Count(t => t.Status == TokenStatus.Completed)));
            }

            var doctors = D.Staff.Where(s => s.IsActive && s.Role == Roles.Doctor).ToList();
            return new AdminStats(
                today.Count,
                today.Count(t => t.Status == TokenStatus.Waiting),
                today.Count(t => t.Status is TokenStatus.Called or TokenStatus.InConsultation),
                today.Count(t => t.Status == TokenStatus.Completed),
                today.Count(t => t.Status == TokenStatus.Skipped),
                today.Count(t => t.Status == TokenStatus.Cancelled),
                waits.Count == 0 ? null : Math.Round(waits.Average(), 1),
                consults.Count == 0 ? null : Math.Round(consults.Average(), 1),
                depts, hourly, days, doctors.Count(s => s.OnDuty), doctors.Count,
                D.Staff.Any(s => s.IsActive && s.IsDemoPassword));
        }
    }

    // ---------------------------------------------------------- demo data
    private static readonly string[] DemoNames =
    {
        "Aarav Shah", "Diya Patel", "Rohan Mehta", "Isha Desai", "Kabir Joshi", "Ananya Trivedi", "Vivaan Parmar",
        "Meera Solanki", "Harsh Vyas", "Priya Chauhan", "Yash Thakkar", "Nisha Rathod", "Dev Bhatt", "Kavya Pandya",
        "Jay Modi", "Riya Gandhi", "Om Raval", "Sneha Dave", "Parth Kapadia", "Tanvi Amin", "Raj Suthar", "Hetal Vora",
    };

    private static readonly string[] DemoReasons =
    {
        "Fever and cold", "Back pain", "Skin allergy", "Chest discomfort", "Routine check-up", "Ear pain",
        "Child vaccination", "Knee pain", "Sore throat", "Headache", "Follow-up visit", "Rash on arms",
    };

    /// <summary>Adds waiting patients for today so the screens have something to show.</summary>
    public int AddDemoPatients(int count)
    {
        var rnd = new Random();
        var added = 0;
        List<Department> depts;
        lock (_gate) depts = D.Departments.Where(d => d.IsActive && d.AcceptingTokens).ToList();
        if (depts.Count == 0) return 0;

        for (var i = 0; i < count; i++)
        {
            var dept = depts[rnd.Next(depts.Count)];
            var roll = rnd.Next(100);
            var priority = roll < 80 ? PriorityLevel.Normal : roll < 88 ? PriorityLevel.Senior : roll < 93 ? PriorityLevel.Pregnant
                : roll < 97 ? PriorityLevel.Disabled : PriorityLevel.Emergency;
            var phone = "9" + string.Concat(Enumerable.Range(0, 9).Select(_ => rnd.Next(10)));
            var r = IssueToken(new IssueRequest(dept.Id, DemoNames[rnd.Next(DemoNames.Length)], phone, rnd.Next(2, 78),
                rnd.Next(2) == 0 ? "Male" : "Female", DemoReasons[rnd.Next(DemoReasons.Length)], priority, "Demo", true));
            if (r.Token != null && !r.Duplicate) added++;
        }
        return added;
    }

    /// <summary>Adds finished visits for the last 7 days so the admin charts and averages look real.</summary>
    public int AddDemoHistory()
    {
        var rnd = new Random();
        lock (_gate)
        {
            var added = 0;
            for (var back = 1; back <= 7; back++)
            {
                var day = Now.Date.AddDays(-back);
                foreach (var d in D.Departments.Where(x => x.IsActive))
                {
                    var n = rnd.Next(8, 26);
                    var minute = 9 * 60 + rnd.Next(0, 20);
                    var seq = D.Tokens.Where(t => t.DepartmentId == d.Id && t.CreatedAt.Date == day).Select(t => t.Sequence).DefaultIfEmpty(0).Max();
                    var doc = D.Staff.FirstOrDefault(s => s.Role == Roles.Doctor && s.DepartmentId == d.Id);
                    for (var k = 0; k < n && minute < 17 * 60; k++)
                    {
                        minute += rnd.Next(3, 18);
                        var created = day.AddMinutes(minute);
                        var wait = rnd.Next(4, 40);
                        var consult = Math.Max(2, d.AvgMinutes + rnd.Next(-3, 5));
                        var roll = rnd.Next(100);
                        var status = roll < 90 ? TokenStatus.Completed : roll < 96 ? TokenStatus.Skipped : TokenStatus.Cancelled;
                        seq++;
                        var t = new TokenRecord
                        {
                            Id = D.NextTokenId++, PublicId = NewPublicId(), DepartmentId = d.Id, Sequence = seq,
                            Code = $"{d.Code}-{seq:000}", PatientName = DemoNames[rnd.Next(DemoNames.Length)],
                            Phone = "9" + string.Concat(Enumerable.Range(0, 9).Select(_ => rnd.Next(10))),
                            Age = rnd.Next(2, 80), Gender = rnd.Next(2) == 0 ? "Male" : "Female",
                            Reason = DemoReasons[rnd.Next(DemoReasons.Length)], Priority = PriorityLevel.Normal,
                            Status = status, Source = "Demo", CreatedAt = created,
                        };
                        if (status != TokenStatus.Cancelled)
                        {
                            t.CalledAt = created.AddMinutes(wait);
                            t.ServedByStaffId = doc?.Id;
                            t.Room = doc?.Room ?? d.Room;
                        }
                        if (status == TokenStatus.Completed)
                        {
                            t.StartedAt = t.CalledAt!.Value.AddMinutes(1);
                            t.CompletedAt = t.StartedAt.Value.AddMinutes(consult);
                        }
                        D.Tokens.Add(t);
                        added++;
                    }
                }
            }
            Persist(null);
            return added;
        }
    }

    /// <summary>Removes all of today's tokens (for a fresh start).</summary>
    public int ClearToday()
    {
        lock (_gate)
        {
            var n = D.Tokens.RemoveAll(t => t.CreatedAt.Date == Now.Date);
            Persist(null);
            return n;
        }
    }
}
