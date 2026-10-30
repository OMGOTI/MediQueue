// A tiny self-test for the queue rules. Run it with:   dotnet run --project tests/MediQueue.SelfTest
using MediQueue.Models;
using MediQueue.Services;

var failures = 0;
var checks = 0;
void Check(bool ok, string what)
{
    checks++;
    if (!ok) failures++;
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
}
void Section(string name) => Console.WriteLine("\n" + name);

var clock = new FakeClock(new DateTime(2026, 10, 26, 10, 0, 0));
var store = new JsonDataStore(null);
var q = new QueueService(store, clock, new NullNotifier());

int Dept(string code) => q.GetDepartments().First(d => d.Code == code).Id;
int Staff(string user) => store.Data.Staff.First(s => s.Username == user).Id;
int phoneSeq = 0;
TokenView Issue(string code, string name, PriorityLevel p = PriorityLevel.Normal, string? phone = null)
{
    var r = q.IssueToken(new IssueRequest(Dept(code), name, phone ?? $"98000{++phoneSeq:00000}", 30, "Male", "test", p, "Online", false));
    if (r.Token == null) throw new Exception("issue failed: " + r.Error);
    return r.Token;
}

Section("Seed data");
Check(q.GetDepartments().Count == 6, "6 departments are created");
Check(store.Data.Staff.Count == 9, "9 staff accounts are created");
Check(q.Authenticate("admin", "admin123") != null, "admin can log in with the demo password");
Check(q.Authenticate("admin", "wrong") == null, "wrong password is refused");
Check(q.Authenticate("nobody", "admin123") == null, "unknown user is refused");

Section("Tokens and order");
var a = Issue("GM", "Normal A");
clock.Now = clock.Now.AddMinutes(1);
var b = Issue("GM", "Normal B");
Check(a.Code == "GM-001" && b.Code == "GM-002", "tokens are numbered GM-001, GM-002");
clock.Now = clock.Now.AddMinutes(9);
var c = Issue("GM", "Senior C", PriorityLevel.Senior);
Check(q.GetStatus(c.PublicId)!.Position == 1, "a senior citizen who arrives 9 min later goes before A and B (20 min head start)");
var e = Issue("GM", "Emergency E", PriorityLevel.Emergency);
Check(q.GetStatus(e.PublicId)!.Position == 1, "an emergency always goes first");
Check(q.GetStatus(c.PublicId)!.Position == 2 && q.GetStatus(a.PublicId)!.Position == 3 && q.GetStatus(b.PublicId)!.Position == 4,
    "order is E, C, A, B");
var dup = q.IssueToken(new IssueRequest(Dept("GM"), "Normal A", a.Phone, 30, "Male", "", PriorityLevel.Normal, "Online", false));
Check(dup.Duplicate && dup.Token!.Id == a.Id, "same person + department gets the same token, not a second one");
var pd = Issue("PD", "Kid P");
Check(pd.Code == "PD-001", "every department counts its own numbers");
Check(q.IssueToken(new IssueRequest(Dept("GM"), "X", "12345", null, null, null, PriorityLevel.Normal, "Online", false)).Error != null,
    "a bad mobile number is refused");

Section("Waiting time estimate (2 GM doctors on duty, 8 min each)");
var st = q.GetStatus(b.PublicId)!;   // position 4
Check(st.ServersOnDuty == 2, "2 doctors on duty");
Check(q.GetStatus(e.PublicId)!.EtaMinutes == 0 && q.GetStatus(c.PublicId)!.EtaMinutes == 0, "positions 1 and 2 can be seen at once");
Check(q.GetStatus(a.PublicId)!.EtaMinutes == 8, "position 3 waits one consultation (8 min)");
Check(st.EtaMinutes == 8, "position 4 waits one consultation (8 min) because two doctors work in parallel");
Check(q.ToggleDuty(Staff("dr.verma")).Ok, "dr.verma goes on a break");
Check(q.GetStatus(b.PublicId)!.EtaMinutes == 24, "with one doctor position 4 waits 3 x 8 = 24 min");
Check(q.ToggleDuty(Staff("dr.verma")).Ok, "dr.verma is back");
Check(q.GetStatus(pd.PublicId)!.EtaMinutes == 0, "pediatrics has a doctor and no queue: any moment");

Section("Doctor flow");
var sharma = Staff("dr.sharma");
var called = q.CallNext(sharma);
Check(called.Ok && called.Token!.Code == e.Code && called.Token.Status == TokenStatus.Called, "dr.sharma calls the emergency patient first");
Check(!q.CallNext(sharma).Ok, "cannot call another patient before finishing the current one");
Check(!q.ToggleDuty(sharma).Ok, "cannot take a break during a patient");
var verma = Staff("dr.verma");
var called2 = q.CallNext(verma);
Check(called2.Ok && called2.Token!.Code == c.Code, "dr.verma calls the next patient (senior C)");
Check(!q.Start(called.Token!.Id, verma).Ok, "a doctor cannot start another doctor's patient");
Check(q.Start(called.Token!.Id, sharma).Ok, "dr.sharma starts the consultation");
var panel = q.GetDoctorPanel(sharma)!;
Check(panel.Current!.Status == TokenStatus.InConsultation && panel.Waiting.Count == 2, "doctor panel shows the patient and 2 still waiting");
clock.Now = clock.Now.AddMinutes(3);
Check(q.GetStatus(a.PublicId)!.EtaMinutes == 5, "an ongoing consultation (3 of 8 min done) frees the room in 5 min");
clock.Now = clock.Now.AddMinutes(6);
Check(q.Complete(called.Token.Id, sharma).Ok, "consultation finished after 9 min");
var avg = q.GetBoard().First(x => x.Code == "GM").AvgMinutes;
Check(avg > 8 && avg < 9, $"average time learns from real data ({avg} min)");

Section("Missed calls");
clock.Now = clock.Now.AddMinutes(1);
var again = q.CallNext(sharma);
Check(again.Ok && again.Token!.Code == a.Code, "dr.sharma calls A");
clock.Now = clock.Now.AddMinutes(2);
Check(q.Recall(again.Token!.Id, sharma).Ok, "recall works and restarts the 5 minute timer");
clock.Now = clock.Now.AddMinutes(4);
q.RunHousekeeping();
Check(q.GetStatus(a.PublicId)!.Token.Status == TokenStatus.Called, "4 min after the recall: still waiting for the patient");
Check(q.GetStatus(c.PublicId)!.Token.Status == TokenStatus.Skipped, "C was called by dr.verma but never came for 10+ min: marked missed");
clock.Now = clock.Now.AddMinutes(2);
Check(q.RunHousekeeping() >= 1, "after 5 minutes without arriving the call is marked missed automatically");
var missed = q.GetStatus(a.PublicId)!;
Check(missed.Token.Status == TokenStatus.Skipped, "A is now a missed call");
Check(q.Requeue(a.Id).Ok && q.GetStatus(a.PublicId)!.Position > 0, "reception puts A back into the queue");

Section("Cancel, priority and lookup");
Check(q.CancelByPatient(b.PublicId).Ok && q.GetStatus(b.PublicId)!.Token.Status == TokenStatus.Cancelled, "a patient can cancel a waiting token");
Check(!q.CancelByPatient(b.PublicId).Ok, "a cancelled token cannot be cancelled again");
Check(q.FindPublicId("gm-2", b.Phone) == b.PublicId && q.FindPublicId("GM 002", b.Phone) == b.PublicId, "token lookup accepts GM-2, GM 002");
Check(q.FindPublicId("GM-2", "9999999999") == null, "lookup needs the right mobile number");
var n1 = Issue("ENT".Substring(0, 0) + "EN", "Normal N");
var n2 = Issue("EN", "Normal M");
Check(q.SetPriority(n2.Id, PriorityLevel.Emergency).Ok && q.GetStatus(n2.PublicId)!.Position == 1, "reception can raise a waiting patient to emergency");
var rec = q.GetReception(null, "active");
Check(rec.Rows.Count > 0 && rec.Rows.All(r => r.Token.Status is TokenStatus.Waiting or TokenStatus.Called or TokenStatus.InConsultation), "reception list 'active' only shows active tokens");
Check(q.GetReception(Dept("EN"), "all").Rows.Count == 2, "reception list can be filtered by department");

Section("Staff and departments");
Check(q.CreateStaff(new StaffForm { FullName = "Dr. Test", Username = "dr.test", Role = Roles.Doctor, DepartmentId = Dept("DE"), Password = "secret123" }).Ok, "admin creates a doctor");
Check(!q.CreateStaff(new StaffForm { FullName = "Dr. Test", Username = "dr.test", Role = Roles.Doctor, DepartmentId = Dept("DE"), Password = "secret123" }).Ok, "duplicate username is refused");
Check(q.Authenticate("dr.test", "secret123") != null, "the new doctor can log in");
Check(q.ResetPassword(Staff("dr.test"), "newpass99").Ok && q.Authenticate("dr.test", "secret123") == null && q.Authenticate("dr.test", "newpass99") != null, "password reset works");
Check(!q.SetStaffActive(Staff("admin"), false, Staff("admin")).Ok, "an admin cannot switch off their own account");
Check(q.SaveDepartment(new DepartmentForm { Name = "Eye Care", Code = "ey", Room = "Room 9", AvgMinutes = 9, Icon = "👁️" }).Ok, "a new department can be added");
Check(!q.SaveDepartment(new DepartmentForm { Name = "Other", Code = "GM", Room = "Room 2", AvgMinutes = 9 }).Ok, "a department code cannot be used twice");
Check(q.GetDepartments().Any(d => d.Code == "EY"), "department codes are stored in capitals");

Section("Statistics");
var stats = q.GetAdminStats();
Check(stats.TotalToday == 7 || stats.TotalToday > 0, $"today's total counted ({stats.TotalToday})");
Check(stats.CompletedToday == 1, "1 completed today");
Check(stats.HourlyToday.Sum() == stats.TotalToday, "hourly arrivals add up to the total");
Check(stats.UsingDemoPasswords, "demo passwords are flagged");

Section("Next day");
clock.Now = new DateTime(2026, 10, 27, 9, 0, 0);
q.RunHousekeeping();
Check(q.GetStatus(a.PublicId)!.Token.Status == TokenStatus.Cancelled, "tokens left over from yesterday are closed");
Check(Issue("GM", "Morning M").Code == "GM-001", "numbering starts again at 001 every day");

Section("Demo data");
Check(q.AddDemoPatients(10) >= 8, "demo patients are added");
Check(q.AddDemoHistory() > 100, "demo history is added");
Check(q.GetAdminStats().Last7Days.Take(6).All(d => d.Total > 0), "the last 6 days now have data for the chart");

Console.WriteLine($"\n{checks - failures} of {checks} checks passed.");
return failures == 0 ? 0 : 1;

sealed class FakeClock : IClock
{
    public FakeClock(DateTime start) { Now = start; }
    public DateTime Now { get; set; }
}
