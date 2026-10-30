using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MediQueue.Models;

namespace MediQueue.Services;

/// <summary>
/// All the queue rules live here: issuing tokens, who is next, waiting-time estimates,
/// calling / skipping / finishing patients, staff and department management.
/// Every public method takes one lock, so two people clicking at the same moment cannot break the queue.
/// </summary>
public partial class QueueService
{
    /// <summary>Senior / pregnant / differently-abled patients are treated as if they arrived this many minutes earlier.</summary>
    public const int PriorityBoostMinutes = 20;

    /// <summary>A called patient who does not show up within this time is marked "missed call" automatically.</summary>
    public const int CallTimeoutMinutes = 5;

    private readonly IDataStore _store;
    private readonly IClock _clock;
    private readonly IQueueNotifier _notifier;
    private readonly object _gate = new();

    public QueueService(IDataStore store, IClock clock, IQueueNotifier notifier)
    {
        _store = store;
        _clock = clock;
        _notifier = notifier;
        lock (_gate)
        {
            if (SeedData.EnsureSeeded(D)) _store.Save();
        }
    }

    private StoreData D => _store.Data;
    private DateTime Now => _clock.Now;

    // ------------------------------------------------------------ helpers
    private IEnumerable<TokenRecord> TodayTokens(int deptId) =>
        D.Tokens.Where(t => t.DepartmentId == deptId && t.CreatedAt.Date == Now.Date);

    private static DateTime SortKey(TokenRecord t) => t.Priority switch
    {
        PriorityLevel.Emergency => DateTime.MinValue.AddTicks(t.Id),
        PriorityLevel.Normal => t.CreatedAt,
        _ => t.CreatedAt.AddMinutes(-PriorityBoostMinutes),
    };

    private List<TokenRecord> WaitingOrdered(int deptId) =>
        TodayTokens(deptId).Where(t => t.Status == TokenStatus.Waiting).OrderBy(SortKey).ThenBy(t => t.Id).ToList();

    private List<TokenRecord> ActiveTokens(int deptId) =>
        TodayTokens(deptId).Where(t => t.Status is TokenStatus.Called or TokenStatus.InConsultation).ToList();

    private int Servers(int deptId) =>
        D.Staff.Count(s => s.IsActive && s.Role == Roles.Doctor && s.DepartmentId == deptId && s.OnDuty);

    /// <summary>Average consultation time: the last 10 finished patients, mixed with the department's default.</summary>
    private double AvgMinutes(Department d)
    {
        var durations = D.Tokens
            .Where(t => t.DepartmentId == d.Id && t.Status == TokenStatus.Completed && t.StartedAt != null && t.CompletedAt != null)
            .OrderByDescending(t => t.CompletedAt)
            .Take(10)
            .Select(t => Math.Clamp((t.CompletedAt!.Value - t.StartedAt!.Value).TotalMinutes, 1, 60))
            .ToList();
        if (durations.Count == 0) return d.AvgMinutes;
        return Math.Max(2, Math.Round((durations.Sum() + d.AvgMinutes * 2.0) / (durations.Count + 2), 1));
    }

    /// <summary>
    /// Minutes until the patient at <paramref name="position"/> (1 = next in line) gets a doctor.
    /// Each doctor on duty is a "server" that becomes free at some time; patients take the earliest free server.
    /// </summary>
    private int? Eta(int position, double avg, int servers, List<TokenRecord> active)
    {
        var n = Math.Max(servers, active.Count);
        if (n == 0) return null;                           // nobody is working in this department

        var free = new List<double>();
        foreach (var a in active)
        {
            free.Add(a.Status == TokenStatus.InConsultation && a.StartedAt != null
                ? Math.Max(1, avg - (Now - a.StartedAt.Value).TotalMinutes)
                : avg);
        }
        while (free.Count < n) free.Add(0);

        double start = 0;
        for (var k = 0; k < position; k++)
        {
            var i = 0;
            for (var j = 1; j < free.Count; j++) if (free[j] < free[i]) i = j;
            start = free[i];
            free[i] = start + avg;
        }
        return (int)Math.Ceiling(start);
    }

    private TokenView View(TokenRecord t)
    {
        var dept = D.Departments.FirstOrDefault(x => x.Id == t.DepartmentId);
        var staff = t.ServedByStaffId == null ? null : D.Staff.FirstOrDefault(s => s.Id == t.ServedByStaffId);
        return new TokenView(t.Id, t.PublicId, t.Code, t.DepartmentId, dept?.Name ?? "?", dept?.Icon ?? "🩺",
            t.Room, t.PatientName, t.Phone, t.Age, t.Gender, t.Reason, t.Priority, t.Status, t.Source,
            t.CreatedAt, t.CalledAt, t.StartedAt, t.CompletedAt, t.ServedByStaffId, staff?.FullName, t.RecallCount);
    }

    private void Persist(int? deptId)
    {
        _store.Save();
        _notifier.QueueChanged(deptId);
    }

    private static string Clean(string? s, int max)
    {
        var t = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static string NewPublicId()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    // ------------------------------------------------------- issue tokens
    public IssueResult IssueToken(IssueRequest r)
    {
        lock (_gate)
        {
            var dept = D.Departments.FirstOrDefault(d => d.Id == r.DepartmentId && d.IsActive);
            if (dept == null) return new IssueResult(null, false, "That department is not available.");
            if (!dept.AcceptingTokens && !r.IgnoreAccepting)
                return new IssueResult(null, false, $"{dept.Name} is not taking new tokens right now. Please contact the reception desk.");

            var name = Clean(r.Name, 60);
            var phone = new string((r.Phone ?? "").Where(char.IsDigit).ToArray());
            if (name.Length < 2) return new IssueResult(null, false, "Please enter the patient's name.");
            if (phone.Length != 10) return new IssueResult(null, false, "Please enter a 10 digit mobile number.");

            // the same person must not hold two tokens in the same department
            var existing = TodayTokens(dept.Id).FirstOrDefault(t =>
                t.Phone == phone && string.Equals(t.PatientName, name, StringComparison.OrdinalIgnoreCase) &&
                t.Status is TokenStatus.Waiting or TokenStatus.Called or TokenStatus.InConsultation);
            if (existing != null) return new IssueResult(View(existing), true, null);

            var seq = TodayTokens(dept.Id).Select(t => t.Sequence).DefaultIfEmpty(0).Max() + 1;
            var token = new TokenRecord
            {
                Id = D.NextTokenId++, PublicId = NewPublicId(), DepartmentId = dept.Id, Sequence = seq,
                Code = $"{dept.Code}-{seq:000}", PatientName = name, Phone = phone, Age = r.Age ?? 0,
                Gender = Clean(r.Gender, 12), Reason = Clean(r.Reason, 120), Priority = r.Priority,
                Status = TokenStatus.Waiting, Source = r.Source, CreatedAt = Now,
            };
            D.Tokens.Add(token);
            Persist(dept.Id);
            return new IssueResult(View(token), false, null);
        }
    }

    // ------------------------------------------------------ doctor actions
    private TokenRecord? Own(int tokenId, int staffId) =>
        D.Tokens.FirstOrDefault(t => t.Id == tokenId && t.ServedByStaffId == staffId);

    public OpResult CallNext(int staffId)
    {
        lock (_gate)
        {
            var s = D.Staff.FirstOrDefault(x => x.Id == staffId && x.IsActive && x.Role == Roles.Doctor);
            if (s?.DepartmentId == null) return new OpResult(false, "Your account is not linked to a department.");
            if (!s.OnDuty) return new OpResult(false, "You are on a break. Press \"Start duty\" first.");
            if (D.Tokens.Any(t => t.ServedByStaffId == staffId && t.Status is TokenStatus.Called or TokenStatus.InConsultation))
                return new OpResult(false, "Finish or skip your current patient first.");

            var next = WaitingOrdered(s.DepartmentId.Value).FirstOrDefault();
            if (next == null) return new OpResult(false, "No patients are waiting.");

            var dept = D.Departments.First(d => d.Id == s.DepartmentId.Value);
            next.Status = TokenStatus.Called;
            next.CalledAt = Now;
            next.ServedByStaffId = s.Id;
            next.Room = string.IsNullOrWhiteSpace(s.Room) ? dept.Room : s.Room;
            next.RecallCount = 0;
            Persist(next.DepartmentId);
            return new OpResult(true, $"Called {next.Code}.", View(next));
        }
    }

    public OpResult Start(int tokenId, int staffId)
    {
        lock (_gate)
        {
            var t = Own(tokenId, staffId);
            if (t == null || t.Status != TokenStatus.Called) return new OpResult(false, "This patient is not waiting at your room.");
            t.Status = TokenStatus.InConsultation;
            t.StartedAt = Now;
            Persist(t.DepartmentId);
            return new OpResult(true, $"{t.Code} is now with the doctor.", View(t));
        }
    }

    public OpResult Complete(int tokenId, int staffId)
    {
        lock (_gate)
        {
            var t = Own(tokenId, staffId);
            if (t == null || t.Status is not (TokenStatus.Called or TokenStatus.InConsultation))
                return new OpResult(false, "This patient is not with you right now.");
            t.StartedAt ??= t.CalledAt ?? Now;
            t.Status = TokenStatus.Completed;
            t.CompletedAt = Now;
            Persist(t.DepartmentId);
            return new OpResult(true, $"{t.Code} completed.", View(t));
        }
    }

    public OpResult Skip(int tokenId, int staffId)
    {
        lock (_gate)
        {
            var t = Own(tokenId, staffId);
            if (t == null || t.Status != TokenStatus.Called) return new OpResult(false, "Only a called patient can be skipped.");
            t.Status = TokenStatus.Skipped;
            Persist(t.DepartmentId);
            return new OpResult(true, $"{t.Code} marked as missed call.", View(t));
        }
    }

    public OpResult Recall(int tokenId, int staffId)
    {
        lock (_gate)
        {
            var t = Own(tokenId, staffId);
            if (t == null || t.Status != TokenStatus.Called) return new OpResult(false, "Only a called patient can be called again.");
            t.RecallCount++;
            t.CalledAt = Now;                    // gives the patient a fresh waiting time
            Persist(t.DepartmentId);
            return new OpResult(true, $"Calling {t.Code} again.", View(t));
        }
    }

    public OpResult ToggleDuty(int staffId)
    {
        lock (_gate)
        {
            var s = D.Staff.FirstOrDefault(x => x.Id == staffId && x.IsActive && x.Role == Roles.Doctor);
            if (s == null) return new OpResult(false, "Doctor account not found.");
            if (s.OnDuty && D.Tokens.Any(t => t.ServedByStaffId == staffId && t.Status is TokenStatus.Called or TokenStatus.InConsultation))
                return new OpResult(false, "Please finish your current patient before taking a break.");
            s.OnDuty = !s.OnDuty;
            Persist(s.DepartmentId);
            return new OpResult(true, s.OnDuty ? "You are on duty." : "You are on a break. New patients can still join the queue.");
        }
    }

    // ------------------------------------------------- patient / reception
    public OpResult CancelByPatient(string publicId)
    {
        lock (_gate)
        {
            var t = D.Tokens.FirstOrDefault(x => x.PublicId == publicId);
            if (t == null) return new OpResult(false, "Token not found.");
            if (t.Status != TokenStatus.Waiting) return new OpResult(false, "Only a waiting token can be cancelled.");
            t.Status = TokenStatus.Cancelled;
            Persist(t.DepartmentId);
            return new OpResult(true, "Your token was cancelled.", View(t));
        }
    }

    public OpResult CancelByStaff(int tokenId)
    {
        lock (_gate)
        {
            var t = D.Tokens.FirstOrDefault(x => x.Id == tokenId);
            if (t == null || t.Status is not (TokenStatus.Waiting or TokenStatus.Called or TokenStatus.InConsultation))
                return new OpResult(false, "This token cannot be cancelled.");
            t.Status = TokenStatus.Cancelled;
            Persist(t.DepartmentId);
            return new OpResult(true, $"{t.Code} cancelled.", View(t));
        }
    }

    /// <summary>A patient who missed the call goes back into the queue (at the back, as a new arrival).</summary>
    public OpResult Requeue(int tokenId)
    {
        lock (_gate)
        {
            var t = D.Tokens.FirstOrDefault(x => x.Id == tokenId);
            if (t == null || t.Status != TokenStatus.Skipped || t.CreatedAt.Date != Now.Date)
                return new OpResult(false, "Only today's missed tokens can be put back in the queue.");
            t.Status = TokenStatus.Waiting;
            t.CreatedAt = Now;
            t.CalledAt = null;
            t.ServedByStaffId = null;
            t.Room = "";
            t.RecallCount = 0;
            Persist(t.DepartmentId);
            return new OpResult(true, $"{t.Code} is back in the queue.", View(t));
        }
    }

    public OpResult SetPriority(int tokenId, PriorityLevel level)
    {
        lock (_gate)
        {
            var t = D.Tokens.FirstOrDefault(x => x.Id == tokenId);
            if (t == null || t.Status != TokenStatus.Waiting) return new OpResult(false, "Priority can only be changed while the patient is waiting.");
            t.Priority = level;
            Persist(t.DepartmentId);
            return new OpResult(true, $"{t.Code} priority is now {Ui.PriorityText(level)}.", View(t));
        }
    }

    // ----------------------------------------------------- housekeeping
    /// <summary>Marks no-shows as missed and closes tokens left over from earlier days. Returns how many changed.</summary>
    public int RunHousekeeping()
    {
        lock (_gate)
        {
            var changed = new HashSet<int>();
            var count = 0;
            foreach (var t in D.Tokens)
            {
                if (t.Status == TokenStatus.Called && t.CalledAt != null && (Now - t.CalledAt.Value).TotalMinutes >= CallTimeoutMinutes)
                {
                    t.Status = TokenStatus.Skipped;
                    changed.Add(t.DepartmentId);
                    count++;
                }
                else if (t.CreatedAt.Date < Now.Date && t.Status is TokenStatus.Waiting or TokenStatus.Called or TokenStatus.InConsultation)
                {
                    t.Status = TokenStatus.Cancelled;
                    changed.Add(t.DepartmentId);
                    count++;
                }
            }
            if (count > 0)
            {
                _store.Save();
                foreach (var id in changed) _notifier.QueueChanged(id);
            }
            return count;
        }
    }

    // ------------------------------------------------------------ queries
    public List<Department> GetDepartments(bool onlyActive = true)
    {
        lock (_gate)
        {
            return D.Departments.Where(d => !onlyActive || d.IsActive).OrderBy(d => d.Id)
                .Select(d => new Department
                {
                    Id = d.Id, Name = d.Name, Code = d.Code, Room = d.Room, AvgMinutes = d.AvgMinutes,
                    IsActive = d.IsActive, AcceptingTokens = d.AcceptingTokens, Icon = d.Icon,
                }).ToList();
        }
    }

    public TokenStatusInfo? GetStatus(string publicId)
    {
        lock (_gate)
        {
            var t = D.Tokens.FirstOrDefault(x => x.PublicId == publicId);
            if (t == null) return null;
            var dept = D.Departments.First(d => d.Id == t.DepartmentId);
            var waiting = WaitingOrdered(dept.Id);
            var active = ActiveTokens(dept.Id);
            var servers = Servers(dept.Id);
            var avg = AvgMinutes(dept);

            int? pos = null, ahead = null, eta = null;
            if (t.Status == TokenStatus.Waiting && t.CreatedAt.Date == Now.Date)
            {
                var idx = waiting.FindIndex(x => x.Id == t.Id);
                if (idx >= 0)
                {
                    pos = idx + 1;
                    ahead = idx;
                    eta = Eta(pos.Value, avg, servers, active);
                }
            }
            var serving = active.OrderBy(a => a.CalledAt).Select(a => new ServingItem(a.Code, a.Room, a.RecallCount, a.Status)).ToList();
            return new TokenStatusInfo(View(t), pos, ahead, eta, servers == 0 && active.Count == 0, servers, avg, serving, waiting.Count);
        }
    }

    /// <summary>Finds today's token from "GM-12" / "gm 012" and the mobile number.</summary>
    public string? FindPublicId(string code, string phone)
    {
        var m = Regex.Match((code ?? "").Trim(), @"^([A-Za-z]{2,3})[\s\-]*0*(\d+)$");
        if (!m.Success) return null;
        var letters = m.Groups[1].Value;
        var seq = int.Parse(m.Groups[2].Value);
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        lock (_gate)
        {
            var dept = D.Departments.FirstOrDefault(d => d.Code.Equals(letters, StringComparison.OrdinalIgnoreCase));
            if (dept == null) return null;
            return TodayTokens(dept.Id).Where(t => t.Sequence == seq && t.Phone == digits)
                .OrderByDescending(t => t.Id).Select(t => t.PublicId).FirstOrDefault();
        }
    }

    public List<DeptBoard> GetBoard(int? onlyDept = null)
    {
        lock (_gate)
        {
            var list = new List<DeptBoard>();
            foreach (var d in D.Departments.Where(x => x.IsActive && (onlyDept == null || x.Id == onlyDept)).OrderBy(x => x.Id))
            {
                var waiting = WaitingOrdered(d.Id);
                var active = ActiveTokens(d.Id);
                var servers = Servers(d.Id);
                var avg = AvgMinutes(d);
                var serving = active.OrderBy(a => a.CalledAt).Select(a => new ServingItem(a.Code, a.Room, a.RecallCount, a.Status)).ToList();
                var stamp = string.Join("|", serving.Where(s => s.Status == TokenStatus.Called).Select(s => $"{s.Code}:{s.RecallCount}"));
                list.Add(new DeptBoard(d.Id, d.Name, d.Code, d.Icon, d.Room, d.AcceptingTokens, serving,
                    waiting.Take(4).Select(w => w.Code).ToList(), waiting.Count, servers, avg,
                    Eta(waiting.Count + 1, avg, servers, active), stamp));
            }
            return list;
        }
    }

    public DoctorPanel? GetDoctorPanel(int staffId)
    {
        lock (_gate)
        {
            var s = D.Staff.FirstOrDefault(x => x.Id == staffId && x.IsActive && x.Role == Roles.Doctor);
            if (s?.DepartmentId == null) return null;
            var dept = D.Departments.First(d => d.Id == s.DepartmentId.Value);
            var waiting = WaitingOrdered(dept.Id);
            var active = ActiveTokens(dept.Id);
            var servers = Servers(dept.Id);
            var avg = AvgMinutes(dept);

            var current = D.Tokens.FirstOrDefault(t => t.ServedByStaffId == staffId && t.Status is TokenStatus.Called or TokenStatus.InConsultation);
            var rows = waiting.Select((t, i) => new QueueRow(View(t), i + 1, Eta(i + 1, avg, servers, active))).ToList();
            var today = TodayTokens(dept.Id).ToList();
            var recent = D.Tokens.Where(t => t.ServedByStaffId == staffId && t.Status == TokenStatus.Completed && t.CompletedAt!.Value.Date == Now.Date)
                .OrderByDescending(t => t.CompletedAt).Take(5).Select(View).ToList();

            return new DoctorPanel(s.Id, s.FullName, string.IsNullOrWhiteSpace(s.Room) ? dept.Room : s.Room, s.OnDuty,
                dept.Id, dept.Name, dept.Code, dept.Icon, current == null ? null : View(current), rows,
                today.Count(t => t.Status == TokenStatus.Completed && t.ServedByStaffId == staffId),
                today.Count(t => t.Status == TokenStatus.Skipped && t.ServedByStaffId == staffId),
                avg, Math.Max(0, servers - (s.OnDuty ? 1 : 0)), recent);
        }
    }

    public ReceptionModel GetReception(int? deptFilter, string? statusFilter)
    {
        lock (_gate)
        {
            var status = string.IsNullOrWhiteSpace(statusFilter) ? "active" : statusFilter!;
            var model = new ReceptionModel { Departments = GetDepartments(), Dept = deptFilter, Status = status };

            foreach (var d in D.Departments.Where(x => x.IsActive && (deptFilter == null || x.Id == deptFilter)).OrderBy(x => x.Id))
            {
                var waiting = WaitingOrdered(d.Id);
                var active = ActiveTokens(d.Id);
                var servers = Servers(d.Id);
                var avg = AvgMinutes(d);
                foreach (var t in TodayTokens(d.Id))
                {
                    var keep = status switch
                    {
                        "active" => t.Status is TokenStatus.Waiting or TokenStatus.Called or TokenStatus.InConsultation,
                        "waiting" => t.Status == TokenStatus.Waiting,
                        "done" => t.Status == TokenStatus.Completed,
                        "missed" => t.Status == TokenStatus.Skipped,
                        "cancelled" => t.Status == TokenStatus.Cancelled,
                        _ => true,
                    };
                    if (!keep) continue;
                    int? pos = null, eta = null;
                    if (t.Status == TokenStatus.Waiting)
                    {
                        var idx = waiting.FindIndex(x => x.Id == t.Id);
                        pos = idx + 1;
                        eta = Eta(pos.Value, avg, servers, active);
                    }
                    model.Rows.Add(new QueueRow(View(t), pos, eta));
                }
            }

            static int Rank(TokenStatus s) => s switch
            {
                TokenStatus.InConsultation => 0, TokenStatus.Called => 1, TokenStatus.Waiting => 2, _ => 3,
            };
            model.Rows = model.Rows
                .OrderBy(r => Rank(r.Token.Status))
                .ThenBy(r => r.Token.Status == TokenStatus.Waiting ? r.Position ?? 0 : 0)
                .ThenByDescending(r => r.Token.CreatedAt)
                .ToList();
            return model;
        }
    }
}
