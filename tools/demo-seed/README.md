# Demo data

Fills a development database with realistic data for manual testing of every feature, through the API
(so all business rules apply). Safe to run again: existing users, organizations, classes and courses are reused.

## Run

1. Start the API with a platform admin and a relaxed sign-in rate limit (the script signs in ~40 users):

   ```powershell
   $env:Platform__BootstrapAdmin__UserName = "demo.admin"
   $env:Platform__BootstrapAdmin__Password = "Demo@12345"
   $env:RateLimiting__AuthPermitsPerMinute = "1000"
   dotnet run --project src/Web/AqlamAcademy.Api
   ```

   In Visual Studio, put the same three values in the launch profile's environment variables (or user secrets).
   The admin only needs to be created once; it stays in the database.

2. Run the seed (Node 18+):

   ```powershell
   node tools/demo-seed/seed.mjs http://localhost:5057
   ```

## What you get

Every demo account uses the password **Demo@12345**.

| Who | Accounts | What to try |
|---|---|---|
| Platform admin | `demo.admin` | Create organizations |
| Saudi school `rowad-school` (Hijri + Gregorian, boys'/girls' campuses) | owner `rowad.owner`, coordinator `rowad.coord`, teachers `t.ahmed` `t.khalid` `t.fatimah` `t.noura`, assistant `a.salem` | Classes, rosters, subjects, attendance, announcements, members, settings, grading queue (one graded, one returned for revision, one waiting) |
| School students | boys `s.omar` `s.yousef` `s.faisal` `s.abdullah` `s.majed` `s.turki`, girls `s.sara` `s.lama` `s.reem` `s.hessa` `s.jana` `s.dana` | My courses with progress, quizzes (passed and failed), homework (some late), notification bell |
| Waiting for approval | `s.pending` | Approve in Members |
| Parents | `p.qahtani` (father of `s.omar` and `s.sara`), `p.mother.lama`, `p.hassan` (center), `p.new` (no child yet) | `/parent`: overview, absence alerts; `p.new` links a child with the family code printed at the end of the run |
| Egyptian center `tamayoz-center` | owner `tamayoz.owner`, teacher `t.mostafa`, students `s.mariam` `s.ali` `s.hana` `s.karim` | Group with a Sat/Mon schedule and attendance per meeting |
| Independent teacher | `t.huda` | Public free course "قدرات - القسم الكمي" (self-enrolled by `s.sara` and `s.mariam`) |

Each school class has three subjects: Mathematics and Science are published with videos, an article, a quiz and
homework; Arabic stays a draft (the teacher's to-do). Attendance covers the last two weeks (Sunday–Thursday), with a
few absences, lates and excused days, so parents already have alerts.

Names show as usernames because self-registration does not ask for a full name.

## Academic year and grades

The school gets the year 1448 هـ with two terms, weighted gradebooks in math and science (scores typed and linked to
quizzes and homework) and published report cards for the first term. To add only this to a database seeded earlier:

```
DEMO_ONLY=academics node tools/demo-seed/seed.mjs
```
