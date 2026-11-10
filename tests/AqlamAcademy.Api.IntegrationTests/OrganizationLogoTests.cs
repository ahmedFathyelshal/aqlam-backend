using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.TestSchool;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrganizationLogoTests(ApiFactory factory)
{
    // Smallest valid PNG: signature, IHDR (1x1), IDAT, IEND.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent Upload(byte[] bytes, string name = "logo.png", string type = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task Owner_uploads_a_logo_that_anyone_can_load_and_can_remove_it()
    {
        var school = await TestSchool.CreateAsync(factory);
        var url = $"{school.Api}/settings/logo";

        var saved = await DataAsync(await school.Owner.PutAsync(url, Upload(Png)));
        var logoUrl = saved.GetProperty("logoUrl").GetString()!;
        Assert.StartsWith($"/api/organizations/{school.Slug}/logo?v=", logoUrl);

        // Served without signing in, as the real image type, cacheable.
        var anonymous = await factory.CreateBrowserClient().GetAsync(logoUrl);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal("image/png", anonymous.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await anonymous.Content.ReadAsByteArrayAsync());

        // Saving the other settings keeps the uploaded logo.
        var kept = await DataAsync(await school.Owner.PutAsJsonAsync($"{school.Api}/settings",
            new { name = "Al Rowad", timeZone = "Asia/Riyadh", calendar = "Both", weekStartDay = "Sunday", logoUrl }));
        Assert.Equal(logoUrl, kept.GetProperty("logoUrl").GetString());

        // A script renamed to .png, and an oversize file, are refused; teachers cannot upload.
        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PutAsync(url, Upload("<svg onload=alert(1)>"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PutAsync(url, Upload(Png.Concat(new byte[300 * 1024]).ToArray()))).StatusCode);
        var teacher = await school.AddMemberAsync("Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PutAsync(url, Upload(Png))).StatusCode);

        var removed = await DataAsync(await school.Owner.DeleteAsync(url));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("logoUrl").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateBrowserClient().GetAsync(logoUrl)).StatusCode);
    }
}
