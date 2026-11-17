# Operations guide (pilot)

What to set up before real schools use Aqlam. Written 2026-11-18. Hosting itself (provider, region, paid tier) is still the owner's decision; nothing here depends on it.

## 1. Settings that must be set in production

| Setting (environment variable) | Why |
|---|---|
| `DatabaseSettings__ConnectionString` | The platform database. |
| `JwtSettings__Rsa__PrivateKeyXml`, `JwtSettings__Rsa__PublicKeyXml` | Signs sign-in tokens. **Back these up separately from the database**; losing them signs everyone out. |
| `Cell__BaseDomain` | The API's own host. Other hosts are read as a school's custom domain. |
| `Cors__AllowedOrigins__0` | The website's address. |
| `App__PublicBaseUrl` | Address used in emailed password-reset links, e.g. `https://app.example.com`. If missing, the link falls back to the first CORS origin, then to `http://localhost:4200`. |
| `ForwardedHeaders__Enabled` | `true` behind a hosting proxy, so rate limits and login records see the visitor's address. |

## 2. Email (password reset links, announcements)

Email is off until an SMTP host is set; until then messages are only written to the log, so the reset-by-email route does nothing visible. Any SMTP provider works (SES, SendGrid, Zoho, a school's own server).

```
Email__SmtpHost=smtp.example.com
Email__SmtpPort=587
Email__EnableSsl=true
Email__UserName=...
Email__Password=...            (secret)
Email__FromAddress=no-reply@your-domain
Email__FromName=أقلام
```

Use a sending domain you control and set up SPF and DKIM with the provider, or messages land in spam. To test: use "forgot password" with an account that has an email and check the inbox. Accounts without an email recover through their school (temporary password) or, for a child, through their linked parent. Text-message (SMS) recovery is not built; it would be added behind the same provider switch.

## 3. Health checks and uptime monitoring

| Address | Meaning |
|---|---|
| `GET /health` | The API process is up. Does not touch the database. Use this for the host's health check (already in `render.yaml`) and for an uptime monitor. |
| `GET /health/ready` | The API is up **and** the database answers. Use for a second, slower monitor. |

Both answer the single word `Healthy` (HTTP 200) or `Unhealthy` (503), need no sign-in and are not rate limited. Point any external uptime monitor (UptimeRobot, Better Stack, the host's own) at `/health` every minute and alert by email or phone.

Free hosting tiers sleep after inactivity, so a monitor also keeps the demo warm; real schools need a paid always-on plan.

## 4. Backups

Platform data (accounts, schools, memberships, audit log, acceptance records, payments, the activation-code secret and the data-protection keys) lives in the platform database. A school on a dedicated database tier has its own database too; back those up the same way.

**Rule: a backup that was never restored is not a backup.** Use the script, which takes the backup and proves it restores:

```powershell
$env:PGPASSWORD = '...'        # never on the command line or in Git
./tools/backup/backup-and-verify.ps1 -DbHost <host> -User <user> -Database <db> -OutDir <folder>
```

It writes a compressed dump, restores it into a temporary scratch database, compares the row count of every table with the original, deletes the scratch database and exits `0` only when everything matches. It needs the `CREATEDB` privilege (or run it from a machine that can create databases on another server). Tested on 2026-11-18 against the development database: 65 tables, 2410 rows identical after restore.

Recommended schedule for the pilot:
- A verified backup **daily**, kept 30 days, copied off the database host (another cloud account or storage).
- Also turn on the hosting provider's own point-in-time recovery if the plan offers it (Neon, RDS and similar do on paid tiers). That is a second layer, not a replacement.
- Take one manually before every release that includes a database migration.
- Keep the two JWT keys (section 1) in a password manager.

### Restoring after a disaster

1. Stop the API (so nothing writes during the restore).
2. Create an empty database and restore the newest verified dump:
   `pg_restore -h <host> -U <user> -d <new database> --no-owner --no-privileges <file.dump>`
3. Point `DatabaseSettings__ConnectionString` at it and start the API. Pending migrations apply on start.
4. Check `/health/ready`, sign in as the platform admin and open the audit log.
5. Write down what happened in the audit/incident notes. If personal data was exposed, the privacy policy promises notification as the law requires.

Practise this once on a spare database before the pilot starts.

## 5. Platform administration

The platform admin console (`/admin/console`) holds: user search, lock/unlock, temporary passwords for any account, suspending a school, personal-data requests (export/delete) and the audit log. A suspended school's members lose access within about two minutes and its courses leave the marketplace; data is kept.

Handling a **data request**: the user files it from the privacy page; an admin sees it under "Data requests", performs the export or deletion by hand (there is no automatic export yet), then marks it done or declined with a note. Every step is in the audit log.

## 6. Before going live, still open

- Legal review of the terms and privacy texts (drafts, marked as such on the pages).
- Saudi hosting and data-location decision, then update the "where data is stored" paragraph of the privacy policy.
- An SMTP provider and `App__PublicBaseUrl` (sections 1 and 2).
- A monitor on `/health` and a scheduled, off-site, verified backup (sections 3 and 4).
