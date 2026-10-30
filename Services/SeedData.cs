using MediQueue.Models;

namespace MediQueue.Services;

/// <summary>Creates the starting departments and demo accounts the first time the website runs.</summary>
public static class SeedData
{
    public const string AdminPassword = "admin123";
    public const string ReceptionPassword = "reception123";
    public const string DoctorPassword = "doctor123";

    public static bool EnsureSeeded(StoreData d)
    {
        var changed = false;

        if (d.Departments.Count == 0)
        {
            void Dept(string name, string code, string room, int mins, string icon) =>
                d.Departments.Add(new Department { Id = d.NextDepartmentId++, Name = name, Code = code, Room = room, AvgMinutes = mins, Icon = icon });

            Dept("General Medicine", "GM", "Room 1", 8, "🩺");
            Dept("Pediatrics", "PD", "Room 3", 10, "🧒");
            Dept("Orthopedics", "OR", "Room 4", 10, "🦴");
            Dept("ENT", "EN", "Room 5", 7, "👂");
            Dept("Dermatology", "DE", "Room 6", 6, "🧴");
            Dept("Cardiology", "CD", "Room 7", 12, "❤️");
            changed = true;
        }

        if (d.Staff.Count == 0)
        {
            int Id(string code) => d.Departments.First(x => x.Code == code).Id;

            void Staff(string user, string name, string role, string password, int? dept = null, string room = "") =>
                d.Staff.Add(new StaffUser
                {
                    Id = d.NextStaffId++, Username = user, FullName = name, Role = role, DepartmentId = dept, Room = room,
                    PasswordHash = PasswordHelper.Hash(password), IsDemoPassword = true, OnDuty = role == Roles.Doctor,
                });

            Staff("admin", "Hospital Admin", Roles.Admin, AdminPassword);
            Staff("reception", "Front Desk", Roles.Reception, ReceptionPassword);
            Staff("dr.sharma", "Dr. Anil Sharma", Roles.Doctor, DoctorPassword, Id("GM"), "Room 1");
            Staff("dr.verma", "Dr. Neha Verma", Roles.Doctor, DoctorPassword, Id("GM"), "Room 2");
            Staff("dr.patel", "Dr. Kiran Patel", Roles.Doctor, DoctorPassword, Id("PD"), "Room 3");
            Staff("dr.mehta", "Dr. Rahul Mehta", Roles.Doctor, DoctorPassword, Id("OR"), "Room 4");
            Staff("dr.joshi", "Dr. Pooja Joshi", Roles.Doctor, DoctorPassword, Id("EN"), "Room 5");
            Staff("dr.shah", "Dr. Manan Shah", Roles.Doctor, DoctorPassword, Id("DE"), "Room 6");
            Staff("dr.desai", "Dr. Sunita Desai", Roles.Doctor, DoctorPassword, Id("CD"), "Room 7");
            changed = true;
        }

        return changed;
    }
}
