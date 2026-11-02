using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MemberImportTests(ApiFactory factory)
{
    [Fact]
    public async Task Template_is_an_excel_file_with_the_expected_columns()
    {
        var (owner, org) = await CreateSchoolAsync();

        var response = await owner.GetAsync($"/api/orgs/{org}/members/import/template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var headers = workbook.Worksheet("Members").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(["FullName", "Role", "Phone", "Email"], headers);
    }

    [Fact]
    public async Task Import_creates_valid_rows_reports_invalid_ones_and_forces_password_change()
    {
        var (owner, org) = await CreateSchoolAsync();
        var file = BuildWorkbook(
            ["FullName", "Role", "Phone", "Email"],
            ["Sara Al-Qahtani", "Student", "0551234567", ""],
            ["Omar Hassan", "Teacher", "", "omar@school.test"],
            ["Bad Role Person", "Principal", "", ""],
            ["Sara Al-Qahtani", "Student", "0551234567", ""]); // duplicate of row 2 in the same file

        var response = await UploadAsync(owner, $"/api/orgs/{org}/members/import", file);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(2, report.GetProperty("created").GetInt32());
        Assert.Equal(2, report.GetProperty("failed").GetInt32());

        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal("Created", rows[0].GetProperty("status").GetString());
        Assert.Equal("Failed", rows[2].GetProperty("status").GetString());
        Assert.Contains("Principal", rows[2].GetProperty("errors")[0].GetString());
        Assert.Contains("duplicate", rows[3].GetProperty("errors")[0].GetString(), StringComparison.OrdinalIgnoreCase);

        // The imported student signs in with the temporary password and must change it.
        var userName = rows[0].GetProperty("userName").GetString()!;
        var temporaryPassword = rows[0].GetProperty("temporaryPassword").GetString()!;
        var student = factory.CreateBrowserClient();
        var login = await student.PostAsJsonAsync("/api/Auth/login", new { userName, password = temporaryPassword });
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("mustChangePassword").GetBoolean());

        var change = await student.PostAsJsonAsync("/api/Auth/change-password", new { currentPassword = temporaryPassword, newPassword = "MyNewPass123" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var relogin = await factory.CreateBrowserClient().PostAsJsonAsync("/api/Auth/login", new { userName, password = "MyNewPass123" });
        Assert.False((await relogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("mustChangePassword").GetBoolean());

        var members = (await owner.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/members")).GetProperty("data");
        var imported = members.EnumerateArray().Single(m => m.GetProperty("userName").GetString() == userName);
        Assert.Equal("BulkImport", imported.GetProperty("source").GetString());
        Assert.Equal("Student", imported.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Non_excel_file_is_rejected_with_400()
    {
        var (owner, org) = await CreateSchoolAsync();

        var response = await UploadAsync(owner, $"/api/orgs/{org}/members/import", "not a workbook"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(HttpClient Owner, string Slug)> CreateSchoolAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();

        var owner = factory.CreateBrowserClient();
        var ownerName = (await TestUsers.RegisterAndLoginAsync(owner, TestUsers.Teacher, "imp")).GetProperty("data").GetProperty("userName").GetString()!;
        var slug = $"school-{Guid.NewGuid():N}"[..20];

        (await admin.PostAsJsonAsync("/api/platform/organizations", new
        {
            name = "Jeddah Pilot School",
            slug,
            type = "School",
            countryCode = "SA",
            ownerUserName = ownerName
        })).EnsureSuccessStatusCode();

        return (owner, slug);
    }

    private static byte[] BuildWorkbook(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Members");
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 1, c + 1).Value = rows[r][c];

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var form = new MultipartFormDataContent { { file, "file", "members.xlsx" } };
        return client.PostAsync(url, form);
    }
}
