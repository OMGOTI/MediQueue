# MediQueue – Smart Hospital Queue System

A hospital queue website built with **ASP.NET Core 8 (MVC + SignalR)**. Patients take a token on their phone, see their live position and waiting time, and get called by number on a display board. Reception and doctors manage the queue, the admin sees statistics and manages staff and departments.

No database server and no NuGet packages are needed. Data is saved in a small file: `App_Data/mediqueue.json`.

## 1. Install (one time)

Install the **.NET 8 SDK** (free): https://dotnet.microsoft.com/download/dotnet/8.0
(Newer SDKs such as .NET 9 also work.) Then close and reopen VS Code and check in the terminal:

```
dotnet --version
```

It should print a number like `8.0.xxx`.

## 2. Run

Open this folder in VS Code (File → Open Folder → `MediQueue`), then **Terminal → New Terminal** and type:

```
dotnet run
```

Wait for `Now listening on: http://localhost:5080` and open **http://localhost:5080** in your browser.
(To use the VS Code browser: press `Ctrl+Shift+P` → type `Simple Browser: Show` → paste the address.)

Stop the server with `Ctrl + C` in the terminal.

## 3. Pages

| Page | Address | Who |
|---|---|---|
| Home (live departments) | `/` | everyone |
| Get a token | `/Token/Create` | patients |
| Track my token (number + mobile) | `/Token/Track` | patients |
| My token page (live, with QR) | `/t/<code>` | patients |
| Display board (TV, voice calls) | `/Display` | waiting hall |
| Staff login | `/Account/Login` | staff |
| Reception desk | `/Reception` | reception, admin |
| Doctor queue | `/Doctor` | doctors |
| Admin dashboard, staff, departments | `/Admin` | admin |

## 4. Demo logins

| Role | Username | Password |
|---|---|---|
| Admin | `admin` | `admin123` |
| Reception | `reception` | `reception123` |
| Doctors | `dr.sharma`, `dr.verma`, `dr.patel`, `dr.mehta`, `dr.joshi`, `dr.shah`, `dr.desai` | `doctor123` |

**Change these passwords** in Admin → Staff before real use. Admin → dashboard also has demo buttons (add demo patients, add 7 days of history for the charts).

## 5. How the queue works

* Every department numbers its tokens each day: `GM-001`, `GM-002` … (starts again at 001 every morning).
* **Priority**: Emergency is always first. Senior citizens, pregnant women and disabled patients are treated as if they had arrived 20 minutes earlier. Everyone else is first come, first served.
* **Waiting time** is calculated from the number of doctors on duty in that department and the real average consultation time of the day (it learns from finished consultations).
* A called patient who does not come within **5 minutes** is marked *missed*; reception can put them back in the queue.
* The same person (name + mobile) cannot take two active tokens in one department.
* Pages update live (SignalR) and also refresh every 15 seconds as a backup.

## 6. Use on phones in the same Wi-Fi (optional)

```
dotnet run --urls http://0.0.0.0:5080
```

(or double-click `run-lan.bat`). Windows may ask to allow the firewall: allow **Private networks**. Find your PC's IP with `ipconfig` (for example `192.168.1.20`) and open `http://192.168.1.20:5080` on the phone. Open the site on the PC with the same IP address too, so the QR codes on the token pages point to the right address.

## 7. Checking the logic (optional)

```
dotnet run --project tests/MediQueue.SelfTest
```

Runs 61 checks of the queue rules (priority, waiting time, missed calls, staff, statistics).

## 8. Good to know

* Data lives in `App_Data/mediqueue.json`. Delete that file to start fresh (demo data is created again).
* SMS / WhatsApp messages are **not** sent; patients use the live token page.
* Storage in a JSON file is fine for a demo or a small clinic. For a large hospital, replace it with a SQL database.
* Port busy? Change `applicationUrl` in `Properties/launchSettings.json`.
